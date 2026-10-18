using System.Threading;
using System.Windows.Forms;

namespace MacWindowSnap;

internal static class Program
{
    private const string AppMutexName = "MacWindowSnap_SingleInstance_Mutex_2026";

    [STAThread]
    private static void Main()
    {
        using Mutex mutex = new(true, AppMutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "MacWindowSnap is already running in the system tray.\nPress Ctrl + Shift + A to capture a window.",
                "MacWindowSnap",
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
            MessageBox.Show($"Application error: {ex.Message}", "MacWindowSnap", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}