# The one offline check this demo needs: every LEFT-HAND column of the bridge in
# src\CustomCreatureMain.cs must be a REAL UnityEngine.AnimationClip field on one of the game's
# three actor anim-action defs. A typo there is invisible in game - the animation just never plays -
# so it is checked here, against the SHIPPED assembly, with no game running.
#
#   pwsh -File tools\check-bridge.ps1            (add -PPRoot "<path>" for a non-default install)
#
# Exits 1 if any slot does not resolve. The last arm is the falsification: a slot name that does not
# exist MUST come back missing, or the check is vacuous.
param([string]$PPRoot = "D:\Steam\steamapps\common\Phoenix Point")

$ErrorActionPreference = 'Stop'
$managed = Join-Path $PPRoot 'PhoenixPointWin64_Data\Managed'
$sdk     = Join-Path $PPRoot 'ModSDK\Assembly-CSharp.dll'
foreach ($p in @($managed, $sdk)) { if (-not (Test-Path $p)) { throw "not found: $p (pass -PPRoot)" } }

# Load the Unity modules FIRST. Without them Assembly-CSharp's AnimationClip fields resolve to a
# broken type and every slot reports missing - which is exactly how this script lied on its first run.
[Reflection.Assembly]::LoadFrom("$managed\UnityEngine.CoreModule.dll")      | Out-Null
$anim = [Reflection.Assembly]::LoadFrom("$managed\UnityEngine.AnimationModule.dll")
$clipT = $anim.GetType('UnityEngine.AnimationClip')
$asm  = [Reflection.Assembly]::LoadFrom($sdk)

$types = @('TacActorIdleAnimActionDef','TacActorNavAnimActionDef','TacActorShootAnimActionDef') |
         ForEach-Object { $asm.GetType("PhoenixPoint.Tactical.Entities.Animations.$_") }
if ($types -contains $null) { throw "an anim-action def type is missing from $sdk" }

# The slots the mod MUST be able to see. There is no hand-kept table in the mod any more - every
# slot mirrors the donor's own clip through ForVanillaClip, and a slot the donor left empty stays
# empty (that is what stopped the spider walking: filling TurnSequence.* claimed a turn-in-place the
# Mutog's controller has no state for, and navigation blocked five seconds on a clip that could
# never play). So what this arm guards is the REFLECTION WALK: if `Slots()` in the mod cannot reach
# a nested sequence, that slot is neither filled nor deliberately left empty - it is invisible.
$slots = @('Run.Start','Run.Loop','Run.Stop',
           'LowIdle','HighIdle','LowIdleAlert','HighIdleAlert',
           'FireStart','ShootPose','FireEnd',
           'Death',
           'JetJump.Start','JetJump.Loop','JetJump.Stop',
           'TurnSequence.Start','TurnSequence.LeftLoop','TurnSequence.RightLoop','TurnSequence.Stop',
           'Skids.Left','Skids.Right')

function Resolve-Slot($type, $path) {
    $cur = $type
    foreach ($part in $path.Split('.')) {
        $f = $cur.GetField($part)
        if (-not $f) { return $false }
        $cur = $f.FieldType
    }
    return $cur -eq $clipT
}

$bad = 0
foreach ($s in $slots) {
    $owners = @($types | Where-Object { Resolve-Slot $_ $s } | ForEach-Object { $_.Name })
    if ($owners.Count -eq 0) { $bad++; "MISSING  $s" }
    else { "ok       {0,-14} -> {1}" -f $s, ($owners -join ',') }
}
""
"BRIDGE-SLOTS {0}  {1}/{2} resolve to a real UnityEngine.AnimationClip field" -f `
    $(if ($bad -eq 0) { 'PASS' } else { 'FAIL' }), ($slots.Count - $bad), $slots.Count

# Falsification: if this resolves, the loop above proves nothing.
$fake = @($types | Where-Object { Resolve-Slot $_ 'NoSuchSlot' }).Count
"FALSIFY      {0}  a slot name that does not exist resolves on {1} type(s)" -f `
    $(if ($fake -eq 0) { 'PASS' } else { 'FAIL' }), $fake

