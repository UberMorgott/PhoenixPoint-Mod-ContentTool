# The offline check for the ONE FILE a creature modder edits: ppcontent.json's "creature" block.
#
#   pwsh -File tools\check-creature-manifest.ps1     (add -PPRoot "<path>" for a non-default install)
#
# Everything here INVOKES the shipped CreatureManifest out of ContentTool.dll - a copy of the logic
# would pass while the real thing was broken. Four arms, each with its falsification, because a
# check that cannot go red measures nothing:
#
#   PARSE     - the demo's real manifest reads back with its clips, roles, events and scalars intact.
#               Falsified by the nested-block trap: "clips" and "events" are objects INSIDE the
#               block, and a flat regex reader silently picks a key out of one of them.
#   REFUSE    - a required role left unmapped must be REFUSED BY NAME. Falsified by the complete
#               manifest, which must refuse nothing.
#   SCAFFOLD  - the bake writes the discovered clips back into the file. Falsified twice: it must be
#               IDEMPOTENT on a finished manifest (re-baking may not churn the author's file), and it
#               must actually DISCOVER into an empty block (and that result must then be refused).
#   EVENTS    - the blocking events parse in ORDER with their times. Falsified by an entry with no
#               time at all, which must throw rather than default to zero - an event stamped at the
#               wrong frame is damage on the wrong frame.
param([string]$PPRoot = "D:\Steam\steamapps\common\Phoenix Point")

$ErrorActionPreference = 'Stop'
$managed = Join-Path $PPRoot 'PhoenixPointWin64_Data\Managed'
$dll     = Join-Path $PSScriptRoot '..\bin\Release\ContentTool\ContentTool.dll'
if (-not (Test-Path $managed)) { throw "not found: $managed (pass -PPRoot)" }
if (-not (Test-Path $dll)) { throw "build first: dotnet build ContentTool.csproj -c Release" }

foreach ($d in 'UnityEngine.CoreModule','UnityEngine.AnimationModule','UnityEngine.AssetBundleModule') {
    [Reflection.Assembly]::LoadFrom("$managed\$d.dll") | Out-Null
}
[Reflection.Assembly]::LoadFrom((Join-Path $PPRoot 'ModSDK\Assembly-CSharp.dll')) | Out-Null
[Reflection.Assembly]::LoadFrom((Join-Path $PPRoot 'ModSDK\0Harmony.dll')) | Out-Null
$asm = [Reflection.Assembly]::LoadFrom((Resolve-Path $dll))
$T   = $asm.GetType('Morgott.ContentTool.Tactical.CreatureManifest')
if (-not $T) { throw "Morgott.ContentTool.Tactical.CreatureManifest is gone" }

$NP = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
$NI = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Instance
$mParse    = $T.GetMethod('Parse',    $NP)
$mScaffold = $T.GetMethod('Scaffold', $NP)
$mMissing  = $T.GetMethod('Missing',  $NI)
$mClipFor  = $T.GetMethod('ClipFor',  $NI)
$mEventsFor= $T.GetMethod('EventsFor',$NI)
foreach ($p in 'Parse','Scaffold','Missing','ClipFor','EventsFor') {
    if (-not (Get-Variable -Name ("m" + $p) -ErrorAction SilentlyContinue).Value) { throw "CreatureManifest.$p is gone" }
}
function Field($o, $n) { return $T.GetField($n, $NI).GetValue($o) }
function Parse($json)  { return $mParse.Invoke($null, @([string]$json)) }
function Missing($m, [string[]]$found) { return $mMissing.Invoke($m, @(,[string[]]$found)) }

$real  = Get-Content (Join-Path $PSScriptRoot '..\demos\CustomCreature\ppcontent.json') -Raw
# The clip list the demo's own model really carries, as ContentTool's reader reports it. Seven, and
# NOT the five the first model had: this is the discovery the bake feeds Scaffold().
$found = @('Spider_Walk','Spider_Idle','Spider_Idle_long','Spider_Damage','Spider_Attack_1',
           'Spider_Attack_2','Spider_Death')
