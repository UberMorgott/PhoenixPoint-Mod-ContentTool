# "Replace one mesh" wizard — one button from a green Doctor verdict to a shipped project — design

Status: **v1, 2026-09-02**, HEAD `689215f`. Owner decisions fixed before writing; recorded, not re-opened. Peer review: Codex
memo `e4203269...out.md`, **accepted in full**; its facts-file corrections are in §3. Builds on the shipped manifest core
(`2026-09-02-manifest-core-design.md`). Next slice: the lifecycle dashboard (Validate/Bake/Apply/Verify/Package, progress).

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
| project dir + discovery | `ContentMods.ProjectDir` `:143-147`, `Sibling` `:123-129`, `ContentToolMain.ProjectDir` `:38`; `ModGate.Decide` `:38`→`Unknown`, `Why` `:62`, `ContentMods.cs:102-105` | a sibling holding `ppcontent.json` wins, else `Mods\ContentTool\<name>`; a folder with no `meta.json` is never applied |
| bake | `ProjectBake.Run(string, out int)` `:63`; replacement-only exit `:103-119`; `PatchCache.Key` `:43/:49` (manifest SHA1 + path/size/mtime stamps) | a mesh-only project ends `ct_project: ALL PASS - this project has no bundle of its own; the patched copy(ies) above are the whole output` |
| apply + residency | `Route7.ApplyProject` `:205` (**private**), freshness `:228-232`, bake `:249-256`, install `:280`; `BundleLive.Register` `:74`, refusal `:88-92`, `Holds` `:145`, build-name note `:230-238` | re-bakes when stale but **continues after a failed bake**; a rendered target is resident → refusal, not a live swap |
| doctor | `Path` `:61`, `Renderer` `:62`, `Prototype` `:68`, `Ready` `:69`, `Busy` `:78`, `Enqueue` `:228`, `Tick` `:360` (from `FitBench.Update` `:2106`), buttons `:1248-1262`, `Draw` `:1182` | where the button and its intent live |
| verdict | `ReplacementPreflight.Run(byte[], string, PrototypeTarget)` `:50`, `Baked` is **`BakedSkin`** `:19`, `Outcome` = `ByName\|NearestBone\|NotRigged\|Refused` (`ReplacementDecision.cs:6-16`), `DiagnosticReport.Count` `:56` | Doctor ≡ bake is a hard rule |
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
- The `ppcontent.json`-only exclusion is `ModGate.Decide:38`→`Unknown` + `ContentMods.cs:102-105` (`ModRoster.cs:317-321` is a
  comment reaching the same conclusion). `CatalogLive` is the loose-file route and locates no serialized Mesh, and
  `BundleLive.Locate` is private (`:195`) — so §4.1 writes its own dependency-oriented walk rather than widening it.

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
1. `asset = smr.sharedMesh.name` — a CANDIDATE until step 4 answers.
2. Collect every `AssetReference` reachable from `addon.AddonDef.SkinData` through the game's own
   `AssetsManager.GetAssetReferencesFromObject` (`:316`, internal instance → reflection; its public-field walk is small enough
   to copy if that breaks). Keep the one whose `.Asset` is `ReferenceEquals` to `addon.VisualsSourcePrefab` (`:179`); zero
   matches or several distinct GUIDs → R9/R10.
3. Locate its runtime key through `Addressables.ResourceLocators` — the walk `BundleLive.Locate:199-213` does, keyed on that
   key instead of every key — then walk the location's `Dependencies` recursively; a dependency whose
   `Data is AssetBundleRequestOptions` contributes `Path.GetFileName(d.InternalId)`, the shipped `.bundle` file name spelled
   as `BundleClaims.Matches:191` compares and `BakeSelfCheck.ShippedBundlePath:735` resolves it.
4. Per candidate file present on disk: `using (var b = new BundleBaker(shipped, "ct.doctor"))` → `b.WhyNot(AssetClassID.Mesh,
   asset)`. **Exactly one** must answer `null`; zero → R10 (carrying the last `WhyNot` sentence), two or more → R9 listing
   them. A name not unique INSIDE a bundle is already refused by `FindUnique:107`, out through `WhyNot`.

The stored pair is by construction what `Patch` matches: bundle `OrdinalIgnoreCase` (`:1534`), asset ordinal through the same
`WhyNot` call (`:1588`).

