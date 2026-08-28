# Discover game content

A replacement row needs names Phoenix Point already uses. This page shows how to obtain them from
the installed game instead of copying a guess from another mod.

[Open the developer console](../SHIPPING-A-CONTENT-MOD.md#open-the-developer-console). Run these
commands from the main menu or later; only `ct_list defs` needs the live def repository.

## What can be listed

```text
ct_list bundles [nameFilter]
ct_list assets <bundleFile> [typeFilter] [nameFilter]
ct_list videos [nameFilter]
ct_list audio [nameFilter]
ct_list defs <nameFilter> [typeFilter]
ct_list bones <bundleFile> <meshName> [nameFilter]
ct_list props <bundleFile> <materialName>
ct_list clip <bundleFile> <clipName>
```

The filters are case-insensitive substrings. Asset lookup after discovery is not: the `m_Name`
printed by `ct_list assets` must be copied with exact case into `ppcontent.json`.

Listings stop at 60 rows. If more match, the last line says how many remain and asks you to narrow
the filter. Independently, the console displays at most 80 lines and 400 characters per line. The
complete result is still written to:

```text
%USERPROFILE%\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\Player.log
```

No list command writes a report file.

### Bundles and asset names

Phoenix Point's Windows bundles are under
`PhoenixPointWin64_Data\StreamingAssets\aa\StandaloneWindows64\`. You do not browse that directory
by hand:

```text
ct_list bundles fireworm
ct_list assets aln_fireworm_assets_all.bundle Material Fireworm
ct_list assets aln_fireworm_assets_all.bundle AnimationClip unfurl
```

Bundle filenames commonly follow `<prefix>_<subject>_assets_all.bundle`. Useful empirical prefixes
include `aln_`, `an_`, `px_`, `nj_`, `sy_`, `neu_` and `dlc1_` through `dlc5_`. The convention is a
search hint, not a guarantee encoded by ContentTool.

`ct_list assets` prints Unity type, serialized `m_Name`, byte count and path ID. `Transform` and
other classes with no name print `(unnamed)`. Its type filter accepts Unity `AssetClassID` names such
as `Texture2D`, `Mesh`, `Material` and `AnimationClip`; apply the type filter before a narrow name
filter to avoid ambiguity.

### Defs

Manifest fields such as creature `donor`, weapon `clone`, `damagetype` and `keywords` name defs, not
bundle assets:

```text
ct_list defs Shotgun WeaponDef
ct_list defs Burning DamageKeywordDef
ct_list defs Fire DamageTypeBaseEffectDef
ct_list defs Swarmer TacCharacterDef
```

In `ct_list defs Burning DamageKeywordDef`, `Burning` is the name filter and
`DamageKeywordDef` is the type filter. Results such as `Burning_DamageKeywordEffectorDef` are def
instance names of that type; the different suffix is not a spelling error.

The name filter is required. The optional type filter matches the concrete class and its base-class
chain, so a `StandardDamageTypeEffectDef` can match `DamageTypeBaseEffectDef`. Before the def
repository is ready, the command reports `VOID`; retry at the main menu.

Bracketed names such as `E_Projectile [PX_ShotgunRifle_WeaponDef]` are embedded defs owned by the def
in brackets. Copy the whole printed name when a field needs that embedded def.

### Bone names

For a rigged mesh replacement, list the shipped skeleton before touching the armature:

```text
ct_list bones mutoid_assets_all.bundle Geo_Head02_V01
```

The result begins like this:

```text
34 of 34 bone(s) of Mesh 'Geo_Head02_V01' in mutoid_assets_all.bundle match '', numbered by m_BindPose index
  0: Mutoid
  1: FacePincer1_L
```

Your replacement GLB must contain exactly the same bone names and count. Joint order may differ;
binding is by name. Use the optional filter when a skeleton exceeds the 60-row display cap.

`ct_extract mesh` cannot substitute for this list: its GLB contains synthesized nodes per bind pose,
not the real shipped bone names or hierarchy.

### Material properties

Do not guess the property part of a material row:

```text
ct_list props aln_fireworm_assets_all.bundle ALN_Fireworm_DMG
```

The one-line result includes the shader reference and serialized texture, float and colour entries:

```text
aln_fireworm_assets_all.bundle 'ALN_Fireworm_DMG': shader fileID=2 pathID=... | _BumpMap -> ... | _Glossiness=0.9 | _Color=1,1,1,1
```

The current manifest route can set only a float, despite the report also showing textures and
colours. Use the exact property spelling in a `material` row.

### Animation clips

List names first, inspect one second:

```text
ct_list assets aln_fireworm_assets_all.bundle AnimationClip
ct_list clip aln_fireworm_assets_all.bundle Fireworm_unfurl
```

The inspector reports the serialized clip rather than guessing its role:

```text
clip 'Fireworm_unfurl' bindings=30 ... legacy=False
```

The clip report names its binding count, signature, dense/streamed data, sample rate, loop and legacy
state. It does not reveal which creature actually selects that clip or extract the clip. See the
[animation contract](animation-reference.md) for the slots that select clips.

### Videos and audio

Videos are loose `.webm` files under `StreamingAssets\StreamableCopiedAssets\`; audio is loose
`.wem` under `StreamingAssets\Audio\`:

```text
ct_list videos intro
ct_list audio 18839791
```

Video stems are useful names. Loose Wwise filenames are decimal media IDs, so the audio list is not
a human-readable catalogue. Discover a sound by observing the event that played it; the walkthrough
below shows the full chain.

## What can be extracted

```text
ct_extract tex <bundleFile> <assetName>
ct_extract mesh <bundleFile> <assetName>
ct_extract video <name>
ct_extract audio <wemName>
```

Outputs are kept outside every mod folder so a Workshop update cannot erase them:

```text
%USERPROFILE%\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\ContentTool\Extracted\
```

| Input | Output | What is lost |
|---|---|---|
| `tex` | `<bundleStem>\<assetName>.png` | mips after level 0, original BC/DXT format, colour-space and import settings |
| `mesh` | `<bundleStem>\<assetName>.glb` | real bone names/hierarchy, materials and blendshapes; geometry, UV0/1, normals, tangents, submeshes, weights and bind poses are retained |
| `video` | `videos\<name>.webm` | nothing; byte-for-byte copy |
| `audio` | `audio\<name>.wem` and `<name>.wav` | the WEM is unchanged; the WAV is a decoded editing copy |

Material, `AnimationClip`, prefab/GameObject, shader and def extraction do not exist. Ambiguous asset
names are refused rather than guessed.

Extracted Phoenix Point files are for inspection and alignment. Do not put them or derivative game
art into a release unless you have redistribution rights.

## Naming rules for your files

ContentTool scans these exact folders and extensions:

```text
Content\Textures\*.png *.jpg *.jpeg
Content\Meshes\*.obj *.glb
Content\Models\*.glb
Content\Videos\*.webm *.mp4 *.mov
Content\Audio\*.wav *.ogg *.mp3
Content\Audio\Replace\*.wav *.ogg *.mp3
```

The lowercased stem is the identifier. `Content\Models\FieldScanner.glb` becomes
`models/fieldscanner` inside your bundle. `Content\Audio\alarm.stream.wav` becomes an added sound
named `alarm` and is marked streamed; without `.stream` it is embedded. A same-stem collision within
one folder is a hard refusal that names both files.

For a video row, including `asset` means replace an existing catalog row. Omitting `asset` means add
a new row whose 32-lowercase-hex RuntimeKey is derived from `MD5("<mod id>/<video stem>")` and
printed for your code to use.

## Walkthrough: replace a texture

Goal: replace the Acidworm albedo.

1. Find a likely bundle:

   ```text
   ct_list bundles worm
   ```

2. List textures and copy the exact `m_Name`:

   ```text
   ct_list assets aln_acidworm_assets_all.bundle Texture2D acidworm
   ```

3. Extract it for dimensions and UV context:

   ```text
   ct_extract tex aln_acidworm_assets_all.bundle acidworm_low_albedo
   ```

4. Create your own art and save it as
   `Phoenix Point\Mods\MyWorm\Content\Textures\myworm.png`.
5. Use this complete `ppcontent.json`:

   ```json
   {
     "id": "yourname.myworm",
     "bundle": "MyWorm.bundle",
     "replace": [
       {
         "bundle": "aln_acidworm_assets_all.bundle",
         "asset": "acidworm_low_albedo",
         "texture": "myworm"
       }
     ]
   }
   ```

6. Bake and apply:

   ```text
   ct_project MyWorm
   ct_route7 apply MyWorm
   ```

`asset` is copied exactly from step 2. `texture` is the lowercased source stem.

## Walkthrough: replace a sound

Goal: replace the sound you can hear but cannot name.

1. Arm a watch, then make the sound happen before the timer expires:

   ```text
   ct_voices watch 20
   ```

2. Read the event-to-media line. A usable result has this shape:

   ```text
   event 784388130 x1 'GUI_StatsPlusClick' in UI -> media 18839791 'GUI_StatsPlusClick' - replaceable
   ```

   `STOP event` means find the matching Start event. `no STREAMED media` means the audio is embedded
   in a bank and this replacement route cannot reach it. `no shipped bank .txt names this event`
   means the shipped listings cannot resolve the event.

3. Confirm and hear the target:

   ```text
   ct_sound status 18839791
   ct_sound probe 18839791
   ```

4. Extract the WEM and an editable WAV:

   ```text
   ct_extract audio 18839791
   ```

5. Make your own replacement. Either name it
   `Content\Audio\Replace\18839791.mp3`, or keep a descriptive filename and declare it. This complete
   manifest uses the descriptive form:

   ```json
   {
     "id": "yourname.uiclick",
     "bundle": "UiClick.bundle",
     "sounds": [
       {
         "media": 18839791,
         "file": "my_click.mp3"
       }
     ]
   }
   ```

6. Bake the replacement bank:

   ```text
   ct_sound bake UiClick
   ```

The source belongs at `Content\Audio\Replace\my_click.mp3`; the output is
`Dist\Sounds\18839791.bnk`.

## Walkthrough: replace a video

Goal: replace the new-campaign intro picture.

1. Ask the live defs which key and row the game uses:

   ```text
   ct_video defs
   ```

   Find the `PP_Intro_Cinematic` entry and copy its streamable row.
2. Narrow the loose-file list and extract the file:

   ```text
   ct_list videos intro
   ct_extract video PP_Intro
   ```

3. Produce your own compatible clip and save it as
   `Content\Videos\myintro.webm`.
4. Use this complete `ppcontent.json`:

   ```json
   {
     "id": "yourname.myintro",
     "bundle": "MyIntro.bundle",
     "replace": [
       {
         "video": "myintro",
         "asset": "StreamableCopiedAssets/Videos/Factions/Phoenix/PP_Intro.webm"
       }
     ]
   }
   ```

5. Serve and verify the row without restarting:

   ```text
   ct_video live MyIntro
   ct_video resolve <runtime-key-printed-by-ct_video-defs>
   ct_video play PP_Intro_Cinematic
   ```

The picture, Wwise audio and subtitles are separate assets. This replaces only the picture; use the
[video recipe](videos.md) for the other two.

## Known discovery gaps

These are limits of the shipped tool, not tasks you are expected to solve by guessing:

1. There is no key-to-bundle browser. If the subject is absent from bundle filenames, finding its
   bundle still means narrowing and trying candidates. A future `ct_list keys` would need to expose
   the Addressables key-to-bundle mapping.
2. Animation assets can be listed and one clip summarized, but the tool cannot show which
   AnimatorController states or actor defs select them, cannot dump the controller, and cannot
   extract an `AnimationClip`.
3. `ct_list bones` gives the required flat bone-name list, but no command dumps the shipped bone
   hierarchy.
4. Only loose/streamed Wwise media can be extracted and replaced. Media embedded in banks remains
   unreachable. Human-readable Wwise names are recoverable only where shipped bank text listings
   expose the event/media relationship.
5. `ct_list props` exposes serialized material properties, but the manifest writes floats only. It
   does not dump a shader's complete declared property schema or author a new material.
6. No command lists all live scene object names, component paths, renderer material indices or
   `ct_replace` target paths. The live-preview binding step therefore remains manual and is not
   available for every object.
7. Listings are not persisted as files. Narrow filters repeatedly or read the full console output in
   `Player.log`.

The safe response to one of these gaps is to change route, inspect the asset in a suitable external
tool, or write behaviour code against a known def. A plausible name is not evidence.
