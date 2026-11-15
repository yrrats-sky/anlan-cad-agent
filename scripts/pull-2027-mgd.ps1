# Pull AutoCAD 2027 managed assemblies from VM to host (base64 over PS Direct).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File pull-2027-mgd.ps1
$ErrorActionPreference = 'Stop'
$pw = ConvertTo-SecureString '456+' -AsPlainText -Force
$cred = New-Object System.Management.Automation.PSCredential('nova', $pw)
$vm = 'Win11-CADTest'
$loc = 'C:\Program Files\Autodesk\AutoCAD 2027'
$outDir = Join-Path $PSScriptRoot '..\lib\acad2027'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

foreach ($name in @('acmgd.dll', 'acdbmgd.dll', 'accoremgd.dll')) {
    $b64 = Invoke-Command -VMName $vm -Credential $cred -ScriptBlock {
        param($p, $f)
        [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $p $f)))
    } -ArgumentList $loc, $name
    [IO.File]::WriteAllBytes((Join-Path $outDir $name), [Convert]::FromBase64String($b64))
    Write-Output ("pulled {0} -> {1} bytes" -f $name, (Get-Item (Join-Path $outDir $name)).Length)
}
# Also grab the runtimeconfig + accoreconsole presence for the record
$rc = Invoke-Command -VMName $vm -Credential $cred -ScriptBlock {
    param($p)
    $r = @{}
    foreach ($f in @('acdbmgd.runtimeconfig.json', 'acmgd.runtimeconfig.json')) {
        $path = Join-Path $p $f
        if (Test-Path $path) { $r[$f] = Get-Content $path -Raw }
    }
    $r['accoreconsole'] = Test-Path (Join-Path $p 'accoreconsole.exe')
    $r | ConvertTo-Json
} -ArgumentList $loc
Write-Output $rc
