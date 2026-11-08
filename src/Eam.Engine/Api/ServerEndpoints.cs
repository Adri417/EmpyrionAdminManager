using Eam.Core.Security;
using Eam.Engine.Servers;

namespace Eam.Engine.Api;

public sealed record RenameRequest(string Name, string? DisplayName = null);
public sealed record StopRequest(int DelayMinutes, string? Message);
public sealed record DeleteServerRequest(bool DeleteFiles, string ConfirmName);
public sealed record ConsoleRequest(string Command);
public sealed record ChatRequest(string Text, string? Mode, int? PlayerId);

public static class ServerEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/servers");
        static string By(HttpContext c) => c.User.Identity?.Name ?? "?";

        g.MapGet("/", (ServerManager m) => Results.Ok(m.Describe())).RequireAuthorization(Roles.Viewer);
        g.MapGet("/suggest-ports", (ServerManager m) => Results.Ok(m.SuggestPorts())).RequireAuthorization(Roles.Owner);

        g.MapPost("/", (CreateServerRequest req, ServerManager m, HttpContext c) =>
            Run(async () => m.Describe(await m.CreateAsync(req, By(c))))).RequireAuthorization(Roles.Owner);

        g.MapPost("/{id:long}/rename", (long id, RenameRequest req, ServerManager m, HttpContext c) =>
            Run(async () => { await m.RenameAsync(id, req.Name, By(c), req.DisplayName); return Ok; })).RequireAuthorization(Roles.Owner);

        g.MapPut("/{id:long}/settings", (long id, ServerSettingsRequest req, ServerManager m, HttpContext c) =>
            Run(async () => new { appliesOnRestart = await m.UpdateSettingsAsync(id, req, By(c)) })).RequireAuthorization(Roles.Admin);

        g.MapPost("/{id:long}/delete", (long id, DeleteServerRequest req, ServerManager m, HttpContext c) =>
            Run(async () => { await m.DeleteAsync(id, req.DeleteFiles, req.ConfirmName, By(c)); return Ok; })).RequireAuthorization(Roles.Owner);

        // Long operations run in the background; progress arrives over the live socket.
        g.MapPost("/{id:long}/update", (long id, ServerManager m, HttpContext c) => Background(m, id, () => m.InstallAsync(id, false, By(c)))).RequireAuthorization(Roles.Admin);
        g.MapPost("/{id:long}/validate", (long id, ServerManager m, HttpContext c) => Background(m, id, () => m.InstallAsync(id, true, By(c)))).RequireAuthorization(Roles.Admin);
        g.MapPost("/{id:long}/start", (long id, ServerManager m, HttpContext c) =>
            Run(async () => { await m.StartAsync(id, By(c)); return Ok; })).RequireAuthorization(Roles.Admin);
        g.MapPost("/{id:long}/stop", (long id, StopRequest req, ServerManager m, HttpContext c) => Background(m, id, () => m.StopAsync(id, req.DelayMinutes, req.Message, By(c)))).RequireAuthorization(Roles.Admin);
        g.MapPost("/{id:long}/restart", (long id, StopRequest req, ServerManager m, HttpContext c) => Background(m, id, () => m.RestartAsync(id, req.DelayMinutes, req.Message, By(c)))).RequireAuthorization(Roles.Admin);
        g.MapPost("/{id:long}/cancel-stop", (long id, ServerManager m) => Results.Ok(new { cancelled = m.CancelStop(id) })).RequireAuthorization(Roles.Admin);
        g.MapPost("/{id:long}/kill", (long id, ServerManager m, HttpContext c) =>
            Run(async () => { await m.KillAsync(id, By(c)); return Ok; })).RequireAuthorization(Roles.Admin);
        g.MapPost("/{id:long}/resume-auto-restart", (long id, ServerManager m) => { m.ResumeAutoRestart(id); return Results.Ok(); }).RequireAuthorization(Roles.Admin);

        g.MapPost("/{id:long}/console", (long id, ConsoleRequest req, ServerManager m, HttpContext c) =>
            Run(async () => new { output = await m.ConsoleAsync(id, req.Command, By(c)) })).RequireAuthorization(Roles.Admin);

        // Step 3: live view (players, playfields, performance) and chat.
        g.MapGet("/{id:long}/live", (long id, LiveService l) => Results.Ok(l.Snapshot(id))).RequireAuthorization(Roles.Viewer);
        g.MapGet("/{id:long}/chat", (long id, long? before, int? limit, string? q, LiveService l) =>
            Run(() => Task.FromResult<object>(l.Chat(id, limit ?? 100, before, q)))).RequireAuthorization(Roles.Viewer);
        g.MapPost("/{id:long}/chat", (long id, ChatRequest req, LiveService l, HttpContext c) =>
            Run(async () => (object?)await l.SendAsync(id, req.Text, req.Mode ?? "chat", req.PlayerId, By(c)) ?? Ok)).RequireAuthorization(Roles.Moderator);

        g.MapGet("/{id:long}/logs", (long id, string? kind, int? lines, ServerManager m) =>
            Run(() => Task.FromResult<object>(new
            {
                lines = kind == "game" ? m.GameLogTail(id, lines ?? 300) : m.EamLogTail(id, lines ?? 300),
            }))).RequireAuthorization(Roles.Admin);
    }

    private static readonly object Ok = new { ok = true };

    private static async Task<IResult> Run(Func<Task<object>> action)
    {
        try { return Results.Ok(await action()); }
        catch (ServerException e) { return Results.BadRequest(new { error = e.Code, detail = e.Detail }); }
    }

    /// <summary>Checks the operation can start, then lets it finish in the background.</summary>
    private static IResult Background(ServerManager m, long id, Func<Task> op)
    {
        var rt = m.Runtime(id);
        if (rt.Lock.CurrentCount == 0) return Results.BadRequest(new { error = "server_busy", detail = rt.Operation });
        var task = op();
        if (task.IsFaulted && task.Exception?.InnerException is ServerException e)
            return Results.BadRequest(new { error = e.Code, detail = e.Detail });
        _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        return Results.Accepted();
    }
}
