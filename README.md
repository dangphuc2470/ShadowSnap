# MacWindowSnap

A lightweight, macOS-style window capture utility for Windows featuring automatic transparent padding, interactive window detection, and authentic DWM drop shadow extraction.

![Interactive Window Detection](assets/demo-hover.png)

![Captured Result with Authentic Shadow](assets/demo-result.png)

## Key Features

- **Global Hotkey**: Runs silently in the system tray. Press `Ctrl + Shift + A` anytime to trigger window selection.
- **Interactive Window Detection**: Move your cursor over any window to automatically highlight it with a rounded macOS-style accent border and a floating pill badge showing application title and pixel dimensions (`W x H`).
- **Authentic DWM Drop Shadow**: Employs multi-pass background compositing and per-pixel alpha channel extraction to capture the genuine Windows DWM drop shadow with smooth corner rounding.
- **Drag-to-Select Any Dialog or Region**: For in-app modals (such as web popups in Chrome, Electron apps, or Figma dialogs), click and drag to select the region. MacWindowSnap automatically applies smooth rounded corners and a dual-layer macOS soft drop shadow.
- **Native Windows Toast Notification**: Displays a Windows 10/11 Snipping Tool-style toast notification with hero image preview, clipboard confirmation, and direct markup integration.
- **Multi-Format Transparent Clipboard**: Copies both 32-bit PNG alpha stream and DIBV5 to the Windows Clipboard so you can paste directly into Telegram, Slack, Discord, Photoshop, Word, or web browsers without unwanted black backgrounds.
- **Automatic Storage**: Saves screenshots to `Pictures/Screenshots` with clean timestamped filenames.
- **Camera Shutter Sound**: Plays a subtle shutter click upon capture completion.

## Controls in Capture Mode

| Action | Control |
| --- | --- |
| Capture Highlighted Window | Left Click or `Space` / `Enter` |
| Select Custom Dialog / Area | Left Click + Drag |
| Cancel Capture Mode | Right Click or `Esc` |

## System Tray Menu

Right-click the MacWindowSnap icon in the system tray to access:
- **Capture Window (`Ctrl + Shift + A`)**
- **Auto-save to Pictures/Screenshots** (toggle)
- **Play Camera Shutter Sound** (toggle)
- **Open Screenshots Folder**
- **Exit**

## Running and Auto-Start

### Quick Start
Run `start.bat` or launch `app/MacWindowSnap.exe`.

### Start with Windows
To run MacWindowSnap automatically when Windows starts:
1. Press `Win + R`, type `shell:startup`, and press `Enter`.
2. Place a shortcut to `app/MacWindowSnap.exe` in the Startup folder (or run `create_shortcuts.ps1`).

## Building from Source

Requires .NET SDK 10 (or .NET 8 / 9 with Windows Desktop SDK):

```bash
dotnet build MacWindowSnap.csproj -c Release
dotnet publish MacWindowSnap.csproj -c Release -r win-x64 --self-contained false -o app
```
