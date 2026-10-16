$ErrorActionPreference='Stop'
$packageRoot=Join-Path $PSScriptRoot 'outputs\Noir'
$appRoot=Join-Path $env:LOCALAPPDATA 'Programs\Noir'
if (!(Test-Path -LiteralPath (Join-Path $packageRoot 'Noir.exe'))) { throw 'Build the app before installing.' }
if (Test-Path -LiteralPath $appRoot) {
    if (!(Test-Path -LiteralPath (Join-Path $appRoot 'Noir.version'))) { throw 'This folder already exists and is not a Noir installation from this workspace.' }
}
New-Item -ItemType Directory -Path $appRoot -Force | Out-Null
$packagePrefix=[System.IO.Path]::GetFullPath($packageRoot).TrimEnd('\')+'\'
foreach ($packageFile in Get-ChildItem -LiteralPath $packageRoot -Recurse -File) {
    if (!$packageFile.FullName.StartsWith($packagePrefix,[System.StringComparison]::OrdinalIgnoreCase)) {throw 'Package file is outside the expected directory.'}
    $relativeName=$packageFile.FullName.Substring($packagePrefix.Length)
    $targetFile=Join-Path $appRoot $relativeName
    $installedFile=Get-Item -LiteralPath $targetFile -ErrorAction SilentlyContinue
    if (!$installedFile -or $installedFile.Length -ne $packageFile.Length -or $installedFile.LastWriteTimeUtc -ne $packageFile.LastWriteTimeUtc) {
        New-Item -ItemType Directory -Path (Split-Path $targetFile -Parent) -Force | Out-Null
        for($copyAttempt=0;;$copyAttempt++) {
            try {Copy-Item -LiteralPath $packageFile.FullName -Destination $targetFile -Force -ErrorAction Stop;break}
            catch [System.IO.IOException] {if($copyAttempt -ge 19){throw};Start-Sleep -Milliseconds 250}
        }
    }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Noir\README.md') -Destination (Join-Path $appRoot 'README.md')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Noir\THIRD-PARTY-NOTICES.txt') -Destination (Join-Path $appRoot 'NOIR-THIRD-PARTY-NOTICES.txt')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Noir\LGPL-2.1.txt') -Destination (Join-Path $appRoot 'LGPL-2.1.txt')
Set-Content -LiteralPath (Join-Path $appRoot 'Noir.version') -Value '0.13.0'
$shortcutTool=New-Object -ComObject WScript.Shell
$desktopPath=[Environment]::GetFolderPath('DesktopDirectory')
$startPath=[Environment]::GetFolderPath('Programs')
foreach ($targetFolder in @($desktopPath,$startPath)) {
    $shortcut=$shortcutTool.CreateShortcut((Join-Path $targetFolder 'Noir.lnk'))
    $shortcut.TargetPath=Join-Path $appRoot 'Noir.exe'
    $shortcut.WorkingDirectory=$appRoot
    $shortcut.IconLocation=(Join-Path $appRoot 'Noir.exe')+',0'
    $shortcut.Description='Your local movies and shows, with VLC playback and saved progress.'
    $shortcut.Save()
}
Write-Output ('Installed: '+(Join-Path $appRoot 'Noir.exe'))
Write-Output ('Desktop shortcut: '+(Join-Path $desktopPath 'Noir.lnk'))
Write-Output ('Start menu shortcut: '+(Join-Path $startPath 'Noir.lnk'))
