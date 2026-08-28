# One bundle per mod — can ADD and REPLACE live in the same mod-owned bundle?

Question settled here: instead of shipping a patched COPY of every shipped bundle a mod touches
(route vii), can ONE mod-owned bundle carry the mod's added assets AND its replacements, with
`catalog.json` simply pointing the relevant KEYS at it?

Everything below marked **[M]** was measured today against the player's own
`StreamingAssets\aa\catalog.json.ct-backup` (1 670 824 B, pristine) and the game's own shipped
`Managed\Unity.Addressables.dll` / `Unity.ResourceManager.dll` (ilspycmd 9.1.0). Nothing here was
run in-game — see UNMEASURED at the bottom, and the gate in §7.

---

## 0. VERDICT

**Reachable, with exceptions — but it is a SECOND route, not a unification. Route vii stays.**

- **ADD**: one mod-owned bundle, registered in `catalog.json`, is reachable and is the right shape.
  Nothing blocks it at format level.
- **REPLACE of a whole addressable asset** (a prefab / fbx that has its own catalog key): reachable
  from the mod's own bundle. The two bricks compose — U3e's self-added externals entry carries the
  references to shipped assets, and the catalog's own dependency-set mechanism satisfies d4e1814's
  MOUNT precondition declaratively **[M]**, so no runtime code mounts anything.
- **REPLACE of a sub-object** (a Texture2D / Material / Mesh *inside* a shipped prefab): **NOT
  reachable by any catalog edit** — those objects are not catalog keys **[M]**, so no key can be
  pointed at them. Every replacement ContentTool has proven in-game so far (P1/P3/P4/P5) is of this
  kind. Route vii's patched copy is the only mechanism that reaches them, and deleting it would
  delete the tool's proven capability.
- So "все в одном бандле" is true for a mod's OWN content and for whole-asset replacement, and
  false for surgical replacement inside shipped assets. The honest shape is **one mod bundle +
  route vii when the target is buried**, not one mechanism.
- Net value of building it: a mod claims a KEY instead of a whole shipped bundle. Blast radius
  today, measured: claiming `_common_assets_all.bundle` locks every other mod out of **1311**
  addressable assets **[M]**; `px_equipment` 411; `nj_equipment` 309. Key-level claims make that
  1-for-1. Plus: no 848 MB copy for a mod that only wants one prefab out of `kaos_content`.

---

## 1. Catalog structure, re-measured end to end **[M]**

`m_InternalIds` 4119 · keys/buckets **8232** · entries **5850** · `m_KeyDataString` 509 072 B ·
`m_BucketDataString` 113 824 B · `m_EntryDataString` 163 804 B · `m_ExtraDataString` 76 240 B.
Both blobs decode with **zero bytes left over** (`bucketData consumed=113824 of 113824`,
`entryData consumed=163804 of 163804`) — the layout in `research-zero-runtime-replacement.md` §3 is
exact and complete.

- Entry = 7 × int32 = 28 B: `{ internalIdIdx, providerIdx, dependencyKeyIdx, depHash, dataIndex,
  primaryKeyIdx, resourceTypeIdx }`.
- `m_ProviderIds` = 2: `[0] AssetBundleProvider`, `[1] BundledAssetProvider`.
- **90 bundle entries** (provider 0), **5760 asset entries** (provider 1). All 5760 asset entries
  have a `dependencyKeyIdx >= 0`; all 90 bundle entries have `dataIndex >= 0` and
  `dependencyKeyIdx == -1`. No asset entry carries a `dataIndex`.
- 90 distinct bundle `internalId` indices, 90 distinct bundle primaryKeys, **90 `.bundle` files on
  disk** (7 856 MB total, median 44.6 MB, largest `kaos_content` 848 554 860 B) — 1:1, no aliasing.
- Key shapes: 4029 32-hex GUIDs, 4001 path-like, 202 other (bundle names + 84 int32 dependency-set
  keys). GUID and address are two keys onto the same entry, so ≈4029 addressable assets.
- Dependency-set keys are **int32**, not Hash128 (84 of them); bucket sizes 2–9.

