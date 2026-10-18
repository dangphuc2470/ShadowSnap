using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace MacWindowSnap;

internal static class ToastHelper
{
    public const string AppId = "MacWindowSnap";

    static ToastHelper()
    {
        EnsureAppRegistered();
    }

    public static void EnsureAppRegistered()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}");
            if (key != null)
            {
                key.SetValue("DisplayName", "MacWindowSnap");
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(exePath))
                {
                    key.SetValue("IconUri", exePath);
                }
            }
        }
        catch
        {
            // Ignore registry write errors
        }
    }

    public static void ShowScreenshotToast(string imageFilePath)
    {
        try
        {
            if (string.IsNullOrEmpty(imageFilePath) || !File.Exists(imageFilePath))
            {
                return;
            }

            string fileUri = new Uri(imageFilePath).AbsoluteUri;

            string toastXmlString = $"""
            <toast duration="short">
              <visual>
                <binding template="ToastGeneric">
                  <image placement="hero" src="{fileUri}"/>
                  <text>Screenshot copied to clipboard</text>
                  <text>Automatically saved to screenshots folder.</text>
                </binding>
              </visual>
              <actions>
                <action content="Markup and share" arguments="markup" activationType="foreground"/>
              </actions>
            </toast>
            """;

            XmlDocument xmlDoc = new();
            xmlDoc.LoadXml(toastXmlString);

            ToastNotification toast = new(xmlDoc);

            toast.Activated += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(imageFilePath)
                    {
                        UseShellExecute = true
                    });
                }
                catch
                {
                    Process.Start("explorer.exe", $"/select,\"{imageFilePath}\"");
                }
            };

            try
            {
                ToastNotificationManager.CreateToastNotifier(AppId).Show(toast);
            }
            catch
            {
                ToastNotificationManager.CreateToastNotifier().Show(toast);
            }
        }
        catch
        {
            // Silently ignore if Windows Notification Service is disabled
        }
    }
}
