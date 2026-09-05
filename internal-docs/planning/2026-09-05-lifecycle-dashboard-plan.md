# Lifecycle dashboard — implementation plan (carrier → extraction → ownership → producers → coordinator → stage producers → panel → SHIP → acceptance)

> **Task order: `1 → 2 → 3 → 4 → 5 → 5b → 6 → 7 → 8`.** Task **5b** was added while Task 5 was being executed, which is
> what exposed it: `LifecycleJob.Start` wires Bake and Package and answers Validate, Apply and Verify with
> `"Lifecycle: <stage> is not wired to the dashboard yet."` (`LifecycleJob.cs:191`), and no task owned those three
> producers. It sits between 5 and 6 because Task 6 draws rows those producers fill.

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
3. **The read-back block is NOT one movable range** (review finding 6, re-read at head). `Patch` is `:1506`–`:1800`, and
   its gates run `:1656`–`:1768` — 113 lines, not 270. Everything after `:1800` is a SEPARATE method: `Live` `:1821`,
   `ByName` `:1827`–`:1952`, then `ParseClipEdit` `:1967`, `Curves` `:1997`, `SampleClip` `:2052`, `Skeleton` `:2161`,
   `PixelsIn` `:2228`, `SamePixels` `:2241`, `Check` `:2251`. Confirmed anchors: `P1` `:1665`, `P1-ctl-shipped` `:1667`,
   `P3` `:1677`, `P4` `:1695`, `P4-bytes VOID` `:1722`, `P4-bytes` counted `:1727`, `P5 VOID` `:1744`, `P5` `:1752`, the
   `ByName` call `:1760`, the clip loop `:1762`–`:1768`. **SEVEN `P6 VOID` arms, not six:** `:1832`, `:1849`, `:1862`,
   `:1872`, `:1892`, `:1920`, `:1939` (the design names two, the plan named six, disk has seven).
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
| the P6 VOID arms are `:1832` and `:1917` (§4.4) | at head there are **seven**: `:1832`, `:1849`, `:1862`, `:1872`, `:1892`, `:1920`, `:1939`. Task 2 must carry **every** VOID arm into the carrier, not the two the design names |
| the read-back block is `ProjectBake.cs:1661`–`:1930` (§4.4, §10) | `Patch` ENDS at `:1800`. The range spans a method boundary: gates `:1656`–`:1768` inside `Patch`, then `ByName` `:1827`–`:1952` and six private helpers past `:1960`. See fact 3 — this is why Task 2 is re-scoped |
| `BundleClaims.Held`, queried per target (§5) | `Held` is a **private** `List<BundleClaim>` (`BundleClaims.cs:182`). The callable per-bundle lookup is `Find(bundleFile)` `:221`; `All` `:184` exposes the list read-only |
| "no automatic `Uninstall` (`BundleLive.cs:138`)" (§5) | `Uninstall` is `:127`; `:138` is a comment inside it. Behaviour as described |

## Codex plan review 2026-09-05

Source: `C:\Temp\cx\a47fcf98181b4dbca93d1d0d53882d4d.out.md` — 15 findings (4 blockers, 10 major, 1 minor), a sequencing
paragraph and 5 wrong facts. **All 15 accepted**, all 5 wrong facts applied. Nothing rejected.

| # | Sev | Taken as |
|---|---|---|
| 1 | blocker | R38 asks `BundleClaims.Find` (`:221`), not the private `Held` list (`:182`) — Task 3 Step 4 |
| 2 | blocker | the publication core becomes a UnityEngine-free `src\Bake\Publication.cs`, LINKED into `ObjCodecTests`, so G7 drives production code — Task 4 |
| 3 | blocker | the seam returns bounded JSON **strings** (PPCLI projects anything else as a handle) — Task 5 Step 2 |
| 4 | blocker | W10's install makes `DashboardValid` un-re-bakeable under R38; the baking rows move to an uninstalled `DashboardAuthor` — Task 8 |
| 5 | major | one narrow phased-import entry on `ContentProject.Load` — Task 4 Step 2 |
| 6 | major | the extraction closure enumerated; six helpers become `internal` IN PLACE instead of moving — Task 2 |
| 7 | major | `--bake` is the SOUND harness; the extraction proof becomes a real byte baseline — Task 2 Step 5 |
| 8 | major | the csproj/`Program.cs` registration is Task 1 **Step 0**, before the RED |
| 9 | major | `Write` keeps its own cleanup guard; `Publish` is `void` — Task 2 Step 2 |
| 10 | major | `BakeResult` carries a disposition (Success/Refused/Cancelled/Failed) and the two dirs are acquired atomically — Task 3 |
| 11 | major | the `PatchCache` observation is captured OUTSIDE the reducer and passed in — Task 3 Step 5 |
| 12 | major | a minimal structured `Install`/`ApplyProject` overload lands in Task 4, consumed by Task 7 |
| 13 | major | W19 splits into a worker-only arm and a main-thread parked arm — Task 8 |
| 14 | major | every task bounded by explicit files and sub-task commits; T2/T3/T4 re-estimated |
| 15 | minor | `Route7` routes R29/R35 THROUGH `StageText` — no test-only second copy — Task 1 |