# ---------------------------------------------------------------------------------------------
# The FULL slot inventory, enumerated the way the mod enumerates it. Nothing here predicts WHICH
# clip a slot receives - that is decided per slot from the donor's own clip at load time and cannot
# be known offline. What is asserted is that the walk reaches the nested sequences at all.
function Slots-Of($type) {
    $out = @()
    foreach ($f in $type.GetFields()) {
        if ($f.FieldType -eq $clipT) { $out += $f.Name; continue }
        if (-not $f.FieldType.IsClass -or $f.FieldType.IsArray) { continue }
        if ([UnityEngine.Object].IsAssignableFrom($f.FieldType)) { continue }   # def references
        foreach ($g in $f.FieldType.GetFields()) {
            if ($g.FieldType -eq $clipT) { $out += "$($f.Name).$($g.Name)" }
        }
    }
    return $out
}
""
$found = @()
foreach ($t in $types) {
    $all = Slots-Of $t
    "{0}  {1} clip slot(s)" -f $t.Name, $all.Count
    $found += $all
}
""
# Anti-vacuity: the nested sequences are the ones the walk can silently miss, and they are exactly
# the ones the movement bug lived in. A flat-fields-only walk leaves these out and goes red.
$nested = @($slots | Where-Object { $_ -match '\.' })
$missed = @($nested | Where-Object { $found -notcontains $_ })
"NESTED-SLOTS {0}  {1}/{2} nested sequence slot(s) are reachable by the same reflection walk the mod uses{3}" -f `
    $(if ($missed.Count -eq 0) { 'PASS' } else { 'FAIL' }), ($nested.Count - $missed.Count), $nested.Count, `
    $(if ($missed.Count -eq 0) { '' } else { " - MISSING: $($missed -join ', ')" })

