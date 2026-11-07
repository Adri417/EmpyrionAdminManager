using Eam.Core.Security;
using Eam.Core.Servers;
using Eam.Engine.Servers;

namespace Eam.Engine.Api;

public sealed record PlayfieldActionRequest(string Playfield, string Action);
public sealed record NameRequest(string Name);

/// <summary>Step 6 (structures, playfields, maps) and the settings of steps 6 to 9.</summary>
public static class WorldEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/servers/{id:long}");
        static string By(HttpContext c) => c.User.Identity?.Name ?? "?";

        g.MapGet("/structures", (long id, WorldService w) => Api.Run(() => w.List(id))).RequireAuthorization(Roles.Moderator);
        g.MapPost("/structures/refresh", (long id, WorldService w) => Api.RunAsync(async () => new { count = await w.RefreshAsync(id) }))
            .RequireAuthorization(Roles.Moderator);
        g.MapGet("/structures/{sid:int}", (long id, int sid, WorldService w) => Api.RunAsync(() => w.DetailAsync(id, sid))).RequireAuthorization(Roles.Moderator);
        g.MapPost("/structures/{sid:int}/action", (long id, int sid, StructureActionRequest req, WorldService w, HttpContext c) =>
            Api.RunAsync(async () => await w.ActAsync(id, sid, req, By(c)) ?? Api.Ok)).RequireAuthorization(Roles.GameMaster);
        g.MapGet("/structures/violations", (long id, WorldService w) => Api.Run(() => w.Violations(id))).RequireAuthorization(Roles.Moderator);
        g.MapGet("/library", (long id, WorldService w) => Api.Run(() => w.Library(id))).RequireAuthorization(Roles.GameMaster);
        g.MapPost("/library/spawn", (long id, SpawnRequest req, WorldService w, HttpContext c) =>
            Api.RunAsync(async () => new { id = await w.SpawnAsync(id, req, By(c)) })).RequireAuthorization(Roles.GameMaster);
        g.MapPost("/library/delete", (long id, NameRequest req, WorldService w) => Api.Run(() => { w.DeleteLibrary(id, req.Name); return Api.Ok; }))
            .RequireAuthorization(Roles.Admin);
        g.MapPost("/blueprints", async (long id, HttpRequest req, WorldService w, HttpContext c) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest(new { error = "invalid_file" });
            var form = await req.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null) return Results.BadRequest(new { error = "invalid_file" });
            return await Api.RunAsync(async () => { await w.SaveBlueprintAsync(id, file, By(c)); return Api.Ok; });
        }).RequireAuthorization(Roles.Admin).DisableAntiforgery();
        g.MapPost("/blueprints/delete", (long id, NameRequest req, WorldService w) => Api.Run(() => { w.DeleteBlueprint(id, req.Name); return Api.Ok; }))
            .RequireAuthorization(Roles.Admin);

        g.MapGet("/playfields", (long id, WorldService w) => Api.Run(() => w.Playfields(id))).RequireAuthorization(Roles.Moderator);
        g.MapPost("/playfields/action", (long id, PlayfieldActionRequest req, WorldService w, HttpContext c) =>
            Api.RunAsync(async () => { await w.PlayfieldActionAsync(id, req.Playfield, req.Action, By(c)); return Api.Ok; })).RequireAuthorization(Roles.Admin);
        g.MapGet("/playfields/map", (long id, string pf, WorldService w) => Api.RunAsync(() => w.MapAsync(id, pf))).RequireAuthorization(Roles.Moderator);
        g.MapGet("/galaxy", (long id, WorldService w) => Api.Run(() => w.Galaxy(id))).RequireAuthorization(Roles.Moderator);
        g.MapGet("/shared", (long id, WorldService w) => Api.Run(() => w.SharedPreview(id))).RequireAuthorization(Roles.Admin);
        g.MapPost("/shared/clean", (long id, WorldService w, HttpContext c) => Api.Run(() => w.SharedClean(id, By(c)))).RequireAuthorization(Roles.Admin);
    }
}

/// <summary>Shared error handling for the API groups of steps 4 to 9.</summary>
public static class Api
{
    public static readonly object Ok = new { ok = true };

    public static IResult Run(Func<object> action)
    {
        try { return Results.Ok(action()); }
        catch (Exception e) { return Fail(e); }
    }

    public static async Task<IResult> RunAsync(Func<Task<object>> action)
    {
        try { return Results.Ok(await action()); }
        catch (Exception e) { return Fail(e); }
    }

    private static IResult Fail(Exception e) => e switch
    {
        ServerException se => Results.BadRequest(new { error = se.Code, detail = se.Detail }),
        FormatException or ArgumentException => Results.BadRequest(new { error = e.Message }),
        IOException or UnauthorizedAccessException => Results.BadRequest(new { error = "io_error", detail = e.Message }),
        System.Net.Http.HttpRequestException => Results.BadRequest(new { error = "network_error", detail = e.Message }),
        _ => throw e,
    };
}
