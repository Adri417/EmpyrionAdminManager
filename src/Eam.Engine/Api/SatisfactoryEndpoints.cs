using Eam.Core.Security;
using Eam.Engine.Servers;

namespace Eam.Engine.Api;

/// <summary>Satisfactory: live state (through the game's HTTPS API) and the admin password EAM set.</summary>
public static class SatisfactoryEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/servers/{id:long}/satisfactory");
        g.MapGet("/state", async (long id, ServerManager m) =>
        {
            try
            {
                var def = m.Definition(id);
                if (!def.IsSatisfactory) return Results.BadRequest(new { error = "not_satisfactory" });
                if (m.Runtime(id).State != ServerState.Running) return Results.Ok(new { answered = false });
                return Results.Ok(await SatisfactoryControl.StateAsync(def));
            }
            catch (ServerException e) { return Results.BadRequest(new { error = e.Code }); }
            catch (Exception) { return Results.Ok(new { answered = false }); }
        }).RequireAuthorization(Roles.Viewer);
        // The admin password of the game's own Server Manager (in game: Server Manager > Administrator).
        g.MapGet("/admin", (long id, ServerManager m) =>
        {
            try
            {
                var def = m.Definition(id);
                return def.IsSatisfactory ? Results.Ok(new { password = def.TelnetPassword }) : Results.BadRequest(new { error = "not_satisfactory" });
            }
            catch (ServerException e) { return Results.BadRequest(new { error = e.Code }); }
        }).RequireAuthorization(Roles.Owner);
    }
}
