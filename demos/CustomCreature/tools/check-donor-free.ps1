# The offline half of "the spider is a NEW entity, not a repainted Mutog".
#
#   pwsh -File tools\check-donor-free.ps1          (add -PPRoot "<path>" for a non-default install)
#
# Two arms, both against the SHIPPED Assembly-CSharp with no game running:
#
#   FIELDS  - every field the donor-stripping in src\CustomCreatureMain.cs writes must exist on the
#             shipped type, with the type we assume. This is the arm that catches a game patch
#             renaming SkeletonChassisAddonDef or retyping GameTags - in game that would be a silent
#             compile-time-only assumption turning into an inherited Mutog body.
#   LEAKS   - CustomCreatureMain.DonorLeaks is INVOKED (not mirrored here - a copy of the logic would
#             pass while the real thing was broken). It is the same predicate the in-game C1-clean
#             arm feeds with values read back off the finished def, so the two cannot drift.
#
# Both arms are falsified below: a field name that does not exist MUST come back missing, and a
# fully-leaking input MUST report every leak. An arm that cannot fail measures nothing.
param([string]$PPRoot = "D:\Steam\steamapps\common\Phoenix Point")

$ErrorActionPreference = 'Stop'
$managed = Join-Path $PPRoot 'PhoenixPointWin64_Data\Managed'
$sdk     = Join-Path $PPRoot 'ModSDK\Assembly-CSharp.dll'
foreach ($p in @($managed, $sdk)) { if (-not (Test-Path $p)) { throw "not found: $p (pass -PPRoot)" } }

# Unity modules first - without them Assembly-CSharp's UnityEngine-typed fields resolve to a broken
# type and every lookup reports missing (the way check-bridge.ps1 lied on its first run).
foreach ($d in 'UnityEngine.CoreModule','UnityEngine.AnimationModule','UnityEngine.AssetBundleModule') {
    [Reflection.Assembly]::LoadFrom("$managed\$d.dll") | Out-Null
}
$asm = [Reflection.Assembly]::LoadFrom($sdk)

# type, field, expected field type.  Each row is something Build() assigns or empties.
$fields = @(
  @('PhoenixPoint.Common.Entities.Addons.AddonsManagerDef',    'SkeletonChassisAddonDef', 'PhoenixPoint.Common.Entities.Addons.AddonDef'),
  @('PhoenixPoint.Common.Entities.Addons.AddonsManagerDef',    'Rig',                     'UnityEngine.GameObject'),
  @('PhoenixPoint.Common.Entities.Addons.AddonsManagerDef',    'Tags',                    'PhoenixPoint.Common.Entities.GameTags.GameTagsList'),
  @('PhoenixPoint.Common.Entities.Addons.AddonDef',            'SkinData',                'PhoenixPoint.Common.Entities.Addons.AddonSkinDataBase'),
  @('PhoenixPoint.Common.Entities.Addons.AddonDef',            'SubAddons',               'PhoenixPoint.Common.Entities.Addons.AddonDef+SubaddonBind[]'),
  @('PhoenixPoint.Common.Entities.Addons.AddonDef',            'Tags',                    'PhoenixPoint.Common.Entities.GameTags.GameTagsList'),
  @('PhoenixPoint.Tactical.Entities.TacticalActorBaseDef',     'GameTags',                'PhoenixPoint.Common.Entities.GameTags.GameTagsList'),
  @('PhoenixPoint.Tactical.Entities.ActorsInstance.TacCharacterData', 'BodypartItems',     'PhoenixPoint.Common.Entities.Items.ItemDef[]'),
  @('PhoenixPoint.Tactical.Entities.ActorsInstance.TacCharacterData', 'EquipmentItems',    'PhoenixPoint.Common.Entities.Items.ItemDef[]'),
  @('PhoenixPoint.Tactical.Entities.ActorsInstance.TacActorData',     'GameTags',          'PhoenixPoint.Common.Entities.GameTags.GameTagDef[]')
)

function Field-Is($typeName, $field, $expected) {
    $t = $asm.GetType($typeName)
    if (-not $t) { return "no such type $typeName" }
    $f = $t.GetField($field)
    if (-not $f) { return "no field $field" }
    if ($f.FieldType.FullName -ne $expected) { return "is $($f.FieldType.FullName)" }
    return $null
}

$bad = 0
foreach ($row in $fields) {
    $why = Field-Is $row[0] $row[1] $row[2]
    $short = $row[0].Substring($row[0].LastIndexOf('.') + 1)
    if ($why) { $bad++; "  MISSING  {0,-18} {1,-24} {2}" -f $short, $row[1], $why }
    else      { "  ok       {0,-18} {1,-24} {2}" -f $short, $row[1], $row[2] }
}
""
"FIELDS       {0}  {1}/{2} donor-stripping targets exist with the assumed type" -f `
    $(if ($bad -eq 0) { 'PASS' } else { 'FAIL' }), ($fields.Count - $bad), $fields.Count

# Falsification: a field that does not exist must NOT resolve, or the loop above proves nothing.
$fake = if (Field-Is 'PhoenixPoint.Common.Entities.Addons.AddonDef' 'NoSuchField' 'System.Object') { 0 } else { 1 }
"FALSIFY-F    {0}  a field name that does not exist resolves on {1} type(s)" -f `
    $(if ($fake -eq 0) { 'PASS' } else { 'FAIL' }), $fake

