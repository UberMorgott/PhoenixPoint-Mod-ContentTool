# "Replace one mesh" wizard — one button from a green Doctor verdict to a shipped project — design

Status: **v2, 2026-09-02**, HEAD `57226cd`. Owner decisions fixed before writing; recorded, not re-opened. Peer review: Codex
memo `e4203269...out.md` (facts-file corrections, §3) and the FIX-THEN-SHIP memo `53fa6109...out.md`, **both accepted in
full** — findings 1-10 of the second are applied to §§3-10 (preview-aware fingerprint §4.5, idempotent reuse §4.2, pre-write
R3 §4.2/§7, manifest-first meta §4.2, per-branch target refusals §4.1/§6, `ApplyDisposition` §4.3, the stage order §7, R2's
empty-folder arm §4.2/§8, R12 from observed state §6, and the `ContentMods.Enabled:50-58` citation §3). Builds on the shipped manifest core
(`2026-09-02-manifest-core-design.md`). Next slice: the lifecycle dashboard (Validate/Bake/Apply/Verify/Package, progress).
**2026-09-05, Codex DEEP review of the PLAN** (`ec5951d5…out.md`, 19 findings + a sequencing paragraph): 17 accepted and
folded into §§4.1-4.5, §8 and §10 here — path normalization and a strict parse before `MetaRefusal` (§4.2), absent-only
creation (§4.2), `Register`'s residency-first precedence (§4.3), the generation cancel and `Dispose` clearing (§4.4),
R16 reserved for a walk that ran (§4.1) — and 2 rejected: **11** (the bench install has its own profile — Instance2 `…592` since 2026-09-05 — so a
`MOD_ACTIVATED` edit there cannot reach the user's `…591`) and **19** (the id is quoted through `JsonWriter`; the
template body stays a fixed literal, §4.2). Execution order is now **2 → 3 → 5 → 4 → 6 → 7 → 8**, numbers unchanged.