**Correction to PROVEN-FOUNDATIONS** (the row under "Corrected assumption", route vii): "all **45**
options blocks are UTF-16, zero ASCII" is an artefact of decoding the whole blob as UTF-16 at even
alignment. Byte-pattern scan for UTF-16LE `"m_Crc":` at ANY alignment finds **90 hits — 45 even,
45 odd** **[M]**, i.e. one options block per bundle, as it must be. `Route7.CrcDigits`
(`src\Bake\Route7.cs:521-547`) already scans on the BYTES from the entry's `dataIndex`, so it is
correct and unaffected; only the doc line is wrong.

---

## 2. Q1 — can a catalog key be repointed at a bundle the mod OWNS?

**Yes, and route vii does not do this today.** Two different edits, worth never confusing:

| | route vii (today) | key redirect (route iii) |
|---|---|---|
| What changes | the FILE PATH behind a shipped bundle's key | which BUNDLE an asset key resolves through |
| Fields touched | one `m_InternalIds` string + that bundle's `m_Crc` in `m_ExtraDataString` | entry field 0 (`internalIdIdx`) + field 2 (`dependencyKeyIdx`), plus appends |
| Consequence | our file must be a full copy of the shipped bundle — every other asset in it still resolves through the same key | our bundle needs to carry only the one asset |
| Code | `Route7.ApplyOne` / `ZeroCrc`, string+byte surgery, ~60 lines | a real `ContentCatalogData` writer, ~200-300 lines |

What decides it, grounded in `ContentCatalogData.CreateLocator` (shipped `Unity.Addressables.dll`,
ilspycmd):

- `m_InternalIds` decides only the PATH of a bundle location, and only for provider 0. For provider
  1 (asset) entries the `internalId` is the asset's NAME inside its bundle — `BundledAssetProvider`
  calls `bundle.LoadAssetAsync(location.InternalId)`. So an asset's internalId must match the name
  in OUR bundle's `m_Container` (foundation #6 proves an `m_Container` edit reaches the engine).
- `dependencyKeyIdx` decides WHICH BUNDLES load first. Its bucket's entry list is the bundle set.
  Measured example, key `54a8f796c0a49f74caac9ab03d4da053` **[M]**:
  ```
  entry 1618  internalId[1115]='Assets/Art/…/ALN_Fireworm_BodyAll_Ready.prefab'  prov=1
              depKey=8161 -> 'i32:805438131'   dataIndex=-1  primaryKey='02_Bodyparts/ALN_Fireworm_BodyAll_Ready.prefab'  resType=1
     dep 12   '…\StandaloneWindows64\aln_fireworm_assets_all.bundle'                prov=0
     dep 33   '…\StandaloneWindows64\defaultlocalgroup_unitybuiltinshaders.bundle'  prov=0
     dep  2   '…\StandaloneWindows64\_shaders_assets_all.bundle'                    prov=0
  ```
- **`m_Crc` decides nothing about ownership** — it is inside the bundle's options block and only
  gates `AssetBundle.LoadFromFile(path, crc)`. Set 0 for our own file.
- **An options block is MANDATORY for a mod-owned bundle location.** `AssetBundleResource.GetLoadInfo`
  opens with `if (!(handle.Location?.Data is AssetBundleRequestOptions)) { loadType = LoadType.None;
  path = null; return; }` and `BeginOperation`'s `default:` arm then completes with
  `RemoteProviderException("Invalid path in AssetBundleProvider: 'null'.")`. So `dataIndex = -1` on a
  bundle entry is a loud, immediate failure — a new bundle location must append its own
  `AssetBundleRequestOptions` JSON into `m_ExtraDataString`. Loud, not silent: good failure mode.
- **Appending is index-stable.** `CreateLocator` reads buckets positionally and assigns
  `array5[l] = ReadObjectFromByteArray(keyData, buckets[l].dataOffset)` — key *i* IS bucket *i*.
  New buckets/entries/internalIds/options blocks appended at the end shift nothing. One gotcha:
  `object[] array5 = new object[BitConverter.ToInt32(keyData, 0)]` — the count int at
  `m_KeyDataString[0..3]` (today **8232**, exactly the bucket count **[M]**) sizes that array and
  must be bumped with every appended key, or `CreateLocator` throws `IndexOutOfRange` at boot.
