# COM 接管已运行的 AutoCAD：NETLOAD + 跑命令 + 弹面板
# DEPRECATED: NETLOAD hangs silently on this machine; use HKCU registry loading instead (see HANDOFF.md / CLAUDE.md). Commands are now ANLANDEVPING / ANLANDEVPANEL / ANLANDEVASK.
$ErrorActionPreference = 'Stop'
try {
    $acad = [Runtime.InteropServices.Marshal]::GetActiveObject("AutoCAD.Application")
} catch {
    $acad = [Runtime.InteropServices.Marshal]::GetActiveObject("AutoCAD.Application.24.2")
}
Write-Output ("Connected: " + $acad.Version)
$doc = $acad.ActiveDocument
Write-Output ("ActiveDoc: " + $doc.Name)

$doc.SendCommand("_.FILEDIA 0 ")
$doc.SendCommand("_.NETLOAD C:\Users\86159\.zcode\workspace\af326b29-9bae-46d2-b173-457c5aef3496\anlan-cad-agent\build\AnlanCadAcadFx48.dll ")
$doc.SendCommand("_.FILEDIA 1 ")
Write-Output "NETLOAD sent."
$doc.SendCommand("_.ANLANPING ")
Write-Output "PING sent."
$doc.SendCommand("_.ANLANPANEL ")
Write-Output "PANEL sent."
