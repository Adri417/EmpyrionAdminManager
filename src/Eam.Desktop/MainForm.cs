using System.Diagnostics;
using Eam.Core;
using Eam.Core.Config;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Eam.Desktop;

/// <summary>
/// The desktop window: it shows exactly the same interface as the web version,
/// so every function is available in both.
/// </summary>
internal sealed class MainForm : Form
{
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(11, 18, 32) };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.FromArgb(142, 160, 189), BackColor = Color.FromArgb(11, 18, 32),
        Font = new Font("Segoe UI", 11f),
    };
    private readonly EngineLauncher _engine;
    private readonly EamPaths _paths;

    public MainForm(string[] args)
    {
        string? dataArg = null;
        for (var i = 0; i < args.Length - 1; i++) if (args[i] == "--data") dataArg = args[i + 1];
        _paths = EamPaths.Resolve(dataArg);
        _paths.EnsureCreated();
        var cfg = EamConfig.Load(_paths);
        _engine = new EngineLauncher(cfg.LocalPort);

        Text = AppInfo.Name;
        using (var s = typeof(MainForm).Assembly.GetManifestResourceStream("Eam.Desktop.eam.ico"))
            if (s is not null) Icon = new Icon(s);
        BackColor = Color.FromArgb(11, 18, 32);
        MinimumSize = new Size(900, 600);
        Size = new Size(1360, 860);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_web);
        Controls.Add(_status);
        _web.Visible = false;
        _status.Text = "Empyrion Admin Manager";
        Shown += async (_, _) => await StartAsync();
        FormClosing += OnClosing;
    }

    private async Task StartAsync()
    {
        var progress = new Progress<string>(m => _status.Text = m);
        var error = await _engine.EnsureRunningAsync(progress);
        if (error is not null)
        {
            _status.Text = error;
            return;
        }

        try
        {
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppInfo.ServiceName, "WebView2");
            var env = await CoreWebView2Environment.CreateAsync(null, userData);
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            _status.Text = "Le composant Microsoft Edge WebView2 est absent. Installez-le depuis\n" +
                           "https://go.microsoft.com/fwlink/p/?LinkId=2124703\n\n" +
                           "Microsoft Edge WebView2 Runtime is missing (link above). " +
                           "You can also open " + _engine.BaseUri + " in a browser.";
            return;
        }

        var core = _web.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsZoomControlEnabled = true;
        // Links leaving the interface open in the normal browser.
        core.NewWindowRequested += (_, e) => { e.Handled = true; OpenExternal(e.Uri); };
        core.NavigationStarting += (_, e) =>
        {
            if (!e.Uri.StartsWith(_engine.BaseUri.ToString(), StringComparison.OrdinalIgnoreCase)) { e.Cancel = true; OpenExternal(e.Uri); }
        };
        core.DocumentTitleChanged += (_, _) => Text = core.DocumentTitle;
        _web.Source = _engine.BaseUri;
        _web.Visible = true;
        _status.Visible = false;
    }

    private static void OpenExternal(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var u) && (u.Scheme == "https" || u.Scheme == "http"))
            Process.Start(new ProcessStartInfo(u.ToString()) { UseShellExecute = true });
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        // A portable engine started by this window keeps running only if the admin wants it to.
        if (_engine.StartedProcess is { HasExited: false } p)
        {
            var answer = MessageBox.Show(this,
                "Arrêter aussi le moteur EAM ? Sans lui, les tâches programmées et l'accès web s'arrêtent.\n\n" +
                "Also stop the EAM engine? Without it, scheduled tasks and web access stop.",
                AppInfo.Name, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) { e.Cancel = true; return; }
            if (answer == DialogResult.Yes)
            {
                try { p.Kill(entireProcessTree: true); p.WaitForExit(5000); } catch { }
            }
        }
    }
}