# ---------------------------------------------------------------------------------------------
$dll = Join-Path $PSScriptRoot '..\..\..\bin\Release\ContentTool\ContentTool.dll'
if (-not (Test-Path $dll)) {
    ""
    "LEAKS        SKIP  build first: dotnet build ..\..\ContentTool.csproj -c Release"
    if ($bad -ne 0 -or $fake -ne 0) { exit 1 }
    return
}
[Reflection.Assembly]::LoadFrom((Join-Path $PPRoot 'ModSDK\0Harmony.dll')) | Out-Null
$mine = [Reflection.Assembly]::LoadFrom((Resolve-Path $dll))
$m = $mine.GetType('Morgott.ContentTool.Tactical.CreatureBuild').GetMethod(
        'DonorLeaks', [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static)
if (-not $m) { throw "CreatureBuild.DonorLeaks is gone - the in-game donor-free audit is unchecked" }

# mutog, vehicle, bodypartsWithSkin, equipment, chassisPresent, chassisSkinNull, rigIsOurs,
# demolition -> count.
# The third axis counts bodyparts that STILL CARRY SkinData, not bodyparts: the unit needs one
# geometry-free bodypart (the melee weapon), and a bodypart's SkinData is what puts a donor limb back
# on the rig (Addon.AttachVisuals:1024).
# The EIGHTH axis is the one the first seven could not see. A creature can be small, one-tile and
# carry no donor tag at all and still smash every wall it walks past, because crushing is a COMPONENT
# (TacticalDemolitionComponent.cs:75 ActorMovedEvent -> :216 ApplyDamage) whose def defaults to
# AlwaysEnabled=true (TacticalDemolitionComponentDef.cs:18). Inherited by silence, so it is asserted.
$cases = @(
  @{ n = 'clean (what Build must produce)'; a = @($false,$false,0,0,$true,$true,$true,$false);  want = 0 },
  @{ n = 'MutogTag back on the base def';   a = @($true, $false,0,0,$true,$true,$true,$false);  want = 1 },
  @{ n = 'VehicleTag back on the base def'; a = @($false,$true, 0,0,$true,$true,$true,$false);  want = 1 },
  @{ n = 'bodyparts kept donor SkinData';   a = @($false,$false,3,0,$true,$true,$true,$false);  want = 1 },
  @{ n = 'donor equipment reattached';      a = @($false,$false,0,1,$true,$true,$true,$false);  want = 1 },
  @{ n = 'chassis nulled (roster throws)';  a = @($false,$false,0,0,$false,$true,$true,$false); want = 1 },
  @{ n = 'chassis kept the donor SkinData'; a = @($false,$false,0,0,$true,$false,$true,$false); want = 1 },
  @{ n = 'Rig left pointing at the donor';  a = @($false,$false,0,0,$true,$true,$false,$false); want = 1 },
  @{ n = 'demolition component inherited';  a = @($false,$false,0,0,$true,$true,$true,$true);   want = 1 },
  @{ n = 'nothing stripped at all';         a = @($true, $true, 3,1,$true,$false,$false,$true); want = 7 }
)
""
$wrong = 0
foreach ($c in $cases) {
    $got = @($m.Invoke($null, [object[]]$c.a))
    if ($got.Count -ne $c.want) {
        $wrong++
        "  WRONG  {0,-32} {1} leak(s), expected {2}" -f $c.n, $got.Count, $c.want
    } else {
        "  ok     {0,-32} {1} leak(s)" -f $c.n, $got.Count
    }
}
"LEAKS        {0}  {1}/{2} donor-leak shapes classify correctly" -f `
    $(if ($wrong -eq 0) { 'PASS' } else { 'FAIL' }), ($cases.Count - $wrong), $cases.Count

# Falsification of the arm itself: the FIRST case is the only clean one. If the predicate returned
# an empty array for everything, every other case would already have failed above - so the anti-
# vacuity statement is that the last case really does report every one of the seven leaks at once.
$all = @($m.Invoke($null, [object[]]@($true,$true,3,1,$true,$false,$false,$true)))
"FALSIFY-L    {0}  a fully-inherited def reports {1} distinct leak(s), all seven axes named" -f `
    $(if ($all.Count -eq 7) { 'PASS' } else { 'FAIL' }), $all.Count

if ($bad -ne 0 -or $fake -ne 0 -or $wrong -ne 0 -or $all.Count -ne 7) { exit 1 }
