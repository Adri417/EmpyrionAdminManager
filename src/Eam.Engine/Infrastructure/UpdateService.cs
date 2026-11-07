using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Eam.Core;
using Eam.Core.Config;
using Eam.Engine.Servers;
using Microsoft.Extensions.Hosting.WindowsServices;

namespace Eam.Engine.Infrastructure;

/// <summary>Settings of the program update, kept in data\update.json.</summary>
public sealed class UpdateSettings
{
    /// <summary>Look for a new version on GitHub every hour.</summary>
    public bool Check { get; set; } = true;
    /// <summary>GitHub repository that publishes the releases ("owner/name").</summary>
    public string Repository { get; set; } = "Adri417/EmpyrionAdminManager";
}

/// <summary>
/// Looks for a newer release on GitHub every hour and installs it on request.
/// The update replaces the program files only: the data folder (servers, databases,
/// settings) is never touched. The game servers keep running during the update.
/// </summary>
public sealed class UpdateService(EamPaths paths, IHttpClientFactory http, ServerManager servers, IHostApplicationLifetime life, ILogger<UpdateService> log)
    : BackgroundService
{
    private readonly object _gate = new();
    private string? _latest, _notes, _pageUrl, _zipUrl, _error;
    private DateTime? _checkedUtc;
    private string _state = "idle";

    private string SettingsFile => Path.Combine(paths.DataRoot, "update.json");
    private string WorkDir => Path.Combine(paths.DataRoot, "temp", "update");

    public UpdateSettings Settings()
    {
        try { if (File.Exists(SettingsFile)) return JsonSerializer.Deserialize<UpdateSettings>(File.ReadAllText(SettingsFile), Json) ?? new(); }
        catch (Exception e) { log.LogWarning("update.json unreadable: {Error}", e.Message); }
        return new();
    }

    public void Save(UpdateSettings s)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(s.Repository ?? "", @"^[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100}$"))
            throw new ServerException("invalid_repository");
        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(s, Json));
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static bool IsNewer(string? latest, string current) =>
        Parse(latest) is { } l && Parse(current) is { } c && l > c;

    private static Version? Parse(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(v, out var r) ? r : null;
    }

    public object Status()
    {
        lock (_gate)
            return new
            {
                current = AppInfo.Version, latest = _latest, available = IsNewer(_latest, AppInfo.Version) && _zipUrl is not null,
                notes = _notes, url = _pageUrl, checkedUtc = _checkedUtc, state = _state, error = _error,
                check = Settings().Check, canApply = OperatingSystem.IsWindows(),
            };
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stop); } catch (OperationCanceledException) { return; }
        while (!stop.IsCancellationRequested)
        {
            if (Settings().Check)
            {
                try { await CheckAsync(stop); }
                catch (Exception e) when (e is not OperationCanceledException) { log.LogInformation("Update check failed: {Error}", e.Message); }
            }
            try { await Task.Delay(TimeSpan.FromHours(1), stop); } catch (OperationCanceledException) { return; }
        }
    }

    public async Task<object> CheckAsync(CancellationToken ct = default)
    {
        var repo = Settings().Repository;
        using var client = http.CreateClient("web");
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
        req.Headers.UserAgent.ParseAdd($"EmpyrionAdminManager/{AppInfo.Version}");
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var res = await client.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode)
        {
            lock (_gate) { _checkedUtc = DateTime.UtcNow; _error = $"GitHub: {(int)res.StatusCode}"; }
            return Status();
        }
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        string? zip = null;
        if (root.TryGetProperty("assets", out var assets))
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                if (name.StartsWith("EmpyrionAdminManager-", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("source", StringComparison.OrdinalIgnoreCase))
                    zip = a.GetProperty("browser_download_url").GetString();
            }
        lock (_gate)
        {
            _latest = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V');
            _notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;
            _pageUrl = root.TryGetProperty("html_url", out var u) ? u.GetString() : null;
            _zipUrl = zip;
            _checkedUtc = DateTime.UtcNow;
            if (_state != "installing") _error = null;
        }
        if (IsNewer(_latest, AppInfo.Version)) log.LogInformation("Version {Latest} of EAM is available (this one: {Current})", _latest, AppInfo.Version);
        return Status();
    }

    /// <summary>Downloads the new version, then hands over to a small script that swaps the files and restarts EAM.</summary>
    public async Task<object> ApplyAsync(string by)
    {
        if (!OperatingSystem.IsWindows()) throw new ServerException("update_windows_only");
        lock (_gate)
        {
            if (_state is "downloading" or "installing") throw new ServerException("update_running");
            if (_zipUrl is null || !IsNewer(_latest, AppInfo.Version)) throw new ServerException("no_update");
            _state = "downloading"; _error = null;
        }
        try
        {
            if (servers.AnyBusy()) throw new ServerException("update_servers_busy");
            if (Directory.Exists(WorkDir)) Directory.Delete(WorkDir, true);
            Directory.CreateDirectory(WorkDir);
            var zipFile = Path.Combine(WorkDir, "update.zip");
            using (var client = http.CreateClient("steam"))
            using (var req = new HttpRequestMessage(HttpMethod.Get, _zipUrl))
            {
                req.Headers.UserAgent.ParseAdd($"EmpyrionAdminManager/{AppInfo.Version}");
                using var res = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                res.EnsureSuccessStatusCode();
                await using var fs = File.Create(zipFile);
                await res.Content.CopyToAsync(fs);
            }
            var extract = Path.Combine(WorkDir, "new");
            ZipFile.ExtractToDirectory(zipFile, extract);
            var engine = Directory.EnumerateFiles(extract, "EamEngine.exe", SearchOption.AllDirectories).FirstOrDefault()
                         ?? throw new ServerException("update_bad_package");
            var source = Path.GetDirectoryName(engine)!;
            var target = AppContext.BaseDirectory.TrimEnd('\\', '/');
            var script = Path.Combine(WorkDir, "update.ps1");
            File.WriteAllText(script, Script(source, target, Path.Combine(WorkDir, "previous"), Path.Combine(paths.LogsDir, "update.log"),
                Environment.ProcessId, ServiceMode()), new UTF8Encoding(true));
            log.LogWarning("{By} starts the update of EAM {Current} to {Latest}", by, AppInfo.Version, _latest);
            lock (_gate) _state = "installing";
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = WorkDir,
            });
            // Let the answer reach the browser, then stop: the script waits for this process to end.
            _ = Task.Run(async () => { await Task.Delay(2000); life.StopApplication(); });
            return Status();
        }
        catch (Exception e)
        {
            lock (_gate) { _state = "idle"; _error = e is ServerException se ? se.Code : e.Message; }
            log.LogWarning("Update failed: {Error}", e.Message);
            throw;
        }
    }

    private static bool ServiceMode() => WindowsServiceHelpers.IsWindowsService();

    private static string Q(string s) => "'" + s.Replace("'", "''") + "'";

    /// <summary>
    /// PowerShell updater. Waits for the engine to stop, closes the EAM window, keeps a copy of the
    /// current program, copies the new files over it (never the data folder) and starts EAM again.
    /// If the copy fails, the previous program is put back.
    /// </summary>
    internal static string Script(string source, string target, string previous, string logFile, int pid, bool service) => $$"""
        $ErrorActionPreference = 'Continue'
        $src = {{Q(source)}}; $dst = {{Q(target)}}; $prev = {{Q(previous)}}; $log = {{Q(logFile)}}
        function Log($m) { Add-Content -Path $log -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' [update] ' + $m) }
        Log "Update from $src to $dst"
        $p = Get-Process -Id {{pid}} -ErrorAction SilentlyContinue
        if ($p) { $p.WaitForExit(120000) | Out-Null }
        $service = {{(service ? "$true" : "$false")}}
        if ($service) { Stop-Service -Name '{{AppInfo.ServiceName}}' -Force -ErrorAction SilentlyContinue; Start-Sleep -Seconds 2 }
        $desktop = $false
        foreach ($name in 'EmpyrionAdminManager', 'EamEngine') {
          Get-Process -Name $name -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($dst, [StringComparison]::OrdinalIgnoreCase) } | ForEach-Object {
            if ($name -eq 'EmpyrionAdminManager') { $desktop = $true }
            Log "Closing $name ($($_.Id))"; Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue; $_.WaitForExit(30000) | Out-Null }
        }
        Start-Sleep -Seconds 1
        robocopy $dst $prev /E /XD (Join-Path $dst 'data') /NFL /NDL /NJH /NJS /R:2 /W:1 | Out-Null
        robocopy $src $dst /E /XD (Join-Path $src 'data') /XF portable.txt /NFL /NDL /NJH /NJS /R:5 /W:2 | Out-Null
        $code = $LASTEXITCODE
        if ($code -ge 8) {
          Log "Copy failed (robocopy $code): the previous version is put back"
          robocopy $prev $dst /E /NFL /NDL /NJH /NJS /R:5 /W:2 | Out-Null
        } else { Log "Files copied" }
        if ($service) { Start-Service -Name '{{AppInfo.ServiceName}}' -ErrorAction SilentlyContinue; Log "Service started" }
        elseif ($desktop) { Start-Process -FilePath (Join-Path $dst 'EmpyrionAdminManager.exe') -WorkingDirectory $dst; Log "Window started" }
        else { Start-Process -FilePath (Join-Path $dst 'EamEngine.exe') -WorkingDirectory $dst -WindowStyle Hidden; Log "Engine started" }
        Log "Update finished"
        """;
}
