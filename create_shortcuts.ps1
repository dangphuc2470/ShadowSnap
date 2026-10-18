$WshShell = New-Object -ComObject WScript.Shell
$targetExe = "D:\Project\MacWindowSnap\app\MacWindowSnap.exe"
$workingDir = "D:\Project\MacWindowSnap\app"

# 1. Desktop shortcut
$desktopShortcutPath = Join-Path ([Environment]::GetFolderPath("Desktop")) "MacWindowSnap.lnk"
$s1 = $WshShell.CreateShortcut($desktopShortcutPath)
$s1.TargetPath = $targetExe
$s1.WorkingDirectory = $workingDir
$s1.Description = "MacWindowSnap (Ctrl + Shift + A)"
$s1.Save()

# 2. Startup shortcut (auto start on Windows boot)
$startupFolder = [Environment]::GetFolderPath("Startup")
$startupShortcutPath = Join-Path $startupFolder "MacWindowSnap.lnk"
$s2 = $WshShell.CreateShortcut($startupShortcutPath)
$s2.TargetPath = $targetExe
$s2.WorkingDirectory = $workingDir
$s2.Description = "MacWindowSnap (Ctrl + Shift + A)"
$s2.Save()

Write-Host "Shortcuts updated successfully!"
