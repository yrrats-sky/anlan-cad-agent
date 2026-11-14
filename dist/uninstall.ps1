# 安岚CAD助手 卸载器 v2.0（与 install.ps1 v2.0 配对，2026-11-11 分发整改版）
# 钩子清理双位置：install-dir Support（v1 旧装）+ Roaming Support（v2 主位置），
# END marker 精确删段（旧格式无 END 删到 EOF）；剩余内容平凡则整文件删除。
# 用法：powershell -ExecutionPolicy Bypass -File uninstall.ps1
# 注意：请先关闭 AutoCAD（dll 会话内锁定会占住文件）
$ErrorActionPreference = 'Continue'

$hookBegin = '[ANLANCADAGENT-HOOK]'
$hookEnd   = '[/ANLANCADAGENT-HOOK]'

# 删除我们钩子段（BEGIN **整行**起至 END **整行**；旧格式无 END 删到 EOF——v1 安装器只做
# 尾部追加，安全）。整行删的理由同 install.ps1：只删 marker 子串会留 ";;; " 与 " end" 碎行
function Remove-HookSeg([string]$txt) {
    $i = $txt.IndexOf($hookBegin)
    if ($i -lt 0) { return $null }
    $lineStart = if ($i -gt 0) { $txt.LastIndexOf("`n", $i - 1) } else { -1 }
    $head = if ($lineStart -ge 0) { $txt.Substring(0, $lineStart + 1) } else { "" }
    $e = $txt.IndexOf($hookEnd)
    if ($e -ge 0) {
        $lineEnd = $txt.IndexOf("`n", $e)
        if ($lineEnd -lt 0) { $lineEnd = $txt.Length }
        return $head.TrimEnd("`r", "`n") + $txt.Substring($lineEnd + 1)
    }
    return $head.TrimEnd("`r", "`n")
}

# 剩余内容是否"平凡"（空行/注释/(princ) 之外无实质内容）→ 平凡文件可整删
function Test-Trivial([string]$txt) {
    foreach ($ln in ($txt -split "`r`n|`n")) {
        $t = $ln.Trim()
        if ($t.Length -eq 0 -or $t.StartsWith(';') -or $t -eq '(princ)') { continue }
        return $false
    }
    return $true
}

