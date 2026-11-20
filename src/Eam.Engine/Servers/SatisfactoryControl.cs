using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Eam.Core.Servers;

namespace Eam.Engine.Servers;

/// <summary>
/// Satisfactory: everything goes through the server's own HTTPS API on the game port (TCP), on this machine only.
/// The server uses a self-signed certificate, accepted here because the address is always 127.0.0.1.
/// </summary>
public static class SatisfactoryControl
{
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (req, _, _, _) => req.RequestUri is { } u && IPAddress.TryParse(u.Host, out var ip) && IPAddress.IsLoopback(ip),
    }) { Timeout = TimeSpan.FromSeconds(15) };

    public sealed record Reply(HttpStatusCode Status, JsonNode? Data, string? ErrorCode, string? ErrorMessage)
    {
        public bool Ok => (int)Status is >= 200 and < 300 && ErrorCode is null;
    }

    public static async Task<Reply> CallAsync(int port, string function, object? data = null, string? token = null, CancellationToken ct = default)
    {
        var body = data is null ? new JsonObject { ["function"] = function } : new JsonObject { ["function"] = function, ["data"] = JsonSerializer.SerializeToNode(data) };
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://127.0.0.1:{port}/api/v1")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var res = await Http.SendAsync(req, ct);
        var text = await res.Content.ReadAsStringAsync(ct);
        JsonNode? json = null;
        try { json = text.Length > 0 ? JsonNode.Parse(text) : null; } catch (JsonException) { }
        return new Reply(res.StatusCode, json?["data"], json?["errorCode"]?.ToString(), json?["errorMessage"]?.ToString());
    }

    public static async Task<bool> IsHealthyAsync(int port, CancellationToken ct = default)
    {
        try { return (await CallAsync(port, "HealthCheck", new { clientCustomData = "" }, null, ct)).Ok; }
        catch { return false; }
    }

    /// <summary>Admin token with EAM's admin password, or null.</summary>
    public static async Task<string?> AdminTokenAsync(ServerDefinition def, CancellationToken ct = default)
    {
        try
        {
            var r = await CallAsync(def.GamePort, "PasswordLogin", new { minimumPrivilegeLevel = "Administrator", password = def.TelnetPassword }, null, ct);
            return r.Ok ? r.Data?["authenticationToken"]?.ToString() : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// After each start: claims the server the first time (sets EAM's admin password and the name), then applies
    /// the name and join password. One short series of calls, nothing repeated while it runs.
    /// </summary>
    public static async Task AfterStartAsync(ServerDefinition def, Action<string> log, CancellationToken ct = default)
    {
        for (var i = 0; i < 30 && !await IsHealthyAsync(def.GamePort, ct); i++) await Task.Delay(TimeSpan.FromSeconds(10), ct);
        var settings = SatisfactoryConfig.Load(def);
        var token = await AdminTokenAsync(def, ct);
        if (token is null)
        {
            // Not claimed yet: passwordless login gives the "initial admin" right until an admin password exists.
            var pl = await CallAsync(def.GamePort, "PasswordlessLogin", new { minimumPrivilegeLevel = "InitialAdmin" }, null, ct);
            var initial = pl.Ok ? pl.Data?["authenticationToken"]?.ToString() : null;
            if (initial is null)
            {
                log("Satisfactory: admin login refused. The server was claimed with another admin password: delete saves\\ServerSettings." + def.GamePort + ".sav (server stopped) to let EAM claim it again.");
                return;
            }
            var claim = await CallAsync(def.GamePort, "ClaimServer", new { serverName = def.ListName, adminPassword = def.TelnetPassword }, initial, ct);
            if (!claim.Ok) { log($"Satisfactory: claim failed ({claim.ErrorCode} {claim.ErrorMessage})"); return; }
            token = claim.Data?["authenticationToken"]?.ToString() ?? await AdminTokenAsync(def, ct);
            settings.Claimed = true;
            SatisfactoryConfig.Save(def, settings);
            log("Satisfactory: server claimed by EAM (admin password in EAM)");
            if (token is null) return;
        }
        await CallAsync(def.GamePort, "RenameServer", new { serverName = def.ListName }, token, ct);
        await CallAsync(def.GamePort, "SetClientPassword", new { password = settings.Password ?? "" }, token, ct);
    }

    /// <summary>Clean stop: saves the running session (one save file "&lt;session&gt;_eam", overwritten each time), then Shutdown.</summary>
    public static async Task<bool> SaveAndShutdownAsync(ServerDefinition def, Action<string> log)
    {
        try
        {
            var token = await AdminTokenAsync(def);
            if (token is null) { log("Satisfactory: admin login failed, the server will be stopped by force"); return false; }
            var state = await CallAsync(def.GamePort, "QueryServerState", null, token);
            var session = state.Data?["serverGameState"]?["activeSessionName"]?.ToString();
            if (state.Data?["serverGameState"]?["isGameRunning"]?.GetValue<bool>() == true && !string.IsNullOrWhiteSpace(session))
            {
                var save = await CallAsync(def.GamePort, "SaveGame", new { saveName = session + "_eam" }, token);
                log(save.Ok ? $"Satisfactory: game saved ({session}_eam)" : $"Satisfactory: save failed ({save.ErrorCode} {save.ErrorMessage})");
            }
            var r = await CallAsync(def.GamePort, "Shutdown", null, token);
            if (r.Ok) log("Satisfactory: shutdown asked through the API");
            return r.Ok;
        }
        catch (Exception e) { log("Satisfactory: API not reachable (" + e.Message + "), the server will be stopped by force"); return false; }
    }

    public static async Task<object> StateAsync(ServerDefinition def)
    {
        var token = await AdminTokenAsync(def);
        if (token is null) return new { answered = false };
        var r = await CallAsync(def.GamePort, "QueryServerState", null, token);
        var g = r.Data?["serverGameState"];
        return new
        {
            answered = r.Ok,
            players = g?["numConnectedPlayers"]?.GetValue<int>(),
            max = g?["playerLimit"]?.GetValue<int>(),
            session = g?["activeSessionName"]?.ToString(),
            techTier = g?["techTier"]?.GetValue<int>(),
            running = g?["isGameRunning"]?.GetValue<bool>(),
            paused = g?["isGamePaused"]?.GetValue<bool>(),
            tickRate = g?["averageTickRate"]?.GetValue<double>(),
            durationSeconds = g?["totalGameDuration"]?.GetValue<double>(),
        };
    }

    public static async Task<string> RunCommandAsync(ServerDefinition def, string command)
    {
        var token = await AdminTokenAsync(def) ?? throw new ServerException("satisfactory_admin_login");
        var r = await CallAsync(def.GamePort, "RunCommand", new { command }, token);
        if (!r.Ok) throw new ServerException("satisfactory_api", $"{r.ErrorCode} {r.ErrorMessage}");
        return r.Data?["commandResult"]?.ToString() ?? r.Data?.ToJsonString() ?? "";
    }

    /// <summary>
    /// The game always keeps its saves in the Windows profile of the account running it
    /// (%LocalAppData%\FactoryGame\Saved\SaveGames). EAM keeps them in the server's own folder instead:
    /// that path is a link (junction) to the server's saves folder, switched at each start. So only one
    /// Satisfactory server can run at a time on a machine; each keeps its own saves.
    /// A folder already there with files is renamed (kept), never deleted.
    /// </summary>
    public static void LinkSaves(ServerDefinition def, Action<string> log)
    {
        if (!OperatingSystem.IsWindows()) return;
        Directory.CreateDirectory(def.SatisfactorySavesDir);
        var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FactoryGame", "Saved");
        var link = Path.Combine(parent, "SaveGames");
        Directory.CreateDirectory(parent);
        var info = new DirectoryInfo(link);
        if (info.Exists)
        {
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                if (string.Equals(info.LinkTarget?.TrimEnd('\\'), def.SatisfactorySavesDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
                Directory.Delete(link, recursive: false); // removes the link only
            }
            else if (info.EnumerateFileSystemInfos().Any())
            {
                var kept = link + ".avant-eam-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                Directory.Move(link, kept);
                log($"Satisfactory: existing saves folder kept as {kept}");
            }
            else Directory.Delete(link);
        }
        var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "/c", "mklink", "/J", link, def.SatisfactorySavesDir }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        p.WaitForExit(10000);
        if (!Directory.Exists(link)) throw new ServerException("satisfactory_saves_link", p.StandardError.ReadToEnd().Trim());
        log($"Satisfactory: saves folder linked to {def.SatisfactorySavesDir}");
    }
}
