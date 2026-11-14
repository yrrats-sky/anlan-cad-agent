# 安岚CAD助手 教师机安装器 v2.0（2026-11-11 分发整改版）
# 整改内容（对应 dist-分发决策审查.md 清单）：钩子迁 Roaming Support（免管理员为真）+
# 旧位置迁移清理；版本枚举 R21-R26+ 分级（2023 全量 / 2021-2026 待验证 / 2017-2020 不支持）；
# bundle 落位 ProgramData 失败自动兜底 %APPDATA%；WebView2 运行时检测；双轨 config
# （bundle 内烘焙 config.json 支持地推 key）；-ReportUrl 可选回传；32 位拒绝。
# 用法：powershell -ExecutionPolicy Bypass -File install.ps1 [-ApiKey sk-xxx] [-ReportUrl https://...]
# 幂等：重复运行安全；卸载用 uninstall.ps1（双位置钩子精确清理）
# 适用：AutoCAD 2021-2026（64 位）；绿色版/精简版不支持
param([string]$ApiKey = "", [string]$ReportUrl = "")
$ErrorActionPreference = 'Stop'

# 顶层异常留痕（排障证据链，与 anlan_web_bridge/anlan_fallback 同款模式）：
# Inno [Run] 的 runhidden 会吞掉 stderr，未捕获异常必须落文件才能定位教师机装机失败。
# 三路兜底：TEMP → USERPROFILE → 脚本目录（Inno [Run] 环境曾实测 TEMP 写入失败，11-14）
trap {
    $msg = (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $_.Exception.Message
            + ' @ line ' + $_.InvocationInfo.ScriptLineNumber
    foreach ($dir in @($env:TEMP, $env:USERPROFILE, $PSScriptRoot)) {
        if ($dir) {
            try {
                [IO.File]::WriteAllText((Join-Path $dir 'anlan_install_err.txt'), $msg)
                break
            } catch { }
        }
    }
    exit 1
}

# ---- 0. 位数自检（不做清单：32 位直接提示） ----
if (-not [Environment]::Is64BitOperatingSystem) {
    Write-Output '[错误] 需要 64 位 Windows（32 位系统不支持 AutoCAD 2021+）。'
    exit 1
}

# ---- 1. 物料自检 ----
$srcBundle = Join-Path $PSScriptRoot 'bundle'
$srcDll    = Join-Path $srcBundle 'Contents\AnlanCadAcadFx48.dll'
$srcXml    = Join-Path $srcBundle 'PackageContents.xml'
$srcWww    = Join-Path $srcBundle 'Contents\wwwroot\panel.html'
$srcLoader = Join-Path $srcBundle 'Contents\WebView2Loader.dll'
if (-not (Test-Path $srcDll) -or -not (Test-Path $srcXml) -or -not (Test-Path $srcWww) -or -not (Test-Path $srcLoader)) {
    Write-Output "[错误] 缺物料：$srcBundle （解压 zip 后应在同目录看到 bundle 文件夹）"; exit 1
}

# ---- 2. 版本枚举 + 分级 ----
# full=R24.2(2023) 实测全量支持；pending=R24.x 其余(2021/2022/2024)+R25+(2025/2026) 注册但标待验证；
# old=R23-(2017-2020) 不支持
# 显式 64 位注册表视图：Inno [Run] 的 powershell 是 32 位（WOW64），Get-ChildItem HKLM:
# 会被重定向到 Wow6432Node 看不到 Autodesk 键 → 教师机装机 exit 1（11-14 实测根因）
$versions = @()
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
                if (-not $loc) { continue }
                $major = 0
                if ($r -match '^R(\d+)') { $major = [int]$Matches[1] }
                $grade = 'old'
                if ($r -eq 'R24.2') { $grade = 'full' }
                elseif ($major -ge 24) { $grade = 'pending' }
                $versions += [pscustomobject]@{ R = $r; Id = $l.Split(':')[0]; Loc = $loc; Grade = $grade }
            }
            $verKey.Close()
        }
        $baseKey.Close()
    }
} catch { }
if (-not $versions) {
    Write-Output '[错误] 未检测到注册表安装的 AutoCAD（绿色版/精简版暂不支持，请安装官方版 2021-2026）。'
    exit 1
}
Write-Output '[1/6] 检测到 AutoCAD：'
foreach ($v in $versions) {
    $tag = switch ($v.Grade) { 'full' { '全量支持' } 'pending' { '兼容模式（待前向兼容验证）' } default { '不支持' } }
    Write-Output ("      - {0,-6} {1}  {2}" -f $v.R, $v.Loc, $tag)
}
$supported = @($versions | Where-Object { $_.Grade -ne 'old' })
if (-not $supported) {
    Write-Output '[错误] 检测到的 AutoCAD 均为 2017-2020 老版本，暂不支持（需 2021+）。'
    exit 1
}