- `m_InternalIdPrefixes` is `[]` **[M]**, so `ExpandInternalId` is a no-op and route vii's plain
  absolute path stays correct.

---

## 3. Q2 — what breaks when one bundle holds one added and one replaced asset?

**Inside the bundle: nothing.** A bundle is a container; both cases are "an entry names an object in
our file". Foundation #4 (new objects ADDED to a bundle) and #6 (`m_Container` edit reaches the
engine) are both already proven, and `BundleBaker` writes both shapes today. No ordering constraint
between the two exists in the format.

The constraints are all at the CATALOG level:

1. **ADD appends a key; REPLACE mutates an entry in place.** They are different edits to different
   blobs and do not interact.
2. **A duplicate key is fatal at boot.** `CreateLocator` ends with
   `resourceLocationMap.Add(key, array7)` → `ResourceLocationMap.Add(object, IList)` →
   `Locations.Add(key, locations)` → `Dictionary.Add` throws `ArgumentException` on a collision, at
   `Addressables.InitializeAsync` (`PhoenixGame.cs:738`), i.e. the game never boots. Exactly the
   failure class as `StreamableAssetsCatalog.cs:22`'s `ToDictionary` (V1-dupkey). **A must-reject
   validator is mandatory, in the A4 / V1-dupkey shape**, and it must be global across mods.
3. **Dependency sets, not ordering, are the real constraint.** Every entry pointing into our bundle
   must have a `dependencyKeyIdx` whose bucket lists our bundle's location entry. If a replaced
   asset external-PPtrs shipped objects (U3d/U3e), **the shipped bundles owning those CABs must be
   in the same dependency set** — which is precisely how the game does it itself (the fireworm
   example above lists both shader bundles). This is d4e1814's mount precondition
   (`archive:/cab-x/cab-x` resolves only while the owning bundle is MOUNTED) satisfied by
   declaration instead of by our own `AssetBundle.LoadFromFile`. Added and replaced assets can share
   one dep-set if they need the same bundles, otherwise append two.
4. **CAB uniqueness.** Unity refuses to mount two archives with the same CAB name (route ii,
   research §4). Our bundle's CAB must be per-mod-unique; `BundleBaker` already names CAB per bundle.
5. **Type index.** A new/redirected entry needs a `resourceTypeIdx` into `m_resourceTypes`; the
   shipped list already carries `IAssetBundleResource`, `GameObject`, `Avatar`, `Mesh`, `Material`,
   `Texture2D` **[M]**, so common kinds need no new type row.

---

## 4. Q3 — multi-mod: better or worse?

**Better on conflict granularity, strictly. Worse on blast radius of a writer bug.** The intuition
holds, and here is the number.

- **What already stacks, unchanged:** `catalog.json.ct-backup` (PRISTINE, written once) +
  `catalog.json.ct-edits` (one line per mod), and **every write rebuilds from pristine + surviving
  lines** (`Route7.Rebuild`, `src\Bake\Route7.cs:440-446`). Proven S1–S4. Plus the SHA-1 guard
  ("REFUSED: catalog.json changed since we last wrote it"), `File.Replace` atomicity, and orphan
  dropping. **None of this changes** — it is a property of the ledger, not of what a record contains.
- **The record changes from `(mod, bundle, path)` to `(mod, key, …)`.** Today `Apply` refuses when
  two mods name the same bundle (`S2-conflict`, "one of them has to go"). That refusal is coarse:
  measured **[M]**, `_common_assets_all.bundle` owns **1311** addressable asset entries,
  `kaos_content` 765, `tutorialimages` 631, `px_equipment` 411, `nj_equipment` 309. Two mods that
  touch two unrelated assets in `_common` are refused today for no physical reason.
- Under key redirect two mods collide only if they replace the **same** addressable asset — 1 of
  ≈4029 instead of 1 of 90. **Strictly finer.**
