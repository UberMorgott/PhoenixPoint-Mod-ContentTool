# Manifest and command reference

Use this page after [How a ContentTool mod is made](../SHIPPING-A-CONTENT-MOD.md). It records the
accepted file shapes and author-facing console commands. Route recipes explain when to use them.

## `meta.json`

Phoenix Point discovers a mod only when a top-level folder contains `meta.json`. A content-only mod
uses this complete shape:

```json
{
  "ID": "yourname.mymod",
  "AssemblyName": "",
  "Version": "1.0.0",
  "Author": [
    { "Key": "English", "Value": "Your Name" }
  ],
  "Name": [
    { "Key": "English", "Value": "My Mod" }
  ],
  "Description": [
    { "Key": "English", "Value": "A Phoenix Point content mod." }
  ],
  "Dependencies": [
    "com.morgott.ContentTool"
  ]
}
```

`ID` is required. `Dependencies` must contain `com.morgott.ContentTool`; that is what makes the mod
manager enable the engine before your mod. `AssemblyName` is optional. Leave it empty or omit it for
a content-only mod. If it names a DLL, that DLL must exist when you package.

Keep `ID` equal to `ppcontent.json`'s `id`. Phoenix Point and ContentTool use them for different
identity systems, and the current packager does not catch a mismatch.

## `ppcontent.json`: top level

This complete file shows every top-level section together. The values are illustrative; use the
route pages before copying it into a real project.

```json
{
  "id": "yourname.mymod",
  "bundle": "MyMod.bundle",
  "scale": 0.008,
  "play": "Spider_Idle",
  "loop": "Spider_Idle, Spider_Walk",
  "replace": [
    {
      "bundle": "aln_acidworm_assets_all.bundle",
      "asset": "acidworm_low_albedo",
      "texture": "acid_skin"
    }
  ],
  "publish": [
    {
      "key": "yourname.mymod/models/field_scanner",
      "asset": "models/field_scanner",
      "type": "GameObject",
      "deps": "defaultlocalgroup_unitybuiltinshaders.bundle"
    }
  ],
  "sounds": [
    {
      "media": 18839791,
      "file": "my_click.mp3"
    }
  ],
  "creature": {
    "clips": {
      "Spider_Walk": "walk",
      "Spider_Idle": "idle",
      "Spider_Idle_long": "",
      "Spider_Damage": "reaction",
      "Spider_Attack_1": "attack",
      "Spider_Attack_2": "ranged",
      "Spider_Death": "death"
    },
    "events": {
      "walk": "SwarmerStep_EventDef 0.15, SwarmerStep_EventDef 0.65",
      "attack": "ActionDo 0.4054, ShootShot 0.4865, ActionEnd 0.8378",
      "ranged": "ActionDo 0.2286, ShootShot 0.5429, ActionEnd 0.9143",
      "death": "Ragdoll 0.9"
    },
    "name": "Spider",
    "model": "cyborg_spider",
    "donor": "Swarmer_TacCharacterDef",
    "ranged": "Crabman_Head_Spitter_WeaponDef",
    "up": "0,1,0",
    "lift": 2.1372,
    "health": 40,
    "will": 10,
    "speed": 16,
    "volume": 1,
    "climbPitch": 90
  },
  "weapons": [
    {
      "id": "YourName_FieldRifle_WeaponDef",
      "clone": "PX_AssaultRifle_WeaponDef",
      "guid": "replace-this-with-your-own-dashed-uuid",
      "name": "Field Rifle",
      "model": "yourname.mymod/models/field_scanner",
      "fit": "auto",
      "offset": "0,-0.07,0",
      "projectile": "Crabman_Head_Spitter_WeaponDef",
      "tint": "#4CFF5A",
      "trail": "0.6",
      "damage": 40,
      "spread": 2.5,
      "count": 1,
      "clips": 3
    }
  ]
}
```

| Key | Type | Default and use |
|---|---|---|
| `id` | string | Required. Names the mod's bundle namespace, sound identities, video keys and private patched-copy cache. |
| `bundle` | string | Required declaration of the bundle filename this mod may own under `Dist`; it need not match the project folder name, and routes with nothing to bake need not produce the file. |
| `scale` | number | `1` when absent or zero; a negative value is refused. Uniform scale for an added rig/model and its root-motion conversion. |
| `play` | string | First bakeable clip when absent. The clip placed on the imported model's Animator. |
| `loop` | comma-separated string | No clips loop when absent. Names imported clips that must cycle. Creature `walk` and `idle` must loop. |
| `replace` | array | Shipped texture, material, mesh, clip-curve or video changes. |
| `publish` | array | New Addressables keys. Existing game-catalog keys are refused. |
| `sounds` | array | Replacements for shipped Wwise media. |
| `creature` | object | Opts into creature scaffolding and build data. |
| `weapons` | array | Weapon defs to clone and add. |

