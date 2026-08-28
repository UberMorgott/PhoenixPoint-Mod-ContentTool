# Clone and add a weapon

ContentTool creates a weapon by cloning a shipped `WeaponDef`. The clone supplies firing behavior,
tags, compatible actor animations, ammo machinery, effects and any values you do not override. Your
manifest supplies a stable identity, optional model/icon, changed stats and starting quantity.

Choose the donor by weapon class. Phoenix Point selects hold poses and firing animation sets by
membership in an `EquipmentListDef` (`RiflesListDef`, `SnipersListDef`, `PistolsListDef`, ...) —
NOT by `ItemDef.Tags` and NOT by `HandsToUse`. ContentTool appends the clone next to its donor
automatically. Consequence: a sniper mesh on a PDW donor gives wrong hands on the grips AND a
wrong-size fit. Choose the donor to match the MESH CLASS, not just the stats.

## 1. Find a donor and supporting defs

Use the live repository:

```text
ct_list defs AssaultRifle WeaponDef
ct_list defs Fire DamageTypeBaseEffectDef
ct_list defs Burning DamageKeywordDef
```

The last command is `<nameFilter> <typeFilter>`: `DamageKeywordDef` is the type, while results such
as `Burning_DamageKeywordEffectorDef` and `Damage_DamageKeywordDataDef` are def names of that type.
In `keywords`, write one of those def names before `=value`; all three spellings are correct in their
own slots.

Copy complete def names, including bracketed owners when present. Start with a donor whose magazine,
projectile behavior, damage keywords and hold pose are already close to the intended weapon.

## 2. Add a stat-only clone

A weapon can keep the donor's model. This is the smallest content shape:

```text
MyWeapon\
  meta.json
  ppcontent.json
```

```json
{
  "id": "yourname.myweapon",
  "bundle": "MyWeapon.bundle",
  "weapons": [
    {
      "id": "YourName_FieldSidearm_WeaponDef",
      "clone": "SY_LaserPistol_WeaponDef",
      "guid": "replace-this-with-your-own-dashed-uuid",
      "name": "Field Sidearm",
      "blurb": "A tuned laser sidearm.",
      "damagetype": "Fire_StandardDamageTypeEffectDef",
      "keywords": "Burning_DamageKeywordEffectorDef=20",
      "count": 1,
      "clips": 3
    }
  ]
}
```

Do the first enable with `damage` and `spread` omitted. Every weapon prints the donor's real tuning
on the left of each arrow and the clone's value on the right. This captured line, for example, says
the donor dealt 40 damage with 2 degrees of spread before the demo overrode them:

```text
ct_weapon PASS 'Vulture Sniper' (Morgott_VultureSniper_WeaponDef) cloned from SY_LaserSniperRifle_WeaponDef; icon ok; prefab load started for key c7a9f1d24b6e4a3c8f5b7d1e9a2c4b60; tuning dmg 80->60 spread 1->1 range 41->41 (source intact)
```

Omitting `damage` or `spread` keeps that donor value and still prints both sides. `range` has no
manifest key; it is recomputed from `spread`, so changing spread changes it. When maximum range is
infinite, the readout is `41 / spread`. Read this line from the player's own installation before
choosing numbers; do not guess from a weapon name or another game version.

`id`, `clone`, and `guid` are required. No ContentTool command generates a GUID: invent it once and
keep it stable after release. It is an opaque def-identity string, not a parsed 128-bit value;
ContentTool requires it to be non-empty and at least two characters long. In practice, use any UUID
generator and follow the shipped convention of a dashed UUID with a hand-varied middle or tail, for
example `c7a9f1d2-4b6e-4a3c-8f5b-7d1e9a2c4b01`, then `...4b11` for another row. Replace the explicit
placeholder in the example above.

ContentTool uses the declared identity for the weapon, then derives the view and skin identities as
`a` plus its tail and `b` plus its tail. Every weapon, view and skin identity across the manifest
must be distinct. A GUID beginning with `a` or `b` would collide with its own derived identity, and
two entries that vary only their first character would still derive the same view and skin. Use a
different middle or tail digit for every row. A duplicate def identity also collides with the
repository.

Omit zero-valued overrides rather than relying on zero as a stat. `damage` and `spread` equal zero
mean “keep the donor.” `count` and `clips` are quantities added to starting storage; zero adds none.
That storage change affects new campaigns, not existing saves.

`keywords` is a semicolon-separated flat string because a nested object is not valid in a weapon
row. Each term is `DefName=value`.

## 3. Give the weapon a model

Put the GLB under `Content\Models`, publish it, and refer to the same key from the weapon row:

```text
MyRifle\
  meta.json
  ppcontent.json
  Content\
    Models\
      field_rifle.glb
  Icons\
    field_rifle.png
```

