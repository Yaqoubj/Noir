$ErrorActionPreference='Stop'
$projectRoot=Join-Path $PSScriptRoot '../Noir.Android'
$signingRoot=Join-Path $projectRoot '.signing'
New-Item -ItemType Directory -Force $signingRoot | Out-Null
if(!(Test-Path "$signingRoot/noir.p12")) {
    $pass=([Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)))
    Set-Content -LiteralPath "$signingRoot/password.txt" -Value $pass -NoNewline
    & "$env:JAVA_HOME/bin/keytool.exe" -genkeypair -keystore "$signingRoot/noir.p12" -storetype PKCS12 -alias noir -storepass:file "$signingRoot/password.txt" -keypass:file "$signingRoot/password.txt" -keyalg RSA -keysize 3072 -validity 10000 -dname 'CN=Noir Android, O=Noir'
    if($LASTEXITCODE -ne 0) {throw 'Signing key creation failed'}
}
