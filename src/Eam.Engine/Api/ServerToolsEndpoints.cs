using Eam.Core.Security;
using Eam.Engine.Servers;

namespace Eam.Engine.Api;

public sealed record BackupCreateRequest(string? Note);
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
            _ = b.CreateAsync(id, "manual", By(c), req.Note?.Trim() is { Length: > 0 and <= 200 } n ? n : null)
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
        g.MapGet("/players/{steamId}", (long id, string steamId, PlayerService p) => RunAsync(() => p.DetailAsync(id, steamId))).RequireAuthorization(Roles.Moderator);
        g.MapPost("/players/{steamId}/action", (long id, string steamId, PlayerActionRequest req, PlayerService p, HttpContext c) =>
            RunAsync(async () => await p.ActAsync(id, steamId, req, By(c), RoleOf(c)) ?? Ok)).RequireAuthorization(Roles.Moderator);
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