## 1. Goal
On the bench's MODEL DOCTOR tab, with a prototype slot picked (`PrototypeTarget`, `Mode==Replace`, live SMR), a GLB picked,
aliases set and a green `ReplacementPreflight` verdict, **one button** produces a real mod folder beside `Mods\ContentTool\`
holding `ppcontent.json` + `meta.json` + `Content\Meshes\<stem>.glb` + its alias sidecar, the shipped bundle patched, and an
honest line saying what the player must still do — no console command typed at any point.

## 2. Non-goals (this slice ships none)
- `VerifyMode.Extend`; texture / material / clip / video rows; any target override field. Forced Addressables release or
  bundle reload; a `PrototypeBaySession` re-show to "make it visible now".
- Packaging, progress bars, cancellation, separate lifecycle buttons — next slice. Editing `MOD_ACTIVATED` or the manager's
  state; deleting or repairing a project; editing/removing an existing `replace` row (the manifest core is add-only, §2).

## 3. Current state
| What | Where | Why it matters here |
|---|---|---|
| row → shipped asset | `ProjectBake.Patch` `:1490`; bundle CI `:1534`; `baker.WhyNot(AssetClassID.Mesh, r.asset)` `:1588`; stem `FindMesh` `:1581`; path `BakeSelfCheck.ShippedBundlePath` `:735` (used `:1499`) | the exact pair the wizard must produce, and the only proof it is right |
| name → asset | `AssetIndex.FindUnique` `:107` / `WhyNot` `:129`; wrapper `BundleBaker.WhyNot(AssetClassID, string)` `:543` (ctor `:43`, `IDisposable`) | ordinal `m_Name`, refuses a non-unique name — the disk oracle |
| slot → renderer → addon → bundles | `FitBench.LiveSlots` `:768` (owning `Addon a` `:776`), `Retarget` `:739`, `SlotTargets` `:698`; `Addon.AddonDef` `:167`, `VisualsSourcePrefab` `:179`, `AddonDef.SkinData` `:85`, `AssetsManager.GetAssetReferencesFromObject` `:316` (internal instance); `BundleLive.Locate` `:195` (**private**), `Consider` `:222`, `BundleClaims.Matches` `:191`; carrier `PrototypeTarget` `:30-43` | the Addon is in hand and dropped today; it owns the dependency graph, and `Locate` is the pattern for `Addressables.ResourceLocators` → `IResourceLocation.Dependencies`. `PrototypeTarget` has no bundle/asset field yet |
| project dir + discovery | `ContentMods.ProjectDir` `:143-147`, `Sibling` `:123-129`, `ContentToolMain.ProjectDir` `:38`; the EXECUTABLE exclusion `ContentMods.Enabled:50-58` → `ModGate.Decide:38`→`Unknown`, `Why` `:62` (`ContentMods.cs:102-105` is the comment that EXPLAINS it, not the code that performs it) | a sibling holding `ppcontent.json` wins, else `Mods\ContentTool\<name>`; a folder with no `meta.json` is never applied |
| meta.json, validated | `Package.EngineId` `:35`, `Package.MetaRefusal` `:313-329` (`ID` `:316-318`, the `Dependencies` regex `:319-322`, `AssemblyName` only when it NAMES a staged file `:323-327`); `ModMeta.cs:33-46` | the wizard writes a meta only when there is none, and REUSES `MetaRefusal` — with `stagedFiles == null`, so the assembly arm cannot fire — to refuse one it must not ship behind |
| bake | `ProjectBake.Run(string, out int)` `:63`; replacement-only exit `:103-119`; `PatchCache.Key` `:43/:49` (manifest SHA1 + path/size/mtime stamps) | a mesh-only project ends `ct_project: ALL PASS - this project has no bundle of its own; the patched copy(ies) above are the whole output` |
| apply + residency | `Route7.ApplyProject` `:205` (**private**), freshness `:228-232`, bake `:249-256`, install `:280`; `BundleLive.Register` `:74`, refusal `:88-92`, `Holds` `:145`, build-name note `:230-238` | re-bakes when stale but **continues after a failed bake**; a rendered target is resident → refusal, not a live swap |
| doctor | `Path` `:61`, `Renderer` `:62`, `Prototype` `:68`, `Ready` `:69`, `Busy` `:78`, `Enqueue` `:228`, `Tick` `:360` (from `FitBench.Update` `:2106`), buttons `:1248-1262`, `Draw` `:1182` | where the button and its intent live |
| verdict | `ReplacementPreflight.Run(byte[], string, PrototypeTarget)` `:50`, `Baked` is **`BakedSkin`** `:19`, `Outcome` = `ByName\|NearestBone\|NotRigged\|Refused` (`ReplacementDecision.cs:6-16`), `DiagnosticReport.Count` `:56` | Doctor ≡ bake is a hard rule |
| fingerprint | `RigTarget` `SkinCompatibility.cs:57-83`, `SameAs` `:70`; `ModelDoctor.Snapshot` `:242-261`, `preview` `:74`, `HasPreview` `:79`, the swap `Renderer.sharedMesh = candidate` `:451`, `origin` restored by `Revert` `:461` | `SameAs` compares `MeshInstanceId` **and** `MeshName`, and `Target` is snapshotted BEFORE the preview swap — so with a preview live, `SameAs` is false by construction (§4.5) |
| writers | `Manifest.AddMeshReplacement` `:167`, `Validate` `:184`, `ManifestFile.Load` `:274` / `Save` `:318`, `AtomicFile.Write/WriteText` `:17/:45`, `AliasMap.SaveSidecar` `:234`, `LoadSidecar` `:155/:163`, `Sha256` `:137` | every byte this wizard writes goes through these |

**Corrections to the facts file**, each opened and checked:
- Declarations were one line early (they pointed at the doc comment): `FitBench.Draw` **1616**, `Retarget` **739**,
  `ShowPrototype` **676**; `ModelDoctor.PickFile` **98**, `PickTarget` **112/134**, `Enqueue` **228**, `DoPreview` **409**,
  `DoSave` **471**, `Draw` **1182**; `ProjectBake.Patch` **1490**; `Route7.ApplyProject` **205**; `SaveSidecar` **234**.
- `ContentMods.ProjectDir` does **not** yield `Mods\<name>` for a new name — only a sibling that already holds
  `ppcontent.json` wins (`Sibling:128`), else `Mods\ContentTool\<name>` (`:147`); it validates no name → the wizard creates
  the sibling itself (§4.2). `ReplacementPreflightResult.Baked` is `BakedSkin` (`:19`), not `ModelBuild`. `ProjectBake.Run`
  does not always end `ALL PASS - <out>`: a replacement-only project returns at `:103`.
- `PatchCache.Key` hashes the manifest but stamps `Content\` files and shipped bundles by path/size/mtime (`:43/:49`) — a
  same-size same-mtime overwrite is invisible to it, so the wizard never overwrites (§4.2). "Apply = LIVE, no restart" is
  false for this target: `BundleLive.Register` refuses at `:88` when the bundle is resident, and it always is here.
- The `ppcontent.json`-only exclusion EXECUTES at `ContentMods.Enabled:50-58`, which asks `ModGate.Decide:38` for every
  candidate folder and gets `Unknown` for one the manager never discovered (`Why:62`). `ContentMods.cs:102-105` and
  `ModRoster.cs:317-321` are COMMENTS explaining that rule, not the rule. `CatalogLive` is the loose-file route and locates no
  serialized Mesh, and `BundleLive.Locate` is private (`:195`) — so §4.1 writes its own dependency-oriented walk.

## 4. Design
### 4.1 Target derivation — addon graph, proved on disk (`src\Doctor\ShippedTarget.cs`, new)
Never `AssetBundle.GetAllLoadedAssetBundles() + Contains`: a loaded bundle's `name` is the build identity, not the shipped
file name (`BundleLive.cs:230-238`); meshes are commonly sub-assets; a global scan loses the owning dependency graph.
```csharp
// Null on success; the refusal sentence otherwise. Fills target.ShippedBundle/ShippedAsset.
internal static string ShippedTarget.Resolve(Addon addon, SkinnedMeshRenderer smr, PrototypeTarget target);
```
`PrototypeTarget` gains `internal string ShippedBundle, ShippedAsset, TargetRefusal;`. `FitBench.LiveSlots` `:768` keeps the
owning `Addon` (local `a`, `:776`) beside each renderer — its value becomes a `KeyValuePair<Addon, SkinnedMeshRenderer>` — and
`Retarget` `:739` calls `Resolve` per slot, storing the refusal instead of throwing.
1. `asset = smr.sharedMesh.name` — a CANDIDATE until step 4 answers. No live mesh at all → **R14**.
2. Collect every `AssetReference` reachable from `addon.AddonDef.SkinData` through the game's own
   `AssetsManager.GetAssetReferencesFromObject` (`:316`, internal instance → reflection; its public-field walk is small enough
   to copy if that breaks). Keep the one whose `.Asset` is `ReferenceEquals` to `addon.VisualsSourcePrefab` (`:179`).
   No `SkinData` or no prefab → **R15**; zero matches → **R16**; several distinct `AssetGUID`s → **R17**.
   **R16 is reserved for a walk that RAN and matched nothing.** No `AssetsManager` component, no reflected method, or a
   null reflection result are the TOOL's own footing giving way, not a statement about this addon's data — they THROW,
   so the outer catch answers **R22** with the stack in `Player.log`. Folding them into an empty collection printed R16
   and sent the author to inspect a def that was perfectly fine.
3. Locate `matched[0].RuntimeKey` through `Addressables.ResourceLocators` — the walk `BundleLive.Locate:199-213` does, keyed on
   that ONE key instead of every key — then walk each location's `Dependencies`. A location whose
   `Data is AssetBundleRequestOptions` contributes `Path.GetFileName(l.InternalId)`, the shipped `.bundle` file name spelled
   as `BundleClaims.Matches:191` compares and `BakeSelfCheck.ShippedBundlePath:735` resolves it.
   The traversal carries a **visited set of location instances** (reference identity, the way `BundleLive.Consider:226`
   de-duplicates hits) rather than a depth cap: a diamond in a real catalog is common, a cycle is what the cap was for, and a
   set answers both without silently truncating a deep-but-finite graph. File names are collected **case-insensitively**
   (`OrdinalIgnoreCase`), because two locations spelling the same file differently would otherwise be opened twice and counted
   twice in step 4. No locator answers the key → **R18**; located, but nothing in the graph carries
   `AssetBundleRequestOptions` → **R19**.
4. Per candidate file present on disk: `using (var b = new BundleBaker(shipped, "ct.doctor"))` → `b.WhyNot(AssetClassID.Mesh,
   asset)`. **Exactly one** must answer `null`. None of the named files is in this install → **R20**; every one that is
   present threw on open → **R21**; at least one opened and none holds the Mesh → **R10** (carrying the last `WhyNot`
   sentence); two or more hold it → **R9**, listing them. A name not unique INSIDE a bundle is already refused by
   `FindUnique:107`, out through `WhyNot`. Anything else thrown → **R22**, the catch-all, with the stack in `Player.log`.

The stored pair is by construction what `Patch` matches: bundle `OrdinalIgnoreCase` (`:1534`), asset ordinal through the same
`WhyNot` call (`:1588`).

**The derivation is LOGGED, or W4 is unfalsifiable.** A successful resolve used to say nothing, and the manifest row plus
the later `patch` line prove only that the CHOSEN pair works — never that no second holder existed. So step 4 logs the
deduplicated candidate list, one outcome line per candidate (`not shipped by this install`, the `WhyNot` sentence, or
`HOLDS IT (WhyNot == null)`), and a closing `resolved '<asset>' -> <bundle> (1 of <present> present candidate(s) …)`.
That block is W4's evidence.

### 4.2 `ProjectScaffold` — `src\Project\ProjectScaffold.cs` (new, UnityEngine-free, test-linked)
```csharp
internal static class ProjectScaffold
{   internal sealed class Result
    {   internal string Root, ManifestPath, MetaPath, MeshPath, SidecarPath;
        internal bool Created, MeshAlreadyPresent, RowAlreadyPresent;
        internal byte[] MeshBytes; }                          // the VERIFIED bytes, for the copied-byte preflight
    // modDir = ContentToolMain.ModDir. Throws InvalidDataException / IOException; never half-writes.
    internal static Result AddMeshReplacement(string modDir, string name, string sourceGlb, string expectedSha,
                                              string shippedBundle, string shippedAsset,
                                              IDictionary<string, string> aliases);
    internal static string NameRefusal(string name);          // null when the name is usable
    internal static string DefaultName(string shippedAsset);  // "Replace_" + safe(asset), <= 64 chars
    internal static string RootOf(string modDir, string name); // the folder AddMeshReplacement would use, or null
}
```
Placement: the **sibling** of the NORMALIZED `modDir` — `Path.GetFullPath(modDir).TrimEnd(Path.DirectorySeparatorChar,
Path.AltDirectorySeparatorChar)`, then its parent, in `RootOf` and `AddMeshReplacement` alike, because a trailing
separator makes `Directory.GetParent` answer `…\Mods\ContentTool` and bury the project inside ContentTool while the
post-condition, walking the same wrong parent, accepts it — i.e. `Mods\<name>`, never the `Mods\ContentTool\<name>`
fallback, because a folder under ContentTool is not a mod the manager can discover or the player can switch off
(`ModGate:38/:62`). Post-condition asserted before returning: `ContentMods.ProjectDir(modDir, name) == Root` — true once
`ppcontent.json` exists (`Sibling:128`), and what makes `ct_project <name>` / `ct_route7 apply <name>` find it. `NameRefusal`:
1–64 chars, first alphanumeric, rest alphanumeric or `.`/`_`/`-`; no separator, rooted path, `.`/`..`, trailing dot or space,
device name (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`, extension or not); resolved parent must be Mods. Order —
**the source file is read and verified before the first byte is written**, everything else validated before the copy, the
manifest committed last:
```csharp
// R1 (name), R2 (an existing non-empty folder with no ppcontent.json) — nothing on disk has been touched yet.
byte[] bytes = File.ReadAllBytes(sourceGlb);                       // R3 is a PRE-WRITE refusal: no directory,
if (AliasMap.Sha256(bytes) != expectedSha) throw new IOException(R3); // no template, nothing created at all
Directory.CreateDirectory(root);
if (created)                                                       // new project only
{   var tree = new Dictionary<string, object> { { "id", name }, { "bundle", name + ".bundle" } };
    AtomicFile.WriteText(manifestPath, new JsonWriter().Val(tree).ToString() + "\n", new UTF8Encoding(false)); }
ManifestFile file = ManifestFile.Load(manifestPath);               // the strict reader is the only gate (E1/E2)
string id = file.Manifest.Id;                                      // the AUTHORED id, which is <name> only for a new one
if (!File.Exists(metaPath)) AtomicFile.WriteText(metaPath, Meta(id), new UTF8Encoding(false));
else { string said = Package.MetaRefusal(File.ReadAllText(metaPath), null); if (said != null) throw ...R13; }
if (!Reuses(file.Manifest, shippedBundle, shippedAsset, stem))     // an EXACT row already there is a reuse, not a refusal
{   file.Manifest.AddMeshReplacement(shippedBundle, shippedAsset, stem);
    file.Manifest.Validate(); }                                    // a CONFLICTING row is R6 HERE, before any copy
if (noAliases && File.Exists(sidecarPath)) throw ...R5;
CopyOrVerify(meshPath, bytes);                                     // AtomicFile.Write only when absent; R4 otherwise
if (aliases.Count != 0) AliasMap.SaveSidecar(meshPath, AliasMap.Sha256(bytes), bytes.LongLength, aliases);
file.Save();                                                       // atomic splice, .bak, SHA guard (E5); no-op on reuse
```
`stem = Path.GetFileNameWithoutExtension(sourceGlb)`; row `{bundle, asset, mesh: stem}`, resolved back by
`ProjectBake.FindMesh` under `Content\Meshes\` (`:1581`, `OrdinalIgnoreCase` at `:2152`); `ManifestFile.Create` stays
unnecessary. An existing project keeps its authored `id` and own `bundle` and only gains a row; reuse only a folder already
holding `ppcontent.json` — a folder that **already exists, is non-empty and holds no `ppcontent.json`** is R2, and an EMPTY
folder of that name counts as new and is filled in. GLB collision: absent → copy; same SHA-256 → no-op
(`MeshAlreadyPresent`); different SHA-256 → R4, never an overwrite; sidecar present while `aliases` is empty → R5.

**"Absent" is decided by the create, not by a preceding `File.Exists`.** The three absent-only writes — the GLB copy, the
`ppcontent.json` template and the `meta.json` template — use `new FileStream(path, FileMode.CreateNew, FileAccess.Write,
FileShare.None)`, the stdlib's own atomic create-or-fail, and never `AtomicFile.Write`/`WriteText`, which is the UPSERT
writer and ends in `File.Replace`: a file created between the check and the write would be silently overwritten, the one
thing this whole section exists to forbid. On the losing side of that race the winner is re-read and validated by the
same gates as any pre-existing file (`AliasMap.Sha256` → R4, `ManifestFile.Load`, `Json.Parse` + `Package.MetaRefusal` →
R13), never trusted and never replaced.
`Result.MeshBytes` carries the verified bytes out, so §4.5's copied-byte preflight judges the very bytes that were written
rather than re-reading the file and re-opening the same question.

**Idempotent reuse, not R6.** `Reuses` answers true for a row that is exactly this one — `bundle` `OrdinalIgnoreCase` (the
fold `ProjectBake:1534` and `Manifest.Validate:203` use), `asset` ordinal (shipped names are folded nowhere), `kind == "mesh"`,
`mesh` `OrdinalIgnoreCase` (`FindMesh:2152` resolves the stem that way, so two spellings are one file). Then no row is queued,
`Save` writes nothing, and the press ends green having re-verified the copy and the sidecar. Without this, every retry after
an R7/R8/R11 failure hit its own committed row and was refused as R6 — which contradicts every "press Ship again" in §6.
R6 stays for a CONFLICTING row: the same `(bundle, asset)` with a different `mesh`, or this `mesh` against a target that is
already claimed by another row.

`meta.json`, written only when absent, exactly this (fields `ModMeta.cs:33-46`, shape copied from the shipped code-free demo
`demos\MaterialTweak\meta.json`; `AssemblyName` omitted — `ModMeta` defaults it to `string.Empty`, `ModRoster.AfterLoadMod`
supplies the content-only instance, and `Package.MetaRefusal:323-327` only objects when the field NAMES a file that is not in
the package). `ID` is the manifest's own `id` (`<name>` for a new project, the authored id for an existing one), never the
folder name assumed to be both; `Dependencies` makes the manager enable ContentTool for the player (`Package.EngineId:35`,
`MetaRefusal:319-322`):
```json
{ "ID": "<id>", "Version": "1.0.0",
  "Name": [ { "Key": "English", "Value": "<id>" } ],
  "Dependencies": [ "com.morgott.ContentTool" ] }