### 4.2 `ProjectScaffold` — `src\Project\ProjectScaffold.cs` (new, UnityEngine-free, test-linked)
```csharp
internal static class ProjectScaffold
{   internal sealed class Result
    {   internal string Root, ManifestPath, MetaPath, MeshPath, SidecarPath;
        internal bool Created, MeshAlreadyPresent; }
    // modDir = ContentToolMain.ModDir. Throws InvalidDataException / IOException; never half-writes.
    internal static Result AddMeshReplacement(string modDir, string name, string sourceGlb, string expectedSha,
                                              string shippedBundle, string shippedAsset,
                                              IDictionary<string, string> aliases);
    internal static string NameRefusal(string name);          // null when the name is usable
    internal static string DefaultName(string shippedAsset);  // "Replace_" + safe(asset), <= 64 chars
}
```
Placement: the **sibling** `Directory.GetParent(modDir)\<name>` — i.e. `Mods\<name>` — never the `Mods\ContentTool\<name>`
fallback, because a folder under ContentTool is not a mod the manager can discover or the player can switch off
(`ModGate:38/:62`). Post-condition asserted before returning: `ContentMods.ProjectDir(modDir, name) == Root` — true once
`ppcontent.json` exists (`Sibling:128`), and what makes `ct_project <name>` / `ct_route7 apply <name>` find it. `NameRefusal`:
1–64 chars, first alphanumeric, rest alphanumeric or `.`/`_`/`-`; no separator, rooted path, `.`/`..`, trailing dot or space,
device name (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`, extension or not); resolved parent must be Mods. Order —
everything validated before the first byte, manifest committed last:
```csharp
var tree = new Dictionary<string, object> { { "id", name }, { "bundle", name + ".bundle" } }; // new project only
AtomicFile.WriteText(manifestPath, new JsonWriter().Val(tree).ToString() + "\n", new UTF8Encoding(false));
if (!File.Exists(metaPath)) AtomicFile.WriteText(metaPath, Meta(name), new UTF8Encoding(false));
ManifestFile file = ManifestFile.Load(manifestPath);      // the strict reader is the only gate
file.Manifest.AddMeshReplacement(shippedBundle, shippedAsset, stem);
file.Manifest.Validate();                                  // dup target refused HERE, before any copy
byte[] bytes = File.ReadAllBytes(sourceGlb);
if (AliasMap.Sha256(bytes) != expectedSha) throw new IOException(R3);
CopyOrVerify(meshPath, bytes);                             // AtomicFile.Write only when absent
if (aliases.Count != 0) AliasMap.SaveSidecar(meshPath, AliasMap.Sha256(bytes), bytes.LongLength, aliases);
file.Save();                                               // atomic splice, .bak, SHA guard (E5)
```
`stem = Path.GetFileNameWithoutExtension(sourceGlb)`; row `{bundle, asset, mesh: stem}`, resolved back by
`ProjectBake.FindMesh` under `Content\Meshes\` (`:1581`); `ManifestFile.Create` stays unnecessary. An existing project keeps
its authored `id` and own `bundle` and only gains a row; reuse only a folder already holding `ppcontent.json` (a non-empty
unrelated folder is R2, an empty one counts as new). GLB collision: absent → atomic copy; same SHA-256 → no-op
(`MeshAlreadyPresent`); different SHA-256 → R4, never an overwrite; sidecar present while `aliases` is empty → R5.

`meta.json`, written only when absent, exactly this (fields `ModMeta.cs:33-46`, shape copied from the shipped code-free demo
`demos\MaterialTweak\meta.json`; `AssemblyName` omitted — `ModMeta` defaults it to `string.Empty` and
`Package.MetaRefusal:323-327` only objects when it names a missing file). `ID` == the `ppcontent.json` `id` == `<name>`;
`Dependencies` makes the manager enable ContentTool for the player (`Package.EngineId:35`, `MetaRefusal:319`):
```json
{ "ID": "<name>", "Version": "1.0.0",
  "Name": [ { "Key": "English", "Value": "<name>" } ],
  "Dependencies": [ "com.morgott.ContentTool" ] }
```

### 4.3 Bake + apply — `Route7.ApplyProject` alone
`private` → `internal`, plus an early return on a failed bake. It already loads the project, computes `PatchCache.Key`,
re-bakes when stale (`:228-250`) and installs (`:280`); calling `ProjectBake.Run` first would bake twice, because `Run` does
not write the freshness key. Its project NAME goes through `ContentToolMain.ProjectDir` (`:208`), where an ABSOLUTE root is
idempotent (`Path.Combine(root, absolute) == absolute`, `ContentMods.cs:127/:147`) — so the wizard hands it `Result.Root` and
the two cannot disagree about the folder baked. The change at `:249-256`, where a failed bake falls through today:
```csharp
int failed;
pre.AppendLine(ProjectBake.Run(projectRoot, out failed));
if (failed != 0) return pre.AppendLine(R11).ToString();   // NOT APPLIED, nothing installed
PatchCache.Write(patched, key);
```
The full log is surfaced (panel: last 6–10 lines; `ContentToolMain.Say` gets all of it) — the lines that matter:
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
Prototype.TargetRefusal == null && Renderer != null && Path != null && File.Exists(Path) &&
Path.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) &&
ProjectScaffold.NameRefusal(projectName) == null && !Busy && !shipPending
```
`Intent.Ship` joins the enum (`:29`), `Enqueue("ship")` the map (`:228-234`). The bake is Unity-heavy and blocks the main
thread for seconds, so `SlimPanel`'s volatile snapshot pattern does not apply (no worker changes state between Layout and
Repaint) — a **two-frame gate** in `Tick` (`:360`, driven by `FitBench.Update` `:2106`) paints the label before the freeze:
(1) Tick N+1 drains `Intent.Ship`, snapshots the inputs, sets `shipPending = true`, `shipPhase = "Baking…"`; (2) `Draw` sets
`shipLabelPainted = true` during `EventType.Repaint` only; (3) Tick N+2 sees both and runs scaffold + `ApplyProject`
synchronously, the painted frame staying up while Unity blocks. No cancel, no moving progress this slice.

