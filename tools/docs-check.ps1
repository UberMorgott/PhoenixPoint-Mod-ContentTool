<#
docs-check.ps1 - keep the public wiki (docs\) honest against src\.

  .\tools\docs-check.ps1            check only (exit 1 on a problem)
  .\tools\docs-check.ps1 -Write     also regenerate the command list in docs\reference\console-commands.md
  .\tools\docs-check.ps1 -Messages  also list inline-code spans whose literal text is not found in src\ (review aid, never fails)

Checks:
  - every ct_* command named in docs\ is registered in src\ContentToolMain.cs (log prefixes listed below are allowed)
  - a developer command ([DevOnly], or 'ct_mission gate') is named only on the console-commands reference page
  - the generated command list is current
#>
[CmdletBinding()]
param([switch]$Write, [switch]$Messages)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$main = Join-Path $root 'src\ContentToolMain.cs'
$docs = Join-Path $root 'docs'
$refPage = Join-Path $docs 'reference\console-commands.md'
# ct_* words the tool prints as a LOG PREFIX, not a command a modder types
$logPrefixes = @('ct_weapon', 'ct_perf', 'ct_autorun')

# --- read the registered commands ---
$lines = Get-Content $main
$cmds = [ordered]@{}
for ($i = 0; $i -lt $lines.Count; $i++) {
    if ($lines[$i] -match '\[ConsoleCommand\(Command = "(ct_\w+)", Description = "(.*)"\)\]') {
        $name = $Matches[1]; $desc = $Matches[2] -replace '\\"', '"' -replace '\\\\', '\'
        $dev = $lines[$i - 1] -match '\[DevOnly\]'
        $argText = if ($desc -match 'Args?: (.*?)\.?$') { $Matches[1] } else { '' }
        $cmds[$name] = [pscustomobject]@{ Name = $name; Dev = $dev; Args = $argText }
    }
}
if ($cmds.Count -eq 0) { throw "no [ConsoleCommand] found in $main" }
$public = @($cmds.Values | Where-Object { -not $_.Dev } | ForEach-Object Name)
$devs = @($cmds.Values | Where-Object Dev | ForEach-Object Name)

# --- generated block ---
$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine('<!-- BEGIN GENERATED: command list -->')
[void]$sb.AppendLine('<!-- written by tools\docs-check.ps1 -Write from src\ContentToolMain.cs; do not edit by hand -->')
[void]$sb.AppendLine('')
[void]$sb.AppendLine('| Command | Who | Arguments (as the command declares them) |')
[void]$sb.AppendLine('|---|---|---|')
foreach ($c in ($cmds.Values | Sort-Object Dev, Name)) {
    $who = if ($c.Dev) { 'developer (needs `ct-dev`)' } elseif ($c.Name -eq 'ct_mission') { 'everyone (`gate` needs `ct-dev`)' } else { 'everyone' }
    $a = if ($c.Args) { '`' + ($c.Args -replace '\|', '\|') + '`' } else { '-' }
    [void]$sb.AppendLine("| ``$($c.Name)`` | $who | $a |")
}
[void]$sb.AppendLine('')
[void]$sb.Append('<!-- END GENERATED -->')
$block = $sb.ToString() -replace "`r`n", "`n"

$problems = [System.Collections.Generic.List[string]]::new()
$pageText = if (Test-Path $refPage) { (Get-Content $refPage -Raw) -replace "`r`n", "`n" } else { '' }
$rx = [regex]'(?s)<!-- BEGIN GENERATED: command list -->.*?<!-- END GENERATED -->'
if (-not $rx.IsMatch($pageText)) { $problems.Add("$refPage has no GENERATED command-list block") }
elseif ($rx.Match($pageText).Value -ne $block) {
    if ($Write) { Set-Content $refPage ($rx.Replace($pageText, { param($m) $block })) -NoNewline -Encoding utf8NoBOM; Write-Host "regenerated command list in $refPage" }
    else { $problems.Add("generated command list in $refPage is stale - run tools\docs-check.ps1 -Write") }
}

# --- command names used in docs ---
foreach ($f in Get-ChildItem $docs -Recurse -Filter *.md) {
    $isRef = $f.FullName -eq $refPage
    $n = 0
    foreach ($l in Get-Content $f.FullName) {
        $n++
        foreach ($m in [regex]::Matches($l, '\bct_[a-z0-9]+\b')) {
            $w = $m.Value
            if ($logPrefixes -contains $w) { continue }
            if (-not $cmds.Contains($w)) { $problems.Add("$($f.Name):$n names '$w' - not a registered command"); continue }
            if (-not $isRef -and ($devs -contains $w)) { $problems.Add("$($f.Name):$n names developer command '$w' outside reference\console-commands.md") }
        }
        if (-not $isRef -and $l -match 'ct_mission gate') { $problems.Add("$($f.Name):$n names developer command 'ct_mission gate' outside reference\console-commands.md") }
    }
}

# --- optional: message spans vs src ---
if ($Messages) {
    $src = (Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.cs | ForEach-Object { Get-Content $_.FullName -Raw }) -join "`n"
    $src = $src -replace '"\s*\+\s*\$?@?"', '' -replace '\\\\', '\' -replace '\\"', '"'
    foreach ($f in Get-ChildItem $docs -Recurse -Filter *.md) {
        $spans = [System.Collections.Generic.List[string]]::new()
        $fence = $null
        foreach ($l in Get-Content $f.FullName) {
            if ($l -match '^\s*```(\w*)') { $fence = if ($null -eq $fence) { $Matches[1] } else { $null }; continue }
            if ($null -ne $fence) { if ($fence -in @('', 'text')) { $spans.Add($l.Trim()) }; continue }
            foreach ($m in [regex]::Matches($l, '`([^`]+)`')) { $spans.Add($m.Groups[1].Value) }
        }
        foreach ($sp in $spans) {
            if ($sp.Length -lt 24) { continue }
            $frags = $sp -split '<[^>]+>' | ForEach-Object { $_.Trim(" .'`"") } | Where-Object { $_.Length -ge 14 }
            $miss = @($frags | Where-Object { -not $src.Contains($_) })
            if ($miss.Count) { Write-Host "MSG? $($f.Name): $($miss[0])" }
        }
    }
}

if ($problems.Count) { $problems | ForEach-Object { Write-Host "DOCS FAIL $_" }; exit 1 }
Write-Host "docs-check: OK - $($public.Count) public + $($devs.Count) developer commands; docs name only registered ones"