```
Only the **id** is written through `JsonWriter` (quoted AND escaped — an existing project's id came back DECODED from
`ManifestFile.Load` and may hold a quote or a backslash); the template BODY is a fixed literal, and the test spells the
expected bytes independently. Assembling the whole tree through the writer buys nothing: no other value here can carry a
character that needs escaping.

An **existing** `meta.json` is never rewritten and never trusted either: it is **strictly parsed first** —
`Json.Parse(text, 64)` (`src\Import\Json.cs:15`), which must yield a `Dictionary<string, object>` or the file is R13,
carrying the `FormatException` — and only then goes through `Package.MetaRefusal(text, null)`, the packager's own
validator, `stagedFiles` null so the `AssemblyName` arm cannot fire. The parse is not belt-and-braces: `MetaRefusal`
(`Package.cs:313-329`) is REGEX-based and accepts an unclosed object that happens to contain a matching `ID` and
`Dependencies`, so R13 without it does not prove the mod is discoverable. A file with no usable `ID` or without the exact
`com.morgott.ContentTool` dependency is R13 too. Shipping behind one of those produces a mod the manager keys wrongly, or
one the player installs with the engine switched off, silently doing nothing.

### 4.3 Bake + apply — `Route7.ApplyProject` alone
`private` → `internal`, an early return on a failed bake, and a **structured answer about the one bundle the wizard cares
about**. It already loads the project, computes `PatchCache.Key`, re-bakes when stale (`:228-250`) and installs (`:280`);
calling `ProjectBake.Run` first would bake twice, because `Run` does not write the freshness key. Its project NAME goes
through `ContentToolMain.ProjectDir` (`:208`), where an ABSOLUTE root is idempotent (`Path.Combine(root, absolute) ==
absolute`, `ContentMods.cs:127/:147`) — so the wizard hands it `Result.Root` and the two cannot disagree about the folder
baked. The change at `:249-256`, where a failed bake falls through today:
```csharp
internal enum ApplyDisposition { Redirected, Resident, Refused, BakeFailed }