# 双位置收集 acaddoc.lsp 候选：install-dir（HKLM 枚举）+ Roaming（AutoCAD * 全枚举）
# HKLM 显式 64 位视图（同 install.ps1：Inno [UninstallRun] 的 powershell 是 32 位，会被 WOW64 重定向）
$targets = @()
try {
    $lm64 = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        [Microsoft.Win32.RegistryHive]::LocalMachine,
        [Microsoft.Win32.RegistryView]::Registry64)
    $baseKey = $lm64.OpenSubKey('SOFTWARE\Autodesk\AutoCAD')
    if ($baseKey) {
        foreach ($r in $baseKey.GetSubKeyNames()) {
            $verKey = $baseKey.OpenSubKey($r)
            if (-not $verKey) { continue }
            foreach ($l in $verKey.GetSubKeyNames()) {
                $locKey = $verKey.OpenSubKey($l)
                $loc = ''
                if ($locKey) { $loc = $locKey.GetValue('AcadLocation'); $locKey.Close() }
                if ($loc) { $targets += (Join-Path $loc.TrimEnd('\') 'Support\acaddoc.lsp') }
            }
            $verKey.Close()
        }
        $baseKey.Close()
    }
} catch { }
$roamRoot = Join-Path $env:APPDATA 'Autodesk'
foreach ($prod in (Get-ChildItem $roamRoot -Directory -Filter 'AutoCAD*' -ErrorAction SilentlyContinue)) {
    foreach ($rv in (Get-ChildItem $prod.FullName -Directory -Filter 'R*' -ErrorAction SilentlyContinue)) {
        foreach ($locdir in (Get-ChildItem $rv.FullName -Directory -ErrorAction SilentlyContinue)) {
            $targets += (Join-Path $locdir.FullName 'Support\acaddoc.lsp')
        }
    }
}

# 1. HKCU 按需加载键（全版本枚举，v2 按每版本注册过）
$appKeys = Get-ChildItem 'HKCU:\Software\Autodesk\AutoCAD' -ErrorAction SilentlyContinue | ForEach-Object {
    Get-ChildItem $_.PSPath -ErrorAction SilentlyContinue | ForEach-Object {
        Get-ChildItem "$($_.PSPath)\Applications" -ErrorAction SilentlyContinue |
            Where-Object { $_.PSChildName -eq 'AnlanCadAgent' }
    }
}
$removed = 0
foreach ($k in $appKeys) { Remove-Item $k.PSPath -Recurse -Force; $removed++ }
Write-Output "[1/3] 已删 HKCU 按需加载键 $removed 处"

# 1b. acad 运行时 Loaded 缓存键：acad 加载过 bundle 后在 HKCU\...\Loaded\<bundle路径> 自写记录
# （2026-11-14 实测卸载残留，键名形如 C:/ProgramData/.../AnlanCadAgent.bundle）。指向已删路径
# 无害，但为卸载断言与重装干净重载清掉；只删含 AnlanCadAgent.bundle 的，别家记录不动
$loadedRemoved = 0
foreach ($rk in (Get-ChildItem 'HKCU:\Software\Autodesk\AutoCAD' -ErrorAction SilentlyContinue)) {
    foreach ($rv in (Get-ChildItem $rk.PSPath -ErrorAction SilentlyContinue)) {
        $loadedKeys = Get-ChildItem "$($rv.PSPath)\Loaded" -ErrorAction SilentlyContinue |
            Where-Object { $_.PSChildName -like '*AnlanCadAgent.bundle' }
        foreach ($k in $loadedKeys) { Remove-Item $k.PSPath -Recurse -Force; $loadedRemoved++ }
    }
}
if ($loadedRemoved -gt 0) { Write-Output "      已清 acad Loaded 缓存键 $loadedRemoved 处" }

# 2. acaddoc.lsp 钩子段（双位置，删前校验段内确为我们的内容）
$hookRemoved = 0
foreach ($lsp in ($targets | Select-Object -Unique)) {
    if (-not (Test-Path $lsp)) { continue }
    $txt = Get-Content $lsp -Raw
    if ($txt.IndexOf($hookBegin) -lt 0) { continue }
    $rest = Remove-HookSeg $txt
    if ($rest -isnot [string]) { continue }
    if (-not ($txt.Substring($txt.IndexOf($hookBegin)) -match 'ANLANDEVBOOT')) {
        Write-Output "[2/3] 跳过（内容校验不符）：$lsp"; continue
    }
    if (Test-Trivial $rest) {
        Remove-Item $lsp -Force
        Write-Output "[2/3] 钩子已删（文件仅剩我们的内容，整文件移除）：$lsp"
    } else {
        Set-Content -Path $lsp -Value $rest.TrimEnd("`r", "`n") -Encoding ASCII
        Write-Output "[2/3] 钩子段已删（保留别家内容）：$lsp"
    }
    $hookRemoved++
}
if ($hookRemoved -eq 0) { Write-Output '[2/3] 未找到钩子段，跳过' }

# 3. bundle 物料（双落位：ProgramData 主 + %APPDATA% 兜底）
foreach ($dst in @(
    'C:\ProgramData\Autodesk\ApplicationPlugins\AnlanCadAgent.bundle',
    (Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\AnlanCadAgent.bundle'))) {
    if (Test-Path $dst) { Remove-Item $dst -Recurse -Force; Write-Output "[3/3] 已删 bundle：$dst" }
}

Write-Output ''
Write-Output '卸载完成。%APPDATA%\AnlanCadAgent\config.json（含 apiKey）已保留，需要彻底清理请手动删除。'
