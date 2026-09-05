# Lifecycle dashboard — implementation plan (carrier → extraction → ownership → producers → coordinator → panel → SHIP → acceptance)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or
> superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
> **One implementer at a time** — the tasks share `obj\`, `ObjCodecTests.csproj` and `Program.cs`, and the wizard slice
> proved that two concurrent implementers corrupt all three (handoff `2026-09-03-…md:25`).

**Goal:** Ship `internal-docs\planning\2026-09-05-lifecycle-dashboard-design.md`: a third FitBench tab that shows, for
ONE selected ContentMods project, five rows — **Validate → Bake → Apply → Verify → Package** — each with its own
freshness, its producer's own verdict string and a button, plus `Run all`, a progress track, a cancel that never lies,
and a log tail. No console command typed at any point, and **no verdict invented by the UI**: every line on the panel is
a string a producer returned.

**Architecture:** Four seams, then the drawing. (1) A structured result **carrier** plus one shared verdict formatter,
so an all-VOID read-back is distinguishable from a PASS without reading text. (2) The ~270-line read-back block is
**extracted** out of `Patch` behind that carrier and gains a second consumer (`ct_route7 verify <name>`), and
`AtomicFile` gains `Publish(temp, path, backup)` so there is exactly ONE file swap in the codebase. (3) **Producer**
ownership: a fail-fast in-flight claim keyed by the canonical output directory inside `ProjectBake.Run:69` — the one
body behind the console verb, the checkbox and `ApplyProject` — plus admission and freshness. (4) Segmented producers
(main → worker → main) with the B1–B5 publication boundary, then the `Run all` coordinator, the static seam
`Open`/`Run`/`Cancel`/`Snapshot` and the pump on `FitBench.Update:2104`. The panel draws snapshots and owns nothing.

**Tech Stack:** C# 9 / net472, Mono inside Unity 2019. No new dependencies. Build `dotnet build -c Release`
(`ContentTool.csproj` globs `src\**\*.cs`, so a new `src\` file needs no csproj edit). Offline gates are
`tests\ObjCodecTests` and `tests\TargetPathTests` (NOT `dotnet test`), each a `static class X { internal static string
Run() }` that throws on failure and is called from `Program.Main`. `ObjCodecTests.csproj` sets
`EnableDefaultCompileItems=false`, so **every new file — test or linked src — must be added to its `<Compile Include>`
list** (the pattern is `..\..\src\IO\AtomicFile.cs` at `:37`, `ProjectScaffoldTests.cs` at `:41`).

**Baseline at HEAD `5fc3404` (re-measure before Task 1, do not trust these numbers):** `PROJECT-SCAFFOLD 89`,
`MANIFEST 53`, `ALIAS 32`, `REFUSAL-COUNT 17`, `PACKAGE-GATE 7`, `MESH extract 57+`, TargetPathTests `R0: ALL PASS`,
build `Ошибок: 0` / `Предупреждений: 1` (the known `GlbCodec.cs(59,23) CS0649`). **Every check count in this plan is a
PREDICTION.** The implementer reports the real one in its commit and this file is corrected to match — a mismatch is
not a failure, an unexplained mismatch is.

**Nine facts this plan is built on, each read at HEAD `5fc3404` rather than assumed:**

1. **`ProjectBake.Run(string projectRoot, out int failed, out int patchFailed)` is `src\Bake\ProjectBake.cs:69`**, the
   one-arg wrapper is `:51`, and it is the single body behind all three entry points: `ct_project`
   (`src\ContentToolMain.cs:480`), the mod-manager checkbox and `ct_route7 apply` (`src\Bake\Route7.cs:341`). That is
   why §5's ownership claim goes THERE and not in the panel.
2. **`AtomicFile` lives at `src\IO\AtomicFile.cs`, not `src\Project\`** (the csproj links it from `..\..\src\IO\`,
   `ObjCodecTests.csproj:37`). `Write` `:17` makes its own temp `:19`, flushes `:29`, `File.Replace` `:31` when the
   destination exists, `File.Move` `:32` when it does not, and deletes an orphaned temp in `finally` `:34`–`:39`. It
   **cannot publish a temp the caller already streamed** — that is the whole reason for `Publish`.
3. **The read-back block is inside `Patch` (`ProjectBake.cs:1506`)**, running `:1661` (`want.Count == 0` → `P1 VOID`)
   through `:1930`. Confirmed anchors: `P1` `:1665`, `P1-ctl-shipped` `:1667`, `P4-bytes VOID` `:1722`, `P4-bytes`
   counted `:1727`, `P5 VOID` `:1744`, `P6 VOID` arms `:1832` / `:1841` / `:1850` / `:1870` / `:1898` / `:1917`.
   `baker.Write(copy, null)` at `:1644` happens BEFORE the gates and even after a row refusal; `PARTIAL` is `:1648`.
4. **Every VOID arm returns `0` exactly like a pass** — that is finding 2's whole point and Task 1's reason to exist.
5. **`FitBench.Update()` is `:2104`**, stopped only by `inputBroken` `:2106`; the Doctor drain is behind `if (open)`
   `:2131` and `OnGUI` returns early when closed `:2296`. **There is an earlier `return` at `:2118`** (`open &&
   !StillThere()` — the level went away), so the pump must be called between `:2106` and `:2112`, before BOTH gates.
   The design says "before the `open` gate"; the `StillThere` return is the sharper constraint.
6. **The bench has no tab enum — it has a `bool doctorTab`** (`FitBench.cs:1674`, guarded by
   `GUI.enabled = !doctor.ShipPending` at `:1672`). A THIRD tab means replacing that bool, which is a Task 6 edit and
   nothing earlier.
7. **`ModelDoctor.Tail` is `private static` (`:745`) and `ModelDoctor.cs` is NOT linked into `ObjCodecTests`** — the
   wizard slice hit this exact wall (handoff `:21`). G3 therefore cannot test `Tail` where it lives: Task 1 MOVES it
   into the new UnityEngine-free carrier file and `ModelDoctor` calls it there. Moving it is smaller than duplicating
   it, and a second copy of a tail helper is how the two sides drift.
8. **Route7's anchors all hold:** `Failed` `:94` private, `RetryHint` `:158` private, checkbox refusal text `:130`,
   LegacyDisk guard `:126`, `ApplyDisposition` `:252`, `ApplyProject` `:261`/`:269`, `declared` `:287`, the copy census
   loop `:310`, `NOT APPLIED` `:349`, key write `:353`, `Failed.Remove(modId)` `:405`, `CatalogVerify` `:510`, and the
   `dryrun|verify|revert|stacktest` refusal arm starting `:56`.
9. **`Package` is pure `System.IO`** (`src\Project\Package.cs:15`): `Run` `:61`, nonempty refusal `:78`, `PACKAGED`
   `:180`, `BuiltAssembly` `:211`. The console wrapper's `Directory.Delete(outDir, true)` is
   `ContentToolMain.cs:511` — the dashboard never goes near it.

**Three design claims corrected against disk** (the design is right everywhere else that this plan cites):

| Design says | Disk at `5fc3404` |
|---|---|
| `ContentToolMain.LiveProjectIds` `:82` (§3, §6) | the method is `src\ContentToolMain.cs:84`; `:82` is inside its doc comment. Harmless — it is a "do NOT use" reference |
| `Patch` "rewrites the copy even after a row refusal, then gates `:1644`" (§3) | `:1644` **is** `baker.Write(copy, null)`, the rewrite itself; the `PARTIAL` accounting is `:1648`. The behaviour described is real, the anchor names the write |
| the P6 VOID arms are `:1832` and `:1917` (§4.4) | both hold, and there are four more: `:1841`, `:1850`, `:1870`, `:1898`. Task 2 must carry **every** VOID arm into the carrier, not the two the design names |

## Codex plan review — PLACEHOLDER

> Run `cx -Review E:\DEV\PhoenixPoint\ContentTool -PromptFile <plan review prompt> -TimeoutSec 900` on THIS file before
> Task 1 starts, then replace this section with: the `C:\Temp\cx\<id>.out.md` path, the finding count, **accepted** (with
> the lighter forms named), **rejected** (each with its reason), and any execution-order change. Task NUMBERS never
> change — other documents cite them; an order change is stated as a sentence, the way the wizard plan did it
> (`2026-09-02-replace-mesh-wizard-plan.md:78`–`:82`).

## File Structure

**Created**

| Path | Responsibility |
|---|---|
| `src\Bake\StageResult.cs` | The carrier: `Outcome { Pass, Fail, Void }`, `GateEntry` (gate id, target key, outcome, the producer's exact line), `ReadBackResult` (`Failed`/`Passed`/`Void` counts + entries + the terminal line), `BakeResult` (`failed`, `patchFailed`, terminal line), `StageResult` (stage, outcome, freshness, verdict text, counts, generation). **UnityEngine-free, test-linked.** Also the new home of `Tail` (fact 7) |
| `src\Bake\StageText.cs` | The ONE verdict formatter: S1–S7, R25–R38, the bake special cases, the `Validate: FAIL - {reason}` / `Verify: FAIL - {reason}` fallbacks, the transient `Message` strings. Pure string composition, no IO. **UnityEngine-free, test-linked** |
| `src\Bake\ReadBack.cs` | The read-back gates extracted from `Patch` (`ProjectBake.cs:1661`–`:1930`) behind `ReadBackResult`, called by BOTH bake and the Verify producer. Unity + AssetsTools → build gate + W18 |
| `src\Bake\OutputClaim.cs` | The process-wide in-flight claim: one `HashSet<string>` of canonical output directories under one lock, `OrdinalIgnoreCase`; `Take`/`Release`/`Held`. UnityEngine-free, test-linked |
| `src\Bake\LifecycleState.cs` | The pure reducer: receipts, freshness (`never`/`stale`/`fresh`), `Admit(stage)` (§4.6 table), the `Run all` sequencer, cancel bookkeeping. **No filesystem, no Unity.** Test-linked — this is what G1/G5/G6 exercise |
| `src\Bake\LifecycleJob.cs` | Segmented producers: the main→worker→main split, captured Unity-derived paths, `SlimJob`-shaped ThreadPool dispatch, progress, cancellation, the B1–B5 publication boundary. Unity → build gate + G7 (which tests the publication primitive, not this file) |
| `src\Dev\LifecycleDashboard.cs` | The panel + the public static seam `Open(string)` / `Run(string)` / `Cancel()` / `Snapshot()` + test-instance-only `Acceptance(string)`. Namespace `Morgott.ContentTool.Dev` (`ModelDoctor.cs:13`, `FitBench.cs:28`) |
| `tests\ObjCodecTests\LifecycleTests.cs` | G1–G7. Prints `LIFECYCLE PASS, N check(s) - …` |

**Modified**

| Path | Change |
|---|---|
| `src\IO\AtomicFile.cs` `:17`–`:39` | add `Publish(tempPath, path, backupPath)` holding the existing swap (`:31`/`:32`) and its `finally`; `Write` becomes "write bytes to my temp, call `Publish`". ONE swap in the codebase |
| `src\Bake\ProjectBake.cs` `:69`, `:142`, `:1644`, `:1661`–`:1930` | the claim (R37) + live-reader refusal (R38) at the body's entry; the read-back block moves to `ReadBack.cs`; `Patch` writes to a temp and publishes (B2/B5); the Dist pre-delete at `:142` goes away with the temp treatment; `Run` also returns a `BakeResult` beside the two `out` counts |
| `src\Bake\Route7.cs` `:56`, `:287`–`:311`, `:341`, `:353` | `verify` leaves the removal arm and routes to the Verify producer; the key write at `:353` moves INTO the shared bake completion; the declared-copy census becomes the shared freshness helper |
| `src\Dev\ModelDoctor.cs` `:71`, `:710`–`:714`, `:745` | S1/S2 come from `StageText`; `Tail` moves to `StageResult.cs` and is called from there; the SHIP handoff hands `made.Root` + its Apply result to the dashboard |
| `src\Dev\FitBench.cs` `:1672`–`:1674`, `:2104`–`:2112` | `bool doctorTab` → a three-state tab; the lifecycle pump call between `:2106` and the `StillThere` return |
| `tests\ObjCodecTests\ObjCodecTests.csproj` | link `StageResult.cs`, `StageText.cs`, `OutputClaim.cs`, `LifecycleState.cs`, `..\..\src\IO\AtomicFile.cs` (already at `:37`); compile `LifecycleTests.cs` |
| `tests\ObjCodecTests\Program.cs` `:142` | `Console.WriteLine(LifecycleTests.Run());` after `ProjectScaffoldTests.Run()` |
| `internal-docs\planning\2026-09-05-lifecycle-dashboard-plan.md` | Task 8 fills in the in-game evidence table, in this file |

**NOT modified:** `Manifest`, `ManifestFile`, `AliasMap`, `BundleBaker`, `BundleClaims`, `BundleLive`, `Package`,
`PatchCache`, `ContentMods`, `ModGate`, `ModRoster`, `ProjectScaffold`, `ShippedTarget`. The dashboard is a CALLER of
every one of them. In particular `Package.Run` keeps its own allowlist and refusals as the sole authority, and
`BundleLive.Uninstall` is never called by anything in this slice.

---

### Task 1: the carrier and the one verdict formatter

**First, because every later task calls it** (§10). ≤250 lines. Files: `src\Bake\StageResult.cs`,
`src\Bake\StageText.cs`, `tests\ObjCodecTests\LifecycleTests.cs`, `ObjCodecTests.csproj`, `Program.cs`,
`src\Dev\ModelDoctor.cs` (Tail moves out, S1/S2 read from `StageText`).

- [ ] **Step 1: Write the failing gate.** Create `tests\ObjCodecTests\LifecycleTests.cs` — G1's carrier arms and all of
  G2. Shape, disk wins:
  ```csharp
  internal static class LifecycleTests
  {
      internal static string Run()
      {
          int checks = 0;
          // ---- G1 carrier: an all-VOID read-back is NOT a PASS, and nobody reads text to find out.
          ReadBackResult allVoid = ReadBackResult.Of(
              GateEntry.Void("P4", "mesh_a", "P4-bytes VOID mesh 'mesh_a' has no readable vertex/index buffers"),
              GateEntry.Void("P4-bytes", "mesh_a", "..."));
          checks += Check(allVoid.Failed == 0 && allVoid.MandatoryVoid("mesh_a", RowKind.Mesh),
                          "zero failures with a mandatory VOID is VOID, never S6");
          // ---- G2 wording: the exact strings, from ONE producer.
          checks += Check(StageText.S1("Replace_Rifle", "px_equipment_assets_all.bundle", false) ==
                          "applied - restart the game and enable 'Replace_Rifle' in the mod manager. " +
                          "Phoenix Point already loaded px_equipment_assets_all.bundle.",
                          "S1 is ModelDoctor.cs:710-712 verbatim");
          ...
      }
  }
  ```
  Every verbatim string is COPIED FROM DISK, not from this plan — the file:line to copy from is in the table below.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected RED: the build fails on the missing `StageText`/`ReadBackResult` types (this is the arm proving the test
    is wired into `Program.cs` at all).

- [ ] **Step 2: The carrier.** `src\Bake\StageResult.cs` — no interface, no builder, one type per fact (design §4.4:
  "one type with one producer"). `Outcome { Pass, Fail, Void }`; `GateEntry { string Gate, Target, Line; Outcome
  Outcome; }`; `ReadBackResult { int Failed, Passed, Void; IList<GateEntry> Entries; string Terminal; }` plus the §4.4
  **mandatory-proof** predicate (mesh row → `P4` AND `P4-bytes` non-VOID; texture row → `P1` AND `P1-ctl-shipped`;
  material row → `P3`). `BakeResult { int Failed, PatchFailed; string Terminal; }`. `StageResult { Stage, Outcome,
  Freshness, Verdict, Generation }`. Move `Tail` here from `ModelDoctor.cs:745` unchanged (fact 7) and make
  `ModelDoctor` call it — **freeze its current semantics, do not "fix" them** (G3).

- [ ] **Step 3: The formatter.** `src\Bake\StageText.cs`. Each string is copied from the file:line below; nothing is
  reconstructed from this plan's quotes.

  | ID | Copy from |
  |---|---|
  | S1 | `src\Dev\ModelDoctor.cs:710`–`:712` (+ ` This session keeps showing your Doctor preview.` iff `HasPreview`) |
  | S2 | `src\Dev\ModelDoctor.cs:714` |
  | S4 / S5 | `src\Bake\ProjectBake.cs:405` / `:406` |
  | bake special cases | `src\Bake\ProjectBake.cs:126`–`:135` (three strings, verbatim, never parsed to classify) |
  | S7 | `src\Project\Package.cs:180` |
  | Package refusal | `src\Project\Package.cs:78`–`:80` |
  | R29 | `src\Bake\Route7.cs:130`–`:132`, with `RetryHint` `:158` |
  | R35 | `src\Bake\Route7.cs:349`–`:351` |
  | S3, S6, R25–R28, R30–R34, R36–R38 | NEW — the design's §7 table, `2026-09-05-lifecycle-dashboard-design.md:355`–`:375` |

  R29's and R35's text are **forwarded from the producer** at runtime; the formatter's copy exists only so G2 can prove
  the two are the same string.

- [ ] **Step 4: GREEN.** Link the two new src files + `LifecycleTests.cs` in `ObjCodecTests.csproj`; register
  `Console.WriteLine(LifecycleTests.Run());` in `Program.cs` after `:142`.
  - Run: `dotnet build -c Release` → `Ошибок: 0`, `Предупреждений: 1`
  - Run: `dotnet run --project tests\ObjCodecTests -c Release` → exit 0, `PROJECT-SCAFFOLD PASS, 89` unchanged,
    **new `LIFECYCLE PASS, ~26 check(s)`** (prediction: 8 carrier arms + 18 wording arms)
  - Run: `dotnet run --project tests\TargetPathTests -c Release` → `R0: ALL PASS`

- [ ] **Step 5: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add src\Bake\StageResult.cs src\Bake\StageText.cs src\Dev\ModelDoctor.cs tests\ObjCodecTests\LifecycleTests.cs tests\ObjCodecTests\ObjCodecTests.csproj tests\ObjCodecTests\Program.cs && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(bake): a structured stage carrier and one verdict formatter, so an all-VOID read-back is not a PASS"`

