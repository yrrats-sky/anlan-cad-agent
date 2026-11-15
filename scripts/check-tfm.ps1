$ErrorActionPreference = 'Stop'
function Get-TfmInfo($path, $label) {
    $a = [Reflection.Assembly]::LoadFile($path)
    $tfa = $a.GetCustomAttributesData() | Where-Object { $_.AttributeType.Name -eq 'TargetFrameworkAttribute' } | Select-Object -First 1
    $tfm = 'null'
    if ($tfa) { $tfm = $tfa.ConstructorArguments[0].Value }
    Write-Output ($label + ': ' + $a.GetName().Name + ' ' + $a.GetName().Version + ' TFM=' + $tfm)
}
$root = Split-Path -Parent $PSScriptRoot
Get-TfmInfo (Join-Path $root 'lib\WebView2\Microsoft.Web.WebView2.Core.dll') 'WV2.Core  '
Get-TfmInfo (Join-Path $root 'lib\WebView2\Microsoft.Web.WebView2.Wpf.dll') 'WV2.Wpf   '
Get-TfmInfo (Join-Path $root 'lib\acad2027\acmgd.dll') '2027 AcMgd'
Get-TfmInfo (Join-Path $root 'lib\acad2027\acdbmgd.dll') '2027 AcDbMgd'
Get-TfmInfo (Join-Path $root 'lib\acad2027\accoremgd.dll') '2027 AcCoreMgd'
