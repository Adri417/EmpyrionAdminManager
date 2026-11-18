using System.Text.Json.Nodes;
using Eam.Core.Security;
using Eam.Engine.Servers;

namespace Eam.Engine.Api;

/// <summary>Vita Somnium Life: configuration files and the game's MySQL data.</summary>
public static class VslEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/servers/{id:long}/vsl");
        static string By(HttpContext c) => c.User.Identity?.Name ?? "?";

        g.MapGet("/config", (long id, VslService v) => Run(() => Task.FromResult(v.Config(id)))).RequireAuthorization(Roles.Admin);
        g.MapPut("/config", (long id, JsonObject req, VslService v, HttpContext c) =>
            Run(() => { v.SaveConfig(id, req, By(c)); return Task.FromResult(Ok); })).RequireAuthorization(Roles.Admin);
        g.MapPut("/database", (long id, VslDatabaseRequest req, VslService v, HttpContext c) =>
            Run(() => { v.SaveDatabase(id, req, By(c)); return Task.FromResult(Ok); })).RequireAuthorization(Roles.Owner);

        g.MapGet("/online", (long id, VslService v) => Run(() => v.OnlineAsync(id))).RequireAuthorization(Roles.Viewer);
        g.MapPost("/db/reset", (long id, VslResetRequest req, VslService v, HttpContext c) =>
            Run(() => v.ResetDatabaseAsync(id, req.ConfirmName, By(c)))).RequireAuthorization(Roles.Owner);
        g.MapGet("/db/tables", (long id, VslService v) => Run(() => v.TablesAsync(id))).RequireAuthorization(Roles.Admin);
        g.MapGet("/db/rows", (long id, string table, string? q, int? offset, int? limit, VslService v) =>
            Run(() => v.RowsAsync(id, table, q, offset ?? 0, limit ?? 50))).RequireAuthorization(Roles.Admin);
        g.MapPut("/db/rows", (long id, string table, VslRowUpdate req, VslService v, HttpContext c) =>
            Run(() => v.UpdateRowAsync(id, table, req, By(c)))).RequireAuthorization(Roles.Admin);
    }

    public sealed record VslResetRequest(string ConfirmName);

    private static readonly object Ok = new { ok = true };

    private static async Task<IResult> Run(Func<Task<object>> action)
    {
        try { return Results.Ok(await action()); }
        catch (ServerException e) { return Results.BadRequest(new { error = e.Code, detail = e.Detail }); }
    }
}