$fail  = 0
function Arm($name, $ok, $detail) {
    $script:fail += $(if ($ok) { 0 } else { 1 })
    "{0,-12} {1}  {2}" -f $name, $(if ($ok) { 'PASS' } else { 'FAIL' }), $detail
}

# --------------------------------------------------------------------------------- PARSE
$m = Parse $real
$clips = Field $m 'Clips'
$roles = @($clips | ForEach-Object { $_.Value })
Arm 'PARSE' ($clips.Count -eq 7 -and ($roles -join ',') -eq 'walk,idle,,reaction,attack,ranged,death') `
    "$($clips.Count) clip(s) -> roles [$($roles -join ',')]"

# The nested-block trap. "clips" and "events" are objects inside the block; a reader that does not
# strip them would pick "attack" (a key of "events") out as the creature's own "name", and would
# never see "lift" past the first inner brace. Every scalar below sits AFTER both nested objects.
$lift = Field $m 'Lift'; $health = Field $m 'Health'; $speed = Field $m 'Speed'
$nm = Field $m 'Name'; $donor = Field $m 'Donor'; $up = Field $m 'Up'
Arm 'PARSE-NEST' ($nm -eq 'Spider' -and $donor -eq 'Swarmer_TacCharacterDef' -and $speed -eq 16 -and
                  [math]::Abs($lift - 2.1372) -lt 1e-3 -and [math]::Abs($health - 40) -lt 1e-3 -and
                  $up[0] -eq 0 -and $up[1] -eq 1 -and $up[2] -eq 0) `
    "name='$nm' donor='$donor' up=$($up -join ',') lift=$lift health=$health speed=$speed - read past two nested objects"

# --------------------------------------------------------------------------------- REFUSE
# Falsification of the refusal: the complete manifest must refuse NOTHING.
$none = Missing $m $found
Arm 'REFUSE-NOT' ($null -eq $none) `
    "a fully mapped manifest refuses: $(if ($null -eq $none) { '(nothing)' } else { $none.Substring(0, 60) })"

# ...and dropping ONE required role must name THAT role and no other. This is the arm that would go
# green on its own if Missing() ever returned null unconditionally.
# Matched against the HOLE LIST specifically, not against the whole sentence: the message also spells
# out the full role vocabulary to help the author, so a bare -match 'walk' would be true for every
# refusal and this arm would be measuring the help text instead of the verdict.
$broken = $real -replace '"Spider_Attack_1": "attack"', '"Spider_Attack_1": ""'
$why = Missing (Parse $broken) $found
$holes = if ($why -match 'unmapped: ([^.]+)\.') { $Matches[1] } else { $null }
Arm 'REFUSE' ($holes -eq 'attack') `
    "unmapping 'attack' -> $(if ($why) { "refused, holes named: '$holes'" } else { 'NOT REFUSED' })"

# Anti-vacuity for the hole list itself: two missing roles must name BOTH, or the arm above would
# pass on any implementation that always reported exactly one.
$two = $real -replace '"Spider_Attack_1": "attack"', '"Spider_Attack_1": ""' `
             -replace '"Spider_Walk": "walk"', '"Spider_Walk": ""'
$whyTwo = Missing (Parse $two) $found
$holesTwo = if ($whyTwo -match 'unmapped: ([^.]+)\.') { $Matches[1] } else { '' }
Arm 'REFUSE-BOTH' ($holesTwo -match 'walk' -and $holesTwo -match 'attack') `
    "unmapping 'walk' AND 'attack' -> holes named: '$holesTwo'"

# ...and an OPTIONAL role left unmapped must NOT refuse - or every model without a jump is rejected.
$nojump = $real -replace '"Spider_Damage": "reaction"', '"Spider_Damage": ""'
Arm 'REFUSE-OPT' ($null -eq (Missing (Parse $nojump) $found)) `
    "unmapping the OPTIONAL 'reaction' role refuses nothing"

# --------------------------------------------------------------------------------- SCAFFOLD
# Idempotent on a finished manifest: re-baking must not churn the file the author hand-edits.
$again = $mScaffold.Invoke($null, @([string]$real, [string[]]$found))
Arm 'SCAFFOLD-ID' ($again -eq $real) `
    "re-scaffolding a finished manifest changes $(if ($again -eq $real) { 'nothing' } else { 'THE FILE' })"