// The console verb keeps calling this one, and its printed output is unchanged.
internal static string ApplyProject(string projectName)
{   ApplyDisposition ignored; return ApplyProject(projectName, null, out ignored); }

internal static string ApplyProject(string projectName, string forBundle, out ApplyDisposition how)
...
    int failed;
    pre.AppendLine(ProjectBake.Run(projectRoot, out failed));
    if (failed != 0) { how = ApplyDisposition.BakeFailed; return pre.AppendLine(R11).ToString(); }
    PatchCache.Write(patched, key);
...
    // From LIVE STATE, never by reading the log back - and in REGISTER'S OWN ORDER: residency is read BEFORE
    // Install, because Register:80-92 refuses a resident bundle before it ever looks at claims. A press made
    // after an earlier redirect has loaded would otherwise find this mod's own stale claim, answer Redirected
    // and print S2 over a log that says "restart required".
    bool wasResident = BundleLive.ResidentNow(forBundle);        // BEFORE Install
    ...
    BundleClaim mine = BundleClaims.Find(forBundle);
    how = wasResident                          ? ApplyDisposition.Resident
        : mine != null && mine.Mod == modId    ? ApplyDisposition.Redirected
        :                                        ApplyDisposition.Refused;
```
`BundleLive` gains exactly one query, `internal static bool ResidentNow(string bundleFile)`, which reuses its own private
`Locate:195` + `Resident:239` — the residency answer lives on the location's `AssetBundleRequestOptions.BundleName`, and
re-deriving that in `Route7` would be a second copy of the one comparison this project has already got wrong once
(`BundleLive.cs:230-238`). `BundleClaims.Find:221` is already `internal`.

Why structured: `Install` concatenates one `Register` line per bundle, and **zero claims taken is not the same fact as
residency** — a catalog `Locate` failure (`:215-218`) and an ownership conflict (`BundleClaims.Claim:250`) also take no
claim. Reading `"REFUSED: restart required"` out of the text would report those two as "restart and enable", which is a
wizard telling the author a lie with a straight face. S1 is emitted for `Resident` only, S2 for `Redirected` only,
R11 for `BakeFailed`, and **R23** for `Refused`.

The full log is surfaced either way (panel: last 6–10 lines; `ContentToolMain.Say` gets all of it) — the lines that matter:
`patch <bundle>: mesh '<asset>' <- <stem> ...`, `ct_project: ALL PASS - this project has no bundle of its own; ...`,
`installing 1 patched copy(ies) ...`, then `redirected ...` or `REFUSED: restart required: <bundle> is already loaded ...`.
**Residency is the normal outcome, not an error**: the bay rendered this very mesh, so the bundle is loaded and `Register:88`
refuses before taking a claim. No forced unload — it would pull the archive from under unrelated live objects, what that
refusal exists to prevent — and re-showing the prototype releases no Addressable. The panel says so plainly (S1).

### 4.4 UI — SHIP section in the Doctor panel
In `ModelDoctor.Draw` right after the Preview / Revert / Save / Skel-plan row (`:1248-1262`), i.e. below Save and above
`FitBench`'s `Advanced (file utilities)` toggle (`FitBench.cs:1656`). Rows, always drawn: project-name field (default
`ProjectScaffold.DefaultName(ShippedAsset)`) · resolved target `<bundle> / <asset>` or `Prototype.TargetRefusal` ·
`CREATE, BAKE & APPLY` · phase/result · project path · restart/enable line · last 6–10 log lines. `OnGUI` stays
read-and-enqueue only; the button is enabled only when:
```csharp
Ready != null && Ready.Outcome == Outcome.ByName && Ready.Report.Count(Severity.Blocking) == 0 &&
Prototype != null && Prototype.Mode == VerifyMode.Replace && Prototype.Live != null &&
Prototype.TargetRefusal == null && Prototype.ShippedBundle != null && Renderer != null && Path != null && File.Exists(Path) &&
Path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) &&
ProjectScaffold.NameRefusal(projectName) == null && !Busy && !shipPending
```
`Intent.Ship` joins the enum (`:29`), `Enqueue("ship")` the map (`:228-234`). The bake is Unity-heavy and blocks the main
thread for seconds, so `SlimPanel`'s volatile snapshot pattern does not apply (no worker changes state between Layout and
Repaint) — a **two-frame gate** in `Tick` (`:360`, driven by `FitBench.Update` `:2106`) paints the label before the freeze:
(1) Tick N+1 drains `Intent.Ship`, snapshots the inputs **and the Doctor generation `gen` (`:58`)**, sets
`shipPending = true`, `shipPhase = "Baking…"`; (2) `Draw` sets `shipLabelPainted = true` during `EventType.Repaint` only;
(3) Tick N+2 sees both and runs scaffold + `ApplyProject` synchronously, the painted frame staying up while Unity blocks.
No progress bar this slice — but the gate spans a frame **the author can act in**, so:
- **the armed press is cancelled when the generation moved** (retarget `:660`, a new file through `Restart:269`, the bench
  closing `:1668`) — checked before the scaffold writes its first byte, because shipping the old snapshot would author a
  mod folder for a slot the panel has already left;
- **every target-changing control is disabled while `shipPending`** (Preview / Revert preview / Save aliases / Write skel
  plan / Copy report, `ModelDoctor.cs:1246-1261`), so the cancel above is the backstop and not the normal path;
- **`Dispose` (`:1664`) clears `shipPending`, `shipLabelPainted` and every snapshot field**, so a press armed on the frame
  the bench closed cannot execute against the next Doctor, and the next Doctor does not open on the last one's result.

### 4.5 Doctor verdict ≡ bake verdict
Before the bake, in order: the scaffold re-reads the source and compares `AliasMap.Sha256` with `Ready.Sha256` before it
writes anything (R3, §4.2); it saves the Doctor's CURRENT alias map beside the COPY with the COPY's SHA; then
`ReplacementPreflight.Run(made.MeshBytes, made.MeshPath, Prototype)` — the SAME bytes the copy holds, carried out on the
`Result` — refusing anything but `ByName` with zero Blocking rows (R7); then the renderer fingerprint (R8). That binds the
verdict, not the bake, whose target lookup, bundle I/O and material-slot mapping can still refuse — the bake result stays
authoritative.

**The fingerprint has to be preview-aware, or W6 can never pass.** `Target` is snapshotted when the slot is picked
(`PickTarget:120/:142`), and `DoPreview:451` then assigns `Renderer.sharedMesh = candidate` — so with a preview on screen the
renderer's mesh is OURS, and `Snapshot(Renderer, ...).SameAs(Target)` is false by construction: `SameAs:73-76` compares
`MeshInstanceId` and `MeshName`. A naive R8 would therefore refuse exactly the state the author ships from, and the acceptance
row that asks for "S1 with the preview still visible" would be unreachable. So:
```csharp
RigTarget now = Snapshot(shipRenderer, shipTargetWas.TransformPath);
bool same = HasPreview
    // The mesh IS the preview - our own object, put there by DoPreview - so its identity is not evidence
    // that the rig moved. Everything that IS evidence still has to match.
    ? ReferenceEquals(shipRenderer.sharedMesh, preview) && now.SameRigAs(shipTargetWas)
    : now.SameAs(shipTargetWas);
