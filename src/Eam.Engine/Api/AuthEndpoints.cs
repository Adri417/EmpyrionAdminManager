using System.Security.Claims;
using Eam.Core;
using Eam.Core.Security;
using Eam.Engine.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Eam.Engine.Api;

public sealed record SetupRequest(string Username, string Password);
public sealed record LoginRequest(string Username, string Password, bool Remember);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public static class AuthEndpoints
{
    public const string IdClaim = "eam:id";
    public const string RoleClaim = "eam:role";
    public const string StampClaim = "eam:stamp";

    public static void Map(WebApplication app)
    {
        // Public: tells the interface which screen to show.
        app.MapGet("/api/status", (HttpContext ctx, UserStore users) => Results.Ok(new
        {
            app = AppInfo.Name,
            version = AppInfo.Version,
            needsSetup = users.NeedsSetup,
            setupAllowedHere = ctx.IsLocal(),
            authenticated = ctx.User.Identity?.IsAuthenticated == true,
        }));

        // First administrator. Only from this machine, and only while no account exists.
        app.MapPost("/api/setup", async (HttpContext ctx, SetupRequest req, UserStore users, ILoggerFactory lf) =>
        {
            if (!ctx.IsLocal()) return Results.Json(new { error = "setup_local_only" }, statusCode: 403);
            try
            {
                var user = users.CreateFirstOwner(req.Username, req.Password);
                lf.CreateLogger("Auth").LogInformation("First administrator account '{User}' created", user.Username);
                await SignIn(ctx, user, remember: true);
                return Results.Ok(Describe(user));
            }
            catch (UserStoreException e) { return Results.BadRequest(new { error = e.Code }); }
        });

        app.MapPost("/api/login", async (HttpContext ctx, LoginRequest req, UserStore users, LoginThrottle throttle, ILoggerFactory lf) =>
        {
            var log = lf.CreateLogger("Auth");
            var key = ctx.ClientKey();
            if (throttle.IsBlocked(key, out var wait))
                return Results.Json(new { error = "too_many_attempts", retryAfterSeconds = (int)wait.TotalSeconds + 1 }, statusCode: 429);

            var user = users.CheckPassword(req.Username ?? "", req.Password ?? "");
            if (user is null)
            {
                throttle.Fail(key);
                log.LogWarning("Failed login for '{User}' from {Ip}", req.Username, key);
                return Results.Json(new { error = "invalid_credentials" }, statusCode: 401);
            }
            throttle.Success(key);
            await SignIn(ctx, user, req.Remember);
            log.LogInformation("'{User}' signed in from {Ip}", user.Username, key);
            return Results.Ok(Describe(user));
        });

        app.MapPost("/api/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        });

        app.MapGet("/api/me", (HttpContext ctx, UserStore users) =>
            CurrentUser(ctx, users) is { } u ? Results.Ok(Describe(u)) : Results.Unauthorized())
            .RequireAuthorization(Roles.Viewer);

        app.MapPost("/api/me/password", async (HttpContext ctx, ChangePasswordRequest req, UserStore users) =>
        {
            var me = CurrentUser(ctx, users);
            if (me is null) return Results.Unauthorized();
            if (users.CheckPassword(me.Username, req.CurrentPassword ?? "") is null)
                return Results.BadRequest(new { error = "invalid_current_password" });
            try
            {
                users.ChangePassword(me.Id, req.NewPassword);
                // Other sessions are closed; this one is renewed.
                await SignIn(ctx, users.FindById(me.Id)!, remember: true);
                return Results.Ok();
            }
            catch (UserStoreException e) { return Results.BadRequest(new { error = e.Code }); }
        }).RequireAuthorization(Roles.Viewer);
    }

    public static object Describe(User u) => new
    {
        id = u.Id,
        username = u.Username,
        role = u.Role,
        steamId = u.SteamId,
        createdUtc = u.CreatedUtc,
        lastLoginUtc = u.LastLoginUtc,
    };

    public static User? CurrentUser(HttpContext ctx, UserStore users) =>
        long.TryParse(ctx.User.FindFirst(IdClaim)?.Value, out var id) ? users.FindById(id) : null;

    private static Task SignIn(HttpContext ctx, User user, bool remember)
    {
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(IdClaim, user.Id.ToString()),
            new Claim(RoleClaim, user.Role),
            new Claim(StampClaim, user.SecurityStamp),
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        return ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = remember });
    }

    /// <summary>
    /// Runs on every request: a deleted account, a changed password or a changed role
    /// ends the existing sessions immediately.
    /// </summary>
    public static async Task ValidatePrincipal(CookieValidatePrincipalContext ctx)
    {
        var users = ctx.HttpContext.RequestServices.GetRequiredService<UserStore>();
        var p = ctx.Principal;
        if (p is null || !long.TryParse(p.FindFirst(IdClaim)?.Value, out var id)
            || users.FindById(id) is not { } user
            || user.SecurityStamp != p.FindFirst(StampClaim)?.Value
            || user.Role != p.FindFirst(RoleClaim)?.Value)
        {
            ctx.RejectPrincipal();
            await ctx.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }
}