# ...on EITHER line ending, because the arm above only ever exercises whichever one the demo file
# happens to carry today. It carried LF, git handed it back as CRLF after a checkout, and Scaffold's
# hardcoded "\n" then rewrote every line of a finished manifest - idempotence lost, and a Windows
# author's whole file turned into a diff. Both directions are asserted so neither can rot again.
$lf   = $real -replace "`r`n", "`n"
$crlf = ($real -replace "`r`n", "`n") -replace "`n", "`r`n"
$lfOut   = $mScaffold.Invoke($null, @([string]$lf,   [string[]]$found))
$crlfOut = $mScaffold.Invoke($null, @([string]$crlf, [string[]]$found))
Arm 'SCAFFOLD-EOL' ($lfOut -eq $lf -and $crlfOut -eq $crlf -and -not $lfOut.Contains("`r")) `
    ("LF manifest -> $(if ($lfOut -eq $lf) { 'unchanged' } else { 'REWRITTEN' })" +
     ", CRLF manifest -> $(if ($crlfOut -eq $crlf) { 'unchanged' } else { 'REWRITTEN' })" +
     ", and the LF result stays CR-free: $(-not $lfOut.Contains("`r"))")

# ...and it really does DISCOVER: an empty block must come back holding every clip, roles blank -
# and that result must then be REFUSED, which is the modder's first bake.
$empty = @'
{
  "id": "x.y", "bundle": "b.bundle",
  "creature": {
  }
}
'@
$filled = $mScaffold.Invoke($null, @([string]$empty, [string[]]$found))
$fm = Parse $filled
$fc = Field $fm 'Clips'
$blank = @($fc | Where-Object { $_.Value -eq '' }).Count
$refused = Missing $fm $found
Arm 'SCAFFOLD' ($fc.Count -eq 7 -and $blank -eq 7 -and $refused) `
    "an empty block gained $($fc.Count) clip(s), $blank unmapped, and the bake is $(if ($refused) { 'REFUSED' } else { 'NOT REFUSED' })"

# ...and a project with NO "creature" block at all is left alone - a texture-only mod must not grow one.
$plain = $mScaffold.Invoke($null, @([string]'{ "id": "x.y", "bundle": "b.bundle" }', [string[]]$found))
Arm 'SCAFFOLD-OPT' ($null -eq $plain) `
    "a project declaring no creature block is $(if ($null -eq $plain) { 'untouched' } else { 'REWRITTEN' })"

# --------------------------------------------------------------------------------- EVENTS
# The ORDER is load-bearing: each wait is registered only after the previous one returned, so the
# events must come back in the sequence the ability waits for them, with the times the author wrote.
$ev = $mEventsFor.Invoke($m, @([string]'attack'))
$names = @($ev | ForEach-Object { $_.GetType().GetField('Name', $NI).GetValue($_) })
$times = @($ev | ForEach-Object { $_.GetType().GetField('At', $NI).GetValue($_) })
Arm 'EVENTS' (($names -join ',') -eq 'ActionDo,ShootShot,ActionEnd' -and
              [math]::Abs($times[1] - 0.4865) -lt 1e-4) `
    "attack -> [$($names -join ', ')] at [$(($times | ForEach-Object { $_.ToString('0.00') }) -join ', ')]"

# Falsification: an event with no time must THROW, not default to zero. A ShootShot silently stamped
# at frame 0 fires before the swing starts - damage on the wrong frame, which reads as a game bug.
$threw = $false
# Matched by PATTERN, not against the literal time the demo currently declares: this arm went red
# the day the model changed and the hit frame moved, which measured the fixture and not the parser.
try { Parse ($real -replace 'ShootShot [0-9.]+', 'ShootShot') | Out-Null } catch { $threw = $true }
Arm 'EVENTS-FAL' $threw "an event written with no time $(if ($threw) { 'is refused' } else { 'SILENTLY DEFAULTS' })"

""
"CREATURE-MANIFEST {0}  {1} arm(s) failed" -f $(if ($fail -eq 0) { 'ALL PASS' } else { 'FAIL' }), $fail
exit $(if ($fail -eq 0) { 0 } else { 1 })
