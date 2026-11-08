using Eam.Core;
using Eam.Core.Config;
using Eam.Core.Monitoring;
using Eam.Core.Security;

namespace Eam.Engine.Api;

public sealed record SettingsRequest(int LocalPort, bool RemoteEnabled, int RemotePort, string? CertificatePath, string? CertificatePassword, string Language, int SessionDays,
    LetsEncryptConfig? LetsEncrypt = null, int ProxyPort = 0, string? ServersFolder = null);

public static class SystemEndpoints
{
    public static void Map(WebApplication app)
    {
        app.MapGet("/api/system", (SystemMonitor mon) => Results.Ok(mon.Take())).RequireAuthorization(Roles.Viewer);

        app.MapGet("/api/settings", (EamConfig cfg, EamPaths paths, Eam.Engine.Infrastructure.LetsEncryptService le) => Results.Ok(new
        {
            dataFolder = paths.DataRoot,
            configFile = paths.ConfigFile,
            version = AppInfo.Version,
            localPort = cfg.LocalPort,
            serversFolder = cfg.ServersFolder,
            serversFolderInUse = paths.ServersDir,
            serversFolderDefault = paths.DefaultServersDir,
            remoteEnabled = cfg.RemoteAccess.Enabled,
            remotePort = cfg.RemoteAccess.Port,
            proxyPort = cfg.RemoteAccess.ProxyPort,
            certificatePath = cfg.RemoteAccess.CertificatePath,
            hasCertificatePassword = cfg.RemoteAccess.CertificatePassword.Length > 0,
            language = cfg.Language,
            sessionDays = cfg.SessionDays,
            letsEncrypt = EamConfig.Load(paths).RemoteAccess.LetsEncrypt,
            letsEncryptStatus = new { error = le.LastError, expiresUtc = le.Expires },
        })).RequireAuthorization(Roles.Owner);

        // Saved to eam.json; network changes apply after the engine restarts.
        app.MapPut("/api/settings", (SettingsRequest req, EamPaths paths, ILoggerFactory lf, HttpContext ctx) =>
        {
            var current = EamConfig.Load(paths);
            var next = new EamConfig
            {
                LocalPort = req.LocalPort,
                ServersFolder = req.ServersFolder?.Trim().Trim('"') ?? current.ServersFolder,
                Language = req.Language,
                SessionDays = req.SessionDays,
                RemoteAccess = new RemoteAccessConfig
                {
                    Enabled = req.RemoteEnabled,
                    Port = req.RemotePort,
                    ProxyPort = req.ProxyPort,
                    CertificatePath = req.CertificatePath?.Trim() ?? "",
                    // Empty = keep the stored password.
                    CertificatePassword = string.IsNullOrEmpty(req.CertificatePassword) ? current.RemoteAccess.CertificatePassword : req.CertificatePassword,
                    LetsEncrypt = req.LetsEncrypt ?? current.RemoteAccess.LetsEncrypt,
                },
            };
            try { next.Validate(); }
            catch (InvalidDataException e) { return Results.BadRequest(new { error = "invalid_settings", detail = e.Message }); }
            if (next.RemoteAccess.CertificatePath.Length > 0 && !File.Exists(next.RemoteAccess.CertificatePath))
                return Results.BadRequest(new { error = "certificate_not_found" });

            if (next.ServersFolder.Length > 0)
            {
                try { Directory.CreateDirectory(next.ServersFolder); }
                catch { return Results.BadRequest(new { error = "servers_folder_unusable" }); }
            }
            next.Save(paths);
            // New servers go to the new folder right away; existing servers stay where they are.
            paths.UseServersFolder(next.ServersFolder);
            lf.CreateLogger("Settings").LogInformation("{By} changed the settings", ctx.User.Identity?.Name);
            var restart = next.LocalPort != current.LocalPort || next.RemoteAccess.Enabled != current.RemoteAccess.Enabled
                          || next.RemoteAccess.Port != current.RemoteAccess.Port || next.RemoteAccess.ProxyPort != current.RemoteAccess.ProxyPort || next.RemoteAccess.CertificatePath != current.RemoteAccess.CertificatePath
                          || next.SessionDays != current.SessionDays
                          || System.Text.Json.JsonSerializer.Serialize(next.RemoteAccess.LetsEncrypt) != System.Text.Json.JsonSerializer.Serialize(current.RemoteAccess.LetsEncrypt);
            return Results.Ok(new { restartRequired = restart });
        }).RequireAuthorization(Roles.Owner);
    }
}