```json
{
  "id": "yourname.myrifle",
  "bundle": "MyRifle.bundle",
  "publish": [
    {
      "key": "yourname.myrifle/models/field_rifle",
      "asset": "models/field_rifle",
      "type": "GameObject",
      "deps": "defaultlocalgroup_unitybuiltinshaders.bundle"
    }
  ],
  "weapons": [
    {
      "id": "YourName_FieldRifle_WeaponDef",
      "clone": "PX_AssaultRifle_WeaponDef",
      "guid": "replace-this-with-your-own-dashed-uuid",
      "name": "Field Rifle",
      "blurb": "An assault rifle fitted to Phoenix specifications.",
      "icon": "Icons\\field_rifle.png",
      "model": "yourname.myrifle/models/field_rifle",
      "fit": "auto",
      "damage": 40,
      "spread": 2.5,
      "count": 1,
      "clips": 3
    }
  ]
}
```

`fit: auto` uniformly scales and offsets the model into the donor weapon's bounds — the largest
mesh by bounds diagonal, uniform scale = smallest of the 3 extent ratios. It cannot know which end
is the muzzle; add `flip: true` when the fitted direction is reversed.

Override the fit solver when it gives the wrong answer:

- `scale`: float — explicit uniform mesh scale, replaces the computed value.
- `rotate`: `"x,y,z"` euler degrees — explicit mesh rotation, replaces the axis-aligned auto
  rotation and `flip`.

Both are written to the prefab's mesh CHILD, not the root — see *Why the fit must live below the
prefab root* below.

The weapon needs attachment transforms for projectile origin/muzzle flash, aim/IK and shell
ejection. With `fit: auto`, ContentTool derives them. For a model pre-fitted in your art tool, provide
explicit local coordinates:

```json
{
  "id": "yourname.myrifle",
  "bundle": "MyRifle.bundle",
  "publish": [
    {
      "key": "yourname.myrifle/models/field_rifle",
      "asset": "models/field_rifle",
      "type": "GameObject",
      "deps": "defaultlocalgroup_unitybuiltinshaders.bundle"
    }
  ],
  "weapons": [
    {
      "id": "YourName_FieldRifle_WeaponDef",
      "clone": "PX_AssaultRifle_WeaponDef",
      "guid": "replace-this-with-your-own-dashed-uuid",
      "name": "Field Rifle",
      "model": "yourname.myrifle/models/field_rifle",
      "shoot": "0.00435,0.06109,0.76880",
      "aim": "0.00435,0.06109,0.41911",
      "shell": "0.02021,0.06109,0.41911",
      "count": 1,
      "clips": 3
    }
  ]
}
```

`0,0,0` is a legal socket. ContentTool tests whether `shoot` was declared, not whether its value is
nonzero. A model with neither `fit: auto` nor `shoot` is refused because a weapon without a projectile
origin fails during firing.

## 4. Choose a projectile, colour and muzzle flash

The clone inherits its donor's projectile and muzzle effects. Override any of these with manifest
keys:

### `projectile`

Name of a shipped `WeaponDef` (takes its `DamagePayload.ProjectileVisuals`) or a `ProjectileDef`
name directly. The projectile prefab is what travels from the muzzle to the target — there is no
hitscan in Phoenix Point. Every non-melee shot is a moving `Projectile` with
`DamagePayload.Speed`; a "beam" is just a projectile whose prefab has a long `TrailRenderer`.
`DamageDeliveryType` (Melee/DirectLine/Parabola/Sphere/Cone) is the only delivery selector.

```json
"projectile": "Crabman_Head_Spitter_WeaponDef"
```

### `flash`

Name of a shipped `WeaponDef` — takes its `VisualEffects` (`EquipmentVisualEffectsDef`: Flash,
Smoke, Shell). Useful when the donor's muzzle effect does not match the projectile you chose.

```json
"flash": "NJ_FlameThrower_WeaponDef"
```

Caveat: `NJ_FlameThrower_WeaponDef`'s `ProjectileVisuals` is an EMPTY prefab shared with
`Bash_WithWhateverYouCan` — copying it gives an INVISIBLE shot. Its fire lives in
`VisualEffects.Flash`. Good example of "check the donor's prefab, not its name."

### `tint`

`#RRGGBB` — clones the `ProjectileDef` and takes a PRIVATE copy of its prefab, then recolours
every `TrailRenderer` colorGradient key and every `ParticleSystem` startColor/colorOverLifetime.
Format is `#RRGGBB` only (no `#RGB`, no alpha).

```json
"tint": "#4CFF5A"
```

Why this key exists: **shot colour is NOT in any def.** All laser projectile prefabs are pure
white (trail gradient keys + PS start colour = `(1,1,1,1)`); the hue comes from a SHARED trail
material. You cannot get a colour by picking a differently-coloured donor — you must `tint`.
Caveat: tint is vertex-colour only, the material stays shared, so the landed hue = tint x
material. Clean on white laser prefabs; an already-coloured donor bolt needs a private material
instance (not implemented).

### `trail`

Float seconds — `TrailRenderer.time` on the private prefab copy = beam LENGTH. Implies the same
private clone as `tint`.

```json
"trail": "0.6"
```

### Shared vs private copies

Donor visual defs are SHARED references — assigning a donor's `ProjectileVisuals`/`VisualEffects`
directly is safe, but mutating them repaints every weapon in the game that uses them. `tint` and
`trail` therefore clone first. The engine logs `projectile=<name> (own copy)` vs `(shared)` in
`Player.log` — check this to confirm your weapon has a private copy.

