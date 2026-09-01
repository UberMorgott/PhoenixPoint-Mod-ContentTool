# RECIPES — copy-ready exact sequences + their gotchas

> **Internal maintainer index.** MkDocs excludes this file from the public site. Mod authors should
> start at [`index.md`](index.md), not with the research and handoff files listed below.

> Hard-won API sequences. Each is already proven (see `PROVEN-FOUNDATIONS.md`). Copy them as
> written; the gotcha under each is the reason the naive version fails.

## 1. AssetsTools.NET — library and deployment

- Source: `nesrak1/AssetsTools.NET` @ commit `9aa8c6e` (git main). **Vendor/build from source.**
- **NuGet 3.0.5 is BROKEN** for adding a class absent from the file's typetree:
  `NullReferenceException at TypeTreeType.set_StringBuffer` (in
  `ClassDatabaseToTypeTree.ConvertInternal`). Fixed only on main; no newer NuGet exists.
- Targets `netstandard2.0;net35;net40`, **zero PackageReferences** → retarget to `net472`,
  nothing breaks. Mono compatibility is a non-issue.

### ILRepack MERGE is mandatory

```text
add AssetsTools.NET to MergeInputs in ILRepack.targets   -> ContentTool.dll
```

**Why:** PPModLoader loads mods via `Assembly.Load(byte[])` and installs **no
`AssemblyResolve` handler**. A sibling `AssetsTools.NET.dll` therefore throws
`TypeLoadException` at runtime — which is NOT a compile error, so it passes CI and dies on the
end user's machine. A sibling DLL is FORBIDDEN in production.
Citation: `decompiled\AssemblyCSharp\Assembly-CSharp\src\PhoenixPoint.Modding\ModSDKContext.cs:51-63`
(`LoadModdingAssembly` = `Assembly.Load(rawAssembly[, pdb])`); zero `AssemblyResolve` hits in the
whole `Assembly-CSharp` tree.

### `classdata.tpk`

- 289 605 B, extracted from UABEA v8 `uabea-windows.zip` (nesrak1/UABEA). **Not** shipped in
  AssetsTools.NET releases.
- MUST be an **embedded resource** in `ContentTool.dll`. A hardcoded scratchpad path means the
  mod works on exactly one machine.

## 2. Creating a new object (bypasses the NRE-ing helper)

`AssetsManager.CreateValueBaseField` NREs for a freshly-registered type — bypass it:

```csharp
var info = AssetFileInfo.Create(afile, pathId, classId, cldb, false); // registers TypeTreeType
                                                                     // CAPTURE the return value
var tf = new AssetTypeTemplateField()
    .FromClassDatabase(cldb, cldb.FindAssetClassByID(classId), false);

var bf = ValueBuilder.DefaultValueFieldFromTemplate(tf);

info.SetNewData(bf);
afile.Metadata.AddAssetInfo(info);

bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);
bun.Write(writer);
```

**Gotcha:** discarding the `AssetFileInfo.Create` result and inventing an `info` from elsewhere
does not compile / does not register — the returned instance IS the one to fill.

## 3. Binary payload into a TextAsset

```csharp
field["m_Script"].Value = new AssetTypeValue(payload, isString: true); // length-prefixed raw
var roundTripped = field["m_Script"].AsByteArray;                      // read back HERE
```

**Gotcha:** `field["m_Script"].AsString` CORRUPTS bytes to U+FFFD (`EF BF BD`). Never use the
string path for arbitrary binary, and never verify via `AsString`.

## 4. Texture2D authored inline

Set `m_Width`, `m_Height`, `m_TextureFormat`, `m_CompleteImageSize`; put the image data in the
object itself; and **zero `m_StreamData`** — `offset = 0`, `size = 0`, `path = ""`.
No `.resS` sidecar in v1.

## 5. `m_Container` registration

- Append every new object to `AssetBundle.m_Container`, else it exists in the file but is
  unreachable by name.
