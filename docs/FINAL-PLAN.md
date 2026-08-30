# Phoenix Point Native Content Tool — Final Frozen Implementation Plan

**Status:** Final architecture freeze after Unity/Wwise in-game investigation  
**Date:** 2026-08-12  
**Target game:** Phoenix Point  
**Unity:** 2019.4.31f1 / Mono / `net472`  
**Wwise:** 2021.1.0 build 7575 / bank version 140  
**Implementation language:** C#  
**Repository strategy:** clean implementation, with proven ResourceReplacer code ported selectively  
**Runtime dependency strategy:** no mandatory shared ContentLib/runtime library  
**Developer workflow:** the Content Tool itself is a Phoenix Point mod DLL running inside the game  
**Release workflow:** bake native assets once; released mods talk directly to Unity and Wwise  
**Release package:** `MyMod.dll + MyMod.bundle`  
**Central rule:** do not invent a parallel runtime when Phoenix Point's own Unity/Wwise runtime can consume the final assets directly.

---

# 0. Non-negotiable architecture

## 0.1 Product shape

The Content Tool **is a Phoenix Point mod DLL running inside the game**.

There is no external production builder.

It has two independent responsibilities:

1. **Live replacement / authoring**
   - import source content;
   - materialize live Unity/Wwise objects directly in the running game;
   - preview;
   - validate;
   - hot-reload.

2. **Bake**
   - serialize already-understood imported content into a shippable native Unity AssetBundle;
   - generate Wwise bank data;
   - package all release content into one bundle.

The finished mod ships only:

```text
MyMod/
  MyMod.dll
  MyMod.bundle
```

`MyMod.bundle` contains:

```text
Unity assets
Wwise banks
streamed WEM payloads
stream manifest / generated metadata
```

No loose `.bnk` or `.wem` files are part of the distributed release.

Released mods:

- do not depend on `ContentTool.dll`;
- do not depend on `ContentLib.dll`;
- do not depend on ResourceReplacer;
- do not carry importers, codecs, bank parsers, Unity serializers, or AssetsTools.NET;
- talk directly to Unity and Wwise.

---

## 0.2 The critical split: Live vs Bake

**Live replacement needs no Unity serialization.**

The tool is already executing inside Unity.

Examples:

```text
PNG
  -> Texture2D.LoadImage
  -> live Texture2D

GLB mesh data
  -> new Mesh()
  -> vertices / normals / UV / indices
  -> live Mesh

material recipe
  -> new Material(...)
  -> live Material

animation source
  -> imported animation representation
  -> live preview path where supported

audio source
  -> PCM WEM / generated bank
  -> LoadBankMemoryCopy
```

**Serialization belongs to exactly one feature: Bake.**

Do not let AssetsTools.NET, serialized-file concepts, PPtr layout, type trees, UnityFS writing, or bake-specific code leak into the live authoring path.

---

## 0.3 One imported representation

Live materialization and bake serialization must consume the **same imported intermediate representation**.

```text
GLB / OBJ / PNG / WAV / OGG / recipes
                    |
                    v
             Imported Content IR
              /              \
             /                \
            v                  v
   Live Materializer      Bake Serializer
     Unity/Wwise          AssetsTools.NET
      objects             native bundle
```

Core IR may include:

```text
ImportedModel
ImportedNode
ImportedMesh
ImportedSubMesh
ImportedTexture
ImportedMaterial
ImportedSkeleton
ImportedSkin
ImportedMorphTarget
ImportedAnimation
ImportedAudio
```

This is a major diagnostic seam:

> If live preview succeeds and the baked result fails, the fault is in serialization/bake, not in the importer.

Do not implement two unrelated import pipelines.

---

# 1. PROVEN foundation — do not re-open

Everything in this section has already been proven by an in-game measurement or successful bake spike.

Do not spend implementation time re-proving these architectural questions unless a regression appears.

## 1.1 Unity AssetBundle runtime

PROVEN:

- `AssetBundle.LoadFromFile` works inside Phoenix Point.
- Phoenix Point ships `UnityEngine.AssetBundleModule`.
- A repacked Phoenix Point bundle loads successfully.
- Repacked texture pixels reach the GPU.
- Mesh data survives repack.
- Brand-new objects can be added to a bundle.
- Newly added assets become reachable through `AssetBundle.m_Container`.
- `GetAllAssetNames` sees the newly registered names.
- A faked 16-byte zero `old_type_hash` is accepted by the engine in the proven path.
- Binary `TextAsset` data survives byte-for-byte through the Unity runtime.
- AssetsTools.NET can write a bundle **from inside Phoenix Point's Mono runtime**.
- The baked bundle can immediately be loaded again by `AssetBundle.LoadFromFile`.

Proven commands/harness include:

```text
rr_bundle1
rr_bundle2
rr_bake
```

---

## 1.2 Closed Unity gates

The following gates are already closed:

```text
U0a  minimal native UnityFS / SerializedFile loads          PROVEN
U0b  binary TextAsset round-trip, exact bytes               PROVEN
U1   Texture2D                                              PROVEN
U2   Static Mesh                                            PROVEN
```

Do **not** schedule these as research tasks again.

They remain regression tests.

---

## 1.3 Binary TextAsset

Exact payload proven through the engine:

```text
00 FF C3 28 00 00 41 42 80 FE 00 7F
```

Therefore:

- `.bnk` can be packaged in the AssetBundle as binary `TextAsset`;
- streamed `.wem` payloads can be packaged in the AssetBundle as binary `TextAsset`;
- no loose-file distribution fallback is needed.

Important writer rule:

```csharp
field["m_Script"].Value =
    new AssetTypeValue(payload, isString: true);   // length-prefixed raw bytes
```

Read the payload back with:

```csharp
var roundTripped = field["m_Script"].AsByteArray;   // NOT AsString
```

Do **not** use:

```csharp
field["m_Script"].AsString = ...
```

for arbitrary binary data.

The high-level string path can corrupt bytes into replacement characters.

---

## 1.4 AssetsTools.NET inside Phoenix Point

Pinned source:

```text
nesrak1/AssetsTools.NET
commit: 9aa8c6e
```

The NuGet `3.0.5` package is not acceptable for the required create-new-class path.

Observed failure:

```text
NullReferenceException
TypeTreeType.set_StringBuffer
ClassDatabaseToTypeTree.ConvertInternal
```

The fix exists on main.

Therefore:

- vendor/build AssetsTools.NET from the pinned source revision;
- do not use the broken NuGet package;
- retarget the required project to `net472`;
- keep only the necessary library code.

AssetsTools.NET currently targets:

```text
netstandard2.0
net35
net40
```

and the required library has no package dependency chain that blocks `net472`.

---

## 1.5 PPModLoader deployment rule

PPModLoader loads mod assemblies via:

```text
Assembly.Load(byte[])
```

and does not install an `AssemblyResolve` handler suitable for sibling dependencies.

Therefore:

> **AssetsTools.NET MUST be ILRepack-merged into the Content Tool DLL.**

A sibling `AssetsTools.NET.dll` is forbidden in production.

Citation (offline-verified 2026-08-12):
`decompiled\AssemblyCSharp\Assembly-CSharp\src\PhoenixPoint.Modding\ModSDKContext.cs:51-63`
— `LoadModdingAssembly` is exactly `Assembly.Load(rawAssembly[, pdb])`, and there are **zero**
`AssemblyResolve` hits anywhere in the `Assembly-CSharp` tree.

This failure can pass compilation and CI and still die on the user's machine with `TypeLoadException`.

Add AssetsTools.NET to `MergeInputs` in `ILRepack.targets`.

---

## 1.6 `classdata.tpk`

Required class database:

```text
classdata.tpk
size: 289605 bytes
source: UABEA v8 package
```

It is not included in normal AssetsTools.NET releases.

Production requirement:

> `classdata.tpk` MUST be embedded as a resource in the Content Tool DLL.

No absolute scratchpad path is permitted.

Removing the current hardcoded path is the first productionization item.

---

# 2. Proven AssetsTools.NET bake recipes

## 2.1 Creating a new object

Do not use `AssetsManager.CreateValueBaseField` for a freshly registered type in the current path.

Use the proven sequence:

```csharp
var info = AssetFileInfo.Create(       // MUST capture the return value —
    afile,                             // it is the registered AssetFileInfo used below
    pathId,
    classId,
    cldb,
    false);

var tf = new AssetTypeTemplateField()
    .FromClassDatabase(
        cldb,
        cldb.FindAssetClassByID(classId),
        false);

var bf = ValueBuilder.DefaultValueFieldFromTemplate(tf);

info.SetNewData(bf);
afile.Metadata.AddAssetInfo(info);

bun.BlockAndDirInfo.DirectoryInfos[0].SetNewData(afile);

bun.Write(writer);
```

Wrap this into a small internal helper after preserving a regression test for the raw proven path.

---

## 2.2 Texture2D authored inline

For baked `Texture2D`:

- set `m_Width`;
- set `m_Height`;
- set `m_TextureFormat`;
- set `m_CompleteImageSize`;
- put image data inside the serialized object;
- zero `m_StreamData.offset`;
- zero `m_StreamData.size`;
- set `m_StreamData.path = ""`.

Do not require `.resS` in v1.

---

## 2.3 Asset registration

A newly created object is not useful merely because it exists in the serialized file.

Append it to:

```text
AssetBundle.m_Container
```

Use deterministic lowercase addressable-style names, for example:

```text
assets/<mod>/models/soldier
assets/<mod>/textures/soldier_body
assets/<mod>/audio/banks/<bankName>.bnk
assets/<mod>/audio/streams/<mediaId>.wem
```

`<mediaId>` is a per-build generated value — never hardcode a literal one
(observed IDs such as `1381233157` are regenerated on every run).

**FROZEN NAME FORM (blocker fix).** There is exactly ONE container-name form,
`assets/<mod>/...`, and the bake writer and the GENERATED release loader must use it
**identically**. The loader's asset-name constants are emitted by the same code path that
writes `AssetBundle.m_Container` — never typed twice.

Mandatory bake-time assertion:

> After writing `m_Container`, every asset-name constant emitted into the generated release
> loader MUST be looked up in `m_Container`; a missing entry FAILS the bake.
> A shortened form (e.g. `audio/banks/mymod.bnk`) that does not exist in `m_Container`
> returns `null` from `LoadAsset<T>` on every end user's machine.

For directly contained assets:

```text
preloadIndex = 0
preloadSize  = 0
m_FileID     = 0
```

---

## 2.4 Test hygiene

Before writing a spike/test bundle:

- change the internal `AssetBundle.m_Name`;
- change the CAB entry/name as required;
- write to a fresh output path.

This prevents an already-loaded vanilla bundle from masquerading as a successful test.

Every critical bake test must load the freshly written file again in the same run.

---

# 3. FROZEN Wwise architecture

Detailed source of truth:

```text
docs/research/pp-audio-architecture-FROZEN.md
```

Do not re-open items in this section.

## 3.1 Runtime model

Normal released-mod audio runtime is intentionally tiny.

For a loaded bank:

```csharp
AkSoundEngine.LoadBankMemoryCopy(...);
AkSoundEngine.PostEvent(...);
```

Both custom sounds and game-media replacement are covered by generated banks.

`LoadBankMemoryView` is forbidden.

Always use:

```text
LoadBankMemoryCopy
```

The input buffer may be released after a successful load.

---

## 3.2 Coverage

Proven replacement coverage:

```text
all known Phoenix Point media IDs — measured set = 7697
(7691 named in SoundbanksInfo.xml + 1 unnamed + 5 loose .wem absent from the XML)
```