if (!same) -> R8
```
`RigTarget` gains `SameRigAs` beside `SameAs` (`SkinCompatibility.cs:70`): renderer instance id, transform path and the bone
name array — everything except the four mesh-derived fields (`MeshInstanceId`, `MeshName`, `BindPoseCount`, `Rigged`).
`SameAs` becomes `SameRigAs(other) && <the four mesh fields>`, so the two can never drift apart. A preview whose mesh is NOT
`preview` means something else swapped the renderer under us, which is the very thing R8 exists to catch.

## 5. Changes
| Path | This slice | Why |
|---|---|---|
| `src\Project\ProjectScaffold.cs` · `src\Doctor\ShippedTarget.cs` | **new**: scaffold UnityEngine-free and linked into `ObjCodecTests`, `ShippedTarget` Unity + AssetTools | the disk half is provable offline; §4.1 needs Unity types, so it is not test-linked |
| `src\Doctor\PrototypeTarget.cs` `:30-43` | **add** `ShippedBundle`, `ShippedAsset`, `TargetRefusal` | the row's target, carried from the slot |
| `src\Dev\FitBench.cs` `:768`, `:739` | **change** `LiveSlots` to keep the owning `Addon`; `Retarget` calls `Resolve` | the Addon is already in hand at `:776` |
| `src\Dev\ModelDoctor.cs` `:29`, `:228`, `:360`, `:1246-1261`, `:1262`, `:1664` | **add** `Intent.Ship`, `"ship"`, the two-frame gate with its generation cancel, the SHIP section; **change** the button row to grey out while `shipPending`, and `Dispose` to clear the armed press | one intent, no new panel class |
| `src\Bake\Route7.cs` `:205`, `:249-256` | **change** to `internal`, early-return on `failed != 0`, plus the `ApplyDisposition` overload | a one-button command must not install an unvouched bake, nor guess S1 from log text |
| `src\Bake\BundleLive.cs` | **add** `internal static bool ResidentNow(string bundleFile)` | the residency fact lives behind two private methods there; a copy in `Route7` is the comparison this project already got wrong once (`:230-238`) |
| `src\Import\SkinCompatibility.cs` `:70` | **add** `RigTarget.SameRigAs`, `SameAs` delegates to it | §4.5 — a live preview must not read as "the renderer changed" |
| `tests\ObjCodecTests\ProjectScaffoldTests.cs` + `.csproj` | **new** | §8 |
| `ManifestFile`, `Manifest`, `AtomicFile`, `AliasMap`, `ProjectBake`, `Package`, `PatchCache` | **untouched** | the wizard is a caller, not an author, of all of them — including `Package.MetaRefusal`, which it reuses whole |

## 6. Error messages (exact strings)
| id | Where | Text |
|---|---|---|
| R1 | `NameRefusal` | `project name REFUSED: '<name>' - use 1-64 characters starting with a letter or digit, then letters, digits, '.', '_' or '-'; no path separators, no device names` |
| R2 | `AddMeshReplacement` | `'<root>' already exists, is not empty, and holds no ppcontent.json, so it is not a ContentTool project - pick another project name` |
| R3 | `AddMeshReplacement`, **pre-write** | `'<glb>' changed on disk after its green verdict, so nothing was written - pick it again, read the report, then press Ship again` |
| R4 | `CopyOrVerify` | `Content\Meshes\<stem>.glb already holds DIFFERENT bytes (sha <have> vs <want>), so it was NOT overwritten - rename the file you are shipping, or ship into another project` |
| R5 | `AddMeshReplacement` | `<stem>.glb.aliases.json already sits beside the copy but this Doctor session has no bone map, so the bake would silently use mappings you never saw - delete it, or set the map` |
| R6 | `Manifest.Validate` (**E4 verbatim**), for a CONFLICTING row only | `ppcontent.json already replaces "<asset>" in "<bundle>" with a <kind>, so a second row for the same target was NOT written - edit the existing row instead` |
| R7 | ship gate | `the COPIED glb did not re-read green (<outcome>), so nothing was baked - the project on disk is complete, fix the file and press Ship again` |
| R8 | ship gate | `the slot's renderer changed while Ship was running, so nothing was baked - pick the slot again` |
| R9 | `Resolve` step 4, two or more holders | `TARGET REFUSED: a Mesh named '<asset>' is in <n> of the bundles this addon loads (<list>) - ContentTool will not guess which one the game means` |
| R10 | `Resolve` step 4, opened but no holder | `TARGET REFUSED: none of the bundles this addon loads holds a Mesh named '<asset>' - <last WhyNot sentence>` |
| R11 | `Route7.ApplyProject`, `BakeFailed` | `NOT APPLIED: the bake reported <n> failure(s); fix the lines above and press Ship again` |
| R12 | ship gate (catch-all), **root on disk** | `SHIP THREW: <Type>: <message> - '<root>' is on disk and the files already written there were retained; see Player.log for the stack` |
| R12 | ship gate (catch-all), **no root** | `SHIP THREW: <Type>: <message> - no project folder was created; see Player.log for the stack` |
| R13 | `AddMeshReplacement`, existing meta | `'<meta>' already exists but is not a mod this project can ship: <Package.MetaRefusal sentence> - fix that file, or ship into another project` |
| R14 | `Resolve` step 1 | `TARGET REFUSED: this slot has no live mesh, so there is no shipped Mesh name to look for` |
| R15 | `Resolve` step 2, no graph | `TARGET REFUSED: this slot's addon carries no SkinData or was not built from a prefab, so there is no dependency graph to walk` |
| R16 | `Resolve` step 2, zero references | `TARGET REFUSED: this addon's SkinData reaches no AssetReference whose asset is the prefab it built, so ContentTool cannot tell which shipped bundle serves this slot` |
| R17 | `Resolve` step 2, several GUIDs | `TARGET REFUSED: this addon's SkinData reaches <n> different AssetReference GUIDs for the prefab it built (<list>) - ContentTool will not guess which one the game means` |
| R18 | `Resolve` step 3, locator failure | `TARGET REFUSED: no live Addressables locator answers this addon's prefab key '<key>' - either the catalog has not initialised yet, or this prefab is not served from a bundle at all` |
| R19 | `Resolve` step 3, empty graph | `TARGET REFUSED: the locations behind this addon's prefab name no .bundle at all - nothing in that dependency graph carries AssetBundleRequestOptions` |
| R20 | `Resolve` step 4, nothing on disk | `TARGET REFUSED: this install ships none of the bundles this addon loads (<list>) - verify the game files, then show the prototype again` |
| R21 | `Resolve` step 4, all opens failed | `TARGET REFUSED: every bundle this addon loads refused to open (<list>) - <last error>` |
| R22 | `Resolve` (catch-all) | `TARGET REFUSED: the addon's dependency graph could not be walked (<Type>: <message>) - see Player.log for the stack` |
| R23 | ship gate, `Refused` | `baked, but NOT APPLIED: <bundle> was neither redirected nor already loaded - the log above names the refusal; the project folder is complete and can be enabled after a restart` |
| S1 | ship gate, `Resident` (normal) | `baked OK - restart the game and enable '<name>' in the mod manager. Phoenix Point already loaded <bundle>, so this session keeps showing your Doctor preview.` |
| S2 | ship gate, `Redirected` | `baked and redirected LIVE - <bundle> now loads from the patched copy on the next load` |

