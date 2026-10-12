$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$exePath = Join-Path $projectRoot 'Start-Codex-Rem.exe'
if (-not (Test-Path -LiteralPath $exePath)) { throw 'Run build.ps1 first.' }

[xml]$configuration = Get-Content -LiteralPath (Join-Path $projectRoot 'launcher.config.xml') -Raw
$fps = [int]$configuration.launcher.frameRate
$duration = [double]::Parse($configuration.launcher.durationSeconds, [Globalization.CultureInfo]::InvariantCulture)
$expectedFrames = [int][Math]::Ceiling($duration * $fps)
$frameRoot = Join-Path $projectRoot ('output/render-frames-' + (Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
New-Item -ItemType Directory -Path $frameRoot -Force | Out-Null
$process = Start-Process -FilePath $exePath -ArgumentList @('--render', ('"' + $frameRoot + '"')) -WindowStyle Hidden -Wait -PassThru
if ($process.ExitCode -ne 0) { throw 'Frame render failed. Check output/launcher-error.txt.' }

$frameCount = (Get-ChildItem -LiteralPath $frameRoot -Filter 'frame-*.png').Count
if ($frameCount -ne $expectedFrames) { throw ('Frame count mismatch: ' + $frameCount + ', expected: ' + $expectedFrames) }
Write-Output ('Frames exported: ' + $frameRoot)

$ffmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue
if ($null -eq $ffmpeg) {
    Write-Output 'FFmpeg not found. Skipped MP4 encoding. The EXE animation is ready to use.'
    return
}

$moviePath = Join-Path $projectRoot 'output/rem-codex-startup-2k-60fps.mp4'
& $ffmpeg.Source -hide_banner -loglevel error -y -framerate $fps -i (Join-Path $frameRoot 'frame-%04d.png') -frames:v $expectedFrames -c:v libx264 -preset slow -crf 18 -pix_fmt yuv420p -r $fps -movflags +faststart $moviePath
if ($LASTEXITCODE -ne 0) { throw 'FFmpeg encoding failed.' }
Copy-Item -LiteralPath (Join-Path $frameRoot 'poster.png') -Destination (Join-Path $projectRoot 'output/rem-codex-startup-poster.png') -Force
Write-Output ('Video generated: ' + $moviePath)
