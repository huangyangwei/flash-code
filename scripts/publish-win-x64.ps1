param(
    [string]$OutputDirectory = (Join-Path (Join-Path $PSScriptRoot '..\artifacts') ("flash-client-win-x64-" + [guid]::NewGuid().ToString('N')))
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$intermediate = "${destination}-obj/"
$buildOutput = "${destination}-bin/"
if ((Test-Path -LiteralPath $destination) -or
    (Test-Path -LiteralPath $intermediate) -or
    (Test-Path -LiteralPath $buildOutput)) {
    throw "Publish destination or build intermediates already exist: $destination"
}
if (-not (Test-Path -LiteralPath (Join-Path $projectRoot 'ClientWeb\node_modules'))) {
    throw 'ClientWeb dependencies are missing; run npm ci in ClientWeb before publishing.'
}

Push-Location (Join-Path $projectRoot 'ClientWeb')
try {
    & npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed' }
}
finally { Pop-Location }

& dotnet publish (Join-Path $projectRoot 'RoomCodeGrabber\RoomCodeGrabber.csproj') -c Release -p:PublishProfile=WindowsPortable -p:DefineConstants=RELEASE "-p:BaseIntermediateOutputPath=$intermediate" "-p:BaseOutputPath=$buildOutput" -o $destination
if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed' }

$executable = Join-Path $destination 'RoomCodeGrabber.exe'
$index = Join-Path $destination 'app\index.html'
$assets = Join-Path $destination 'app\assets'
if (-not (Test-Path -LiteralPath $executable) -or
    -not (Test-Path -LiteralPath $index) -or
    -not (Test-Path -LiteralPath $assets) -or
    -not (Get-ChildItem -LiteralPath $assets -File | Where-Object Extension -eq '.js')) {
    throw "Publish is incomplete: $destination"
}

Write-Output "Runnable client: $executable"
Write-Output 'Keep the complete publish directory together. Microsoft Edge WebView2 Evergreen Runtime is required on the target PC; Node/Vite are not.'