### 4.5 Doctor verdict ≡ bake verdict
Before the bake, in order: re-read the source and compare `AliasMap.Sha256` with `Ready.Sha256` (R3); save the Doctor's
CURRENT alias map beside the COPY with the COPY's SHA; re-run `ReplacementPreflight.Run(copiedBytes, meshPath, Prototype)`,
refusing anything but `ByName` with zero Blocking rows (R7); re-`Snapshot(Renderer, Target.TransformPath)`, refusing when
`!SameAs(Target)` (R8) — the guard `Tick:376-386` already applies to a report. That binds the verdict, not the bake, whose
target lookup, bundle I/O and material-slot mapping can still refuse — the bake result stays authoritative.

## 5. Changes
| Path | This slice | Why |
|---|---|---|
| `src\Project\ProjectScaffold.cs` · `src\Doctor\ShippedTarget.cs` | **new**: scaffold UnityEngine-free and linked into `ObjCodecTests`, `ShippedTarget` Unity + AssetTools | the disk half is provable offline; §4.1 needs Unity types, so it is not test-linked |
| `src\Doctor\PrototypeTarget.cs` `:30-43` | **add** `ShippedBundle`, `ShippedAsset`, `TargetRefusal` | the row's target, carried from the slot |
| `src\Dev\FitBench.cs` `:768`, `:739` | **change** `LiveSlots` to keep the owning `Addon`; `Retarget` calls `Resolve` | the Addon is already in hand at `:776` |
| `src\Dev\ModelDoctor.cs` `:29`, `:228`, `:360`, `:1262` | **add** `Intent.Ship`, `"ship"`, the two-frame gate, the SHIP section | one intent, no new panel class |
| `src\Bake\Route7.cs` `:205`, `:249-256` | **change** to `internal`, early-return on `failed != 0` | a one-button command must not install an unvouched bake |
| `tests\ObjCodecTests\ProjectScaffoldTests.cs` + `.csproj` | **new** | §8 |
| `ManifestFile`, `Manifest`, `AtomicFile`, `AliasMap`, `ProjectBake`, `BundleLive`, `PatchCache` | **untouched** | the wizard is a caller, not an author, of all of them |

