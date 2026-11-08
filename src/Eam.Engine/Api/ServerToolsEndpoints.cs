using Eam.Core.Security;
using Eam.Engine.Servers;

namespace Eam.Engine.Api;

public sealed record BanSteamRequest(string SteamId, string? Duration, bool Unban, string? Reason);

public sealed record BackupCreateRequest(string? Note, string? Kind = null);
public sealed record BackupPartRequest(string Name, List<int>? Structures, string? SteamId, bool Keep = false);
public sealed record BackupRestoreRequest(string Name, bool RestoreConfig, bool RestoreEamData);
public sealed record OptionsRequest(List<OptionChange> Changes);
public sealed record RawYamlRequest(string Text);
public sealed record WorkshopRequest(string WorkshopId, string Name, bool Use);
public sealed record FolderImportRequest(string Path, string Name, bool Use);

/// <summary>Steps 4 and 5: backups, scheduler, every server and game option, scenarios, players.</summary>
public static class ServerToolsEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/servers/{id:long}");
        static string By(HttpContext c) => c.User.Identity?.Name ?? "?";
        static string RoleOf(HttpContext c) => c.User.FindFirst(AuthEndpoints.RoleClaim)?.Value ?? Roles.Viewer;

        // ---------------- backups
        g.MapGet("/backups", (long id, BackupService b) => Run(() => new { list = b.List(id), settings = b.Settings(id), running = b.IsRunning(id) }))
            .RequireAuthorization(Roles.Admin);
        g.MapPost("/backups", (long id, BackupCreateRequest req, BackupService b, HttpContext c) =>
        {
            if (b.IsRunning(id)) return Results.BadRequest(new { error = "backup_running" });
            _ = b.CreateAsync(id, req.Kind == "structures" ? "structures" : "manual", By(c), req.Note?.Trim() is { Length: > 0 and <= 200 } n ? n : null)
                .ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return Results.Accepted();
        }).RequireAuthorization(Roles.Admin);
        g.MapPut("/backups/settings", (long id, BackupSettings req, BackupService b) => Run(() => { b.SaveSettings(id, req); return Ok; }))
            .RequireAuthorization(Roles.Admin);
        g.MapPost("/backups/restore", (long id, BackupRestoreRequest req, BackupService b, HttpContext c) =>
            RunAsync(async () => { await b.RestoreAsync(id, req.Name, req.RestoreConfig, req.RestoreEamData, By(c)); return Ok; }))
            .RequireAuthorization(Roles.Owner);
        g.MapPost("/backups/delete", (long id, BackupRestoreRequest req, BackupService b, HttpContext c) => Run(() => { b.Delete(id, req.Name, By(c)); return Ok; }))
            .RequireAuthorization(Roles.Admin);
        g.MapPost("/backups/keep", (long id, BackupPartRequest req, BackupService b) => Run(() => { b.SetKept(id, req.Name, req.Keep); return Ok; }))
            .RequireAuthorization(Roles.Admin);
        g.MapGet("/backups/contents", (long id, string name, BackupService b) => Run(() => b.Contents(id, name))).RequireAuthorization(Roles.Admin);
        g.MapPost("/backups/restore-part", (long id, BackupPartRequest req, BackupService b, HttpContext c) =>
            RunAsync(() => b.RestorePartAsync(id, req.Name, req.Structures, req.SteamId, By(c)))).RequireAuthorization(Roles.Admin);
        g.MapGet("/backups/download", (long id, string name, BackupService b) =>
        {
            try { return Results.File(b.FileOf(id, name), "application/zip", name + ".zip", enableRangeProcessing: true); }
            catch (ServerException e) { return Results.BadRequest(new { error = e.Code }); }
        }).RequireAuthorization(Roles.Admin);

        // ---------------- scheduler
        g.MapGet("/tasks", (long id, SchedulerService s) => Run(() => s.List(id))).RequireAuthorization(Roles.Admin);
        g.MapPost("/tasks", (long id, TaskRequest req, SchedulerService s, HttpContext c) => Run(() => new { id = s.Save(id, 0, req, By(c)) }))
            .RequireAuthorization(Roles.Admin);
        g.MapPut("/tasks/{taskId:long}", (long id, long taskId, TaskRequest req, SchedulerService s, HttpContext c) => Run(() => new { id = s.Save(id, taskId, req, By(c)) }))
            .RequireAuthorization(Roles.Admin);
        g.MapPost("/tasks/{taskId:long}/delete", (long id, long taskId, SchedulerService s, HttpContext c) => Run(() => { s.Delete(id, taskId, By(c)); return Ok; }))
            .RequireAuthorization(Roles.Admin);
        g.MapPost("/tasks/{taskId:long}/run", (long id, long taskId, SchedulerService s, HttpContext c) => Run(() => { s.RunNow(id, taskId, By(c)); return Ok; }))
            .RequireAuthorization(Roles.Admin);

        // ---------------- options and scenarios
        g.MapGet("/config", (long id, GameConfigService s) => Run(() => s.Get(id))).RequireAuthorization(Roles.Admin);
        g.MapPut("/config/options", (long id, OptionsRequest req, GameConfigService s, HttpContext c) =>
            RunAsync(async () => new { appliesOnRestart = await s.SaveOptionsAsync(id, req.Changes ?? [], By(c)) })).RequireAuthorization(Roles.Admin);
        g.MapPut("/config/raw", (long id, RawYamlRequest req, GameConfigService s, HttpContext c) =>
            RunAsync(async () => new { appliesOnRestart = await s.SaveRawAsync(id, req.Text, By(c)) })).RequireAuthorization(Roles.Owner);
        g.MapPut("/config/gameoptions", (long id, OptionsRequest req, GameConfigService s, HttpContext c) =>
            RunAsync(async () => { await s.SaveGameOptionsAsync(id, req.Changes ?? [], By(c)); return Ok; })).RequireAuthorization(Roles.Admin);
        g.MapPost("/scenarios/workshop", (long id, WorkshopRequest req, GameConfigService s, LiveHub hub, HttpContext c) =>
            Background(s.InstallWorkshopAsync(id, req.WorkshopId?.Trim() ?? "", req.Name?.Trim() ?? "", req.Use, By(c)), hub, id, "scenario")).RequireAuthorization(Roles.Owner);
        g.MapPost("/scenarios/import", (long id, FolderImportRequest req, GameConfigService s, LiveHub hub, HttpContext c) =>
            Background(s.InstallFromAsync(id, req.Path?.Trim() ?? "", req.Name?.Trim() ?? "", null, req.Use, By(c)), hub, id, "scenario")).RequireAuthorization(Roles.Owner);
        g.MapPost("/scenarios/delete", (long id, FolderImportRequest req, GameConfigService s, HttpContext c) =>
            RunAsync(async () => { await s.DeleteScenarioAsync(id, req.Name ?? "", By(c)); return Ok; })).RequireAuthorization(Roles.Owner);

        // ---------------- players
        g.MapGet("/players", (long id, PlayerService p) => Run(() => p.List(id))).RequireAuthorization(Roles.Moderator);
        g.MapGet("/players/banned", (long id, PlayerService p) => RunAsync(() => p.BannedAsync(id))).RequireAuthorization(Roles.Moderator);
        g.MapGet("/players/items", (long id, string? lang, PlayerService p) => Run(() => p.Items(id, lang ?? "en"))).RequireAuthorization(Roles.GameMaster);
        // Item pictures of THIS server (its scenario's icons, see ItemIcons): 404 when there is none.
        g.MapGet("/item-icon/{itemId:int}", (long id, int itemId, PlayerService p, HttpContext c) =>
        {
            var f = p.IconFile(id, itemId);
            if (f is null) return Results.NotFound();
            c.Response.Headers.CacheControl = "private, max-age=86400";
            return Results.File(f, "image/png");
        }).RequireAuthorization(Roles.GameMaster);
        g.MapGet("/players/{steamId}", (long id, string steamId, PlayerService p) => RunAsync(() => p.DetailAsync(id, steamId))).RequireAuthorization(Roles.Moderator);
        g.MapPost("/players/{steamId}/action", (long id, string steamId, PlayerActionRequest req, PlayerService p, Eam.Core.Security.UserStore users, HttpContext c) =>
            RunAsync(async () =>
            {
                // "Impersonate" uses the admin's own character: the Steam ID of their EAM account.
                if (req.Action is "impersonate" or "impersonate_end")
                    req = req with { Target = users.FindByName(By(c))?.SteamId ?? throw new ServerException("admin_steam_missing") };
                return await p.ActAsync(id, steamId, req, By(c), RoleOf(c)) ?? Ok;
            })).RequireAuthorization(Roles.Moderator);
        g.MapPost("/players/ban-steam", (long id, BanSteamRequest req, PlayerService p, HttpContext c) => RunAsync(async () => { await p.BanSteamAsync(id, req, By(c)); return Ok; }))
            .RequireAuthorization(Roles.Moderator);
        g.MapGet("/factions", (long id, PlayerService p) => Run(() => p.Factions(id))).RequireAuthorization(Roles.Moderator);
        g.MapPost("/players/kick-all", (long id, NameRequest req, PlayerService p, HttpContext c) => RunAsync(async () => new { kicked = await p.KickAllAsync(id, req.Name, By(c)) }))
            .RequireAuthorization(Roles.Moderator);
        g.MapGet("/inventories", (long id, string? steamId, string? reason, PlayerService p) => Run(() => p.Inventories(id, steamId, reason))).RequireAuthorization(Roles.GameMaster);
        g.MapGet("/inventory-templates", (long id, LiveService l, ServerManager m) => Run(() => l.Data(m.Definition(id)).InventoryTemplates())).RequireAuthorization(Roles.GameMaster);
        g.MapPost("/inventory-templates/delete", (long id, NameRequest req, LiveService l, ServerManager m) => Run(() => { l.Data(m.Definition(id)).DeleteInventoryTemplate(req.Name); return Ok; }))
            .RequireAuthorization(Roles.Admin);
        g.MapGet("/positions", (long id, LiveService l, ServerManager m) => Run(() => l.Data(m.Definition(id)).SavedPositions())).RequireAuthorization(Roles.GameMaster);
        g.MapPost("/positions", (long id, Eam.Core.Servers.SavedPosition req, LiveService l, ServerManager m) => Run(() =>
        {
            if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 40 || string.IsNullOrWhiteSpace(req.Playfield)) throw new ServerException("invalid_name");
            l.Data(m.Definition(id)).SavePosition(req with { Name = req.Name.Trim(), Playfield = req.Playfield.Trim() });
            return Ok;
        })).RequireAuthorization(Roles.GameMaster);
        g.MapPost("/positions/delete", (long id, NameRequest req, LiveService l, ServerManager m) => Run(() => { l.Data(m.Definition(id)).DeletePosition(req.Name); return Ok; }))
            .RequireAuthorization(Roles.GameMaster);
    }

    private static readonly object Ok = new { ok = true };

    private static IResult Run(Func<object> action)
    {
        try { return Results.Ok(action()); }
        catch (ServerException e) { return Results.BadRequest(new { error = e.Code, detail = e.Detail }); }
        catch (Exception e) when (e is ArgumentException or FormatException or IOException or UnauthorizedAccessException)
        { return Results.BadRequest(new { error = e is ArgumentException or FormatException ? e.Message : "io_error", detail = e.Message }); }
    }

    private static async Task<IResult> RunAsync(Func<Task<object>> action)
    {
        try { return Results.Ok(await action()); }
        catch (ServerException e) { return Results.BadRequest(new { error = e.Code, detail = e.Detail }); }
        catch (Exception e) when (e is ArgumentException or FormatException or IOException or UnauthorizedAccessException)
        { return Results.BadRequest(new { error = e is ArgumentException or FormatException ? e.Message : "io_error", detail = e.Message }); }
    }

    /// <summary>Long job: errors found right away are returned, the rest is reported on the server's page.</summary>
    private static IResult Background(Task task, LiveHub hub, long id, string job)
    {
        if (task.IsFaulted)
        {
            var e = task.Exception!.InnerException;
            return e is ServerException se ? Results.BadRequest(new { error = se.Code, detail = se.Detail })
                : Results.BadRequest(new { error = e is ArgumentException ? e.Message : "error", detail = e?.Message });
        }
        _ = task.ContinueWith(t =>
        {
            var e = t.Exception?.InnerException;
            var error = e is ServerException se ? se.Code : e?.Message;
            var detail = (e as ServerException)?.Detail;
            return hub.BroadcastAsync(new { type = "job", id, job, ok = e is null, error, detail }, CancellationToken.None);
        });
        return Results.Accepted();
    }
}