The validator loads that set from `ContentTool\data\pp_wwise_index.json`; it must never
compare against a hardcoded count (see 9.3).

Replacement does not require `SetMedia`.

A replacement-only bank may be:

```text
BKHD
DIDX
DATA
```

with no HIRC.

Its DIDX declares the **existing game's media ID**.

Proven example:

```text
mediaID 18839791
original: 1200 ms FILE
replacement: 500 ms MEMORY
```

---

## 3.3 Generated bank shapes

The tool supports three production shapes.

### A. Embedded short SFX

```text
BKHD
DIDX
DATA
HIRC
...
```

Media is embedded in bank memory.

Use for short SFX.

### B. Streamed long audio

Bank contains the HIRC source definition but no embedded media bytes.

**HARD REQUIREMENT:** the HIRC Sound's `AkBankSourceData.eStreamType` MUST be `2`.
`eStreamType` is the field that sends Wwise to FILE instead of in-bank memory; both proven
`rr_streamtest` arms depend on it. With `eStreamType = 0` the engine looks for the media
inside the bank (DIDX/DATA) and the streamed `.wem` is never read.

```text
eStreamType = 0   media is IN-BANK (shape A / shape C)
eStreamType = 2   media is STREAMED from file (shape B)
```

The WEM exists as a binary asset inside `MyMod.bundle`.

At release runtime:

```text
MyMod.bundle
  -> load stream TextAsset
  -> extract bytes to Application.persistentDataPath
  -> Wwise reads the extracted <mediaId>.wem as FILE media
```

### C. Replacement-only media bank

```text
BKHD
DIDX
DATA
```

using the game's existing media ID.

---

# 4. Final release audio packaging

## 4.1 One distributed bundle

Final release layout is frozen:

```text
MyMod/
  MyMod.dll
  MyMod.bundle
```

There are no distributed loose:

```text
.bnk
.wem
.glb
.obj
.png
.wav
.ogg
```

A mod author MAY additionally ship SOURCE formats (`.glb`, `.obj`, `.png`, `.wav`, `.ogg`)
alongside the release — e.g. for redistribution or licensing. That choice never applies to
`.bnk` or `.wem`: generated bank and media payloads live **only** inside `MyMod.bundle`
(see L57 and §34 "no loose `.bnk`/`.wem` release layout").

---

## 4.2 Banks inside the bundle

Generated Wwise banks are stored in `MyMod.bundle` as binary `TextAsset`.

Frozen paths (same form as `m_Container`, §2.3):

```text
assets/<mod>/audio/banks/<bankName>.bnk
assets/<mod>/audio/banks/replacements.bnk
```

Runtime (the string below is a GENERATED constant, produced by the bake writer from the
same value it wrote into `m_Container` — see the §2.3 assertion):

```csharp
var bankAsset =
    bundle.LoadAsset<TextAsset>("assets/mymod/audio/banks/mymod.bnk");

var bytes = bankAsset.bytes;
var handle =
    GCHandle.Alloc(bytes, GCHandleType.Pinned);

try
{
    var result =
        AkSoundEngine.LoadBankMemoryCopy(
            handle.AddrOfPinnedObject(),
            (uint)bytes.Length,
            out var bankId);

    if (result != AKRESULT.AK_Success)
        throw new Exception(
            $"Wwise bank load failed: {result}");
}
finally
{
    handle.Free();
}
```

---

## 4.3 Streamed WEMs inside the bundle

Frozen paths (same form as `m_Container`, §2.3):

```text
assets/<mod>/audio/streams/<mediaId>.wem
```

They are distribution assets inside `MyMod.bundle`. The stream manifest stores this exact
container name, so the extractor never re-derives it.

At runtime they are **materialized** to the filesystem only because native Wwise streaming requires a file.

This is not a second distribution format.

---

## 4.4 Proven stream extraction path

Proven in-game by `rr_streamtest`, commit:

```text
dc5f583
```

Two measured arms passed:

```text
ARM1:
  <modDir>\WwiseAudio\
  ONE setup-time AddBasePath
  PASS

ARM2:
  Application.persistentDataPath
  NO AddBasePath call
  PASS
```

**ARM1 is the production architecture** — USER DECISION 2026-08-12, deliberately reversing the
earlier ARM2 freeze. Reasons:

- a mod must be self-contained: deleting the mod folder removes its media with it;
- the AppData root must not accumulate flat `<mediaId>.wem` from every mod built with this tool.

Production shape:

```text
<modDir>\WwiseAudio\<mediaId>.wem
+ ONE setup-time AkSoundEngine.AddBasePath("<modDir>\WwiseAudio\")  before LoadBankMemoryCopy
```

That directory is not a Wwise default, so the call is REQUIRED, not optional — and it is the
legitimate use of a valid, proven API. Base-path **shadowing** (replacing shipped media by
shadowing it on a base path) remains rejected; so does calling `AddBasePath` from more than the
one setup site. `Application.persistentDataPath` needs no call (PP registers it at init) and stays
a proven fallback shape, but is no longer what the tool emits.

---

## 4.5 Stream proof strictness

The proven test intentionally used:

- a 132-byte bank;
- no DIDX;
- no DATA;
- streamed source only;
- deletion of any old target WEM before the run;
- separate byte-for-byte extraction verification;
- separate Wwise playback verification.

This proves Wwise was reading FILE media from the extracted WEM rather than an in-memory copy.

Maintain this separation in regression tests.

---

# 5. Audio storage policy: embed vs stream

The bake system exposes two storage modes.

| Mode | Representation | Runtime cost | Typical use |
|---|---|---|---|
| `embed` | media in bank `DIDX+DATA` | PCM remains in Wwise memory until `UnloadBank` | short SFX |
| `stream` | WEM stored in bundle, extracted to PP persistent storage | normal Wwise stream buffers | music, ambience, long voice |

Approximate observed scale:

```text
3 min stereo PCM ~= 31 MB
```

Therefore long PCM must not default to embedded memory.

Suggested authoring UI:

```text
Storage:
  Auto
  Memory
  Stream
```

Internal policy names may remain:

```text
Embed
Stream
```

`Auto` may eventually choose by measured PCM byte size/duration.

Manual override must remain available.

The initial automatic threshold must be measured, not guessed.

---

# 6. Runtime stream materialization cache

The streamed-WEM cache is allowed and required.

It is **not**:

- a custom content format;
- a ContentLib cache;
- a build cache;
- a staging architecture;
- a distributed release layout.

It is only:

> generated filesystem materialization required because Wwise external streaming uses normal file I/O.

A stream manifest inside the bundle must include, per WEM:

```text
media ID
bundle asset path
byte length
content hash
cache format version
```

Runtime behavior:

1. read stream manifest;
2. determine expected WEM path;
3. if cached state proves the file unchanged, do not rewrite it;
4. if missing/stale:
   - load that stream `TextAsset`;
   - write through a temporary file;
   - close/flush;
   - atomically replace final `<mediaId>.wem`;
5. verify extracted bytes against packaged metadata;
6. let Wwise resolve the file off the mod's own base path.