# ---------------------------------------------------------------------------------------------
# The OTHER bridge: vanilla CONTROLLER clip name -> our clip. This is the one that makes turn,
# idle and death work, because one-shot states are reached by trigger and never read a clip field.
# Checked by INVOKING THE SHIPPED METHOD, not by mirroring it here - a copy of the logic would
# pass while the real thing was broken.
$dll = Join-Path $PSScriptRoot '..\..\..\bin\Release\ContentTool\ContentTool.dll'
if (-not (Test-Path $dll)) {
    ""
    "CLIP-NAMES   SKIP  build first: dotnet build ..\..\ContentTool.csproj -c Release"
} else {
    foreach ($d in 'UnityEngine.AssetBundleModule') {
        [Reflection.Assembly]::LoadFrom("$managed\$d.dll") | Out-Null
    }
    [Reflection.Assembly]::LoadFrom((Join-Path $PPRoot 'ModSDK\0Harmony.dll')) | Out-Null
    $mine = [Reflection.Assembly]::LoadFrom((Resolve-Path $dll))
    $m = $mine.GetType('Morgott.ContentTool.Tactical.CreatureBuild').GetMethod(
            'RoleForVanilla', [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static)

    # name -> expected clip. The first row is the one that matters: "Soldier_Idle" CONTAINS "die"
    # (sol-die-r), so a naive substring match calls it a death animation. Token matching must not.
    $cases = [ordered]@{
        'Soldier_Idle'     = 'idle'
        'Mutog_Die'        = 'death'
        'MutogDeath'       = 'death'
        'Crab_TurnLeft'    = 'walk'
        'Turn90L'          = 'walk'
        'Mutog_RunFwdLoop' = 'walk'
        'FireStart'        = 'attack'
        'Reload'           = 'attack'
        # The three states the SOLDIER controller 'HumanoidAnimatorLOC' actually fires from. None of
        # them carries the token "shoot", only "shot", so they used to fall through to the idle - and
        # a bash that plays an event-less idle costs 10s per blocking event (measured in game:
        # damage landed 23.24 s after the swing started). The most common donor controller in the
        # game, so these three rows are the ones this arm exists for.
        'FF_FirstShot_AR'  = 'attack'
        'FF_ShotLoop_AR'   = 'attack'
        'FF_EndShot_AR'    = 'attack'
        'HL_ActionPlaceholder' = 'attack'
        # ...and the near-miss that must NOT be swept up with them: one CamelCase token, "reaction",
        # which is its OWN role now that a model finally shipped a flinch clip. It must never come
        # back 'attack' - that is the collision this row guards, and it is why the reaction keywords
        # are tested before the attack ones.
        'HL_ReactionPlaceholder' = 'reaction'
        'E_Hurt_Reaction'        = 'reaction'
        'JetJumpLoop'      = 'jump'
        'SomethingWeird'   = 'idle'      # unknown must fall back, never return nothing
    }
    ""
    $wrong = 0
    foreach ($k in $cases.Keys) {
        $got = $m.Invoke($null, @($k))
        if ($got -ne $cases[$k]) { $wrong++; "  WRONG  {0,-18} -> {1} (expected {2})" -f $k, $got, $cases[$k] }
        else { "  ok     {0,-18} -> {1}" -f $k, $got }
    }
    "CLIP-NAMES   {0}  {1}/{2} vanilla controller-clip names classify correctly" -f `
        $(if ($wrong -eq 0) { 'PASS' } else { 'FAIL' }), ($cases.Count - $wrong), $cases.Count
    if ($wrong -ne 0) { exit 1 }

    # -----------------------------------------------------------------------------------------
    # THE SLOT-FIRST RULE, which is what stopped the spider walking for a whole session.
    # A slot's dotted path is a DECLARATION: TacActorNavAnimActionDef.Run IS the run sequence,
    # whatever the donor happened to name the clip parked in it. The Swarmer's run clips carry no
    # locomotion word, so classifying by the donor's CLIP NAME wired Run.Start/Loop/Stop to the
    # idle - the creature's navigation played an animation that travels nowhere, reported PASS the
    # whole time, and only C1-walk in game could see it (0.00 tiles in 30 s).
    #
    # RoleOrNull is the half that must answer NULL for a slot that says nothing on its own, so the
    # donor's clip name still decides those. Those null rows ARE the falsification: if this method
    # ever starts confidently returning a role for 'Start' or 'Clip', the slot-first rule would
    # override the donor everywhere and the bug comes back inverted.
    $mo = $mine.GetType('Morgott.ContentTool.Tactical.CreatureBuild').GetMethod(
            'RoleOrNull', [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static)
    if (-not $mo) {
        "SLOT-ROLES   FAIL  CreatureBuild.RoleOrNull is gone - the slot-first rule cannot be checked"
        exit 1
    }
    $slotCases = [ordered]@{
        'Run.Start'        = 'walk'      # the three that were wired to the idle
        'Run.Loop'         = 'walk'
        'Run.Stop'         = 'walk'
        'JetJump.Loop'     = 'jump'
        'Death'            = 'death'
        'ShootPose'        = 'attack'
        'FireStart'        = 'attack'
        # MEASURED, not assumed - this row is here because writing it down wrong is how the rule gets
        # over-trusted. "Skids" is not the keyword "skid": Tokens() splits CamelCase but does not
        # stem, so the plural misses and the slot stays SILENT. That is the safe direction (the
        # donor's own clip name then decides) and it is deliberately NOT fixed by widening the
        # keyword list - every word added there fires on every donor in the game, and the slot-first
        # rule only needs to be right where it speaks.
        'Skids.Left'       = $null
        # ...and the ones that must stay SILENT, or the slot would outrank a donor clip name that
        # actually knows better. 'LowIdle' is deliberately here: "idle" is the catch-all, never a
        # keyword, so even it must come back null rather than claim the role by accident.
        'Start'            = $null
        'Clip'             = $null
        'LowIdle'          = $null
    }
    ""
    $sw = 0
    foreach ($k in $slotCases.Keys) {
        $got = $mo.Invoke($null, @($k))
        $exp = $slotCases[$k]
        if ($got -ne $exp) { $sw++; "  WRONG  {0,-18} -> {1} (expected {2})" -f $k, $(if ($null -eq $got) { '(null)' } else { $got }), $(if ($null -eq $exp) { '(null)' } else { $exp }) }
        else { "  ok     {0,-18} -> {1}" -f $k, $(if ($null -eq $got) { '(null, donor clip name decides)' } else { $got }) }
    }
    "SLOT-ROLES   {0}  {1}/{2} slot paths classify correctly - Run.* MUST be walk, or navigation is wired to an animation that does not travel" -f `
        $(if ($sw -eq 0) { 'PASS' } else { 'FAIL' }), ($slotCases.Count - $sw), $slotCases.Count
    if ($sw -ne 0) { exit 1 }
}

if ($bad -ne 0 -or $fake -ne 0 -or $missed.Count -ne 0) { exit 1 }
exit 0
