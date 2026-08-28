<#
  The cutscene's SOUND, generated here rather than fetched - three plucked notes, one under each
  subtitle line, so what you hear lines up with what you read and with the 6 s card in
  Content\Videos\. A sine with an envelope is ours by construction: no licence to chase, no one's
  track redistributed. See SOURCES.md.

  Drop YOUR file over Content\Audio\Replace\intro_theme.mp3 (.wav and .ogg work too - ContentTool
  decodes all three itself) and re-run 'ct_sound bake IntroVideo'. Length is free: the shipped
  media is 121.3 s and this one is 6 s, because the bake writes whatever it is handed.

  The VIDEO card is NOT made here - it comes from demos\tools\make_placeholders.ps1, which owns
  every demo's placeholder clip.
#>
$ErrorActionPreference = 'Stop'
$out = Join-Path (Split-Path $PSScriptRoot -Parent) 'Content\Audio\Replace'
New-Item -ItemType Directory -Force -Path $out | Out-Null

# One plucked note: a sine under an exponential decay, with a 10 ms attack so the onset is not a
# click. t0 = when it starts, f = its frequency, len = how long it may ring.
function Note([double]$t0, [double]$f, [double]$len) {
    "0.30*sin(2*PI*$f*t)*exp(-2.2*(t-$t0))*min(1,(t-$t0)/0.01)*between(t,$t0,$($t0+$len))"
}

# A3, C#4, E4 - an A-major triad, arpeggiated. The three start times are the three subtitle cues
# in Content\Subtitles\campaign_intro.srt, kept in step by hand: three numbers, one file each.
$expr = (Note 0.2 220.00 1.8), (Note 2.2 277.18 1.8), (Note 4.2 329.63 1.7) -join '+'

& ffmpeg -y -hide_banner -loglevel error `
    -f lavfi -i "aevalsrc='$expr':d=6:s=44100:c=mono" `
    -af 'afade=t=out:st=5.85:d=0.15' `
    -c:a libmp3lame -b:a 96k (Join-Path $out 'intro_theme.mp3')
if ($LASTEXITCODE -ne 0) { throw 'ffmpeg failed' }
Write-Host "wrote Content\Audio\Replace\intro_theme.mp3"
