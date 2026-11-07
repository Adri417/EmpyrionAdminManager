using System.Diagnostics;
using System.Net.Http;
using System.ServiceProcess;
using Eam.Core;

namespace Eam.Desktop;

/// <summary>
/// Makes sure the EAM engine is running before the window shows the interface.
/// Installed mode: the engine is a Windows service. Portable/test mode: the
/// window starts EamEngine.exe from its own folder.
/// </summary>
internal sealed class EngineLauncher(int port)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2) };
    public Uri BaseUri { get; } = new($"http://127.0.0.1:{port}/");
    public Process? StartedProcess { get; private set; }

    public async Task<bool> IsRunningAsync()
    {
        try
        {
            using var r = await Http.GetAsync(new Uri(BaseUri, "api/status"));
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public static bool ServiceInstalled()
    {
        try { return ServiceController.GetServices().Any(s => s.ServiceName == AppInfo.ServiceName); }
        catch { return false; }
    }

    /// <summary>Returns null on success, otherwise a message for the user.</summary>
    public async Task<string?> EnsureRunningAsync(IProgress<string> progress)
    {
        if (await IsRunningAsync()) return null;

        if (ServiceInstalled())
        {
            progress.Report("Démarrage du service EAM… / Starting the EAM service…");
            try
            {
                using var sc = new ServiceController(AppInfo.ServiceName);
                if (sc.Status != ServiceControllerStatus.Running && sc.Status != ServiceControllerStatus.StartPending)
                    sc.Start();
            }
            catch (Exception e)
            {
                return "Le service EAM est arrêté et n'a pas pu être démarré (droits administrateur nécessaires).\n" +
                       "The EAM service is stopped and could not be started (administrator rights needed).\n\n" + e.Message;
            }
        }
        else
        {
            var exe = Path.Combine(AppContext.BaseDirectory, "EamEngine.exe");
            if (!File.Exists(exe)) return "EamEngine.exe introuvable / not found:\n" + exe;
            progress.Report("Démarrage du moteur EAM… / Starting the EAM engine…");
            StartedProcess = Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory,
            });
        }

        for (var i = 0; i < 60; i++)
        {
            if (await IsRunningAsync()) return null;
            if (StartedProcess is { HasExited: true })
                return $"Le moteur EAM s'est arrêté (code {StartedProcess.ExitCode}). Consultez le dossier logs.\n" +
                       $"The EAM engine stopped (code {StartedProcess.ExitCode}). See the logs folder.";
            await Task.Delay(500);
        }
        return "Le moteur EAM ne répond pas. / The EAM engine does not respond.";
    }
}
