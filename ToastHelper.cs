using System.Diagnostics;
using System.IO;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace MacWindowSnap;

internal static class ToastHelper
{
    private const string SnippingToolAppId = "Microsoft.ScreenSketch_8wekyb3d8bbwe!App";

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

            toast.Activated += (_, args) =>
            {
                try
                {
                    // Open image in default editor / viewer
                    Process.Start(new ProcessStartInfo(imageFilePath)
                    {
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // Fallback to revealing file in Explorer
                    Process.Start("explorer.exe", $"/select,\"{imageFilePath}\"");
                }
            };

            // Try creating toast with Snipping Tool identity for 100% authentic Snipping Tool look
            try
            {
                ToastNotificationManager.CreateToastNotifier(SnippingToolAppId).Show(toast);
                return;
            }
            catch
            {
                // Fallback to default notifier if Snipping Tool app ID is not registered
                ToastNotificationManager.CreateToastNotifier().Show(toast);
            }
        }
        catch
        {
            // Silently ignore if Windows Notification Service is disabled
        }
    }
}
