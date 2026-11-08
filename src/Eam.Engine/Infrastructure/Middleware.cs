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

/// <summary>
/// Requests coming through the reverse-proxy port: the real client address and scheme come from the
/// X-Forwarded-For / X-Forwarded-Proto headers set by the proxy. Ignored on every other port.
/// </summary>
public sealed class ProxyHeadersMiddleware(RequestDelegate next, Eam.Core.Config.EamConfig config)
{
    public Task Invoke(HttpContext ctx)
    {
        if (ctx.IsProxied())
        {
            if (string.Equals(ctx.Request.Headers["X-Forwarded-Proto"].ToString().Split(',')[0].Trim(), "https", StringComparison.OrdinalIgnoreCase))
                ctx.Request.Scheme = "https";
            // The last address is the one the proxy itself added: the only one that cannot be forged by the client.
            var forwarded = ctx.Request.Headers["X-Forwarded-For"].ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (forwarded.Length > 0 && System.Net.IPAddress.TryParse(forwarded[^1], out var ip)) ctx.Items["eam.client"] = ip.ToString();
        }
        return next(ctx);
    }
}

public static class HttpContextExtensions
{
    public static bool IsProxied(this HttpContext ctx)
    {
        var port = ctx.RequestServices.GetRequiredService<Eam.Core.Config.EamConfig>().RemoteAccess.ProxyPort;
        return port > 0 && ctx.Connection.LocalPort == port && ctx.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip);
    }

    public static bool IsLocal(this HttpContext ctx) =>
        ctx.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip)
        && ctx.Connection.LocalPort == ctx.RequestServices.GetRequiredService<Eam.Core.Config.EamConfig>().LocalPort;

    public static string ClientKey(this HttpContext ctx) =>
        ctx.Items["eam.client"] as string ?? (ctx.IsProxied() ? "proxy" : ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown");
}
