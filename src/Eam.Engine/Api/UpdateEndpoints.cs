using Eam.Core.Security;
using Eam.Engine.Infrastructure;
using Eam.Engine.Servers;

namespace Eam.Engine.Api;

public sealed record UpdateSettingsRequest(bool Check);

/// <summary>Program update from the GitHub releases.</summary>
public static class UpdateEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/update", (UpdateService u) => Results.Ok(u.Status())).RequireAuthorization(Roles.Viewer);
        app.MapPost("/api/update/check", async (UpdateService u) =>
        {
            try { return Results.Ok(await u.CheckAsync()); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            { return Results.BadRequest(new { error = "update_check_failed", detail = e.Message }); }
        }).RequireAuthorization(Roles.Owner);
        app.MapPut("/api/update/settings", (UpdateSettingsRequest req, UpdateService u) =>
        {
            var s = u.Settings();
            s.Check = req.Check;
            u.Save(s);
            return Results.Ok(u.Status());
        }).RequireAuthorization(Roles.Owner);
        app.MapPost("/api/update/apply", async (UpdateService u, HttpContext c) =>
        {
            try { return Results.Ok(await u.ApplyAsync(c.User.Identity?.Name ?? "?")); }
            catch (ServerException e) { return Results.BadRequest(new { error = e.Code, detail = e.Detail }); }
            catch (Exception e) when (e is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
            { return Results.BadRequest(new { error = "update_failed", detail = e.Message }); }
        }).RequireAuthorization(Roles.Owner);
    }
}
