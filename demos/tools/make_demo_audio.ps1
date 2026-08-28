<#
  Every sound the three audio demos ship, generated here rather than fetched. A sine under an
  envelope is OURS by construction: no performance, no sample, no library, so there is no licence
  to chase and nothing of anyone else's is redistributed with this repository. See each demo's
  SOURCES.md - they all point back at this file.

  The same rule as make_placeholders.ps1 next door, applied to audio. Drop YOUR file over any of
  these (same name) and re-run the demo's bake - the plumbing does not care which file it is.

    ct_sound bake MenuMusic
    ct_sound bake ReplaceUiSounds
    ct_project AddUiSounds

  Lengths are deliberately different from each other and from the media each one replaces, because
  the gates prove identity by fDuration, not by a file name.
#>
$ErrorActionPreference = 'Stop'
$demos = Split-Path $PSScriptRoot -Parent

# One note: a sine under an exponential decay, with a 10 ms attack so the onset is not a click.
# t0 = when it starts, f = frequency, len = how long it may ring, a = peak, d = how fast it dies.
function Note([double]$t0, [double]$f, [double]$len, [double]$a, [double]$d) {
    "$a*sin(2*PI*$f*t)*exp(-$d*(t-$t0))*min(1,(t-$t0)/0.01)*between(t,$t0,$($t0 + $len))"
}

function Render([string]$project, [string]$rel, [string]$expr, [double]$dur, [int]$kbps) {
    $path = Join-Path $demos "$project\$rel"
    New-Item -ItemType Directory -Force -Path (Split-Path $path -Parent) | Out-Null
    & ffmpeg -y -hide_banner -loglevel error `
        -f lavfi -i "aevalsrc='$expr':d=${dur}:s=44100:c=mono" `
        -af "afade=t=in:st=0:d=0.005,afade=t=out:st=$($dur - 0.02):d=0.02" `
        -c:a libmp3lame -b:a ${kbps}k $path
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed on $project\$rel" }
    Write-Host ("wrote {0}\{1}  ({2} B)" -f $project, $rel, (Get-Item $path).Length)
}

# --- MenuMusic: one 12 s loop, A minor pentatonic over a two-note drone. It replaces a 128 s track
# and it is allowed to: the bake writes whatever it is handed, and a menu loop that is 12 s instead
# of 128 s is 1 MB of bank instead of 24 MB. Both editions get the same loop (which is what the
# two files were before, too - the edition decides which media the game asks for, not what is in it).
$arp = @()
$scale = 220.00, 261.63, 329.63, 392.00, 440.00, 392.00, 329.63, 261.63
# The amplitudes are set so the loop lands near -15 LUFS, which is where game music is mixed and
# what the track this replaced was hand-adjusted to. Measured after each change with
# `ffmpeg -i <file> -af ebur128 -f null -`.
for ($i = 0; $i -lt 16; $i++) { $arp += Note ($i * 0.75) $scale[$i % 8] 0.9 0.36 2.2 }
$menu = ((, "0.18*sin(2*PI*110*t)+0.09*sin(2*PI*164.81*t)") + $arp) -join '+'
Render 'MenuMusic' 'Content\Audio\Replace\208540756.mp3' $menu 12 96
Copy-Item (Join-Path $demos 'MenuMusic\Content\Audio\Replace\208540756.mp3') `
          (Join-Path $demos 'MenuMusic\Content\Audio\Replace\423563089.mp3') -Force
Write-Host 'wrote MenuMusic\Content\Audio\Replace\423563089.mp3 (a copy - same loop, other edition)'

# --- AddUiSounds: two blips the game never had, one at random on Alt+B. The stems are what the
# bake names the events after, so renaming one means editing Clips[] in src\AddUiSoundsMain.cs.
Render 'AddUiSounds' 'Content\Audio\blip_rise.mp3' `
    (((Note 0.00 880.00 0.14 0.32 16), (Note 0.10 1318.51 0.24 0.32 11)) -join '+') 0.35 64
Render 'AddUiSounds' 'Content\Audio\blip_fall.mp3' `
    (((Note 0.00 1318.51 0.14 0.30 14), (Note 0.12 987.77 0.14 0.30 14),
      (Note 0.24 659.25 0.22 0.30 10)) -join '+') 0.45 64

# --- ReplaceUiSounds: three geoscape UI sounds. Short blips only - the shipped media they replace
# are 1200 / 3533 / 2231 ms, and these are 300 / 400 / 550, which is the discriminator.
Render 'ReplaceUiSounds' 'Content\Audio\Replace\sting_plus.mp3' `
    (((Note 0.00 1567.98 0.20 0.34 20), (Note 0.00 2349.32 0.12 0.12 30)) -join '+') 0.30 64
Render 'ReplaceUiSounds' 'Content\Audio\Replace\sting_confirm.mp3' `
    (((Note 0.00 659.25 0.16 0.30 14), (Note 0.14 1318.51 0.26 0.30 10)) -join '+') 0.40 64
# A falling buzz: the third harmonic gives it a reedy edge no shipped click has.
Render 'ReplaceUiSounds' 'Content\Audio\Replace\sting_cancel.mp3' `
    (((Note 0.00 220.00 0.30 0.26 6), (Note 0.00 660.00 0.30 0.09 6),
      (Note 0.26 146.83 0.29 0.26 5), (Note 0.26 440.49 0.29 0.09 5)) -join '+') 0.55 64
