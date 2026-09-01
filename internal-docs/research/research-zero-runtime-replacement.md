# Research — is zero-runtime native replacement physically impossible, or a policy we chose?

> **Status:** research note, 2026-08-12. Answers one question with evidence and maps the routes.
> Nothing here is a decision, a task, or a licence to implement. It does **not** amend FINAL-PLAN
> §39; it reports what §39's premises are worth.

**Short answer: it is a policy, not a physical limit.** One self-imposed line —
"no bake-time rewriting of Phoenix Point's own shipped serialized assets" (§34 last bullet, §39.1)
— is the sole reason visual replacement needs runtime binding. Every §39 conclusion downstream of
it is correct *given* it. Nothing in the Unity serialized-file format, the AssetBundle format, the
Addressables 1.18.15 catalog, or the game's own code forbids what the line forbids. The price of
lifting it is real and is stated in §6 below.

New measurements taken for this note are marked **[M]** — all offline, on shipped files, today.

> **AMENDED same day — read §9 before acting on §4's ranking or §6's price list.** The user has
> removed game-patch fragility as a cost that counts ("patches don't come out any more, and if one
> does we update the mod"). §1-§3 and §5 are unaffected; §4 and §6 are re-ranked in §9 against the
> two costs that *do* still count: safe failure/reversibility, and no redistribution of PP's data.

---

## 1. Rule-by-rule classification

| Rule | Actual text (quoted) | Class | Evidence |
|---|---|---|---|
| **§39.1** | "`MyMod.bundle` cannot rewrite a shipped Phoenix Point serialized object." … "Anyone who reads this section and proposes to make visual replacement fully load-native must first answer how a second bundle edits the first one. There is no known answer; do not invent one." | **(a) true as literally stated, (c) untested as an argument** | A second bundle file indeed cannot mutate another file's bytes at load. But the *question posed* is the wrong one: nothing requires a second bundle to edit the first. Two mechanisms bypass it entirely — redirect the game's own load (Addressables catalog, §3) or edit the shipped file at install time (§4 route i). Neither was considered when §39.1 was written; there is no measurement behind "there is no known answer". |
| **§10.1** | "Production materials do **not** use a durable external AssetBundle PPtr to a Phoenix Point shader. External PPtr depends on private details of foreign serialized files and is patch-fragile." | **(b) possible but rejected** | Stated reason = patch fragility. §10.3 confirms the status: "U3d (external shader PPtr) is **removed from the implementation sequence entirely** (D1). … parked as research only". That is a scheduling decision, explicitly not an impossibility. **[M]** the game itself ships exactly this construct (§2). |
| **§29.3** | "Before using a game bundle as a Mesh serialization/repack test subject, verify `m_Mesh.fileID == 0`. A previous false failure came from a prefab whose Mesh used `fileID = 1` into an unloaded external bundle." | **(c) misquoted — it bans nothing** | This is test hygiene, in the *methodology* chapter. Its actual content is the opposite of a prohibition: it says an external PPtr resolves null **when its bundle is not loaded** — i.e. it resolves fine when it is. §39.4 cites it ("banned by §10.1/§29.3") as if it forbade external references. It does not. |
| **§12.2** | "Do not port: old replacement orchestration; `PatchBank`; `Restage`; `StageOne`; RR base-path shadowing; … general dump infrastructure; scene scanning except narrowly required developer diagnostics." | **(b) policy, and about something else** | A ResourceReplacer *porting* list, already re-scoped by §39.8. Says nothing about bundles, catalogs or game files. Not load-bearing for this question. |
| **§34** | "no game-wide automatic replacement of arbitrary Unity references"; "no bake-time rewriting of Phoenix Point's own shipped serialized assets (§39.1)." | **(b) policy — and circular** | The second bullet was *added by* §39 ("§34 gains one non-goal", §39.8) and cites §39.1 as its justification, while §39.1 is itself unmeasured. The whole ban rests on one unverified sentence. The first bullet is orthogonal: a finite authored table is explicitly not what it forbids (§34's own closing paragraph). |
| **§39.4 animation verdict** | "the binding is an `AnimatorOverrideController` over the GAME's controller, which in a baked asset means an external PPtr into a foreign serialized file: banned (§10.1, §29.3) … there is no bake for it" | **(c) untested assumption** | The premise (it needs an external PPtr) is right. The conclusion ("banned", "no bake") inherits §10.1's *policy* and §29.3's *misreading*. At format level `AnimatorOverrideController.m_Controller` is a PPtr like any other and can carry `fileID != 0`. Unproven that it works in PP; not proven impossible either. |

---

## 2. External PPtrs across serialized files — the format, and what PP already does **[M]**

Format (public): a SerializedFile's metadata holds an externals table; a `PPtr` is
`{ fileID, pathID }`; `fileID == 0` means "this file", otherwise it indexes the externals table
1-based; each external entry identifies the target SerializedFile by internal path, canonically
`archive:/CAB-<hash>/CAB-<hash>`; for non-scene bundles the CAB name is the **MD4 hash of the
AssetBundle name**; `pathID` is the target object's local id inside the resolved file
([UnityDataTools assetbundle-format.md](https://github.com/Unity-Technologies/UnityDataTools/blob/main/Documentation/assetbundle-format.md)).

Measured on shipped Phoenix Point data (AssetsTools.NET 3.0.0.0, `ContentTool\lib\AssetsTools.NET.dll`,
loaded in PowerShell against `…\StreamingAssets\aa\StandaloneWindows64\`):

```text
mutoid_assets_all.bundle       CAB-35c6207d8d79fb22e17ef121965f6b14   unity=2019.4.31f1 assets=93
  externals=1  [1] guid=00000000000000000000000000000000 type=Normal
                   path='archive:/cab-207b1100b7c0eac21654e77dc25fa206/cab-207b1100b7c0eac21654e77dc25fa206'
_shaders_assets_all.bundle     CAB-22f8ff865f4ca3fac668dbcaedfdbb9d   assets=58   externals=1 (-> builtin shaders CAB)
defaultlocalgroup_unitybuiltinshaders.bundle  CAB-207b1100b7c0eac21654e77dc25fa206  assets=13  externals=0
an_civilian_assets_all.bundle  CAB-9958589db90d72242cae7fe86d17cfb4   assets=834  externals=3
  [1] archive:/cab-229815d8dce2904e975e679b608088b9/…   [2] -> _shaders CAB   [3] -> builtin-shaders CAB
dlc5_assets_all.bundle         CAB-d12810a5c2815cb66bc7d12803d2c57b   assets=3    externals=0
```

Conclusions, format-level:

- A hand-baked bundle **can legally carry a reference into a shipped bundle's object**. It is not
  an exotic construct — it is how every PP character bundle reaches the shared shader bundle.
- **Identity required = CAB path + pathID. No GUID.** Every shipped external carries an all-zero
  GUID; the path string is the whole identity. Path is lowercase in the reference, mixed-case in
  the archive listing — Unity matches case-insensitively here.
- **CAB stability**: derived from the bundle's internal name (MD4). Stable across a game patch
  unless Snapshot renames the bundle.
- **pathID stability**: **[M]** PP's pathIDs are 64-bit hash-shaped, not sequential
  (`-9158346901105743875`, `-8828344512014435907`, … in `mutoid_assets_all.bundle`), i.e. Scriptable
  Build Pipeline deterministic object ids derived from asset GUID + local file id. Unity states a
  rebuild keeps the same ids ([determinism docs](https://docs.unity3d.com/6000.0/Documentation/Manual/build-deterministic-assetbundles-addressables.html)).
  So a forged external survives a game patch **unless that specific asset is re-authored**. The
  fragility §10.1 feared is real but bounded and detectable (hash the target bundle at bake time).
- **Live requirement**: the target archive must be mounted when our object deserializes. That is
  what §29.3's "false failure" actually recorded. Addressables satisfies it by loading a location's
  dependency bundles first — so the dependency must be declared in the catalog entry (§3).

What this does **not** prove: that Unity's runtime accepts an external we forged *by hand* (right
byte layout, right table order, right `old_type_hash` interplay). Foundations #5 shows the loader
already tolerates a faked `old_type_hash`, which is encouraging, not conclusive. Experiment **E3**
(§7) settles it in one run.

---

## 3. Does PP use an Addressables catalog at runtime? Yes, and it is a plain file **[M]**

```text
…\PhoenixPointWin64_Data\StreamingAssets\aa\catalog.json     1 670 824 B   (plain JSON, on disk)
…\aa\settings.json  ->  m_AddressablesVersion "1.18.15"
                        m_CatalogLocations = [ {RuntimePath}/catalog.json ]   (one, local)
                        m_IsLocalCatalogInBundle = false    m_SettingsHash = ""    (no hash gate)
```

Game code: `Addressables.InitializeAsync()` —
`decompiled\AssemblyCSharp\Assembly-CSharp\src\PhoenixPoint.Common.Game\PhoenixGame.cs:738`.
Default init, no custom provider, no `InternalIdTransformFunc`, no `LoadContentCatalogAsync`
anywhere in `Assembly-CSharp` (grep: the only other Addressables hits are
`Base.Assets\AssetsManager.cs:407` iterating `Addressables.ResourceLocators` and
`UnityTools\RootkitController.cs:138` `ClearResourceLocators`).

**Ordering, decisive:** `Addressables.InitializeAsync()` is at `PhoenixGame.cs:738`;
`InitMods()` is at `PhoenixGame.cs:758`, in the same `FirstRunCrt`. **Mods load after the catalog
is already parsed.** A mod cannot supply or amend a catalog before the game's own locator exists.
Any catalog change is therefore an *on-disk, pre-launch* edit — which is exactly why it can be
zero-runtime.

Catalog structure, decoded **[M]** (matches Addressables 1.18 `ContentCatalogData`):

```text
m_InternalIds   4119 strings   e.g. "{UnityEngine.AddressableAssets.Addressables.RuntimePath}\StandaloneWindows64\an_civilian_assets_all.bundle"
m_ProviderIds   2              AssetBundleProvider | BundledAssetProvider
m_KeyDataString    509 072 B -> 8232 keys  (4029 look like 32-hex asset GUIDs, 727 "Assets/…" paths, plus bundle names and Hash128 dependency-set keys)
m_EntryDataString  163 804 B -> 5850 entries, 7 int32 each:
                   { internalIdIndex, providerIndex, dependencyKeyIndex, depHash, dataIndex, primaryKeyIndex, resourceTypeIndex }
m_BucketDataString 113 824 B -> key -> entry-index list
m_ExtraDataString   76 240 B -> per-bundle AssetBundleRequestOptions JSON
```

Worked example **[M]** — GUID key `671f5acd4da022c4596f60893683abbc`:

```text
entry 90 -> internalId "Assets/Art/Characters&Equipment/00_Common/01_HumanBody/CHR_Human_Rig.fbx"
            provider  BundledAssetProvider
            depKey    Hash128 (key type 4) whose location list is the bundle entries to load first
            primaryKey "00_Common/01_HumanBody/CHR_Human_Rig.fbx"
```

So: **GUID → (asset path inside a bundle, provider, which bundles to load)** is an editable table.
Redirecting one GUID to our bundle = repoint that entry's `internalIdIndex` at our asset name and
its `dependencyKeyIndex` at a key whose locations are our bundle. All three blobs are
length-prefixed and re-serializable; a reader/writer is a few hundred lines (this note's decode
was ~40 lines of PowerShell).

**CRC gate, and it is real [M].** Decompiling the game's *own shipped*
`PhoenixPointWin64_Data\Managed\Unity.ResourceManager.dll`
(`ilspycmd -t UnityEngine.ResourceManagement.ResourceProviders.AssetBundleResource`):

```csharp
// line 282 of the decompiled output
CompleteBundleLoad(AssetBundle.LoadFromFile(m_TransformedInternalId, (m_Options != null) ? m_Options.Crc : 0u));
// line 285
m_RequestOperation = AssetBundle.LoadFromFileAsync(m_TransformedInternalId, (m_Options != null) ? m_Options.Crc : 0u);
```

and the catalog supplies a non-zero CRC per bundle, e.g. for `_common_assets_all`:

```text
{"m_Hash":"07c9e6267a7063009e1c026682818384","m_Crc":3811850443,"m_BundleName":"5ee9bb8f7c8d4d6fc84c28e9d94efcd6",
 "m_BundleSize":440489098,"m_UseUWRForLocalBundles":false,…}
```

Unity: the crc argument is "an optional CRC-32 checksum of the uncompressed content. If this is
non-zero, then the content will be compared against the checksum before loading"
([2019.4 docs](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/AssetBundle.LoadFromFile.html)) —
**zero disables the check**. Consequence: patching a shipped bundle in place *requires* also
editing `m_Crc` (recompute, or set 0) in `catalog.json`. A patcher that edits the bundle and not
the catalog fails loudly — which is a good failure mode, and is the control arm of experiment E2.

---

## 4. Every route to zero-runtime native replacement

"Zero-runtime" = the shipped mod contributes no code that performs the replacement. An
**install-time** step that edits files before the game starts is not runtime.

| # | Route | Verdict | Mechanism | Breaks on | Cheapest experiment |
|---|---|---|---|---|---|
| i | **Patch the shipped bundle in place** at install/bake time | **PLAUSIBLE-UNTESTED** (components near-proven) | AssetsTools.NET rewrites the object inside `…_assets_all.bundle`; also set that bundle's `m_Crc` to 0 (or recompute) and `m_BundleSize` in `catalog.json`. Foundation #2 already proves a repacked *game* bundle loads and its pixels reach the GPU — via `LoadFromFile`, not yet via Addressables. | any game patch touching that bundle; Steam "Verify integrity of game files"; two mods patching the same bundle; rewriting a 250-800 MB bundle costs minutes and disk | **E2** (§7) |
| ii | **Load-order / name / path precedence** — ship a bundle the game picks instead of the original | **IMPOSSIBLE** without editing the catalog | Addressables 1.18 has no overlay or search path: `m_InternalIds` is the only mapping and it is absolute-by-token. Unity refuses to mount two archives with the same CAB name, so a same-CAB shadow does not override, it collides. Winning the race by pre-mounting requires runtime code *and* breaks the real bundle's load. | — | none worth running |
| iii | **Addressables catalog redirect** | **PLAUSIBLE-UNTESTED — best route** | Rewrite one entry: GUID → our asset's internal id + our bundle as its dependency; append our bundle's location. The game's own `Addressables` then natively loads our file. Zero runtime, no game-bundle rewrite, mod content stays in the mod folder. **Limit:** governs only top-level Addressables loads (by GUID/address) — a PPtr *inside* a shipped prefab is not catalog-resolved, so this replaces whole addressable assets (e.g. a skin prefab), not a mesh buried in one. | game patch replaces `catalog.json`; Steam verify; last-writer-wins between mods; needs a `ContentCatalogData` writer | **E1** (§7) |
| iv | **Forged external PPtr from our bundle into the game's** | **PLAUSIBLE-UNTESTED; format-legal and used by the game itself** | Our serialized file declares `archive:/cab-<md4>/cab-<md4>` in its externals table; our objects reference the game's objects by `{fileID=n, pathID=<64-bit>}`. This is what makes route iii *cheap*: our replacement prefab keeps the game's skeleton, materials and `RuntimeAnimatorController` by reference and changes only what we replace. | Snapshot re-authoring that specific asset (pathID moves) or renaming the bundle (CAB moves) — both detectable at bake time by hashing the target | **E3** (§7) |
| v | **A sanctioned PPModLoader override hook** | **IMPOSSIBLE — there is none** | `ModLoaders\` is empty; no doorstop/BepInEx; mods are official `PhoenixPoint.Modding` assemblies `Assembly.Load`ed with no `AssemblyResolve` (foundation O5, `ModSDKContext.cs:51-63`) and initialised at `PhoenixGame.cs:758`, i.e. **after** `Addressables.InitializeAsync()` at `:738`. There is no pre-asset-load seam and no asset-override API. | — | none |
| vi | **Binary patching of game binaries** (`UnityPlayer.dll`, `Assembly-CSharp.dll`) | **POSSIBLE, strictly dominated** | Could hook the loader or inject an early catalog. Same update fragility as i/iii, far higher risk, and buys nothing the catalog does not already give at a fraction of the cost. | every game patch; Steam verify; EAC/anti-tamper surface | do not |

Also considered and rejected as a *route*, though proven as a *mechanism*: the audio path
(foundations #12/#13). Wwise resolves media by ID at bank-load, so replacement needs no object
edit — but the bank still has to be handed to Wwise by `LoadBankMemoryCopy` from the mod's
`OnModEnabled`. That is one init call, not a replacement engine, and it is the floor: going below
it would mean patching PP's shipped `.bnk` files (physically possible — no checksum, no signature,
`dwProjectID` never read — but explicitly dead by policy, §39.8).

---

## 5. Per-kind reachability

Best route = **iii + iv applied at install time** (catalog redirect to our bundle; our bundle
external-PPtrs everything we did not replace).

| Kind | True zero-runtime reachable? | Why, at format level |
|---|---|---|
| Wwise media replacement | **Almost** — resolution is native (media ID), one `LoadBankMemoryCopy` call remains | to remove the call you must patch a shipped `.bnk`; possible, banned by policy |
| Texture2D inside a shipped prefab | **YES** via i (rewrite the texture object) or iii+iv (replace the owning addressable asset) | nothing in the format objects; it is a byte rewrite of a file we are choosing not to touch |
| Mesh (static and skinned) | **YES**, same two ways | same |
| Material | **YES**, and iv removes the whole §10.1 donor-shader dance: `m_Shader` becomes an external PPtr into `_shaders_assets_all` (`cab-22f8ff865f4ca3fac668dbcaedfdbb9d`) | the game's own bundles do exactly this **[M]** |
| AnimationClip + Animator binding | **YES** — `AnimatorOverrideController.m_Controller` is an ordinary PPtr and may be external; `m_Clips` is a list of (original PPtr, override PPtr) pairs, i.e. exactly a baked override table | §39.4's "not bakeable at all" is false at format level; it is true only under the no-game-file-edits rule |
| A scene prop / static-batched geometry with no addressable GUID | **YES via i, NO via iii** | it is not reachable by the catalog because nothing loads it by address — but it *is* inside a shipped serialized file, so an in-place rewrite reaches it. Note this is the one place where the shipping-mode gap of §39.4 is genuinely closed rather than moved |

**Nothing is out of reach for format reasons.** If the "don't touch game files" line stays, the
inverse is equally absolute: **every** visual kind is unreachable zero-runtime and §39 is exactly
right. The whole question collapses to that one line.

---

## 6. The price, stated plainly

- **Per-update re-bake.** A Steam patch replaces `catalog.json` and any bundle it touches; the mod
  silently reverts to vanilla (best case) until re-applied. An external PPtr into a re-authored
  asset is worse than a revert: a wrong or null object, no exception, visible as corruption.
- **Steam "Verify integrity of game files" undoes everything**, with no signal to the user.
- **The install step must run outside the game** (or as a first-run patcher that then demands a
  restart — code, but *installer* code, not replacement code, and it never runs during play).
- **Mods conflict on shared state.** `catalog.json` is one file; two mods that both rewrite it need
  a shared patcher with a merge model, or the second overwrites the first. This is the real
  architectural cost, larger than the format work.
- **Distribution shape changes.** A Workshop item today is a mod folder; this needs a patcher and a
  backup of the original files, plus an uninstall that restores them.
- **Larger install** for route i (a patched copy of a multi-hundred-MB bundle) — route iii avoids
  this, which is the main reason it is the better one.
- **Vs. the §39 status quo**, the runtime cost being bought out is *one dictionary miss per skin
  prefab resolve* (§39.4). That is the honest comparison: the current design's runtime cost is
  already very small, and the price above is not.

---

## 7. The three cheapest experiments

**E1 — catalog redirect works or dies in one run** (settles route iii; the whole architecture hangs
on it). Offline: append our bundle to `m_InternalIds` + a location entry; repoint one small
addressable GUID's entry at an asset in our bundle. Launch, open the screen that shows it.
*Control in the same run:* a sibling GUID left untouched must still show vanilla, and the game must
reach the main menu at all (a catalog Addressables refuses to parse fails everything, loudly).
Keep a byte copy of the original `catalog.json`. Cost: a `ContentCatalogData` reader/writer
(~200 lines; the decode is already done in §3) + one launch.

**E2 — in-place bundle patch + the CRC gate** (settles route i, and proves the decompiled CRC path
is what actually runs). Repack `mutoid_assets_all.bundle` (175 599 B — the smallest real content
bundle) with one texture forced magenta.
*Arm A:* set that bundle's `m_Crc` to `0` in `catalog.json` → expect the magenta to appear.
*Arm B, the control, same session:* restore the original non-zero `m_Crc` with the patched bundle
still in place → expect a hard load failure. Both arms must be logged; Arm A passing alone would
not distinguish "CRC ignored" from "CRC recomputed by luck". Cost: ~1 hour, no new tooling.

**E3 — does a hand-forged external PPtr resolve?** (settles route iv, and simultaneously unparks
U3d and the animation verdict — and it needs **no game-file edit at all**, so it can run inside the
existing `ct_bake` harness today). Bake a Material whose `m_Shader` is `{fileID=1, pathID=<a real
shader pathID read out of _shaders_assets_all.bundle>}` with externals entry
`archive:/cab-22f8ff865f4ca3fac668dbcaedfdbb9d/cab-22f8ff865f4ca3fac668dbcaedfdbb9d`. Load our
bundle with `AssetBundle.LoadFromFile` in a session where the game has already loaded its shaders,
and log `mat.shader != null` + `mat.shader.name`.
*Controls in the same run:* (1) the existing U3a shaderless material, which must still report a
null shader — otherwise the reading is an artefact; (2) log whether `_shaders_assets_all` is
actually mounted, because a null result with the archive unmounted is §29.3's false failure, not a
verdict.

If E3 passes and E1 passes, zero-runtime replacement is available for every kind in §5, at the
price in §6. If E3 passes and E1 fails, route i is the only path and the install cost goes up. If
E3 fails, §39's architecture is right for material and animation regardless of what E1 says.

---

## 8. What this note does not claim

- Not measured: that any edited catalog loads; that any patched game bundle loads through
  Addressables; that a forged external resolves at runtime. All three are UNPROVEN and each has its
  experiment above.
- Not measured: how many replacement targets are addressable-GUID-reachable versus buried inside a
  prefab. That ratio decides how much of route iii's limit bites, and it is the same coverage
  number §39.6's R-U1 probe was already going to produce.
- Not addressed: Steam Workshop rules on shipping a game-file patcher. That is a policy question
  for the user, not a technical one.

---

## 9. Re-ranking — patch fragility no longer counts (amendment, same day)

User ruling: game updates are not a cost. Do not down-rank a route for pathID/offset/layout drift;
a re-bake after an update is expected and cheap. Judge on: does it work · zero-runtime at play time
· author/player cost · reversibility and safe failure · **does it redistribute PP data** (these mods
ship on Steam Workshop, so a route may rewrite the *player's own local files* but must not carry
Phoenix Point's assets inside the mod).

**What the removal changes:** §10.1's entire stated objection to external PPtrs was patch fragility
— it is now void, and U3d should be unparked. §6's first two bullets (per-update re-bake, silent
revert) drop out of the price. Nothing in §1, §2, §3 or §5 changes; the physical/policy verdict is
unaffected.

**One constant, and it drives everything below:** mods initialise at `PhoenixGame.cs:758`, after
`Addressables.InitializeAsync()` at `:738`. So *every* zero-runtime route needs an install-time edit
of a player-local game file. Reversibility is therefore not optional — back up what you touch, and
make a half-finished install detectable and undoable. The smallest safe footprint wins.

### New route vii — patch a private copy, repoint one catalog string. Now the top route.

At install, on the player's machine: read their own `<target>_assets_all.bundle`, write a patched
copy into the **mod folder**, then in `catalog.json` repoint that bundle's `m_InternalIds` string at
the copy and zero its `m_Crc`.

Why it wins on the new criteria:

- **No forged pathIDs, no external PPtr, no catalog blob surgery for the entry table.** Every
  reference inside the patched bundle stays internal, because we patched a whole copy of the file.
- **Coverage equals route i** — reaches non-addressable scene props and sub-objects buried in
  prefabs, which route iii cannot.
- **No redistribution**: the copy is generated from the player's own install, at install time. The
  mod ships only the author's own content plus the patcher.
- **Reversibility is one small file**: the game's bundles are never written. Restoring a backed-up
  `catalog.json` (1.6 MB) fully un-does it, and Steam's own verify restores it for free. A crash
  mid-install leaves at worst a stray file in the mod folder.
- **Mechanically clean [M]**: a plain absolute path in `m_InternalIds` needs no token expansion, and
  the shipped `Unity.ResourceManager.dll` routes it straight to a local load — `TransformInternalId`
  returns `location.InternalId` unchanged when no `InternalIdTransformFunc` is set (PP sets none),
  `ShouldPathUseWebRequest` is `path?.Contains("://")` which a Windows path fails, so
  `GetLoadInfo` yields `LoadType.Local` → `AssetBundle.LoadFromFile(path, Crc)`.
- Cost: **disk**. A copy of the whole target bundle — trivial for `mutoid` (175 KB), 553 MB for
  `nj_equipment`. That is the only reason route iii still exists.
  **Superseded 2026-08-12:** that cost was our uncompressed write, not the route. Repacking with the
  source's own LZ4 gives 175 838 B from a 175 599 B source — **1.00x**. Route iii has no remaining
  reason to exist and is not to be built; see FINAL-PLAN §39.4.

### Ranking now

| Rank | Route | Why here | Redistributes PP data? | Reversibility |
|---|---|---|---|---|
| **1** | **vii — local patched copy + one internalId repoint** | best coverage-per-complexity; only `catalog.json` is written | **No** | restore one 1.6 MB file, or Steam verify |
| **2** | **iii + iv — entry redirect to our own small bundle** | the size-efficient variant; use when the target bundle is huge. iv is now unobjectionable and shrinks our bundle further by referencing the game's untouched skeleton/materials/controller | **No** — a PPtr is a reference, not a copy. iv is in fact the *anti*-redistribution route | same: only `catalog.json` |
| **3** | **i-b — patch the player's bundle in place** | works, same coverage as vii, but writes multi-hundred-MB game files: needs a backup copy (double disk) and a half-written bundle is a broken install until Steam verify | No | worst of the three; recovery needs Steam verify |
| — | **i-a — ship a pre-patched game bundle** | **DISQUALIFIED**, and no longer for fragility: it puts Phoenix Point's assets inside a Workshop item | **YES** | n/a |
| — | **ii, v** | unchanged: no overlay in Addressables; no PPModLoader seam exists | — | — |
| — | **vi — binary patching game binaries** | still dominated: buys nothing over the catalog and is the one route whose failure mode is an unlaunchable game | No | poor |

### Experiments, re-ordered

**E3 first** — it is unchanged, needs no game-file edit, runs in `ct_bake` today, and now settles a
question with no remaining objection behind it (external PPtr / U3d / the animation verdict).

**E2' replaces E1 as the second run** — it is E2 plus one string, and it settles route vii whole:
write a patched copy of `mutoid_assets_all.bundle` (175 599 B) with one texture magenta into the mod
folder, repoint that bundle's `m_InternalIds` at the absolute path, set its `m_Crc` to `0`.
*Controls, same session:* (a) the original non-zero `m_Crc` with the repoint in place must fail to
load — otherwise the CRC reading is unearned; (b) a sibling bundle untouched must render vanilla;
(c) restore the backed-up `catalog.json` and confirm the game is byte-identical vanilla again, which
is the reversibility claim and must be measured, not assumed.

**E1 drops to third** — the entry-level redirect and its `ContentCatalogData` entry/key writer are
only needed once a target bundle is too big to copy. E2' already proves the catalog is writable.

Sources: [UnityDataTools AssetBundle format](https://github.com/Unity-Technologies/UnityDataTools/blob/main/Documentation/assetbundle-format.md) ·
[AssetBundle.LoadFromFile (2019.4)](https://docs.unity3d.com/2019.4/Documentation/ScriptReference/AssetBundle.LoadFromFile.html) ·
[AssetBundle and Addressables determinism](https://docs.unity3d.com/6000.0/Documentation/Manual/build-deterministic-assetbundles-addressables.html) ·
shipped `Unity.ResourceManager.dll`, `catalog.json`, `settings.json`, the `aa\StandaloneWindows64\`
bundles, and `decompiled\AssemblyCSharp`.