**Sequencing (accepted, task NUMBERS unchanged, `1 → 2 → 3 → 4 → 5 → 6 → 7 → 8` retained; **5b** inserted after 5 by
the gap Task 5's execution found — see the note under the title):** the read-only `Route7.Failed`
query and the freshness contract move INTO Task 3, where admission first needs them, instead of first appearing in Task 7;
the phased import, the publication core and the structured multi-target Apply/Verify move INTO Task 4; Task 7's safe
external Package destination is settled before Task 5 exposes `Run("Package")`; Task 7 consumes producers Tasks 3–4
already guarded; Task 2's byte baseline is captured before its extraction commit.

**Wrong facts, all corrected below:** `BundleClaims.Held` is private; the read-back block is not one `:1661`–`:1930`
range (`Patch` ends `:1800`); `--bake` runs the sound-bank harness; `Publish` returns `void`; and Task 8 is confirmatory
for MOST but not all gates — the extraction, thread-split and seam proofs are genuinely deferred to it (P:245, P:366,
P:420), which is now labelled instead of claimed away.

> **Anchor drift.** The review read `58b15f3` and this plan was written at `5fc3404`; the anchors below are re-read at the
> CURRENT head. They differ from both by up to ten lines in `ProjectBake.cs`. Re-measure before Task 1 regardless.

## File Structure

**Created**

| Path | Responsibility |
|---|---|
| `src\Bake\StageResult.cs` | The carrier: `Outcome { Pass, Fail, Void }`, `GateEntry` (gate id, target key, outcome, the producer's exact line), `ReadBackResult` (`Failed`/`Passed`/`Void` counts + entries + the terminal line), `BakeResult` (`failed`, `patchFailed`, terminal line), `StageResult` (stage, outcome, freshness, verdict text, counts, generation). **UnityEngine-free, test-linked.** Also the new home of `Tail` (fact 7) |
| `src\Bake\StageText.cs` | The ONE verdict formatter: S1–S7, R25–R38, the bake special cases, the `Validate: FAIL - {reason}` / `Verify: FAIL - {reason}` fallbacks, the transient `Message` strings. Pure string composition, no IO. **UnityEngine-free, test-linked** |
| `src\Bake\ReadBack.cs` | The read-back gates extracted from `Patch` (`ProjectBake.cs:1656`–`:1768`) plus `Live` `:1821` and `ByName` `:1827`–`:1952`, behind `ReadBackResult`, called by BOTH bake and the Verify producer. The six shared helpers do NOT move — they become `internal` in place (fact 3). Unity + AssetsTools → build gate + W18 |
| `src\Bake\Publication.cs` | **NEW, finding 2.** The B5 publication core, UnityEngine-free and AssetsTools-free: given `(temp, dest)` pairs, the key path/text, an invalidate step, a "is this file claimed or resident?" predicate and a cancel check, it invalidates the old key, `AtomicFile.Publish`es each complete copy and writes the new key LAST. **Test-linked** — this is the file G7 drives, so the fault arms run the production sequence instead of a copy of it. `ProjectBake`'s B5 and `LifecycleJob` are its only callers |
| `src\Bake\OutputClaim.cs` | The process-wide in-flight claim: one `HashSet<string>` of canonical output directories under one lock, `OrdinalIgnoreCase`; `Take`/`Release`/`Held`. UnityEngine-free, test-linked |
| `src\Bake\LifecycleState.cs` | The pure reducer: receipts, freshness (`never`/`stale`/`fresh`), `Admit(stage)` (§4.6 table), the `Run all` sequencer, cancel bookkeeping. **No filesystem, no Unity.** Test-linked — this is what G1/G5/G6 exercise |
| `src\Bake\LifecycleJob.cs` | Segmented producers: the main→worker→main split, captured Unity-derived paths, `SlimJob`-shaped ThreadPool dispatch, progress, cancellation, the B1–B5 publication boundary. Unity → build gate + G7 (which tests the publication primitive, not this file) |
| `src\Dev\LifecycleDashboard.cs` | The panel + the public static seam `Open(string)` / `Run(string)` / `Cancel()` / `Snapshot()` + test-instance-only `Acceptance(string)`. Namespace `Morgott.ContentTool.Dev` (`ModelDoctor.cs:13`, `FitBench.cs:28`) |
| `src\Bake\StageValidate.cs` | **Task 5b.** The Validate producer: `ManifestFile.Load` → `Manifest.Validate()` → `PatchCache.Key`, plus `ModGate.Decide`/`Why` as an eligibility FIELD. The roster arrives as an `IDictionary<string,bool>` captured on main, so nothing here touches Unity. **Test-linked** — G8 drives it |
| `tests\ObjCodecTests\LifecycleTests.cs` | G1–G9. Prints `LIFECYCLE PASS, N check(s) - …` |

**Modified**

| Path | Change |
|---|---|
| `src\IO\AtomicFile.cs` `:17`–`:41` | add `void Publish(tempPath, path, backupPath)` holding the existing swap (`:31`/`:32`) **and its own orphan-temp `finally`**; `Write` becomes "write bytes to my temp, call `Publish`" but **KEEPS its outer best-effort cleanup** (`:34`–`:40`) — an exception in the open, write or flush never reaches `Publish`, so moving the guard wholesale would strand `Write`'s temp (finding 9). ONE swap, two cleanup owners, each for its own temp |
| `src\Bake\ProjectBake.cs` `:69`, `:142`, `:1644`, `:1656`–`:1768`, `:1821`–`:1952` | the claim (R37) + live-reader refusal (R38) at the body's entry; the gates and `ByName`/`Live` move to `ReadBack.cs` while `Check` `:2251`, `PixelsIn` `:2228`, `SamePixels` `:2241`, `Skeleton` `:2161`, `ParseClipEdit` `:1967`, `Curves` `:1997` and `SampleClip` `:2052` only change `private`→`internal` (`Check` alone has 25+ callers elsewhere in this file — moving it is churn for nothing); `Patch` writes to a temp and publishes through `Publication` (B2/B5); the Dist pre-delete at `:142` goes away with the temp treatment; `Run` also returns a `BakeResult` beside the two `out` counts |
| `src\Bake\Route7.cs` `:56`, `:129`–`:132`, `:287`–`:311`, `:341`, `:349`–`:351`, `:353`, `:405` | R29 `:129`–`:132` and R35 `:349`–`:351` are composed BY `StageText`, not copied into it (finding 15); `verify` leaves the removal arm and routes to the Verify producer; the key write at `:353` moves INTO the shared bake completion; the declared-copy census becomes the shared freshness helper; a read-only `IsFailed(modId)` + `RetryHint` accessor over the private `Failed` `:94`; a structured `ApplyProject` overload returning per-target dispositions |
| `src\Bake\BundleLive.cs` `:55`–`:68` | **removed from "NOT modified" (finding 12).** `Install` collects a line per target (`:59`–`:65`) and throws them away; add a minimal overload that also hands back the per-target `(bundle, line, outcome)` list, keeping the existing string-returning wrapper exactly as it is. Nothing else in the class is touched — `Register` `:74`, `Uninstall` `:127`, `Holds` `:145` are unchanged |
| `src\Project\ContentProject.cs` `:305`–`:384` | **one narrow phased entry (finding 5).** `Load(root)` keeps its signature and becomes a wrapper over `Load(root, pump)`, where the pump carries a cancel check, a phase/progress callback and a "run this on main" marshal. The Unity-bound phases stay marshalled: `JsonUtility` `:310`, `ImportTexture` `:328`–`:329` (→ `Texture2D` `:628`). Refusal and audio-id accounting is untouched **by construction** — the `ref uint next` loop `:365`–`:373` and the single `ImportFailures = p.SourceRefusals.Count` at `:382` stay exactly where they are |
| `src\Dev\ModelDoctor.cs` `:71`, `:710`–`:714`, `:745` | S1/S2 come from `StageText`; `Tail` moves to `StageResult.cs` and is called from there; the SHIP handoff hands `made.Root` + its Apply result to the dashboard |
| `src\Dev\FitBench.cs` `:1672`–`:1674`, `:2104`–`:2112` | `bool doctorTab` → a three-state tab; the lifecycle pump call between `:2106` and the `StillThere` return |
| `tests\ObjCodecTests\ObjCodecTests.csproj` | link `StageResult.cs`, `StageText.cs`, `OutputClaim.cs`, `LifecycleState.cs`, `Publication.cs`; `..\..\src\IO\AtomicFile.cs` is already at `:37`, `..\..\src\Import\Json.cs` (the `JsonWriter` the seam composes with) at `:145`; compile `LifecycleTests.cs` |
| `tests\ObjCodecTests\Program.cs` `:142` | `Console.WriteLine(LifecycleTests.Run());` after `ProjectScaffoldTests.Run()` |
| `internal-docs\planning\2026-09-05-lifecycle-dashboard-plan.md` | Task 8 fills in the in-game evidence table, in this file |

**NOT modified:** `Manifest`, `ManifestFile`, `AliasMap`, `BundleBaker`, `BundleClaims`, `Package`, `PatchCache`,
`ContentMods`, `ModGate`, `ModRoster`, `ProjectScaffold`, `ShippedTarget`. The dashboard is a CALLER of every one of
them. In particular `Package.Run` keeps its own allowlist and refusals as the sole authority, and `BundleLive.Uninstall`
is never called by anything in this slice. **`BundleLive` left this list** (finding 12): the structured `Install`
overload has no other possible owner. `BundleClaims` stays on it — R38 asks the existing `Find` `:221`.

---

### Task 1: the carrier and the one verdict formatter

**First, because every later task calls it** (§10). ~230 impl lines (`StageResult.cs` ~90, `StageText.cs` ~110, the
`ModelDoctor`/`Route7` call-site changes ~30) plus ~200 test lines. Files: `src\Bake\StageResult.cs`,
`src\Bake\StageText.cs`, `tests\ObjCodecTests\LifecycleTests.cs`, `ObjCodecTests.csproj`, `Program.cs`,
`src\Dev\ModelDoctor.cs` (Tail moves out, S1/S2 read from `StageText`), `src\Bake\Route7.cs` (R29/R35 read from
`StageText`).

> **DONE at `cd32eef` (Step 0) + `fc1c4a4` (Steps 1–4).** Real gate: **`LIFECYCLE PASS, 50 check(s)`** against
> the predicted ~26 — the G2 wording table has 26 arms of its own (every S, every R, the three bake special
> cases, the fallbacks, the transients, the placeholders), G1 13 and G3 9. Baseline re-measured at `27c2a53`
> and unchanged after: `PROJECT-SCAFFOLD 89`, `MANIFEST 53`, `ALIAS 32`, `REFUSAL-COUNT 17`, `PACKAGE-GATE 7`,
> `MESH extract 64` (the plan's `57+`), `R0: ALL PASS`, `Ошибок: 0` / `Предупреждений: 1`.
> **Three deviations, disk over plan:**
> 1. the carrier enum is **`GateOutcome`**, not `Outcome` — `Morgott.ContentTool.Import.Outcome` already exists
>    and `BundleBaker.cs:203` uses it unqualified under `using Morgott.ContentTool.Import`, so a second
>    `Outcome` in `Bake` makes those lines CS0104-ambiguous. `None` is its idle member.
> 2. **`StageResult` ships as the static home of `Tail` only.** Its five row fields (`Stage`, `Verdict`,
>    `Outcome`, `Freshness`, `Generation`) have no producer until Task 3's receipts and were five CS0649s;
>    Task 3 lands them with the reducer that fills them.
> 3. `ReadBackResult.Terminal` is **readonly, set by a second `Of(terminal, …)` overload** rather than a
>    settable field — same reason (CS0649 in `ContentTool.csproj`, which never assigns it yet).
> Also: `StageText.R28` takes a **`Freshness`**, not a freshness string, so "never/stale/fresh" has one
> spelling. `ProjectBake` (S4/S5, the three bake special cases) and `Package` (S7, the refusal) are NOT routed
> through `StageText` — both are outside Task 1's file list, `Package` is on the "NOT modified" list, and
> `ProjectBake` is Task 2's file; those five strings are quoted in `StageText` with a `ponytail:` note to route
> `ProjectBake` when Task 2 opens it.

- [x] **Step 0: Register the gate BEFORE writing it** (finding 8). `ObjCodecTests.csproj` sets
  `EnableDefaultCompileItems=false` (`:6`), so a `LifecycleTests.cs` that is not in the `<Compile Include>` list is not
  compiled and Step 1's "RED" would be a green build that silently ran nothing. Add the `<Compile Include>` entry and the
  `Console.WriteLine(LifecycleTests.Run());` line in `Program.cs` first, with a stub `Run()` returning
  `"LIFECYCLE PASS, 0 check(s)"`.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release` → the marker PRINTS. That is the proof the gate is wired;
    a compile failure alone never was.

- [x] **Step 1: Write the failing gate.** Fill `tests\ObjCodecTests\LifecycleTests.cs` — G1's carrier arms, all of G2 and
  **G3's `Tail` cases** (empty, under/at/over the limit, CRLF and LF, trailing newline, one long line; the offline table
  at the end of this file assigns G3 here and no other step implemented it). Shape, disk wins:
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
  - Expected RED, **in two stages** (finding 8): first the build fails on the missing `StageText`/`ReadBackResult` types;
    then, once the types exist as empty shells, a COMPILING run that fails on the wrong result. A compile error alone
    proves nothing about whether `Program` reaches the gate — Step 0 is what proved that.

- [x] **Step 2: The carrier.** `src\Bake\StageResult.cs` — no interface, no builder, one type per fact (design §4.4:
  "one type with one producer"). `Outcome { Pass, Fail, Void }`; `GateEntry { string Gate, Target, Line; Outcome
  Outcome; }`; `ReadBackResult { int Failed, Passed, Void; IList<GateEntry> Entries; string Terminal; }` plus the §4.4
  **mandatory-proof** predicate (mesh row → `P4` AND `P4-bytes` non-VOID; texture row → `P1` AND `P1-ctl-shipped`;
  material row → `P3`). `BakeResult { int Failed, PatchFailed; string Terminal; }`. `StageResult { Stage, Outcome,
  Freshness, Verdict, Generation }`. Move `Tail` here from `ModelDoctor.cs:745` unchanged (fact 7) and make
  `ModelDoctor` call it — **freeze its current semantics, do not "fix" them** (G3).

- [x] **Step 3: The formatter.** `src\Bake\StageText.cs`. Each string is copied from the file:line below; nothing is
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

  **No second copy of a production string** (finding 15). A `StageText` copy that only a test reads is a second truth,
  and G2 would then prove the two copies agree while the producer drifts from both. So `Route7` is EDITED here:
  `:129`–`:132` composes R29 through `StageText.R29(id, RetryHint(modDir))` and `:349`–`:351` composes R35 through
  `StageText.R35(patchFailed)`. The dashboard forwards the producer's returned line at runtime; G2 compares the
  formatter against the strings copied from disk, and the producer now IS the formatter.

- [x] **Step 4: GREEN.** (Step 0 already linked `LifecycleTests.cs` and registered it; link the two new src files here.)
  - Run: `dotnet build -c Release` → `Ошибок: 0`, `Предупреждений: 1`
  - Run: `dotnet run --project tests\ObjCodecTests -c Release` → exit 0, `PROJECT-SCAFFOLD PASS, 89` unchanged,
    **new `LIFECYCLE PASS, ~26 check(s)`** (prediction: 8 carrier arms + 18 wording arms)
  - Run: `dotnet run --project tests\TargetPathTests -c Release` → `R0: ALL PASS`

- [x] **Step 5: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add src\Bake\StageResult.cs src\Bake\StageText.cs src\Bake\Route7.cs src\Dev\ModelDoctor.cs tests\ObjCodecTests\LifecycleTests.cs tests\ObjCodecTests\ObjCodecTests.csproj tests\ObjCodecTests\Program.cs && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(bake): a structured stage carrier and one verdict formatter, so an all-VOID read-back is not a PASS"`

- [ ] **Review gate:** `cx -Review E:\DEV\PhoenixPoint\ContentTool -Commit <sha> -TimeoutSec 600` (background) +
  `caveman:cavecrew-reviewer` in parallel → triage (ponytail: reject pedantry, fix real defects with a fix agent,
  re-review) before Task 2 starts.

---

### Task 2: the read-back extraction and `AtomicFile.Publish`

**Both seams before any caller reaches for them** (§10). Files: `src\Bake\ReadBack.cs` (new),
`src\Bake\ProjectBake.cs`, `src\IO\AtomicFile.cs`, `tests\ObjCodecTests\LifecycleTests.cs`, `ObjCodecTests.csproj`.
Lands as **three green commits**: (a) `Publish`, (b) the mesh/texture/material extraction, (c) the clip gates.

**The dependency closure, enumerated** (finding 6 — the review is right that a literal `:1661`–`:1930` move does not
compile, and the ponytail answer is that most of the closure should not move at all):

| Symbol | At | Disposition |
|---|---|---|
| the gate body inside `Patch` | `:1656`–`:1768` (113 lines) | **MOVES** to `ReadBack.cs` |
| `Live` | `:1821` (5 lines) | **MOVES** — only `ByName` calls it |
| `ByName` | `:1827`–`:1952` (126 lines) | **MOVES** — only `:1760` calls it |
| `Check` | `:2251` | **STAYS**, `private`→`internal`. 25+ callers elsewhere in `ProjectBake` (`:375`, `:389`, `:451`, `:492`, `:522`, … `:2090`); moving it is pure churn |
| `PixelsIn` | `:2228` | **STAYS**, `private`→`internal`. Calls `SamePixels`, which has a caller outside the block (`:374`) |
| `SamePixels` | `:2241` | **STAYS**, `private`→`internal` (`:374`) |
| `Skeleton` | `:2161` | **STAYS**, `private`→`internal` |
| `ParseClipEdit` / `Curves` / `SampleClip` | `:1967` / `:1997` / `:2052` | **STAY**, `private`→`internal`. `Curves` also collides with the unrelated `Curves` `:1063` in the same class, so moving it would need a rename for nothing |

**Re-estimated size:** ~248 moved lines + ~55 of entry signature, captured-expectation plumbing and the `Patch` call
site + ~30 for `Publish` = **≈330 impl lines across three commits**, not the ≤300 single slot the plan advertised. Six
accessibility keywords change; nothing else in `ProjectBake` past `:1960` is touched.

**Before anything else in this task: capture the baseline** (finding 7, and Step 5 depends on it). Bake a real fixture
project in game through `ct_project`, and save into the session scratchpad both (a) the SHA-256 of every produced
`.bundle` and (b) the full printed gate log. Without those two artefacts the extraction has nothing to be compared
against and W18 degenerates into comparing two post-change runs.

- [ ] **Step 1: RED for `Publish`.** Add to `LifecycleTests.Run()` — real files in a temp directory, `System.IO` only:
  ```csharp
  // ---- Publish: the ONE swap. A temp the caller streamed, published over an existing file and over
  // an absent one, and an orphaned temp deleted on the failing path.
  AtomicFile.Publish(tmp, dest, bak);            // void (:218 below), so it is a STATEMENT, never an expression
  checks += Check(File.ReadAllBytes(dest).SequenceEqual(streamed) && !File.Exists(tmp),
                  "Publish swaps the caller's temp and leaves none behind");
  ```
  `SequenceEqual`, never `==` — `byte[] == byte[]` compares references and would pass on two arrays that differ.
  - Expected RED: no `Publish` method.

- [ ] **Step 2: `Publish`.** In `src\IO\AtomicFile.cs`, extract the swap `Write` already performs (`:31`–`:32`) and give
  `Publish` its OWN orphan-temp `finally`. Shape, disk wins:
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
  `Write` then becomes: write the bytes to its own temp (keeping the flush at `:29`), call `Publish` — **and it keeps
  its existing outer `finally` `:34`–`:40` exactly as it is** (finding 9). Today that guard cleans up after a failed
  `FileStream` open, a failed write, a failed flush OR a failed swap. If cleanup lived only inside `Publish`, a throw
  before the swap would never reach it and `Write` would strand its own temp — a regression the current code does not
  have. Two temps, two owners: `Write` cleans the temp it made, `Publish` cleans the temp it was handed.
  Add the arm that proves it: a `Write` whose stream throws mid-write leaves no `.tmp` behind.
  `WriteText` `:45` is untouched. Where a file must be created and never overwritten, callers keep using the
  absent-only `File.Move` arm directly, as the wizard slice requires.

- [ ] **Step 3: GREEN + commit (a).**
  - Run: `dotnet run --project tests\ObjCodecTests -c Release` → `LIFECYCLE PASS, ~32` (+6 publish arms),
    `PROJECT-SCAFFOLD 89`, `MANIFEST 53`, `ALIAS 32` unchanged (they call `Write`, whose behaviour must not move)
  - `git … add src\IO\AtomicFile.cs tests\ObjCodecTests\LifecycleTests.cs && git … commit -m "refactor(io): one file swap - AtomicFile.Publish takes a temp the caller streamed, Write routes through it"`

- [ ] **Step 4: The extraction (b) — mesh, texture and material gates.** Move `ProjectBake.cs:1656`–`:1761` (everything
  up to and including the `ByName` call) plus `Live` `:1821` and `ByName` `:1827`–`:1952` into `src\Bake\ReadBack.cs`,
  as one entry that takes the captured expectations, shipped paths and existing copy paths, **never rewrites and never
  installs**, and returns `ReadBackResult`. Flip the six helpers in the closure table to `internal` and call them from
  `ReadBack`. Each `log.AppendLine(...)` becomes `GateEntry` + the SAME line appended to the same log — the line text
  does not change. Carry **all seven** P6 VOID arms (`:1832`, `:1849`, `:1862`, `:1872`, `:1892`, `:1920`, `:1939`), the
  `P4-bytes` VOID `:1722` and its counted comparison `:1727`, `P5 VOID` `:1744` and `P5` `:1752`, `P1` `:1665`,
  `P1-ctl-shipped` `:1667`, `P3` `:1677`/`:1679`, `P4` `:1695` and the uncounted `P4-ctl-shipped` diagnostic `:1705`.
  Bake keeps its existing counting behaviour — **a VOID stays uncounted there, exactly as today**.
  - `Patch` calls it and adds `result.Failed` to its own `failures` exactly where the inline block did.
  - Unity + AssetsTools → **compiler + Step 5's byte comparison + Task 8 row W18**.
  - `git … add src\Bake\ReadBack.cs src\Bake\ProjectBake.cs && git … commit -m "refactor(bake): the load-back gates move out of Patch behind the stage carrier, one producer for bake and verify"`

- [ ] **Step 5: The extraction (c) — the clip gates.** Move the clip loop `:1762`–`:1768` into the same entry; leave
  `ParseClipEdit` `:1967`, `Curves` `:1997` and `SampleClip` `:2052` where they are as `internal`. Separate commit
  because it is a separate green: a project with no clips exercises none of it, so the fixture used for Step 6 must be
  one that HAS a clip edit or this half is unproven.
  - `git … add src\Bake\ReadBack.cs src\Bake\ProjectBake.cs && git … commit -m "refactor(bake): the clip read-back gates join the shared producer"`

- [ ] **Step 6: Prove nothing moved — the real gate** (finding 7). `--bake` is **NOT** an extraction proof: it dispatches
  the SOUND-BANK harness (`Program.cs:62` → `Bake()` `:22`–`:39`, which calls `WwisePcm.ReadWav`, `BuildWem` and
  `BankGen.BuildMediaOnly`) and never reaches `ProjectBake.Patch` at all. Run it, but only as an unrelated regression
  check. The extraction proof is:
  - the pre-extraction baseline captured at the head of this task (per-bundle SHA-256 + the full gate log), compared
    against a bake of the SAME fixture at the same key through the extracted producer: **bytes identical, and the gate
    log identical line for line**. Both halves — a matching log over changed bytes, or matching bytes over a reworded
    log, is a failure.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release -- --bake <wav> <mediaId> - <out.bnk>` and the full suite;
    expected: unchanged markers, exit 0.
  - **This is genuinely deferred to Task 8 row W18** — it needs a game session. It is not a gate that "already passed
    offline"; see the Task 8 preamble.

- [ ] **Review gate:** as Task 1.

---

### Task 3: producer ownership, admission and freshness

Needs Task 1's outcomes. **≈340 impl lines** (`OutputClaim.cs` ~70, `LifecycleState.cs` ~170, `ProjectBake.Run` wiring
~45, `Route7` ~35, `BakeResult` dispositions ~20) — over the ≤280 the plan advertised, so it lands as **two green
commits**: (a) the claim + R38 in the producer, (b) admission, freshness and the `Failed` query. Files:
`src\Bake\OutputClaim.cs` (new), `src\Bake\LifecycleState.cs` (new — the `Admit` half and freshness),
`src\Bake\StageResult.cs` (the `BakeResult` disposition), `src\Bake\ProjectBake.cs` `:69`, `src\Bake\Route7.cs` `:94`,
`:287`–`:311`, `:353`, `tests\ObjCodecTests\LifecycleTests.cs`, `ObjCodecTests.csproj`.

**Pulled in from Task 7 by the review's sequencing paragraph:** the read-only `Route7.Failed` query lands HERE, because
admission (R29) is the first thing that needs it — Task 7 only DISPLAYS it. Add `internal static bool IsFailed(string
modId)` beside the private set `:94` and expose `RetryHint` `:158`; never the mutable set, never a bypass. The clearing
path stays `Failed.Remove(modId)` `:405` alone.

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
  **Acquire BOTH directories atomically** (finding 10): one call takes the pair under the single lock and returns
  either both or neither. Taking them one at a time lets two runs each hold one and refuse each other forever; the
  `finally` must also unwind a partial acquisition if the second is ever added separately.
  When the caller is `ApplyProject`, the claim is **passed down**, not re-taken, so Apply's own bake does not deadlock
  against itself — held through A4 (including the fresh-cache Apply path that never bakes) and released only in the
  owner's `finally`. A forced same-key bake is not exempt.

- [ ] **Step 3b: Dispositions on the carrier** (finding 10). `BakeResult` gains an outcome —
  `Success | Refused | Cancelled | Failed` — because `failed`/`patchFailed` counts cannot express the new refusals:
  R37 and R38 return with **zero** counts, and today `ApplyProject` reads only `patchFailed != 0` (`Route7.cs:341`)
  before enumerating and installing (`:355`–`:399`), so a zero-count refusal would install the STALE copies as if the
  bake had succeeded. Encoding contention as a patch failure instead is equally wrong: it reaches `Failed.Add(modId)`
  `:345` and poisons the session block over a race nobody caused. So: `Refused`/`Cancelled` stop Apply without touching
  `Failed`; `Failed` alone reaches `:345`; and the key + declared-copy census (`:307`–`:311`) is **re-checked
  immediately before Install**, not only in the `!haveAll` branch it is evaluated in today (`:309`).

- [ ] **Step 4: The live-reader refusal (R38), general.** Before replacing any claimed file, ask
  **`BundleClaims.Find(bundleFile)` `:221` per target** and compare `c.Mod` against this project's id and `c.Path`
  against the copy about to be replaced (finding 1). `BundleClaims.Held` `:182` is a **private** `List<BundleClaim>` and
  is not a per-target query; `All` `:184` is its read-only view if an enumeration is ever needed. `BundleClaims` stays
  unmodified — `Find` already answers this. Never `BundleLive.Holds` (`:145` → `BundleClaims.Holds` `:296`, which
  returns true on the FIRST claim of that mod and so passes with two copies and one claimed target). Applies to a
  stale, fresh, repair or forced same-key bake alike. The answer is a restart boundary, never rewriting beneath live
  readers. Residency is sampled on main.

- [ ] **Step 5: Freshness, once — observed OUTSIDE the reducer** (finding 11). `LifecycleState` is declared
  filesystem-free, and `PatchCache.Key` (`:43`/`:49`) and `Fresh` (`:84`) both read and enumerate files — the two
  contracts cannot both hold, and `PatchCache` is deliberately absent from the test links. So: **one** filesystem
  observation is taken by the caller (a small `FreshnessObservation { string Key, CachedKey; bool CacheDirExists;
  IList<string> MissingCopies; }`), and `LifecycleState` receives that value and decides. The reducer stays pure and
  test-linked; `PatchCache` stays out of `ObjCodecTests.csproj`.
  The observation is `PatchCache.Key(root, shipped)` + `Fresh` + **the declared-copy census exactly as
  `Route7.cs:310`–`:311` does it** — `Fresh` compares key text only, so the census is the other half. **The same helper
  is what Apply calls**, replacing the inline `haveAll` (`:309`–`:311`), so the two cannot drift. `never` = no receipt;
  `stale` = a receipt exists but inputs differ or a required output vanished (an old cache directory with no key is
  **stale, not never**); `fresh` = receipt matches and outputs exist. Recompute on explicit refresh, at stage start and
  after completion — **never in `OnGUI`**. Move the successful patch-key publication out of `Route7.cs:353` into the
  shared bake completion.

- [ ] **Step 6: GREEN.**
  - Run: `dotnet build -c Release` → `Ошибок: 0`, `Предупреждений: 1`
  - Run: `dotnet run --project tests\ObjCodecTests -c Release` → `LIFECYCLE PASS, ~54` (+22: 12 admission rows, 6 claim
    arms, 4 freshness arms), everything else unchanged
- [ ] **Step 7: Commits** (two greens).
  - (a) `git … add src\Bake\OutputClaim.cs src\Bake\StageResult.cs src\Bake\ProjectBake.cs tests\ObjCodecTests\LifecycleTests.cs tests\ObjCodecTests\ObjCodecTests.csproj && git … commit -m "feat(bake): one owner per output directory and a general live-reader refusal the console verb gets too"`
  - (b) `git … add src\Bake\LifecycleState.cs src\Bake\Route7.cs tests\ObjCodecTests\LifecycleTests.cs tests\ObjCodecTests\ObjCodecTests.csproj && git … commit -m "feat(bake): stage admission and one freshness observation, so nothing re-implements a dependency graph"`
- [ ] **Review gate:** as Task 1.

---

### Task 4: segmented producers, progress, cancellation and the B1–B5 publication boundary

Needs Tasks 2 and 3. **≈520 impl lines** (`LifecycleJob.cs` ~200, `Publication.cs` ~90, `ProjectBake` B1–B5 ~120,
`ContentProject` phased entry ~50, the structured `Install`/`ApplyProject` overload ~60) — far over the ≤300 the plan
advertised, so it lands as **four green commits**, listed at Step 6. **G4 and G7 ship WITH this code, not after it.**
Files: `src\Bake\Publication.cs` (new), `src\Bake\LifecycleJob.cs` (new), `src\Bake\ProjectBake.cs` (B1–B5 inside
`Patch`/`Run`), `src\Project\ContentProject.cs` `:305`, `src\Bake\BundleLive.cs` `:55`, `src\Bake\Route7.cs` `:269`,
`tests\ObjCodecTests\LifecycleTests.cs`, `ObjCodecTests.csproj`.

**Why `Publication.cs` exists at all** (blocker finding 2). B1–B5 as originally planned lived only in `ProjectBake` and
`LifecycleJob`, both of which pull `UnityEngine` and `AssetsTools` and therefore CANNOT be linked into
`ObjCodecTests`. A G7 that then implements its own invalidate/swap/key sequence proves nothing about production and
stays green while the real bake stamps the key first. The linked `AtomicFile.Publish` only swaps ONE file, so it does
not cover the ordering either. Fix, smallest form: the ordering — invalidate old key → publish each complete copy →
write the new key LAST — moves into a UnityEngine-free `src\Bake\Publication.cs`, added to `ObjCodecTests.csproj` next
to `Package.cs` `:83` and `Manifest.cs` `:38`. Faults are injected through its `(temp, dest)` list, its invalidate step
and its claimed/resident predicate. `ProjectBake`'s B5 becomes a call into it, and **that file is in commit (b)**.

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

- [ ] **Step 2: The thread split, and the ONE entry that makes it callable** (finding 5). Main → worker → main, per
  stage (§4). Capture on MAIN and hand in as strings: `BakeSelfCheck.ShippedBundlePath`
  (`Application.streamingAssetsPath`, `:739`), `ContentToolMain.PatchedRoot` (`persistentDataPath`, `:65`),
  `InstallTag` (`dataPath`, `:74`) — those three captures are correct as written. A worker that calls one of those is a
  bug. Main work: Unity sampling, embedded texture decoding (`ProjectBake.cs:1347`), bundle loads (`:341`/`:351`), rig
  instantiation, the Unity Verify gates. Gates whose Unity dependence is uncertain stay on MAIN.
  **The import boundary the plan was missing:** `ContentProject.ImportModel` is `private static` (`:691`) and
  `ImportAudio` is `private` *instance* with `ref uint nextId, out string why` (`:760`) — neither is callable from a
  job, and no task named a `ContentProject` edit. Nor can the job dispatch `Load` (`:305`) wholesale: it calls
  `JsonUtility.FromJson` (`:310`) and `ImportTexture` (`:328`–`:329`, which constructs `Texture2D` `:628`), both
  main-thread. So add exactly ONE narrow entry — `Load(root, pump)` under the existing `Load(root)` `:305` wrapper —
  where the pump supplies a cancel check, a phase callback and a marshal-to-main. The refusal and audio-id accounting
  is preserved by leaving it in place: the `ref uint next` loop `:365`–`:373`, the `ReplaceRefusals` delta `:341`/`:345`
  and the single `p.ImportFailures = p.SourceRefusals.Count` at `:382` are not moved, reordered or duplicated.
  `Package` needs no such entry — it is plain `System.IO` by construction (`Package.cs:15`) and runs wholly on a worker.

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
  **B5's ordering lives in `Publication.cs`, not inline** — that is what makes G7 a production test.
  If publication fails midway: files individually complete, key absent → FAIL, Apply forbidden until a repair bake. If
  key invalidation fails, publish nothing. **A publication ordering, not a transaction and not a crash rollback.**
  Apply follows the same shape: A1 revalidate (the Task 3 freshness helper, re-run here), A2 final cancel check, A3 the
  main-thread `Install` loop with no cancellation and no yields, A4 publish the dispositions then release the claim.
  **No automatic `Uninstall`** (`BundleLive.Uninstall` `:127`).

- [ ] **Step 4b: The structured multi-target Apply** (finding 12 — it had no owner in any task, and Task 7 was written
  to consume it). `BundleLive.Install` `:55` builds one line per target (`:59`–`:65`) and then discards the list into
  an aggregate (`:66`); `ApplyProject(name, null, out how)` leaves `how` at its initial `Refused` (`:271`, only
  overwritten inside `if (ours)` `:406`–`:414`), so the console-shaped call reports a refusal that did not happen. The
  dashboard cannot recover per-target truth from either without parsing the log or installing twice, both forbidden.
  Minimum mechanism: **one overload each**, existing wrappers untouched — `BundleLive.Install(modId, copies, out
  IList<TargetInstall>)` handing back `(bundle, line, outcome)` per target from the loop it already runs, and
  `Route7.ApplyProject(root, out IList<TargetInstall>, out ApplyDisposition)` aggregating conservatively (any refusal
  survives; any S1 survives; no blanket LIVE). Lands **here**, before Task 7's badges consume it.

- [ ] **Step 4c: three MUSTs carried over from the Task 3 review — none of them optional.**
  - **(a) Publish the freshness key at the SHARED bake completion, inside B5, while the output dirs are still
    claimed.** Today the ONLY `PatchCache.Write` is inside Apply (`Route7.cs:350`), so a standalone Bake leaves
    `Route7.Observe` reading `never`/`stale` and `LifecycleState.Admit("Verify")` refuses R28 over copies that were
    just baked — Verify would require an Apply, i.e. a change to game state, to become admissible at all (Codex P2 on
    `b81b43b`). After this step a standalone Bake leaves the observation FRESH and Verify is admitted with no Apply.
  - **(b) Probe R38 and resolve the patched directory ON MAIN, before the job reaches the worker.**
    `BundleClaims.Find` walks the unlocked static list main mutates (`ProjectBake.cs:1986`), and
    `ContentToolMain.PatchedDir` reads `Application.persistentDataPath` — both are main-thread facts. Capture the
    R38 verdict and the resolved directory on MAIN and hand them in as values, the same rule as the three captures in
    Step 2.
  - **(c) One test line for `BakeDisposition.Cancelled` precedence** when its producer lands: `Route7.cs:392` already
    treats `Refused` and `Cancelled` alike (return before `patchFailed` is read), so the arm asserts a cancelled bake
    never reaches `Failed.Add` and never installs.

- [ ] **Step 5: GREEN.**
  - Run: `dotnet build -c Release` → `Ошибок: 0`, `Предупреждений: 1`
  - Run: `dotnet run --project tests\ObjCodecTests -c Release` → `LIFECYCLE PASS, ~78` (+24: 8 cancel arms, 16 G7
    filesystem arms), everything else unchanged. G7 leaves no temp directory behind — assert that too.
  - Unity-only halves (the thread split, the Install loop): **compiler + Task 8 rows W10, W12, W13** — genuinely
    deferred proof, not a re-run of something already green.
- [ ] **Step 6: Commits** (four greens, each with its own gate):
  - (a) `src\Bake\Publication.cs` + `ObjCodecTests.csproj` + the G7 arms — `refactor(bake): the publication ordering becomes one linkable file, so its fault arms run the real sequence`
  - (b) `src\Bake\ProjectBake.cs` B1–B5 on top of it — `feat(bake): temps then a non-cancellable publication - a cancelled bake can no longer leave output nobody can classify`
  - (c) `src\Bake\LifecycleJob.cs` + `src\Project\ContentProject.cs` + the G4 arms — `feat(bake): segmented producers with a phased import entry, cancellation and progress`
  - (d) `src\Bake\BundleLive.cs` + `src\Bake\Route7.cs` — `feat(bake): a structured multi-target Apply, so a per-target disposition survives the install loop`
- [ ] **Review gate:** as Task 1.

---

### Task 5: the `Run all` coordinator, the seam, the pump and the acceptance fixtures

Needs Tasks 3 and 4. **≈380 impl lines** (sequencer ~90, seam + JSON sectioning ~140, pump ~25, `Acceptance` fixtures
~110, Package destination resolver ~15) — over ≤300, so **four green commits** (Step 6). Files:
`src\Bake\LifecycleState.cs` (the sequencer), `src\Dev\LifecycleDashboard.cs` (new — seam + `Acceptance` only, no
drawing yet), `src\Dev\FitBench.cs` `:2104`, `tests\ObjCodecTests\LifecycleTests.cs`.

**The sequencer MUST NOT re-implement §4.6's graph.** The `Run all` column conditions — Bake after Validate PASS, Apply
after a Bake that did not FAIL, Verify after Apply and S1 → R30 — become fields of `Admission` and arms of `Admit`
(`LifecycleState.cs:102`) **in Task 5's first commit**, and the sequencer only reads them. A second copy of the
dependency graph in the coordinator is the exact drift §4.6 exists to prevent.

- [ ] **Step 1: RED — G5 sequence.** All succeeds; each stage fails in turn; the S1 barrier; a prerequisite refusal;
  cancellation. Assert invocation order and count, first stop position, earlier receipts unchanged, and that
  **Package is not entered after `Run all` stops at Verify**. `Run all` calls `Admit` per stage **as it reaches that
  stage**, never up front. A gate VOID alone does not fail a row; an absent mandatory proof stays VOID and blocks
  completion. `Failed` is never cleared on Validate or Bake success — `Route7.cs:405` stays the only clearing path.

- [ ] **Step 2: The seam — a JSON-STRING contract, because PPCLI cannot return anything else** (blocker finding 3).
  `connect call` returns what `PPCLI\src\Reflect.cs` can project, and `Project` `:1080` **never** enumerates or walks
  properties: a non-trivial reference becomes `{h, type}`, a collection becomes a handle plus a count, and only
  primitives, strings, enums, `Guid`/`DateTime`/`TimeSpan` and a value type with ≤4 primitive **fields**
  (`TryInlineStruct` `:1150`–`:1156`) come back inline. A snapshot object of five rows would arrive as one useless
  handle. Two consequences, both binding:
  - **every seam method is `public static` and returns `string`.** `Invoke` filters to statics when no target is given
    (`Reflect.cs:479`–`:480`), so an instance method is unreachable by construction.
  - **the string is bounded JSON.** `Protocol.Clip` truncates at `MaxOutputLineChars = 2000`
    (`PPCLI\src\Protocol.cs:56`, `:256`–`:260`) and appends ` ...(clipped)` — silently, mid-token, producing JSON that
    does not parse. Five verbatim producer verdicts exceed that easily, so `Snapshot` is **sectioned**, never one blob.

  ```csharp
  // src\Dev\LifecycleDashboard.cs - the whole RPC surface. Composed with the existing
  // Morgott.ContentTool.Import.JsonWriter (src\Import\Json.cs, already linked at ObjCodecTests.csproj:145),
  // so no JSON dependency enters the tool and the seam is offline-testable.
  public static string Open(string projectName);      // {"ok":bool,"root":string,"id":string,"error":string}
  public static string Run(string stage);             // {"ok":bool,"runId":string,"refusal":string}  <- the RUN HANDLE
  public static string Cancel();                      // {"ok":bool,"runId":string,"acknowledged":bool}
  public static string Snapshot(string section);      // see below; every payload carries "bytes" and "truncated"
  public static string Acceptance(string scenario);   // {"ok":bool,"scenario":string,"error":string}
  ```
  `Snapshot("")` is the **poll header** and is written to stay under ~1200 chars: game root, selected root/id, `runId`,
  `busy`, `stage`, `cancelRequested`/`cancelAcknowledged`, `failedMember`, `claimHeld`, `barrierArmed`, `barrierRunId`,
  and per row only `{stage, freshness, outcome, verdictLength}` — no verdict text. `Snapshot("Verify")` (or any stage
  token) returns that ONE row's verbatim producer verdict plus its start counts; `Snapshot("log")` returns the tail;
  `Snapshot("s1s2")` returns the S1/S2 strings. A payload that still would not fit sets `"truncated":true` and names
  the section to ask for — it never emits JSON that `ConvertFrom-Json` chokes on.
  **The run-handle protocol:** `Run` returns the accepted `runId`; every subsequent poll compares
  `Snapshot("").runId` against it, so a poll can never read a NEWER run's state and call it this run's result.
  `barrierArmed` is published with `barrierRunId` **only when the worker is actually parked**, never on arming alone —
  otherwise W13's first poll passes before the run exists.

  Semantics unchanged from the design: `Open("")` clears the selection explicitly and is **never** passed to the
  existing name resolver, whose empty-name default is Sample (`ContentMods.cs:153`–`:154`); a unique name resolves to a
  canonical root before `LoadDeclared` (`ContentProject.cs:289`), which takes a ROOT holding `ppcontent.json`, not a
  name; an ambiguous name is rejected. `Run` enqueues the same intent as the button, returns promptly, and **never**
  performs a synchronous Apply from the RPC call. Accepted tokens are exactly `Validate`, `Bake`, `Apply`, `Verify`,
  `Package`, `All` — anything else is R33. `Snapshot` is observational and cannot validate, apply or clear anything.
  *(`PPCLI` is read here and never edited — `PPCLI\docs\REFERENCE.md`, `PPCLI\AGENTS.md`, `PPCLI\PLAYBOOK.md`.)*

- [ ] **Step 2b: Settle Package's destination BEFORE `Run("Package")` is reachable** (the review's sequencing note).
  Task 7 Step 4 owns the `%LOCALAPPDATA%\ContentTool\Packages\<project>\<run-id>` path and the "never the console
  wrapper" rule (`ContentToolMain.cs:511` recursively deletes the previous destination). That decision has to be in
  place HERE, because this task is what first exposes `Package` to an RPC caller. Implement the destination resolver
  now; Task 7 only displays the returned path.

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
  setting `Failed`, residency, `Holds` or verdict fields directly. `prepare` creates `DashboardValid`,
  `DashboardPatchFail` and **`DashboardAuthor`** (an uncontested target that is never applied, so R38 cannot fire on
  its bakes — see Task 8's fixture table); `resident` prepares `DashboardResident` and actually loads its target
  bundle; `change-source` changes only the selected fixture; `ship` drives the real Doctor fixture through its existing
  selection and `Enqueue` path; `enable-resident` invokes the actual mod-manager enable callback after a restart.
  `arm-cancel-bake` **arms** the barrier for the NEXT run and returns immediately, publishing its armed state and run id
  through `Snapshot`; it releases on the same `Cancel()` the UI calls and lets normal worker completion publish VOID —
  no sleep-based race, no `Thread.Abort`, no synthetic success, no detached worker. **The barrier parks a WORKER and
  never the main-thread pump** — parking the pump makes `Snapshot` unanswerable and W13/W20 unpollable by construction.

- [ ] **Step 5: GREEN.**
  - Run: `dotnet build -c Release` → `Ошибок: 0`, `Предупреждений: 1`
  - Run: `dotnet run --project tests\ObjCodecTests -c Release` → `LIFECYCLE PASS, ~92` (+14 sequence arms)
  - The JSON sectioning is NOT Unity-only — assert offline that `Snapshot("")` stays under 2000 chars with five
    maximum-length verdicts loaded, and that every section round-trips through a parser. That is the arm which stops
    `Protocol.Clip` from silently truncating a poll in game.
  - Seam behaviour, pump and fixtures are Unity-only: **compiler + Task 8 rows W13, W19, W20** — deferred proof.
- [ ] **Step 6: Commits.** (a) sequencer + G5; (b) the seam + its JSON-bound arms; (c) pump + closed-window policy;
  (d) `Acceptance`.
  - final: `git … commit -m "feat(dev): one dispatch path for buttons and RPC, and a run that survives a closed bench"`
- [ ] **Review gate:** as Task 1.

---

### Task 5b: stage producers — Validate, Apply, Verify (+ `ct_route7 verify`)

**The gap Task 5 exposed, and it has no other owner.** `LifecycleJob.Start` (`src\Bake\LifecycleJob.cs:182`) wires
`Bake` `:184` and `Package` `:185` and answers every other token with `"Lifecycle: " + stage + " is not wired to the
dashboard yet."` `:191` — a line that is not a producer verdict and not a refusal, on three of the panel's five rows.
`ct_route7 verify` still refuses through the `dryrun|verify|revert|stacktest` arm (`src\Bake\Route7.cs:56`–`:63`), so
§4.4's "one Verify producer, two consumers" has neither consumer. And Task 5 left two `Admission` fields with no writer:
`LegacyDiskActive` (`LifecycleState.cs:394`) and `WriteOutsideRoots` (`:397`) are read by `Admit` `:540`–`:541` and set
by nobody, so **R36 and R34 are unreachable code**.

Needs Tasks 3, 4 and 5. **≈390 impl lines** (`StageValidate.cs` ~70, `LifecycleJob` three starts + `Captured` ~95,
`ReadBack.Verify` ~75, `LifecycleState.VerifyVerdict` ~18, `Route7` verify verb + root entry + legacy accessor ~45,
`Acceptance` scenarios ~70, `LifecycleDashboard` capture reorder ~12) plus ~120 test lines — over the ≤300 this task was
scoped at, so it lands as **three green commits** (Step 7). The fat one is (c); `Acceptance('ship')` is deliberately NOT
in it (Step 6). Files: `src\Bake\StageValidate.cs` (new), `src\Bake\LifecycleJob.cs`, `src\Bake\ReadBack.cs`,
`src\Bake\ProjectBake.cs` (three `private`→`internal`), `src\Bake\Route7.cs`, `src\Bake\LifecycleState.cs`,
`src\Dev\LifecycleDashboard.cs`, `tests\ObjCodecTests\LifecycleTests.cs`, `ObjCodecTests.csproj`.

**Five facts this task is built on, read at the current head rather than assumed:**

1. **§4.1's four calls are all UnityEngine-free and three of their files are ALREADY linked** into `ObjCodecTests`
   (`Manifest.cs` `:38`, `ModGate.cs` `:94`, `PatchCache.cs` `:58`). `ManifestFile.Load(path)` is `Manifest.cs:290`,
   `Manifest.Validate()` `:200` (throws `InvalidDataException`), `PatchCache.Key(root, shippedPaths)` `:43`,
   `ModGate.Decide(modFolder, roster)` `ModGate.cs:34` and `ModGate.Why(v)` `:57`. The ONE Unity-bound half is
   `ModRoster.Build()` (`ModRoster.cs:53`, `ModManager.Mods`), which is why §4.1 splits exactly there: **main captures
   the roster dictionary, the worker validates.** That split is what makes the Validate producer offline-armable.
2. **`ReadBack.Run` is already the shared producer** (`src\Bake\ReadBack.cs:37`), takes
   `(StringBuilder log, string bundleFile, string shipped, string copy, List<ImportedTexture> want,
   List<KeyValuePair<string,string>> mats, List<KeyValuePair<string,ImportedMesh>> meshes,
   List<KeyValuePair<string,ShippedReplacement>> clips)` and returns `ReadBackResult` with a **null** `Terminal` — the
   terminal line is the CALLER's to compose. Bake's only call site is `ProjectBake.cs:1879`, against the B2 temp.
3. **The expectation lists cannot be reused from `Patch`.** They are filled inside the patch loop, interleaved with the
   writer: `want.Add(t)` `:1842` after `baker.ReplaceTexture2D` `:1841`, `meshes.Add` `:1822` after `baker.ReplaceMesh`
   `:1810`, `mats.Add` `:1771`, `clips.Add` `:1790`. Verify must build the same four lists from the imported project
   ALONE — `Bundles(p)` `:2223`, `Find(p, r.texture)` `:2268`, `FindMesh(p, r.mesh)` `:2275`, all three `private` today —
   and never construct a `BundleBaker`, so nothing is opened for writing and `baker.WhyNot` is not asked (the gates
   measure the copy directly).
4. **`Route7.ApplyProject` takes a NAME, not a root.** Every overload (`:347`, `:357`, `:365`) funnels into the private
   `:371`, which resolves `ContentToolMain.ProjectDir(projectName)` `:376` — and `ProjectDir` is
   `ContentMods.ProjectDir(ModDir, name)` (`ContentToolMain.cs:37`), a NAME lookup by construction (the game console
   eats backslashes, `:28`–`:32`). The dashboard binds a canonical ROOT (Task 5 Step 2) and a duplicate name resolves to
   the wrong folder, so Apply needs a root-taking entry. The structured overload T4 landed —
   `Route7.ApplyProject(string projectName, out IList<TargetInstall> targets, out ApplyDisposition how)` `:357`, over
   `BundleLive.Install(modId, bundleToCopy, out IList<Route7.TargetInstall> targets)` `BundleLive.cs:66`, with
   `TargetInstall { Bundle, Line, Outcome }` `Route7.cs:326` and `ApplyDisposition { Redirected, Resident, Refused,
   BakeFailed }` `:319` — but it resolves by name like the others.
5. **`Route7.LegacyDisk(modId)` is `private static string`** (`Route7.cs:259`), returning the refusal text or `null`.
   `LegacyPub` `:247` is the OTHER route (published keys) and is **not** what R36 is about.

**Three design claims corrected against disk** (§4.1's anchors all hold; these do not):

| Design says | Disk at this head |
|---|---|
| §4.3 "Main entry `Route7.ApplyProject(root, forBundle, out how)` `:269`", `Refused` default `:271` | the entry is `:365` and the default is `:375` (Task 4 moved them); and it takes a **name**, not a root — fact 4 |
| §4.6 "fields of `Admission` and arms of `Admit` (`LifecycleState.cs:102`)" | `Admission` is `LifecycleState.cs:383`, `Admit` is `:519`. Behaviour as described |
| §4.4 "the declared-copy census (`Route7.cs:310`–`:311`)", §7 R29 "reuse `RetryHint` `:158`" | the census became `Route7.Observe(patched, projectRoot, declared, shippedPaths)` `:147`–`:160` returning `FreshnessObservation`; `RetryHint` is `:225` and R29 is composed by `StageText.R29` (`StageText.cs:161`). Both are Task 1/3 moves, not errors |

**One ordering defect Task 5 shipped, fixed in commit (b).** `LifecycleDashboard.Refresh` (`:248`) builds the
`Admission` and `Run` `:95` calls `Admit` with it — but `captured` is only filled in `Dispatch` `:266`, which runs
**after** the admission verdict. Every captured fact admission needs (fact 5's legacy verdict, the write-root check)
would therefore be read one run late, or never on the first press. The capture moves into `Refresh`, before `Admit`;
`Dispatch` keeps its `captured.Root != root` guard so nothing is captured twice.

- [ ] **Step 1: RED — G8 (Validate) offline, on the pure core.** Add to `LifecycleTests.Run()`. `StageValidate` is
  UnityEngine-free and linked, so every arm here drives production code. Shape, disk wins:
  ```csharp
  // ---- G8 Validate: the pure half, with the roster handed in as a dictionary exactly as MAIN captures it.
  // Real files in a temp directory, System.IO only - ManifestFile.Load reads bytes (Manifest.cs:292).
  LifecycleState.StageReport ok = StageValidate.Run(root, Path.Combine(root, ContentMods.Manifest),
                                                    new[] { shippedA }, roster);
  checks += Check(ok.Outcome == GateOutcome.Pass && ok.Verdict == StageText.S3("DashboardValid"),
                  "S3 verbatim, and Validate PASS is the producer's own line");
  checks += Check(ok.Eligibility == ModGate.Why(ModVerdict.Disabled),
                  "a DISABLED project still validates - eligibility is a field, never the verdict (design:103)");
  ```
  Arms: a manifest that validates → `S3` + `Pass`; a duplicated target (E4, `Manifest.cs:205`) → `Fail` +
  `StageText.ValidateFailed(ex.Message)`; a missing `ppcontent.json` → `IOException` → the same fallback, one line;
  `roster == null` → `NoRoster` eligibility, outcome still `Pass`; a roster that lists the folder as disabled →
  `Disabled` eligibility, outcome still `Pass`; the key is computed and non-empty. **A throw never escapes** — the
  producer answers with `ValidateFailed`, the same rule `LifecycleJob.Capture` `:88`–`:98` already applies.
  - Expected RED: no `StageValidate` type, then a compiling run that fails on the wrong verdict.

- [ ] **Step 2: The Validate producer.** `src\Bake\StageValidate.cs` — new, UnityEngine-free, added to
  `ObjCodecTests.csproj` beside `LifecycleState.cs` `:51`. No new type where `StageReport` (`LifecycleState.cs:426`)
  already carries what the pump consumes; it gains **one** field. Shape, disk wins:
  ```csharp
  // src\Bake\StageValidate.cs - 4.1, and nothing more: declaration structure plus activation eligibility.
  // It does NOT prove assets import or targets exist (design:102) and it never writes.
  internal static LifecycleState.StageReport Run(string projectRoot, string manifestPath,
                                                 IList<string> shippedPaths,
                                                 IDictionary<string, bool> roster)
  {
      string name = Path.GetFileName(projectRoot.TrimEnd('\\', '/'));
      try
      {
          ManifestFile mf = ManifestFile.Load(manifestPath);   // Manifest.cs:290 - E1/E2/E8
          mf.Manifest.Validate();                              // :200 - E3 row, E4 duplicate target
          PatchCache.Key(projectRoot, shippedPaths);           // :43 - it must be computable, not stored here
      }
      catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ArgumentException)
      {
          return new LifecycleState.StageReport(GateOutcome.Fail, StageText.ValidateFailed(ex.Message),
                                                BakeDisposition.Failed, false, true, null);
      }
      // DISABLED IS NOT MALFORMED (design:103): its own field, never folded into the verdict.
      return new LifecycleState.StageReport(GateOutcome.Pass, StageText.S3(name), BakeDisposition.Success,
                                            false, true, ModGate.Why(ModGate.Decide(projectRoot, roster)));
  }
  ```
  `StageReport` gains `internal readonly string Eligibility;` as the sixth constructor argument — Task 6 draws it beside
  the Validate row and **`Admit` never reads it**, because `Disabled` blocks nothing (§4.6's activation column).
  Catch BY TYPE, the three §4.1 can produce; anything else is a bug in here and reaches `LifecycleJob.Worker`'s handler
  (`:289`–`:299`), which says a stage threw and keeps the exception.

- [ ] **Step 3: Wire it into `LifecycleJob.Start`, and capture the roster on MAIN.** `Captured` (`:60`–`:64`) gains
  `internal IDictionary<string, bool> Roster;` filled in `Capture` `:106`–`:114` with `ModRoster.Build()` — main-thread,
  like the three path captures at `:110`/`:113`/`:115`. Then:
  ```csharp
  internal static string StartValidate(Captured on)
  {
      long id = Run.Begin("Validate");
      if (id == 0) return StageText.R26(Run.Latest.Stage);
      // WHOLLY A WORKER, like StartPackage (:203): the roster is already a dictionary and the other three
      // calls are plain System.IO. No parked main segment, so it completes with the bench closed.
      Worker(delegate
      {
          LifecycleState.StageReport r = StageValidate.Run(on.Root, Path.Combine(on.Root, ContentMods.Manifest),
                                                          on.Shipped, on.Roster);
          Observe(on);
          Run.Complete(id, r.Verdict, r.How);
      });
      return null;
  }
  ```
  and `Start` `:182` grows `if (stage == "Validate") return StartValidate(on);` above the Bake arm. The `ponytail:`
  block `:186`–`:191` loses its Validate sentence; the fallback line stays until commit (c) deletes the last of it.
  Unity-only half: **compiler + Task 8 rows W9, W18.**

- [ ] **Step 4: GREEN + commit (a).**
  - Run: `dotnet build -c Release` → `Ошибок: 0`, `Предупреждений: 1`
  - Run: `dotnet run --project tests\ObjCodecTests -c Release` → `LIFECYCLE PASS, ~98` (+6 G8 arms), everything else
    unchanged. The temp fixture directory is deleted in a `finally`, like G7's.
  - `git -C E:\DEV\PhoenixPoint\ContentTool add src\Bake\StageValidate.cs src\Bake\LifecycleState.cs src\Bake\LifecycleJob.cs tests\ObjCodecTests\LifecycleTests.cs tests\ObjCodecTests\ObjCodecTests.csproj && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(bake): the Validate producer - a manifest checked and eligibility reported, with the roster captured on main"`

- [ ] **Step 5: The Apply producer, and the two admission fields nobody set.** Four small pieces, one commit.
  - **(a) A root-taking entry, because the panel binds a root and `ApplyProject` resolves a NAME** (fact 4). The private
    body `:371`–`:389` already receives `projectRoot` at `:376`; split it there — everything from
    `ContentProject.Load(projectRoot)` `:377` down keeps its claim take/release exactly as it is and becomes
    `ApplyRoot(string projectRoot, string forBundle, out IList<TargetInstall> targets, out ApplyDisposition how)`, and
    the name overloads become `ApplyRoot(ContentToolMain.ProjectDir(name), …)`. **No behaviour change, no second claim
    path**, and the console verb `:51` keeps resolving by name.
  - **(b) The smallest legacy accessor.** Beside `LegacyDisk` `:259`, one line:
    `internal static bool LegacyActive(string modId) { return LegacyDisk(modId) != null; }` — a bool, never the text.
    R36's wording is `StageText.R36()` `:199` and the panel must not print a producer refusal it did not receive.
  - **(c) `WriteOutsideRoots`, decided on MAIN with the paths already captured.** `Captured` gains
    `internal bool LegacyDiskActive, WriteOutsideRoots;`. In `Capture`, after `OutputDirs` `:113`: the apply's two
    sanctioned roots are `ContentToolMain.PatchedRoot` (`ContentToolMain.cs:59`, `persistentDataPath`) and the project
    root itself; any captured output dir whose `Path.GetFullPath` is under neither sets the flag. `Route7.LegacyActive(d.Id)`
    fills the other. Both are main-thread facts (`persistentDataPath`, the static edits ledger), same rule as the R38
    verdict at `:117`–`:125`.
  - **(d) `Refresh` reads them, before `Admit` runs.** In `LifecycleDashboard.Refresh` `:248`, move the capture up
    (`if (captured == null || captured.Root != root) captured = LifecycleJob.Capture(root);`, the line `Dispatch` `:266`
    holds today) and add `ctx.LegacyDiskActive = captured != null && captured.LegacyDiskActive;` /
    `ctx.WriteOutsideRoots = captured != null && captured.WriteOutsideRoots;`. That is what makes `Admit` `:540`–`:541`
    reachable at all.
  Then the producer itself, parked on MAIN because `Install` is a Unity segment with no yields (§5's A3):
  ```csharp
  internal static string StartApply(Captured on)
  {
      long id = Run.Begin("Apply");
      if (id == 0) return StageText.R26(Run.Latest.Stage);
      cts = new CancellationTokenSource();
      Worker(delegate
      {
          Observe(on);                                   // A1 revalidate, on a worker
          Park(delegate                                  // A3: no cancellation, no yields inside
          {
              IList<Route7.TargetInstall> targets; Route7.ApplyDisposition how;
              string line = Route7.ApplyRoot(on.Root, null, out targets, out how);
              // The DISPOSITION classifies, never the text (design:361). Resident is a PASS that needs a
              // restart - S1 - and that is what arms R30 for Verify; Refused is VOID, not a failure.
              Run.Complete(id, line, how == Route7.ApplyDisposition.BakeFailed ? BakeDisposition.Failed
                                   : how == Route7.ApplyDisposition.Refused ? BakeDisposition.Refused
                                   : BakeDisposition.Success);
              Worker(delegate { Observe(on); });
          }, true);
      });
      return null;
  }
  ```
  The pump's `StageReport` for this row carries `RestartRequired = how == Resident` and
  `Applicable = targets.Count > 0` — a project with no non-video target is "VOID with a reason" and does **not** stop
  the chain (§4.6's Apply row). **No automatic `Uninstall`**, and `ApplyRoot` is called exactly once per run.
  RED→GREEN split: the disposition mapping is Unity-side → **compiler + W10, W12, W14, W17**; the offline arms are on
  `Admit`, which is linked — R36 fires with `LegacyDiskActive`, R34 with `WriteOutsideRoots`, R36 **before** R34 before
  R29 (the order at `:540`–`:542`), and neither fires with both false. +4 G6 arms.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release` → `LIFECYCLE PASS, ~102`
  - `git … add src\Bake\Route7.cs src\Bake\LifecycleJob.cs src\Dev\LifecycleDashboard.cs tests\ObjCodecTests\LifecycleTests.cs && git … commit -m "feat(bake): the Apply producer on the structured overload, and the two admission fields that made R34/R36 dead code"`

- [ ] **Step 6: The Verify producer, ONE of it, and the console path that shares it** (§4.4 item 9).
  - **The producer lands in `ReadBack.cs`**, beside the gates it drives — not in a third file that would have to reach
    into them. `Bundles` `ProjectBake.cs:2223`, `Find` `:2268` and `FindMesh` `:2275` become `internal` in place, the
    same disposition Task 2 gave `Check`/`PixelsIn`/`SamePixels`; nothing moves.
    ```csharp
    // src\Bake\ReadBack.cs - THE Verify producer. Two consumers, one string: the dashboard row and
    // `ct_route7 verify <name>` both print what this returns, which is what W18 compares byte for byte.
    // It READS: no BundleBaker is constructed, so nothing is opened for writing and `WhyNot` is never asked.
    internal static LifecycleState.StageReport Verify(ContentProject p, string patchedDir, StringBuilder log)
    ```
    Per declared bundle from `ProjectBake.Bundles(p)`: `shipped = BakeSelfCheck.ShippedBundlePath(b)`,
    `copy = Path.Combine(patchedDir, b)`. An absent `copy` is a **census miss** — the target is named and NO gate is run
    over a file that is not there. Otherwise build the four lists from `p`'s rows for that bundle with `Find`/`FindMesh`
    (fact 3 — the resolution is rebuilt, the WORDING is not: every line still comes out of `Run`), call
    `Run(log, b, shipped, copy, want, mats, meshes, clips)` and accumulate `Failed` and the entries.
    Then the **per-target claim census, never `BundleLive.Holds`** (`BundleLive.cs:166` → `BundleClaims.Holds` `:296`,
    which returns true on ONE matching `c.Mod` — design:388): for every declared bundle, `BundleClaims.Find(b)` `:221`
    must be non-null, `.Mod == p.Id` and `.Path` must be this project's `copy`.
  - **The decision is pure and linked, so both consumers are proven offline.** `LifecycleState` gains
    ```csharp
    internal static StageReport VerifyVerdict(string name, int served, int declared,
                                              bool mandatoryVoid, string voidLine, int failed)
    ```
    with the §4.4/§7 rules in ONE place: `failed > 0` → `Fail` + `StageText.VerifyFailed`; `mandatoryVoid` → `Void` with
    **the gate's own line** (`voidLine`, never relabelled); `declared == 0` → `Pass` + `StageText.S8(name)`;
    otherwise `StageText.S6(name, served, declared)`, which is already the function that refuses to word a shortfall as
    PASS (`StageText.cs:60`–`:68`) — `served == declared` → `Pass`, else `Void`. `ReadBack.Verify` gathers and calls it;
    it composes no verdict of its own.
  - **The console path.** In `Route7.Run`'s switch, `verify` leaves the removal arm — `dryrun`, `revert` and
    `stacktest` keep `:56`–`:63`'s text **unchanged**, which W18 re-checks:
    ```csharp
    case "verify":
        return args != null && args.Length > 1
            ? VerifyProject(args[1])
            : "usage: ct_route7 verify <project> - re-read this project's patched copies. It installs " +
              "nothing and writes nothing.";
    ```
    `VerifyProject(name)` = `ContentProject.Load(ContentToolMain.ProjectDir(name))` →
    `ReadBack.Verify(p, ContentToolMain.PatchedDir(p.Id), log)` → print `log` then `r.Verdict`. **No install, no write,
    no key.**
  - **The dashboard consumer.** `LifecycleJob.StartVerify(Captured on)`: a `Park(…, true)` main segment (the gates
    sample Unity), `ContentProject.Load(on.Root, pump)` for the expectations, `ReadBack.Verify(p, on.PatchedDir, log)`,
    `Run.Complete(id, r.Verdict, r.How)`. `Start` `:182` gains its arm and **the `ponytail:` fallback line `:191` is
    deleted with it** — every token now reaches a producer.
  - **`Acceptance`, the scenarios that are decidable now** (`LifecycleDashboard.cs:164`–`:188`). The bench already has
    `Mods\Replace_Leftleg` from the wizard slice on Instance2, so a fixture no longer has to be invented — it is
    COPIED. One helper (`Fork(source, name, mutate)`: copy the tree, rewrite `"id"` in `ppcontent.json`, return the
    root) serves three of the four:
    | Scenario | What it needs, exactly |
    |---|---|
    | `prepare` | `Mods\Replace_Leftleg` present beside `ContentToolMain.ModDir`; refuses NAMING that path when absent. Forks it into `DashboardValid` (unchanged rows), `DashboardPatchFail` (`"asset"` retargeted to a name the shipped bundle does not contain → a real `MissingTarget` P4 failure, `ProjectBake.cs:1807`) and `DashboardAuthor` (retargeted to a bundle no other fixture and no live claim contests, so R38 cannot fire on its bakes — Task 8's fixture table) |
    | `change-source` | only the already-forked fixture. Rewrites one source byte under `Content\`, which is what `PatchCache.Key` stats (`Route7.Observe` `:150`) → the receipt goes stale by an actual key comparison, not by a flag |
    | `resident` | a bundle the running game has ALREADY loaded. It does not invent one: it asks `BundleLive.ResidentNow(b)` (`BundleLive.cs:174`) over the shipped names and forks `DashboardResident` onto the first that answers true — and refuses, saying so, when none does. That refusal is the honest answer, not a fixture bug |
    | `enable-resident` | `DashboardResident` on disk from the previous session, then the REAL checkbox body `Route7.Toggle(modDir, true)` (`Route7.cs:162`) — not a roster edit and not a fabricated claim |
    `ship` stays UNIMPLEMENTED here, and for a reason that belongs to another task: it needs a Doctor with a loaded
    preview and its `made.Root` (`ModelDoctor.cs:653`), which **Task 7 Step 1** is what wires. Its refusal names Task 7.

- [ ] **Step 7: GREEN and the three commits.**
  - Run: `dotnet build -c Release` → `Ошибок: 0`, `Предупреждений: 1`
  - Run: `dotnet run --project tests\ObjCodecTests -c Release` → `LIFECYCLE PASS, ~108` (+6 G9 verdict arms:
    empty census → S8; shortfall → S6's VOID wording; a mandatory VOID → the gate's own line, unrelabelled;
    `failed > 0` → `VerifyFailed`; full census → S6 PASS; and a shortfall that ALSO has a mandatory VOID reports the
    VOID line, not the census sentence). `PROJECT-SCAFFOLD 89`, `MANIFEST 53`, `ALIAS 32`, `REFUSAL-COUNT 17`,
    `PACKAGE-GATE 7`, `R0: ALL PASS` unchanged.
  - Run: `dotnet run --project tests\TargetPathTests -c Release` → `R0: ALL PASS`
  - Unity-only halves — the read-back over an existing copy, the claim census, the console verb, the fixtures:
    **compiler + Task 8 rows W15, W16, W17, W18**. W18's parity assertion (console terminal line == dashboard verdict,
    character for character) is now structurally true: one producer, one `VerifyVerdict`.
  - (c) `git … add src\Bake\ReadBack.cs src\Bake\ProjectBake.cs src\Bake\Route7.cs src\Bake\LifecycleState.cs src\Bake\LifecycleJob.cs src\Dev\LifecycleDashboard.cs tests\ObjCodecTests\LifecycleTests.cs && git … commit -m "feat(bake): one Verify producer for the panel and ct_route7 verify, and the acceptance fixtures that are forks of a real project"`
- [ ] **Review gate:** as Task 1.

---

### Task 6: the drawing and the third FitBench tab

Needs Task 5's snapshots and **Task 5b's producers** — five rows cannot be drawn over three stages that answer
"not wired to the dashboard yet". ≤280 lines. Files: `src\Dev\LifecycleDashboard.cs` (the `Draw` half), `src\Dev\FitBench.cs`
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
  never a name rebuilt from a label — and transfers SHIP's authoritative Apply result through the **structured
  per-target dispositions Task 4 added** (`ApplyDisposition` + the exact producer string per target), never by parsing
  the log and never by re-installing. Unobserved stages stay `never`. Change tabs only after SHIP releases ownership
  and the current GUI event finishes.
- [ ] **Step 2: The S1 barrier.** Apply may PASS while Verify is VOID. `restart required` shows in the global status
  and the Apply installation column; a restart alone turns nothing green. **No dismiss button, no forced unload.**
- [ ] **Step 3: The session block.** Show it whenever the selected id is in the actual `Route7.Failed` set (`:94`),
  through the read-only `IsFailed` query and `RetryHint` (`:158`) that **Task 3 already added** — this task only
  displays them, and never touches the mutable set or offers a bypass. Disable dashboard
  Apply and `Run all` while diagnosis and author-output work stay available. The dashboard follows the checkbox's
  suppression (`:130`); explicit console Apply bypasses it and **the dashboard must not use that bypass**. The badge
  clears only when the set actually clears — a new process, or a successful producer operation reaching `:405`.
  Fixing sources, refreshing or passing Validate clears nothing.
- [ ] **Step 4: Package's destination — display only.** The resolver itself landed in **Task 5 Step 2b**, because that
  is where `Run("Package")` first became reachable over RPC. Here the panel only shows what it returned.
  Call `Package.Run` (`:61`) directly into a NEW directory under
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

≤250 lines (script + this file's evidence rows). **Last, and mostly confirmatory** — but not entirely, and the
distinction matters (review's fifth wrong fact). Three things reach their FIRST proof here and are not re-runs of
anything green:

| Deferred to Task 8 | Owning task said so at |
|---|---|
| the read-back extraction is byte- and log-identical | Task 2 Step 6 (`--bake` proves only the sound harness) |
| the main→worker→main split and the main-thread `Install` loop | Task 4 Step 5 |
| the seam, the pump and the `Acceptance` fixtures behaving in a real session | Task 5 Step 5 |

Everything else — the carrier, the formatter, admission, freshness, cancel bookkeeping, the publication faults — IS
already green offline in its own task. **Do not mark the slice done before this task is green.**

**Fixture isolation** (blocker finding 4). W10 runs `All` on `DashboardValid`, so its Apply installs the copies and the
claim SURVIVES for the rest of the process (`BundleLive.Register` `:96` → `BundleClaims.Claim` `:233`, whose `Held.Add`
is `:270`, and a same-mod re-claim deliberately keeps the record `:258`–`:267`). Task 3's R38 then refuses **any**
re-bake of that project's claimed copies until a restart — which is correct behaviour and fatal to every later row that
bakes `DashboardValid`. So the rows split by fixture:

| Fixture | Prepared by | Used by | Ever installed? |
|---|---|---|---|
| `DashboardValid` | `Acceptance('prepare')` | W9, **W10**, W16 | YES, by W10 — kept as W10's evidence and never re-baked afterwards |
| `DashboardPatchFail` | `Acceptance('prepare')` | W11, W14 | no (its bake fails by design) |
| `DashboardResident` | `Acceptance('resident')` | W12, W15 | yes, deliberately resident; W15 runs Verify only |
| **`DashboardAuthor`** (NEW) | `Acceptance('prepare')`, uncontested target | **W13, W18, W19, W20** | **never** — no Apply, no claim, so R38 cannot fire on its bakes |

W16 stays on `DashboardValid` **because it runs `Verify` only** — Verify never rewrites, so R38 is unreachable there,
and it genuinely needs W10's receipt to have something to make stale. W19 joins the moved rows even though the review
listed only W13/W16/W18/W20: it bakes, and the mechanism is identical.

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
  console JSON follow `PPCLI\PLAYBOOK.md:306`, `:319`, screenshot JSON `:323`–`:324`. `D 'Snapshot' @('')` is the
  header poll and `D 'Snapshot' @('<stage>')` fetches one row's verbatim verdict — **the seam is sectioned** because
  `Protocol.Clip` truncates any reply over 2000 chars (`PPCLI\src\Protocol.cs:56`) into JSON that does not parse. Every
  poll after an asynchronous `Run`/`Acceptance` means **bounded polling until `busy=false` AND `runId` still equals the
  id that `Run` returned**; a timeout is a failed row, not a pass. Never edit anything under `PPCLI\`.
- [ ] **Step 3: Run the suite in this order** — `W8 → W9 → W10 → W16 → W11 → W13 → W20 → W19a → W19b → W14 → W12 →
  restart → W15 → W17 → W18`. Rebuild and revalidate after W16 before further success cases. W12/W15 are a pair —
  preserve the fixture. For W18, explicitly reopen and revalidate `DashboardAuthor` if SHIP selected another project.
  **W10 must run against an ENABLED `DashboardValid`**: W9 deliberately uses a disabled fixture to prove disabled
  projects are listed, but Verify's live half needs the mod enabled (design §4.6's activation column), so W10 enables it
  first and says so in its evidence. Every baking row after W10 uses `DashboardAuthor` per the fixture table above.
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
| W9 selector/Validate | `Acceptance('prepare')`; `Open('DashboardValid')`; `Run('Validate')`; `Snapshot`; `Shot 'W9'` | **Validate producer lands in T5b.** Disabled fixture included, exact root bound, S3; eligibility shown as its own field and `Disabled` blocks nothing; prev/next/Refresh share the selection path; duplicate name disambiguated by root | |
| W10 happy chain | enable `DashboardValid` in the mod manager; `Open('DashboardValid')`; `Run('All')`; `Shot 'W10-running'`; `Snapshot`; `Shot 'W10'` | **Validate/Apply/Verify producers land in T5b** — before it, three of these five rows cannot report at all. Clean process, target not resident: five rows PASS, Apply S2, exact producer strings, Package writes a new external path. A resident target makes this W12, not W10. **The fixture is ENABLED** — Verify's live half needs it (W9's disabled-fixture arm is about listing, not verifying). This install is what makes `DashboardValid` un-re-bakeable for the rest of the process; every later baking row uses `DashboardAuthor` | |
| W11 first failure | `Open('DashboardPatchFail')`; `Run('All')`; `Snapshot`; `Shot 'W11'` | **Validate producer lands in T5b** (the chain's first stage). Real bake patch-gate failure. Bake FAIL; Apply/Verify/Package start counts stay zero; prior receipts retained | |
| W12 restart required | `Acceptance('resident')`; `Open('DashboardResident')`; `Run('All')`; `Snapshot`; `Shot 'W12'` | **Apply and Verify producers land in T5b**, and `Acceptance('resident')` with them. A really resident bundle: Apply PASS/S1 with the exact S1 text, Verify VOID/R30, no Package dispatch, no forced unload | |
| W13 cancel | `Open('DashboardAuthor')`; `Acceptance('arm-cancel-bake')`; `Run('Bake')`; **poll** `Snapshot('')` until `barrierArmed=true` AND `barrierRunId`==the `runId` `Run` returned; `Shot 'W13-armed'`; `Cancel`; **terminal poll** until `busy=false`; `Cancel` again; `Snapshot`; `Shot 'W13'` | **`DashboardAuthor`, never `DashboardValid`** — W10 installed the latter and R38 would refuse this bake before it ever reached the barrier. The barrier parks a WORKER, never the main-thread RPC pump, or the poll itself cannot be answered. The first poll observes THIS run parked (timeout = failed row); Cancel is the button's entry point; terminal poll ends R31/VOID with ONE receipt; later start counts zero; busy clears only after acknowledgement AND worker completion; the second Cancel adds nothing; previous outputs byte-identical | |
| W14 Failed block | `C 'ct_route7' @('apply','DashboardPatchFail')`; `Open('DashboardPatchFail')`; `Run('Apply')`; `Snapshot`; `Shot 'W14'` | **Apply producer lands in T5b**, with `LegacyDiskActive`/`WriteOutsideRoots` — R34/R36 are unreachable before it. Console setup really sets `Failed`; dashboard admission R29; Apply and `Run all` disabled, no retry, no clearing; Validate/refresh cannot clear the badge | |
| W15 restart proof | after restart + identity preflight: `Acceptance('enable-resident')`; `Snapshot`; `Open('DashboardResident')`; `Run('Verify')`; `Snapshot`; `Shot 'W15'` | **Verify producer and `Acceptance('enable-resident')` land in T5b.** Real enable callback (`Route7.Toggle`), fresh load-back, the **per-target** claim/path census of S6 (not `Holds`); a partially claimed fixture must produce VOID naming the unserved target; new-session `Failed` clear | |
| W16 stale | `Open('DashboardValid')`; `Acceptance('change-source')`; `Run('Verify')`; `Snapshot`; `Shot 'W16'` | **Verify producer and `Acceptance('change-source')` land in T5b.** **Stays on `DashboardValid` deliberately** — it needs W10's receipt to make stale, and `Verify` never rewrites, so R38 is unreachable here. Receipt becomes stale by an actual key comparison; no old PASS promoted, no automatic Apply. Run after W10 in the same process | |
| W17 SHIP landing | `Acceptance('ship')`; `Snapshot`; `Shot 'W17'` | **Apply producer lands in T5b; `Acceptance('ship')` stays with Task 7**, which is what wires the Doctor handoff. A real successful SHIP opens Lifecycle after GUI dispatch, selects exactly `made.Root`, transfers the same Apply string and disposition, launches no duplicate bake/apply/package | |
| W18 console parity/package | `Open('DashboardAuthor')`; `Run('Validate')`; `Snapshot`; `C 'ct_project' @('DashboardAuthor')`; `Run('Bake')`; `Snapshot`; `Run('Package')`; `Snapshot`; `C 'ct_route7' @('verify','DashboardAuthor')`; `Run('Verify')`; `Snapshot`; `Shot 'W18'` | **Validate and Verify producers, and `ct_route7 verify` itself, land in T5b** — the parity half of this row cannot run before it. **`DashboardAuthor`** — this row bakes twice, which R38 forbids on the installed `DashboardValid`. Bake payload matches for the same unchanged project and key, **and matches the pre-extraction baseline captured at the head of Task 2, bytes and gate log** (this is Task 2's extraction proof, reaching it for the first time). Package matches its producer payload with `ok=true`, writes only a new external directory, previous package intact. **Verify parity: the console verb's terminal line and the dashboard's verdict are the same string character for character**, both out of the one producer; the console call installs and writes nothing; `dryrun/revert/stacktest` still print the unchanged removal text (`Route7.cs:57`–`:63`) | |
| W19a closed run, **worker-only** | `Open('DashboardAuthor')`; `Run('Package')`; close the bench with the chord while it runs; poll `Snapshot('')` until `busy=false`; reopen; `Snapshot`; `Shot 'W19a'` | `Package` is the one stage that is plain `System.IO` end to end (`Package.cs:15`), so it has NO main-thread final segment and can genuinely finish with the window closed. The run completes, receipt and log recorded, reopening SHOWS the terminal result without re-running. `Cancel` reachable while closed | |
| W19b closed run, **main-thread arm** | `Open('DashboardAuthor')`; `Run('Bake')`; close the bench while it runs; `Snapshot('')` **once** — assert `busy=true` and the parked-for-paint state, do NOT terminal-poll; reopen and let it paint; THEN poll until `busy=false`; `Snapshot`; `Shot 'W19b'` | v1's single row deadlocked by construction: it terminal-polled a run whose final phase waits for an open, painted panel, so `busy=false` could never arrive. Split here. Assertions: while closed the row reports it is waiting for a painted panel and nothing is re-run; after reopening it resumes and reaches its terminal result once | |
| W20 competing producer | `Open('DashboardAuthor')`; `Acceptance('arm-cancel-bake')`; `Run('Bake')`; poll until parked; `C 'ct_project' @('DashboardAuthor')`; `Cancel`; poll until `busy=false`; `Snapshot`; `Shot 'W20'` | **`DashboardAuthor`** — an R38 refusal from the installed `DashboardValid` would mask the R37 this row exists to prove. The console verb hits R37 and returns immediately, writing nothing — no second bake, no key stamped over the parked run's copies. After the cancel the claim is released and a plain `C 'ct_project'` succeeds | |

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
| G1 state (carrier arms) | Task 1 (carrier) + Task 3 (freshness/receipts) | 8 + 4 | **13** from Task 1 (`fc1c4a4`) |
| G2 wording (R25–R38, S1–S7, bake special cases, backend passthrough) | Task 1 | 18 | **26** (`fc1c4a4`) |
| G3 Tail (empty, under/at/over the limit, CRLF and LF, trailing newline, one long line) | Task 1 | inside the 26 | **9** (`fc1c4a4`) |
| G4 cancel | Task 4 | 8 | |
| G5 sequence | Task 5 | 14 | |
| G6 admission | Task 3 | 22 | |
| G7 publication faults (**real files, plain `System.IO`, no Unity**) | Task 4 | 16 | |
| G8 Validate (S3, the two fallbacks, eligibility incl. `Disabled`/`NoRoster`, the key) | Task 5b | 6 | |
| G9 Verify verdict + R34/R36 admission (`VerifyVerdict`'s five rules; `LegacyDiskActive`/`WriteOutsideRoots` and their precedence) | Task 5b | 6 + 4 | |
| **Total `LIFECYCLE PASS`** | — | **~108** | |

Unchanged throughout: `PROJECT-SCAFFOLD 89`, `MANIFEST 53`, `ALIAS 32`, `REFUSAL-COUNT 17`, `PACKAGE-GATE 7`,
`MESH extract 57+`, `R0: ALL PASS`, build `Ошибок: 0` / `Предупреждений: 1`. **A changed count in any of those is a
regression to explain, not a number to update.**

## Self-review — PLACEHOLDER

> Filled at the end of Task 8: what the slice actually shipped, which predicted counts were wrong and why, which design
> claims the implementation contradicted, and the `ponytail:` ledger items that survived (design §11).