## 7. Failure model — order, and what is left on disk
**Nothing is rolled back after a failed bake** — copy, sidecar and row are authored project state, retryable and cheap on the
next press, and a three-writer rollback would be the more dangerous code.
| # | Stage | Left on disk |
|---|---|---|
| 1 | target unresolved (R9/R10, R14–R22) or name invalid (R1) | nothing |
| 2 | the name's folder exists, is non-empty and is not a project (R2), or the source GLB no longer hashes to the verdict's SHA (R3) | nothing — both refusals are PRE-WRITE, so a first press that fails here creates no folder at all |
| 3 | project directory cannot be created | nothing, or an empty directory |
| 4 | `ppcontent.json` template write fails | the atomic destination is absent or unchanged; an empty directory may remain |
| 5 | `ManifestFile.Load` refuses (E1/E2), or the existing `meta.json` is refused (R13) | existing manifest untouched, no GLB copied; a just-created template may remain |
| 6 | `meta.json` template write fails | manifest present, meta absent — the next press writes it |
| 7 | `Validate` refuses the row (R6/E3), or a stray sidecar is found (R5) | manifest unchanged on disk, no copy |
| 8 | GLB copy fails (R4, or I/O) | `AtomicFile` removes its temp; folder + templates remain, the manifest still has no new row |
| 9 | sidecar write fails | copied GLB orphaned, manifest still has no new row, any old sidecar intact |
| 10 | `ManifestFile.Save` fails (E5/E6) | original manifest intact; copy + sidecar orphaned, making the retry cheap (and the retry REUSES both, §4.2) |
| 11 | copied-byte preflight or renderer fingerprint refuses (R7/R8) | complete project — copy, sidecar and row all committed. **No bake ran, so there is no patched output at all** |
| 12 | bake reports `N FAILURE(S)` (R11, `BakeFailed`) | complete project; `Patch` may have left a PARTIAL patched copy — not installed, cache not marked current |
| 13 | apply answers `Resident` (S1) or `Refused` (R23 — catalog `Locate` failed, or another mod owns that bundle) | project + good patched copy; no live claim taken; restart + enable required |
| 14 | apply throws (R12) | whatever had been written when it threw — R12 reads the folder's existence back off disk and says only that |

