# CAD 内一键验证（阶段2）。前置：1) build/config.json 已填真 key  2) AutoCAD 已打开
# DEPRECATED: NETLOAD hangs silently on this machine; use HKCU registry loading instead (see HANDOFF.md / CLAUDE.md). Commands are now ANLANDEVPING / ANLANDEVPANEL / ANLANDEVASK.
# 用法：powershell -ExecutionPolicy Bypass -File verify.ps1
# 注意：dll 一旦 NETLOAD 会被 AutoCAD 锁定到会话结束；改码重编前先关 CAD
$ErrorActionPreference = 'Stop'

# COM retry helper: AutoCAD busy -> RPC_E_CALL_REJECTED (0x80010001) is transient, retry
function Try-Com([scriptblock]$sb, [int]$tries = 12, [int]$delayMs = 1500) {
    for ($i = 0; $i -lt $tries; $i++) {
        try { return & $sb } catch [Runtime.InteropServices.COMException] {
            if ($_.Exception.HResult -ne -2147417846) { throw }
            Start-Sleep -Milliseconds $delayMs
        }
    }
    return & $sb
}

$acad = [Runtime.InteropServices.Marshal]::GetActiveObject("AutoCAD.Application")
# wait until a document exists (COM registers before Drawing1 is created)
for ($i = 0; $i -lt 15; $i++) {
    if ($null -ne $acad.ActiveDocument) { break }
    Start-Sleep 2
}
$doc = $acad.ActiveDocument
if ($null -eq $doc) {
    Write-Output "no doc yet, creating one via COM..."
    $doc = Try-Com { $acad.Documents.Add() }
}
if ($null -eq $doc) { Write-Output "cannot create document"; exit 1 }
Start-Sleep 3   # let the new doc finish opening
Write-Output ("Connected: " + $acad.Version + "  doc: " + $doc.Name)

Try-Com { $doc.SendCommand("_.SECURELOAD 0 ") }   # SendCommand 通路可用；SetVariable API 会拒设（实测）
# SendCommand 异步：轮询确认 SECURELOAD 真正生效后再 NETLOAD，消除弹安全框的竞态
$v = 1
for ($i = 0; $i -lt 10; $i++) {
    $v = Try-Com { $doc.GetVariable("SECURELOAD") }
    if ("$v" -eq "0") { break }
    Start-Sleep 1
}
Write-Output ("SECURELOAD=" + $v)
Try-Com { $doc.SendCommand("_.FILEDIA 0 ") }
Try-Com { $doc.SendCommand("_.NETLOAD C:\Users\86159\.zcode\workspace\af326b29-9bae-46d2-b173-457c5aef3496\anlan-cad-agent\build\AnlanCadAcadFx48.dll ") }
Try-Com { $doc.SendCommand("_.FILEDIA 1 ") }
Try-Com { $doc.SendCommand("_.SECURELOAD 1 ") }

Try-Com { $doc.SendCommand("_.ANLANDEVPING ") }     # 命令行应回显 pong（若重复加载报错属正常）
Start-Sleep 3
Try-Com { Write-Output ("LASTPROMPT=[" + $doc.GetVariable("LASTPROMPT") + "]") }
Try-Com { $doc.SendCommand("_.ANLANDEVPANEL ") }    # 弹出聊天面板（标题：安岚助手DEV）
Start-Sleep 3
Try-Com { Write-Output ("ModelSpace.Count=" + $doc.ModelSpace.Count) }
Write-Output "sent. 面板内输入：画一条从(0,0)到(100,50)的线，画完告诉我画了什么"
