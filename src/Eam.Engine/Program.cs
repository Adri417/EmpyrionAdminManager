using System.Net;
using Eam.Core;
using Eam.Core.Config;
using Eam.Core.Data;
using Eam.Core.Monitoring;
using Eam.Core.Security;
using Eam.Engine.Api;
using Eam.Engine.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

// ---------------------------------------------------------------------------
// Empyrion Admin Manager engine.
// Runs as a Windows service (or in a console window for testing) and serves
// the interface used by both the desktop window and remote browsers.
// ---------------------------------------------------------------------------

string? dataArg = null;
for (var i = 0; i < args.Length - 1; i++)
    if (args[i] is "--data") dataArg = args[i + 1];

var paths = EamPaths.Resolve(dataArg);
paths.EnsureCreated();
var config = EamConfig.Load(paths);

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
    WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot"),
});

builder.Host.UseWindowsService(o => o.ServiceName = AppInfo.ServiceName);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
builder.Logging.AddProvider(new FileLoggerProvider(paths.LogsDir));
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

var db = new Database(paths.DatabaseFile);
db.Migrate();

builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(config);
builder.Services.AddSingleton(db);
builder.Services.AddSingleton<UserStore>();
builder.Services.AddSingleton<LoginThrottle>();
builder.Services.AddSingleton<SystemMonitor>();
builder.Services.AddSingleton<LiveHub>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveHub>());

var keys = builder.Services.AddDataProtection()
    .SetApplicationName(AppInfo.ServiceName)
    .PersistKeysToFileSystem(new DirectoryInfo(paths.KeysDir));
if (OperatingSystem.IsWindows()) keys.ProtectKeysWithDpapi(protectToLocalMachine: true);

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "eam_session";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromDays(config.SessionDays);
        o.SlidingExpiration = true;
        o.Events.OnRedirectToLogin = ctx => { ctx.Response.StatusCode = 401; return Task.CompletedTask; };
        o.Events.OnRedirectToAccessDenied = ctx => { ctx.Response.StatusCode = 403; return Task.CompletedTask; };
        o.Events.OnValidatePrincipal = AuthEndpoints.ValidatePrincipal;
    });
builder.Services.AddAuthorization(o =>
{
    foreach (var role in Roles.All)
        o.AddPolicy(role, p => p.RequireAuthenticatedUser().RequireAssertion(ctx =>
            ctx.User.FindFirst(AuthEndpoints.RoleClaim)?.Value is { } r && Roles.AtLeast(r, role)));
});

builder.WebHost.ConfigureKestrel(k =>
{
    k.AddServerHeader = false;
    k.Limits.MaxRequestBodySize = 10 * 1024 * 1024;
    // Local interface: only this machine can reach it.
    k.Listen(IPAddress.Loopback, config.LocalPort);
    if (config.RemoteAccess.Enabled)
    {
        using var lf = LoggerFactory.Create(l => l.AddProvider(new FileLoggerProvider(paths.LogsDir)));
        var cert = Certificates.LoadOrCreate(paths, config.RemoteAccess, lf.CreateLogger("Certificates"));
        k.Listen(IPAddress.Any, config.RemoteAccess.Port, l => l.UseHttps(cert));
    }
});

var app = builder.Build();
var log = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Engine");

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache",
});
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<CsrfMiddleware>();

AuthEndpoints.Map(app);
UserEndpoints.Map(app);
SystemEndpoints.Map(app);
app.Map("/ws", (HttpContext ctx, LiveHub hub) => hub.Accept(ctx)).RequireAuthorization(Roles.Viewer);
app.MapFallback(ctx =>
{
    // Unknown API routes stay 404; everything else gets the single-page interface.
    if (ctx.Request.Path.StartsWithSegments("/api")) { ctx.Response.StatusCode = 404; return Task.CompletedTask; }
    ctx.Response.ContentType = "text/html; charset=utf-8";
    return ctx.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "index.html"));
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    log.LogInformation("{App} {Version} started. Data folder: {Data}", AppInfo.Name, AppInfo.Version, paths.DataRoot);
    log.LogInformation("Local interface: http://127.0.0.1:{Port}/", config.LocalPort);
    if (config.RemoteAccess.Enabled) log.LogInformation("Remote interface: https://<this machine>:{Port}/", config.RemoteAccess.Port);
    if (app.Services.GetRequiredService<UserStore>().NeedsSetup)
        log.LogWarning("No account yet: open the local interface on this machine to create the first administrator.");
});
app.Lifetime.ApplicationStopping.Register(() => log.LogInformation("Engine stopping"));

try
{
    app.Run();
}
catch (Exception ex) when (ex is Microsoft.AspNetCore.Connections.AddressInUseException
    || ex.InnerException is Microsoft.AspNetCore.Connections.AddressInUseException
    || ex is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.AddressAlreadyInUse or System.Net.Sockets.SocketError.AccessDenied })
{
    log.LogCritical("Port already in use ({Message}). Is another copy of EAM running? Change localPort in {File}.", ex.Message, paths.ConfigFile);
    Environment.ExitCode = 2;
}

public partial class Program;
