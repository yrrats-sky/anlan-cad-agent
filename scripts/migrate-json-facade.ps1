# One-shot migration: JavaScriptSerializer call sites -> JsX facade (JSON collapse refactoring).
# Idempotent: no-ops when patterns absent. Files must be saved back with original encoding (UTF-8/BOM preserved).
$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..\src'
$files = @(
    'Core\AgentLoop.cs', 'Core\LlmConfig.cs', 'Core\ToolRegistry.cs', 'Core\VlmClient.cs',
    'AnlanCadAcadFx48\SettingsForm.cs', 'AnlanCadAcadFx48\WebShell.cs', 'SmokeTest\Program.cs'
)
foreach ($rel in $files) {
    $p = Join-Path $root $rel
    $t = [IO.File]::ReadAllText($p)
    $before = $t
    $t = $t.Replace('new System.Web.Script.Serialization.JavaScriptSerializer()', 'new AnlanCadCore.JsX()')
    $t = $t.Replace('new JavaScriptSerializer()', 'new JsX()')
    $t = $t.Replace('JavaScriptSerializer ser', 'JsX ser')
    $t = $t.Replace("using System.Web.Script.Serialization;`r`n", '')
    $t = $t.Replace("using System.Web.Script.Serialization;`n", '')
    if ($t -ne $before) {
        [IO.File]::WriteAllText($p, $t)
        Write-Output "migrated $rel"
    } else {
        Write-Output "no-op    $rel"
    }
}
# leftover check
$left = Select-String -Path (Get-ChildItem $root -Recurse -Filter *.cs).FullName -Pattern 'JavaScriptSerializer' -List | Select-Object -ExpandProperty Path
if ($left) { Write-Output ('LEFTOVER in: ' + ($left -join ', ')) } else { Write-Output 'no JavaScriptSerializer refs left (comments excluded only if none)' }
