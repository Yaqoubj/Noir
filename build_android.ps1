$ErrorActionPreference='Stop'
$androidTools=Join-Path $PSScriptRoot 'work/android-tools'
if(Test-Path "$androidTools/gradle-8.11.1/bin/gradle.bat") {
    $gradleExecutable="$androidTools/gradle-8.11.1/bin/gradle.bat"
    $env:JAVA_HOME="$androidTools/jdk"
    $env:ANDROID_HOME="$androidTools/sdk"
    $env:GRADLE_USER_HOME="$androidTools/gradle-cache"
} else {
    $gradleExecutable=(Get-Command gradle -ErrorAction Stop).Source
    if(!$env:JAVA_HOME -or !$env:ANDROID_HOME) {throw 'Install JDK 17+, Gradle 8.11.1 and Android SDK 36, then set JAVA_HOME and ANDROID_HOME.'}
}
$env:PATH="$env:JAVA_HOME/bin;$env:PATH"
Push-Location (Join-Path $PSScriptRoot 'Noir.Android')
try {
    & $gradleExecutable --no-daemon --no-watch-fs assembleDebug assembleDebugAndroidTest lintDebug
    if($LASTEXITCODE -ne 0) {throw 'Android build failed'}
    New-Item -ItemType Directory -Force "$PSScriptRoot/outputs/Android" | Out-Null
    Copy-Item app/build/outputs/apk/debug/app-debug.apk "$PSScriptRoot/outputs/Android/Noir-Android-Debug.apk" -Force
} finally {Pop-Location}
