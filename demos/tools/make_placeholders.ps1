<#
  The placeholder clips the video demos ship, generated here rather than fetched: a title card on a
  flat background is ours by construction, so no one's footage is redistributed. Drop YOUR file over
  any of these (same name) and re-run 'ct_video apply <project>' - the plumbing does not care which
  file it is, which is the whole design.

  VP8 video + Vorbis audio in WebM: what all 69 shipped clips are (they came out of Unity's own
  transcoder), and what F1 measured the engine to accept. .mp4 and .mov also play; .mkv and VP9 are
  REJECTED. A file downloaded from the web is very often AV1 + Opus - the container still says
  .webm and it still will not play. Convert first:
      ffmpeg -i yours.webm -c:v libvpx -b:v 1500k -c:a libvorbis -b:a 128k out.webm

  Frame counts are deliberately different from each other and from every shipped clip, because the
  gates prove identity by frameCount, not by a file name.
#>
$ErrorActionPreference = 'Stop'
$demos = Split-Path $PSScriptRoot -Parent

function Card([string]$project, [string]$file, [string]$big, [string]$small, [int]$seconds, [string]$bg, [string]$fg) {
    $out = Join-Path $demos "$project\Content\Videos"
    New-Item -ItemType Directory -Force -Path $out | Out-Null
    $bold = "C\:/Windows/Fonts/arialbd.ttf"
    $thin = "C\:/Windows/Fonts/arial.ttf"
    $vf = "drawtext=fontfile='$bold':text='$big':fontcolor=${fg}:fontsize=96:x=(w-text_w)/2:y=(h-text_h)/2-60," +
          "drawtext=fontfile='$thin':text='$small':fontcolor=0x9FB3C8:fontsize=40:x=(w-text_w)/2:y=(h-text_h)/2+70"
    & ffmpeg -y -hide_banner -loglevel error `
        -f lavfi -i "color=c=${bg}:s=1280x720:r=30:d=$seconds" `
        -f lavfi -i "sine=frequency=220:duration=$seconds" `
        -vf $vf -c:v libvpx -b:v 500k -c:a libvorbis -shortest (Join-Path $out $file)
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed on $file" }
    Write-Host "wrote $project\Content\Videos\$file"
}

# 6 s = 180 frames. Replaces a cutscene the game already plays.
Card 'IntroVideo'   'campaign_intro.webm' 'CONTENTTOOL'    'this replaces a shipped cutscene'  6 '0x0B0E11' '0xE8A33D'
# 4 s = 120 frames. A clip Phoenix Point never shipped.
# 3 s = 90 frames. Plays on quit, then the game exits.
Card 'QuitCutscene' 'quit_outro.webm'     'GOODBYE'        'drop your own clip over this file' 3 '0x0E0B11' '0xA33DE8'
