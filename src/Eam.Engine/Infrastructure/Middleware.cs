namespace Eam.Engine.Infrastructure;

/// <summary>Strict browser security headers on every response.</summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task Invoke(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self'; script-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
        h["X-Content-Type-Options"] = "nosniff";
        h["X-Frame-Options"] = "DENY";
        h["Referrer-Policy"] = "no-referrer";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        if (ctx.Request.IsHttps) h["Strict-Transport-Security"] = "max-age=31536000";
        return next(ctx);
    }
}

/// <summary>
/// Blocks cross-site requests: every call that changes something must carry the
/// X-EAM header, which a foreign web page cannot add without CORS permission.
/// </summary>
public sealed class CsrfMiddleware(RequestDelegate next)
{
    public Task Invoke(HttpContext ctx)
    {
        var m = ctx.Request.Method;
        if (ctx.Request.Path.StartsWithSegments("/api") && m is not ("GET" or "HEAD" or "OPTIONS")
            && ctx.Request.Headers["X-EAM"] != "1")
        {
            ctx.Response.StatusCode = 400;
            return ctx.Response.WriteAsJsonAsync(new { error = "missing_csrf_header" });
        }
        return next(ctx);
    }
}

public static class HttpContextExtensions
{
    public static bool IsLocal(this HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip)
        && ctx.Connection.LocalPort == ctx.RequestServices.GetRequiredService<Eam.Core.Config.EamConfig>().LocalPort;

    public static string ClientKey(this HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
