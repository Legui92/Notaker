param([Parameter(Mandatory=$true)][string]$NotesPath)
$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    if (git status --porcelain) { throw 'Guarda los cambios en un commit antes de publicar.' }
    [xml]$project = Get-Content src/Notaker/Notaker.csproj
    $version = $project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'La versión debe ser major.minor.patch.' }
    $commit = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'No se encuentra el commit.' }
    $notes = (Resolve-Path -LiteralPath $NotesPath).Path
    ./scripts/publish.ps1
    gh release create "v$version" artifacts/Notaker/Notaker.exe artifacts/Notaker-win-x64.zip --repo Legui92/Notaker --target $commit --title "Notaker $version" --notes-file $notes --latest
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear la release. No se reemplazan publicaciones existentes.' }
} finally { Pop-Location }