A mod may contain at most one `.bundle`, but a manifest-only payload is valid. There is no
one-route-per-mod rule. Every top-level section is parsed independently. Array rows must be flat
objects: do not put a nested object inside a `replace`, `publish`, `sounds` or `weapons` row. A
declared array with no complete row is refused. Literal `\uXXXX` escapes are not decoded, so write
ordinary characters.

## `replace[]`

Each row must contain exactly one route key: `texture`, `material`, `mesh`, `clip` or `video`.
`bundle` and `asset` are required only when the row is not a video. Every video row is exempt from
that pair: omit `bundle`; include `asset` to replace that catalog row, or omit `asset` to add a row.

| Route key | Value | Source or effect |
|---|---|---|
| `texture` | source stem | `Content\Textures\<stem>.png`, `.jpg` or `.jpeg` replaces a `Texture2D`. |
| `material` | `_Property=value` | Sets one float serialized on a shipped Material. |
| `mesh` | source stem | `Content\Meshes\<stem>.obj` or `.glb` replaces a Mesh; a rigged GLB can also replace skin data. |
| `clip` | `position*k` or `scale*k` | Multiplies one channel of a shipped `AnimationClip`. `rotation*k` is refused. |
| `video` | source stem | `Content\Videos\<stem>.webm`, `.mp4` or `.mov`; `asset` present means replace, absent means add. |

`asset` is the exact, case-sensitive serialized name found by `ct_list assets`, except for video.
A video value may be either the entire streaming path or only its filename; matching uses exact,
case-insensitive equality after normalizing slashes. ContentTool refuses zero matches or any value
that matches more than one row. Prefer the full path printed by `ct_video defs`, such as
`StreamableCopiedAssets/Videos/Factions/Phoenix/PP_Intro.webm`.

## `publish[]`

| Key | Required | Meaning |
|---|---|---|
| `key` | yes | New Addressables address the game or your DLL will request. Namespace it with the mod ID, for example `yourname.mymod/models/field_scanner`. |
| `asset` | yes | Lowercase path inside your bundle, such as `models/sniper`. |
| `type` | yes | Resource type such as `GameObject`. |
| `deps` | no | Semicolon-separated shipped bundles containing external references the asset needs. |

The key must be non-empty and new. A key already present in the game's catalog is refused by name,
and two enabled mods cannot publish the same key. ContentTool cannot override a shipped key through
this route.

## `sounds[]`

Each row requires `media`, the unsigned decimal ID of replaceable shipped media, and `file`, a file
under `Content\Audio\Replace`. Quoted and unquoted media IDs are accepted. The filename shortcut
`Content\Audio\Replace\18839791.mp3` produces the same replacement without a `sounds` row. Two
sources aimed at one media ID are refused.

## `creature`

