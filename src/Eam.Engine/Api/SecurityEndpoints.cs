using Eam.Core.Security;
using Eam.Engine.Servers;

namespace Eam.Engine.Api;

/// <summary>Step 8: journal, "what happened here", position tracks, statistics and anti-cheat scan.</summary>
public static class SecurityEndpoints
{
    public static void Map(WebApplication app)
    {
        var g = app.MapGroup("/api/servers/{id:long}");
        static string By(HttpContext c) => c.User.Identity?.Name ?? "?";

        g.MapGet("/journal", (long id, string? category, int? severity, long? before, string? steam, SecurityService s) =>
            Api.Run(() => s.Journal(id, category, severity ?? 0, before, steam))).RequireAuthorization(Roles.Moderator);
        g.MapGet("/history/near", (long id, string pf, double x, double y, double z, double radius, DateTime from, DateTime to, SecurityService s) =>
            Api.Run(() => s.Near(id, pf, x, y, z, radius, from.ToUniversalTime(), to.ToUniversalTime()))).RequireAuthorization(Roles.Moderator);
        g.MapGet("/history/track", (long id, string reference, DateTime from, DateTime to, SecurityService s) =>
            Api.Run(() => s.Track(id, reference, from.ToUniversalTime(), to.ToUniversalTime()))).RequireAuthorization(Roles.Moderator);
        g.MapGet("/stats", (long id, int? days, SecurityService s) => Api.Run(() => s.Stats(id, days ?? 7))).RequireAuthorization(Roles.Viewer);
        g.MapPost("/anticheat/scan", (long id, SecurityService s, ServerManager m, HttpContext c) =>
            Api.RunAsync(async () => await s.ScanAsync(m.Definition(id), s.Settings(m.Definition(id)), By(c)))).RequireAuthorization(Roles.Moderator);
    }
}