Target directory is `<modDir>\WwiseAudio\` (ARM1, §4.4, user decision 2026-08-12), and exactly ONE
setup-time `AddBasePath("<modDir>\WwiseAudio\")` registers it, before any `LoadBankMemoryCopy`.
One call, one place — extraction and the registration live in the same function so no caller can
do one without the other. No path fallback chain, no shadowing.

Orphan cleanup: SKIPPED, and moot in this shape — the media lives in the mod's own folder and goes
away when the folder does.

---

# 7. Audio approaches permanently rejected

Do not research or reintroduce:

- Unity `AudioClip` playback for production audio (`m_DisableAudio = true`) — still rejected, but for
  the ZERO-RUNTIME-CODE reason, not because the engine cannot. **NARROWED 2026-08-12:** flipping that
  one byte (`globalgamemanagers` offset 7168) on the AUTHOR'S machine makes Unity decode `.ogg`/`.mp3`
  to PCM at BAKE time — measured, `research-format-coverage.md` §2.1. Bake-time decoding is allowed
  and now preferred; runtime playback is not;
- `SetMedia`;
- `UnsetMedia` management;
- long-lived pinned media buffers;
- `LoadBankMemoryView`;
- ResourceReplacer bank patching;
- ResourceReplacer base-path shadowing;
- staging orchestration;
- runtime WAV/OGG/MP3 decoding in released mods (bake-time decoding via the flag flip is fine — §2.1);
- runtime WEM generation in released mods;
- own Wwise Vorbis encoder;
- External Sources;
- Audio Input plugin;
- `StartOutputCapture`;
- custom managed Wwise file-I/O hook;
- manual `IAkFileLocationResolver` vtable construction;
- P/Invoke against mangled internal C++ symbols.

Not rejected, only **out of scope for v1** (§34 non-goal): a single `.pck` via
`LoadFilePackage`/`UnloadFilePackage`. Ground truth says only "skipped for v1" — it needs a
`.pck` writer and buys nothing over `stream`. It may be revisited later without breaking any
frozen decision.

`AddBasePath` itself is a valid Wwise API and was proven working. Since the ARM1 decision
(2026-08-12, §4.4) the production stream path REQUIRES exactly one setup-time call for the mod's
own `WwiseAudio\` directory.

What is rejected is:

- RR-style shadowing;
- more than the one setup-site call, or any use beyond registering the mod's own directory.

---

# 8. Wwise generator rules

## 8.1 Version

```text
bank version = 140
Wwise = 2021.1.0 b7575
```

## 8.2 Valid chunk ordering

Use canonical ordering where applicable:

```text
BKHD
[INIT]
[PLAT]
[STMG]
DIDX
DATA
HIRC
STID
```

Rules:

- `BKHD` first;
- DIDX size `% 12 == 0`;
- DIDX before DATA;
- no unexplained trailing bytes;
- deterministic bank IDs;
- validate all offsets/lengths;
- reject duplicate media IDs unless explicitly meaningful;
- `dwProjectID` does not need to match Phoenix Point.

### `AkBankSourceData` layout (HIRC Sound source block)

```text
ulID                u32
ulPluginID          u32
eStreamType         u8      0 = in-bank, 2 = streamed (FILE)
sourceID            u32
uInMemoryMediaSize  u32
```

- shape A (embedded SFX) and shape C (replacement media bank): `eStreamType = 0`;
- shape B (streamed long audio): `eStreamType = 2`, no DIDX/DATA entry for that media;
- `eStreamType = 2` combined with an embedded DIDX entry for the same `sourceID` is a
  generator bug — the generator must reject it.

Validation oracle:

```text
decompiled/AkSoundEngine/hirc_parse.py
```

Current corpus:

```text
53 banks
19110 HIRC objects
0 mismatches
```

---

## 8.3 HIRC minimum

Implement/retain minimum v140 serialization for:

- Sound (`0x02`);
- Action (`0x03`);
- Event (`0x04`).

Add containers only when real content needs them:

- Random/Sequence container;
- Switch container;
- ActorMixer.

NodeBaseParams, metadata, varints, optional FX/aux/state/RTPC blocks must match v140.

---

## 8.4 Routing

For a playable root sound, use a valid route.

Initial proven/safe route:

```text
OverrideBusId = 0x5C770DB7
DirectParentID = 0
```

`0x5C770DB7` confirmed offline in `Init.bnk`: HIRC object id `0x5C770DB7`, **type 8 (AuxBus)**,
115 B, name `"UI"` — and `fnv1_lower32("UI") == 0x5C770DB7`. This matters because a missing bus
is a hard bank-LOAD failure (below), so the ID had to be shown to actually exist.

Do not emit both routing fields as zero for a playable root.

Two distinct failure modes — do not confuse them:

- `OverrideBusId == 0` AND `DirectParentID == 0` → the node has no `m_pBusOutputNode`;
  the bank loads, `PostEvent` returns a valid playingID, and **nothing is audible**;
- `OverrideBusId != 0` pointing at a bus that does NOT exist → `OverrideBusId` is resolved
  FIRST during bank load and a missing bus is a **HARD BANK-LOAD FAILURE**, not silent audio.
  So an invented/typo'd bus ID fails at `LoadBankMemoryCopy`, with a non-success `AKRESULT`.

Later presets may include:

- UI;
- tactical SFX;
- ambience;
- music;
- voice;

only after verified against Phoenix Point data.

---

# 9. Wwise ID generation and collision validation

Use deterministic Wwise IDs.

## 9.1 Name→ID hash — SETTLED (offline-proven 2026-08-12)

The name→ID convention is **FNV-1 32-bit (multiply-then-XOR), name LOWERCASED (UTF-8 bytes)**:

```text
basis = 2166136261   prime = 16777619   NO masking (full 32-bit)
h = basis; for b in name.lower().encode('utf-8'): h = (h * prime) & 0xFFFFFFFF; h ^= b
```

Evidence — exact on **1078/1078** name-based pairs in the shipped
`SoundbanksInfo.xml` + the 53 `.bnk` STID chunks:

```text
Event 764/764 · SoundBank 53/53 · Switch 221 · SwitchGroup 13 · GameParameter 9
State 7 · StateGroup 5 · Trigger 2 · SetState 2 · TriggerEntry 2   = 1078
```

Falsifiable, not fitted. Rejected candidates, as re-measured by the checker:

- **FNV-1a — 0/1078** in every form (lowercased, raw, 30-bit). Do not "tidy" the
  multiply-then-XOR order; that is the whole difference.
- **non-lowercased FNV-1 — 0/1078**. Lowercase first, always.
- **30-bit-masked FNV-1 (lowercased) — partial only, never exact** (e.g. 201/764 events):
  it matches exactly the subset whose top 2 bits are already zero, i.e. it is a strict
  subset of the 32-bit answer. The unmasked 32-bit form is the correct one.

Cross-checks: 5/5 STID bank-name→bankID pairs read out of the `.bnk` binaries;
`OverrideBusId 0x5C770DB7` == `fnv1_lower32("UI")`.

Note: `SoundbanksInfo.xml` carries no `Bus`/`AuxBus` name/ID rows, so the bus arm of the
proof comes from `Init.bnk`'s HIRC (see 8.4), not from the manifest.

Reproduce: `ContentTool\tools\wwise_hash_check.py` against `ContentTool\data\pp_wwise_index.json`.

Namespace generated names, for example:

```text
<author>_<mod>_<asset>
```

## 9.2 Two ID families — computed vs allocated

This split is architectural, not a detail. A generator that hashes a media name is WRONG.

| Family | Kinds | How the ID is produced |
|---|---|---|
| **COMPUTED** | Event, SoundBank, Bus/AuxBus, Switch, SwitchGroup, State, StateGroup, GameParameter (RTPC), Trigger | `fnv1_lower32(<namespaced name>)` — deterministic, reproducible from the name alone |
| **ALLOCATED** | media / WEM IDs (incl. the streamed `<mediaId>.wem` filename) | Wwise-allocated counter/random. **NOT a name hash** |

Media IDs are **not** name-hashed — measured **0/7691** matches under every hash candidate,
and 242 distinct `ShortName`s map to MULTIPLE IDs (one source file, several conversions),
so no name→media-ID function can exist. Allocate them (counter or random) and check
membership.

## 9.3 The PP ID index — a measured SET, never a constant

The validator must LOAD the dumped index; it must never compare against a hardcoded count.

```text
ContentTool\data\pp_wwise_index.json
```

Measured contents:

```text
7697 known media IDs = 7692 distinct File ids in SoundbanksInfo.xml (of which 1 carries no
                       ShortName) + 5 loose .wem on disk that are absent from the manifest
764 events · 53 banks · 221 switches · 13 switch groups · 9 RTPCs · 7 states · 5 state groups · 2 triggers
53 banks / 19110 HIRC objects (bank-structure corpus)
```

**Read the right key.** The JSON holds two media arrays:

- `_media_ids` — 7691 manifest ids that carry a `ShortName` (what `--dump` emits);
- `_media_ids_all` — **7697**, the COMPLETE occupied set. **This is the one the validator
  must test membership against.** Using `_media_ids` silently leaves 6 real IDs unguarded,
  and a colliding streamed `<mediaId>.wem` shadows a game sound with no error at all.

Earlier drafts of this plan said "7696 media IDs" — imprecise; the measured set is 7697 and
the loose-`.wem` tail is exactly why a constant is the wrong shape.

## 9.4 Collision matrix — per family

COMPUTED IDs — compare the hash against the PP index:

| Case | Result |
|---|---|
| generated ID vs own generated ID | ERROR always |
| generated ID vs indexed Phoenix Point ID | ERROR |

ALLOCATED media IDs — check membership in BOTH the PP media-ID set AND the project's own set:

| Case | Result |
|---|---|
| allocated media ID vs own allocated media ID | ERROR always |
| allocated media ID vs indexed Phoenix Point media ID | ERROR (re-allocate) |
| explicitly declared replacement media ID vs PP media ID | EXPECTED |
| explicit replacement media ID duplicated by another project asset | ERROR |

Replacement must be explicit, for example:

```json
{
  "replaces": 18839791
}
```

`"replaces"` suppresses ONLY the PP-side hit.

It does not suppress a duplicate inside the project.

This validation is mandatory, especially for streamed WEMs whose physical filename is:

```text
<mediaId>.wem
```

---

# 10. Material architecture — frozen

## 10.1 Production shader path

> **Amended 2026-08-12 (see §39.4):** this section's objection was patch fragility, a cost the user
> has since retired, and gate **U3d** measured a forged external `m_Shader` resolving in-game. In the
> **baked** path the shader may be an external PPtr written at bake time; §10.1/§10.2 remain the rule
> for anything materialized live at runtime.

Production materials do **not** use a durable external AssetBundle PPtr to a Phoenix Point shader.

External PPtr depends on private details of foreign serialized files and is patch-fragile.

`Shader.Find(...)` is also not considered a reliable primary resolver because Addressables content may not yet have loaded the shader.

Production rule:

> Shader assignment uses a Phoenix Point **Def donor**.

Material recipe example:

```json
{
  "shader": {
    "mode": "donor",
    "donor": "<stable Phoenix Point Def identity>"
  },
  "textures": {
    "_MainTex": "../Textures/soldier.png",
    "_BumpMap": "../Textures/soldier_n.png"
  }
}
```

The donor identity is a stable PP Def identity, such as the appropriate actor/view-element Def.

Do not identify donors by:

- AssetBundle filename;
- Addressables hash;
- Unity asset/material name.

---

## 10.2 Runtime donor resolution

Runtime flow:

```text
material is needed
  ->
resolve donor PP Def
  ->
obtain donor's live material / shader
  ->
generatedMaterial.shader = donorShader
```

If the required donor is not yet resident:

- resolve lazily on first actual use;
- no background retry timer;
- no polling loop;
- no permanently half-resolved material state.

---

## 10.3 U3 acceptance

```text
U3a generated Material survives bundle load
U3b runtime donor shader assignment works
U3c properties/textures survive shader reassignment
```

U3d (external shader PPtr) is **removed from the implementation sequence entirely** (D1).
It is not a gate, not a task, not a blocker — parked as research only:

```text
research/optional/external-shader-ptr
```

---

# 11. Source content

## 11.1 Models

Support:

- `.glb` primary;
- `.obj` for simple static meshes.

Prefer GLB for:

- hierarchy;
- skinned meshes;
- skeletons;
- materials;
- morph targets;
- animations.

---

## 11.2 Textures

Support:

- `.png`;
- `.jpg`;
- `.jpeg`.

Do not add DDS/TGA until a real mod needs them.

---

## 11.3 Animations

Support:

- animations embedded in model GLB;
- separate animation-only GLB.

Preserve:

- timestamps;
- translation;
- rotation;
- scale;
- interpolation modes that can be represented correctly.

No automatic humanoid retargeting in v1.

No guessed bone mapping.

---

## 11.4 Audio

Developer-friendly inputs:

- `.wav`;
- `.ogg`;
- `.mp3` only if the existing decoder proves reliable.

Production output is always Wwise-compatible PCM WEM + generated v140 bank structures.

No runtime source-codec decoding in released mods.

---

# 12. ResourceReplacer harvesting

ResourceReplacer is a donor repository, not a runtime dependency.

Port only proven/relevant code.

## 12.1 Port into Content Tool internals

- OBJ parser and tests;
- GLB parser/data model and tests;
- skeleton/bone/morph helpers;
- texture import helpers;
- audio input decoding;
- `WwiseWem.cs`;
- `BankGen.cs`;
- Wwise index/extraction code needed by developer UI;
- `WwiseSetupHeaders.cs` only where required by developer inspection;
- proven tests.

## 12.2 Do not port

- old replacement orchestration;
- `PatchBank`;
- `Restage`;
- `StageOne`;
- RR base-path shadowing;
- old pack priority system;
- old staging/cache design;
- runtime codec path;
- general dump infrastructure;
- scene scanning except narrowly required developer diagnostics.

**Re-scoped by §39.8 (2026-08-12):** the scan ban, and the ban implied on assigning
`renderer.sharedMesh` / `sharedMaterials`, mean **banned as a shipping mechanism**. Both are
permitted inside developer mode — see §39.3/§39.8 for the exact boundary and for the items that
stay permanently dead (`SetMedia`, `PatchBank`, `Restage`, `StageOne`, base-path shadowing, pack
priority, old staging/cache, runtime codec, general dump infrastructure).

Record non-trivial ports in:

```text
docs/RR_PORTING_LOG.md
```

---

# 13. Project model

## 13.0 Content Tool mod identity — FROZEN

```text
mod folder              ContentTool
root namespace          Morgott.ContentTool
assembly / output DLL   ContentTool.dll
console command prefix  ct_*
repo path               E:\DEV\PhoenixPoint\ContentTool\
```

- ILRepack output name, the embedded-resource namespace for `classdata.tpk`, and the
  "no sibling AssetsTools.NET.dll" check all key off `ContentTool.dll`.
- `rr_*` commands (`rr_bundle1`, `rr_bake`, `rr_streamtest`, `rr_testA..H`) belong to the DONOR
  harness `ResourceReplacer` and are NOT part of this mod. The Content Tool's own commands use
  `ct_*`.

Suggested project structure:

```text
MyMod/
  MyMod.csproj
  ppcontent.json

  src/

  Content/
    Models/
    Animations/
    Textures/
    Materials/
    Audio/

  Build/
  Dist/
```

`Content/` is developer source.

`Build/` contains generated developer artifacts.

`Dist/` contains release artifacts.

---

## 13.1 `ppcontent.json`

Keep tool-specific metadata minimal.

Example:

```json
{
  "id": "ubermorgott.example",
  "bundle": "Example.bundle"
}
```

If Phoenix Point's own mod metadata cannot reliably provide the already-built DLL path, add:

```json
{
  "dll": "bin/Release/MyMod.dll"
}
```

Do not introduce a second `mod.json`.

Do not duplicate display name, author, version, or other metadata already available from PP's mod system.

---

# 14. Live authoring path

## 14.1 Live Texture

```text
PNG/JPEG
  ->
Texture2D
  ->
LoadImage
```

## 14.2 Live Mesh

```text
ImportedMesh
  ->
new Mesh()
  ->
vertices
normals
tangents
UVs
indices
submeshes
bounds
```

## 14.3 Live Material

```text
ImportedMaterial
  ->
new Material(...)
  ->
textures / scalar / vector properties
  ->
donor shader resolver
```

## 14.4 Live model

Build a runtime hierarchy from Imported Content IR.

Support:

- static;
- skinned;
- bones;
- bind poses;
- morph targets.

## 14.5 Live audio

Build/refresh generated Wwise content and load it through the proven bank path.

No Unity serialization is involved.

---

# 15. Bake path

Bake consumes Imported Content IR and produces one native bundle.

```text
Imported Content IR
      |
      +--> Unity serialized assets
      |
      +--> Wwise .bnk binary TextAsset
      |
      +--> streamed .wem binary TextAssets
      |
      +--> stream manifest / generated constants metadata
      |
      v
MyMod.bundle
```

AssetsTools.NET exists only in this bake path and only inside ContentTool.dll.

It does not ship with finished mods.

---

# 16. Remaining Unity gates

The remaining real implementation frontier is:

```text
U3 Material
U4 GameObject hierarchy
U6 AnimationClip + Mecanim
U5 SkinnedMeshRenderer
```

U6 intentionally moves before full U5 after a minimal U4 exists.

Reason:

> `AnimationClip` serialization/Mecanim compatibility remains the highest-risk Unity serialization item.

It is better to discover a blocker before spending time finishing every complex skinned-model detail.

---

# 17. U4 — minimal GameObject hierarchy

Implement the smallest correct native hierarchy first.

Required:

- GameObject;
- Transform;
- parent/child hierarchy;
- MeshFilter;
- MeshRenderer;
- internal PPtrs;
- deterministic asset names.

Acceptance:

- baked prefab/root loads;
- transform hierarchy is correct;
- renderer references the intended baked mesh/material;
- no false success from external PPtrs.

---

# 18. U6 — AnimationClip + Mecanim

Attack U6 immediately after a minimal U4.

## 18.1 Minimal risk-closing spike

Use a minimal artificial hierarchy:

```text
Root
  Bone
```

Bake one native `AnimationClip`.

Example curve:

```text
Bone.localPosition.x:
  0.0 -> 1.0 -> 0.0
```

Acceptance requires:

1. `LoadAsset<AnimationClip>()` succeeds;
2. clip metadata is sane;
3. Mecanim accepts the clip;
4. actual Transform motion occurs;
5. replay/loop behavior is understood;
6. `AnimatorOverrideController` compatibility is proven if required by PP integration.

Only after this minimal proof, expand to imported GLB animation data.

---

## 18.2 Full animation pipeline

Parse GLB channels:

- translation;
- rotation;
- scale;
- timestamps;
- supported interpolation.

Skeleton mapping:

1. exact normalized transform path;
2. exact unique bone name only as explicit fallback;
3. ambiguous mapping = ERROR;
4. missing required bone = ERROR.

No automatic humanoid retargeting.

No guessed mappings.

---

# 19. U5 — SkinnedMeshRenderer

After U6 risk closure, finish full skinned-model bake.

Required:

- GameObject/Transform hierarchy;
- `SkinnedMeshRenderer`;
- bones array;
- root bone;
- bone weights;
- bind poses;
- bounds;
- materials;
- submeshes;
- morph/blendshape data;
- correct internal references.

Acceptance:

- baked model loads;
- live and baked topology match;
- correct sharedMesh;
- correct bone count;
- bind poses match;
- morphs exist and deform;
- known imported animation plays on the baked model.

---

# 20. Wwise game index

Build/retain a developer-only searchable index from:

- `SoundbanksInfo.xml`;
- all shipped `.bnk`;
- media IDs;
- event IDs;
- bank IDs;
- Sound/Action/Event chains;
- bus IDs/names where recoverable.

UI search:

- event name;
- event ID;
- media ID;
- bank name;
- source short name.

Selected chain view:

```text
Event
  -> Action
  -> Sound
  -> mediaID
  -> codec
  -> stream type
  -> bank
```

Provide:

```text
Replace this media
```

The index/parser never ships in a released mod.

---

# 21. Developer UI

The Content Tool runs in-game and uses Phoenix Point itself as the authoritative preview environment.

Suggested panel:

```text
Project: ubermorgott.example

Assets
  Models
  Animations
  Textures
  Materials
  Audio

Selected: soldier.glb

Meshes: 3
Bones: 64
Materials: 4
Animations: 12
Morphs: 7

[Preview]
[Validate]
[Reload Changed]
[Bake Development]
[Bake Release]
```

Show errors inline.

Do not build a second standalone renderer.

---

# 22. Preview

> **NON-FROZEN IDEAS.** The preview control sets below (orbit/zoom/lighting/skeleton overlay,
> play/pause/scrub/speed/loop) are illustrative UX wishes, not requirements. Nothing here is
> proven or mandated; implement the minimum a real workflow needs.

## 22.1 Model preview

Support:

- static model;
- skinned model;
- materials;
- morph targets;
- animations.

Developer controls:

- orbit camera;
- zoom;
- optional lighting controls;
- skeleton overlay;
- bounds display.

## 22.2 Animation preview

Support:

- clip selection;
- play;
- pause;
- scrub;
- speed;
- loop;
- skeleton diagnostics.

## 22.3 Audio preview

Custom event:

```text
generate/load dev bank
-> PostEvent
-> stop/replay
```

Replacement:

```text
generate replacement bank
-> load replacement bank
-> trigger actual game Event where practical
```

Always display relevant IDs and objective diagnostics.

---

# 23. File watching and incremental development

Use `FileSystemWatcher` only in developer mode.

Debounce:

- duplicate events;
- temp-file save patterns;
- rename/write sequences.

Maintain dependency graph such as:

```text
texture -> material
material -> model
model -> skeleton
animation -> skeleton/model
audio source -> WEM/bank
```

Frozen wording:

> Incremental build means incremental import, conversion, validation and dependency processing.  
> The final development AssetBundle may be fully rewritten.  
> Do not split one content mod into per-asset AssetBundles solely to optimize incremental writes.

Track only enough metadata to skip useless work:

- relative path;
- file size;
- mtime;
- tool format version;
- content hash where needed.

Do not build a new generic persistent asset cache.

---

# 24. Unity hot reload

> **NON-FROZEN IDEAS.** Versioned dev bundle filenames and the reflection-based
> `DevReloadHook.cs` below are design sketches, not proven or mandated. No in-game measurement
> covers them. An implementing agent may replace or drop them.

For developer baked bundles:

Do not overwrite a currently loaded bundle in place.

Use versioned files:

```text
MyMod.dev.0001.bundle
MyMod.dev.0002.bundle
```

Process:

1. bake new version;
2. load new bundle;
3. swap tool-owned preview/dev references;
4. destroy old preview instances;
5. unload old bundle when safe;
6. clean old dev files.

Real released mods do not depend on Content Tool hot reload.

An optional generated `DevReloadHook.cs` may use reflection to communicate with the tool if present.

If the tool is absent:

- no error;
- no dependency failure;
- mod continues normally.

---

# 25. Wwise hot reload

Use proven sequence:

```text
stop relevant preview event if needed
UnloadBank
rebuild
LoadBankMemoryCopy
PostEvent
```

The same bank ID may be reused after a successful unload.

Add regression tests for repeated unload/load cycles.

Do not use `SetMedia` for normal reload.

---

# 26. Generated mod-side release helpers

Released mods must contain only tiny engine-facing helpers.

**Required reference:** a released mod's `.csproj` MUST reference
`UnityEngine.AssetBundleModule` (ships in the game's `Managed\`) — `AssetBundle.LoadFromFile`
is unavailable without it. The generated helper sources assume this reference exists.

**Required ordering:** stream extraction (§26.3) MUST COMPLETE before the bank is loaded with
`LoadBankMemoryCopy`, and therefore before the first `PostEvent`. A streamed
(`eStreamType = 2`) source resolves to a FILE at load/post time; if the `.wem` is not yet on
disk the media is simply unreachable.

```text
LoadFromFile(MyMod.bundle)
  -> extract all streamed WEMs to <modDir>\WwiseAudio\  (byte-verified)
  -> AddBasePath("<modDir>\WwiseAudio\")                (once, same step)
  -> LoadBankMemoryCopy(bank bytes)
  -> PostEvent
```

Allowed examples:

## 26.1 Bundle loader

```csharp
internal static class ModAssets
{
    internal static AssetBundle Bundle;

    internal static void Load(string modDirectory)
    {
        Bundle = AssetBundle.LoadFromFile(
            Path.Combine(
                modDirectory,
                "MyMod.bundle"));
    }
}
```

## 26.2 Wwise bank loader

Load bank bytes from the bundle and call `LoadBankMemoryCopy`.

## 26.3 Stream extractor

Only if the project has streamed media.

Responsibilities:

- read stream manifest;
- compare cache state;
- extract stale/missing WEM;
- byte-verify extraction;
- no `AddBasePath`;
- expose no general-purpose audio framework.

## 26.4 Generated constants

May include:

- asset names;
- Event IDs;
- Bank IDs;
- replacement media IDs;
- diagnostic constants.

Do not generate:

- codec code;
- BNK parser;
- Unity serializer;
- generic replacement engine;
- general-purpose Wwise path manager;
- ContentLib wrapper.

---

# 27. Bake Release

`Bake Release` does not compile C#.

Frozen sequence:

1. import/validate source content;
2. build final native AssetBundle;
3. validate Unity/Wwise output;
4. locate already-built `MyMod.dll`;
5. copy DLL + bundle into `Dist/`.

Normal result:

```text
Dist/
  MyMod.dll
  MyMod.bundle
```

Do not auto-search arbitrary directories for the mod DLL.

Use PP metadata when reliable, otherwise the explicit `dll` field in `ppcontent.json`.

---

# 28. Release validation

Fail Bake Release on:

- duplicate asset names;
- malformed AssetBundle;
- missing baked object;
- broken internal PPtr;
- invalid skeleton binding;
- ambiguous bone mapping;
- missing material texture;
- unresolved donor shader definition;
- malformed Wwise bank;
- invalid Wwise routing;
- duplicate generated Wwise ID;
- collision with Phoenix Point ID;
- unresolved explicit replacement ID;
- duplicate replacement media ID;
- missing stream manifest entry;
- duplicate streamed media ID;
- missing packaged streamed WEM;
- stream hash/length mismatch.

Release validation must load its own newly built bundle.

For streams it must additionally:

1. materialize a test WEM;
2. verify bytes independently;
3. verify Wwise playback independently.

Do not accept one check as proof of the other.

---

# 29. Methodology rules — mandatory

These rules exist because violating them already produced false conclusions.

## 29.1 Audio

A non-zero `playingID` does not prove audible playback.

A zero ID usually means the Event did not exist, not that the API itself failed.

Never judge an audio experiment by ear alone.

Use objective instrumentation.

For every critical test log:

- function result;
- event ID;
- media ID;
- bank ID;
- duration;
- FILE vs MEMORY when observable;
- streaming state where observable;
- EndOfEvent timing;
- bank load result;
- bank unload result.

Do not block Unity's main thread with `Thread.Sleep`.

When timing needs to advance in a spike, pump exactly this pair each iteration instead:

```csharp
AkSoundEngine.RenderAudio();
AkCallbackManager.PostCallbacks();
```

(`Thread.Sleep` stalls the frame loop ⇒ `AkSoundEngineController.LateUpdate` never runs,
`PostCallbacks` never dispatches, and queued `PostEvent`s flush together on wake.)

A main-menu spike is meaningless if the required bank/content is not resident.

Always log bank-load state.

---

## 29.2 Control in the same run

Every critical experimental conclusion requires a control measurement in the same run whenever feasible.

Never compare only against an expectation from another process/session.

---

## 29.3 External Mesh PPtr trap

Before using a game bundle as a Mesh serialization/repack test subject, verify:

```text
m_Mesh.fileID == 0
```

A previous false failure came from a prefab whose Mesh used:

```text
fileID = 1
```

into an unloaded external bundle.

Material references in the same subject were local, which is why they appeared to work.

---

## 29.4 Asset enumeration trap

These APIs expose top-level assets only:

```text
AssetBundle.GetAllAssetNames
AssetBundle.LoadAllAssets<T>
```

They do not prove the absence of nested dependencies.

Do not build a "Mesh objects in bundle" conclusion from them.

For prefab/model tests, walk the loaded graph and inspect each renderer.

The trustworthy signal is the actual:

```text
renderer.sharedMesh
```

not a top-level object count.

---

## 29.5 Game textures

Shipped game textures may not be CPU-readable.

Use:

```text
RenderTexture
Graphics.Blit
ReadPixels
```

for measurement.

Authored test textures may be read directly with `GetPixel`.

---

# 30. Tests

## 30.1 Imported content

OBJ:

- vertices;
- normals;
- UV;
- triangles;
- malformed input.

GLB:

- static mesh;
- hierarchy;
- primitives;
- skin;
- bone weights;
- bind poses;
- morphs;
- embedded animation;
- animation-only skeleton mapping.

---

## 30.2 Bake writer

Regression tests for already-proven foundation:

- U0a SerializedFile/UnityFS load;
- U0b exact binary TextAsset;
- U1 authored Texture2D;
- U2 Mesh.

New tests:

- Material;
- GameObject/Transform;
- internal PPtrs;
- AnimationClip;
- SkinnedMeshRenderer;
- morph data.

---

## 30.3 WEM

- PCM header;
- mono;
- stereo;
- real sample rates;
- duration;
- deterministic output.

## 30.4 BNK

- BKHD v140;
- deterministic bank ID;
- DIDX entry layout;
- DATA offsets;
- HIRC Sound;
- HIRC Action;
- HIRC Event;
- routing;
- `AkBankSourceData.eStreamType` assertion:
  embedded/replacement bank -> `eStreamType == 0`;
  streamed bank -> `eStreamType == 2` AND no DIDX entry for that `sourceID`;
- embedded bank;
- stream-source bank;
- replacement-only bank;
- collision detection;
- unload/reload cycle.

## 30.5 Stream packaging

- stream manifest read/write;
- deterministic content hash;
- missing cache file -> extract;
- unchanged cache -> no rewrite;
- changed WEM -> rewrite only changed media;
- atomic temp -> final file;
- byte verification;
- cache version invalidation;
- Wwise FILE playback without mod-side `AddBasePath`.

---

# 31. Sample mods

## 31.1 SampleWeaponMod

Include:

- weapon GLB;
- texture;
- material;
- short custom shot SFX;
- replacement of one existing PP sound.

Output:

```text
SampleWeaponMod.dll
SampleWeaponMod.bundle
```

## 31.2 SampleCharacterMod

Include:

- skinned character;
- skeleton;
- materials;
- idle animation;
- attack animation;
- morph target;
- long streamed voice/ambience sample.

Verify:

- first-run stream extraction;
- byte equality;
- Wwise FILE playback;
- second-run cache reuse;
- no loose release media.

---

# 32. Final implementation order

## Task 1 — Productionize the proven foundation

This is the first actual implementation task.

Required:

- bootstrap/clean production project structure;
- vendor AssetsTools.NET at pinned commit;
- retarget required code to `net472`;
- ILRepack AssetsTools.NET into ContentTool DLL;
- embed `classdata.tpk`;
- remove all absolute scratchpad paths;
- wrap proven new-object creation path;
- preserve U0b/U1 regression tests;
- preserve bundle internal rename hygiene;
- port/retain `WwiseWem.cs`;
- port/retain `BankGen.cs`;
- implement/embed bank binary asset packaging;
- implement streamed WEM binary asset packaging;
- implement stream manifest;
- implement extraction to `Application.persistentDataPath`;
- verify extracted bytes separately;
- verify Wwise streamed playback separately;
- implement `embed` / `stream` flags;
- implement Wwise ID collision validation;
- leave no sibling AssetsTools.NET dependency.

Task 1 acceptance requires all already-proven primitives to still pass from the productionized codebase.

---

## Task 1b — Imported Content IR and project discovery

**MOVED FORWARD (was Task 9).** The shared IR is the central architectural rule (§0.3: one IR,
two backends). It must exist BEFORE U3, or Tasks 2-6 each grow their own ad-hoc import shape and
the live/bake diagnostic seam is lost. May be folded into Task 1.

- IR types: `ImportedMesh`, `ImportedMaterial`, `ImportedTexture`, `ImportedSkeleton`,
  `ImportedAnimation`, `ImportedAudio` (extend as §0.3);
- source discovery;
- project metadata (`ppcontent.json`);
- source-to-generated mapping.

Everything after this task consumes the IR; no second import implementation is permitted.

---

## Task 2 — U3 Material

- serialized Material;
- texture refs;
- scalar/vector properties as required;
- donor Def recipe;
- runtime donor resolver;
- shader assignment;
- baked load validation.

(No external-PPtr sub-task — U3d is out of the sequence entirely, D1; see
`research/optional/external-shader-ptr`.)

---

## Task 3 — U4 minimal GameObject hierarchy

- GameObject;
- Transform;
- hierarchy;
- MeshFilter;
- MeshRenderer;
- internal references.

Keep this minimal.

Its purpose is to unblock U6 early.

---

## Task 4 — U6 minimal AnimationClip/Mecanim proof

- artificial root/bone;
- one simple transform curve;
- native AnimationClip;
- load;
- Mecanim playback;
- objective Transform movement;
- override-controller check where required.

Do not implement the complete animation importer before this gate passes.

---

## Task 5 — Full animation serialization

- imported GLB channels;
- curve conversion;
- path normalization;
- interpolation;
- clip metadata;
- deterministic names.

---

## Task 6 — U5 full SkinnedMeshRenderer

- hierarchy;
- bones;
- bind poses;
- weights;
- morph targets;
- materials;
- bounds;
- full baked character test.

---

## Task 7 — Material recipe system

Generalize the proven U3 path into author-facing recipes.

---

## Task 8 — Wwise game index

- `SoundbanksInfo.xml`;
- BNK parse;
- event/media lookup;
- generated JSON index;
- replacement selection UI.

---

## Task 9 — (moved)

Project discovery + Imported Content IR moved to **Task 1b**, immediately after Task 1 and
before U3. Nothing remains here.

---

## Task 10 — In-game developer UI

Only after the major native serialization gates are closed.

---

## Task 11 — Live model/material preview

Use runtime objects directly.

Do not bake merely to preview.

---

## Task 12 — Animation preview

---

## Task 13 — Audio browser/preview

---

## Task 14 — File watcher + incremental dependency graph

---

## Task 15 — Unity hot reload

---

## Task 16 — Wwise hot reload

---

## Task 17 — Generated release helper sources

---

## Task 18 — Bake Release

---

## Task 19 — SampleWeaponMod

---

## Task 20 — SampleCharacterMod

---

## Task 21 — Documentation / release

---

# 33. Recommended commit sequence

```text
chore: bootstrap in-game Phoenix Point content tool
feat: vendor AssetsTools.NET and embed classdata.tpk
feat: merge AssetsTools.NET into content tool assembly
test: preserve binary TextAsset and authored Texture2D bake regressions
feat: productionize native bundle bake primitives
feat: package generated Wwise banks inside AssetBundle
feat: package and materialize streamed WEM payloads
feat: add embed and stream audio bake modes
feat: add Phoenix Point Wwise ID collision validation
feat: add shared imported content IR and project discovery
feat: add donor-based material shader resolution
feat: add native Material serialization
feat: add minimal GameObject hierarchy serialization
spike: prove native AnimationClip playback through Mecanim
feat: add GLB animation serialization
feat: add SkinnedMeshRenderer serialization
feat: add morph target serialization
feat: add material recipe system
feat: add Phoenix Point Wwise event and media index
feat: add in-game content tool UI
feat: add live model and material preview
feat: add animation preview
feat: add Wwise browser and audio preview
feat: add incremental source dependency tracking
feat: add Unity and Wwise hot reload
feat: add generated release helper sources
feat: add bake release pipeline
docs: add sample weapon and character mods
```

---

# 34. Explicit non-goals for v1

- no mandatory shared runtime library;
- no ContentLib public runtime API;
- no custom `.clib` format;
- no external production build application;
- no Unity Editor dependency;
- no Wwise Authoring dependency;
- no WAAPI dependency;
- no custom Wwise Vorbis encoder;
- no runtime WAV/OGG/MP3 decoding in released mods;
- no runtime PCM-WEM generation in released mods;
- no runtime BNK generation in released mods;
- no loose `.bnk`/`.wem` release layout;
- no custom Wwise filesystem plugin;
- no custom `IAkFileLocationResolver`;
- no `.pck` writer;
- no shader compilation;
- no FBX importer;
- no automatic humanoid retargeting;
- no generic package/dependency manager;
- no generic public `Load<T>` framework;
- no game-wide automatic replacement of arbitrary Unity references;
- no per-asset AssetBundle explosion for incremental writes;
- no attempt to preserve ResourceReplacer's old architecture;
- no attempt to optimize long embedded replacement PCM until a real use case justifies it;
- no bake-time rewriting of Phoenix Point's own shipped serialized assets (§39.1).

"No game-wide automatic replacement of arbitrary Unity references" above means exactly that:
*game-wide* and *automatic*. A shipped mod applying a finite, explicitly authored replacement
table off a load-time seam is not that, and is the §39 shipping mode.

---

# 35. Agent implementation rules

The implementing agent must obey these rules.

- Read actual ResourceReplacer code before porting a component.
- Do not copy illustrative signatures from old planning documents.
- Preserve proven tests before refactoring proven logic.
- Do not re-open U0a/U0b/U1/U2 as architecture questions.
- Do not re-open the frozen Wwise architecture.
- Keep live materialization and bake serialization separate.
- Keep one shared Imported Content IR.
- Do not make AssetsTools.NET a runtime dependency of released mods.
- Do not ship AssetsTools.NET as a sibling DLL.
- Do not read `classdata.tpk` from an absolute path.
- Do not add external shader PPtr as a production dependency.
- Do not use `Shader.Find` as the primary PP shader resolver.
- Do not add background shader retry loops.
- Do not reintroduce `SetMedia`.
- Do not reintroduce bank patching.
- Do not reintroduce ResourceReplacer base-path shadowing.
- Do call `AddBasePath` exactly ONCE, at setup, for `<modDir>\WwiseAudio\` — the production stream
  path since the ARM1 decision (2026-08-12, §4.4). Never a second site, never for shadowing.
- Do not build custom Wwise I/O.
- Do not build `.pck`.
- Do not infer success from audible-only tests.
- Do not infer Mesh presence from `LoadAllAssets`.
- Do not use a test prefab with an external `m_Mesh` PPtr as proof of local Mesh serialization.
- Do not block Unity main thread with `Thread.Sleep` in timing tests.
- Every critical spike uses an objective control where possible.
- Every task ends with:
  - build passing;
  - relevant offline tests passing;
  - relevant in-game tests passing;
  - focused commit.

---

# 36. Final runtime model

```text
                         DEVELOPMENT

 GLB / OBJ / PNG / WAV / OGG / JSON
                 |
                 v
           ContentTool.dll
          (runs inside PP)
                 |
                 v
         Imported Content IR
            /           \
           /             \
          v               v
 LIVE MATERIALIZER     BAKE SERIALIZER
 Unity/Wwise runtime   AssetsTools.NET
 objects directly      + Wwise BankGen
          |               |
          |               v
          |          MyMod.bundle
          |      (Unity assets + banks
          |       + streamed WEMs)
          |
          +--> preview / validate / hot reload


============================================================


                            PLAYER

 MyMod.dll
    |
    +--> AssetBundle.LoadFromFile("MyMod.bundle")
    |       |
    |       +--> GameObject
    |       +--> Mesh
    |       +--> Texture2D
    |       +--> Material
    |       +--> AnimationClip
    |
    +--> load bank TextAsset bytes
    |       |
    |       +--> LoadBankMemoryCopy
    |       +--> custom Events
    |       +--> embedded short media
    |       +--> replacement media
    |
    +--> if streamed media exists
            |
            +--> extract packaged WEM
            |    to Application.persistentDataPath
            |
            +--> Phoenix Point has already
                 registered that Wwise base path
            |
            +--> Wwise streams FILE media normally


 NO ContentLib runtime
 NO ResourceReplacer runtime
 NO AssetsTools.NET in released mod
 NO runtime codecs
 NO runtime parsers
 NO custom Wwise I/O
 NO loose release media
 NO Unity Editor
 NO Wwise Authoring project
```

---

# 37. v1 acceptance checklist

## Architecture

- [ ] Content Tool runs as a Phoenix Point mod.
- [ ] Live preview does not require Unity serialization.
- [ ] Bake uses the same Imported Content IR as live materialization.
- [ ] Released mods have no Content Tool dependency.
- [ ] Released mods have no ResourceReplacer dependency.
- [ ] Released mods have no AssetsTools.NET dependency.
- [ ] Normal release consists of `MyMod.dll + MyMod.bundle`.

## Bake foundation

- [ ] AssetsTools.NET pinned/vendored from the proven source revision.
- [ ] AssetsTools.NET ILRepack-merged into ContentTool DLL.
- [ ] `classdata.tpk` embedded in ContentTool DLL.
- [ ] No hardcoded developer filesystem paths.
- [ ] U0a regression passes.
- [ ] U0b exact binary TextAsset regression passes.
- [ ] U1 authored Texture2D regression passes.
- [ ] U2 Mesh regression passes.

## Unity

- [ ] U3 Material loads from baked bundle.
- [ ] Donor Def shader resolution works.
- [ ] Material textures/properties survive shader assignment.
- [ ] U4 GameObject hierarchy loads correctly.
- [ ] U6 native AnimationClip loads.
- [ ] U6 clip drives Mecanim/Transform correctly.
- [ ] Full imported animation works.
- [ ] U5 skinned model loads.
- [ ] Bone weights/bind poses work.
- [ ] Morph targets work.

## Wwise

- [ ] Tool generates v140 banks.
- [ ] Embedded short custom SFX works.
- [ ] Custom Event playback works.
- [ ] Replacement of streamed PP media works.
- [ ] Replacement of embedded PP media works.
- [ ] Replacement uses no `SetMedia`.
- [ ] Replacement uses no bank patching.
- [ ] Bank unload/reload works repeatedly.
- [ ] Wwise ID collision matrix is enforced.

## Streamed audio

- [ ] Streamed WEM is packaged inside `MyMod.bundle`.
- [ ] Runtime extracts it to `<modDir>\WwiseAudio\`.
- [ ] Extraction bytes are verified independently.
- [ ] Wwise plays it as FILE/streaming media.
- [ ] Production path calls `AddBasePath` exactly once, for the mod's own directory, and never shadows.
- [ ] First run writes required streams.
- [ ] Second run performs zero unnecessary rewrite.
- [ ] Updating one stream rewrites only that stream.
- [ ] No streamed `.wem` is distributed loose.

## Developer workflow

- [ ] In-game asset browser works.
- [ ] Live model preview works.
- [ ] Animation preview works.
- [ ] Audio preview works.
- [ ] File watching works.
- [ ] Incremental dependency processing works.
- [ ] Unity preview hot reload works.
- [ ] Wwise hot reload works.

## Release

- [ ] Bake Release does not compile C#.
- [ ] Already-built mod DLL is located deterministically.
- [ ] Release validation loads its own bundle.
- [ ] Release contains no loose source content by default.
- [ ] Release contains no serializer/importer/parser/codec dependencies.
- [ ] SampleWeaponMod runs from release files only.
- [ ] SampleCharacterMod runs from release files only.

---

# 38. Architecture freeze

Do not re-open the following without a newly proven engine limitation:

- external-vs-in-game tool architecture;
- shared ContentLib runtime;
- `.clib`;
- Unity Editor requirement;
- Wwise Authoring requirement;
- loose release banks/WEMs;
- `SetMedia`;
- ResourceReplacer bank patching;
- base-path shadowing;
- custom Wwise I/O;
- external shader PPtr as the production material path;
- re-proving U0a/U0b/U1/U2;
- rebuilding Wwise research already frozen.

The remaining work is no longer broad architecture research.

It is a concrete implementation sequence focused primarily on:

```text
U3 Material
U4 GameObject hierarchy
U6 AnimationClip + Mecanim
U5 SkinnedMeshRenderer
```

The highest remaining technical risk is native Unity 2019.4 `AnimationClip` serialization and Mecanim compatibility.

The central product is:

> **An in-game Phoenix Point content authoring and baking tool that turns developer-friendly source assets into native Phoenix Point Unity/Wwise content while leaving finished mods with only their own DLL and one native bundle.**

---

# 39. Two-mode replacement — asset identity, dev apply, shipping bake

**Status:** architecture amendment, 2026-08-12. Does not re-open §0-§38; it fills the hole those
sections leave: the plan can ADD content and can REPLACE audio, but has no way to NAME a shipped
Unity asset, so texture/mesh/material/animation replacement has no target.

**Requirement (settled, not a design option):** one tool, two modes over one codebase.

```text
DEVELOPER MODE   live swap in the running game, iterate, revert. Runtime cost accepted.
SHIPPING MODE    the same decisions applied at load in the player's game. No scan, no per-frame work.
ADD               unchanged, already covered by §14/§15.
```

---

## 39.1 The one honest constraint everything else follows from

> `MyMod.bundle` cannot rewrite a shipped Phoenix Point serialized object.

Our bundle is a second bundle. It can hold new native assets (proven: U0a/U0b/U1/U2, foundations
#4/#8/#8a) but it cannot reach into `dlc5_assets_all.bundle` and change the prefab the game loads.

Therefore "baked native replacement" is only literally true for audio, where Wwise resolves media
by ID at bank-load time and a shape-C bank wins by declaring the game's media ID (§3.2,
foundations #12/#13) — no object is edited, the lookup simply resolves elsewhere.

For every visual kind the replacement splits in two:

```text
CONTENT   the mesh / texture / material / clip   -> baked NATIVE into MyMod.bundle (no runtime import)
BINDING   "this object wears that content"       -> a runtime assignment, in both modes
```

Both modes therefore run the **same binding code**. Shipping mode is not "no code"; it is
"binding driven by a baked table off a load-time seam, instead of by a scan". That is what removes
the stutter, and it is the whole of the difference.

~~Anyone who reads this section and proposes to make visual replacement fully load-native must first
answer how a second bundle edits the first one. There is no known answer; do not invent one.~~

**MEASURED FALSE 2026-08-12 (gate R7, PROVEN-FOUNDATIONS).** The question was the wrong one: nothing
has to edit the first bundle. Route vii patches a COPY of the shipped bundle and repoints the game's
own Addressables catalog at it, so visual replacement IS fully load-native, with zero runtime code.
Everything in §39.1 downstream of the sentence above — "baked native replacement is only literally
true for audio", and the CONTENT/BINDING split — is superseded for anything reachable through the
catalog. The runtime binding path remains the developer workbench, not the shipping mechanism.

---

## 39.2 Asset identity — the target path

One string names a target, in both modes, in the project file and in the baked table:

```text
<anchor>#<subpath>
```

### Anchors, in resolution order

| Anchor | Form | Obtained from | Stability |
|---|---|---|---|
| `guid:` | `guid:8f3c…` | `AssetReference.AssetGUID` seen at the resolve seam (§39.3) | Addressables GUID; survives sessions, survives a game patch unless the asset itself is re-authored |
| `media:` | `media:18839791` | the frozen Wwise index (§20, `data\pp_wwise_index.json`) | already the audio production key (§3.2); unchanged by this section |
| `name:` | `name:Geo_Head02_V01` | a live `UnityEngine.Object.name` from the dev scan | **dev mode only.** Ambiguous name = ERROR, never a guess (RR's rule, `MeshReplacer.cs:1927-1933`) |

`defname:<DefName>` is accepted as author-facing sugar in `replacements.json`: the tool resolves
the Def's `AssetReference` once and **rewrites the record to the `guid:` form on save**. Only
`guid:`/`media:`/`name:` reach the bake.

### Subpath

Empty for `media:`. Otherwise, transform path inside the anchored prefab + the slot:

```text
guid:8f3c…#Root/Chest/Arm_R@SkinnedMeshRenderer.mesh
guid:8f3c…#Root/Chest/Arm_R@Renderer.materials[1]
guid:8f3c…#Root/Chest/Arm_R@Renderer.materials[1].tex:_MainTex
guid:8f3c…#@Animator.clip:Idle_Rifle
```

The transform path is the game's own hierarchy, read at pick time. A path that no longer resolves
is a refusal naming the missing node — never a fuzzy match.

### Verification, not identification

Each record also stores `sha1` of the target's content as it was when the author picked it
(vertex bytes for a Mesh; RGBA readback per §29.5 for a Texture2D; property block for a Material).
It is **checked at apply time and never used to look anything up**:

```text
sha1 matches      apply
sha1 differs      refuse this one record, log "the game changed this asset since it was picked"
```

This is the deliberate departure from ResourceReplacer. RR *identified* by
`<name>_<checksum>` (`MeshReplacer.cs:1959-1972`, `Resource_Replacer.cs:1057-1064`), which forced
a whole dump pipeline to give the author the checksum, forced meshes to be CPU-readable, and then
needed a rename table because a replaced object no longer hashes to its own key
(`Resource_Replacer.cs:1156-1163`). Checksum-as-verification keeps the one useful property (you
learn when the game was patched under you) and costs nothing else.

### What identity costs, stated plainly

- A GUID cannot be typed. Identity is acquired by **pointing at something in the running game**
  (§39.3 picker). That is dev-mode work by construction, which is precisely why the two modes are
  one tool.
- Offline browsing aid, already on disk, no new tooling:
  `E:\DEV\PhoenixPoint\extracted\GameData\inventory\assets.csv` — 1,354,635 rows,
  `Name,Type,Container,PathID,SourceBundle,Size`; and `extracted\GameData\defs\<Type>\<Def>.json`
  carries each def's `guid`. **`PathID` is not a runtime key** — Unity exposes no PathID to a
  running mod — so the CSV informs the picker's search box and nothing else.
- No dump infrastructure is added. §12.2's "no general dump infrastructure" stands.

---

## 39.3 Developer mode

### Discovery — a sanctioned load-time seam exists, and it is the primary

```text
AddonSkinDataBase.GetPrefabAsset(AssetReferenceGameObject)
decompiled\AssemblyCSharp\Assembly-CSharp\src\PhoenixPoint.Common.Entities.Addons\AddonSkinDataBase.cs:19-30
```

One `public static` chokepoint, 13 call sites, all inside the skin-data defs
(`…Entities.Items.SkinData\{SimpleSkinDataDef,SimpleBodyPartSkinDataDef,HumanBodyPartVariant,FilteredSkinDataDef}.cs`),
returning `(GameObject)assetReference.Asset`. A Harmony postfix there hands us **the AssetGUID and
the resolved prefab together, before instantiation**. Two consequences:

- identity is free and exact — no checksum, no dump, no scan;
- we can write the **prefab**, and every instance the game spawns afterwards is born replaced —
  MEASURED, foundations #19 (`ct_seamprobe` 2026-08-12 13:12:35:
  `instancesWearingOurs=4 instancesWearingOriginal=3`; a soldier stripped of his armour had no body).
  Instances already built keep what they were built with; only new ones inherit.
- but the write does NOT stay written. Addressables releases and re-acquires the prefab: measured
  across a mission load as 102 of 410 resolves handing back a DIFFERENT object with our mesh gone,
  and `off` unable to restore because "the renderer we wrote is gone" (foundations #20). **The seam
  binding is apply + RE-APPLY on every resolve, not a one-shot write.** The re-apply is free: the
  postfix already runs on every resolve and already does the dictionary lookup.
- that is still what removes RR's periodic re-scan (`Resource_Replacer.cs:40`, `:1023`) for
  everything the seam covers — the re-apply is event-driven, not a timer.

Coverage, honestly: characters, body parts, item visuals — the assets that go through skin data.
NOT the environment, NOT scene props, NOT UI, NOT VFX.

Second seam, no patch needed, already used by RR:

```text
AddonsCharacterBuilder.OnCharacterRebuilded   AddonsCharacterBuilder.cs:51 (declared), :293 (invoked)
```

Fires after a character is re-dressed; re-applies bindings on an assembled character. RR reaches
the builders with a typed `Object.FindObjectsOfType<AddonsCharacterBuilder>()`
(`MeshReplacer.cs:1833-1841`) — a bounded, on-demand, typed find, not the banned periodic sweep.

Rejected as the general seam: `AssetsManager.AcquireDependenciesAsync`
(`Base.Assets\AssetsManager.cs:68-237`) is a compiler-generated iterator and only iterates
`AssetReference`s reflected off root defs (`:86-88`); patching it buys coverage we cannot use and
costs a state-machine patch. A postfix on `AssetReference.get_Asset` would be universal but sits on
a per-frame hot path — UNPROVEN (§39.6), not a v1 dependency.

### Fallback discovery — the scan, dev mode only

For a target with no `guid:` anchor (scene props, static-batched geometry) the RR budgeted scan is
**permitted in developer mode** and is the only thing that reaches them:

```text
Resources.FindObjectsOfTypeAll<Texture2D>   Resource_Replacer.cs:1023
scene traversal + FindObjectsOfTypeAll<Mesh> MeshReplacer.cs:3156-3172
```

Rules: OFF by default; one `ct_scan` toggle; the same frame budget shape RR used
(`Resource_Replacer.cs:1030-1055`); and a scan-discovered (`name:`) target **cannot be baked**
(§39.4).

### Apply — port RR's mechanics verbatim, they are correct

| Kind | Apply | RR reference |
|---|---|---|
| Texture2D | ~~`ImageConversion.LoadImage(tex, bytes)` in place~~ **AMENDED 2026-08-12, measured:** bind OUR texture through a cloned material (`SetTexture`), exactly like the Material row. The game's Texture2D is never written, so revert is a reference restore instead of a re-import | `Resource_Replacer.cs:1165-1167` (the in-place write, now rejected); clone mechanics `MeshReplacer.cs:2110-2132` |
| Mesh (static) | `filter.sharedMesh = ours`, then toggle `renderer.enabled` off/on (Unity resets static-batch info on mesh assignment) | `MeshReplacer.cs:1936-1943` |
| Mesh (skinned) | `renderer.sharedMesh = ours` after binding to the live rig; skeleton never touched | `MeshReplacer.cs:1896-1901` |
| Material | build `new Material(original)`, change only named properties, assign the whole `sharedMaterials` array atomically — nothing written until every slot succeeded | `MeshReplacer.cs:2110-2132`, `:2169-2179` |
| Animation | `AnimatorOverrideController` slot assignment, planned offline | `AnimationRemap.cs:156-259` |
| Audio | unchanged: generated bank + `LoadBankMemoryCopy`; hot reload with the same bankId is proven (foundation #14) | §3, §25 |

### Revert

Origin maps, exactly RR's shape — the original object is never written to, so restore is real and
not best-effort:

```text
RestoreMeshes     MeshReplacer.cs:2335-2349
RestoreMaterials  MeshReplacer.cs:2355-2368
```

**Superseded 2026-08-12 by measurement — kept because the reasoning below is why the amendment
exists.** `ct_texswap` 13:47 showed the §29.5 capture is NOT faithful enough to be a revert: on
`UI_ArbitraryIcon_Circle_Fuel_uinomipmaps 128x128 BC7` the captured PNG decoded into a fresh texture
hashed `10217be4…` against a pre of `e8c39d7d…`, and the restored texture hashed exactly that same
`10217be4…` — the restore faithfully reproduced a capture that was already wrong. On
`CHR_PX_ASS_LG_Accessories_V01_albedo 2048x2048 DXT1` a second loss stacked on top: `LoadImage`
re-encoded the container (`before=DXT1 mips=12 after=DXT5 mips=12`, and `BC7 -> ARGB32` on the
icon), so the write is lossy even where the capture is not. R-U4 is therefore ANSWERED NO. The fix
was not a better capture but not needing one: bind our own texture through a cloned material and
never write the game's object.

~~The one kind where revert is NOT free is Texture2D, because `LoadImage` destroys the original
pixels.~~ Capture the original once, before the first write, per §29.5
(`RenderTexture` + `Graphics.Blit` + `ReadPixels`), and hold it for the session. Cost: one RGBA32
copy per replaced texture, in managed memory, dev mode only. If capture fails, **refuse the
replacement** rather than perform an unrevertable one.

### Re-apply

- `guid:` targets — **re-apply at the seam on every resolve.** The earlier "the prefab is written, so
  no re-apply" was MEASURED FALSE (foundations #20): a re-acquired prefab comes back clean. Keep the
  authored table keyed by GUID and re-assign whenever the resolved prefab is not already wearing our
  content — a reference comparison per resolve. `OnCharacterRebuilded` remains available for
  re-dressing an assembled character, but it is not what makes the binding hold.
- `name:` targets — need the periodic re-scan, same as RR. Accepted, dev mode only, and this is
  the second reason they cannot ship.

---

## 39.4 Shipping mode

Bake emits, into the existing `MyMod.bundle`, one extra binary `TextAsset` beside the ones already
proven (#7, #8a):

```text
assets/<modid>/bindings.json     the resolved replacement table: target path, content asset name, sha1
```

The released mod's runtime, generated as a new §26.5 helper `ContentBindings.cs` (no new
architecture — §26 already generates the bundle loader, bank loader and stream extractor).
**SUPERSEDED 2026-08-12 — this helper is not being built (Task 27 dropped); route vii replaces the
asset before instantiation and leaves it nothing to bind. Kept because the shipping verdict below
only makes sense next to the design it replaces:**

```text
OnModEnabled
  load MyMod.bundle                      (§26.1, already generated)
  read bindings.json                     -> Dictionary<AssetGUID, Binding[]>
  Harmony postfix on AddonSkinDataBase.GetPrefabAsset
      one dictionary lookup per prefab resolve; miss -> return
      hit -> if the slot is not already wearing our content, LoadAsset<T> and assign
             (re-assign, every resolve - a re-acquired prefab comes back clean, foundations #20)
```

Player-side cost: one dictionary miss per skin-prefab resolve, and on a hit one reference
comparison plus an assignment only when the prefab came back clean. No scan, no `Update`, no
polling, no per-frame work, no import — the content is already native in the bundle. Harmony is
already a hard dependency of every PP mod.

**Shipping verdict, now that the seam is measured (foundations #18-#21).** The seam works and it
reaches rendered objects, but it buys this at the price of a Harmony patch plus a re-apply on every
prefab resolve in the player's game, forever. Route vii (`ct_route7`: patched bundle copy + catalog
repoint, `U3d` passed with a forged external PPtr resolving in-game, commit `88dce36`) replaces the
asset BEFORE anything is instantiated: no patch, no per-resolve work, no re-acquisition to lose the
write to, no runtime code in the shipped mod at all. **Route vii is the shipping path; the seam is
the developer workbench** — where a change is visible immediately without rebuilding a bundle. This
is not weakened by the seam having passed: passing only made it a good dev tool, not a good
shipping mechanism.

**Amendment 2026-08-12 — the shipping mechanism is route vii, measured (gate R7).** The model above
(a generated Harmony postfix on `GetPrefabAsset`, one dictionary lookup per prefab resolve) is no
longer the shipping answer. A patched private copy of the shipped bundle in the mod folder, plus one
rewritten `m_InternalIds` string and a zeroed `m_Crc` in `catalog.json`, makes the game's own
Addressables load the replacement with **zero runtime code** — proven end to end, apply → restart →
verify → revert, byte-identical restore (PROVEN-FOUNDATIONS R7, commit `7213575`).

The seam keeps its job, a different one: it is the **developer workbench** — it is how an author
points at a live object and acquires its identity, and it does reach rendered objects. It is not the
shipping path, because shipping through it costs a Harmony patch plus a re-apply on every prefab
resolve in the player's game.

Open before route vii is a shipping design, in the order that blocks most:

1. **multi-mod catalog sharing** — one `catalog.json`, several mods. Needs a pristine backup plus a
   per-mod edit record; today's single-backup refusal handles exactly one mod.
2. ~~**disk cost for large bundles**~~ — **CLOSED 2026-08-12, and it takes a whole mechanism with
   it.** The 1.5x was our own doing: we wrote the copy uncompressed. Measured on
   `mutoid_assets_all.bundle` (LZ4 in the shipped file) — uncompressed **266 623 B (1.52x)**,
   repacked LZ4 **175 838 B (1.00x)**, LZMA 125 276 B (0.71x). `BundleBaker.Write` now packs with
   the source's own compression, so a private copy costs what the game already ships.
   **Do not build the entry-redirect variant** (research note §4 route iii): it existed only to
   avoid a disk cost that does not exist. Route vii covers everything it would have, plus the
   non-addressable objects it never could.
3. **U3e** — an externals entry we add ourselves, which `AnimatorOverrideController` needs.

### Per-kind verdict — is it bakeable?

| Kind | Content baked native? | Binding native at load? | Verdict |
|---|---|---|---|
| Audio (replace game media) | n/a — the bank IS the replacement | **YES, fully** — shape C, media-ID keyed, `LoadBankMemoryCopy` at init | fully bakeable, already proven (#12, #13) |
| Audio (new sound) | YES (#10a, #15d) | YES | unchanged |
| Texture2D | YES (U1 proven) | NO — needs `LoadImage`/`CopyTexture` onto the game's texture at the seam | content bakeable, binding is runtime |
| Mesh | YES (U2 proven) | NO — needs `sharedMesh =` | content bakeable, binding is runtime |
| Material | YES once U3 lands (Task 2) | NO — needs `sharedMaterials[i] =` | content bakeable, binding is runtime |
| AnimationClip | YES once U6 + Task 5 land | **superseded — see the amendment below** | ~~content bakeable; binding MUST be the runtime override path, there is no bake for it~~ |

**Amendment 2026-08-12 — the animation row above is wrong, and so is its premise.** Its argument was
that an `AnimatorOverrideController` needs an external PPtr into a foreign serialized file, "banned
(§10.1, §29.3)". Neither citation holds: §29.3 is a *test-hygiene* rule (verify `m_Mesh.fileID == 0`
before using a bundle as a repack subject) and bans nothing, and §10.1's objection was patch
fragility — a cost the user retired on 2026-08-12. **Gate U3d then measured it**
(PROVEN-FOUNDATIONS, `ct_bake` 12:45:10, commit `4aa7148`): a hand-forged external `m_Shader` into a
shipped bundle resolves to the real shader in-game, with a wrong-pathID control returning
`Hidden/InternalErrorShader`. `AnimatorOverrideController.m_Controller` is the same construct, and
`m_Clips` is already a list of (original PPtr, override PPtr) pairs — i.e. a baked override table.

What U3d does **not** settle: it forged into a CAB the cloned bundle *already declared*. An
externals entry we add ourselves is gate **U3e** (see PROVEN-FOUNDATIONS), and that is the case
Task 28 actually needs. Do not design Task 28 against a baked controller until U3e has a number.

Same amendment, §10.1: the Def-donor shader path is no longer the only option — in the **baked**
path the shader can be an external PPtr assigned at bake time (U3d), which removes the runtime
donor resolution of §10.2 and makes U3b/U3c optional there. §10.1 still stands for anything
materialized live at runtime.

### What bake refuses

- any `name:` (scan-discovered) target — shipping mode has no scan, so the record could never
  apply; bake fails naming the record. No flag, no "scan mode" release build.
- any record whose `sha1` no longer matches the live asset at bake time — the game was patched
  since the pick; re-pick.

---

## 39.5 What the two modes share

This is the answer to "why one tool":

```text
identity scheme          §39.2, one string, one parser, one resolver
ContentProject IR        src\Project\ContentProject.cs — add ReplacementRule beside
                         ImportedTexture (:10) / ImportedAudio (:21); no second import path (§0.3)
the picker               produces the identity; dev-only UI, but it is what makes a shipping mod possible
the apply functions      literally the same static methods (§39.3 table) called from the dev seam
                         and from the generated shipping helper
the bake path            src\Bake\ProjectBake.cs — bindings.json is one more TextAsset next to
                         the bank and stream assets that already work
the validator            sha1 + path resolution + the Wwise matrix (§9.4, src\Wwise\IdIndex.cs)
```

The only code that exists in one mode and not the other: the scan (dev), the picker UI (dev), the
generated Harmony postfix (shipping).

---

## 39.6 UNPROVEN — do not build on anything still open below

> R-U1 and R-U2 were SETTLED by measurement on 2026-08-12 and are struck through rather than
> deleted, so the claim and the number that closed it stay side by side. R-U3/R-U4/R-U5 are still
> open and the rule above applies to them unchanged.

| # | Claim | Cheapest experiment |
|---|---|---|
| ~~R-U1~~ | **SETTLED 2026-08-12, foundations #18/#21** — the postfix fires with usable GUIDs (61 guids / 60 resolvable subpaths / 0 without a GUID / 0 errors inside a mission) and the same-run scan control measured 72,7% (8 of 11) rigged-renderer coverage outside a mission. Mission-scale coverage is still unmeasured. | — |
| ~~R-U2~~ | **SETTLED 2026-08-12, foundations #19/#20 — and it settled BOTH ways.** A prefab write does reach rendered objects (`instancesWearingOurs=4`), but it does NOT stay written: 102 of 410 resolves handed back a re-acquired prefab with our mesh gone. Binding = apply + RE-APPLY on every resolve (§39.3). | — |
| R-U3 | `AssetReference.AssetGUID` is stable and equals the `guid` in `extracted\GameData\defs\*.json` | offline, no game: take 20 skin defs from `extracted\GameData\defs\`, compare their stored `guid` to the GUIDs the R-U1 probe logged. Disagreement means the picker must store the runtime GUID only and the CSV/defs are search-only. |
| ~~R-U4~~ | **ANSWERED NO 2026-08-12, then MOOT.** A §29.5 readback does not round-trip (captured PNG `10217be4…` vs pre `e8c39d7d…`), and `LoadImage` re-encodes the container on top (`BC7 -> ARGB32`, `DXT1 -> DXT5`). Moot because the dev swap stopped using that path: it binds our own texture through a cloned material and never writes the game's (R2 green, foundations #23). Open again the day something needs a faithful readback of a compressed texture. | — |
| R-U5 | A postfix on `AssetReference.get_Asset` is viable as the universal seam | only if R-U1 coverage is unacceptable: patch it behind a GUID `HashSet` lookup, log frame time over 60 s against an unpatched control run. |

---

## 39.7 Task list — continues §32

### Task 22 — Replacement identity + IR

- target-path parser/formatter (`guid:` / `media:` / `name:` + subpath grammar, §39.2);
- `ReplacementRule` in `src\Project\ContentProject.cs` beside `ImportedTexture` / `ImportedAudio`;
- `replacements.json` load/save; `defname:` resolved to `guid:` on save;
- sha1 helpers per kind.

Gate **R0** (offline): round-trip every anchor form and every subpath form through
parse → format → parse, byte-identical; a malformed path is a named refusal, never a silent skip.
Control: one deliberately ambiguous `name:` record in the same run must be REFUSED while the
`guid:` record beside it loads.

### Task 23 — Resolve-seam probe (settles R-U1, R-U2, R-U3)

Depends on Task 22.

- Harmony postfix on `AddonSkinDataBase.GetPrefabAsset`;
- `ct_seamprobe` logs `(AssetGUID, prefab name, renderer paths)`;
- one scan pass in the same run as the control.

Gate **R1**: `ct_seamprobe` reports ≥1 GUID with a resolvable renderer subpath during a real
mission load, AND the same-run scan control reports the total, so coverage is a measured ratio and
not a claim. Nothing after this task may be designed against the seam until R1 has a number.

### Task 24 — Dev-mode texture replace + revert

Depends on Task 23.

- `ct_replace <targetpath> <file>` / `ct_revert`;
- §29.5 original capture BEFORE the first write; capture failure = refusal.

Gate **R2**: in one run — pick a shipped texture at the seam, log its pre-sha1, replace, log
post-sha1 (differs), revert, log sha1 again (**equals** pre). Control in the same run: a second
shipped texture never named by any record, sha1 unchanged at all three points.

### Task 25 — Dev-mode mesh + material replace + revert

Depends on Task 23. Ports `MeshReplacer.cs:1896-1901`, `:1936-1943`, `:2110-2132`, `:2335-2368`.

Gate **R3**: one run — a named renderer wears our mesh (vert count + bounds logged, differ from
origin), our material (shader name preserved, one property changed), then reverts to the exact
origin object identity (`sharedMesh == originalMesh` by reference). Control: a sibling renderer on
the same character, unnamed by any record, unchanged throughout.

### Task 26 — Scan fallback, dev only

Depends on Task 25. OFF by default, `ct_scan` toggle, RR frame budget, ambiguity = refusal.

Gate **R4**: a scene prop with no `guid:` anchor is replaced via a `name:` record with the scan ON,
and is NOT replaced with the scan OFF, in the same session. Control: a `guid:` record keeps
applying with the scan OFF.

### ~~Task 27 — Bake the bindings + generated shipping helper~~

**DROPPED 2026-08-12, approved. Do not restore it from a stale task list.** Route vii (`ct_route7`:
patched bundle copy + catalog repoint, R7 green `0455b76`) replaces the asset BEFORE anything is
instantiated, so a shipped binding table has nothing left to bind: there is no resolve to postfix,
no prefab to write, and no re-apply to perform. Shipping every replacement through route vii also
costs the player strictly less than this task's design did — no Harmony patch, no dictionary lookup
per prefab resolve, no runtime code in the released mod at all (§39.4, shipping verdict).

The seam keeps its job as the DEVELOPER WORKBENCH (Tasks 24-26), which is a different job from
shipping and is not affected by this.

~~Depends on Tasks 24-25 and on Task 18 (Bake Release). Adds §26.5 `ContentBindings.cs` to the
generated release helpers. Gate **R5**: a sample mod with ONE mesh + ONE texture replacement, built
through `Bake Release`, loaded in a game session where ContentTool.dll is not present, shows both
replacements...~~

### Task 28 — Animation binding

Depends on Task 5 (full animation serialization). ~~and Task 27~~ — Task 27 is dropped, so the
shipping half of this task goes through route vii like every other replacement; what remains here is
the dev-mode override path. Baked clip + runtime
`AnimatorOverrideController` slot assignment (`AnimationRemap.cs:156-259`). No baked controller —
see §39.4.

Gate **R6**: a baked clip plays on a shipped character in a session without ContentTool.dll,
with objective Transform motion logged (§18.1 rule: never audible/visual-only). Control: the same
character with the record removed plays the game's own clip in the same run.

---

## 39.8 Amendment to §12.2 and §34

§12.2's and §34's bans were written against ResourceReplacer's *shipping* architecture. They are
re-scoped, not lifted. Precisely:

| Item | Was | Now |
|---|---|---|
| `Resources.FindObjectsOfTypeAll` scanning | "do not port … scene scanning" (§12.2) | **banned in any released mod; permitted inside developer mode** as the fallback discovery seam for targets with no `guid:` anchor (§39.3). Off by default, frame-budgeted, and a scan-only target cannot be baked. |
| assigning `renderer.sharedMesh` / `sharedMaterials` | implied banned with the scan | **permitted in both modes** — it is the only mechanism that exists (§39.1). What stays banned is a *game-wide sweep* that assigns them by scanning, in a shipped mod. §34's "no game-wide automatic replacement of arbitrary Unity references" is exactly this, and stands: a shipped mod applies a finite, explicitly authored table off a load-time seam. |
| `ImageConversion.LoadImage` onto a game texture | not addressed | permitted in both modes, with the §29.5 original capture as a precondition in dev mode. |
| RR's periodic re-scan timer | banned | banned in shipping; in dev mode it is needed ONLY for `name:` targets (§39.3). |

Still permanently dead, one reason each:

- `SetMedia` production path — a media-only bank replaces without it (§3.2, foundation #12).
- `PatchBank` — rewriting a shipped `.bnk` when generating our own bank is strictly simpler.
- `Restage` / `StageOne` — staging orchestration for a file layout the tool no longer emits (ARM1, §4.4).
- RR base-path shadowing — one `AddBasePath` for the mod's own folder is proven and sufficient (§7, #15a).
- old pack priority system, old staging/cache design, runtime codec path, general dump
  infrastructure — all serve RR's file-driven model; identity here is a picked GUID, not a filename (§39.2).

§34 gains one non-goal:

- no bake-time rewriting of Phoenix Point's own shipped serialized assets (§39.1).
