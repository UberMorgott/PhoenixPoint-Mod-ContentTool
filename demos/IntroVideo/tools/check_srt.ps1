<#
  The runnable check for Content\Subtitles\campaign_intro.srt.

  Phoenix Point's SRT reader is hand-written and has almost no bounds checks:
  ParserUtils.ParseLine:47 walks `text[pointer]` with no length test, and
  ParserUtils.SkipNewLineSymbols:226 does the same - so a file with no trailing newline throws
  IndexOutOfRangeException inside VideoPlaybackController.WarmUpPlayer:166 and takes the cutscene
  with it. A .srt an editor wrote is not automatically a .srt this parser survives.

  So this is a FAITHFUL PORT of the three functions the game actually runs, transcribed from
  decompiled\AssemblyCSharp\Assembly-CSharp\src\SubtitltesTool\ParserUtils.cs (ReadNextInt:244,
  ParseTimeValue:119, ParseLines:12) and SRTParser.cs:23 - deliberately including their missing
  bounds checks, so a file that crashes the game crashes here first, offline.

  Run:  pwsh -File tools\check_srt.ps1
#>
$ErrorActionPreference = 'Stop'
$path = Join-Path (Split-Path $PSScriptRoot -Parent) 'Content\Subtitles\campaign_intro.srt'
$text = [System.IO.File]::ReadAllText($path)
$script:p = 0

function Ch([int]$i) {
    if ($i -ge $text.Length) {
        throw "IndexOutOfRangeException at $i, from $((Get-PSCallStack)[1].FunctionName) (the game would throw here too)"
    }
    $text[$i]
}

function SkipTillNumber { while (-not [char]::IsDigit((Ch $script:p))) { $script:p++ } }
function SkipNewLineSymbols { while ((Ch $script:p) -eq "`n" -or (Ch $script:p) -eq "`r") { $script:p++ } }
function SkipNewLineSymbolsOnce {
    if ((Ch $script:p) -eq "`r") { $script:p++ }
    if ((Ch $script:p) -eq "`n") { $script:p++ }
}
function SkipTillNewLine {
    while ((Ch $script:p) -ne "`n" -and (Ch $script:p) -ne "`r") { $script:p++ }
    SkipNewLineSymbols
}

function ReadNextInt {
    SkipTillNumber
    $sb = ''
    while ((Ch $script:p) -ne "`n" -and (Ch $script:p) -ne "`r") { $sb += $text[$script:p]; $script:p++ }
    $script:p++
    [int]::Parse($sb)
}

function ParseTimeValue {
    $stack = New-Object System.Collections.Stack
    SkipTillNumber
    while ($script:p -lt $text.Length -and [char]::IsDigit($text[$script:p])) {
        $sb = ''
        while ($script:p -lt $text.Length -and [char]::IsDigit($text[$script:p])) { $sb += $text[$script:p]; $script:p++ }
        $script:p++
        $stack.Push([int]$sb)
    }
    $num = $stack.Pop() * 0.001
    $mult = 1.0
    while ($stack.Count -gt 0) { $num += $mult * $stack.Pop(); $mult *= 60 }
    $num
}

function ParseLine {
    $sb = ''
    while ($script:p -lt $text.Length -and $text[$script:p] -eq ' ') { $script:p++ }
    if ($script:p -ge $text.Length) { return '' }
    while ((Ch $script:p) -ne "`n" -and (Ch $script:p) -ne "`r") { $sb += $text[$script:p]; $script:p++ }
    SkipNewLineSymbolsOnce
    $sb
}

function ParseLines {
    SkipTillNewLine
    $lines = @()
    while ($true) { $l = ParseLine; if ([string]::IsNullOrEmpty($l)) { break }; $lines += $l }
    , $lines
}

$parts = @()
while ($script:p -lt $text.Length) {
    $id = ReadNextInt
    $start = ParseTimeValue
    $end = ParseTimeValue
    $lines = ParseLines
    $parts += [pscustomobject]@{ Id = $id; Start = $start; End = $end; Text = ($lines -join ' / ') }
}

$parts | Format-Table -AutoSize

# The cues must land inside the 6 s clip, in order, or a line shows over the wrong picture.
$prev = 0.0
foreach ($s in $parts) {
    if ($s.Start -lt $prev) { throw "cue $($s.Id) starts at $($s.Start)s, before the previous one ended" }
    if ($s.End -le $s.Start) { throw "cue $($s.Id) ends before it starts" }
    if ($s.End -gt 6.0) { throw "cue $($s.Id) ends at $($s.End)s, past the 6 s clip" }
    $prev = $s.End
}
Write-Host "PASS $($parts.Count) cue(s), the game's own parser walked the whole file without throwing"
