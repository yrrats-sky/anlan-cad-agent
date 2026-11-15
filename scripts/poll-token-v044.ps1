# Poll device flow until token granted; write to workspace-root .gh_token.txt (deleted after use).
$ErrorActionPreference = 'Stop'
$device = '3403ae0029329e7b40786ffb8c5dc236c05b4aab'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)   # af326b29 workspace root
$deadline = (Get-Date).AddSeconds(860)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
    try {
        $r = Invoke-RestMethod -Method Post -Uri 'https://github.com/login/oauth/access_token' `
            -Headers @{ Accept = 'application/json' } `
            -Body @{ client_id = '178c6fc778ccc68e1d6a'; device_code = $device; grant_type = 'urn:ietf:params:oauth:grant-type:device_code' }
    } catch { Write-Output ('poll exception: ' + $_.Exception.Message); continue }
    if ($r.access_token) {
        [IO.File]::WriteAllText((Join-Path $root '.gh_token.txt'), $r.access_token)
        Write-Output 'TOKEN-OK'
        break
    }
    if ($r.error -eq 'slow_down') { Start-Sleep -Seconds 5; continue }
    if ($r.error -eq 'authorization_pending') { continue }
    Write-Output ('FLOW-ERROR: ' + $r.error)
    break
}
