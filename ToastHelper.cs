using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace ShadowSnap;

internal static class ToastHelper
{
    public const string AppId = "ShadowSnap";

    static ToastHelper()
    {
        EnsureAppRegistered();
        EnsureStartMenuShortcut();
    }

    public static void EnsureAppRegistered()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{AppId}");
            if (key != null)
            {
                key.SetValue("DisplayName", "ShadowSnap");
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string logoPath = Path.Combine(baseDir, "assets", "logo.png");
                if (File.Exists(logoPath))
                {
                    key.SetValue("IconUri", logoPath);
                }
                else
                {
                    string icoPath = Path.Combine(baseDir, "assets", "app.ico");
                    if (File.Exists(icoPath))
                    {
                        key.SetValue("IconUri", icoPath);
                    }
                }
                key.SetValue("IconBackgroundColor", "0");
                key.SetValue("ShowInSettings", 1, RegistryValueKind.DWord);
            }
        }
        catch
        {
            // Ignore registry write errors
        }
    }

    public static void EnsureStartMenuShortcut()
    {
        try
        {
            string programsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            string shortcutPath = Path.Combine(programsFolder, "ShadowSnap.lnk");
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
            {
                return;
            }

            string baseDir = Path.GetDirectoryName(exePath) ?? AppDomain.CurrentDomain.BaseDirectory;
            string iconPath = Path.Combine(baseDir, "assets", "app.ico");
            if (!File.Exists(iconPath))
            {
                iconPath = exePath;
            }

            IShellLinkW link = (IShellLinkW)new CShellLink();
            link.SetPath(exePath);
            link.SetWorkingDirectory(baseDir);
            link.SetDescription("ShadowSnap (Ctrl + Shift + A)");
            link.SetIconLocation(iconPath, 0);

            IPropertyStore store = (IPropertyStore)link;
            PropertyKey pkey = new(new Guid("9F4C2855-9F79-4BDE-9AE3-E1F60D02F2A1"), 5);
            PropVariant pv = PropVariant.FromString(AppId);
            store.SetValue(ref pkey, ref pv);
            store.Commit();

            IPersistFile persistFile = (IPersistFile)link;
            persistFile.Save(shortcutPath, true);
        }
        catch
        {
            // Ignore shortcut creation errors
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
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string logoPath = Path.Combine(baseDir, "assets", "logo.png");
            string appLogoXml = string.Empty;
            if (File.Exists(logoPath))
            {
                string logoUri = new Uri(logoPath).AbsoluteUri;
                appLogoXml = $"""<image placement="appLogoOverride" hint-crop="none" src="{logoUri}"/>""";
            }

            string toastXmlString = $"""
            <toast duration="short">
              <visual>
                <binding template="ToastGeneric">
                  {appLogoXml}
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

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    [ClassInterface(ClassInterfaceType.None)]
    private class CShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint cProps);
        void GetAt(uint iProp, out PropertyKey pkey);
        void GetValue(ref PropertyKey key, out PropVariant pv);
        void SetValue(ref PropertyKey key, ref PropVariant pv);
        void Commit();
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder ppszFileName);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PropertyKey
    {
        public Guid fmtid;
        public uint pid;

        public PropertyKey(Guid guid, uint id)
        {
            fmtid = guid;
            pid = id;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)]
        public ushort vt;

        [FieldOffset(8)]
        public IntPtr pwszVal;

        public static PropVariant FromString(string val)
        {
            return new PropVariant
            {
                vt = 31, // VT_LPWSTR
                pwszVal = Marshal.StringToCoTaskMemUni(val)
            };
        }
    }
}
