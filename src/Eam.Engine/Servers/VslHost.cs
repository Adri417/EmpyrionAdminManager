using System.Diagnostics;
using Eam.Core.Servers;

namespace Eam.Engine.Servers;

/// <summary>
/// Vita Somnium Life has no remote console: commands ("stop") are typed in its console input.
/// On Windows EAM starts the game through a small PowerShell host, a separate process that keeps the game's
/// console input open even when EAM itself restarts (update, service restart). It runs from Windows' own
/// PowerShell, so it never locks EAM's files during an update. EAM appends commands to a file; the host types
/// each new line in the game's console.
/// </summary>
public static class VslHost
{
    public static string CommandFile(ServerDefinition def) => Path.Combine(def.EamDir, "vsl-console.txt");
    public static string PidFile(ServerDefinition def) => Path.Combine(def.EamDir, "vsl-host.pid");
    private static string ScriptFile(ServerDefinition def) => Path.Combine(def.EamDir, "vsl-host.ps1");

    private const string Script = """
        param([string]$CmdFile, [string]$PidFile, [string]$Exe, [string]$LogFile)
        # Easy Admin Manager: Vita Somnium Life console host. Starts the game, then types every new line of
        # $CmdFile in its console. Ends with the game.
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $Exe
        $psi.WorkingDirectory = Split-Path -Parent $Exe
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        $psi.RedirectStandardInput = $true
        $psi.Arguments = '-batchmode -nographics -logFile "' + $LogFile + '"'
        $p = [System.Diagnostics.Process]::Start($psi)
        Set-Content -LiteralPath $PidFile -Value ("$PID " + $p.Id) -Encoding ASCII
        $offset = 0
        $pending = ''
        while (-not $p.WaitForExit(500)) {
          try {
            $fs = [System.IO.File]::Open($CmdFile, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::Read, [System.IO.FileShare]'ReadWrite, Delete')
            try {
              if ($fs.Length -lt $offset) { $offset = 0 }
              if ($fs.Length -gt $offset) {
                [void]$fs.Seek($offset, [System.IO.SeekOrigin]::Begin)
                $buf = New-Object byte[] ($fs.Length - $offset)
                $n = $fs.Read($buf, 0, $buf.Length)
                $offset += $n
                $pending += [System.Text.Encoding]::UTF8.GetString($buf, 0, $n)
                $i = $pending.LastIndexOf("`n")
                if ($i -ge 0) {
                  foreach ($line in $pending.Substring(0, $i).Split("`n")) {
                    $t = $line.Trim()
                    if ($t.Length -gt 0) { $p.StandardInput.WriteLine($t); $p.StandardInput.Flush() }
                  }
                  $pending = $pending.Substring($i + 1)
                }
              }
            } finally { $fs.Dispose() }
          } catch { }
        }
        Remove-Item -LiteralPath $PidFile -ErrorAction SilentlyContinue
        """;

    /// <summary>
    /// Starts the game. On Windows through the host (returns the host process, <paramref name="direct"/> false);
    /// elsewhere directly with its console input kept by EAM (development only).
    /// </summary>
    public static Process Start(ServerDefinition def, string exe, string logFile, out bool direct)
    {
        Directory.CreateDirectory(def.EamDir);
        File.WriteAllText(CommandFile(def), "");
        try { File.Delete(PidFile(def)); } catch { }
        if (OperatingSystem.IsWindows())
        {
            File.WriteAllText(ScriptFile(def), Script);
            var ps = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var psi = new ProcessStartInfo(File.Exists(ps) ? ps : "powershell.exe") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = def.GameDir };
            foreach (var a in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", ScriptFile(def),
                         "-CmdFile", CommandFile(def), "-PidFile", PidFile(def), "-Exe", exe, "-LogFile", logFile })
                psi.ArgumentList.Add(a);
            direct = false;
            return Process.Start(psi) ?? throw new ServerException("start_failed");
        }
        var g = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, WorkingDirectory = def.GameDir };
        foreach (var a in new[] { "-batchmode", "-nographics", "-logFile", logFile }) g.ArgumentList.Add(a);
        direct = true;
        return Process.Start(g) ?? throw new ServerException("start_failed");
    }

    /// <summary>True when a host of this server is alive (it can type in the game's console).</summary>
    public static bool IsAlive(ServerDefinition def)
    {
        try
        {
            if (!File.Exists(PidFile(def))) return false;
            var pid = int.Parse(File.ReadAllText(PidFile(def)).Trim().Split(' ')[0]);
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch { return false; }
    }

    /// <summary>Queues a line for the game's console. False when no host is alive.</summary>
    public static bool Type(ServerDefinition def, string line)
    {
        if (!IsAlive(def)) return false;
        try { File.AppendAllText(CommandFile(def), line.Replace("\r", "").Replace("\n", " ") + "\n"); return true; }
        catch { return false; }
    }
}
