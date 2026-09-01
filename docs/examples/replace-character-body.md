# ReplaceCharacterBody

**Experimental.** This demo attempts to put the foreign humanoid body on Eileen, a named Synedrion
character from Festering Skies. She should keep her shipped identity, class and story references,
but the complete in-game result has not been verified.

**Corresponds to:** [Replace a shipped character body](../recipes/replace-character-body.md),
[Add a playable humanoid soldier](../recipes/humanoid-soldier.md), and
[Prepare a rigged, animated GLB](../recipes/animation-contract.md).

## Features and how they work

- **This is the `replaceBody` route.** It is not a `replace[]` bundle row. The runtime builder finds
  `S_SY_Eileen_CharacterTemplateDef` by exact def name and changes that existing character in place.
- **The model still comes from the mod's own bundle.** `body.glb` is baked to
  `assets/morgott.demo.replacecharacterbody/models/body` in `ReplaceCharacterBody.bundle`; no
  shipped Unity bundle is patched.
- **The body carries the full 300-clip humanoid set.** The same five representative role mappings
  and attack/death events as `HumanoidSoldier` feed setup, while the remaining clips cover playable
  soldier states.
- **Preview and loop choices match the playable-human project.** `play` selects
  `HL_IdleAlert_NoGun`; `*Loop*` and `*Idle*` mark matching baked clips as loops.
- **Eileen remains the target def.** There is no donor clone, new name, starting roster flag or stat
  block. The builder keeps her def and GUID, then rewires her body, rig and animation components.
- **Existing bodypart equipment is cleared.** Phoenix Point human bodies are assembled from bodypart
  items. The body-swap path removes that equipment so it does not render through the replacement;
  the character must be re-equipped from stores.
- **The route is DLC-dependent and refuses by name.** Without Festering Skies the Eileen def is
  absent, so the builder logs a refusal and changes nothing. It does not choose a substitute.

## Project on disk

```text
ReplaceCharacterBody\
  meta.json                       <- content-only; depends on ContentTool
  ppcontent.json                  <- replaceBody, play/loop and clip roles
  Content\Models\
    body.glb                      <- own rig plus 300 retargeted clips
  README.md
```

`Dist\ReplaceCharacterBody.bundle` is not committed. Bake it before testing.

## Rebuild and run it

Install and enable Festering Skies first.

```text
ct_list defs Eileen TacCharacterDef
ct_project ReplaceCharacterBody
ct_package ReplaceCharacterBody
```

Require `ALL PASS`, restart, then load or reach a campaign state containing Eileen. The target name
must appear in the discovery output before you treat the runtime test as valid.

## What a good run prints

The verified part is the bake:

```text
model 'body' kept <n> material(s) as <n> submesh(es)
creature-clips 'body': 300 animation(s) in the file -> <clip list>
creature-roles PASS "clips" maps 5 of 300 discovered animation(s); every required role (walk, idle, attack, death) is mapped
ct_project: ALL PASS - <project>\Dist\ReplaceCharacterBody.bundle
```

The unverified runtime path is written to emit:

```text
ct_creature PASS replacing the body of 'S_SY_Eileen_CharacterTemplateDef' (by def name)
ct_creature PASS 'S_SY_Eileen_CharacterTemplateDef' now wears this mod's body and is otherwise the character the game shipped: name '<name>', <n> tag(s), Strength <value> - the def, its GUID and every event that names it are the game's own
ct_creature PASS 'S_SY_Eileen_CharacterTemplateDef' is built: set='<set>' anims='<anims>' base='<base>' chassis='<chassis>'
```

If the DLC target is absent, the byte-exact first-class refusal begins:

```text
ct_creature FAIL ppcontent.json "creature": "replaceBody" is 'S_SY_Eileen_CharacterTemplateDef', which is not the name of a shipped TacCharacterDef with a component set. It names the ONE character whose body this model replaces - write a def name such as "S_SY_Eileen_CharacterTemplateDef". A DLC character is absent unless that DLC is installed. Nothing was changed.
```

## Verification status

**Not verified in-game.** The authority ledger explicitly excludes this demo. `TODO(verify)`: with
Festering Skies installed, measure Eileen in roster and tactical views, equipment removal and
re-equipping, animation coverage, story identity, then save and reload. Until that run exists, only
the source route and bake are established.
