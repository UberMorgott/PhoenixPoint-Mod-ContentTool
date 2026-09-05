# Lifecycle dashboard — Validate / Bake / Apply / Verify / Package on one panel — design

Status: **v2, 2026-09-05**, HEAD `b3de7d7`. Merged from two Codex memos — half A (stage contracts, freshness, threading,
progress, cancellation, publication) and half B (panel, verdict table, acceptance, task split, committed as `22143b9`) —
with every load-bearing `file:line` spot-checked against source. Where the two disagreed, the source line decided; the
resolutions are named inline (§4 threading, §4.2 signature, §7 authority column, §8 Package shape). Follows the shipped
wizard slice (`2026-09-02-replace-mesh-wizard-design.md`), whose §2 defers exactly this work (`:25`) and whose forced
release/reload exclusion still stands (`:23`).
**2026-09-05, Codex SPEC CHECK of this design** (`cb6db30a…out.md`, FIX-THEN-SHIP, 11 findings + a sequencing paragraph +
three wrong facts): **all accepted**, and re-verified against source before folding in. Publication ownership is now one
fail-fast in-flight claim keyed by the canonical output directory, taken inside the shared producer `ProjectBake.Run:69`
and held through key publication and install (§5, R37), with the live-reader refusal generalized to every replacement of
claimed files (R38). Verify returns a structured carrier with a per-gate PASS/FAIL/VOID outcome instead of a count plus
text (§4.4), from ONE producer that also backs a new console path `ct_route7 verify <name>` (§4.4, §8.2 W18); S6 now
demands current ownership for every applicable target plus the declared-copy census, not `Holds`' any-claim answer (§7).
A new §4.6 Admission table fixes stage prerequisites for standalone runs, `Run all`, restart and unsupported routes; the
lifecycle pump ticks from `FitBench.Update:2104`, which runs whether or not the window is open, with the visible two-frame
gate kept for blocking main-thread steps (§6). W13 became start/arm → poll → Cancel → terminal poll (§8.2), Task 3 gained
filesystem fault gate G7 (§8.1), and the task order is Codex's: carrier → shared extraction/publication primitive →
ownership/admission/freshness → segmented producers → coordinator/seam → panel → SHIP handoff → integrated acceptance
(§10, old Task 3 split). Three wrong facts corrected: §4's "no stage is a pure worker stage" (Package's core is plain
`System.IO`, `Package.cs:15`), the §3 `BundleBaker` anchors (`Write:1064`, `File.Create:1112`, `AssetsFileWriter:1118` —
v1 and the memo were both off), and the P6 VOID anchor (`ProjectBake.cs:1917`; `:1924` is the counted comparison).

## 1. Goal
One FitBench tab showing, for ONE selected ContentMods project, five rows — **Validate → Bake → Apply → Verify → Package** —
each with its own freshness, its producer's own verdict string, and a button. Plus `Run all`, a progress track, a cancel
that never lies about what it can stop, and a log tail. No console command typed at any point, and no verdict invented by
the UI: every green line on the panel is a string a producer returned.

## 2. Non-goals (this slice ships none)
- A ZIP release (§11); a watcher or a persistent receipt database; a second dependency model in the UI.
- Forced Addressables release or bundle reload; a `Route7.Failed` reset button; a mod-manager state write; an
  installation-root picker; any dashboard-owned copy/delete of player files.
- Sound/video/publish lifecycle coverage beyond what `Route7.ApplyProject` already routes — those rows are VOID with a
  reason, never a claimed PASS (`src/Project/Package.cs:154` already refuses unbaked sounds).
- Repairing a project, editing an existing `replace` row, `VerifyMode.Extend` — still the manifest core's non-goals.

