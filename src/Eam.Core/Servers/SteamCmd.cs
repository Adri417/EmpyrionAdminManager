using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace Eam.Core.Servers;

public sealed record SteamProgress(string Stage, double Percent, string Line);

/// <summary>
/// EAM's own SteamCMD. Downloaded on first use into the data folder, then used to
/// install, update and verify the Empyrion dedicated server (Steam app 530870).
/// Only one SteamCMD run at a time: SteamCMD does not support parallel runs.
/// </summary>
public sealed partial class SteamCmd(string steamCmdDir, HttpClient http)
{
    public const string EmpyrionDedicatedAppId = "530870";
    public const string DownloadUrl = "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public string Exe => Path.Combine(steamCmdDir, OperatingSystem.IsWindows() ? "steamcmd.exe" : "steamcmd.sh");
    public bool IsInstalled => File.Exists(Exe);

    [GeneratedRegex(@"Update state \(0x[0-9a-fA-F]+\) ([\w ]+?), progress: ([\d.]+)", RegexOptions.CultureInvariant)]
    private static partial Regex ProgressRegex();

    [GeneratedRegex("\"buildid\"\\s+\"(\\d+)\"")]
    private static partial Regex BuildIdRegex();

    public async Task EnsureInstalledAsync(IProgress<SteamProgress>? progress, CancellationToken ct)
    {
        if (IsInstalled) return;
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("steamcmd_windows_only");
        Directory.CreateDirectory(steamCmdDir);
        progress?.Report(new SteamProgress("download_steamcmd", 0, "Downloading SteamCMD"));
        var zip = Path.Combine(steamCmdDir, "steamcmd.zip.tmp");
        await using (var src = await http.GetStreamAsync(DownloadUrl, ct))
        await using (var dst = File.Create(zip))
            await src.CopyToAsync(dst, ct);
        ZipFile.ExtractToDirectory(zip, steamCmdDir, overwriteFiles: true);
        File.Delete(zip);
        progress?.Report(new SteamProgress("download_steamcmd", 100, "SteamCMD ready"));
    }

    /// <summary>Installs or updates the game into <paramref name="installDir"/>. Returns the Steam build id.</summary>
    public async Task<string?> InstallOrUpdateAsync(string installDir, string branch, bool validate,
        IProgress<SteamProgress>? progress, Action<string>? log, CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try
        {
            await EnsureInstalledAsync(progress, ct);
            Directory.CreateDirectory(installDir);
            var args = new List<string> { "+force_install_dir", installDir, "+login", "anonymous", "+app_update", EmpyrionDedicatedAppId };
            if (!string.IsNullOrWhiteSpace(branch) && branch != "public") { args.Add("-beta"); args.Add(branch); }
            if (validate) args.Add("validate");
            args.Add("+quit");

            // SteamCMD updates itself on its first runs and may exit early: try up to 3 times.
            for (var attempt = 1; ; attempt++)
            {
                var (code, success, lastError) = await RunAsync(args, progress, log, ct);
                if (success) return ReadBuildId(installDir);
                if (attempt >= 3)
                    throw new InvalidOperationException(lastError ?? $"steamcmd_exit_{code}");
                log?.Invoke($"SteamCMD exited with code {code}, retrying ({attempt}/3)");
                // "Failed to get manifest request code, 'Access Denied'" / "state 0x6": a stale SteamCMD cache or a
                // half-done download from an earlier attempt. Start the next attempt from a clean state.
                ClearCaches(installDir, log);
            }
        }
        finally { Gate.Release(); }
    }

    /// <summary>Latest build of the dedicated server on Steam, per branch ("public", "experimental"), from the last check.</summary>
    public IReadOnlyDictionary<string, string> LatestBuilds { get; private set; } = new Dictionary<string, string>();
    public DateTime? LatestCheckedUtc { get; private set; }

    /// <summary>Asks Steam which build of the dedicated server is the newest (does not download anything).</summary>
    public async Task<IReadOnlyDictionary<string, string>> CheckLatestBuildsAsync(CancellationToken ct)
    {
        if (!IsInstalled) return LatestBuilds;
        await Gate.WaitAsync(ct);
        try
        {
            var sb = new System.Text.StringBuilder();
            var args = new List<string> { "+login", "anonymous", "+app_info_update", "1", "+app_info_print", EmpyrionDedicatedAppId, "+quit" };
            await RunAsync(args, null, line => sb.AppendLine(line), ct);
            var found = ParseBranchBuilds(sb.ToString());
            if (found.Count > 0) { LatestBuilds = found; LatestCheckedUtc = DateTime.UtcNow; }
            return LatestBuilds;
        }
        finally { Gate.Release(); }
    }

    /// <summary>Reads the "branches" block of app_info_print: branch name → buildid (public for tests).</summary>
    public static Dictionary<string, string> ParseBranchBuilds(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var start = text.IndexOf("\"branches\"", StringComparison.OrdinalIgnoreCase);
        if (start < 0) return result;
        foreach (Match m in BranchBuild().Matches(text[start..]))
            result.TryAdd(m.Groups[1].Value, m.Groups[2].Value);
        return result;
    }

