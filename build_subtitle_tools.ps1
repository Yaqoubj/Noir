$ErrorActionPreference='Stop'
$workspace=$PSScriptRoot
$venv=Join-Path $workspace 'work/subtitle-build'
if(!(Test-Path -LiteralPath "$venv/Scripts/python.exe")){& python -m venv $venv;if($LASTEXITCODE -ne 0){throw 'Could not create subtitle build environment'}}
& "$venv/Scripts/python.exe" -m pip install -r "$workspace/Noir.Tools/subtitle-sync/requirements.txt"
if($LASTEXITCODE -ne 0){throw 'Could not install subtitle build dependencies'}
& "$venv/Scripts/python.exe" -m PyInstaller --noconfirm --onedir --name NoirSubtitleSync --distpath "$workspace/work/subtitle-dist" --workpath "$workspace/work/subtitle-pyinstaller" --specpath "$workspace/work" --additional-hooks-dir "$workspace/Noir.Tools/subtitle-sync/hooks" --collect-all ffsubsync --copy-metadata ffsubsync "$workspace/Noir.Tools/subtitle-sync/subtitle_sync.py"
if($LASTEXITCODE -ne 0){throw 'Could not build subtitle alignment worker'}
$runtime=Join-Path $workspace 'work/subtitle-runtime'
New-Item -ItemType Directory -Force $runtime | Out-Null
Copy-Item -LiteralPath "$workspace/work/subtitle-dist/NoirSubtitleSync/NoirSubtitleSync.exe" -Destination $runtime -Force
Copy-Item -LiteralPath "$workspace/work/subtitle-dist/NoirSubtitleSync/_internal" -Destination $runtime -Recurse -Force
$ffmpeg=Get-Command ffmpeg -ErrorAction Stop
$ffmpegRoot=Split-Path $ffmpeg.Source
$ffmpegOutput=Join-Path $runtime 'ffmpeg'
New-Item -ItemType Directory -Force $ffmpegOutput | Out-Null
foreach($name in @('ffmpeg.exe','ffprobe.exe')){Copy-Item -LiteralPath (Join-Path $ffmpegRoot $name) -Destination $ffmpegOutput -Force}
Get-ChildItem -LiteralPath $ffmpegRoot -Filter '*.dll' -File | Copy-Item -Destination $ffmpegOutput -Force
foreach($name in @('LICENSE','README.txt')){if(Test-Path -LiteralPath (Join-Path (Split-Path $ffmpegRoot) $name)){Copy-Item -LiteralPath (Join-Path (Split-Path $ffmpegRoot) $name) -Destination $ffmpegOutput -Force}}
& "$venv/Scripts/python.exe" "$workspace/Noir.Tools/subtitle-sync/export_notices.py" $runtime
if($LASTEXITCODE -ne 0){throw 'Could not export subtitle dependency notices'}
$pythonLicense=Join-Path (Split-Path (Get-Command python).Source) 'LICENSE.txt'
if(Test-Path -LiteralPath $pythonLicense){New-Item -ItemType Directory -Force "$runtime/licenses/Python" | Out-Null;Copy-Item -LiteralPath $pythonLicense -Destination "$runtime/licenses/Python/LICENSE.txt" -Force}
Write-Output 'Subtitle alignment runtime ready. It runs only when requested and processes local media.'
