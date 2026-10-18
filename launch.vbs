Set sh = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
appDir = fso.BuildPath(scriptDir, "app")
appExe = fso.BuildPath(appDir, "ShadowSnap.exe")
sh.CurrentDirectory = appDir
sh.Run """" & appExe & """", 1, False