- [ ] **Review gate:** `cx -Review E:\DEV\PhoenixPoint\ContentTool -Commit <sha> -TimeoutSec 600` (background) +
  `caveman:cavecrew-reviewer` in parallel → triage (ponytail: reject pedantry, fix real defects with a fix agent,
  re-review) before Task 2 starts.

---

### Task 2: the read-back extraction and `AtomicFile.Publish`

**Both seams before any caller reaches for them** (§10). ≤300 lines — and note that ~270 of them already exist
(`ProjectBake.cs:1661`–`:1930`), so this is a MOVE, not a rewrite. Files: `src\Bake\ReadBack.cs` (new),
`src\Bake\ProjectBake.cs`, `src\IO\AtomicFile.cs`, `tests\ObjCodecTests\LifecycleTests.cs`, `ObjCodecTests.csproj`.
Lands as **two green commits**: (a) `Publish`, (b) the extraction.

- [ ] **Step 1: RED for `Publish`.** Add to `LifecycleTests.Run()` — real files in a temp directory, `System.IO` only:
  ```csharp
  // ---- Publish: the ONE swap. A temp the caller streamed, published over an existing file and over
  // an absent one, and an orphaned temp deleted on the failing path.
  checks += Check(AtomicFile.Publish(tmp, dest, bak) is done && File.ReadAllBytes(dest) == streamed, ...);
  ```
  - Expected RED: no `Publish` method.

