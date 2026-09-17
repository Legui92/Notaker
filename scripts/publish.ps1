$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet publish src/Notaker/Notaker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -o artifacts/Notaker --configfile NuGet.Config
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar Notaker.' }
    Copy-Item README.md artifacts/Notaker/LEEME.md
    Copy-Item THIRD-PARTY-NOTICES.md artifacts/Notaker/THIRD-PARTY-NOTICES.md
    $releaseFiles = @('artifacts/Notaker/Notaker.exe', 'artifacts/Notaker/LEEME.md', 'artifacts/Notaker/THIRD-PARTY-NOTICES.md')
    Compress-Archive -Path $releaseFiles -DestinationPath artifacts/Notaker-win-x64.zip -Force
    Write-Host 'Listo: artifacts/Notaker/Notaker.exe y artifacts/Notaker-win-x64.zip'
} finally { Pop-Location }