## 5. Why the fit must live below the prefab root

`Addon.AttachVisuals` (`Assembly-CSharp`, `Addon.cs:1079-1080`) does
`VisualRoot.SetParent(attachTransform); VisualRoot.ResetTransform();` — that zeroes
localPosition/localRotation/localScale of the prefab ROOT on attach. ContentTool therefore writes
fit/scale/rotate to the prefab's existing mesh CHILD (commit `477a2dc`). Known ceiling: a foreign
prefab whose mesh sits ON the root has nowhere below the root to write, and keeps the erased-at-attach
behaviour.

## 6. Understand the four pieces

The builder creates:

1. a weapon def cloned from `clone`;
2. view/skin defs tied to stable derived GUIDs;
3. an optional prefab loaded from the published `model` key, with shoot, aim, aim-IK and shell
   transforms;
4. starting-storage entries according to `count` and `clips`.

Omitting `model` skips the skin replacement entirely, so the clone keeps the donor's art. Publishing
a model without naming its key in a weapon row does not attach it. When `icon` is present, the
builder loads it and assigns the cloned view's inventory, small and large icons itself.

## 7. Build from your DLL

Weapons require a DLL because the manifest cannot call the def builder by itself. Build your normal
Phoenix Point mod assembly from the complete [project, references and `ModMain`
skeleton](behavior-dll.md), declare it in `meta.json`, and keep its `WeaponBuild.Build` call in
`OnModEnabled`. Before adding any assembly reference, read the shared
[profile-wide `Managed\` module warning](behavior-dll.md#managed-module-load-failure). Substitute
your weapon project name for `MyMod` in that project file.

The API is `public static List<WeaponDef> Build(string modDir, Action<string> log)`. It registers all
declared defs and applies their starting-storage quantities, so normally ignore the return value.
Keep the returned list only when later behaviour needs those defs.

The builder handles the declared rows and starting storage. If your design uses research,
manufacturing, rewards or existing-save migration instead, leave starting quantities at zero and
wire the returned defs into that behavior yourself.

Use a complete code-mod `meta.json`:

```json
{
  "ID": "yourname.myrifle",
  "AssemblyName": "MyRifle.dll",
  "Version": "1.0.0",
  "Author": [
    { "Key": "English", "Value": "Your Name" }
  ],
  "Name": [
    { "Key": "English", "Value": "My Rifle" }
  ],
  "Description": [
    { "Key": "English", "Value": "Adds a weapon. Requires ContentTool." }
  ],
  "Dependencies": [
    "com.morgott.ContentTool"
  ]
}
```

## 8. Bake, test and package

For a model-backed weapon:

```text
ct_project MyRifle
ct_catalog apply MyRifle
ct_catalog verify
```

The two catalog commands are an [author preview, not a player or release
step](../SHIPPING-A-CONTENT-MOD.md#ordinary-loop-always-available).

Unticking removes a published model key, but it does not remove weapon defs created earlier in the
session. The weapon can therefore remain while its art is unavailable. Restart for a clean undo.

A stat-only weapon needs no `ct_project`: the bake does not read `weapons[]` and reports that there is
nothing to bake. Its complete authoring path is the two manifests, the DLL built directly into the
mod folder, and `ct_package`:

```text
ct_package MyWeapon
```

The packager accepts that manifest-only content payload and stages the DLL. Start a new campaign
when testing `count`/`clips`.

For a model-backed weapon, rebuild the DLL after code changes, rerun `ct_project` after model or
publish changes, then:

```text
ct_package MyRifle
```

The packager picks up the DLL named by `AssemblyName` but does not compile it. Install the staged
folder as a player and repeat the test after a cold start. During authoring, follow the shared
[build-to-mod-folder and restart loop](behavior-dll.md#name-the-real-dll).

Equip the weapon and verify inventory icon, hold pose, muzzle position, aim, firing, overwatch,
reload, holster, shell effect, damage type, ammunition behavior, projectile colour (`tint`) and
beam length (`trail`). Check `Player.log` for `(own copy)` vs `(shared)` on the projectile line
to confirm private copies when using `tint` or `trail`.

## Limits

- Cloning does not create research, manufacturing or localization beyond fields the builder writes.
- Existing saves do not receive starting-storage additions.
- A model does not bring actor hold/firing animations; the donor's `EquipmentListDef` membership selects them.
- Auto-fit is a bounding-box fit, not semantic weapon setup. Inspect all sockets in live firing.
  Override with `scale` (uniform) and `rotate` (`"x,y,z"` euler degrees) when auto gives the wrong answer.
- Damage keywords and damage type defs must already exist and be found by exact def name.
- Weapon GUID checks inside one manifest do not protect against another mod. If two mods use the
  same weapon `guid`, the first enabled mod wins; the second weapon is never created, so its stats,
  name, and icon never apply. The only sign is the success-looking line
  `ct_weapon PASS '<id>' already built this session`. On a cold first enable, that line means a
  cross-mod collision. Generate a genuinely random UUID and vary it somewhere other than the first
  character, because the view and skin identities replace that character with `a` and `b`.
