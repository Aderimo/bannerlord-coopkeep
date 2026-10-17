# CoopKeep görselli indirme paketi.
#
# Kod reposuna görsel girmez (TaleWorlds'ün görselleri). Bu betik, yayınlarken ayrı
# yüklenecek zip'i hazırlar:
#   1. Wiki görselleri:   coopkeep images image-pack      (CoopKeep.Cli)
#   2. Oyundan çıkarılan: %LOCALAPPDATA%\CoopKeep\images  (CoopKeep.ImageExporter modülü)
#      Bunlar wiki'dekilerin önüne geçer (eşyalar yalnızca buradan gelir).
#   3. Büyük görseller küçültülür, her şey zip'lenir.
#
# Kullanım:  powershell -File tools\build-image-pack.ps1 [-PackDir image-pack] [-Out artifacts\CoopKeep-Gorseller.zip]
param(
    [string]$PackDir = (Join-Path $PSScriptRoot "..\image-pack"),
    [string]$Exported = (Join-Path $env:LOCALAPPDATA "CoopKeep\images\game"),
    [string]$Out = (Join-Path $PSScriptRoot "..\artifacts\CoopKeep-Gorseller.zip"),
    [int]$MaxWidth = 720
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$stage = Join-Path ([IO.Path]::GetTempPath()) ("coopkeep-pack-" + [Guid]::NewGuid().ToString("N"))
$images = Join-Path $stage "images"
New-Item -ItemType Directory -Force $images | Out-Null

# 1) wiki görselleri
if (Test-Path $PackDir) {
    Get-ChildItem $PackDir -Directory | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $images $_.Name) -Recurse -Force
    }
}

# 2) oyundan çıkarılanlar (aynı adlı wiki görselinin üstüne yazar)
foreach ($kind in "item", "troop", "lord", "clanbanner") {
    $src = Join-Path $Exported $kind
    if (-not (Test-Path $src)) { continue }
    $dst = Join-Path $images $kind
    New-Item -ItemType Directory -Force $dst | Out-Null
    Get-ChildItem $src -File | ForEach-Object {
        # Aynı ada sahip .jpg varsa kaldır: .png öncelikli okunur ama paket temiz kalsın.
        $jpg = Join-Path $dst ($_.BaseName + ".jpg")
        if (Test-Path $jpg) { Remove-Item $jpg }
        Copy-Item $_.FullName $dst -Force
    }
}

# 3) büyük görselleri küçült (harita hariç: koordinat dönüşümü 1920 px'e göre)
$resized = 0
Get-ChildItem $images -Recurse -File -Include *.png, *.jpg | Where-Object { $_.Directory.Name -ne "map" } | ForEach-Object {
    $img = [System.Drawing.Image]::FromFile($_.FullName)
    try {
        if ($img.Width -le $MaxWidth) { return }
        $h = [int]($img.Height * $MaxWidth / $img.Width)
        $bmp = New-Object System.Drawing.Bitmap $MaxWidth, $h
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.DrawImage($img, 0, 0, $MaxWidth, $h)
        $g.Dispose()
    }
    finally { $img.Dispose() }

    $format = if ($_.Extension -eq ".jpg") { [System.Drawing.Imaging.ImageFormat]::Jpeg } else { [System.Drawing.Imaging.ImageFormat]::Png }
    $bmp.Save($_.FullName, $format)
    $bmp.Dispose()
    $resized++
}

# index.json ve geçici dosyalar pakette gerekmez (CoopKeep dosyaları doğrudan okur).
Get-ChildItem $images -File -Filter *.json | Remove-Item
Get-ChildItem $images -Recurse -File -Filter *.tmp | Remove-Item

@"
CoopKeep görsel paketi
======================
Bu klasörü CoopKeep.exe'nin yanına "images" adıyla koy. CoopKeep görselleri internetten
indirmek yerine buradan okur.

Görseller TaleWorlds Entertainment'a aittir (Mount & Blade II: Bannerlord). Bir kısmı
topluluk wiki'lerinden (mountandblade.fandom.com, mountandblade2bannerlord.wiki.fextralife.com),
eşyalar oyunun kendi çizdiği küçük resimlerden alınmıştır. Yalnızca kişisel kullanım içindir.
"@ | Set-Content -Encoding UTF8 (Join-Path $images "OKU-BENI.txt")

New-Item -ItemType Directory -Force (Split-Path $Out) | Out-Null
if (Test-Path $Out) { Remove-Item $Out }
Compress-Archive -Path $images -DestinationPath $Out -CompressionLevel Optimal

$count = (Get-ChildItem $images -Recurse -File).Count
$size = "{0:N1} MB" -f ((Get-Item $Out).Length / 1MB)
Remove-Item $stage -Recurse -Force
"Paket: $Out  ($count dosya, $size, $resized küçültüldü)"
