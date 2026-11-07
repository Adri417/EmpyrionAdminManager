using Eam.Core.Security;
using Eam.Engine.Servers;

namespace Eam.Engine.Api;

public sealed record ModToggleRequest(string Name, bool Enabled);
public sealed record ModConfigRequest(string Name, string File, string? Text);
public sealed record EventActionRequest(string Action);
public sealed record FreshStartRequest(bool KeepChat, bool DeleteSave, string Confirm);
public sealed record FileRequest(string File);

/// <summary>Step 9: mods, autominer, events and maintenance.</summary>
public static class ExtraEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/servers/{id:long}");
        static string By(HttpContext c) => c.User.Identity?.Name ?? "?";

        g.MapGet("/mods", (long id, ModService m) => Api.Run(() => m.List(id))).RequireAuthorization(Roles.Admin);
        g.MapPost("/mods", async (long id, HttpRequest req, ModService m, HttpContext c) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest(new { error = "invalid_file" });
            var file = (await req.ReadFormAsync(new Microsoft.AspNetCore.Http.Features.FormOptions { MultipartBodyLengthLimit = 210L * 1024 * 1024 }, c.RequestAborted)).Files.FirstOrDefault();
            if (file is null) return Results.BadRequest(new { error = "invalid_file" });
            return await Api.RunAsync(async () => new { name = await m.InstallAsync(id, file, By(c)) });
        }).RequireAuthorization(Roles.Owner).DisableAntiforgery().WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(210 * 1024 * 1024));
        g.MapPost("/mods/toggle", (long id, ModToggleRequest req, ModService m, HttpContext c) => Api.Run(() => { m.SetEnabled(id, req.Name, req.Enabled, By(c)); return Api.Ok; }))
            .RequireAuthorization(Roles.Admin);
        g.MapPost("/mods/remove", (long id, NameRequest req, ModService m, HttpContext c) => Api.Run(() => { m.Remove(id, req.Name, By(c)); return Api.Ok; }))
            .RequireAuthorization(Roles.Owner);
        g.MapGet("/mods/config", (long id, string name, string file, ModService m) => Api.Run(() => m.ReadConfig(id, name, file))).RequireAuthorization(Roles.Admin);
        g.MapPut("/mods/config", (long id, ModConfigRequest req, ModService m, HttpContext c) => Api.Run(() => { m.SaveConfig(id, req.Name, req.File, req.Text ?? "", By(c)); return Api.Ok; }))
            .RequireAuthorization(Roles.Admin);

        g.MapGet("/autominer", (long id, GameplayService s) => Api.Run(() => s.Miners(id))).RequireAuthorization(Roles.GameMaster);
        g.MapGet("/autominer/default-ores", (long id, GameplayService s) => Api.Run(() => s.DefaultOres(id))).RequireAuthorization(Roles.Admin);
        g.MapGet("/events", (long id, GameplayService s) => Api.Run(() => s.Events(id))).RequireAuthorization(Roles.GameMaster);
        g.MapPost("/events", (long id, GameEventRequest req, GameplayService s) => Api.Run(() => new { id = s.SaveEvent(id, 0, req) })).RequireAuthorization(Roles.GameMaster);
        g.MapPut("/events/{eid:long}", (long id, long eid, GameEventRequest req, GameplayService s) => Api.Run(() => new { id = s.SaveEvent(id, eid, req) }))
            .RequireAuthorization(Roles.GameMaster);
        g.MapPost("/events/{eid:long}/action", (long id, long eid, EventActionRequest req, GameplayService s, HttpContext c) =>
            Api.RunAsync(async () => { await s.EventActionAsync(id, eid, req.Action, By(c)); return Api.Ok; })).RequireAuthorization(Roles.GameMaster);

        g.MapGet("/maintenance", (long id, MaintenanceService m) => Api.Run(() => m.Info(id))).RequireAuthorization(Roles.Admin);
        g.MapPost("/maintenance/compact", (long id, MaintenanceService m) => Api.Run(() => m.Compact(id))).RequireAuthorization(Roles.Admin);
        g.MapPost("/maintenance/fresh-start", (long id, FreshStartRequest req, MaintenanceService m, ServerManager sm, HttpContext c) => Api.RunAsync(async () =>
        {
            if (!string.Equals(req.Confirm?.Trim(), sm.Definition(id).Name, StringComparison.Ordinal)) throw new ServerException("confirm_name_mismatch");
            await m.FreshStartAsync(id, req.KeepChat, req.DeleteSave, By(c));
            return Api.Ok;
        })).RequireAuthorization(Roles.Owner);
        g.MapGet("/maintenance/damaged-players", (long id, MaintenanceService m) => Api.Run(() => m.DamagedPlayers(id))).RequireAuthorization(Roles.Admin);
        g.MapPost("/maintenance/damaged-players/move", (long id, FileRequest req, MaintenanceService m, HttpContext c) => Api.Run(() => m.MoveDamagedPlayer(id, req.File, By(c))))
            .RequireAuthorization(Roles.Admin);
        g.MapGet("/maintenance/items", (long id, string? q, string? lang, MaintenanceService m) => Api.Run(() => m.Items(id, q ?? "", lang ?? "en"))).RequireAuthorization(Roles.Moderator);
    }
}