## 3. Current state
| What | Where | Why it matters here |
|---|---|---|
| bake entry | `ProjectBake.Run(string projectRoot, out int failed, out int patchFailed)` `:69` (one-arg wrapper `:51`) | **two** counts, not one: `failed` is the row verdict, `patchFailed` alone gates patch-cache publication |
| bake terminal strings | special cases `:126`–`:135`; general `ct_project: ALL PASS - <outPath>` `:405`, `ct_project: <n> FAILURE(S)` `:406` | a replacement-only or no-work project never reaches `:405`; wording is preserved, never parsed to classify |
| bake writes to the final path | `BundleBaker.Write(outPath, bundleName)` `:1064` → `File.Create(outPath)` `:1112` / `new AssetsFileWriter(outPath)` `:1118` (v1 and the spec-check memo both mis-anchored these; re-read on `b3de7d7`); own Dist pre-deleted `ProjectBake.cs:142`; `Patch` rewrites the copy even after a row refusal, then gates `:1644` | there is **no** transactional or cancellable output today — §5 is what adds one |
| freshness key | `PatchCache.Key(root, shipped)` `:43`/`:49`, `Fresh(patchedDir, key)` `:84`, `Write` `:93` → plain `File.WriteAllText` `:97` | manifest SHA1 + path/size/mtime stamps; an equal-size equal-mtime edit is invisible to it |
| declared targets | `Route7.cs:287` builds `declared`; `:309` requires `fresh && Directory.Exists(patched)` **and** every declared copy | `Fresh` alone compares key text only — the copy census is the other half |
| apply | `Route7.ApplyProject(string, string, out ApplyDisposition)` `:269` (wrapper `:261`); enum `:252`; `BakeFailed` `:344`; `NOT APPLIED …` `:349`–`:351`; key write `:353`; residency sampled BEFORE install `:397`; `Failed.Remove(modId)` `:405`; disposition `:410`–`:413` | `Redirected \| Resident \| Refused \| BakeFailed` describes ONE requested bundle, not the project |
| session block | `Route7.Failed` `:94` (**private**), `RetryHint` `:158` (**private**), checkbox refusal text `:129`–`:132`, LegacyDisk guard `:126`–`:127` | expose read-only query + hint; never the mutable set, never a bypass |
| install / residency | `BundleLive.Install(modId, IList<KeyValuePair<string,string>>)` `:55`, `Uninstall` `:127`, `Holds` `:145`, `ResidentNow` `:154`, per-bundle log `:60`, unload caveat `:138`; `Held` claims carry `Mod`/`Bundle`/`Crc` and `BundleClaims.Holds` `:296` returns true on the **first** claim of that mod | `Uninstall` restores routing and CRC; already-loaded assets survive it. `Holds` is an **any-claim** answer — never evidence that every target is served (§7 S6) |
| load-back gates | the whole read-back block runs `ProjectBake.cs:1661` (P1 VOID/`want.Count == 0`) → `:1930` (end of the P6 helper): P1 pixels `:1665`, P3 material `:1677`, P4 mesh description `:1694`; P4-bytes vertex/index buffers, unreadable → VOID `:1722`; P5 shipped skeleton/layout/range `:1740`; P6 author weights by bone name, VOID arms `:1832` (no armature) and `:1917` (target's own bone order), the counted comparison `:1924` | Verify has **no** `ct_verify` today; these gates live inside bake and must be extracted, not cloned. ~270 lines of existing code — its own task (§10 task 2) |
| catalog status | `Route7.Run(new[]{"status"})` `:210`; `CatalogVerify` `:510`–`:525` | `CatalogVerify` checks ALL published keys — a different scope from one selected project |
| package | `Package.Run(authorDir, outDir, assembly, out ok)` `:61`; nonempty refusal `:78`–`:80`; `PACKAGED …` `:180`; manual-zip instruction `:191`; `BuiltAssembly` `:211`; console wrapper recursively deletes the previous destination `ContentToolMain.cs:511` | `Run` produces a **folder**, never a ZIP; the destructive wrapper is not on the dashboard's path |
| Unity on the main thread | `ContentProject.Load` imports Texture2D `:624`/`:628`; `ProjectBake.Run` loads bundles `:341`/`:351`; clip sampling loads + instantiates `:2085`/`:2106` | decides §4's thread split — Validate/Bake/Apply/Verify each need main-thread instants |
| Unity-derived paths, main only | `BakeSelfCheck.ShippedBundlePath` reads `Application.streamingAssetsPath` `:739`; `ContentToolMain.PatchedRoot` reads `Application.persistentDataPath` `:65`; `InstallTag` reads `Application.dataPath` `:74` | worker code never calls these — the strings are captured on main **before** dispatch and passed in |
| worker-safe cores | `ContentProject.ImportModel` `:691`/`:695` (GLB read + `ModelBuild.From`), `ImportAudio` `:761`–`:772` (decode + `BuildWem`), `Package` — "plain System.IO, no UnityEngine type" by construction (`src/Project/Package.cs:15`) | these are real worker work; Package's core is pure filesystem, so §4's thread split is per-stage, not "everything touches Unity" |
| doctor / bench seams | `ModelDoctor.Message` `:71`, `Tail` `:745`–`:754`, S1 `:710`–`:712`, S2 `:714`, SHIP enqueue `:1557`/`:1572`, fields+tail `:1520`, `made.Root` `:656`/`:695`; `FitBench` tab switch + ShipPending guard `:1672`; `SlimPanel` volatile+CTS `:74`, intent drain `:102`, progress box `:270`; `SlimJob` ThreadPool + checkpoint callback `:407`/`:428` | every mechanism this slice needs already exists somewhere in the panel code |
| project inventory | `ContentProject.LoadDeclared(root)` `:289`–`:296` vs source-importing `Load` `:305`; `ContentMods` candidates/resolve `:116`–`:154`, marker `:25`, empty-name default is Sample `:153`–`:154`; `ContentMods.Enabled` `:41` and `ContentToolMain.LiveProjectIds` `:82` omit author projects | the selector enumerates roots holding `ppcontent.json`, disabled ones included |
| writers | `AtomicFile.Write(path, byte[], backupPath)` `:17` makes its **own** unique temp `:19`, flushes `:29`, then `File.Replace` `:31` when the destination exists and `File.Move` `:32` when it does not; `WriteText` `:45` | the only publication primitive this design uses — and it cannot publish a temp the caller already wrote, which §5 B5 needs (finding 11) |
| console/checkbox producers | `ct_project` → `ProjectBake.Run(root)` `ContentToolMain.cs:480`; the mod-manager checkbox and `ct_route7 apply` → `Route7.ApplyProject` → `ProjectBake.Run(root, out failed, out patchFailed)` `Route7.cs:341` → key `:353` → install | three entry points, ONE producer body — that is where §5's ownership claim belongs |
| doctor tick seam | `FitBench.Update()` `:2104` runs every frame while the component lives (only `inputBroken` `:2106` stops it); the Doctor drain is gated behind `if (open)` `:2131`, and `OnGUI` returns early when closed `:2296` | a closed window still gets `Update` — §6's lifecycle pump ticks there, **before** the `open` gate |

## 4. Stage model
Half B assumed bake and package run wholly on a worker and only apply/verify need the main thread. **Half A wins here: it
opened the seams.** `ContentProject.Load` constructs `Texture2D` (`:628`), `ProjectBake.Run` calls
`AssetBundle.LoadFromFile` (`:341`, `:351`), and clip sampling instantiates a rig (`:2085`, `:2106`). v1 then over-stated
that as "no stage is a pure worker stage" — **wrong, corrected here**: what is main-thread is the *Unity* work, not the
stage. `ContentProject.ImportModel` (`:691`, GLB bytes → `ModelBuild.From` `:695`), `ImportAudio` (`:761`, decode `:764`
→ `BuildWem` `:772`) and the whole of `Package` (plain `System.IO`, no UnityEngine type by construction,
`src/Project/Package.cs:15`) are worker work and move to the worker. What stays on main is Unity sampling, embedded
texture decoding, bundle loads, rig instantiation — and the **capture of every Unity-derived path before dispatch**:
`BakeSelfCheck.ShippedBundlePath` reads `Application.streamingAssetsPath` (`:739`), `ContentToolMain.PatchedRoot` reads
`persistentDataPath` (`:65`) and `InstallTag` reads `dataPath` (`:74`); a worker that calls one of those on a background
thread is a bug, so they are resolved to strings on main and handed in. Every stage is therefore **main → worker → main**:
main imports Unity-bound data and captures plain data plus absolute paths; the worker does the import cores, isolated
AssetsTools serialization and file comparison; main runs the Unity gates and disposes Unity objects.
Worker-only serialization requires all Unity-dependent preparation to have completed first, embedded texture decoding
included (`ProjectBake.cs:1347`). Gates whose Unity dependence is uncertain stay on main. Advance between segments from
`Tick`; each synchronous Unity call can still freeze a frame, and no token interrupts one.

The arming discipline is SHIP's, unchanged: enqueue → snapshot (including the generation) → paint the warning → execute on
a later `Tick` (`ModelDoctor.cs:595`, `:443`, `:1572`). That is a **paint gate**, not existing async execution; reuse it
for every blocking main segment.

**Axes are independent and never collapsed.** Freshness (`never` / `stale` / `fresh`) measures evidence age; the outcome
(`PASS` / `FAIL` / `VOID` / `—`) measures completed checks; S1/S2 measures apply disposition; the session block is its own
field. A row receipt stores input identity, the producer's exact returned text, structured counts/disposition, and the
completion generation. `never` = no receipt. `stale` = a receipt exists but inputs differ or a required output vanished —
an old cache directory with no key is **stale, not never** (`PatchCache.cs:84`). `fresh` = receipt matches current inputs
and the required outputs exist. A PASS is never manufactured from a filename.

Recompute `PatchCache.Key` on explicit refresh, at stage start and after completion — never in `OnGUI`. Reuse Route7's
case-insensitive non-video target selection (`Route7.cs:287`) and its full census: `Fresh(patched, key)` compares key text
only (`PatchCache.cs:84`), so also require every declared copy exactly as `Route7.cs:309` does. That proves **patch-cache**
freshness only; the project's own Dist output needs its own run receipt, and Package freshness additionally tracks
`meta.json`, the selected DLL, Dist and the staged payload, none of which the key covers.

### 4.1 Validate
Worker: `ManifestFile.Load(root/ppcontent.json)` → `ManifestFile`, then `.Manifest.Validate()` → void or
`InvalidDataException`; compute the key from the declared non-video bundles (`Manifest.cs:290`, `:200`).
Main: capture `ModRoster.Build()` → `IDictionary<string,bool>` and pass that snapshot to `ModGate.Decide(root, roster)` →
`Apply | Disabled | Unknown | NoRoster`, printing `ModGate.Why` verbatim (`ModRoster.cs:53`, `ModGate.cs:34`, `:57`).
This validates declaration structure and reports activation eligibility; it does **not** prove assets import or targets
exist. **Disabled is not malformed** — eligibility is a separate field, so a disabled project can still be authored, baked
and packaged. Reuse the existing checks (replace-row types, required fields, duplicate targets `Manifest.cs:204`; id and
bundle enforced by `Load` `:310`); extract one small shared entry only if Doctor or the console also call it. The
write-side duplicate refusal is stronger than the runtime's partial-import behaviour — label it authoring validation and
never impose it on player Apply. Freshness carries the roster verdict as its own field, because the key covers neither
activation nor meta.

### 4.2 Bake
`ProjectBake.Run(root, out failed, out patchFailed)` `:69` — the shipped wizard design's `Run(string, out int)` `:63` is
stale. `failed > 0` is FAIL even when `patchFailed == 0`; `patchFailed` alone authorizes patch-cache publication, and
unrelated import failures stay reported. Add an optional token, a progress callback and a log sink to the shared
implementation; keep the existing synchronous wrappers. **Move successful patch-key publication into that shared bake
completion and remove Route7's independent write (`:353`)**, so a standalone Bake followed by Apply does not bake twice.
Stamp only after complete patch gates and an unchanged input key; an own-bundle failure must stay visible independently.

### 4.3 Apply
Main entry `Route7.ApplyProject(root, forBundle, out how)` `:269`. Keep its stale-bake fallback, routed through the same
staged bake core, with the continuation returning to main before installation; recheck inputs immediately before install.
`fresh` means this receipt's key and current claims still match — **residency alone never proves which revision is
visible**. Sample residency before install (`:397`) and keep the existing precedence. For multiple targets: install once,
collect per-target structured dispositions inside the existing loop (`BundleLive.cs:60`) and aggregate conservatively.
That needs an OWNER, which v2 left unassigned: `Install` `:55` builds one line per target and discards the list at `:66`,
and `ApplyProject(name, null, out how)` leaves `how` at its initial `Refused` (`:271`). So add a minimal structured
overload to each — `BundleLive.Install(..., out IList<TargetInstall>)` and the matching `Route7.ApplyProject` — keeping
both existing wrappers untouched.
Never call `ApplyProject` repeatedly, and never let a null's default `Refused` stand as the project verdict. Mixed results
keep each refusal and each S1; no blanket LIVE.

### 4.4 Verify
There is no `ct_verify` in `src` or `tools`. A shared read-back helper is **extracted** from `Patch` and called by BOTH
bake and Verify: it receives the captured project expectations, shipped paths and existing copy paths. It never rewrites
and never installs. **The extraction is not one range** (plan review 2026-09-05, finding 6): `Patch` ENDS at `:1800`, so
`:1661`–`:1930` spans a method boundary. What moves is the gate body `:1656`–`:1768` plus `Live` `:1821` and `ByName`
`:1827`–`:1952`; `Check` `:2251`, `PixelsIn` `:2228`, `SamePixels` `:2241`, `Skeleton` `:2161`, `ParseClipEdit` `:1967`,
`Curves` `:1997` and `SampleClip` `:2052` stay in `ProjectBake` and only become `internal` (`Check` alone has 25+ other
callers there). Gates: P1 `:1665`, P3 `:1677`, P4 `:1695`, P4-bytes `:1722` (VOID on unreadable data), P5 `:1752`, P6
with **seven** VOID arms `:1832`/`:1849`/`:1862`/`:1872`/`:1892`/`:1920`/`:1939` and the counted comparison `:1946` —
plus the applicable clip gates through the same extraction, Unity sampling on main.

**The carrier is structured, never parsed text** (finding 2). A failure count plus a log cannot tell PASS from all-VOID:
every VOID arm returns `0` exactly like a pass (`:1835`, `:1852`, `:1866`, `:1873`, `:1894`, `:1923`, `:1942`). So the helper
returns a small struct — no interface, no builder, one type with one producer:

- `Failed`, `Passed`, `Void` counts;
- one entry per gate instance: gate id (`P1`, `P1-ctl-shipped`, `P3`, `P4`, `P4-bytes`, `P5`, `P6`), the target key, an
  `Outcome` of `PASS | FAIL | VOID`, and the producer's **exact line** as already written to the log;
- the terminal console line the producer would print, verbatim.

`Failed > 0` → row FAIL. `Failed == 0` is **not** automatically PASS: S6 additionally requires the **mandatory proofs**
below to be non-VOID. Everything else (a clip gate on a project with no clips, P6 on a skinless mesh) may be VOID without
failing the row.

**Mandatory proofs for S6** — for every declared non-video replacement row, in its own applicable subset:
- a mesh row: `P4` **and** `P4-bytes` non-VOID (`P5`/`P6` may be VOID — a skinless or same-order source has nothing to
  measure, `:1832`/`:1939`);
- a texture row: `P1` **and** `P1-ctl-shipped` non-VOID;
- a material row: `P3` non-VOID;
- plus the live-claim census of §7 S6.
Any mandatory gate VOID → Verify is VOID with the gate's own line, never PASS. Bake keeps its existing counting
behaviour: a VOID stays uncounted there, exactly as today.

Verify then refreshes `Route7.Run(new[]{"status"})` `:210`, per-target claim ownership and residency on main. Those
observations are **not** target proof: no target selected or observed → target-proof VOID; S2 is a next-load redirect, and
S1 stays restart-required even when every disk check passes. Never load a resident shipped identity merely to prove
visibility. Display the original gate lines; a P6 VOID is never relabelled PASS.

**One Verify producer, two consumers** (finding 9). The dashboard is not allowed to be the only caller: the same producer
backs a new console path `ct_route7 verify <name>`, which today refuses through `BundleClaims.Removed` (`Route7.cs:57`,
the `dryrun|verify|revert|stacktest` arm `:56`–`:63`). That arm loses only `verify`; `dryrun`, `revert` and `stacktest`
keep refusing with the unchanged text. Both consumers print the **same** terminal string from the carrier — W18 compares
them byte for byte — and the console path performs no install and no write.

### 4.5 Package
**Worker, wholly** — the class is plain `System.IO` with no UnityEngine type by construction (`Package.cs:15`), so this
is the one stage whose core needs no main-thread instant beyond capturing its paths: main resolves `root`, `outDir` and
`Package.BuiltAssembly(root)` (`:211`) and dispatches `Package.Run(root, outDir, assembly, out ok)` `:61` with a token,
log sink and progress at the copy and validation loops. The existing allowlist and refusal checks remain the sole
authority; `ok` authorizes the **folder** only. Preserve the `PACKAGED`/refusal text and never print `Package.cs:191`'s manual-zip instruction as if a ZIP
existed.

### 4.6 Admission
v1 left stage prerequisites implicit, and R28 as written contradicted two things the design keeps: the stale-bake
fallback inside `ApplyProject` (§4.3) and restart Verify (§8.2 W15). Fixed by naming admission per stage. **One
function** — `Admit(stage)` returning either `null` or the refusal — is called immediately before an intent is enqueued,
by BOTH the button path and the `Run(string stage)` seam (§8.2); `Run all` calls it per stage **as it reaches that
stage**, never up front, so an earlier stage's output can satisfy a later stage's admission. Nothing else re-implements a
dependency graph.

The governing rule: **a stage that can regenerate its own input is never refused for missing evidence.** R28 fires only
when a stage would otherwise have to read evidence that is not on disk.

| Stage | Standalone run | Inside `Run all` | After a restart | Unsupported route | Activation eligibility |
|---|---|---|---|---|---|
| Validate | always, given a selected resolvable root (else R25/R27) | first, unconditionally | admitted; receipts are `never` and it re-derives them | n/a — this is declaration-level | reports `ModGate.Decide` as its own field; `Disabled` never blocks |
| Bake | always; Bake loads and validates the manifest itself, so a `never` Validate is **not** R28 | after Validate PASS | admitted | a video-only or bundle-less project ends in its own special-case PASS string (§7) | irrelevant — authoring output |
| Apply | R25/R27/R29/R34/R36 only. **Never R28 for bake freshness**: `ApplyProject` bakes on a stale/absent key itself (`Route7.cs:312`–`:341`), and that bake reports through the same producer, filling the Bake row | after Bake did not FAIL | admitted; the key comparison decides whether the fallback bakes | a project with no non-video declared target is VOID with a reason, not a refusal | `Disabled` does not block Apply; the S1/S2 line already tells the player to enable it |
| Verify | requires the copies it reads to exist — the declared-copy census (`Route7.cs:310`–`:311`). Absent → R28 with `{prerequisite}` = `patched copies` / `never`; key mismatch → R28 `stale`. It requires **no session receipt** | after Apply; S1 → R30 | admitted — evidence is re-derived from disk, the key and live claims, which is what makes W15 possible | no applicable gate for a row → that gate VOID, §4.4's mandatory-proof rule decides the row | needs the mod enabled for the live-claim half; not enabled → live half VOID, disk half still reported |
| Package | requires a payload on disk and an empty destination — both refused by `Package.Run` itself (`:78`), not re-checked here | only if the chain did not stop; a VOID Verify stops it (§5) | admitted | n/a | irrelevant |

## 5. Progress, cancellation and the publication boundary
One job, one CTS. The worker callback replaces a volatile immutable progress reference; completion publishes the result
**before** clearing `running`; main `Tick` alone touches UI and log output. Copy `SlimPanel`'s three volatile fields + CTS
(`:74`) and `SlimJob`'s ThreadPool/checkpoint pattern (`:407`, `:428`). The full exact log lives in job-owned storage;
callbacks append completed lines and never re-read text to decide success. Console and Doctor consume the same results.

Real granularity, counts shown only with a known denominator: Validate = manifest / row / file-stamp census; Bake = import
file, target bundle, replacement row, serialization, each P gate; Apply = each `Register` observation; Verify = bundle /
asset / gate; Package = copied file, refusal check. Serialization, Unity and compression calls are indeterminate — show
the phase and the minimum fill, never an invented percentage. Cancellation is checked before and after those calls and
between file chunks.

**Publication ownership — fail fast, in the producer, not in the panel** (blocker finding 1). Panel-level ownership (§6)
only stops the panel's own buttons. The console verb (`ContentToolMain.cs:480`) and the mod-manager checkbox / `ct_route7
apply` (`Route7.cs:341`) reach the same output directory without ever asking the panel, so two runs can interleave and
leave one run's key stamped over another run's copies. The guard therefore lives where all three already pass:
**`ProjectBake.Run(string projectRoot, out int failed, out int patchFailed)` `:69`**, the single body behind the one-arg
wrapper `:51`, the console verb and `ApplyProject`.

- One process-wide in-flight set of **canonical output directories** (the patched dir and the project's own Dist,
  full-path-canonicalized, `OrdinalIgnoreCase` — the same case-blindness `Route7.cs:287` already uses). A plain `HashSet`
  under one lock: one owner per directory, no per-directory lock objects, no queue.
- Claimed at entry to that body, **fail-fast**: a second producer for a directory already in flight returns R37
  immediately and writes nothing. It never waits, never retries, never steals.
- Held **through key publication** (B5 below) and, when the caller is `ApplyProject`, through `BundleLive.Install` to the
  disposition publication (A4) — the claim is passed down, not re-taken, so Apply's own bake does not deadlock against
  itself.
- Released in a `finally`, on every path including cancellation and exception.
- A forced same-key Bake is not exempt: it is a replacement of the claimed files like any other, and it takes the claim
  and the live-reader check below exactly as a normal bake does.

**The publication boundary — the one non-obvious correctness rule.** Today `BundleBaker` writes straight to the final path
(`:1112`/`:1118`), `Patch` rewrites even after a row refusal (`ProjectBake.cs:1644`), Dist is pre-deleted (`:142`), and
`PatchCache.Write` is a plain `WriteAllText` (`:97`) that never invalidates the old key first — so cancelling mid-bake
today leaves output nobody can classify. Proposed bake steps:

- **B1** capture the key and every path.
- **B2** stream each bundle into a unique sibling temp (no extra byte-array copy). Remove the Dist pre-delete; Dist gets
  the same temp treatment.
- **B3** close the writers, run the applicable patch gates against the patch temps, and validate the own-Dist output
  separately — both failure counts retained.
- **B4** recompute the key and check cancellation. **This is the last cancellable instant.**
- **B5** enter the **non-cancellable publication**: refuse if any file about to be replaced is claimed or resident (R38),
  invalidate the old key, atomically publish each complete copy, then write the new key **LAST**, through `AtomicFile`.

**The publication primitive, stated explicitly** (finding 11). `AtomicFile.Write(path, byte[], backupPath)` `:17` makes
its **own** unique temp (`:19`) and cannot publish the temp B2 already streamed — so B5 does not "call `AtomicFile.Write`"
for bundles. Extend the existing class with one operation, `Publish(tempPath, path, backupPath)`, holding the swap that
`Write` already performs: `File.Replace(tmp, path, backupPath)` when the destination exists (`:31`), `File.Move` when it
does not (`:32`), with its OWN orphan-temp `finally`. `Write` is then two lines — write the bytes to its temp, call
`Publish` — **while KEEPING its existing outer cleanup `:34`–`:40`**: that guard also covers a failed stream open, write
or flush, none of which ever reach `Publish`, so moving it wholesale would strand `Write`'s temp on exactly the paths it
protects today. One swap, two temps, each cleaned by its own owner. The byte overload keeps its current behaviour, flush
included (`:29`). `Publish` returns `void`. Where a file must be created and never overwritten, the absent-only `File.Move` arm is
used directly, as the wizard slice already requires. The key itself is a string and keeps using `WriteText` `:45`.

Cancelling before B5 deletes only owned temps and preserves the previous outputs. Once B5 begins it finishes and reports
completion — cancellation cannot interrupt it. If publication fails midway the files are individually complete but the key
is absent: report FAIL and forbid Apply until a repair bake. If key invalidation itself fails, publish nothing. This is a
publication ordering, **not** an atomic multi-file transaction and not a crash rollback.

Apply follows the same shape: **A1** complete and revalidate the disk work; **A2** final cancel check; **A3** the
main-thread `Install` loop, with no cancellation and no yields; **A4** publish the dispositions, then release the claim.
A late cancel stops later stages, never a completed redirect. No automatic `Uninstall` (`BundleLive.cs:127`) — it
restores routing and CRC but loaded assets survive (`:138`–`:141`).

**The live-reader refusal is general, not a stale-bake special case.** ANY replacement of a claimed file is refused while
this mod's claims (**`BundleClaims.Find(bundleFile)` `:221`, comparing `c.Mod` and `c.Path` — `Held` `:182` is a private
`List<BundleClaim>` and is not a per-target query**; not `Holds`' any-claim answer) or resident copies could be
consuming it — a stale bake, a fresh bake, an explicitly forced same-key Bake, and a repair bake alike. The refusal is
R38 and the answer is a restart boundary, never rewriting beneath live readers.

`Run all` runs serially in displayed order and stops immediately on FAIL, exception, refusal or acknowledged cancellation;
earlier successful rows persist and are never erased. A gate VOID alone does not fail a row, but an absent mandatory proof
stays VOID and blocks completion — so `Run all` stops at a restart-dependent Verify and does **not** go on to Package. An
explicit standalone Package remains available for valid disk output. Non-applicable Apply/Verify rows are VOID with a
reason and do not stop the chain. `Failed` is never cleared on Validate or Bake success; today's removal happens after
`Install` (`Route7.cs:405`) and that stays the only clearing path.

## 6. Panel
A third FitBench tab, `Lifecycle`. Doctor remains the mesh-authoring workflow; a successful SHIP selects the produced
project and opens Lifecycle **without** running another stage.

```text
[ FIT ] [ MODEL DOCTOR ] [ Lifecycle ]
Project  [ < ] [ ExampleProject                 ] [ > ] [ Refresh ]
Session  [ ready / restart required / session block                ]
Stage       Freshness  Outcome  Installation      Action
Validate    never      —        —                 [ Run ]
  —
Bake        never      —        —                 [ Run ]
  —
Apply       never      —        — / S1 / S2        [ Run ]
  —
Verify      never      —        —                 [ Run ]
  —
Package     never      —        —                 [ Run ]
  —
Progress [ fixed 240px track / fraction fill ]  [ phase / — ]
[ Run all ] [ Cancel ]  [ Message / — ]
Log tail [ fixed-height Tail text; empty placeholder before run    ]
```

- **Layout is constant.** Selector, global status, five rows, progress, `Run all`/`Cancel` and the log tail are always
  drawn — placeholders and disabled controls before any result exists. Result text changes; the control sequence does not.
- **Selector.** Enumerate sibling Mods directories, ContentTool's own child directories and roster roots; keep the roots
  holding `ppcontent.json`, canonicalize and deduplicate (`ContentMods.cs:116`–`:154`, marker `:25`). Do **not** use
  `ContentMods.Enabled` (`:41`) or `LiveProjectIds` (`ContentToolMain.cs:82`) — they omit author projects. Disabled
  projects are included. Use `ContentProject.LoadDeclared` (`:289`), never source-importing `Load` (`:305`), and do the
  enumeration outside `OnGUI` drawing. Selection binds to the canonical root, shows the root on a duplicate name, and
  passes the absolute root to Apply. Keep the selected name across a refresh if it still exists; an empty selection stays
  empty and a deleted target never silently switches. No arbitrary path text box.
- **Ownership.** While a run owns the job, disable project selection, `Refresh`, every stage button, `Run all`, competing
  SHIP/source-edit actions and FitBench tab changes. `Cancel` stays enabled only until requested or until a
  non-interruptible main-thread segment begins, then shows `Cancel unavailable during {stage}.` Closing the window neither
  releases ownership nor implies cancellation. This is the **panel's** ownership only — it stops buttons, nothing else;
  the producer-level claim of §5 (R37) is what stops the console verb and the checkbox, and the two are independent.
- **The lifecycle pump ticks whether or not the window is open.** The SHIP arming discipline is a *paint* gate:
  `DoShip` runs only once `shipLabelPainted` is true (`ModelDoctor.cs:443`) and is otherwise cancelled after two ticks
  (`:455`) — and a closed bench neither paints (`FitBench.OnGUI` returns at `:2296`) nor drains the Doctor (the
  `if (open)` gate at `:2131`). Reusing that gate unchanged for the dashboard would abandon a run the moment the window
  closed. So the pump is called from **`FitBench.Update()` `:2104`, before the `open` gate** — that method runs every
  frame while the component lives, stopped only by `inputBroken` (`:2106`). Policy while the window is closed: the worker
  keeps running and publishes its result; the job's completion, log and receipts are still recorded; `Cancel` remains
  available through the seam (§8.2). Steps that must run on the main thread still run — they need a frame, not a paint —
  **except** the ones the visible two-frame gate protects: a blocking main-thread segment (Apply's `Install`, the Unity
  Verify gates) waits until the panel is open and has painted its warning, exactly as SHIP does. A run that ends while
  closed shows its terminal result when the tab is next opened; nothing is re-run to produce it.
- **Tail.** One fixed-height tail holding only this run's captured stage log, retained until the next run starts. Reuse
  `ModelDoctor.Message` (`:71`) and its `Tail` semantics (`:745`–`:754`); expose or move that helper only as far as shared
  callers and the offline tests need. A row verdict never depends on whichever line currently ends the tail.
- **Progress** reuses `GUILayout.Box("", GUILayout.Width(Mathf.Max(1f, 240f * done)), GUILayout.Height(6f))`
  (`SlimPanel.cs:270`) inside a fixed track. Existing anchors: tab switch + ShipPending guard `FitBench.cs:1672`; SHIP
  fields and tail `ModelDoctor.cs:1520`; enqueue + Repaint acknowledgement `:1557`/`:1572`; intent drain and worker
  snapshots `SlimPanel.cs:102`.
- **SHIP handoff** uses the captured absolute `made.Root` (`ModelDoctor.cs:656`, `:695`), never a name rebuilt from a
  label, and transfers SHIP's authoritative Apply result; unobserved stages stay `never`. Change tabs only after SHIP
  releases ownership and the current GUI event finishes.
- **S1 barrier.** Apply may PASS while Verify is VOID. `restart required` shows in the global status and the Apply
  installation column; a restart alone turns nothing green — re-observe the project and run Verify. No dismiss button and
  no forced unload.
- **Session block.** Show it whenever the selected id is in the actual `Route7.Failed` set (`:94`); disable dashboard Apply
  and `Run all` while diagnosis and author-output work stay available under the normal path guards. No reset button and no
  direct set mutation. The checkbox suppresses retry (`:129`); explicit console Apply bypasses that suppression and **the
  dashboard must not use that bypass**. The badge clears only when the set actually clears — a new process, or a successful
  producer operation reaching `:405`. Fixing sources, refreshing or passing Validate clears nothing.
- **Player-installation writes stay inside the existing Route7 path**, matching both the LegacyDisk guard (`:126`–`:127`)
  and Failed suppression before invoking `ApplyProject` (`:269`). No dashboard copy, delete, forced unload,
  installation-root picker or mod-manager setting write.
- **Package** calls `Package.Run` directly into a new directory under `%LOCALAPPDATA%\ContentTool\Packages\<project>\<run-id>`,
  outside the game installation, and displays the returned path. Never the console wrapper — it recursively deletes the
  previous destination (`ContentToolMain.cs:511`) — and `Package.Run` itself refuses a nonempty destination (`:78`). A
  repeated run gets a new directory; there is no overwrite control.

## 7. Verdict and refusal contract
Outcome and freshness are structured fields from §4. The UI stores and displays the producer's final string **verbatim**;
it never infers success from log contents, file existence, an empty exception field or a green previous row. Braces are
substitution slots. All existing producer refusals are forwarded unchanged; this table only adds guards after the
wizard's R24 (`2026-09-02-replace-mesh-wizard-design.md:346`) — R1–R24 are not reimplemented. R25–R36 are dashboard
guards; **R37 and R38 are producer guards** and are therefore printed by the console verb and the checkbox as well.

| ID | Condition / outcome | Exact message payload | Authority |
|---|---|---|---|
| R25 | No project selected; VOID | `Lifecycle: select a ContentMods project.` | NEW; admission guard |
| R26 | Another run owns the job; VOID | `Lifecycle: busy running {stage}.` | NEW; current job and result untouched |
| R27 | Selected root vanished or no longer resolves; VOID | `Lifecycle: selected project is unavailable; refresh the project list.` | NEW; admission guard |
| R28 | Required evidence missing or stale; VOID | `Lifecycle: {stage} blocked; {prerequisite} is {freshness}.` | NEW; **§4.6's table** is the only prerequisite source — no independent UI dependency graph. Fires for Verify with absent or stale copies; never for Apply on a stale bake, which `ApplyProject` re-bakes itself |
| R29 | Selected id in `Route7.Failed`; VOID | `'{id}' failed to bake earlier in this session - not baking it again. Fix the lines it printed, then {RetryHint}` | Existing checkbox text `src/Bake/Route7.cs:129`–`:132`; reuse `RetryHint` `:158`; no bypass button |
| R30 | Verify cannot establish live routing because Apply is S1; VOID | `Verify: VOID - restart required for '{name}'.` | NEW; barrier, not failure |
| R31 | Cancellation acknowledged before successful publication; VOID | `Lifecycle: {stage} cancelled; later stages were not run.` | NEW; shared cancellation result; never claims rollback |
| R32 | Project inputs changed during the run; VOID | `Lifecycle: project changed during {stage}; validate again.` | NEW; the §5 B4 key recheck owns it |
| R33 | Unsupported callable stage token; VOID | `Lifecycle: unknown stage '{stage}'.` | NEW; accepted tokens are exactly `Validate`, `Bake`, `Apply`, `Verify`, `Package`, `All` |
| R34 | Game-root or destination admission guard fails; VOID | `Lifecycle: refused a write outside the mod-manager apply path or author output.` | NEW; fails before any write |
| R35 | Actual patch failure; FAIL | `NOT APPLIED: patching the shipped bundle(s) reported {n} failure(s), named in the P0/REFUSED line(s) above; nothing was installed and no copy was marked current.` | Existing `src/Bake/Route7.cs:349`–`:351` (disposition set `:344`); display the producer-returned line |
| R36 | LegacyDisk guard denies Apply; VOID | `Lifecycle: Apply blocked while legacy disk patching is active.` | NEW line for the existing guard `src/Bake/Route7.cs:126`–`:127`; no migration or repair button |
| R37 | Another producer already owns this project's output directory; VOID | `ct_project: '{dir}' is already being written by another run - nothing was baked. Wait for it to finish, then bake again.` | NEW; §5's fail-fast claim in `src/Bake/ProjectBake.cs:69`, so the console verb (`src/ContentToolMain.cs:480`) and the checkbox (`src/Bake/Route7.cs:341`) get the same refusal the panel does |
| R38 | A file about to be replaced is claimed or resident; VOID | `ct_project: '{file}' is being served to the game right now, so it was not rewritten - restart the game and bake again.` | NEW; general live-reader refusal (§5), applies to a stale, fresh, repair or forced same-key bake alike; per-target claim lookup, never `BundleLive.Holds` |
| S1 | Apply PASS / `Resident` | `applied - restart the game and enable '{name}' in the mod manager. Phoenix Point already loaded {bundle}.` | Existing `src/Dev/ModelDoctor.cs:710`–`:712`; append exactly ` This session keeps showing your Doctor preview.` iff the originating Doctor has `HasPreview` |
| S2 | Apply PASS / `Redirected` | `applied and redirected LIVE - {bundle} now loads from the patched copy on the next load` | Existing `src/Dev/ModelDoctor.cs:714` |
| S3 | Validate PASS | `Validate: PASS - '{name}'.` | NEW; only after `PatchCache.Key` and `Manifest.Validate` complete — Validate has no existing success string (`src/Project/Manifest.cs:200`) |
| S4 | Bake PASS | `ct_project: ALL PASS - {outPath}` | Existing `src/Bake/ProjectBake.cs:405`; preserve the producer's special-case outcomes instead of fabricating this line |
| S5 | Bake FAIL | `ct_project: {n} FAILURE(S)` | Existing `src/Bake/ProjectBake.cs:406` |
| S6 | Verify PASS | `Verify: PASS - load-back gates passed; {n} of {n} declared target(s) served from this project's copies for '{name}'.` | NEW; §4.4's mandatory proofs **plus a per-target census** — NOT `BundleLive.Holds` (`src/Bake/BundleLive.cs:145`), which delegates to an any-claim predicate (`src/Bake/BundleClaims.cs:296`: one matching `c.Mod` and it returns true) and so passes with two valid copies and a single claimed target. Requires, for EVERY applicable target: a claim owned by this mod id whose path is this project's patched copy, and the declared-copy census of `src/Bake/Route7.cs:310`–`:311`. Any target missing → VOID with that target named; individual refusals are preserved, never aggregated into one sentence. Not visual correctness |
| S7 | Package PASS | `PACKAGED {n} file(s), {bytes} B into {outDir}` | Existing `src/Project/Package.cs:180`; requires `out ok == true` |

- A backend failure keeps its own string. If Validate or Verify throws with no producer verdict, the shared fallback is
  `Validate: FAIL - {reason}` / `Verify: FAIL - {reason}`; the tail keeps the detail, and every caller uses that same
  single-line reason.
- Transient `Message` strings, never terminal verdicts: `Queued: {stage}`, `Running: {stage}`,
  `Cancel requested; waiting for {stage} to stop.`, `Cancel unavailable during {stage}.` After a publication has already
  succeeded, keep that stage's PASS and cancel only the continuation:
  `Lifecycle: cancelled after {stage}; later stages were not run.`
- Idle row placeholder `—`; global ready placeholder `Ready.`. The Failed and restart badges are independent of the last
  `Message` and cannot be hidden by a later successful Package.
- **Do not** use `ct_catalog: PASS - the game's own Addressables served the mod's own bundle, and nothing was written to the installation`
  as a selected-project Verify: `CatalogVerify` checks all published keys (`src/Bake/Route7.cs:510`–`:525`), a different scope.
- The wizard design's old S1/S2 wording (`baked OK`, `baked and redirected LIVE`, `:347`–`:348`) is **stale**. Use the
  current Doctor strings through the shared formatter extracted there.
- Preserve the bake special-case strings verbatim from `src/Bake/ProjectBake.cs:126`–`:135`:
  `nothing to bake - put .png/.jpg under Content\Textures\, .glb under Content\Models\ or .wav under Content\Audio\`;
  `ct_project: ALL PASS - this project has no bundle of its own; the patched copy(ies) above are the whole output`;
  `ct_project: ALL PASS - nothing needed patching: none of this project's {n} replacement(s) names a shipped bundle, so no copy was written - the video row(s) above are served live by ct_video`.
  Their outcome comes from the producer; the wording is never parsed to classify them.
- Preserve the Package refusal exactly:
  `REFUSED: {outDir} already holds files. Name a folder that does not exist yet - a package is built from nothing, so no leftover of a previous run can be shipped by accident.`
  (`src/Project/Package.cs:78`–`:80`).

## 8. Acceptance
### 8.1 Offline
Extend the existing `tests\ObjCodecTests` executable — no Unity player, no mock UI framework, no new test dependency. Link
the minimal pure state/result/Tail code the way the harness links other pure source (`ObjCodecTests.csproj:4`, `:40`,
net472); register a new `Run()` beside the unconditional scaffold gate (`Program.cs:142`). Do not invent a test-name CLI
filter — only `--bake` and `--u9probe` exist (`Program.cs:60`–`:63`). State-transition inputs are values; the pure reducer
touches no filesystem and no Unity. **G7 is the deliberate exception**: publication safety is a filesystem property and a
reducer cannot prove it, so that gate creates a real temporary directory, writes real bytes with plain `System.IO`, and
deletes it in a `finally`. Still no Unity, still no new dependency.
Run: `dotnet run --project tests\ObjCodecTests -c Release` → exit 0, the current `PROJECT-SCAFFOLD PASS, ` marker
(`tests/ObjCodecTests/ProjectScaffoldTests.cs:770`; the wizard design's `:426` abbreviates it) and a new `LIFECYCLE PASS`.

| Gate | Arm / assertions |
|---|---|
| G1 state | Table inputs for missing receipt, same key, changed key, previous FAIL, S1, S2, `Failed` membership. Exact freshness/outcome/badges; a stale PASS stays a stale PASS; `never` is not VOID. Verify carriers: all-PASS, one FAIL, a mandatory gate VOID and an **all-VOID** carrier — zero failures with a mandatory VOID is VOID, never S6, and the decision reads the per-gate outcomes, never the text |
| G2 wording | Golden expectations for R25–R38 and S1–S7 including substitution, backend passthrough and the bake special cases. Compare Doctor / shared producer / dashboard / console string identity; no independently reconstructed sentence |
| G3 Tail | Empty, fewer/exactly/more than the limit, CRLF and LF, trailing newline, one long line. Freeze the reused helper's current semantics; the constant output slot count is a panel rule, not a fabricated Tail behaviour |
| G4 cancel | Cancel before dispatch; while the worker is at a cooperative boundary; after successful publication; repeated Cancel. One terminal result, busy retained until worker completion, no next-stage dispatch, no false rollback, no late result overwriting a newer run |
| G5 sequence | All succeeds; each stage fails in turn; the S1 barrier; a prerequisite refusal; cancellation. Invocation order and count, first stop position, earlier receipts unchanged; Package is not entered after `Run all` stops at Verify |
| G6 admission | The §4.6 table row by row: standalone / `Run all` / post-restart / unsupported-route / activation for each stage, plus missing, deleted and duplicate project names; key changed before commit; Failed suppression; nonempty package output; a write routed outside the allowed roots. Apply is **not** refused for a stale bake (the fallback owns it); Verify is refused for absent copies. Rejection happens before the write callback is entered |
| G7 publication faults | **Real files in a temp directory**, plain `System.IO`, no Unity — the reducer proves none of this. **Driven through the production publication file** (plan review, blocker 2): the B5 ordering lives in a UnityEngine-free `src/Bake/Publication.cs` that is LINKED into `ObjCodecTests` beside `Package.cs`/`Manifest.cs`, and faults are injected into it. A gate that reimplements invalidate/swap/key stays green while the real bake stamps the key first, and the already-linked `AtomicFile.Publish` only covers ONE file, not the ordering. Key invalidation fails → nothing published, previous outputs intact; a failure between two copy replacements → the completed files are complete, the key is absent, the row is FAIL and Apply is refused until a repair bake; the key write itself fails → same; cancel requested at B4 → temps deleted, previous outputs byte-identical; cancel requested inside B5 → publication completes and the run reports completion; a competing admission (R37) while a claim is held → refused immediately, the holder's bytes untouched; the claim is released on success, on refusal and on exception. Assert **actual bytes**, key presence/content, and ownership release in every arm |

### 8.2 In-game, PPCLI on `D:\PP-Instance2`
`connect state` proves readiness, not installation identity (`E:/DEV/PhoenixPoint/PPCLI/src/PPBridgeMain.cs:205`–`:212`).
PPCLI validates the endpoint PID/executable and filters `.install` to `-PPRoot`
(`E:/DEV/PhoenixPoint/PPCLI/ppcli.ps1:212`–`:221`, `:249`–`:259`); additionally require the seam's in-process canonical
game root to equal the test root — a mismatch fails closed before any fixture is created.

```powershell
$ppcli = 'E:\DEV\PhoenixPoint\PPCLI\ppcli.ps1'
$ppRoot = 'D:\PP-Instance2'
$ppProfileId = '76561197996210592'
function D([string]$member, [object[]]$a = @()) {
    $json = @{ op='invoke'; type='Morgott.ContentTool.Dev.LifecycleDashboard'; member=$member; args=$a } | ConvertTo-Json -Compress -Depth 5
    & $ppcli -PPRoot $ppRoot -ProfileId $ppProfileId connect call $json
}
function C([string]$command, [object[]]$a = @()) {
    $json = @{ command=$command; args=$a } | ConvertTo-Json -Compress -Depth 5
    & $ppcli -PPRoot $ppRoot -ProfileId $ppProfileId connect console $json
}
function Shot([string]$row) {
    $json = @{ path=('C:\Temp\' + $row + '.png') } | ConvertTo-Json -Compress
    & $ppcli -PPRoot $ppRoot -ProfileId $ppProfileId connect screenshot $json
}
& $ppcli -PPRoot $ppRoot -ProfileId $ppProfileId connect state
D 'Snapshot'
```

RPC and console JSON follow `E:/DEV/PhoenixPoint/PPCLI/PLAYBOOK.md:306`, `:319`; screenshot JSON `:323`–`:324`. The class
namespace follows `src/Dev/ModelDoctor.cs:13` and `src/Dev/FitBench.cs:28`. Every `D 'Snapshot'` after an asynchronous
`Run`/`Acceptance` denotes **bounded polling until that run reports `busy=false`**; a timeout is a failed row, not a pass.
Capture queued/running screenshots before waiting where specified.

**The seam's transport is fixed by PPCLI, not chosen** (plan review, blocker 3). `Reflect.Project` `:1080` never
enumerates or walks properties: a non-trivial reference returns `{h, type}` and a collection a handle plus a count, with
only primitives, strings, enums and a ≤4-primitive-field value type inlined (`:1150`–`:1156`). A snapshot object would
therefore arrive as one unusable handle. So **every seam method is `public static` and returns a bounded JSON `string`**
— static because `Invoke` filters to statics when no target is given (`:479`–`:480`), bounded because `Protocol.Clip`
truncates at 2000 chars (`PPCLI/src/Protocol.cs:56`, `:256`) into JSON that will not parse. `Snapshot` is consequently
**sectioned**: `Snapshot("")` is a compact poll header (ids, `busy`, `stage`, cancel flags, `claimHeld`, `barrierArmed`,
`barrierRunId`, and per row only stage/freshness/outcome), and `Snapshot("<stage>")` / `("log")` / `("s1s2")` fetch one
verbatim payload at a time. `Run` returns the accepted `runId` and every poll matches it, so a poll cannot read a newer
run's state. The mod composes these with the existing `Morgott.ContentTool.Import.JsonWriter` (`src/Import/Json.cs`), so
no JSON dependency is added.

The public static seam on the dashboard class is `Open(string projectName)`, `Run(string stage)`, `Cancel()`,
`Snapshot(string section)`, `Acceptance(string scenario)` — all returning `string`. `Open("")` clears the selection explicitly and is never passed to the existing name resolver, whose
empty-name default is Sample (`ContentMods.cs:153`–`:154`); a unique name resolves to a canonical root before
`LoadDeclared`, which takes a root holding `ppcontent.json`, not a name (`ContentProject.cs:289`–`:296`); an ambiguous name
is rejected. `Run` enqueues the same intent as the button, returns promptly and never performs a synchronous Apply from the
RPC call — the ordinary update pump owns dispatch even when the window is closed. `Snapshot` returns the canonical game
root, the selected canonical root/id, run id, busy/current stage, cancel requested/acknowledged, five freshness/outcome/
verdict entries, S1/S2, actual `Failed` membership, stage-start counts, the producer claim currently held (§5) and — the
addition v2 makes — **the test barrier's observation: whether it is armed, and the run id it is holding**. Without that
field W13 cannot tell "the worker is parked at the barrier" from "the worker has not started yet", and the row degenerates
into a sleep. `Snapshot` is observational and cannot validate, apply or clear anything.

Test-only `Acceptance(string scenario)` lives on the same class and refuses any game root but the test instance. Scenarios
prepare isolated named fixtures, arm narrow gates and call the public seam and the real producers — never installing a
fabricated PASS/FAIL snapshot and never setting `Failed`, residency, `Holds` or verdict fields directly. `prepare` creates
`DashboardValid`, `DashboardPatchFail` and `DashboardAuthor` (the never-applied fixture the baking rows use, see
**Fixture isolation** below); `resident` prepares `DashboardResident` and actually loads its target
bundle; `cancel-bake` arms the worker barrier at the first supported cancellation boundary; `change-source` changes only
the selected fixture; `ship` drives the real Doctor fixture through its existing selection and `Enqueue` path;
`enable-resident` invokes the actual mod-manager enable callback after a restart. `arm-cancel-bake` **arms** the worker
barrier for the next run and returns immediately, publishing its armed state and run id through `Snapshot` — and
`barrierArmed` is published only once a worker is ACTUALLY parked, never on arming alone, or W13's first poll passes
before the run exists. It parks a **worker** and never the main-thread RPC pump, which would make `Snapshot`
unanswerable. It releases the barrier on the same `Cancel()` the UI calls and lets normal worker completion publish VOID
— no sleep-based race, no `Thread.Abort`, no synthetic success, no detached worker.

W8 onward continues the wizard's W1–W7 baseline; W5 failed-bake isolation, W6 restart/enable proof and W7 owner visual
inspection are retained (`2026-09-02-replace-mesh-wizard-design.md:431`–`:433`). New screenshots use an enabled upscaler as
the owner requires; no rendering-setting workaround is part of this slice.

| Row | Exact calls, in order | Required evidence |
|---|---|---|
| W8 empty | `D 'Open' @('')`; `D 'Run' @('Validate')`; `D 'Snapshot'`; `Shot 'W8'` | Open-empty allowed; Run yields R25. Five `never / —` rows, placeholders, unavailable actions disabled, no layout exception |
| W9 selector/Validate | `D 'Acceptance' @('prepare')`; `D 'Open' @('DashboardValid')`; `D 'Run' @('Validate')`; `D 'Snapshot'`; `Shot 'W9'` | Disabled fixture included, exact root bound, S3. Previous/next/Refresh use the same selection path; a duplicate name is disambiguated by root |
| W10 happy chain | `D 'Open' @('DashboardValid')`; `D 'Run' @('All')`; `Shot 'W10-running'`; `D 'Snapshot'`; `Shot 'W10'` | Clean process, target not resident: five rows PASS, Apply S2, exact producer strings, Package writes a new external path. A resident target makes this run W12 instead — it cannot count as W10 |
| W11 first failure | `D 'Open' @('DashboardPatchFail')`; `D 'Run' @('All')`; `D 'Snapshot'`; `Shot 'W11'` | Fixture passes manifest validation but causes a real bake patch-gate failure. Bake FAIL; Apply/Verify/Package start counts stay zero for this run; prior receipts retained |
| W12 restart required | `D 'Acceptance' @('resident')`; `D 'Open' @('DashboardResident')`; `D 'Run' @('All')`; `D 'Snapshot'`; `Shot 'W12'` | A really resident bundle: Apply PASS/S1 with the exact S1 text, Verify VOID/R30, no Package dispatch, no forced unload |
| W13 cancel | `D 'Open' @('DashboardAuthor')`; `D 'Acceptance' @('arm-cancel-bake')` (arms the barrier for the NEXT run and returns at once — it never waits for a completion that Cancel is what produces); `D 'Run' @('Bake')`; **bounded poll** `D 'Snapshot'` until `barrierArmed=true` **and** `barrierRunId` equals this run's id; `Shot 'W13-armed'`; `D 'Cancel'`; **bounded terminal poll** `D 'Snapshot'` until `busy=false`; `D 'Cancel'` again; `D 'Snapshot'`; `Shot 'W13'` | v1's row waited for completion without ever issuing Cancel, and the barrier only releases on Cancel (§5) while Snapshot polling waits for `busy=false` — a guaranteed deadlock. Fixed order above. Assertions: the first poll observes THIS run parked at the barrier (a timeout is a failed row, not a pass); Cancel is the same entry point the button calls; the terminal poll ends in R31/VOID with one terminal receipt; later stage start counts stay zero; busy clears only after acknowledgement **and** worker completion; the second Cancel produces no duplicate result and no second receipt; the previous outputs are byte-identical |
| W14 Failed block | `C 'ct_route7' @('apply','DashboardPatchFail')`; `D 'Open' @('DashboardPatchFail')`; `D 'Run' @('Apply')`; `D 'Snapshot'`; `Shot 'W14'` | Console setup really sets `Failed` through a patch failure. Dashboard admission R29; Apply and `Run all` disabled, no direct retry, no set clearing. Validate/refresh cannot clear the badge |
| W15 restart proof | After a normal restart and the identity preflight: `D 'Acceptance' @('enable-resident')`; `D 'Snapshot'`; `D 'Open' @('DashboardResident')`; `D 'Run' @('Verify')`; `D 'Snapshot'`; `Shot 'W15'` | The real enable callback, fresh load-back and the **per-target** claim/path census of S6 (not `Holds`); S6 only if all pass, and a partially claimed fixture must produce VOID naming the unserved target. New-session `Failed` observed clear. Missing fresh evidence yields a refusal, never an inherited green row; Validate/Bake may be run first if §4 cannot re-derive their receipts |
| W16 stale | `D 'Open' @('DashboardValid')`; `D 'Acceptance' @('change-source')`; `D 'Run' @('Verify')`; `D 'Snapshot'`; `Shot 'W16'` | The existing receipt becomes stale; Verify is blocked by an actual key comparison. No old PASS promoted to fresh, no automatic Apply. Run after W10 in the same process |
| W17 SHIP landing | `D 'Acceptance' @('ship')`; `D 'Snapshot'`; `Shot 'W17'` | A real successful Doctor SHIP opens Lifecycle after GUI dispatch, selects exactly `made.Root`, transfers the same Apply string and disposition, and launches no duplicate bake/apply/package |
| W18 console parity/package | `D 'Open' @('DashboardAuthor')`; `D 'Run' @('Validate')`; `D 'Snapshot'`; `C 'ct_project' @('DashboardAuthor')`; `D 'Run' @('Bake')`; `D 'Snapshot'`; `D 'Run' @('Package')`; `D 'Snapshot'`; `C 'ct_route7' @('verify','DashboardAuthor')`; `D 'Run' @('Verify')`; `D 'Snapshot'`; `Shot 'W18'` | Final bake payload matches for the same unchanged project and key, **and matches the baseline captured before task 2's extraction, bytes and gate log**. Package matches its captured producer payload with `ok=true` and writes only a new external directory; the previous package stays intact. **Verify parity:** the console verb's terminal line and the dashboard's Verify verdict are the **same string, character for character**, both out of the one producer of §4.4; the console call installs nothing and writes nothing; `ct_route7 dryrun/revert/stacktest` still print the unchanged removal text (`src/Bake/Route7.cs:60`–`:63`) |
| W19a closed run, worker-only | `D 'Open' @('DashboardAuthor')`; `D 'Run' @('Package')`; close the bench with the chord while it runs; bounded poll until `busy=false`; reopen; `D 'Snapshot'`; `Shot 'W19a'` | `Package` is plain `System.IO` end to end (`Package.cs:15`) and has no main-thread final segment, so it can genuinely finish closed. §6's pump: the run completes with the window closed, the receipt and log are recorded, and reopening SHOWS the terminal result without re-running anything. `Cancel` is reachable through the seam while closed |
| W19b closed run, main-thread arm | `D 'Open' @('DashboardAuthor')`; `D 'Run' @('Bake')`; close the bench while it runs; `D 'Snapshot' @('')` **once** (assert `busy=true` and the parked-for-paint state — do NOT terminal-poll); reopen and let it paint; then poll until `busy=false`; `Shot 'W19b'` | v1's single row deadlocked by design: it terminal-polled a run whose final phase waits for an open, painted panel (`:316`–`:319`), so `busy=false` could never arrive. Split. While closed the row reports it is waiting for a painted panel and nothing is re-run; after reopening it resumes and terminates once |
| W20 competing producer | `D 'Open' @('DashboardAuthor')`; `D 'Acceptance' @('arm-cancel-bake')`; `D 'Run' @('Bake')`; poll until parked at the barrier; `C 'ct_project' @('DashboardAuthor')`; `D 'Cancel'`; poll until `busy=false`; `D 'Snapshot'`; `Shot 'W20'` | The blocker fix, proven from the OTHER entry point: the console verb hits R37 and returns immediately, writing nothing — no second bake, no key stamped over the parked run's copies. After the cancel the claim is released and a plain `C 'ct_project'` succeeds |

Suite order: W8 → W9 → W10 → W16 → W11 → W13 → W20 → W19a → W19b → W14 → W12 → restart → W15 → W17 → W18. Rebuild and
revalidate after W16 before further success cases. W12/W15 are a pair — preserve the fixture. For W18, explicitly reopen
and revalidate `DashboardAuthor` if SHIP selected another project.

**Fixture isolation** (plan review, blocker 4). W10's Apply installs `DashboardValid` and the claim survives the process
(`BundleLive.cs:96` → `BundleClaims.cs:270`, same-mod re-claim kept `:258`–`:267`), so §5's R38 correctly refuses every
later re-bake of it. `prepare` therefore also creates **`DashboardAuthor`**, an uncontested fixture that is never
applied, and the baking rows W13, W18, W19a/b and W20 use it. `DashboardValid` stays with W9, W10 and W16 — W16 runs
`Verify` only, which never rewrites, and needs W10's receipt to make stale. W10 additionally runs against an **enabled**
fixture (W9's disabled-fixture arm is about listing, not verifying; §4.6's activation column requires enablement for
Verify's live half).
A screenshot response with `ok=true` is **not** panel proof: inspect the PNG for all five rows, the status text, progress,
tail and disabled controls. Response fields are documented at `E:/DEV/PhoenixPoint/PPCLI/src/Screenshot.cs:162`, and the
targetTexture branch may also write a separate `.scene.png` (`:169`–`:175`) — use the image that actually contains IMGUI.
Visual mesh acceptance remains the wizard's W7.

## 9. Risks and defaults
| Risk | Default |
|---|---|
| `PatchCache.Key` stamps by path/size/mtime (`:49`), so an equal-size equal-mtime edit evades it | Preserve the existing contract and expose `Refresh` plus a forced Bake; never advertise cryptographic content freshness |
| Bake/apply cannot move wholesale to a worker (Unity on main, `ProjectBake.cs:341`/`:2106`, `ContentProject.cs:628`) | Main → worker → main per stage; uncertain gates stay on main; no claim that a token interrupts a Unity call |
| A blocked main thread cannot repaint or take clicks during Apply/Verify | Honest queued/running state and cancellation only before entry; no invented percentage and no interrupt promise |
| Cancelling mid-bake could leave classified-as-good output | §5's B1–B5: temps, then a non-cancellable publication that invalidates the old key first and writes the new key last |
| Multi-target partial install, and `Failed.Remove` is unconditional (`Route7.cs:405`) | Separate block / apply / visibility fields; conservative aggregation; no log parsing and no manager-state edits; `RetryHint` unchanged apart from shared access |
| The session block differs between the dashboard and explicit console Apply | The dashboard follows checkbox suppression (`:129`); no force retry, no reset; a successful Bake alone never clears the badge |
| Multiple ContentMods roots and duplicate ids | Canonical-root identity, root shown on collision, SHIP's absolute root preserved; refuse unresolved ambiguity rather than applying whichever name resolves first |
| Verify evidence is narrower than visual mesh correctness | S6 promises only its load-back gates and the held claim; the owner's W7 visual proof stays separate and unclaimed until observed |
| Inputs can change outside the panel | B4 key recheck before publication and before every next stage; R32 stops the chain. No watcher, no receipt database this slice |
| The console package wrapper destroys old output (`ContentToolMain.cs:511`) | `Package.Run` directly into a fresh external directory; cancel retains successfully published outputs; no speculative rollback delete |
| A restart invalidates session observations | Re-derive rows on startup, retain no in-memory PASS as current evidence, require enable + Verify before claiming S2 or live verification |
| General projects may need sound/video/publish routes beyond `ApplyProject` | Mark unsupported lifecycle coverage VOID with a reason; five rows certify nothing about those routes |
| The shared gate extraction and temp publication are implementation work, not shipped guarantees | Exactly ONE new console path — `ct_route7 verify <name>`, so the Verify producer has a second consumer proving its string (§4.4); every other verb keeps its wrapper unchanged. Verify before shipping: cancellation around B5, stamp-invalidation failure, locked files, mixed dispositions, generation changes and byte-identical shared verdicts (G7, W18) |
| Two producers race for one output directory | §5's fail-fast claim in `ProjectBake.Run:69` — one owner per canonical directory, R37 to everyone else, released in a `finally`; proven from the console side by W20 |
| A run outlives its window | §6's pump on `FitBench.Update:2104`; the worker continues, blocking main-thread steps wait for a painted panel, `Cancel` stays reachable through the seam (W19) |

## 10. Task split
Order follows the standing lesson from the wizard slice: **land the seam or carrier before its first caller**, so no task
ships a call into something that does not exist yet. Limits are per-task implementation diff, not a budget to fill; split
green commits inside a task; do not create factories or interfaces to hit a size target. Integrate in order on `main`,
committing each verified logical change, and push only on request.

**v2 reorders this** on the spec check's sequencing paragraph. v1 put callers before the seams they need — the read-back
extraction §4.4 requires had no owner at all while task 4 already dispatched Verify, and task 4 exposed Apply/Package
before task 6 supplied the admission and path guards. v1's task 3 also bundled the worker split, the publication boundary
**and** the extraction of ~270 existing lines (`ProjectBake.cs:1661`–`:1930`) into one ≤300-line slot, which it does not
fit. New order: carrier → shared extraction and publication primitive → ownership/admission/freshness → segmented
producers with their filesystem fault tests → coordinator, seam and closed-window handling → panel → SHIP handoff →
integrated acceptance. **Each check now belongs to the task that owns it**, and task 8 confirms integration rather than
providing the first substantive proof of anything.

| # | Ownership | Finish condition / ordering reason |
|---|---|---|
| 1 | The structured result carrier and the shared exact verdict formatter — the two-count `ProjectBake.Run` result, the §4.4 per-gate `PASS/FAIL/VOID` entries plus the exact terminal line, `Route7` dispositions, Doctor S1/S2 in one formatter; ≤250 lines | G1's carrier arms and G2 pass. Doctor, console and the returned result agree on the existing strings, and an all-VOID carrier is distinguishable from a PASS without reading text. **First: the carrier every later task calls** |
| 2 | Shared read-back extraction out of `Patch` (gates `ProjectBake.cs:1656`–`:1768` + `ByName` `:1827`–`:1952`; the six shared helpers become `internal` in place) behind that carrier, and `AtomicFile.Publish(temp, path, backup)` with `Write` rerouted through it while keeping its own cleanup guard; ≈330 lines, three commits | Bake's output and its printed lines are byte-identical before and after the extraction, proven against a baseline captured BEFORE the move and re-baked through the extracted producer — **not** by the `--bake` harness, which dispatches the sound-bank path (`tests/ObjCodecTests/Program.cs:22`–`:34`) and never reaches `Patch`. The one swap in the codebase is `Publish`. **Both seams exist before any caller reaches for them** |
| 3 | Producer ownership (the in-flight claim in `ProjectBake.Run:69`, R37, the general live-reader refusal R38), §4.6 admission at the shared entry point, and freshness (one filesystem observation taken OUTSIDE the pure reducer and passed in, plus the read-only `Route7.Failed` query and `RetryHint` that admission needs — both pulled forward from task 7), the `BakeResult` disposition (Success/Refused/Cancelled/Failed, so a zero-count R37/R38 refusal cannot read as a successful bake at `Route7.cs:341`), and atomic acquisition of both output directories; ≈340 lines, two commits | G6 passes; the console verb and the checkbox get R37/R38 too. No UI IO, no second dependency model. Needs task 1's outcomes |
| 4 | Segmented producers: the §4 thread split with Unity-derived paths captured on main, ONE narrow phased-import entry under `ContentProject.Load:305` (both importers are private and `Load` itself is main-thread-bound), `SlimJob`-compatible progress, cancellation bookkeeping, the §5 B1–B5 publication boundary extracted into the linkable `Publication.cs`, and the structured multi-target Apply overload; ≈520 lines, four commits | G4 **and G7** pass — the filesystem fault arms ship with the code that can fail them, not after it, and they drive the production publication file. Cancellation cannot release busy early and cannot interrupt publication |
| 5 | `Run all` coordinator, the main-thread arming gate, the sectioned JSON-string seam `Open`/`Run`/`Cancel`/`Snapshot`/`Acceptance` (barrier observation included), the safe external Package destination resolver (settled here, where `Run("Package")` first becomes reachable), the §6 pump on `FitBench.Update:2104` with the closed-window policy, and the test-instance-only `Acceptance` fixtures with the WORKER barrier; ≈380 lines, four commits | G5 passes; RPC and buttons enqueue one path; a run survives a closed window; `Snapshot("")` provably stays under PPCLI's 2000-char clip. Fixture hooks are test-instance-only and bounded. Needs tasks 3 and 4 |
| 6 | Lifecycle drawing and the third FitBench tab; ≤280 lines | Five rows, placeholders, fixed tail and progress, controls drawn in every state. Build plus seam/panel smoke checks. Needs task 5's snapshots |
| 7 | Doctor SHIP handoff, S1 and Failed badges, checkbox-equivalent admission, and the safe external Package output path; ≤250 lines | Build plus admission/handoff smoke checks against a real result and root. Needs the panel and the existing producer guards; no install-copy helper |
| 8 | PPCLI acceptance script, screenshots and log receipts, final memo corrections; ≤250 lines | W8–W20 run with the exact commands and results preserved, PASS distinguished from unverified visual work. **Last, and MOSTLY confirmatory** — three proofs genuinely land here for the first time and must be labelled as such rather than claimed as re-runs: the read-back extraction's byte/log identity (task 2), the thread split and main-thread `Install` loop (task 4), and the seam/pump/fixtures in a real session (task 5) |

## 11. Follow-ups (`ponytail:` ledger)
- `ponytail:` **ZIP release** — Z1 `Package.Run` into a private staging folder; Z2 require `ok` and unchanged package
  inputs; Z3 write a sibling temp ZIP with a top-level mod folder; Z4 close, reopen and verify the entry inventory; Z5
  final token check then atomic `Replace`/`Move` to the destination. Cancel or error removes only owned temps and the
  previous release stands. Deferred because `Package.Run` ships a folder (`src/Project/Package.cs:180`) and the S7/W18
  acceptance is written for that folder; the manual-zip instruction at `:191` must not be printed as if a ZIP existed.
- `ponytail:` the shared read-back helper (§4.4) is extracted from `Patch` for exactly two callers — bake and the Verify
  producer. `ct_route7 verify` is a consumer of the Verify producer, not a third caller of the helper; a genuine third
  caller means it wants its own file. Verify's live observations (`Route7.Run("status")` `:210`, per-target claim
  ownership, residency) stay resampled every run rather than cached.
- `ponytail:` freshness is in-memory only — after a restart Validate and Verify start `never`. A persisted receipt store is
  the next slice's call, not this one's; the key comparison is what makes `stale` honest in the meantime.
- `ponytail:` manifest lexical hardening stays deferred (`2026-09-02-manifest-core-design.md:186`). The wizard's own
  ledger items (locator walk folding, `meta.json` repair) are unchanged by this slice.
