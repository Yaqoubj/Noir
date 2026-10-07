$ErrorActionPreference='Stop'
$exe=Join-Path $PSScriptRoot 'Noir.exe'
if(!(Test-Path -LiteralPath $exe) -or !(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Noir.version'))) {throw 'Run the copy installed next to Noir.exe.'}
$existingRule=Get-NetFirewallRule -Name 'NoirAndroidLocal' -ErrorAction SilentlyContinue
if($existingRule) {$existingRule | Remove-NetFirewallRule}
New-NetFirewallRule -Name 'NoirAndroidLocal' -DisplayName 'Noir Android (local networks)' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 443,49271 -Program $exe -RemoteAddress '10.0.0.0/8','172.16.0.0/12','192.168.0.0/16' -Profile Any -EdgeTraversalPolicy Block | Out-Null