| Key | Default | Meaning |
|---|---|---|
| `clips` | none | Imported clip name to role. Required roles: `walk`, `idle`, `attack`, `death`. Optional: `jump`, `reaction`, `ranged`, `climb`. |
| `events` | none | Role to ordered `EventName fraction` entries. Fractions are clamped to 0–1. |
| `name` | donor name | Roster display name. |
| `model` | the only GLB | Stem under `Content\Models`; set it when more than one model exists. |
| `donor` | `Swarmer_TacCharacterDef` | Shipped creature definition whose component and combat structure is cloned. Ignored when `replaceBody` is set. |
| `replaceBody` | empty | Name of a shipped `TacCharacterDef` whose BODY this model replaces, in place: that character keeps her def, name, class, story role and stats, and the new rig is written onto a clone of her own component set. Empty (the default) mints a new def from `donor` and touches nothing shipped. Replaces `donor`, never written with it. See [a shipped character's new body](replace-character-body.md). |
| `up` | `0,1,0` | Model's imported up axis. |
| `lift` | `0` | File-unit distance from origin down to the lowest vertex. |
| `health` | donor value | Starting strength; zero keeps the donor value. |
| `will`, `speed`, `volume` | `0` | Base will, Action Points and volume. |
| `pace` | shipped pace | Tiles per second. Explicit `0` preserves the source clip's timing. |
| `climbPitch` | `0` | Degrees nose-up while synthesized climbing clips play. The spider example uses `90`; `0` is honest for a biped. |
| `ranged` | none | Shipped `WeaponDef` cloned as a second attack. |
| `aiAction` | `MoveAndShoot_AIActionDef` | AI action added for the ranged attack. |
| `shootBone` | measured | Bone used for the synthesized shoot point. |
| `accuracy` | donor value | Accuracy for the ranged body part; zero keeps the donor value. |
| `startingRoster` | `false` | `true` adds the built unit to the player's starting aircraft in both campaign starts, with no DLL. Only the literal `true` opts in. The unit's class is the donor's. |
| `colliders` | on | The value `off` disables synthesized hit/hover colliders. |
| `aim` | measured/root | Bone used for the aim marker. |
| `hitRadius` | measured | Radius for per-bone sphere colliders. |
| `hitBones` | empty | Comma-separated bones that receive sphere colliders instead of one box. |

The first creature bake writes every discovered clip into `clips` with empty roles. Map the four
required roles by hand and rerun it. See [A new creature](creature.md) and the
[animation contract](animation-reference.md).

`ranged` is intentionally used in two slots: a value inside `creature.clips` is the ranged clip
role, while creature-level `ranged` names a shipped `WeaponDef` to clone for the attack.

## `weapons[]`

| Key | Required | Default and meaning |
|---|---|---|
| `id` | yes | New weapon def name. |
| `clone` | yes | Shipped `WeaponDef` to clone. Choose the same weapon class and hold pose. |
| `guid` | yes | Author-chosen stable def identity; no command generates it. See the [weapon GUID rules](weapon.md#2-add-a-stat-only-clone). |
| `name`, `blurb` | no | Display text. |
| `icon` | no | Relative PNG path such as `Icons\rifle.png`. |
| `model` | no | Published Addressables key. Absent keeps the donor's art. |
| `fit` | no | With a model and no explicit `shoot`, must be `auto`. |
| `shoot`, `aim`, `shell` | no | Socket coordinates as `x,y,z`. Zero is a legal coordinate. |
| `flip` | no | `true` reverses which fitted end is treated as the muzzle. |
| `scale` | no | Positive float; explicit uniform mesh scale, overrides the fit solver. Zero or negative does not override. |
| `rotate` | no | `"x,y,z"` euler degrees; explicit mesh rotation, overrides auto rotation + `flip`. |
| `offset` | no | `"x,y,z"` metres; local-position nudge added after the auto-fit solve. It preserves the solved rotation/scale/centre and moves derived sockets with the mesh. Without auto-fit, adds to the baked mesh child's position. |
| `projectile` | no | Name of a `WeaponDef` (takes its `DamagePayload.ProjectileVisuals`) or a `ProjectileDef` name directly. |
| `flash` | no | Name of a `WeaponDef`; takes its `VisualEffects` (`EquipmentVisualEffectsDef`: Flash/Smoke/Shell). |
| `tint` | no | `#RRGGBB`; clones the `ProjectileDef` + private prefab copy, recolours TrailRenderer + ParticleSystem colours. No `#RGB`, no alpha. |
| `trail` | no | Positive float seconds; `TrailRenderer.time` on the private prefab copy = beam length. Implies a private clone like `tint`; zero or negative does not override. |
| `damage`, `spread` | no | Zero keeps the cloned values. |
| `count`, `clips` | no | Starting-storage weapon and magazine quantities; zero adds none. |
| `damagetype` | no | `DamageTypeBaseEffectDef` name. |
| `keywords` | no | Semicolon-separated `DamageKeywordDef=value` entries. |

Weapon VFX parsing and application are in `WeaponBuild.cs:373-502`; fit overrides and `offset`
composition are in `WeaponBuild.cs:767-840` and `:906-920`.

## Source folders and identifiers

```text
Content\Textures\       .png .jpg .jpeg
Content\Meshes\         .obj .glb
Content\Models\         .glb
Content\Videos\         .webm .mp4 .mov
Content\Audio\          .wav .ogg .mp3
Content\Audio\Replace\  .wav .ogg .mp3
Icons\                  .png
```

The lowercased file stem is the identifier. Two same-stem files in one folder are refused.
`name.stream.wav` creates a streamed added sound named `name`; other added sounds are embedded.
There is no top-level `models` or `videos` array: added files are discovered from these folders.

`Icons\` is top-level, beside `Content\`, and is included by the packager. An `icon` value is a path
relative to the mod folder and must name a PNG that Unity can decode. In JSON,
`"Icons\\rifle.png"` represents the real path `Icons\rifle.png`; the doubled slash is JSON escaping,
not a second directory separator.

## Author-facing console commands

[Open the developer console](../SHIPPING-A-CONTENT-MOD.md#open-the-developer-console).

### Discover and inspect

```text
ct_version
ct_dump <ClassName> [depth=3]
ct_list bundles [nameFilter]
ct_list assets <bundleFile> [typeFilter] [nameFilter]
ct_list videos [nameFilter]
ct_list audio [nameFilter]
ct_list defs <nameFilter> [typeFilter]
ct_list bones <bundleFile> <meshName> [nameFilter]
ct_list props <bundleFile> <materialName>
ct_list clip <bundleFile> <clipName>
ct_extract tex <bundleFile> <assetName>
ct_extract mesh <bundleFile> <assetName>
ct_extract video <name>
ct_extract audio <wemName>
ct_voices watch [seconds]
ct_music probe [waitSeconds]
ct_sound status <mediaId>
ct_sound probe <mediaId>
ct_sound probe event <eventId>
ct_video defs
ct_creature list
```

The three exact-name inspectors—`bones`, `props`, and `clip`—refuse duplicate names rather than
choosing a path ID. Phoenix Point really ships duplicates, so narrow the target or choose a unique
asset instead of assuming the first match.

```text
ct_list REFUSED - 2 Meshs are named 'ALN_Fireworm' (pathIds ...) - refusing to guess which one to use
```

### Preview, bake and apply

```text
ct_project <project>
ct_sound bake <project>
ct_route7 apply <project>
ct_route7 status
ct_catalog apply <project>
ct_catalog verify
ct_catalog status
ct_video live <project>
ct_video status
ct_video resolve <runtimeKey>
ct_video open <runtimeKey>
ct_video play <defName>
ct_video quit
ct_dev on <project>
ct_dev off
ct_dev status
ct_dev sets
ct_dev set <name>
ct_dev next
ct_dev reload
ct_bench [open|close|reset]
ct_fit [show]
ct_fit <weapon> [<dx,dy,dz>|save|reload]
ct_fit <weapon> move|turn|pos|rot <x,y,z>
ct_fit <weapon> scale <value>
ct_replace <targetPath> <file-or-value>
ct_revert
ct_scan on|off|status
```

`ct_project` writes `Dist\<bundle>` when the project has bundle content and may rewrite a creature
manifest with its discovered clips. Video-only projects write no bundle. `ct_sound bake` writes
`Dist\Sounds\<mediaId>.bnk`; neither invokes the other. `ct_route7`,
`ct_catalog`, and `ct_video` are [author previews, not player or release
steps](../SHIPPING-A-CONTENT-MOD.md#ordinary-loop-always-available). A player only enables the mod.

`ct_bench` opens the [in-game weapon fit workbench](weapon.md#fit-the-model-in-the-workbench), closes
it, or resets its view. With no argument it toggles. A fully loaded geoscape campaign is required.

`ct_fit` controls the same live fit service from the console. A bare `x,y,z` or `move` adds a
position delta in metres; `turn` adds euler degrees; `pos`, `rot` and `scale` set absolute values.
`save` writes the three fit fields into the originating manifest, while `reload` discards live
changes and re-reads it. Every adjusting form updates matching live instances and prints the final
`scale`, `rotate` and `offset` values.

### Package

```text
ct_package <project>
```

This stages a publishable folder under
`%USERPROFILE%\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\ContentTool\Packaged\`. It neither
bakes nor compiles. A refusal deletes the incomplete staged folder. On success, zip the folder
itself so the archive contains `<project>\meta.json`.

### Engine diagnostics

Commands such as `ct_bake`, `ct_audio`, `ct_texswap`, `ct_meshswap`, `ct_liveswap`, `ct_fmt`,
`ct_seamprobe`, `ct_mission`, `ct_creature gate`, `ct_extract gate`, `ct_sound selftest`,
`ct_sound shapec`, `ct_scan gate`, `ct_music gate` and `ct_outtest` are ContentTool's regression and
instrumentation gates. They are not authoring steps. A guide may mention one only when diagnosing
the advanced live-preview target path.
