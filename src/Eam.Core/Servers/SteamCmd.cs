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
            }
        }
        finally { Gate.Release(); }
    }

    public static string? ReadBuildId(string installDir)
    {
        var manifest = Path.Combine(installDir, "steamapps", $"appmanifest_{EmpyrionDedicatedAppId}.acf");
        if (!File.Exists(manifest)) return null;
        var m = BuildIdRegex().Match(File.ReadAllText(manifest));
        return m.Success ? m.Groups[1].Value : null;
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
