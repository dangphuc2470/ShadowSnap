$WshShell = New-Object -ComObject WScript.Shell
$targetExe = "D:\Project\MacWindowSnap\app\ShadowSnap.exe"
$workingDir = "D:\Project\MacWindowSnap\app"
$iconPath = "D:\Project\MacWindowSnap\app\assets\app.ico"

# 1. Desktop shortcut
$desktopShortcutPath = Join-Path ([Environment]::GetFolderPath("Desktop")) "ShadowSnap.lnk"
$s1 = $WshShell.CreateShortcut($desktopShortcutPath)
$s1.TargetPath = $targetExe
$s1.WorkingDirectory = $workingDir
$s1.IconLocation = "$targetExe,0"
$s1.Description = "ShadowSnap (Ctrl + Shift + A)"
$s1.Save()

# 2. Startup shortcut (auto start on Windows boot)
$startupFolder = [Environment]::GetFolderPath("Startup")
$startupShortcutPath = Join-Path $startupFolder "ShadowSnap.lnk"
$s2 = $WshShell.CreateShortcut($startupShortcutPath)
$s2.TargetPath = $targetExe
$s2.WorkingDirectory = $workingDir
$s2.IconLocation = "$targetExe,0"
$s2.Description = "ShadowSnap (Ctrl + Shift + A)"
$s2.Save()

# 3. Start Menu Programs shortcut (for Windows Notification Center & Start Menu)
$programsFolder = [Environment]::GetFolderPath("Programs")
$programsShortcutPath = Join-Path $programsFolder "ShadowSnap.lnk"
$s3 = $WshShell.CreateShortcut($programsShortcutPath)
$s3.TargetPath = $targetExe
$s3.WorkingDirectory = $workingDir
$s3.IconLocation = "$targetExe,0"
$s3.Description = "ShadowSnap (Ctrl + Shift + A)"
$s3.Save()

# Remove old shortcuts if present
$oldDesktop = Join-Path ([Environment]::GetFolderPath("Desktop")) "MacWindowSnap.lnk"
if (Test-Path $oldDesktop) { Remove-Item $oldDesktop -Force -ErrorAction SilentlyContinue }
$oldStartup = Join-Path $startupFolder "MacWindowSnap.lnk"
if (Test-Path $oldStartup) { Remove-Item $oldStartup -Force -ErrorAction SilentlyContinue }
$oldPrograms = Join-Path $programsFolder "MacWindowSnap.lnk"
if (Test-Path $oldPrograms) { Remove-Item $oldPrograms -Force -ErrorAction SilentlyContinue }

Write-Host "ShadowSnap shortcuts updated successfully!"
