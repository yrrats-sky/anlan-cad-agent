$ErrorActionPreference = 'Stop'
$script = Join-Path $env:TEMP 'dotnet-install.ps1'
$content = (Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1').Content
[IO.File]::WriteAllText($script, $content)
& $script -Channel 10.0 -InstallDir 'C:\dotnet10' -Architecture x64 2>&1 | Select-Object -Last 4
Get-ChildItem 'C:\dotnet10\sdk' -Directory | Select-Object -ExpandProperty Name