- Lowercase addressable-style names, ONE frozen form: `assets/<mod>/...`
  (`assets/<mod>/textures/soldier_body`, `assets/<mod>/audio/banks/<bankName>.bnk`,
  `assets/<mod>/audio/streams/<mediaId>.wem`).
- `preloadIndex = 0`, `preloadSize = 0`, `m_FileID = 0` for directly contained assets.
- The generated release loader's constants MUST be the SAME strings, emitted by the writer —
  assert at bake time that each loader constant exists in `m_Container`, else `LoadAsset<T>`
  returns `null` on every user's machine.
- Test hygiene: rename the bundle internally (`m_Name` + the CAB entry) before writing, so an
  already-loaded vanilla bundle cannot masquerade as success.

## 6. The three Wwise bank shapes (version 140)

| Shape | Chunks | `eStreamType` | Use |
|---|---|---|---|
| A embedded | `BKHD DIDX DATA HIRC [STID]` | `0` | short SFX, media held in engine RAM until `UnloadBank` |
| B streamed | `BKHD HIRC [STID]`, no media chunks | `2` | long music/ambience; `.wem` read as FILE |
| C replacement | `BKHD DIDX DATA`, **no HIRC**, DIDX declares the GAME's media ID | `0` | replace a shipped sound |

### Chunk / DIDX rules (generator)

- Canonical order: `BKHD, [INIT], [PLAT], [STMG], DIDX, DATA, HIRC, STID`; `BKHD` first.
- DIDX size **`% 12 == 0`** and strictly **BEFORE** DATA. `LoadMedia` early-returns on an empty
  media index, so DIDX-after-DATA loads clean and silently registers NOTHING.
- Exactly **0** trailing bytes after the last chunk.
- Media offsets inside DATA are not validated by the engine — validate them yourself.
- `dwProjectID` is never read → foreign banks are accepted.
- Validate every generated bank against the oracle `decompiled\AkSoundEngine\hirc_parse.py`
  (53 banks / 19110 objects / 0 mismatches).
- v140 HIRC traps: Event `ulActionListSize` is a **VARINT** (not u8); a
  `bIsOverrideParentMetadata`/`uNumMetadata` block sits between the FX block and
  `bOverrideAttachmentParams` (new in 2021.1); `bitsFXBypass` exists only when `uNumFx != 0`;
  StateChunk and RTPC ParamID use varints; Positioning fields are doubly conditional; the plugin
  blob appears only when `(ulPluginID & 0xF) == 2`; Sound has no ChildrenList.

## 7. `AkBankSourceData` + `eStreamType`

```text
ulID                u32
ulPluginID          u32
eStreamType         u8     0 = in-bank media, 2 = streamed (FILE)
sourceID            u32
uInMemoryMediaSize  u32
```

`eStreamType` is the field that sends Wwise to FILE. `eStreamType = 2` with a DIDX entry for the
same `sourceID` is a generator bug; `eStreamType = 0` with no embedded media is silence.

## 8. Routing

```text
OverrideBusId  = 0x5C770DB7      // "UI" bus from Init.bnk, \Master Audio Bus\UI
DirectParentID = 0               // silently skips AddChild, returns AK_Success
```

- BOTH zero → the node has no `m_pBusOutputNode`: a valid playingID is handed out and
  **nothing is audible**. No shipped node has both zero.
- `OverrideBusId` is resolved FIRST at load and a **missing bus is a HARD BANK-LOAD FAILURE**,
  not silent audio — an invented bus ID fails at `LoadBankMemoryCopy`.
- `0x5C770DB7` verified present in `Init.bnk`: HIRC id `0x5C770DB7`, type **8 (AuxBus)**, 115 B,
  name `"UI"`; `fnv1_lower32("UI") == 0x5C770DB7`.

## 8b. Wwise ID generation

Name→ID is **FNV-1 32-bit (multiply-then-XOR), name LOWERCASED**, no masking. Exact on
**1078/1078** shipped name-based pairs (events, banks, switches, states, RTPCs, triggers).
FNV-1a and non-lowercased FNV-1 score **0/1078** — do not "fix" the order of the ops, and do
not mask to 30 bits (that matches only a subset and silently breaks the rest).