    [GeneratedRegex("\"([A-Za-z0-9_.-]+)\"\\s*\\{\\s*\"buildid\"\\s*\"(\\d+)\"")]
    private static partial Regex BranchBuild();

    /// <summary>Empyrion's Steam app (the game, not the dedicated server): owner of the Workshop items.</summary>
    public const string EmpyrionGameAppId = "383120";

    /// <summary>
    /// Downloads (or updates) a Steam Workshop item anonymously into <paramref name="downloadDir"/>.
    /// Returns the folder holding the item, or throws with SteamCMD's error.
    /// </summary>
    public async Task<string> DownloadWorkshopItemAsync(string downloadDir, string itemId, IProgress<SteamProgress>? progress,
        Action<string>? log, CancellationToken ct)
    {
        if (!itemId.All(char.IsDigit) || itemId.Length is < 3 or > 20) throw new ArgumentException("invalid_workshop_id");
        await Gate.WaitAsync(ct);
        try
        {
            await EnsureInstalledAsync(progress, ct);
            Directory.CreateDirectory(downloadDir);
            var args = new List<string> { "+force_install_dir", downloadDir, "+login", "anonymous",
                "+workshop_download_item", EmpyrionGameAppId, itemId, "validate", "+quit" };
            string? lastError = null;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                var ok = false;
                var (code, _, err) = await RunAsync(args, progress, line =>
                {
                    log?.Invoke(line);
                    if (line.Contains("Success. Downloaded item", StringComparison.Ordinal)) ok = true;
                    var e = line.IndexOf("ERROR!", StringComparison.Ordinal);
                    if (e >= 0) lastError = line[e..].Trim();
                }, ct);
                var folder = WorkshopItemDir(downloadDir, itemId);
                if (ok && Directory.Exists(folder)) return folder;
                lastError ??= err ?? $"steamcmd_exit_{code}";
                log?.Invoke($"Workshop download failed ({lastError}), attempt {attempt}/3");
            }
            throw new InvalidOperationException(lastError ?? "workshop_download_failed");
        }
        finally { Gate.Release(); }
    }

    public static string WorkshopItemDir(string root, string itemId) =>
        Path.Combine(root, "steamapps", "workshop", "content", EmpyrionGameAppId, itemId);

    public static string? ReadBuildId(string installDir)
    {
        var manifest = Path.Combine(installDir, "steamapps", $"appmanifest_{EmpyrionDedicatedAppId}.acf");
        if (!File.Exists(manifest)) return null;
        var m = BuildIdRegex().Match(File.ReadAllText(manifest));
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// Removes SteamCMD's caches (app info, depot manifests) and the unfinished download of the game.
    /// Nothing of the installed game or its saves is touched.
    /// </summary>
    private void ClearCaches(string installDir, Action<string>? log)
    {
        foreach (var dir in new[]
        {
            Path.Combine(steamCmdDir, "appcache"),
            Path.Combine(steamCmdDir, "depotcache"),
            Path.Combine(installDir, "steamapps", "downloading"),
            Path.Combine(installDir, "steamapps", "temp"),
        })
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                Directory.Delete(dir, recursive: true);
                log?.Invoke("Cleared " + dir);
            }
            catch (Exception e) { log?.Invoke($"Could not clear {dir}: {e.Message}"); }
        }
    }

    private async Task<(int code, bool success, string? error)> RunAsync(List<string> args, IProgress<SteamProgress>? progress, Action<string>? log, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(Exe)
        {
            WorkingDirectory = steamCmdDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var success = false;
        string? lastError = null;
        void OnLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            log?.Invoke(line);
            var info = Parse(line);
            if (info.success) success = true;
            if (info.error is not null) lastError = info.error;
            if (info.progress is not null) progress?.Report(info.progress);
        }
        p.OutputDataReceived += (_, e) => OnLine(e.Data);
        p.ErrorDataReceived += (_, e) => OnLine(e.Data);
        p.Start();
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        return (p.ExitCode, success, lastError);
    }

    /// <summary>Reads one SteamCMD output line (public for tests).</summary>
    public static (SteamProgress? progress, bool success, string? error) Parse(string line)
    {
        var m = ProgressRegex().Match(line);
        if (m.Success && double.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pct))
            return (new SteamProgress(m.Groups[1].Value.Trim(), Math.Clamp(pct, 0, 100), line), false, null);
        if (line.Contains("Success! App", StringComparison.Ordinal) && line.Contains("fully installed", StringComparison.Ordinal))
            return (new SteamProgress("done", 100, line), true, null);
        var err = line.IndexOf("ERROR!", StringComparison.Ordinal);
        if (err >= 0) return (null, false, line[err..].Trim());
        return (null, false, null);
    }
}
