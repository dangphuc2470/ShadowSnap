using System.Threading;
using System.Windows.Forms;

namespace ShadowSnap;

internal static class Program
{
    private const string AppMutexName = "ShadowSnap_SingleInstance_Mutex_2026";

    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && (args[0] == "--mcp" || args[0] == "mcp"))
        {
            McpServer.Run();
            return;
        }

        // Explicitly declare process AUMID so Windows Notification Center displays the app icon in the header
        NativeMethods.SetCurrentProcessExplicitAppUserModelID(ToastHelper.AppId);
        ToastHelper.EnsureAppRegistered();
        ToastHelper.EnsureStartMenuShortcut();

        if (args.Length > 0 && args[0] == "--test-toast")
        {
            string samplePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "demo-result.png");
            ToastHelper.ShowScreenshotToast(samplePath);
            Thread.Sleep(2000);
            return;
        }

        using Mutex mutex = new(true, AppMutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "ShadowSnap is already running in the system tray.\nPress Ctrl + Shift + A to capture a window.",
                "ShadowSnap",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApplicationContext());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Application error: {ex.Message}", "ShadowSnap", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}