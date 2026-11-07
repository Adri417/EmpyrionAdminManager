using Eam.Core.Security;

namespace Eam.Engine.Api;

public sealed record CreateUserRequest(string Username, string Password, string Role, string? SteamId);
public sealed record UpdateUserRequest(string Role, string? SteamId, string? NewPassword);

/// <summary>Account management, reserved to owners.</summary>
public static class UserEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/users").RequireAuthorization(Roles.Owner);

        g.MapGet("/", (UserStore users) => Results.Ok(users.List().Select(AuthEndpoints.Describe)));

        g.MapGet("/roles", () => Results.Ok(Roles.All));

        g.MapPost("/", (CreateUserRequest req, UserStore users, ILoggerFactory lf, HttpContext ctx) =>
        {
            try
            {
                var u = users.Create(req.Username, req.Password, req.Role, req.SteamId);
                lf.CreateLogger("Users").LogInformation("{By} created account '{User}' ({Role})", ctx.User.Identity?.Name, u.Username, u.Role);
                return Results.Ok(AuthEndpoints.Describe(u));
            }
            catch (UserStoreException e) { return Results.BadRequest(new { error = e.Code }); }
        });

        g.MapPut("/{id:long}", (long id, UpdateUserRequest req, UserStore users, ILoggerFactory lf, HttpContext ctx) =>
        {
            try
            {
                users.Update(id, req.Role, req.SteamId);
                if (!string.IsNullOrEmpty(req.NewPassword)) users.ChangePassword(id, req.NewPassword);
                lf.CreateLogger("Users").LogInformation("{By} updated account #{Id}", ctx.User.Identity?.Name, id);
                return Results.Ok(AuthEndpoints.Describe(users.FindById(id)!));
            }
            catch (UserStoreException e) { return e.Code == "not_found" ? Results.NotFound() : Results.BadRequest(new { error = e.Code }); }
        });

        g.MapDelete("/{id:long}", (long id, UserStore users, ILoggerFactory lf, HttpContext ctx) =>
        {
            try
            {
                users.Delete(id);
                lf.CreateLogger("Users").LogInformation("{By} deleted account #{Id}", ctx.User.Identity?.Name, id);
                return Results.Ok();
            }
            catch (UserStoreException e) { return e.Code == "not_found" ? Results.NotFound() : Results.BadRequest(new { error = e.Code }); }
        });
    }
}
