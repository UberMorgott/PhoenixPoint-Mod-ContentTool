# ContentTool Demo: Replace a Character's Body

**Eileen keeps being Eileen, and gets a different body.**

`S_SY_Eileen_CharacterTemplateDef` — the named Synedrion character the Festering Skies DLC ships —
keeps her def, her GUID, her displayed name, her class tags, her story role and every geoscape event
and reward that names her. What changes is what she LOOKS like: a foreign humanoid served out of
this mod's own bundle. **Zero C#**; the whole mod is `ppcontent.json`, and the whole swap is one key:

```json
"replaceBody": "S_SY_Eileen_CharacterTemplateDef"
```

It is the REPLACE half of a pair. The ADD half is
[HumanoidSoldier](../HumanoidSoldier/README.md) — the same model, the same toolchain, the same five
clip roles, put into the game as a NEW person instead of over an existing one. The full recipe, the
seam it writes to and the limits are in
[docs/guides/replace-character-body.md](../../docs/guides/replace-character-body.md).

## Run it

The model ships, so there is nothing to download and nothing to build offline:

```text
ct_project ReplaceCharacterBody
```

Restart, and Eileen wears it — in the roster, on the geoscape and in tactical. Unlike its sibling
this does NOT need a new campaign: nothing here touches the campaign start, and the def is read when
her actor is built.

**You need the Festering Skies DLC.** Without it that def does not exist, and the mod refuses BY NAME
and changes nothing (`src\Tactical\CreatureBuild.cs`, the `"replaceBody"` arm):

```text
ct_creature FAIL ppcontent.json "creature": "replaceBody" is 'S_SY_Eileen_CharacterTemplateDef', which is not the name of a shipped TacCharacterDef with a component set. It names the ONE character whose body this model replaces - write a def name such as "S_SY_Eileen_CharacterTemplateDef". A DLC character is absent unless that DLC is installed. Nothing was changed.
```

To point it at someone you DO have, put another shipped def name in that key. The named characters
in the def repository, read live out of a running game (`PPCLI\catalog\defs.ndjson`):
`S_Helena_TacCharacterDef`, `S_Felipe_TacCharacterDef`, `S_DrKalindar_TacCharacterDef`,
`S_RaviChaudri_TacCharacterDef`, `S_MrSpark_TacCharacterDef`, `S_IN_AbdonTusk_TacCharacterDef`.
Nothing else in the manifest changes.

## What ships, and what you may swap

| | |
|---|---|
| **Ships here** | `meta.json`, `ppcontent.json` (the whole mod), this README, and the model — `Content\Models\body.glb`, 36 254 816 B, all 300 retargeted clips, already through the toolchain and through `tools\ppzip.py` |
| **Optional** | your own `.glb` under the same stem, taken through the offline tools the [HumanoidSoldier README](../HumanoidSoldier/README.md#the-toolchain--the-four-commands) lists — including `ppzip.py`, the recommended final compression step |
| **Built on your machine** | `Dist\ReplaceCharacterBody.bundle`, by `ct_project ReplaceCharacterBody` |

The shipped model is byte-identical to the sibling demo's `soldier.glb`, and it carries the FULL clip
set on purpose. **Eileen is a real playable soldier**: the player will give her a pistol, take an
aimed shot, put her in overwatch, reload, crouch. Every one of those is a state with a clip, and a
clip the `.glb` does not contain cannot be substituted by anything — the ability waits on an
animation event that never fires and the camera stays locked on her forever.

An earlier build of this demo shipped a 9,4 MB model trimmed to the five clips the manifest maps.
That is exactly the bug above. **Do not trim a playable character; size is not a reason to.**

If size is the worry, COMPRESS instead of trimming: `tools\ppzip.py` is the recommended last step of
the toolchain and is what makes this file 36,254,816 B instead of 104,511,576 B (-65.3%, animation
89,281,672 B -> 22,764,800 B). It rewrites how the same curves are stored — constant rotations
collapsed to their two endpoint keys, rotations as normalized int16 at a worst measured error of
1.526e-05 per component — so all 300 clips and all 29,082 channels are still there, at no frame rate
loss. `ppslim.py` deletes clips; `ppzip.py` deletes nothing.

## The manifest, key by key

- `"replaceBody"` — the shipped `TacCharacterDef` this model takes over. **Absent = nothing shipped
  is touched**, which is the standing behaviour of every creature mod written before this key. It
  REPLACES `"donor"`: the named character is both the structural template and the def written back
  to, which is why the two are never written together.
- `"model": "body"` — which `Content\Models\*.glb` is the body, by file stem.
- `"clips"` — five Phoenix Point clip names mapped to roles. `walk, idle, attack, death, reaction`
  are filled; `jump, climb, ranged` are deliberately empty (`src\Tactical\CreatureRoles.cs`).
- `"events"` — the blocking animation events the game waits for, as fractions of the clip.
- No `"donor"`, no `"startingRoster"`, no `"health"/"will"/"speed"`: she keeps her own class,
  her own place in the story and her own base stats. Setting any of them would be overwriting the
  identity this demo exists to preserve.

## What the swap does NOT carry — read this before you file a bug

- **She arrives with no armour and no weapon.** On a human, PP's body IS its bodypart items: torso
  and legs are addons with their own geometry, attached by NAME to points a retargeted rig also
  provides. Kept, they hang a Phoenix Point soldier over this model; cleared, this model is the whole
  visible character. So `BodypartItems` and `EquipmentItems` are emptied and she is re-equipped from
  stores. This is stated, not solved — see the guide for the upgrade path.
- **Animations follow the rig, not the character.** She plays the clips in THIS file, and anything
  the game asks for that is not in it stalls — which is why the file carries all 300 and why nothing
  in this demo trims them.
- **Weapon sockets are the source rig's own**, settled offline before the bake; the in-game fit
  workbench cannot correct them for shipped weapons.
- **Her portrait and UI headshot do not change.** They are rendered from customisation data on her
  identity, not from this rig.
- **Another mod that rewrites the same def wins or loses by load order** — TFTV, for one, clones
  this very def (`refs\TFTV-src\TFTV\TFTVIncidents\GeoscapeEvents.cs:767`).

The guide covers each of these with the file:line behind it.

## Checked offline

`tests\ObjCodecTests\StartRoster.cs` reads this folder's `ppcontent.json` and asserts the key, the
target, the mapped roles, that the campaign start is NOT touched, and that the model the manifest
names is actually here.

```powershell
dotnet run --project tests\ObjCodecTests
```
