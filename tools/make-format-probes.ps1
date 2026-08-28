<#
  Writes the format-coverage probe corpus into <mod>\FormatProbes\ for `ct_fmt`.

  DEV-ONLY SCAFFOLDING. ffmpeg is used HERE, to MANUFACTURE probe files, and nowhere in the tool:
  the "no external tools" mandate is about what a mod AUTHOR must install, not about how a
  measurement gets its input. Nothing this writes is committed and nothing ships.

  Every probe is deliberately tiny (128x72 video, 0.5 s audio) so a whole run costs seconds, and
  every family carries a JUNK negative control - a decoder that "succeeds" on random bytes proves
  nothing about the ones it succeeded on.
#>
param(
    [string] $PPRoot = 'D:\Steam\steamapps\common\Phoenix Point'
)

$ErrorActionPreference = 'Stop'
$dir = Join-Path $PPRoot 'Mods\ContentTool\FormatProbes'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
Get-ChildItem $dir -File -ErrorAction SilentlyContinue | Remove-Item -Force

if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) { throw 'ffmpeg is not on PATH' }
function FF { & ffmpeg -hide_banner -loglevel error -y @args; if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed: $args" } }

$vsrc = @('-f', 'lavfi', '-i', 'testsrc=size=128x72:rate=15:duration=1')
$asrc = @('-f', 'lavfi', '-i', 'sine=frequency=440:duration=0.5:sample_rate=44100')

# --- textures. One 64x64 source frame, re-muxed into every still container ffmpeg can write.
FF @vsrc '-frames:v' 1 -vf scale=64:64 (Join-Path $dir 'tex.png')
FF @vsrc '-frames:v' 1 -vf scale=64:64 (Join-Path $dir 'tex.jpg')
FF @vsrc '-frames:v' 1 -vf scale=64:64 (Join-Path $dir 'tex.bmp')
FF @vsrc '-frames:v' 1 -vf scale=64:64 (Join-Path $dir 'tex.tga')
# ffmpeg has a DDS *decoder* and no encoder. A DDS is a 128-byte header in front of raw pixels,
# so the probe is written by hand rather than skipped: DDPF_RGB|DDPF_ALPHAPIXELS, 32 bpp B8G8R8A8,
# uncompressed, no mips - the simplest DDS that exists, so a refusal cannot be blamed on BC7.
$w = 64; $h = 64
$hdr = [System.Collections.Generic.List[byte]]::new()
$hdr.AddRange([byte[]][char[]]'DDS ')
foreach ($d in 124, 0x0000100F, $h, $w, ($w * 4), 0, 0) { $hdr.AddRange([BitConverter]::GetBytes([uint32]$d)) }
foreach ($d in 1..11) { $hdr.AddRange([BitConverter]::GetBytes([uint32]0)) }          # dwReserved1
foreach ($d in 32, 0x41, 0, 32) { $hdr.AddRange([BitConverter]::GetBytes([uint32]$d)) }  # DDS_PIXELFORMAT
foreach ($d in 0x00FF0000, 0x0000FF00, 0x000000FF, 4278190080) { $hdr.AddRange([BitConverter]::GetBytes([uint32]$d)) }
foreach ($d in 0x1000, 0, 0, 0, 0) { $hdr.AddRange([BitConverter]::GetBytes([uint32]$d)) }
$px = New-Object byte[] ($w * $h * 4)
for ($i = 0; $i -lt $px.Length; $i += 4) { $px[$i] = 40; $px[$i + 1] = 160; $px[$i + 2] = 220; $px[$i + 3] = 255 }
$hdr.AddRange($px)
[IO.File]::WriteAllBytes((Join-Path $dir 'tex.dds'), $hdr.ToArray())

# --- audio. PCM16 wav is the known-good control; the rest are the question.
FF @asrc '-c:a' pcm_s16le            (Join-Path $dir 'aud.wav')
FF @asrc '-c:a' pcm_s24le            (Join-Path $dir 'aud24.wav')
FF @asrc '-c:a' pcm_f32le            (Join-Path $dir 'aud32f.wav')
FF @asrc '-c:a' libmp3lame '-b:a' 64k  (Join-Path $dir 'aud.mp3')
FF @asrc '-c:a' libvorbis '-q:a' 2     (Join-Path $dir 'aud.ogg')
FF @asrc '-c:a' flac                 (Join-Path $dir 'aud.flac')
FF @asrc '-c:a' aac '-b:a' 64k         (Join-Path $dir 'aud.m4a')

# --- video. The CONTROL is not an ffmpeg product: lib\v1_probe.webm is the clip gate V1 already
# plays in game, so it separates "this build cannot open the container" from "ffmpeg muxed it in a
# flavour Unity dislikes". vid.webm below is ffmpeg's own VP8 and is a question, not a control.
Copy-Item (Join-Path $PSScriptRoot '..\lib\v1_probe.webm') (Join-Path $dir 'vidctl.webm') -Force
FF @vsrc @asrc -shortest '-c:v' libvpx '-b:v' 200k '-c:a' libvorbis (Join-Path $dir 'vid.webm')
FF @vsrc @asrc -shortest '-c:v' libvpx-vp9 '-b:v' 200k '-c:a' libvorbis (Join-Path $dir 'vid9.webm')
FF @vsrc @asrc -shortest '-c:v' libx264 -pix_fmt yuv420p '-c:a' aac (Join-Path $dir 'vid.mp4')
FF @vsrc @asrc -shortest '-c:v' libx264 -pix_fmt yuv420p '-c:a' aac (Join-Path $dir 'vid.mov')
FF @vsrc @asrc -shortest '-c:v' libx264 -pix_fmt yuv420p '-c:a' libmp3lame (Join-Path $dir 'vid.mkv')
FF @vsrc @asrc -shortest '-c:v' mpeg4 -pix_fmt yuv420p '-c:a' libmp3lame (Join-Path $dir 'vid.avi')

# --- negative controls: random bytes wearing each extension.
$rng = New-Object byte[] 4096
(New-Object Random 7).NextBytes($rng)
foreach ($n in 'tex.junk', 'aud.junk', 'vid.junk') { [IO.File]::WriteAllBytes((Join-Path $dir $n), $rng) }

Get-ChildItem $dir -File | Sort-Object Name | ForEach-Object { '{0,-12} {1,9} B' -f $_.Name, $_.Length }
Write-Host "probes in $dir"