## 6. Error messages (exact strings)
| id | Where | Text |
|---|---|---|
| R1 | `NameRefusal` | `project name REFUSED: '<name>' - use 1-64 characters starting with a letter or digit, then letters, digits, '.', '_' or '-'; no path separators, no device names` |
| R2 | `AddMeshReplacement` | `'<root>' already exists and holds no ppcontent.json, so it is not a ContentTool project - pick another project name` |
| R3 | `AddMeshReplacement` | `'<glb>' changed on disk after its green verdict, so nothing was written - pick it again, read the report, then press Ship again` |
| R4 | `CopyOrVerify` | `Content\Meshes\<stem>.glb already holds DIFFERENT bytes (sha <have> vs <want>), so it was NOT overwritten - rename the file you are shipping, or ship into another project` |
| R5 | `AddMeshReplacement` | `<stem>.glb.aliases.json already sits beside the copy but this Doctor session has no bone map, so the bake would silently use mappings you never saw - delete it, or set the map` |
| R6 | `Manifest.Validate` (**E4 verbatim**) | `ppcontent.json already replaces "<asset>" in "<bundle>" with a <kind>, so a second row for the same target was NOT written - edit the existing row instead` |
| R7 | ship gate | `the COPIED glb did not re-read green (<outcome>), so nothing was baked - the project on disk is complete, fix the file and press Ship again` |
| R8 | ship gate | `the slot's renderer changed while Ship was running, so nothing was baked - pick the slot again` |
| R9 | `ShippedTarget.Resolve` | `TARGET REFUSED: a Mesh named '<asset>' is in <n> of the bundles this addon loads (<list>) - ContentTool will not guess which one the game means` |
| R10 | `ShippedTarget.Resolve` | `TARGET REFUSED: none of the bundles this addon loads holds a Mesh named '<asset>' - <last WhyNot sentence>` |
| R11 | `Route7.ApplyProject` | `NOT APPLIED: the bake reported <n> failure(s); fix the lines above and press Ship again` |
| R12 | ship gate (catch-all) | `SHIP THREW: <Type>: <message> - the project folder is on disk; see Player.log for the stack` |
| S1 | success, resident (normal) | `baked OK - restart the game and enable '<name>' in the mod manager. Phoenix Point already loaded <bundle>, so this session keeps showing your Doctor preview.` |
| S2 | success, not resident | `baked and redirected LIVE - <bundle> now loads from the patched copy on the next load` |

## 7. Failure model — order, and what is left on disk
**Nothing is rolled back after a failed bake** — copy, sidecar and row are authored project state, retryable and cheap on the
next press, and a three-writer rollback would be the more dangerous code.
| # | Stage | Left on disk |
|---|---|---|
| 1 | target unresolved/ambiguous (R9/R10) or name invalid (R1) | nothing |
| 2 | project directory cannot be created | nothing, or an empty directory |
| 3 | `ppcontent.json` / `meta.json` template write fails | the atomic destination is absent or unchanged; a companion already written may remain |
| 4 | `ManifestFile.Load` refuses, or `Validate` refuses the row (R6/E1/E2) | existing manifest untouched, no GLB copied; a just-created empty template may remain |
| 5 | source SHA changed (R3), destination differs (R4), stray sidecar (R5) | manifest unchanged, no copy |
| 6 | GLB copy fails | `AtomicFile` removes its temp; folder + template remain, manifest has no row |
| 7 | sidecar write fails | copied GLB orphaned, manifest still has no row, any old sidecar intact |
| 8 | `ManifestFile.Save` fails (E5/E6) | original manifest intact; copy + sidecar orphaned, making the retry cheap |
| 9 | bake reports `N FAILURE(S)` (R11) | complete project; `Patch` may have left a PARTIAL patched copy — not installed, cache not marked current |
| 10 | apply refuses residency (S1) | project + good patched copy; no live claim taken; restart + enable required |
| 11 | apply throws (R12) | project + baked output; type/message shown, stack to `Player.log` |

## 8. Tests — `tests\ObjCodecTests\ProjectScaffoldTests.cs`
House pattern (`AliasTests.cs`): `internal static string Run()`, `checks += Check(cond, msg)`, temp dir deleted in `finally`,
last line `SCAFFOLD PASS, N check(s) - ...`, wired into `Program.cs` beside `ManifestTests.Run()` (`:141`). The `.csproj` gains
`ProjectScaffold.cs` + the test; `ContentMods.cs` `:75`, `Manifest.cs` `:38`, `AtomicFile.cs` `:37`, `AliasMap.cs` `:182`,
`Json.cs` `:143` are linked already.

