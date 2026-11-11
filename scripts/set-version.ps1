# Sets the release numbers everywhere they are written:
#   Noir/Noir.csproj, install_noir.ps1, README.md, Noir.Android/app/build.gradle
# Example: powershell -File scripts/set-version.ps1 -Windows 0.18.1 -Android 0.11.1 -Code 17
# This file is plain ASCII on purpose: Windows PowerShell 5.1 reads a script without a BOM in the system code page.
param(
  [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Windows,
  [Parameter(Mandatory=$true)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Android,
  [Parameter(Mandatory=$true)][ValidateRange(1,2100000000)][int]$Code
)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot

function Set-Number([string]$Path,[string]$Pattern,[string]$Replacement){
  $full=Join-Path $repo $Path
  $bytes=[IO.File]::ReadAllBytes($full)
  $bom=$bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
  $text=[IO.File]::ReadAllText($full,[Text.Encoding]::UTF8)
  $count=([regex]::Matches($text,$Pattern)).Count
  if($count -ne 1){throw "$Path : expected one match for $Pattern, found $count"}
  $updated=[regex]::Replace($text,$Pattern,$Replacement)
  if($updated -ne $text){[IO.File]::WriteAllText($full,$updated,(New-Object Text.UTF8Encoding($bom)))}
  "$Path : $(([regex]::Match($updated,$Pattern)).Value)"
}

$dot=[string][char]0x00B7
Set-Number 'Noir/Noir.csproj' '<Version>\d+\.\d+\.\d+</Version>' "<Version>$Windows</Version>"
Set-Number 'install_noir.ps1' "'Noir\.version'\) -Value '\d+\.\d+\.\d+'" "'Noir.version') -Value '$Windows'"
Set-Number 'README.md' "Windows \d+\.\d+\.\d+ $dot Android \d+\.\d+\.\d+" "Windows $Windows $dot Android $Android"
Set-Number 'Noir.Android/app/build.gradle' "versionCode \d+; versionName '\d+\.\d+\.\d+'" "versionCode $Code; versionName '$Android'"