- **Where it gets worse, honestly:** three blobs get appended instead of one string being swapped,
  so the rebuild is more code, and a bug in the writer corrupts the catalog for EVERY mod, not just
  the offender — and the symptom is an unbootable game (§3.2), not a missing texture. Mitigation is
  the discipline already in place (pristine backup in the game's own folder, SHA-1, `File.Replace`)
  plus the mandatory dup-key refusal.
- **Second new global check:** added keys must be unique across mods, not just against vanilla. The
  ledger already lists every mod's records, so the check has the data it needs.

---

## 5. Q4 — the loading story

- **A catalog-registered mod bundle loads exactly like a shipped one** — lazily, by the game's own
  `AssetBundleProvider`, on first request of an asset in it. Zero runtime code, same as route vii.
- **It does NOT load "ahead of `InitMods()`", and neither does anything else.** `InitializeAsync`
  (`PhoenixGame.cs:738`) loads the CATALOG and no content; at `InitMods()` (`:758`) not one shipped
  bundle is open — measured, d4e1814. "Loads cleanly ahead of InitMods" is the wrong goal; the right
  one is "the catalog names it before InitMods", which an on-disk edit gives.
- **Cost of ONE big bundle vs several small:** a bundle is mounted whole on first use and stays
  resident until released, so a single mod bundle is all-or-nothing residency; several small ones
  load independently. The game itself splits: **90 bundles, 7 856 MB, median 44.6 MB** **[M]**.
  Recommendation: one bundle per mod is right at mod scale (tens of MB); do not make it a rule above
  ~100 MB. Bake side is not the constraint — a **110 MB** bundle bakes and LZ4-repacks inside the
  gate's budget (commit `12793b4`) and LZ4 repack is 1.00x (R7-lz4).
- Runtime mount cost of a mod bundle: **UNMEASURED** (see §8).

---

## 6. Q5 — migration shape

**INCREMENT on route vii's install machinery; NEW code for the catalog writer.** Not a rewrite, and
route vii is not replaced.

- **`ppcontent.json`**: today a `replace` entry is `{ bundle, asset, texture|material|mesh }`. Add a
  SECOND shape keyed by what the author names, no new manifest language:
  - `"bundle"+"asset"` → route vii (sub-object surgery inside a patched copy) — unchanged.
  - `"key"` (a GUID or an address, e.g. `02_Bodyparts/ALN_Fireworm_BodyAll_Ready.prefab`) → key
    redirect: the mod authors the whole asset into its own `Dist\<bundle>` and the catalog points
    that key at it. Adds keep the existing add syntax and additionally get a catalog key.
  - The tool can REFUSE a `"key"` that is not in the catalog by name, offline, at bake time — the
    same `FindUnique` discipline.
- **Bake path**: already there. `BundleBaker` writes the mod bundle; U3d/U3e forge and add externals;
  U4/U5/U6 bake hierarchy, skin and clips. Nothing new is needed to PRODUCE the content.
- **What is genuinely new**: a `ContentCatalogData` writer — append internalId, append options block,
  append key blob + bump the `m_KeyDataString` count int, append bucket, append entries, mutate one
  entry's fields 0 and 2, refuse duplicate keys. It plugs into `Route7.ApplyOne`/`Rebuild` as another
  record kind; the ledger, backup, SHA-1 guard, revert and orphan-drop are reused verbatim.
- **Honest cost**: ~200-300 lines plus its offline self-check. This is E1 from
  `research-zero-runtime-replacement.md` §7, which was dropped to third when LZ4 killed route vii's
  disk cost. The reason to build it now is NOT disk — it is multi-mod granularity (§4) and mods that
  add rather than replace.

---

## 7. Q6 — what CANNOT go in the mod's own bundle (name them all)

1. **Video.** Not in bundles and not in Addressables at all: 69 loose `.webm` under
   `StreamingAssets\StreamableCopiedAssets\` + a plain-JSON side catalog read by
   `StreamableAssetsManager.Awake`. Route vii and route iii both do not apply. Stays
   `VideoCatalog.cs` (V1, commit `15d37a8`). **Never put video in the bundle.**
2. **Wwise streamed media (`.wem`).** Wwise's own file IO reads loose files under
   `<modDir>\WwiseAudio\` plus one setup-time `AddBasePath` (ARM1 / A2 / row 15a). Wwise cannot read
   out of a Unity archive.
3. **Wwise banks — allowed in the bundle, but not zero-runtime.** A `.bnk` ships fine as a binary
   TextAsset inside the bundle (A1, proven byte-identical), but it only reaches Wwise via one
   `LoadBankMemoryCopy` from `OnModEnabled`. Packaging convenience, not a native load path.
4. **Sub-object replacements inside shipped assets — the big one, measured.** These are **NOT
   catalog keys** **[M]**, so no key can name them:
   `fireworm_low_emissive` NOT A KEY · `fireworm_low_normal` NOT A KEY · `ALN_Fireworm_DMG` NOT A KEY
   · `ALN_Fireworm` NOT A KEY · `CHR_PX_ASS_TS_M_V01_02` NOT A KEY · `CHR_PX_ASS_TS_F_V01` NOT A KEY
   (0 keys even CONTAIN `CHR_PX_ASS_TS`). What IS a key is the owning addressable — e.g.
   `02_Bodyparts/CHR_PX_Assault_F_V01_Torso_Ready.prefab`; `px_assault_assets_all.bundle` owns 22
   addressable asset entries in total **[M]**. To reach P1/P3/P4/P5's targets by key redirect a mod
   must re-author the WHOLE owning prefab in its own bundle (possible — U4+U5+U3e — but a far
   heavier authoring job than "swap this texture"). **Route vii is the cheap and only surgical
   route, and must not be removed.**
5. **Non-addressable targets** — scene props, static geometry, anything with no `guid:` anchor
   (T26's case). Nothing loads them by address, so the catalog cannot reach them at all; only a
   patched copy of the serialized file does.
6. **Bundle file NAMES are not keys either** **[M]**: `aln_fireworm_assets_all.bundle` is NOT a key;
   the key is `aln_fireworm_assets_all_e0acf21a871681dd2c6bbe482ba68a40.bundle` (name + content
   hash). A writer that looks a bundle up by file name will miss — `Route7.FindInternalId` searches
   `m_InternalIds` (paths) and is right; a key-level writer must not copy that assumption.
7. **`m_DisableAudio`** (`globalgamemanagers` byte at absolute offset 7168) — a game-file flag, not
   an asset. ~~`tools\audio-flag.ps1`~~, deleted 2026-08-23 (gate A7): the tool decodes `.ogg`/`.mp3`
   itself, so nothing here writes to a game file any more.

---

> **BUILT AND MEASURED 2026-08-12.** §8 exists; §9's first two rows are closed. The verdict in §0
> survived first contact with a running game unchanged. See `PROVEN-FOUNDATIONS.md`, "Zero-runtime
> KEY publishing (route iii, gate C1)", for the pasted arms. Two things the offline analysis did not
> have: dependency ORDER is load-bearing (`LoadBundleFromDependecies` loads out of the FIRST
> `AssetBundleResource`, so the mod bundle must lead the set), and a mod that ADDS a key can be
> silently hijacked by a second mod's record unless the writer refuses a key claimed twice in one
> rebuild — §3.2's dup-key guard is necessary but not sufficient, because after the first ADD the key
> is no longer a duplicate, it is a REPOINT target.

## 8. The smallest gate that would PROVE it — **C1** (BUILT; specified here first)

One command `ct_catalog apply|verify|revert|selftest`, one restart, all arms in one `autogate` run.
This is E1 from `research-zero-runtime-replacement.md` §7, made falsifiable.

- **C1-boot** — the run reaching the console at all IS the proof the appended catalog parses.
  A duplicate key or a bad key-count int makes `CreateLocator` throw inside `InitializeAsync` and
  the game never boots, so this arm cannot be faked.
- **C1-add** — append a NEW key (`morgott.sample/probe_tex`) + an asset entry (provider 1,
  `internalId` = the swatch's name in the sample's `Dist\Sample.bundle`, `dependencyKeyIdx` = a new
  dep-set naming the mod bundle) + the mod bundle's own location entry WITH an
  `AssetBundleRequestOptions` block, `m_Crc` 0. After restart
  `Addressables.LoadAssetAsync<Texture2D>("morgott.sample/probe_tex")` must return the **8×8** swatch.
  Oracle is the SIZE, not null (shipped textures in that bundle are 1024).
- **C1-replace** — repoint the EXISTING key `02_Bodyparts/ALN_Fireworm_BodyAll_Ready.prefab`
  (entry 1618, internalId idx 1115, depKey bucket 8161 **[M]**) at a prefab in the mod's own bundle
  whose Material's `m_Shader` is an external PPtr into `_shaders_assets_all`, with the dep-set
  `[mod bundle, defaultlocalgroup_unitybuiltinshaders, _shaders_assets_all]`. Assert **both** that
  the loaded GameObject is OURS by name **and** that its shader reports
  `_PX_CHR/CHR_Character_shader` — one assertion settling key redirect AND "an external resolves
  when ADDRESSABLES did the mount", the open half of d4e1814. Negative identity
  `Hidden/InternalErrorShader` is the fail reading, per U3d/U3e discipline.
- **C1-ctl-sibling** — `02_Bodyparts/ALN_Fireworm_BodyAll_DMG_Ready.prefab`, untouched, must still
  resolve to the SHIPPED object by name. Positive identity, not absence.
- **C1-dupkey** (offline, `selftest`) — the writer must REFUSE appending a key that already exists,
  by name, in the V1-dupkey shape; plus a control that a correctly built catalog passes the same
  guard, plus a key-count arm (`8232 -> 8233`, and `m_KeyDataString[0..3]` bumped with it).
- **C1-ctl-revert** — restore from `.ct-backup` and assert SHA-1 byte-identity, as R7 does.
- **Falsification** — restore `catalog.json` by hand from `.ct-backup` with the mod bundle and the
  ledger left in place: C1-add and C1-replace must go RED while C1-ctl-sibling stays green.

Budget: one launch, plus the writer. Do NOT build the writer before C1's shape is agreed — the whole
architecture hangs on this one run, exactly as §7 of the research note said in the first place.

---

## 9. UNMEASURED — do not treat as proven

| Row | What is unknown | The run that would close it |
|---|---|---|
| ~~catalog-append~~ | **CLOSED 2026-08-12.** The game boots on a catalog carrying 8236 keys and serves an appended one: `C1-boot PASS` · `C1-add PASS … 'morgott.sample/probe_tex' to Texture2D 'swatch'` · `C1-add-size PASS it is 8x8`. §2-§3's layout is exact | done |
| ~~externals-under-addressables~~ | **CLOSED 2026-08-12, answer YES.** `C1-shader PASS … an external PPtr in the mod's own asset, mounted by ADDRESSABLES and by no code of ours, resolved to shader 'Standard'`. Falsified reading in the same arm with the catalog restored: `_PX_CHR/CHR_Character_Damaged_shader`. Caveat: it proves *an* Addressables mount satisfies the archive VFS, not that our dep-set entry specifically did it | done |
| bundle-mount-cost | Runtime memory / time cost of mounting one big mod bundle vs several small ones. Only the BAKE side is measured (110 MB inside budget, `12793b4`) | a `ct_bake`-hosted timing arm mounting one N-MB bundle and reporting `Profiler.GetTotalAllocatedMemoryLong` deltas; not run |
| options-fields | Only `m_Crc` is understood in an `AssetBundleRequestOptions` block. Whether a hand-written block needs correct `m_BundleSize` / `m_Hash` / `m_BundleName` for a LOCAL load is not measured — `GetLoadInfo` reads only `UseUnityWebRequestForLocalBundles`, and `LoadType.Local` reads only `Crc`, but `ComputeSize`/`BytesToDownload` also touch the block | **C1-add** with a minimal block (`m_Crc:0`, `m_BundleName`, everything else default) |
| key-count-int | That `m_KeyDataString[0..3]` must be bumped is read off `new object[BitConverter.ToInt32(keyData,0)]`; not observed failing | **C1-dupkey**'s key-count arm |