- [ ] **Step 2: `Publish`.** In `src\IO\AtomicFile.cs`, extract the swap `Write` already performs (`:30`–`:32`) and its
  `finally` (`:34`–`:39`). Shape, disk wins:
  ```csharp
          /// <summary>Publish a temp the CALLER streamed. AtomicFile.Write makes its own temp (:19) and so
          /// cannot be handed one - a bake that streams a bundle straight into a sibling temp needs the swap
          /// without the buffer. One swap in the codebase, so a publication cannot drift from a write.</summary>
          internal static void Publish(string tempPath, string path, string backupPath = null)
          {
              try
              {
                  if (File.Exists(path)) File.Replace(tempPath, path, backupPath);
                  else File.Move(tempPath, path);
              }
              finally { /* the existing orphan-temp delete, unchanged */ }
          }
  ```
  `Write` then becomes: write the bytes to its own temp (keeping the flush at `:29`), call `Publish`. `WriteText`
  `:45` is untouched. Where a file must be created and never overwritten, callers keep using the absent-only
  `File.Move` arm directly, as the wizard slice requires.

- [ ] **Step 3: GREEN + commit (a).**
  - Run: `dotnet run --project tests\ObjCodecTests -c Release` → `LIFECYCLE PASS, ~32` (+6 publish arms),
    `PROJECT-SCAFFOLD 89`, `MANIFEST 53`, `ALIAS 32` unchanged (they call `Write`, whose behaviour must not move)
  - `git … add src\IO\AtomicFile.cs tests\ObjCodecTests\LifecycleTests.cs && git … commit -m "refactor(io): one file swap - AtomicFile.Publish takes a temp the caller streamed, Write routes through it"`

