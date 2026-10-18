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
                "MacWindowSnap đang chạy ngầm dưới khay hệ thống (System Tray) rồi nghen m!\nBấm Ctrl + Shift + A để chụp cửa sổ.",
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
            MessageBox.Show($"Lỗi ứng dụng: {ex.Message}", "MacWindowSnap", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}