# Builds the LIVE-SEAM fixture the R5/R6 gates cannot build for themselves: a .glb whose joint NAMES
# still biject with the target rig, but whose INVERSE BIND MATRICES no longer match the bind poses of
# the mesh it replaces. That is the one shape every model shipped in this repository lacks - every
# rigged .glb here IS the source of the mesh it would replace, so its bind matrices are identical to
# the shipped ones and the live rebind cannot be told right from wrong with them.
#
# The perturbation is the one a real author hits: Blender's Object > Apply > All Transforms moves a
# chain's rest pose without touching the mesh, so the file's bind matrices drift from the game's.
# Only the translation column of the named chain's matrices is moved; names, weights, geometry and
# every other byte are the base file's.
#
# Output is NOT committed: the base is a 36 MB CC-BY model (see the demo's own README) and a second
# copy of it does not belong in the repository. Re-run this to get the fixture back.
param(
    [string] $Base   = (Join-Path $PSScriptRoot '..\demos\ReplaceCharacterBody\Content\Models\body.glb'),
    [string] $Out    = (Join-Path $PSScriptRoot '..\local\body_handshift.glb'),
    # The reported symptom is a HAND: every joint of the right-hand chain, and nothing else.
    [string] $Chain  = '^R\.(Hand|Index|Middle|Pinky|Ring|Thumb)',
    [double] $Offset = 0.25
)
$ErrorActionPreference = 'Stop'

$bytes = [IO.File]::ReadAllBytes($Base)
$jsonLen = [BitConverter]::ToUInt32($bytes, 12)
$doc = [Text.Encoding]::UTF8.GetString($bytes, 20, $jsonLen) | ConvertFrom-Json
$binAt = 20 + $jsonLen + 8

$skin = $doc.skins[0]
$acc = $doc.accessors[$skin.inverseBindMatrices]
if ($acc.type -ne 'MAT4' -or $acc.componentType -ne 5126) { throw "bind matrices are $($acc.type)/$($acc.componentType), not float MAT4" }
$view = $doc.bufferViews[$acc.bufferView]
if ($view.byteStride) { throw "the bind-matrix view is interleaved (byteStride $($view.byteStride)); this patcher assumes tight packing" }
$at = $binAt + [int]$view.byteOffset + [int]$acc.byteOffset

$moved = @()
for ($slot = 0; $slot -lt $skin.joints.Count; $slot++) {
    $name = $doc.nodes[$skin.joints[$slot]].name
    if ($name -notmatch $Chain) { continue }
    # glTF states a matrix column-major, so the translation is floats 12, 13 and 14.
    foreach ($k in 12, 13, 14) {
        $o = $at + ($slot * 64) + ($k * 4)
        [BitConverter]::GetBytes([single]([BitConverter]::ToSingle($bytes, $o) + $Offset)).CopyTo($bytes, $o)
    }
    $moved += $name
}
if ($moved.Count -eq 0) { throw "no joint matched '$Chain' - nothing would differ from the base file" }

New-Item -ItemType Directory -Force -Path (Split-Path $Out -Parent) | Out-Null
[IO.File]::WriteAllBytes($Out, $bytes)
Write-Host "wrote $Out"
Write-Host "moved $($moved.Count) joint(s) by +$Offset on each axis: $($moved -join ', ')"
