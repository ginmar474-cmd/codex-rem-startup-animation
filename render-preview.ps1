$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$exePath = Join-Path $projectRoot 'Start-Codex-Rem.exe'
if (-not (Test-Path -LiteralPath $exePath)) { throw 'Run build.ps1 first.' }

$localFfmpeg = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'tools') -Recurse -Filter 'ffmpeg.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $localFfmpeg) {
    $systemFfmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue
    if ($null -eq $systemFfmpeg) { throw 'FFmpeg was not found. Run install-ffmpeg.ps1 first.' }
    $ffmpegPath = $systemFfmpeg.Source
} else {
    $ffmpegPath = $localFfmpeg.FullName
}

$outputRoot = Join-Path $projectRoot 'output'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$moviePath = Join-Path $outputRoot 'rem-codex-crystal-wake-1080p-60fps.mp4'
$progressPath = Join-Path $outputRoot 'render-progress.txt'
Remove-Item -LiteralPath $progressPath -Force -ErrorAction SilentlyContinue

$arguments = @('--render-video', ('"' + $moviePath + '"'), ('"' + $ffmpegPath + '"'))
$process = Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
$lastProgress = ''
while (-not $process.HasExited) {
    Start-Sleep -Milliseconds 750
    if (Test-Path -LiteralPath $progressPath) {
        $progress = (Get-Content -LiteralPath $progressPath -Raw).Trim()
        if ($progress -ne $lastProgress) {
            Write-Output ('Rendering: ' + $progress + '%')
            $lastProgress = $progress
        }
    }
}
if ($process.ExitCode -ne 0) { throw 'Video render failed. Check output/launcher-error.txt.' }

$test = Start-Process -FilePath $exePath -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if ($test.ExitCode -ne 0) { throw 'Poster frame render failed.' }
Copy-Item -LiteralPath (Join-Path $outputRoot 'self-test\ready.png') -Destination (Join-Path $outputRoot 'rem-codex-crystal-wake-poster.png') -Force

$ffprobePath = Join-Path (Split-Path -Parent $ffmpegPath) 'ffprobe.exe'
if (Test-Path -LiteralPath $ffprobePath) {
    & $ffprobePath -v error -select_streams v:0 -show_entries stream=width,height,r_frame_rate,codec_name -show_entries format=duration -of default=noprint_wrappers=1 $moviePath
    if ($LASTEXITCODE -ne 0) { throw 'Video verification failed.' }
}
Write-Output ('Video generated: ' + $moviePath)
