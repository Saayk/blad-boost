# Blad Boost - compila y publica el release en GitHub desde un PC con Windows.
# Uso:  powershell -ExecutionPolicy Bypass -File publicar-release.ps1
# Requisitos: Git y GitHub CLI (gh) con sesion iniciada (gh auth login).
# Si no tienes gh, el script compila igualmente y te explica como subir el .exe a mano.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

# 1) Compilar (genera dist\GameBoost_Setup.exe y dist\version.json)
& "$PSScriptRoot\build.ps1"
$setup = Join-Path $PSScriptRoot 'dist\GameBoost_Setup.exe'
if (-not (Test-Path $setup)) { throw 'No se genero dist\GameBoost_Setup.exe' }

$info = Get-Content (Join-Path $PSScriptRoot 'dist\version.json') -Raw | ConvertFrom-Json
$tag = 'v' + $info.version

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
  Write-Host ""
  Write-Host "Compilado: $setup"
  Write-Host "No se encontro 'gh'. Sube el instalador a mano:"
  Write-Host "  https://github.com/Saayk/blad-boost/releases/new  (tag $tag)"
  Write-Host "  Adjunta GameBoost_Setup.exe (el nombre debe ser exactamente ese)."
  exit 0
}

# 2) No duplicar un release que ya exista
$ErrorActionPreference = 'Continue'
gh release view $tag *> $null
$existe = ($LASTEXITCODE -eq 0)
$ErrorActionPreference = 'Stop'
if ($existe) { throw "El release $tag ya existe. Sube la version en build.ps1, App.cs, Setup.cs, Services.cs y version.json." }

# 3) Publicar
gh release create $tag $setup (Join-Path $PSScriptRoot 'dist\version.json') --target main --title "Blad Boost $tag" --notes $info.notes
if ($LASTEXITCODE -ne 0) { throw 'Fallo al crear el release' }
Write-Host "Release $tag publicado."
