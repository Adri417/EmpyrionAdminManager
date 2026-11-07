using Eam.Core;

namespace Eam.Desktop;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // One window at a time: a second launch just exits.
        using var mutex = new Mutex(true, @"Local\EmpyrionAdminManager.Desktop", out var first);
        if (!first) return;

        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
            MessageBox.Show(e.Exception.Message, AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        Application.Run(new MainForm(args));
    }
}
