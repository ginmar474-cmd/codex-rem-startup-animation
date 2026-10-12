$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$compilerPath = Join-Path $frameworkPath 'csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319'
    $compilerPath = Join-Path $frameworkPath 'csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'Windows .NET Framework 4.x is required.' }

$arguments = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001', '/warn:4')
$arguments += '/out:' + (Join-Path $projectRoot 'Start-Codex-Rem.exe')
$arguments += '/r:' + (Join-Path $frameworkPath 'WPF/PresentationCore.dll')
$arguments += '/r:' + (Join-Path $frameworkPath 'WPF/PresentationFramework.dll')
$arguments += '/r:' + (Join-Path $frameworkPath 'WPF/WindowsBase.dll')
$arguments += '/r:' + (Join-Path $frameworkPath 'System.Xaml.dll')
$arguments += '/r:' + (Join-Path $frameworkPath 'System.Xml.Linq.dll')
$arguments += (Join-Path $projectRoot 'src/RemLauncher.cs')
& $compilerPath @arguments
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Write-Output ('Generated: ' + (Join-Path $projectRoot 'Start-Codex-Rem.exe'))
