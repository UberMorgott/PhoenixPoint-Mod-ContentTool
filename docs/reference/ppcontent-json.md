# `ppcontent.json` reference

Put `ppcontent.json` at the root of your project folder. The root must be a JSON object. Keep key spelling as shown. `replace`, `publish`, and `sounds` are parsed as JSON arrays; an incomplete row is reported instead of silently accepted. The `creature` block and flat `weapons` rows have their own text readers, so keep their documented shapes.

“Required” below means required to use that feature. You can omit an entire optional section.

## Root keys

| Key | Type | Required | Meaning | Example |
|---|---|---|---|---|
| `id` | string | Yes | This mod's identity and a folder component for patched copies. Use one plain name. | `"author.mymod"` |
| `bundle` | string | Yes | File name for this mod's own output in `Dist`. Use one plain name. | `"MyMod.bundle"` |
| `scale` | number | No | Uniform rig-root scale. Absent or zero uses 1; a negative value is skipped and reported. | `0.005` |
| `loop` | string | No | Model clip names to loop, separated by commas or semicolons. Matching ignores case and accepts `*`. | `"Spider_Idle, Spider_Walk"` |
| `play` | string | No | One model clip for the Animator to play; empty uses the first bakeable clip. | `"Spider_Walk"` |
| `replace` | array of objects | No | Changes shipped textures, materials, meshes, clips, or videos. | `[{"bundle":"<shipped-bundle>","asset":"<asset-name>","texture":"<stem>"}]` |
| `publish` | array of objects | No | Serves catalog keys from this mod's own bundle. | `[{"key":"<key>","asset":"textures/<stem>","type":"Texture2D"}]` |
| `sounds` | array of objects | No | Declares replacements of shipped sound media. | `[{"media":<media-id>,"file":"<source-file>.ogg"}]` |
| `creature` | object | No | Configures a custom-model creature or a shipped character's new body. | `{"model":"<model-stem>"}` |
| `weapons` | array of flat objects | No | Adds weapon definitions cloned from shipped weapons. | `[{"id":"<weapon-def>","clone":"<shipped-weapon-def>","guid":"<fixed-guid>"}]` |

