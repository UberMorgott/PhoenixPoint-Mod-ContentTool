# Downscale the kit's shared base-colour atlas to the size this mod ships, pixel-for-pixel (the UV
# layout is untouched, so the mesh keeps landing on the same islands).
#
# WHY 1024 and not the kit's 2048: ContentTool writes a baked Texture2D uncompressed RGBA32 with a
# single mip (BundleBaker.FillTexture2D), so 2048 would be 16 MB inside the mod's own bundle
# against 4 MB at 1024 - for a gun that is a few hundred pixels tall on screen. The atlas is shared
# across a batch of guns and this rifle's UVs span most of it, so cropping is not available;
# downscaling is.
#
# The colours are NOT changed here. WeaponMesh recolours its atlas because it is impersonating a
# Phoenix weapon; this mod ships a NEW weapon and the kit's own look is the point.
param(
    [string]$In  = "$PSScriptRoot\source\T_Guns_Batch2_BaseColor.png",
    [string]$Out = "$PSScriptRoot\..\Content\Textures\sniper.png",
    [int]$Size = 1024
)

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null

$src = [System.Drawing.Image]::FromFile($In)
try {
    $srcW = $src.Width
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($src, 0, 0, $Size, $Size)
    $g.Dispose()
} finally { $src.Dispose() }

$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

# Self-check: the file exists, is the requested size, and is not a blank sheet.
$check = [System.Drawing.Image]::FromFile($Out)
try {
    if ($check.Width -ne $Size -or $check.Height -ne $Size) { throw "wrong size: $($check.Width)x$($check.Height)" }
    $probe = New-Object System.Drawing.Bitmap $check
    $distinct = @{}
    for ($y = 0; $y -lt $Size; $y += 64) {
        for ($x = 0; $x -lt $Size; $x += 64) { $distinct[$probe.GetPixel($x, $y).ToArgb()] = 1 }
    }
    $probe.Dispose()
    if ($distinct.Count -lt 8) { throw "only $($distinct.Count) distinct colours sampled - this is not an atlas" }
} finally { $check.Dispose() }

"OK  $Out  ${Size}x${Size}  $((Get-Item $Out).Length) bytes  (from ${srcW}px, $($distinct.Count) colours sampled)"