```python
def fnv1_lower32(name: str) -> int:
    h = 2166136261
    for b in name.lower().encode('utf-8'):
        h = (h * 16777619) & 0xFFFFFFFF
        h ^= b
    return h
```

- Lowercase FIRST, then UTF-8 bytes. Wwise names are case-insensitive; the ID is the lowercased form.
- Applies to: Event, SoundBank, Bus/AuxBus, Switch, SwitchGroup, State, StateGroup,
  GameParameter, Trigger.
- **WARNING — media/WEM IDs are ALLOCATED, never hashed.** 0/7691 shipped media IDs match any
  hash of their name, and 242 ShortNames map to several IDs each. Allocate a media ID
  (counter/random) and validate it for membership against the PP set + the project's own set;
  never derive it from a filename.
- Index + checker: `ContentTool\data\pp_wwise_index.json`, `ContentTool\tools\wwise_hash_check.py`.
  Membership tests use the index's **`_media_ids_all`** (7697 = complete occupied set), not
  `_media_ids` (7691 manifest-named only) — the 6-ID gap is unguarded otherwise, and a colliding
  streamed `<mediaId>.wem` shadows a game sound with NO error (see recipe 10).

## 9. Loading a bank

```csharp
var b = bundle.LoadAsset<TextAsset>("assets/mymod/audio/banks/mymod.bnk").bytes;
var h = GCHandle.Alloc(b, GCHandleType.Pinned);
try {
    var r = AkSoundEngine.LoadBankMemoryCopy(h.AddrOfPinnedObject(), (uint)b.Length, out var bankId);
    if (r != AKRESULT.AK_Success) throw new Exception($"Wwise bank load failed: {r}");
} finally { h.Free(); }
```

**Never `LoadBankMemoryView`:** for version ≥ 135 it checks `addr % uAlignment` as a real
division; `uAlignment == 0` is a **#DE process crash**, not an AKRESULT. View also keeps a
pointer into the caller's buffer until `UnloadBank`. `Copy` copies ⇒ freeing the handle at once
is safe.

## 10. Streamed WEM extraction