`id` and `bundle` must each be a single safe file or folder name. See [project files](project-files.md#names-and-collisions). Use the same identity in `meta.json`'s `ID`.

## `replace` rows

Each row selects **exactly one** of `texture`, `material`, `mesh`, `clip`, or `video`. A texture, material, mesh, or clip row also needs `bundle` and `asset`. A video row does not use `bundle`; an omitted `asset` adds a video instead of replacing a shipped one.

| Key | Type | Required | Meaning | Example |
|---|---|---|---|---|
| `bundle` | string | For texture, material, mesh, clip | Shipped bundle file to patch. | `"<shipped-bundle>.bundle"` |
| `asset` | string | For texture, material, mesh, clip; optional for video | Exact shipped asset name. For video, omit it to add a clip. | `"<asset-name>"` |
| `texture` | string | If this is a texture row | Stem of a file directly in `Content\Textures`. | `"<stem>"` |
| `material` | string | If this is a material row | One material property and numeric value, written as `property=value`. | `"_GlossMapScale=0.15"` |
| `mesh` | string | If this is a mesh row | Stem of an `.obj` or `.glb` in `Content\Meshes`. | `"<stem>"` |
| `clip` | string | If this is a clip row | Multiplies a position or scale curve: `position*<number>` or `scale*<number>`. Rotation and a factor of 1 are refused. | `"position*3"` |
| `video` | string | If this is a video row | Stem of a clip in `Content\Videos`. | `"<stem>"` |

A target asset name must identify one shipped asset of the requested class. Use `ct_list` to obtain its spelling. Source stems are matched without regard to case; shipped asset names are exact.

## `publish` rows

| Key | Type | Required | Meaning | Example |
|---|---|---|---|---|
| `key` | string | Yes | Address the game will request. | `"morgott.sample/probe_tex"` |
| `asset` | string | Yes | Path to the asset inside this mod's own bundle. | `"textures/swatch"` |
| `type` | string | Yes | Resource type of the asset: a UnityEngine class name (`Texture2D`, `GameObject`, `Mesh`, `Material`, `AnimationClip`) or an assembly-qualified type name. A missing or unknown type is refused. | `"Texture2D"` |
| `deps` | string | No | Shipped bundle file names this asset needs mounted, separated by `;`. | `"<bundle-a>;<bundle-b>"` |

Publishing adds new keys only. A key the game's own catalog already has is refused; replace shipped content with a `replace` row instead. Bake the mod's bundle with `ct_project <project>` before applying its published keys.

## `sounds` rows

| Key | Type | Required | Meaning | Example |
|---|---|---|---|---|
| `media` | nonnegative whole number, or a string containing one | Yes | Shipped Wwise media ID to replace; must fit an unsigned 32-bit integer. | `18839791` |
| `file` | string | Yes | File name in `Content\Audio\Replace`. | `"<source-file>.ogg"` |

A file named `<mediaId>.wav`, `<mediaId>.ogg`, or `<mediaId>.mp3` in `Content\Audio\Replace` can also declare its target by filename, without a `sounds` row. Run `ct_sound bake <project>` to create the replacement banks.

## `creature` object

This block is optional. Its clip and event maps use **names as keys**, so their entries vary with the model. The other keys are fixed.

| Key | Type | Required | Meaning | Example |
|---|---|---|---|---|
| `clips` | object mapping clip name to role | For a creature using its own clips | Assigns model clips to roles. `walk`, `idle`, `attack`, and `death` are required roles; other supported roles include `jump`, `reaction`, `ranged`, and `climb`. | `{"Spider_Walk":"walk"}` |
| `events` | object mapping role to event string | When that animation needs blocking events | Gives an event name and a fraction of the clip for each event, in playback order. | `{"attack":"ActionDo 0.25, ShootShot 0.55, ActionEnd 0.90"}` |
| `name` | string | No | Roster display name; empty keeps the donor's. | `"Spider"` |
| `model` | string | When more than one model is present | Stem of the `.glb` in `Content\Models`; a single model is selected automatically. | `"<model-stem>"` |
| `donor` | string | No | Shipped character definition to clone. Defaults to `Swarmer_TacCharacterDef`. | `"Swarmer_TacCharacterDef"` |
| `startingRoster` | boolean | No | Literal `true` adds the creature to a new campaign's starting roster. | `true` |
| `useGameAnimations` | boolean | No | Literal `true` uses the game's clips on a compatible rig instead of shipping model clips. | `true` |
| `replaceBody` | string | No | Shipped character definition whose body is replaced in place. This also uses that character as the structural template. | `"<shipped-character-def>"` |
| `up` | string of three comma-separated numbers | No | Up axis of the imported model. | `"0,1,0"` |
| `lift` | number | No | Distance from the model origin down to its lowest vertex, in file units. | `32.8288` |
| `health` | number | No | Enter-play health; zero leaves the donor's value. | `40` |
| `will` | number | No | Will stat override. | `10` |
| `speed` | number | No | Movement action points, distinct from animation pace. | `16` |
| `volume` | number | No | Creature volume setting. | `1` |
| `pace` | number | No | Traversal speed in tiles per second. Absent uses shipped pace; zero keeps the model clip's own pace. | `5.4284` |
| `climbPitch` | number | No | Nose-up pitch in degrees during a synthesized climb. | `90` |
| `ranged` | string | No | Shipped weapon definition to clone for a second, ranged attack. Empty keeps the creature melee-only. | `"Crabman_Head_Spitter_WeaponDef"` |
| `aiAction` | string | No | AI action for the ranged attack. Defaults to `MoveAndShoot_AIActionDef`. | `"MoveAndShoot_AIActionDef"` |
| `shootBone` | string | No | Bone for the synthesized muzzle; empty lets the tool measure one. | `"<bone-name>"` |
| `accuracy` | number | No | Ranged accuracy percentage; zero leaves the donor's value. | `40` |
| `colliders` | string | No | `"off"` disables the generated colliders. | `"off"` |
| `aim` | string | No | Name of the rig bone that marks the creature's aim point. | `"<bone-name>"` |
| `hitRadius` | number | No | Radius for generated hit colliders. | `0.5` |
| `hitBones` | string | No | Comma-separated bone names for hit colliders. | `"<bone-a>,<bone-b>"` |

For an authored creature, let the bake discover the `.glb` clips, then fill the roles it writes into `clips`. See [the creature recipe](../recipes/creature.md).

## `weapons` rows

Each entry must be **flat**: do not put a nested object in a weapon row. `id`, `clone`, and `guid` are mandatory. `model` is optional; without it, the clone keeps the shipped weapon's art. With a model, use `"fit": "auto"` or declare the `shoot` socket.

| Key | Type | Required | Meaning | Example |
|---|---|---|---|---|
| `id` | string | Yes | New weapon definition name. | `"<weapon-def>"` |
| `clone` | string | Yes | Shipped weapon definition to copy, including its class and handling tags. | `"<shipped-weapon-def>"` |
| `guid` | string | Yes | Fixed identity for the new weapon and derived definitions. Keep it stable across releases. | `"<fixed-guid>"` |
| `name` | string | No | Display name in the game. | `"<display-name>"` |
| `blurb` | string | No | Inventory description. | `"<description>"` |
| `icon` | string | No | Path to a PNG icon relative to the project folder. | `"Icons/<icon>.png"` |
| `model` | string | No | Published Addressables runtime key for this weapon's prefab. | `"<model-key>"` |
| `fit` | string | No | `"auto"` fits the authored model to the donor weapon and derives sockets. | `"auto"` |
| `scale` | number | No | Positive scale override for the model fit. | `1` |
| `rotate` | string of three comma-separated numbers | No | Model rotation in degrees. | `"0,0,0"` |
| `offset` | string of three comma-separated numbers | No | Position correction, in metres, added to the fit. | `"0,0,0"` |
| `flip` | boolean | No | Reverses which end of an automatically fitted model points toward the muzzle. | `true` |
| `shoot` | string of three comma-separated numbers | If `model` is set without `fit: auto` | Muzzle socket position. Zero is a valid declared position. | `"0,0,0"` |
| `aim` | string of three comma-separated numbers | No | Aim socket position. | `"0,0,0"` |
| `shell` | string of three comma-separated numbers | No | Shell-ejection socket position. | `"0,0,0"` |
| `damage` | number | No | Positive standard-damage override. | `40` |
| `spread` | number | No | Positive spread override in degrees. | `1` |
| `damagetype` | string | No | Shipped damage-type definition for the payload. | `"<damage-type-def>"` |
| `keywords` | string | No | Extra damage keyword definitions and values, separated by `;`; each clause is `DEFNAME=VALUE`. | `"Burning_DamageKeywordEffectorDef=40"` |
| `projectile` | string | No | Projectile definition, or a shipped weapon definition whose projectile to borrow. | `"<projectile-or-weapon-def>"` |
| `flash` | string | No | Shipped weapon definition whose muzzle effects to borrow. | `"<shipped-weapon-def>"` |
| `tint` | string | No | Hex color for a private copy of the projectile visuals. | `"#RRGGBB"` |
| `trail` | number | No | Positive trail lifetime in seconds. | `0.5` |
| `count` | number | No | How many of this weapon a new campaign's starting storage holds, on every difficulty. | `1` |
| `clips` | number | No | How many of the weapon's first compatible ammunition item go into that starting storage. | `1` |

Use [the weapon-add recipe](../recipes/weapon-add.md) for the complete build and fit sequence.
