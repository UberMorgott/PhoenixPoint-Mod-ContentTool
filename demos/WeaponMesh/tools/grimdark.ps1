# Recolor a Quaternius gun atlas toward a grimdark look, pixel-for-pixel (UV layout untouched).
# Amber/orange accents -> crimson hazard; everything else desaturated + darkened toward cold iron.
param(
    [string]$In  = "$PSScriptRoot\quaternius-gun-rifle\T_Guns_Batch1_BaseColor.png",
    [string]$Out = "$PSScriptRoot\grimdark\T_Guns_Batch1_BaseColor_grimdark.png",
    [int]$Size = 1024
)

Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path (Split-Path $Out) | Out-Null

$src = [System.Drawing.Image]::FromFile($In)
try {
    # Downscale first: 2048 -> $Size. Fewer pixels to walk, and it is the size we ship.
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($src, 0, 0, $Size, $Size)
    $g.Dispose()
} finally { $src.Dispose() }

# LockBits: per-pixel GetPixel/SetPixel on 1M pixels takes minutes, this takes under a second.
$rect = New-Object System.Drawing.Rectangle 0, 0, $Size, $Size
$data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadWrite,
                      [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
$count = $Size * $Size * 4
$buf = New-Object byte[] $count
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $count)

$accent = 0   # pixels recognised as amber/orange accent
for ($i = 0; $i -lt $count; $i += 4) {
    # BGRA
    $b = $buf[$i]; $gr = $buf[$i+1]; $r = $buf[$i+2]
    if ($buf[$i+3] -eq 0) { continue }

    $max = [Math]::Max($r, [Math]::Max($gr, $b))
    $min = [Math]::Min($r, [Math]::Min($gr, $b))
    $sat = if ($max -eq 0) { 0 } else { ($max - $min) / [double]$max }

    if ($sat -gt 0.35 -and $r -gt $gr -and $gr -gt $b) {
        # Amber/orange accent -> crimson hazard: keep the luminance, crush green, kill blue.
        # Dried-blood / red-oxide, not fire-engine red: pull the red down and leave a little
        # green in so it reads as pigment on metal rather than a pure channel.
        $accent++
        $nr = [Math]::Min(255, [int]($r * 0.62 + 18))
        $ng = [int]($gr * 0.30 + 8)
        $nb = [int]($b  * 0.26 + 8)
    } else {
        # Everything else -> cold desaturated iron, darkened.
        $lum = 0.299 * $r + 0.587 * $gr + 0.114 * $b
        $nr = [int]([Math]::Max(0, $lum * 0.62 + 2))
        $ng = [int]([Math]::Max(0, $lum * 0.63 + 3))
        $nb = [int]([Math]::Max(0, $lum * 0.68 + 6))   # slight blue cast = cold steel
    }
    $buf[$i]   = [byte][Math]::Min(255, $nb)
    $buf[$i+1] = [byte][Math]::Min(255, $ng)
    $buf[$i+2] = [byte][Math]::Min(255, $nr)
}

[System.Runtime.InteropServices.Marshal]::Copy($buf, 0, $data.Scan0, $count)
$bmp.UnlockBits($data)
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()

# Self-check: the file exists, is the requested size, and the accent rule actually fired.
$check = [System.Drawing.Image]::FromFile($Out)
try {
    if ($check.Width -ne $Size -or $check.Height -ne $Size) { throw "wrong size: $($check.Width)x$($check.Height)" }
} finally { $check.Dispose() }
if ($accent -eq 0) { throw "no accent pixels matched - the orange->crimson rule never fired, check the thresholds" }

$bytes = (Get-Item $Out).Length
"OK  $Out  ${Size}x${Size}  $bytes bytes  accent pixels recoloured: $accent"
