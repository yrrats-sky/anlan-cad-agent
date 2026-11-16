# One-shot recovery: poll device token -> rebuild release v0.4.4 (tag already points to 4ba20bd)
# -> upload assets -> delete token -> anonymous verify. Reason: deleting the old tag removed
# the release with it (GitHub releases die with their tag); token is required to recreate.
$ErrorActionPreference = 'Stop'
$ws = Split-Path -Parent $PSScriptRoot
$root = Split-Path -Parent $ws
$repo = 'yrrats-sky/anlan-cad-agent'
$device = [IO.File]::ReadAllText((Join-Path $ws 'tmp\device-code-2.txt')).Trim()
$tokenFile = Join-Path $root '.gh_token.txt'
$deadline = (Get-Date).AddSeconds(860)

# 1. poll
$token = $null
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
    try {
        $r = Invoke-RestMethod -Method Post -Uri 'https://github.com/login/oauth/access_token' `
            -Headers @{ Accept = 'application/json' } `
            -Body @{ client_id = '178c6fc778ccc68e1d6a'; device_code = $device; grant_type = 'urn:ietf:params:oauth:grant-type:device_code' }
    } catch { Write-Output ('poll exception: ' + $_.Exception.Message); continue }
    if ($r.access_token) { $token = $r.access_token; Write-Output 'TOKEN-OK'; break }
    if ($r.error -eq 'slow_down') { Start-Sleep -Seconds 5; continue }
    if ($r.error -eq 'authorization_pending') { continue }
    throw ('FLOW-ERROR: ' + $r.error)
}
if (-not $token) { throw 'token deadline exceeded' }

$H = @{ Authorization = "token $token"; Accept = 'application/vnd.github+json' }
try {
    # 2. clear leftover draft/partial release on tag v0.4.4 if any
    $rel = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases" -Headers $H
    foreach ($x in $rel) {
        if ($x.tag_name -eq 'v0.4.4') {
            Invoke-RestMethod -Method Delete -Uri "https://api.github.com/repos/$repo/releases/$($x.id)" -Headers $H | Out-Null
            Write-Output ('CLEARED-OLD: ' + $x.id)
        }
    }

    # 3. create release v0.4.4 (tag already exists at 4ba20bd)
    $payload = @{
        tag_name = 'v0.4.4'; name = 'v0.4.4'
        body = "## 安岚CAD助手 v0.4.4`n`n下载 anlan-cad-setup-v0.4.4.exe 双击安装(全程免管理员、无 UAC),完成后重启 AutoCAD 打开任意图纸,右侧自动出现「安岚助手」面板,点 ⚙ 填入控制台密钥即可。`n`n- AutoCAD 2027(.NET 10 内核)GUI 端到端验证通过,转正式支持——安装器自动按版本分流加载 dll(2021-2026 走 .NET 4.8,2027 走 .NET 10)`n- 当前完整支持 AutoCAD 2023、2027(64 位);2021-2026 兼容模式`n- 下载慢可用站内镜像:https://anlanai.cc/downloads/`n- SHA-256 见 version.json"
        draft = $false; prerelease = $false
    } | ConvertTo-Json -Depth 5
    $tmpPayload = Join-Path $env:TEMP 'rel-payload-044b.json'
    [IO.File]::WriteAllText($tmpPayload, $payload, (New-Object Text.UTF8Encoding $false))
    $new = Invoke-RestMethod -Method Post -Uri "https://api.github.com/repos/$repo/releases" -Headers $H -ContentType 'application/json' -InFile $tmpPayload
    Write-Output ('RELEASE-OK: ' + $new.html_url)

    # 4. upload assets
    $dist = Join-Path $ws 'dist'
    foreach ($f in @('anlan-cad-setup-v0.4.4.exe', 'anlan-cad-helper.zip', 'version.json')) {
        $up = "https://uploads.github.com/repos/$repo/releases/$($new.id)/assets?name=$f"
        Invoke-RestMethod -Method Post -Uri $up -Headers $H -ContentType 'application/octet-stream' -InFile (Join-Path $dist $f) | Out-Null
        Write-Output ('ASSET-OK: ' + $f)
    }
} finally {
    Remove-Item $tokenFile -Force -ErrorAction SilentlyContinue
    Write-Output 'token-deleted'
}

# 5. anonymous verify
Start-Sleep -Seconds 3
$v = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/tags/v0.4.4"
Write-Output ('VERIFY tag=' + $v.tag_name + ' assets=' + $v.assets.Count + ' state=' + $v.state)
Write-Output 'ALL-DONE'