- [ ] **Step 4: The extraction.** Move `ProjectBake.cs:1661`–`:1930` into `src\Bake\ReadBack.cs` as one entry that takes
  the captured expectations, shipped paths and existing copy paths, **never rewrites and never installs**, and returns
  `ReadBackResult`. Each `log.AppendLine(...)` becomes `GateEntry` + the SAME line appended to the same log — the line
  text does not change. Carry **all six** P6 VOID arms (`:1832`, `:1841`, `:1850`, `:1870`, `:1898`, `:1917`), the
  `P4-bytes` VOID `:1722` and its counted comparison `:1727`, `P5` `:1744`, `P1` `:1665`, `P1-ctl-shipped` `:1667`, P3
  and P4, plus the applicable clip gates. Bake keeps its existing counting behaviour — **a VOID stays uncounted there,
  exactly as today**.
  - `Patch` calls it and adds `result.Failed` to its own `failures` exactly where the inline block did.
  - Unity + AssetsTools → **compiler + Task 8 row W18** for the extraction itself; the byte-identity gate below is what
    proves it did not change behaviour.

- [ ] **Step 5: Prove nothing moved.** Bake the same project before and after with the existing harness:
  - Run: `dotnet run --project tests\ObjCodecTests -c Release -- --bake <wav> <mediaId> - <out.bnk>`
    (`Program.cs:62`) and the full suite; expected: unchanged markers, exit 0
  - **In-game byte-identity is W18** — the same project, same key, the printed lines and the final bundle bytes
    identical before and after. Record the pre-extraction bake log in the session scratchpad before Step 4 or this arm
    has nothing to compare against.

- [ ] **Step 6: Commit (b).**
  - `git … add src\Bake\ReadBack.cs src\Bake\ProjectBake.cs && git … commit -m "refactor(bake): the load-back gates move out of Patch behind the stage carrier, one producer for bake and verify"`

- [ ] **Review gate:** as Task 1.

---

### Task 3: producer ownership, admission and freshness

Needs Task 1's outcomes. ≤280 lines. Files: `src\Bake\OutputClaim.cs` (new), `src\Bake\LifecycleState.cs` (new — the
`Admit` half and freshness), `src\Bake\ProjectBake.cs` `:69`, `src\Bake\Route7.cs` `:287`–`:311`/`:353`,
`tests\ObjCodecTests\LifecycleTests.cs`, `ObjCodecTests.csproj`.

- [ ] **Step 1: RED — G6, the §4.6 table row by row.** Add to `LifecycleTests`: standalone / `Run all` / post-restart /
  unsupported-route / activation for each of the five stages, plus missing, deleted and duplicate project names, key
  changed before commit, `Failed` suppression, nonempty package output, a write routed outside the allowed roots.
  **The two arms that catch the design's own trap:** Apply is **NOT** refused for a stale bake (the `ApplyProject`
  fallback owns it, `Route7.cs:312`–`:341`), and Verify **IS** refused for absent copies. Plus the claim arms: a second
  `Take` on the same canonical directory refuses immediately; `Release` happens on success, refusal and exception.
  - Expected RED: no `OutputClaim`, no `Admit`.

- [ ] **Step 2: The claim.** `src\Bake\OutputClaim.cs` — one `HashSet<string>` under one lock, `OrdinalIgnoreCase`
  (the case-blindness `Route7.cs:287` already uses), full-path-canonicalized. Shape, disk wins:
  ```csharp
          /// <summary>One owner per canonical output directory. FAIL FAST: a second producer for a directory
          /// already in flight is refused (R37) and writes nothing - it never waits, never retries, never
          /// steals. The panel's own ownership stops the panel's buttons; this stops the console verb
          /// (ContentToolMain.cs:480) and the mod-manager checkbox (Route7.cs:341), which never ask the panel.</summary>
          internal static bool Take(string dir, out string refusal)
  ```
  No per-directory lock objects, no queue.

- [ ] **Step 3: Claim it in the producer.** In `ProjectBake.Run` `:69` — the body behind the wrapper `:51`, the console
  verb and `ApplyProject` — take the claim on the patched dir and the project's own Dist at entry, release in a
  `finally` on **every** path including cancellation and exception, and return R37 (`StageText`) when it is refused.
  When the caller is `ApplyProject`, the claim is **passed down**, not re-taken, so Apply's own bake does not deadlock
  against itself. A forced same-key bake is not exempt.

- [ ] **Step 4: The live-reader refusal (R38), general.** Before replacing any claimed file, ask `BundleClaims.Held`
  **per target** — never `BundleLive.Holds` (`:145` → `BundleClaims.Holds` `:296`, which returns true on the FIRST
  claim of that mod and so passes with two copies and one claimed target). Applies to a stale, fresh, repair or forced
  same-key bake alike. The answer is a restart boundary, never rewriting beneath live readers.

- [ ] **Step 5: Freshness, once.** In `LifecycleState.cs`: `PatchCache.Key(root, shipped)` (`:43`/`:49`) + `Fresh`
  (`:84`) + **the declared-copy census exactly as `Route7.cs:310`–`:311` does it** — `Fresh` compares key text only, so
  the census is the other half. `never` = no receipt; `stale` = a receipt exists but inputs differ or a required output
  vanished (an old cache directory with no key is **stale, not never**); `fresh` = receipt matches and outputs exist.
  Recompute on explicit refresh, at stage start and after completion — **never in `OnGUI`**. Move the successful
  patch-key publication out of `Route7.cs:353` into the shared bake completion.