# ---- 3. bundle 落位（ProgramData 优先；无权限兜底 %APPDATA%，免管理员闭环） ----
$bundleDir = 'C:\ProgramData\Autodesk\ApplicationPlugins\AnlanCadAgent.bundle'
try {
    New-Item -ItemType Directory -Force -Path (Join-Path $bundleDir 'Contents') | Out-Null
    Copy-Item $srcXml (Join-Path $bundleDir 'PackageContents.xml') -Force
    Copy-Item (Join-Path $srcBundle 'Contents\*') (Join-Path $bundleDir 'Contents\') -Recurse -Force
} catch {
    $bundleDir = Join-Path $env:APPDATA 'Autodesk\ApplicationPlugins\AnlanCadAgent.bundle'
    New-Item -ItemType Directory -Force -Path (Join-Path $bundleDir 'Contents') | Out-Null
    Copy-Item $srcXml (Join-Path $bundleDir 'PackageContents.xml') -Force
    Copy-Item (Join-Path $srcBundle 'Contents\*') (Join-Path $bundleDir 'Contents\') -Recurse -Force
}
Write-Output "[2/6] bundle 已就位：$bundleDir"

# 烘焙 config 显式双向同步（重装/换轨防串 key，11-11 实测发现）：Copy-Item Contents\* 只覆盖
# 不删除——push 包装过后再装 public 包，目标残留的旧烘焙 config 会被当注入源串 key。
# 目标 bundle 的 config 存在性必须严格跟随本次安装源
$srcBaked = Join-Path $srcBundle 'Contents\config.json'
$bakedCfg = Join-Path $bundleDir 'Contents\config.json'
if (Test-Path $srcBaked) { Copy-Item $srcBaked $bakedCfg -Force }
else { Remove-Item $bakedCfg -Force -ErrorAction SilentlyContinue }

# ---- 4. HKCU 按需加载注册（每个支持版本各注册一份）----
# 本方案已实测；NETLOAD 在部分机器静默卡死，勿改。LOADER 指向 bundle 里的 dll。
$cmds = @('ANLANDEVPING','ANLANDEVPANEL','ANLANDEVASK','ANLANDEVBOOT','ANLANDEVSET','ANLANDEVWEB','ANLANDEVWPF')
foreach ($v in $supported) {
    $app = "HKCU:\Software\Autodesk\AutoCAD\$($v.R)\$($v.Id)\Applications\AnlanCadAgent"
    New-Item -Path $app -Force | Out-Null
    Set-ItemProperty $app 'DESCRIPTION' 'AnLan CAD teaching agent'
    Set-ItemProperty $app 'LOADCTRLS' 14        # int → REG_DWORD（Set-ItemProperty 无 -Type）
    Set-ItemProperty $app 'MANAGED' 1
    Set-ItemProperty $app 'LOADER' (Join-Path $bundleDir 'Contents\AnlanCadAcadFx48.dll')
    New-Item -Path "$app\Commands" -Force | Out-Null
    foreach ($c in $cmds) { Set-ItemProperty "$app\Commands" $c 'ANLANGRP' }
}
Write-Output '[3/6] HKCU 按需加载已注册（输命令即自动加载 dll）'

# ---- 5. acaddoc.lsp 钩子 ----
# 主位置 = Roaming Support（per-user 可写，免管理员）；install-dir Support 里的旧钩子迁移清理。
# 注意 SFSP "先找到者生效"：install-dir 若还有别家内容（无法删除），把钩子也合并进去保底。
$hookBegin = '[ANLANCADAGENT-HOOK]'
$hookEnd   = '[/ANLANCADAGENT-HOOK]'
$hookSeg = @"

;;; [ANLANCADAGENT-HOOK] AnLan CAD agent auto-open panel (begin; uninstall via uninstall.ps1)
(defun S::STARTUP ()
  (command "_.ANLANDEVBOOT")
  (princ)
)
;;; [/ANLANCADAGENT-HOOK] end
"@

# 删除我们钩子段（BEGIN **整行**起至 END **整行**；旧格式无 END 删到 EOF），返回剩余文本。
# 整行删的两个理由：只删 marker 子串会留 ";;; " 与 " end" 碎行，且每次 install 的迁移
# 合并会累积一块（11-11 实测 11 块残骸的根因）；BEGIN 不回退行首同理留 ";;; " 碎尾
function Remove-HookSeg([string]$txt) {
    $i = $txt.IndexOf($hookBegin)
    if ($i -lt 0) { return $txt }
    $lineStart = if ($i -gt 0) { $txt.LastIndexOf("`n", $i - 1) } else { -1 }
    $head = if ($lineStart -ge 0) { $txt.Substring(0, $lineStart + 1) } else { "" }
    $e = $txt.IndexOf($hookEnd)
    if ($e -ge 0) {
        $lineEnd = $txt.IndexOf("`n", $e)
        if ($lineEnd -lt 0) { $lineEnd = $txt.Length }
        return $head.TrimEnd("`r", "`n") + $txt.Substring($lineEnd + 1)
    }
    return $head.TrimEnd("`r", "`n")   # 旧格式：段总在文件尾（旧安装器只做尾部追加）
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

function Add-HookToFile([string]$path) {
    if (-not (Test-Path $path)) {
        Set-Content -Path $path -Value $hookSeg.TrimStart("`r", "`n") -Encoding ASCII
        return $true
    }
    $txt = Get-Content $path -Raw
    if ($txt.IndexOf($hookBegin) -ge 0) { return $false }   # 已有我们的段，幂等跳过
    if ($txt -match 'defun\s+S::STARTUP') {
        Write-Output "      [警告] $path 已有别家 S::STARTUP，未自动合并——请手工在其 S::STARTUP 里加一行 (command \"_.ANLANDEVBOOT\")"
        return $false
    }
    Add-Content -Path $path -Value $hookSeg -Encoding ASCII
    return $true
}

# 5a. Roaming Support 主钩子（真实路径 %APPDATA%\Autodesk\AutoCAD <产品>\R<版本>\<locale>\Support，
# locale 层实证为 chs 等，枚举 R 目录下的全部子目录；按 R 版本过滤）
$roamCount = 0
$roamRoot = Join-Path $env:APPDATA 'Autodesk'
foreach ($prod in (Get-ChildItem $roamRoot -Directory -Filter 'AutoCAD*' -ErrorAction SilentlyContinue)) {
    foreach ($rv in (Get-ChildItem $prod.FullName -Directory -Filter 'R*' -ErrorAction SilentlyContinue)) {
        if ($supported | Where-Object { $_.R -eq $rv.PSChildName }) {
            foreach ($locdir in (Get-ChildItem $rv.FullName -Directory -ErrorAction SilentlyContinue)) {
                $sup = Join-Path $locdir.FullName 'Support'
                if (-not (Test-Path $sup)) { New-Item -ItemType Directory -Force -Path $sup | Out-Null }
                try { if (Add-HookToFile (Join-Path $sup 'acaddoc.lsp')) { $roamCount++ } } catch { Write-Output "      [警告] Roaming 钩子写入失败：$($_.Exception.Message)" }
            }
        }
    }
}
Write-Output "[4/6] Roaming 钩子已写入 $roamCount 处（per-user，免管理员）"

# 5b. install-dir 旧钩子迁移清理（决策1：先找到者生效，旧位置必须清理）
foreach ($v in $supported) {
    $lsp = Join-Path $v.Loc.TrimEnd('\') 'Support\acaddoc.lsp'
    if (-not (Test-Path $lsp)) { continue }
    try {
        $rest = Remove-HookSeg (Get-Content $lsp -Raw)
        if (Test-Trivial $rest) {
            Remove-Item $lsp -Force
            Write-Output "      [迁移] 旧钩子已清理（原文件仅含我们的内容）：$lsp"
        } else {
            Set-Content -Path $lsp -Value ($rest.TrimEnd("`r", "`n") + "`r`n" + $hookSeg) -Encoding ASCII
            Write-Output "      [迁移] 旧位置仍有别家内容，钩子已合并保底：$lsp"
        }
    } catch {
        Write-Output "      [警告] 旧位置 acaddoc.lsp 无写权限，未迁移：$lsp（若面板不自动弹出请输 ANLANDEVPANEL）"
    }
}

# ---- 6. WebView2 运行时检测（缺失不阻塞：WebShell 会自动降级兼容面板）----
# 64 位视图同上（32 位 powershell 的 HKLM: 驱动器被 WOW64 重定向）
$pv = $null
try {
    $lm64 = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
        [Microsoft.Win32.RegistryHive]::LocalMachine,
        [Microsoft.Win32.RegistryView]::Registry64)
    foreach ($k in @(
        'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
        'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}')) {
        $sub = $lm64.OpenSubKey($k)
        if ($sub) { $val = $sub.GetValue('pv'); $sub.Close(); if ($val) { $pv = $val; break } }
    }
} catch { }
if (-not $pv) {
    try {
        $cu = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
            [Microsoft.Win32.RegistryHive]::CurrentUser,
            [Microsoft.Win32.RegistryView]::Default)
        $sub = $cu.OpenSubKey('SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}')
        if ($sub) { $val = $sub.GetValue('pv'); $sub.Close(); if ($val) { $pv = $val } }
    } catch { }
}
if ($pv) { Write-Output "[5/6] WebView2 运行时: $pv（Web 面板可用）" }
else { Write-Output '[5/6] 未检测到 WebView2 运行时——面板将自动使用 WPF 兼容模式（功能完整），建议后续安装 Edge WebView2 Evergreen 运行时升级体验' }

# ---- 7. 配置 %APPDATA%\AnlanCadAgent\config.json ----
# 不覆盖已有；-ApiKey 优先更新；否则 bundle 内烘焙 config.json（地推轨）可作初始配置
$cfgDir = Join-Path $env:APPDATA 'AnlanCadAgent'
$cfg = Join-Path $cfgDir 'config.json'
$bakedCfg = Join-Path $bundleDir 'Contents\config.json'
if (Test-Path $cfg) {
    if ($ApiKey -ne '') {
        $j = Get-Content $cfg -Raw | ConvertFrom-Json
        $j.apiKey = $ApiKey
        $j | ConvertTo-Json | Set-Content $cfg -Encoding UTF8
        Write-Output "[6/6] apiKey 已更新（其余配置未动）"
    } else {
        Write-Output '[6/6] 配置已存在，未改动：' $cfg
    }
} else {
    New-Item -ItemType Directory -Force -Path $cfgDir | Out-Null
    if (Test-Path $bakedCfg) {
        Copy-Item $bakedCfg $cfg -Force
        Write-Output '[6/6] 配置已从安装包注入（含 key，地推轨）：' $cfg
    } else {
        $key = $ApiKey
        Set-Content $cfg ("{`n  `"baseUrl`": `"https://anlanai.cc/v1`",`n  `"apiKey`": `"$key`",`n  `"model`": `"glm-5.3`",`n  `"vlmModel`": `"sensenova-6.8-flash`"`n}") -Encoding UTF8
        Write-Output '[6/6] 配置已生成：' $cfg
        if ($key -eq '') { Write-Output '提醒：尚未配置 apiKey。开 CAD 后点面板 ⚙ 填入中转站 key（或重跑本脚本带 -ApiKey）。' }
    }
}

# ---- 8. 版本探测回传（可选；试点中站点端点上线后由安装方传 -ReportUrl 启用）----
if ($ReportUrl -ne '') {
    try {
        $report = @{
            os = [Environment]::OSVersion.VersionString
            versions = @($versions | ForEach-Object { "$($_.R) $($_.Grade)" })
            bundle = $bundleDir
        } | ConvertTo-Json
        Invoke-WebRequest -Uri $ReportUrl -Method Post -Body $report -ContentType 'application/json' -UseBasicParsing -TimeoutSec 8 | Out-Null
    } catch { Write-Output '（版本回传失败，已忽略）' }
}

Write-Output ''
Write-Output '安装完成。重启 AutoCAD 后打开任意图纸，右侧自动出现「安岚助手」面板。'
if (@($versions | Where-Object { $_.Grade -eq 'pending' }).Count -gt 0) {
    Write-Output '注意：本次按兼容模式安装了非 2023 版本，功能待前向兼容验证，遇异常请反馈。'
}
