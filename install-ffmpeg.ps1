$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$toolsRoot = Join-Path $projectRoot 'tools'
$archivePath = Join-Path $toolsRoot 'ffmpeg-release-essentials.zip'
$installRoot = Join-Path $toolsRoot 'ffmpeg-dist'
$downloadUrl = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'

New-Item -ItemType Directory -Path $toolsRoot -Force | Out-Null
if (-not (Test-Path -LiteralPath $archivePath)) {
    Write-Output 'Downloading portable FFmpeg...'
    try {
        Invoke-WebRequest -Uri $downloadUrl -OutFile $archivePath -UseBasicParsing
    } catch {
        $python = Get-Command python -ErrorAction SilentlyContinue
        if ($null -eq $python) { throw }
        & $python.Source -c "import urllib.request, shutil; r=urllib.request.urlopen('$downloadUrl', timeout=60); f=open(r'$archivePath','wb'); shutil.copyfileobj(r,f,1024*1024); f.close()"
        if ($LASTEXITCODE -ne 0) { throw 'FFmpeg download failed.' }
    }
}

Expand-Archive -LiteralPath $archivePath -DestinationPath $installRoot -Force
$ffmpeg = Get-ChildItem -LiteralPath $installRoot -Recurse -Filter 'ffmpeg.exe' | Select-Object -First 1
if ($null -eq $ffmpeg) { throw 'ffmpeg.exe was not found in the archive.' }
& $ffmpeg.FullName -version | Select-Object -First 1
Write-Output ('FFmpeg installed in project: ' + $ffmpeg.FullName)