- [ ] **Step 6: GREEN.**
  - Run: `dotnet build -c Release` → `Ошибок: 0`, `Предупреждений: 1`
  - Run: `dotnet run --project tests\ObjCodecTests -c Release` → `LIFECYCLE PASS, ~54` (+22: 12 admission rows, 6 claim
    arms, 4 freshness arms), everything else unchanged
- [ ] **Step 7: Commit.**
  - `git … add src\Bake\OutputClaim.cs src\Bake\LifecycleState.cs src\Bake\ProjectBake.cs src\Bake\Route7.cs tests\ObjCodecTests\LifecycleTests.cs tests\ObjCodecTests\ObjCodecTests.csproj && git … commit -m "feat(bake): one owner per output directory, a general live-reader refusal, and stage admission that never re-implements a dependency graph"`
- [ ] **Review gate:** as Task 1.

---

### Task 4: segmented producers, progress, cancellation and the B1–B5 publication boundary

Needs Tasks 2 and 3. ≤300 lines. **G4 and G7 ship WITH this code, not after it.** Files:
`src\Bake\LifecycleJob.cs` (new), `src\Bake\ProjectBake.cs` (B1–B5 inside `Patch`/`Run`),
`tests\ObjCodecTests\LifecycleTests.cs`.

- [ ] **Step 1: RED — G4 (cancel) on the reducer, G7 (publication faults) on real files.** G4 arms: cancel before
  dispatch; at a cooperative boundary; after successful publication; repeated Cancel. Assert one terminal result, busy
  retained until worker completion, no next-stage dispatch, no false rollback, **no late result overwriting a newer
  run**. G7 is the deliberate filesystem exception (§8.1): a real temp directory, real bytes, plain `System.IO`,
  deleted in a `finally`. Arms: key invalidation fails → nothing published, previous outputs intact; a failure between
  two copy replacements → completed files complete, key absent, row FAIL, Apply refused until a repair bake; the key
  write itself fails → same; cancel at B4 → temps deleted, previous outputs **byte-identical**; cancel inside B5 →
  publication completes and the run reports completion; a competing R37 while a claim is held → refused immediately,
  the holder's bytes untouched; the claim released on success, refusal and exception. **Assert actual bytes** in every
  arm.

- [ ] **Step 2: The thread split.** Main → worker → main, per stage (§4). Capture on MAIN and hand in as strings:
  `BakeSelfCheck.ShippedBundlePath` (`Application.streamingAssetsPath`, `:739`), `ContentToolMain.PatchedRoot`
  (`persistentDataPath`, `:65`), `InstallTag` (`dataPath`, `:74`). A worker that calls one of those is a bug. Worker
  work: `ContentProject.ImportModel` (`:691`/`:695`), `ImportAudio` (`:761`–`:772`), the whole of `Package`
  (`Package.cs:15`). Main work: Unity sampling, embedded texture decoding (`ProjectBake.cs:1347`), bundle loads
  (`:341`/`:351`), rig instantiation (`:2085`/`:2106`), the Unity Verify gates. Gates whose Unity dependence is
  uncertain stay on MAIN.

- [ ] **Step 3: Progress and cancellation.** Copy `SlimPanel`'s three volatile fields + CTS (`SlimPanel.cs:74`–`:77`)
  and `SlimJob`'s ThreadPool/checkpoint pattern (`SlimJob.cs:407`, `:428`). The worker callback replaces a volatile
  immutable progress reference; completion publishes the result **before** clearing `running`; main `Tick` alone
  touches UI and log output. Counts only with a known denominator; serialization/Unity/compression show the phase and
  the minimum fill, **never an invented percentage**. Check cancellation before and after those calls and between file
  chunks.

- [ ] **Step 4: B1–B5.** In the bake body:
  - **B1** capture the key and every path.
  - **B2** stream each bundle into a unique sibling temp (no extra byte-array copy); **remove the Dist pre-delete at
    `ProjectBake.cs:142`** — Dist gets the same temp treatment.
  - **B3** close the writers, run the applicable patch gates against the patch temps (`ReadBack` from Task 2), validate
    the own-Dist output separately; both failure counts retained.
  - **B4** recompute the key, check cancellation. **The last cancellable instant.**
  - **B5** non-cancellable: refuse if any file about to be replaced is claimed or resident (R38, Task 3), invalidate the
    old key, `AtomicFile.Publish` each complete copy, write the new key **LAST** through `WriteText` `:45`.
  If publication fails midway: files individually complete, key absent → FAIL, Apply forbidden until a repair bake. If
  key invalidation fails, publish nothing. **A publication ordering, not a transaction and not a crash rollback.**
  Apply follows the same shape: A1 revalidate, A2 final cancel check, A3 the main-thread `Install` loop with no
  cancellation and no yields, A4 publish the dispositions then release the claim. **No automatic `Uninstall`**
  (`BundleLive.cs:138`).

- [ ] **Step 5: GREEN.**
  - Run: `dotnet build -c Release` → `Ошибок: 0`, `Предупреждений: 1`
  - Run: `dotnet run --project tests\ObjCodecTests -c Release` → `LIFECYCLE PASS, ~78` (+24: 8 cancel arms, 16 G7
    filesystem arms), everything else unchanged. G7 leaves no temp directory behind — assert that too.
  - Unity-only halves (the thread split, the Install loop): **compiler + Task 8 rows W10, W12, W13**.
- [ ] **Step 6: Commit** (several green commits allowed: the split, then B1–B5).
  - `git … add src\Bake\LifecycleJob.cs src\Bake\ProjectBake.cs tests\ObjCodecTests\LifecycleTests.cs && git … commit -m "feat(bake): temps then a non-cancellable publication - a cancelled bake can no longer leave output nobody can classify"`
- [ ] **Review gate:** as Task 1.

---

### Task 5: the `Run all` coordinator, the seam, the pump and the acceptance fixtures

Needs Tasks 3 and 4. ≤300 lines, **several green commits**. Files: `src\Bake\LifecycleState.cs` (the sequencer),
`src\Dev\LifecycleDashboard.cs` (new — seam + `Acceptance` only, no drawing yet), `src\Dev\FitBench.cs` `:2104`,
`tests\ObjCodecTests\LifecycleTests.cs`.

- [ ] **Step 1: RED — G5 sequence.** All succeeds; each stage fails in turn; the S1 barrier; a prerequisite refusal;
  cancellation. Assert invocation order and count, first stop position, earlier receipts unchanged, and that
  **Package is not entered after `Run all` stops at Verify**. `Run all` calls `Admit` per stage **as it reaches that
  stage**, never up front. A gate VOID alone does not fail a row; an absent mandatory proof stays VOID and blocks
  completion. `Failed` is never cleared on Validate or Bake success — `Route7.cs:405` stays the only clearing path.