## 8. Tests — `tests\ObjCodecTests\ProjectScaffoldTests.cs`
House pattern (`AliasTests.cs`): `internal static string Run()`, `checks += Check(cond, msg)`, temp dir deleted in `finally`,
last line `SCAFFOLD PASS, N check(s) - ...`, wired into `Program.cs` beside `ManifestTests.Run()` (`:141`). The `.csproj` gains
`ProjectScaffold.cs` + the test; `ContentMods.cs` `:75`, `Manifest.cs` `:38`, `AtomicFile.cs` `:37`, `AliasMap.cs` `:182`,
`Json.cs` `:143` are linked already.

| Arm | What it proves |
|---|---|
| `Scaffold_CreatesProjectTemplates` | new folder → `ppcontent.json` (`id`/`bundle`), `meta.json` (the §4.2 template, `ID` == the manifest's `id`, ContentTool in `Dependencies`), one mesh row, `Content\Meshes\<stem>.glb`, sidecar — and `ContentMods.ProjectDir(modDir, name)` resolves to that root |
| `Scaffold_FillsAnEmptyFolder` | an EMPTY `Mods\<name>` is not someone else's work: it counts as new and is filled in, `Created` true |
| `Scaffold_RefusesAnUnrelatedFolder` | a non-empty folder with no `ppcontent.json` → R2 verbatim, and nothing written into it |
| `Scaffold_KeepsAnAuthoredId` | a hand-written `ppcontent.json` whose `id` is NOT the folder name gets a `meta.json` carrying that `id`, not the name |
| `Scaffold_RefusesAnUnshippableMeta` | an existing `meta.json` with no `ID`, and one without the `com.morgott.ContentTool` dependency → R13 carrying `Package.MetaRefusal`'s own sentence; the file is not rewritten |
| `Scaffold_AppendsSecondRow` | a second distinct row lands **into a HAND-WRITTEN manifest** carrying a BOM, an unknown member and a nested value; the `replace` span is located independently in the before and after texts, and prefix AND suffix outside it are byte-identical; the original row survives INSIDE the new span as one unbroken run; `id`/`bundle` untouched, the meta keyed on the manifest's id |
| `Scaffold_ReusesAnIdenticalRow` | the SAME (bundle CI, asset, mesh) pressed twice **in a fresh project** → exactly ONE row after run two, no exception, `RowAlreadyPresent` true, manifest bytes byte-identical — the retry path every "press Ship again" depends on |
| `Scaffold_RefusesAMalformedMeta` | an existing `meta.json` that is unclosed, and one that is not a JSON object → R13 from the strict `Json.Parse` before `MetaRefusal` ever runs; neither file rewritten |
| `Scaffold_QuotedId` | an authored id `com.test"quote` → the written `meta.json` re-reads with `ID == com.test"quote` and `Package.MetaRefusal` accepts it (the test's own template escapes it the same way `Meta()` does) |
| `Scaffold_TrailingSeparator` | `modDir` spelled with a trailing `\` resolves to the SAME `Root` as without it, in `RootOf` and in a real press — never `Mods\ContentTool\<name>` |
| `Scaffold_RefusesConflictingTarget` | same (bundle CI, asset) with a DIFFERENT mesh stem → R6/E4, manifest bytes identical, no copy written |
| `Scaffold_MeshCollisionPolicy` | same SHA → no-op with `MeshAlreadyPresent`; different SHA → R4, destination bytes unchanged |
| `Scaffold_RefusesAStaleSourceBeforeWriting` | a SHA mismatch under a name whose folder does not exist → R3, and no folder was created |
| `Scaffold_SidecarRoundTrips` | `AliasMap.LoadSidecar(copy, sha, out why)` returns the same aliases and the sidecar's sha is the COPY's; `Result.MeshBytes` equals the copy's bytes |
| `Scaffold_NameTable` | valid; empty; 65 chars; `..`; `a\b`; `C:\x`; `CON`; `nul.glb`; leading `-`; trailing `.` and space → each refused with R1, nothing created outside the Mods folder |
| `Fingerprint_APreviewIsNotAChangedRig` | §4.5 on plain data: a preview-shaped `RigTarget` (same renderer, same bones, **all four mesh-derived fields moved** — `MeshInstanceId`, `MeshName`, `BindPoseCount`, `Rigged`) is `!SameAs` and `SameRigAs`; a different renderer and a renamed bone are neither. Written and observed RED (`CS1061` on `SameRigAs`) before the split is implemented. `SkinCompatibility.cs` is linked into this gate (`ObjCodecTests.csproj:190`), so the split does not wait for a game |

**In-game acceptance, PPCLI on `D:\PP-Instance2`** (user order 2026-09-05: bench = Instance2, profile `…592`; Steam install untouchable) (steps only — `PPCLI\PLAYBOOK.md` maps them to lines): (1) `connect state`
answers, start a campaign, open the bench; (2) via `call`, `FitBench.ShowPrototype`, wait until the prototype is no longer
busy, take one `SlotTargets()` entry, read its `ShippedBundle`/`ShippedAsset`; (3) via `call` on `FitBench.doctor`,
`PickFile(<glb>)`, `PickTarget(target)`, poll `Ready.Outcome == ByName`, then `Enqueue("preview")` and confirm `HasPreview`
— **the ship is performed with the preview LIVE**, which is what W6 asks for and what the §4.5 fingerprint makes possible;
(4) set the project-name field, `Enqueue("ship")`, poll the ship result; (5) assert on disk
`Mods\<name>\ppcontent.json` + `meta.json` + `Content\Meshes\<stem>.glb` + sidecar,
the row's bundle/asset being the pair step 2 resolved; (6) assert `Player.log` holds the derivation line, `patch <bundle>:
mesh ...`, `ct_project: ALL PASS` and the expected `REFUSED: restart required` line (S1); (7) re-run `ct_project <name>` via
`connect console` → `ALL PASS`; (8) press Ship a SECOND time with everything unchanged — it must end green again with the
manifest holding exactly ONE row (§4.2 reuse), not R6; (8b) **W5, in a SEPARATE never-applied project and BEFORE the
restart** — a second project (`Replace_BadRow`, its own id, never enabled) whose `ppcontent.json` is hand-edited to name a
bundle this install does not ship: `shipResult` is byte-for-byte the R11 string, `Holds(<its id>)` is false, and no
`ct-cache.key` exists under its patched directory. It cannot run on the project above or after step 9, where `Holds` is
deliberately true and a good bake has already written the key; (9) **mandatory final arm** — restart so `meta.json` is
discovered, enable `<name>` BEFORE entering the geoscape, show the prototype again, assert `BundleLive.Holds(<id>)` plus
live `sharedMesh` vertex/index counts equal the GLB's baked counts.

## 9. Follow-ups (`ponytail:` ledger)
- `ponytail:` `ShippedTarget.Resolve` opens each candidate bundle with `BundleBaker` per slot — fine for one press, O(bundles)
  if the panel ever resolves every slot eagerly; cache by bundle file then. Copy `GetAssetReferencesFromObject`'s `:316`
  public-field walk if a game update breaks the reflection.
- `ponytail:` two locator walks now (`BundleLive.Locate:195` + §4.1), fold them if a third caller appears; blocking
  main-thread bake with no cancel — the lifecycle dashboard's job, next slice.
- `ponytail:` an existing `meta.json` is VALIDATED (`Package.MetaRefusal`, R13) but never repaired or rewritten — offering
  to fix it is the packaging slice's job, not this one's. `Version` and the English `Name` are the template's; nothing reads
  them back. Extend mode, texture/material rows and deletion reuse this scaffold — add rows.

## 10. Acceptance
| id | Check | Command / evidence |
|---|---|---|
| W1 | Offline gates green | `dotnet build -c Release` → 0 errors (1 known CS0649 allowed); `dotnet run --project tests\ObjCodecTests -c Release` (exit 0, `SCAFFOLD PASS` present); `dotnet run --project tests\TargetPathTests -c Release` (exit 0); `dotnet build tools\Package\Package.csproj -c Release` → `0 Error(s)` |
| W2 | Scaffold is exact | `Scaffold_CreatesProjectTemplates` + `Scaffold_KeepsAnAuthoredId` + `Scaffold_QuotedId` + `Scaffold_AppendsSecondRow`: `meta.json` byte-compared to the §4.2 template (the id escaped as `Meta()` escapes it), and, appending into a HAND-WRITTEN manifest with a BOM, an unknown member and a nested value, the bytes before and after an INDEPENDENTLY located `replace` span byte-identical with the original row still one unbroken run inside it |
| W3 | No overwrite is possible | `Scaffold_MeshCollisionPolicy` + `Scaffold_RefusesConflictingTarget` + `Scaffold_RefusesAnUnrelatedFolder` + `Scaffold_RefusesAnUnshippableMeta` |
| W3b | A retry is a retry, not a refusal | `Scaffold_ReusesAnIdenticalRow` offline + in-game step 8: the second press ends green with one row |
| W4 | Target derivation disk-proved | in-game steps 2 + 5, plus the §4.1 derivation block in `Player.log`: `[ContentTool] ShippedTarget: '<asset>' candidates (n): …`, one outcome line per deduplicated candidate with a single `HOLDS IT (WhyNot == null)`, closed by `resolved '<asset>' -> <bundle> (1 of <present> present candidate(s) …)`. The row the bake matched equals that pair |
| W5 | A failed bake installs nothing | in-game step 8b, in a SEPARATE never-applied project, before the step-9 restart: force a bad row, press Ship → `shipResult` is byte-for-byte the R11 string (`ApplyDisposition.BakeFailed`), `BundleLive.Holds(<its id>)` false, no `ct-cache.key` under its patched directory |
| W6 | Honest end state, with the preview up | in-game steps 3 + 6 + 9: shipped while `HasPreview` is true and NOT refused as R8; S1 shown with no live swap this session; after restart + enable, `Holds` true and live mesh counts equal the GLB's |
| W7 | Owner visual check | owner sees the replaced mesh on the prototype after the restart in step 9 |
