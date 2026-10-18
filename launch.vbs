Set sh = CreateObject("WScript.Shell")
sh.CurrentDirectory = "D:\Project\MacWindowSnap\app"
sh.Run """D:\Project\MacWindowSnap\app\ShadowSnap.exe""", 1, False