| Arm | What it proves |
|---|---|
| `Scaffold_CreatesProjectAtomically` | new folder → `ppcontent.json` (`id`/`bundle`), `meta.json` (the §4.2 template, `ID` == `id`, ContentTool in `Dependencies`), one mesh row, `Content\Meshes\<stem>.glb`, sidecar — and `ContentMods.ProjectDir(modDir, name)` resolves to that root |
| `Scaffold_AppendsSecondRow` | a second distinct row lands; every byte outside the `replace` span unchanged; `id`/`bundle`/`meta.json` untouched |
| `Scaffold_RefusesDuplicateTarget` | same (bundle CI, asset, mesh) → R6/E4, manifest bytes identical, no copy written |
| `Scaffold_MeshCollisionPolicy` | same SHA → no-op with `MeshAlreadyPresent`; different SHA → R4, destination bytes unchanged |
| `Scaffold_SidecarRoundTrips` | `AliasMap.LoadSidecar(copy, sha, out why)` returns the same aliases and the sidecar's sha is the COPY's |
| `Scaffold_NameTable` | valid; empty; 65 chars; `..`; `a\b`; `C:\x`; `CON`; `nul.glb`; leading `-`; trailing `.` and space → each refused with R1, nothing created outside the Mods folder |

**In-game acceptance, PPCLI on `D:\PP-Instance3`** (steps only — `PPCLI\PLAYBOOK.md` maps them to lines): (1) `connect state`
answers, start a campaign, open the bench; (2) via `call`, `FitBench.ShowPrototype`, wait until the prototype is no longer
busy, take one `SlotTargets()` entry, read its `ShippedBundle`/`ShippedAsset`; (3) via `call` on `FitBench.doctor`,
`PickFile(<glb>)`, `PickTarget(target)`, poll `Ready.Outcome == ByName`; (4) set the project-name field, `Enqueue("ship")`,
poll the ship result; (5) assert on disk `Mods\<name>\ppcontent.json` + `meta.json` + `Content\Meshes\<stem>.glb` + sidecar,
the row's bundle/asset being the pair step 2 resolved; (6) assert `Player.log` holds the derivation line, `patch <bundle>:
mesh ...`, `ct_project: ALL PASS` and the expected `REFUSED: restart required` line (S1); (7) re-run `ct_project <name>` via
`connect console` → `ALL PASS`; (8) **mandatory final arm** — restart so `meta.json` is discovered, enable `<name>` BEFORE
entering the geoscape, show the prototype again, assert `BundleLive.Holds(<id>)` plus live `sharedMesh` vertex/index counts
equal the GLB's baked counts.

## 9. Follow-ups (`ponytail:` ledger)
- `ponytail:` `ShippedTarget.Resolve` opens each candidate bundle with `BundleBaker` per slot — fine for one press, O(bundles)
  if the panel ever resolves every slot eagerly; cache by bundle file then. Copy `GetAssetReferencesFromObject`'s `:316`
  public-field walk if a game update breaks the reflection.
- `ponytail:` two locator walks now (`BundleLive.Locate:195` + §4.1), fold them if a third caller appears; blocking
  main-thread bake with no cancel — the lifecycle dashboard's job, next slice.
- `ponytail:` `meta.json` written only when absent, an existing one never validated (`Package.MetaRefusal:313` is the
  validator once the wizard also packages). Extend mode, texture/material rows and deletion reuse this scaffold — add rows.

## 10. Acceptance
| id | Check | Command / evidence |
|---|---|---|
| W1 | Offline gates green | `dotnet build -c Release` → 0 errors (1 known CS0649 allowed); `dotnet run --project tests\ObjCodecTests -c Release` (exit 0, `SCAFFOLD PASS` present); `dotnet run --project tests\TargetPathTests -c Release` (exit 0); `dotnet build tools\Package\Package.csproj -c Release` → `0 Error(s)` |
| W2 | Scaffold is exact | `Scaffold_CreatesProjectAtomically` + `Scaffold_AppendsSecondRow`: `meta.json` byte-compared to the §4.2 template, manifest bytes outside the `replace` span identical |
| W3 | No overwrite is possible | `Scaffold_MeshCollisionPolicy` + `Scaffold_RefusesDuplicateTarget` |
| W4 | Target derivation disk-proved | in-game steps 2 + 5: the stored pair equals the row the bake matched, `WhyNot` answered `null` for exactly one bundle |
| W5 | A failed bake installs nothing | force a bad row, press Ship → log ends R11, `BundleLive.Holds` false, `ct-cache.key` not written |
| W6 | Honest end state | in-game steps 6 + 8: S1 shown with no live swap this session; after restart + enable, `Holds` true and live mesh counts equal the GLB's |
| W7 | Owner visual check | owner sees the replaced mesh on the prototype after the restart in step 8 |
