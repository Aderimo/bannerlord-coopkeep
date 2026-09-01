# CoopKeep'i tek dosyalik, kendi kendine yeten bir exe olarak yayinlar.
#
# Sonuc: C:\dev\CoopKeep\CoopKeep.exe
#
# "Kendi kendine yeten" (self-contained) olmasi onemli: kullanicinin makinesinde
# .NET kurulu olmasi gerekmiyor. Indirip cift tiklayan herkes calistirabiliyor.

$ErrorActionPreference = 'Stop'

$root    = $PSScriptRoot
$proj    = Join-Path $root 'src\CoopKeep.App\CoopKeep.App.csproj'
$staging = Join-Path $root 'artifacts\publish'
$dotnet  = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe'

if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }

Write-Host "CoopKeep yayinlaniyor..." -ForegroundColor Cyan

& $dotnet publish $proj `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $staging `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:GenerateDocumentationFile=false

if ($LASTEXITCODE -ne 0) { throw "Yayinlama basarisiz (exit $LASTEXITCODE)" }

$built = Join-Path $staging 'CoopKeep.exe'
if (-not (Test-Path $built)) { throw "Beklenen exe olusmadi: $built" }

$target = Join-Path $root 'CoopKeep.exe'
Copy-Item $built $target -Force

$mb = [math]::Round((Get-Item $target).Length / 1MB, 1)
Write-Host ""
Write-Host "Hazir: $target  ($mb MB)" -ForegroundColor Green
Write-Host "Cift tiklayarak calistirilabilir. .NET kurulumu gerekmiyor."