- Extract the packaged `.wem` TextAsset to `Application.persistentDataPath`
  (`%LOCALLOW%\Snapshot Games Inc\Phoenix Point\`) as `<mediaId>.wem`.
- **No `AddBasePath` call is needed**: PP registers three base paths at init
  (`AkWwiseInitializationSettings.InitializeSoundEngine`,
  `API/AK.Wwise.Unity.API.decompiled.cs:16632-16676`) —
  `SetBasePath(<StreamingAssets>\Audio\GeneratedSoundBanks\Windows)`,
  `AddBasePath(PersistentDataPath)`, `SetDecodedBankPath` + `AddBasePath(DecodedBankFullPath)`.
- ONE setup-time `AkSoundEngine.AddBasePath(<dir>)` before `LoadBankMemoryCopy` is required ONLY
  if a mod places streamed media somewhere else (proven ARM1 / `rr_testB`).
- **Ordering:** extraction must COMPLETE before `LoadBankMemoryCopy` and the first `PostEvent`.
- Write through a temp file, flush/close, then atomically replace `<mediaId>.wem`; verify the
  extracted bytes against the packaged length/hash as a SEPARATE assertion.
- Collision danger: the filename IS the media ID and base paths are global — a colliding ID
  silently shadows a game sound with no error. Only the validator catches it.

## 11. Pumping the engine in a spike

```csharp
AkSoundEngine.RenderAudio();
AkCallbackManager.PostCallbacks();
```

Never `Thread.Sleep` on Unity's main thread: the frame loop stalls,
`AkSoundEngineController.LateUpdate` never runs, `PostCallbacks` never dispatches, and queued
`PostEvent`s flush together on wake (two tones then overlap and comb-filter — heard as a pitch
change, i.e. a wrong conclusion).

## 12. Measurement instrument

`PostEvent` with `AK_EndOfEvent | AK_Duration` (`1|8`); `AkDurationCallbackInfo` yields
`fDuration`, `fEstimatedDuration`, `mediaID`, `bStreaming`. Log block:

```text
POST <label>: playingID=<n> dur=<n>ms estDur=<n>ms mediaID=<n> streaming=true(FILE)|false(MEMORY) endOfEvent=<n>ms
```

Reference: event `GUI_StatsPlusClick = 784388130`, media `18839791`, dur=1200ms, normally
`streaming=true(FILE)`. Generated test tones are exactly **500 ms** — that is what distinguishes
them.

## 13. Reading game textures

Bundled game textures are not CPU-readable → `RenderTexture` + `Graphics.Blit` + `ReadPixels`
(`ResourceReplacerMain.DuplicateTexture`). Textures WE author are readable via `GetPixel`.

## 14. Adding a whole new model (author-facing)

Drop one `.glb` into the project's `Content\Models\`. Nothing goes in `ppcontent.json` — the file
IS the declaration, exactly like `Content\Textures\*.png`.

```text
MyMod\
  ppcontent.json          { "id": "author.mod", "bundle": "MyMod.bundle" }
  Content\Models\thing.glb
  Content\Textures\thing.png     (optional: same stem -> _MainTex of the model's Material)
```

`ct_project <name>` bakes it into `Dist\MyMod.bundle` as a prefab addressed
`assets/<id>/models/thing`, which the game loads with
`AssetBundle.LoadFromFile(...).LoadAsset<GameObject>("assets/<id>/models/thing")`. Zero runtime
code ships with it.

- **Rigged .glb** (mesh + armature) → root, one bone per joint, `SkinnedMeshRenderer`, and a Mesh
  built from the empty template carrying the file's own bind poses and per-vertex weights. A vertex
  may be shared between two bones — that is the thing an `.obj` cannot express (the format carries no
  skin data at all). `Content\Meshes\` accepts `.glb` too, for the REPLACE path — see §17.
- **The armature's SHAPE comes across too**: a bone is parented to the bone your .glb parents it
  to, so moving a hip takes the head with it and the rig can be animated rather than only posed.
  Name your bones the way the animation you intend to drive them with expects — a bone's identity
  to the engine is the CRC of its PATH under the model root (`hip/spine/head`), so re-parenting a
  bone in Blender renames it as far as any clip is concerned.
- **Static .glb** (no armature) → root + `MeshFilter`/`MeshRenderer` over a Mesh built the same way.
- Export from Blender as **glTF Binary (.glb)**, Normals ON, and the **Compression (Draco)** box
  UNTICKED — that is the one compression still refused. `EXT_meshopt_compression` and
  `KHR_mesh_quantization` (what `gltfpack` and "optimised for the web" produce) are decoded IN-HOUSE
  since gate U10, so a file carrying them needs no conversion step at all. The reader refuses
  anything else BY NAME with the cause and the fix.
- **A `.glb`'s own ANIMATIONS come across with it** (gates U8/U9, in-game green 2026-08-23): every
  clip in the file is baked, per bone, per frame, and an `AnimatorOverrideController` over a shipped
  base controller lets Mecanim play one. `"loop"` and `"play"` in `ppcontent.json` name the file's own
  clips (U12/U13). A clip that drives no bone of the rig is skipped with its own line, never silently.
- Natural loop: `ct_extract` a shipped model to `.glb` → edit in Blender → drop it back.

Ceilings: four influences per vertex (more than four is refused by name — Blender's "Include All"
emits a second joint set that the tool rejects rather than silently dropping weights), one submesh,
and the Material's shading is ungated. ~~Nothing yet
plays a CLIP over an imported rig~~ — **closed, U7/U9**. ONE clip per model plays: a state machine
that switches Idle/Walk needs a `ControllerConstant`, which this route does not serialize. A SHIPPED
clip's own curves are editable in place (§18).

## 15. Iterating on a model LIVE, no restart (author-facing, DEV ONLY)

`ct_project` bakes the shipping output. `ct_route7 apply` applies it live unless the target bundle is
already loaded; in that case it refuses by name and asks for a restart. The dev workbench writes the
same slots into what is already on screen — the game keeps running, the author keeps looking at the
character.

```text
ct_seamprobe on                       # guid: targets - open the screen that shows the character
ct_scan on                            # OR: name: targets, for anything no resolve goes through
ct_replace <targetpath> <file|value>
ct_revert                             # every swap back, by object reference
```

The TARGET PATH names the slot, so there is one command and no new verbs:

| slot in the target path | what the second argument is | goes to |
|---|---|---|
| `...@Renderer.materials[i].tex:_MainTex` | a `.png` / `.jpg` | that texture property, on a CLONED material |
| `...@SkinnedMeshRenderer.mesh` | a `.glb` / `.obj` | the mesh, rebound to the target's OWN skeleton |
| `...@MeshFilter.mesh` | a `.glb` / `.obj` | the mesh of a static prop |
| `...@Renderer.materials[i].col:_Color` | `r,g,b,a` (invariant, `0.5` not `0,5`) | that colour property, on a clone |
| `...@Renderer.materials[i].num:_Metallic` | a number | that float property, on a clone |

- The model is read by the SAME importers the bake uses (`GlbReader`+`ModelBuild`, `ObjCodec`+
  `MeshBuild`), so the preview is the mesh that would ship, not a look-alike.
- **A rigged target keeps the shipped skeleton, and a `.glb` keeps its OWN WEIGHTS.** A `.glb`
  carrying an armature binds with its own `WEIGHTS_0` onto that shipped skeleton, bones matched BY
  NAME (so the file's joint ORDER is free) — see §17. A `.glb` whose armature is foreign to the
  target falls back to `SkinFields.Rebind`'s nearest-bind-pose synthesis — one full-weight influence
  per vertex, a joint creases instead of bending — and the bake log SAYS SO, so the downgrade is
  never silent. A file with NO armature at all (an `.obj`, an unweighted `.glb`) is REFUSED for a
  rigged target and replaces static objects only. `localBounds` moves with the mesh, or the new geometry is culled
  wherever the old one was not.
- The game's Mesh/Material/Texture objects are NEVER written — a clone is assigned — so `ct_revert`
  puts back the origin OBJECTS by reference, not by value.
- ONE swap per anchored object at a time: a second `ct_replace` on the same anchor is refused until
  `ct_revert`. Re-running the same command after editing the file is the whole loop (no watcher).
- DEV ONLY. `name:` targets can never be baked (`ReplacementSet.Bakeable` refuses them), and
  SHIPPING stays zero runtime code.
- Gate: `ct_liveswap` (R5) — drives the real `ct_replace` with the sample `.glb` onto a renderer
  already on screen and asserts the file's own numbers (6 verts, 9 indices), the binding, the
  material property, and the revert by reference, against an untouched control renderer.

## 16. Publishing a KEY out of the mod's own bundle (author-facing) — route iii

Route vii (`"replace"`) ships a patched COPY of a whole shipped bundle to reach an object buried
inside a shipped asset. Route iii (`"publish"`) claims a catalog KEY instead, and serves it out of
the mod's own `Dist\<bundle>` — no copy of anyone's bundle, and the conflict granularity goes from
1-of-90 bundles to 1-of-~4029 addressables. Both are ZERO RUNTIME CODE and both edit the same
`catalog.json` through the same ledger.

```json
{
  "publish": [
    { "key": "morgott.sample/probe_tex", "asset": "textures/swatch", "type": "Texture2D" },
    { "key": "02_Bodyparts/ALN_Fireworm_BodyAll_DMG_Ready.prefab", "asset": "models/rig" }
  ]
}
```

```text
ct_project <name>            # bake the mod bundle first - publishing never bakes
ct_catalog apply <name>      # publish the keys LIVE - no restart, nothing written to the install
ct_catalog verify
ct_catalog status            # what is published right now
                             # un-publish = disable the mod; there is no catalog.json edit to revert
```

- `key` **absent from the game's catalog** → an ADD (a new address, `type` required, one of the
  catalog's own `m_resourceTypes`: `Texture2D`, `GameObject`, `Mesh`, `Material`, `AnimationClip`…).
  `key` **present** → a REPOINT of that address at your asset; the shipped type and primary key stay.
  The tool decides; there is no mode to choose.
- `asset` is the path under your own bundle exactly as `Content\` spells it (`textures/swatch`,
  `models/rig`). It is checked against the built bundle before anything is written — a key pointing
  at a name the bundle does not contain would load as `null` forever, silently.
- `deps` (optional, `;`-separated shipped bundle files) mounts extra archives for an asset whose
  external PPtrs need them. A REPOINT inherits the shipped entry's own dependency set and normally
  needs none. Your bundle is always FIRST in the set — the provider loads the asset out of the first
  dependency and mounts the rest.
- **What route iii CANNOT reach:** anything that is not a catalog key — a Texture2D/Material/Mesh
  *inside* a shipped prefab, loose video, Wwise media, non-addressable scene props. Those stay
  `"replace"` (route vii). Publishing a buried object is not a smaller version of it; it is a
  different, much heavier authoring job (re-author the whole owning prefab).
- Two mods publishing the same key are refused by name, twice over — at the ledger and again in the
  writer. This matters more than it sounds: a duplicate key does not degrade, it makes
  `Addressables.InitializeAsync` throw and the game unlaunchable for **every** installed mod. The
  writer therefore validates a whole rebuild in memory and refuses before a byte lands.

## 17. Replacing a shipped mesh WITH ITS OWN SKIN WEIGHTS (author-facing)

Drop a `.glb` into `Content\Meshes\` — the REPLACE path takes it beside `.obj` now
(`ContentProject.cs:30`; the old `.obj`-only refusal was `ProjectBake.cs:480`). Proven in-game as
P6/R6.

```text
MyMod\Content\Meshes\torso.glb        # armature + WEIGHTS_0, exported from Blender as glTF Binary
```

- **Bones are matched BY NAME** against the target's shipped skeleton, so the file's joint ORDER is
  free — reordering joints in Blender changes nothing. Name your armature's bones the way the
  SHIPPED skeleton spells them.
- The file's own `WEIGHTS_0` is what gets written, so a vertex may be shared between bones and a
  joint BENDS. This is the ceiling U5b/R5 used to state, and it is retired.
- **A `.glb` whose armature is foreign to the target falls back** to nearest-bone synthesis (one
  full-weight influence per vertex, rigid) — and the bake log says which one you got. Read it; there
  is no silent downgrade, but there is also no error.
- **A file with no armature is REFUSED for a rigged target**, by name, and nothing is written: there
  are no weights to follow the skeleton with, so every vertex would weld to one bone. Weight the mesh
  to the target's own bones in Blender and export `.glb`. Static objects still take a bare mesh.
- **Bone names ripped from a live scene** carry a `#<Bone>_Addon => <BodyPartDef>` decoration
  (engine `Addon.cs:143`). ContentTool normalises them in `SkinBinder.Plain()`, so by-name binding
  works without manual cleanup.
- **Do NOT run `Apply All Transforms` in Blender** — it rewrites the skin and can destroy hand-
  painted weights. ContentTool handles the normal PP body-part shape (each addon joint under a
  different parent bone) without any transform application.
- Same path live in the dev workbench, no restart: `ct_replace <target>@SkinnedMeshRenderer.mesh
  <file.glb>` (§15).

## 18. Editing a SHIPPED animation clip's curves (author-facing)

An `AnimationClip` used to be ADD-only. One `replace` entry now scales one channel of a clip the
game already ships — same name, same bindings, same bank sizes, so every controller that plays it
keeps playing it and no runtime code is involved.

```json
{ "bundle": "aln_fireworm_assets_all.bundle", "asset": "Fireworm_unfurl", "clip": "position*3" }
```

- Grammar is `<channel>*<number>`, the same shape `"material"` uses. `position` and `scale` only:
  **`rotation` is refused by name** — a rotation curve is a quaternion and scaling one denormalises it.
- **Curves are NOT where a baked clip puts them.** A shipped clip carries its curves in the
  STREAMED and CONSTANT banks of `m_MuscleClip.m_Clip` and leaves the dense bank empty (measured:
  `Fireworm_unfurl` = 47 streamed curves + 53 constant floats, dense 0; `MV_RocketJumpIdle` = 40
  constant floats and nothing else). `ClipFields.MapCurves` walks all three under the one flat curve
  index, so an editor that only knew the dense bank would silently change nothing.
- Streamed layout: a `uint[]` of frames, `{float time; int keyCount; keyCount × {int curveIndex;
  float coeff0..3}}`. A key is a cubic in those four coefficients and evaluation is linear in them,
  so one factor over all four is that factor on the sampled value at every time.
- Which floats belong to which binding is checked, not assumed: the widths (position 3, rotation 4,
  scale 3, anything else 1) must add up to exactly the number of floats the three banks hold, or the
  clip is REFUSED rather than half-edited.
- `m_MuscleClipSize` is a function of the bank SIZES, and scaling values changes none of them — so
  it stays the shipped number.
- Proven in game (P7 / P7-ctl-channel / P7-sample): the engine samples `Fireworm_base` at
  `(-0.006446,0.128418,0.138676)` with the shipped clip and `(-0.019338,0.385253,0.416029)` with the
  edited one — the author's ×3, on the rig, not just in the file.
- Ceiling (`ponytail:` in `ClipFields.MapCurves`): the edit is one float→float function, so it can
  reshape values a curve already has but cannot add, remove or retime a key, or bind a bone the clip
  never bound. Authoring curves from a file is an importer, not this.

## 19. Replacing a shipped SOUND, music included (author-facing)

No JSON at all: a `.wav` **named after the media ID it replaces**, in a subfolder of its own.
Demo mod: `ContentTool\demos\MenuMusic\` (the vanilla main-menu music).

```text
ct_extract audio 208540756                       # writes 208540756.wav - edit it
MyMod\Content\Audio\Replace\208540756.mp3        # the file NAME is the target
autogate.ps1 -Commands 'ct_sound bake MyMod'     # one media-only .bnk per replacement into
                                                 # MyMod\Dist\Sounds - no shipped file is touched
ct_sound status [mediaId] | ct_sound probe <mediaId> | ct_sound selftest
```

- Coverage is the **3105 STREAMED** media, which are loose files; an embedded one is refused by name.
- **`.wav`, `.ogg` and `.mp3`** — the same whitelist `Content\Audio\` takes, and all three are
  decoded by the TOOL (gate A7). No flag, no launch option, no converter. A file the decoder cannot
  read is refused BY NAME and skipped; the rest of the project still installs.
- Source filenames may be non-ASCII on the ADD path (`Аве! Император.mp3` proven in game); on the
  REPLACE path the name must be the media ID, which is grammar, not encoding.
- **Nothing on disk is swapped.** The bake re-encodes the source as 16-bit PCM and wraps it in one
  media-only `.bnk` per replaced media id, inside the MOD's `Dist\Sounds`; ContentTool hands those
  to `AkSoundEngine.LoadBankMemoryCopy` at init and Wwise serves the game's own media id out of our
  bank. Our bank declares the media itself, so there is no shipped codec declaration to fight —
  PCM is the only codec this tool emits.
- **On MUSIC the engine reports `dur=0`** (a looping track has no duration), so the decoded-LENGTH
  arm goes VOID and identity rests on the file hash, the codec declaration and "still playing after
  3 s". `ct_sound probe <mediaId>` posts a media's event on any install, replaced or not — that is
  how the zero was shown to belong to the SOUND rather than to the replacement.
- **An event is not always named after its sound**: `MainMenuMusic` is played by
  `MainMenuMusicStart`. `<sound>` then `<sound>Start` are read off the shipped `<bank>.txt`.
- **There are no backups and no `revert`** — nothing was applied to a file. A bank cannot be
  unloaded once loaded (unloading it gives you silence, not vanilla), so unticking the mod leaves
  the sound live until you restart; the restart is the clean undo.