- [ ] **Step 2: The seam.** `public static` on `LifecycleDashboard`: `Open(string projectName)`, `Run(string stage)`,
  `Cancel()`, `Snapshot()`. `Open("")` clears the selection explicitly and is **never** passed to the existing name
  resolver, whose empty-name default is Sample (`ContentMods.cs:153`–`:154`); a unique name resolves to a canonical
  root before `LoadDeclared` (`ContentProject.cs:289`), which takes a ROOT holding `ppcontent.json`, not a name; an
  ambiguous name is rejected. `Run` enqueues the same intent as the button, returns promptly, and **never** performs a
  synchronous Apply from the RPC call. Accepted tokens are exactly `Validate`, `Bake`, `Apply`, `Verify`, `Package`,
  `All` — anything else is R33. `Snapshot` returns the canonical game root, the selected canonical root/id, run id,
  busy/current stage, cancel requested/acknowledged, five freshness/outcome/verdict entries, S1/S2, actual `Failed`
  membership, stage-start counts, the held producer claim, and **`barrierArmed` + `barrierRunId`** — without that pair
  W13 cannot tell "parked at the barrier" from "not started" and degenerates into a sleep. `Snapshot` is
  observational and cannot validate, apply or clear anything.

- [ ] **Step 3: The pump.** In `src\Dev\FitBench.cs`, call the lifecycle pump inside `Update()` **after the
  `inputBroken` guard `:2106` and BEFORE the `open && !StillThere()` return at `:2112`** — not merely before the
  `if (open)` drain at `:2131` (fact 5). Its own `try`, like the Doctor drain's (`:2133`–`:2135`), so a lifecycle bug
  cannot set `inputBroken` and take the bench's mouse down with it. Closed-window policy: the worker keeps running and
  publishes its result; completion, log and receipts are recorded; `Cancel` stays reachable through the seam; a
  **blocking main-thread segment** (Apply's `Install`, the Unity Verify gates) waits until the panel is open and has
  painted its warning, exactly as SHIP does (`ModelDoctor.cs:443`, `:1572`). A run that ends while closed shows its
  terminal result when the tab is next opened; **nothing is re-run to produce it**.

- [ ] **Step 4: `Acceptance(string scenario)`.** Test-instance-only — refuses any game root but the test instance,
  compared against the seam's in-process canonical game root. Scenarios prepare isolated named fixtures, arm narrow
  gates and call the **public seam and the real producers**: never installing a fabricated PASS/FAIL snapshot, never
  setting `Failed`, residency, `Holds` or verdict fields directly. `prepare` creates only `DashboardValid` and
  `DashboardPatchFail`; `resident` prepares `DashboardResident` and actually loads its target bundle; `change-source`
  changes only the selected fixture; `ship` drives the real Doctor fixture through its existing selection and
  `Enqueue` path; `enable-resident` invokes the actual mod-manager enable callback after a restart. `arm-cancel-bake`
  **arms** the barrier for the NEXT run and returns immediately, publishing its armed state and run id through
  `Snapshot`; it releases on the same `Cancel()` the UI calls and lets normal worker completion publish VOID — no
  sleep-based race, no `Thread.Abort`, no synthetic success, no detached worker.

- [ ] **Step 5: GREEN.**
  - Run: `dotnet build -c Release` → `Ошибок: 0`, `Предупреждений: 1`
  - Run: `dotnet run --project tests\ObjCodecTests -c Release` → `LIFECYCLE PASS, ~92` (+14 sequence arms)
  - Seam, pump and fixtures are Unity-only: **compiler + Task 8 rows W13, W19, W20**.
- [ ] **Step 6: Commits.** (a) sequencer + G5; (b) seam + pump; (c) `Acceptance`.
  - final: `git … commit -m "feat(dev): one dispatch path for buttons and RPC, and a run that survives a closed bench"`
- [ ] **Review gate:** as Task 1.

---

### Task 6: the drawing and the third FitBench tab

Needs Task 5's snapshots. ≤280 lines. Files: `src\Dev\LifecycleDashboard.cs` (the `Draw` half), `src\Dev\FitBench.cs`
`:1672`–`:1674`. **Unity-only → the gate is the compiler plus Task 8 rows W8, W9, W10, W11, W12.**

- [ ] **Step 1: The third tab.** `bool doctorTab` (`FitBench.cs:1674`) becomes a three-state selector; keep the
  `GUI.enabled = !doctor.ShipPending` guard at `:1672` and extend it so a tab change is also refused while the
  lifecycle job owns the run.
- [ ] **Step 2: Constant layout** (design §6's sketch): selector, global status, five rows, progress, `Run all`/
  `Cancel`, log tail — **always drawn**, placeholders and disabled controls before any result exists. Result text
  changes; the control sequence does not. Idle row placeholder `—`, global placeholder `Ready.`.
- [ ] **Step 3: The selector.** Enumerate sibling Mods directories, ContentTool's own child directories and roster
  roots; keep the roots holding `ppcontent.json` (`ContentMods.cs:116`–`:154`, marker `:25`), canonicalize, dedupe. Do
  **not** use `ContentMods.Enabled` (`:41`) or `ContentToolMain.LiveProjectIds` (**`:84`**, not `:82`) — they omit
  author projects. Disabled projects are INCLUDED. `LoadDeclared` (`ContentProject.cs:289`), never source-importing
  `Load` (`:305`), and the enumeration happens **outside `OnGUI` drawing**. Selection binds to the canonical root,
  shows the root on a duplicate name, passes the absolute root to Apply, and survives a refresh; a deleted target never
  silently switches. No arbitrary path text box.
- [ ] **Step 4: Ownership, progress, tail.** While a run owns the job, disable selection, `Refresh`, every stage
  button, `Run all`, competing SHIP/source-edit actions and tab changes. `Cancel` stays enabled only until requested or
  until a non-interruptible main-thread segment begins, then `Cancel unavailable during {stage}.` Closing the window
  neither releases ownership nor implies cancellation. Progress reuses
  `GUILayout.Box("", GUILayout.Width(Mathf.Max(1f, 240f * done)), GUILayout.Height(6f))` (`SlimPanel.cs:270`) inside a
  fixed track. Tail = one fixed-height box holding only this run's captured log, via the moved `Tail` (Task 1); **a row
  verdict never depends on whichever line currently ends the tail.**
- [ ] **Step 5: GREEN.** `dotnet build -c Release` → `Ошибок: 0`, `Предупреждений: 1`;
  `dotnet run --project tests\ObjCodecTests -c Release` → `LIFECYCLE PASS, ~92` unchanged (this task adds no offline
  arms — it draws snapshots the reducer already proves).
- [ ] **Step 6: Commit.**
  - `git … add src\Dev\LifecycleDashboard.cs src\Dev\FitBench.cs && git … commit -m "feat(dev): the Lifecycle tab - five rows, a constant layout, and no verdict the UI invented"`
- [ ] **Review gate:** as Task 1.

---

### Task 7: the SHIP handoff, the badges and the external Package path

Needs the panel and the existing producer guards. ≤250 lines. Files: `src\Dev\ModelDoctor.cs`,
`src\Dev\LifecycleDashboard.cs`. **Unity-only → compiler + Task 8 rows W14, W17, W18.**

- [ ] **Step 1: SHIP handoff.** A successful SHIP selects the produced project and opens Lifecycle **without running
  another stage**, using the captured absolute `made.Root` (`ModelDoctor.cs:653`, and the second use in `DoShip`) —
  never a name rebuilt from a label — and transfers SHIP's authoritative Apply result (its `ApplyDisposition` and its
  exact string). Unobserved stages stay `never`. Change tabs only after SHIP releases ownership and the current GUI
  event finishes.
- [ ] **Step 2: The S1 barrier.** Apply may PASS while Verify is VOID. `restart required` shows in the global status
  and the Apply installation column; a restart alone turns nothing green. **No dismiss button, no forced unload.**
- [ ] **Step 3: The session block.** Show it whenever the selected id is in the actual `Route7.Failed` set (`:94`) —
  expose a read-only query and `RetryHint` (`:158`), **never the mutable set and never a bypass**. Disable dashboard
  Apply and `Run all` while diagnosis and author-output work stay available. The dashboard follows the checkbox's
  suppression (`:130`); explicit console Apply bypasses it and **the dashboard must not use that bypass**. The badge
  clears only when the set actually clears — a new process, or a successful producer operation reaching `:405`.
  Fixing sources, refreshing or passing Validate clears nothing.
- [ ] **Step 4: Package's destination.** Call `Package.Run` (`:61`) directly into a NEW directory under
  `%LOCALAPPDATA%\ContentTool\Packages\<project>\<run-id>`, outside the game installation, and display the returned
  path. **Never the console wrapper** — `ContentToolMain.cs:511` recursively deletes the previous destination — and
  `Package.Run` itself refuses a nonempty destination (`:78`). A repeated run gets a new directory; there is no
  overwrite control, and `Package.cs:191`'s manual-zip instruction is never printed as if a ZIP existed.
- [ ] **Step 5: GREEN.** `dotnet build -c Release` → `Ошибок: 0`, `Предупреждений: 1`; full offline suite unchanged.
- [ ] **Step 6: Commit.**
  - `git … add src\Dev\ModelDoctor.cs src\Dev\LifecycleDashboard.cs && git … commit -m "feat(dev): SHIP lands on the Lifecycle tab, the session block is shown and never bypassed, and Package writes outside the game"`
- [ ] **Review gate:** as Task 1.

---

### Task 8: in-game acceptance on `D:\PP-Instance2` via PPCLI (**W8–W20**)

≤250 lines (script + this file's evidence rows). **Last, and confirmatory** — every gate it exercises has already
passed offline in the task that owns it. **Do not mark the slice done before this task is green.**

**Command source: `E:\DEV\PhoenixPoint\PPCLI\PLAYBOOK.md`.** Read it and take the exact invocations from there; this
plan spells the shape only, because a stale command line in a plan is worse than no command line.

- [ ] **Step 1: Preflight, in this order.**
  1. **`connect state` FIRST.** The bench on Instance2 is SHARED with the Renderforge session, which may have pulled
     the game into a mission (handoff `2026-09-03-…md:11`, `:27`). A process may already be up; if it is in a mission,
     coordinate before assuming the bench.
  2. Build + deploy to `D:\PP-Instance2` with no game running; confirm `com.morgott.ContentTool` in that profile's
     `MOD_ACTIVATED` (`…LocalLow\Snapshot Games Inc\Phoenix Point\Steam\76561197996210592\Options.jopt` — **Instance2's
     OWN profile**; the user's game is `…591`). Byte-copy that file before the first edit.
  3. **Launch `D:\PP-Instance2\PhoenixPointWin64.exe -mods` BY HAND — never `run` / `batch`**, which stop their game.
  4. Gate on `connect state -PPRoot 'D:\PP-Instance2' -ProfileId 76561197996210592` actually answering, and read
     `failed` on every reply.
  5. **Never the Steam install** (`D:\Steam\steamapps\common\Phoenix Point`) — memory `test-on-instance2-not-steam`.
  6. Additionally require the seam's in-process canonical game root to equal `D:\PP-Instance2` — a mismatch fails
     closed **before any fixture is created**.
- [ ] **Step 2: The helpers.** `D`, `C`, `Shot` exactly as design §8.2 (`…-design.md:427`–`:445`) spells them; RPC and
  console JSON follow `PPCLI\PLAYBOOK.md:306`, `:319`, screenshot JSON `:323`–`:324`. Every `D 'Snapshot'` after an
  asynchronous `Run`/`Acceptance` means **bounded polling until that run reports `busy=false`**; a timeout is a failed
  row, not a pass.
- [ ] **Step 3: Run the suite in this order** — `W8 → W9 → W10 → W16 → W11 → W13 → W20 → W19 → W14 → W12 → restart →
  W15 → W17 → W18`. Rebuild and revalidate after W16 before further success cases. W12/W15 are a pair — preserve the
  fixture. For W18, explicitly reopen and revalidate `DashboardValid` if SHIP selected another project.
- [ ] **Step 4: Fill the table below** with the actual calls made and the actual strings returned.

**Screenshots.** `connect screenshot` captures the **IMGUI/GL layer only** while an upscaler renders to a camera target
(logged in `PPCLI\ISSUES.md`) — which is fine here: the panel rows ARE IMGUI. A response with `ok=true` is **not**
panel proof: open the PNG and read all five rows, the status text, progress, tail and disabled controls. Fields are
documented at `PPCLI\src\Screenshot.cs:162`; the targetTexture branch may also write a separate `.scene.png`
(`:169`–`:175`) — use the image that actually contains IMGUI. Visual mesh acceptance remains the wizard's W7.

**If PPCLI itself misbehaves:** append to `E:\DEV\PhoenixPoint\PPCLI\ISSUES.md` (attempted → happened → expected →
evidence → severity, from a real run only) and SendMessage the open `ppcli-*` session. **Do not fix PPCLI**, do not
commit anything in that repo from this session.

| Row | Calls (from design §8.2) | Required evidence | Result |
|---|---|---|---|
| W8 empty | `Open('')`; `Run('Validate')`; `Snapshot`; `Shot 'W8'` | Open-empty allowed; R25. Five `never / —` rows, placeholders, unavailable actions disabled, no layout exception | |
| W9 selector/Validate | `Acceptance('prepare')`; `Open('DashboardValid')`; `Run('Validate')`; `Snapshot`; `Shot 'W9'` | Disabled fixture included, exact root bound, S3; prev/next/Refresh share the selection path; duplicate name disambiguated by root | |
| W10 happy chain | `Open('DashboardValid')`; `Run('All')`; `Shot 'W10-running'`; `Snapshot`; `Shot 'W10'` | Clean process, target not resident: five rows PASS, Apply S2, exact producer strings, Package writes a new external path. A resident target makes this W12, not W10 | |
| W11 first failure | `Open('DashboardPatchFail')`; `Run('All')`; `Snapshot`; `Shot 'W11'` | Real bake patch-gate failure. Bake FAIL; Apply/Verify/Package start counts stay zero; prior receipts retained | |
| W12 restart required | `Acceptance('resident')`; `Open('DashboardResident')`; `Run('All')`; `Snapshot`; `Shot 'W12'` | A really resident bundle: Apply PASS/S1 with the exact S1 text, Verify VOID/R30, no Package dispatch, no forced unload | |
| W13 cancel | `Open('DashboardValid')`; `Acceptance('arm-cancel-bake')`; `Run('Bake')`; **poll** until `barrierArmed=true` AND `barrierRunId`==this run; `Shot 'W13-armed'`; `Cancel`; **terminal poll** until `busy=false`; `Cancel` again; `Snapshot`; `Shot 'W13'` | The first poll observes THIS run parked (timeout = failed row); Cancel is the button's entry point; terminal poll ends R31/VOID with ONE receipt; later start counts zero; busy clears only after acknowledgement AND worker completion; the second Cancel adds nothing; previous outputs byte-identical | |
| W14 Failed block | `C 'ct_route7' @('apply','DashboardPatchFail')`; `Open('DashboardPatchFail')`; `Run('Apply')`; `Snapshot`; `Shot 'W14'` | Console setup really sets `Failed`; dashboard admission R29; Apply and `Run all` disabled, no retry, no clearing; Validate/refresh cannot clear the badge | |
| W15 restart proof | after restart + identity preflight: `Acceptance('enable-resident')`; `Snapshot`; `Open('DashboardResident')`; `Run('Verify')`; `Snapshot`; `Shot 'W15'` | Real enable callback, fresh load-back, the **per-target** claim/path census of S6 (not `Holds`); a partially claimed fixture must produce VOID naming the unserved target; new-session `Failed` clear | |
| W16 stale | `Open('DashboardValid')`; `Acceptance('change-source')`; `Run('Verify')`; `Snapshot`; `Shot 'W16'` | Receipt becomes stale by an actual key comparison; no old PASS promoted, no automatic Apply. Run after W10 in the same process | |
| W17 SHIP landing | `Acceptance('ship')`; `Snapshot`; `Shot 'W17'` | A real successful SHIP opens Lifecycle after GUI dispatch, selects exactly `made.Root`, transfers the same Apply string and disposition, launches no duplicate bake/apply/package | |
| W18 console parity/package | `Open('DashboardValid')`; `Run('Validate')`; `Snapshot`; `C 'ct_project' @('DashboardValid')`; `Run('Bake')`; `Snapshot`; `Run('Package')`; `Snapshot`; `C 'ct_route7' @('verify','DashboardValid')`; `Run('Verify')`; `Snapshot`; `Shot 'W18'` | Bake payload matches for the same unchanged project and key (**this is also Task 2's extraction proof**). Package matches its producer payload with `ok=true`, writes only a new external directory, previous package intact. **Verify parity: the console verb's terminal line and the dashboard's verdict are the same string character for character**, both out of the one producer; the console call installs and writes nothing; `dryrun/revert/stacktest` still print the unchanged removal text (`Route7.cs:57`–`:63`) | |
| W19 closed-window run | `Open('DashboardValid')`; `Run('Bake')`; close the bench with the chord while it runs; poll until `busy=false`; reopen; `Snapshot`; `Shot 'W19'` | The run completes with the window closed, receipt and log recorded, reopening SHOWS the terminal result without re-running. `Cancel` reachable while closed. A run parked on a blocking main-thread segment says so and resumes once the panel is open and painted | |
| W20 competing producer | `Open('DashboardValid')`; `Acceptance('arm-cancel-bake')`; `Run('Bake')`; poll until parked; `C 'ct_project' @('DashboardValid')`; `Cancel`; poll until `busy=false`; `Snapshot`; `Shot 'W20'` | The console verb hits R37 and returns immediately, writing nothing — no second bake, no key stamped over the parked run's copies. After the cancel the claim is released and a plain `C 'ct_project'` succeeds | |

- [ ] **Step 5: Commit.**
  - `git … add internal-docs\planning\2026-09-05-lifecycle-dashboard-plan.md && git … commit -m "docs(planning): lifecycle dashboard - in-game acceptance W8-W20 on Instance2"`
- [ ] **Final review:** `cx -Review E:\DEV\PhoenixPoint\ContentTool -Base <Task 1's parent> -TimeoutSec 900` for the
  whole slice, then update memory `model-doctor-roadmap` and write the slice handoff.

---

## The acceptance table, offline half

Filled by the task that owns each gate — not deferred to a single "tests task", because the wizard slice showed that a
gate written after its code is a gate written to pass.

| Gate | Owner | Predicted count | Result |
|---|---|---|---|
| G1 state (carrier arms) | Task 1 (carrier) + Task 3 (freshness/receipts) | 8 + 4 | |
| G2 wording (R25–R38, S1–S7, bake special cases, backend passthrough) | Task 1 | 18 | |
| G3 Tail (empty, under/at/over the limit, CRLF and LF, trailing newline, one long line) | Task 1 | inside the 26 | |
| G4 cancel | Task 4 | 8 | |
| G5 sequence | Task 5 | 14 | |
| G6 admission | Task 3 | 22 | |
| G7 publication faults (**real files, plain `System.IO`, no Unity**) | Task 4 | 16 | |
| **Total `LIFECYCLE PASS`** | — | **~92** | |

Unchanged throughout: `PROJECT-SCAFFOLD 89`, `MANIFEST 53`, `ALIAS 32`, `REFUSAL-COUNT 17`, `PACKAGE-GATE 7`,
`MESH extract 57+`, `R0: ALL PASS`, build `Ошибок: 0` / `Предупреждений: 1`. **A changed count in any of those is a
regression to explain, not a number to update.**

## Self-review — PLACEHOLDER

> Filled at the end of Task 8: what the slice actually shipped, which predicted counts were wrong and why, which design
> claims the implementation contradicted, and the `ponytail:` ledger items that survived (design §11).
