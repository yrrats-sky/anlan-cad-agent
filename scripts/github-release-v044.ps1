# One-shot: push v0.4.4 + create GitHub Release + upload assets (token from .gh_token.txt at workspace root).
$ErrorActionPreference = 'Stop'
$ws = Split-Path -Parent $PSScriptRoot
$token = [IO.File]::ReadAllText((Join-Path $ws '..\.gh_token.txt')).Trim()
$repo = 'yrrats-sky/anlan-cad-agent'

# 1. push main (PS5.1: git writes progress to stderr -> NativeCommandError under Stop; relax locally)
$ErrorActionPreference = 'Continue'
git -C $ws push "https://oauth2:$token@github.com/$repo.git" main 2>&1 | ForEach-Object { Write-Output $_ }
$pushExit = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($pushExit -ne 0) { throw "push failed (exit $pushExit)" }
Write-Output 'PUSH-OK'

# 2. create release v0.4.4
$payload = @{
    tag_name = 'v0.4.4'; target_commitish = 'main'; name = 'v0.4.4'
    body = "## 安岚CAD助手 v0.4.4`n`n下载 anlan-cad-setup-v0.4.4.exe 双击安装(全程免管理员、无 UAC),完成后重启 AutoCAD 打开任意图纸,右侧自动出现「安岚助手」面板,点 ⚙ 填入控制台密钥即可。`n`n- AutoCAD 2027(.NET 10 内核)GUI 端到端验证通过,转正式支持——安装器自动按版本分流加载 dll(2021-2026 走 .NET 4.8,2027 走 .NET 10)`n- 当前完整支持 AutoCAD 2023、2027(64 位);2021-2026 兼容模式`n- 下载慢可用站内镜像:https://anlanai.cc/downloads/`n- SHA-256 见 version.json"
    draft = $false; prerelease = $false
} | ConvertTo-Json -Depth 5
$tmpPayload = Join-Path $env:TEMP 'rel-payload-044.json'
[IO.File]::WriteAllText($tmpPayload, $payload, (New-Object Text.UTF8Encoding $false))
$rel = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$repo/releases" -Headers @{
    Authorization = "token $token"; Accept = 'application/vnd.github+json'
} -ContentType 'application/json' -InFile $tmpPayload
Write-Output ('RELEASE-OK: ' + $rel.html_url)

# 3. upload assets
$dist = Join-Path $ws 'dist'
foreach ($f in @('anlan-cad-setup-v0.4.4.exe', 'anlan-cad-helper.zip', 'version.json')) {
    $p = Join-Path $dist $f
    $up = "https://uploads.github.com/repos/$repo/releases/$($rel.id)/assets?name=$f"
    Invoke-RestMethod -Method Post -Uri $up -Headers @{
        Authorization = "token $token"; Accept = 'application/vnd.github+json'
    } -ContentType 'application/octet-stream' -InFile $p | Out-Null
    Write-Output ('ASSET-OK: ' + $f)
}
Write-Output 'ALL-DONE'
