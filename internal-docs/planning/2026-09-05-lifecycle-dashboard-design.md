# Lifecycle dashboard — design memo, part B/2

- Scope: panel, wording, acceptance, task split. Part A owns stage execution, freshness, progress and cancellation mechanics.
- Fixed stage order: Validate → Bake → Apply → Verify → Package. One selected ContentMods project; native IMGUI; existing producers own verdicts.
- Design only. Commands below are implementation acceptance requirements, not evidence of executed tests.

## B1. Panel decision

- Add a third FitBench tab, `Lifecycle`. Doctor remains the mesh-authoring workflow; its successful SHIP result selects the produced project and opens Lifecycle without automatically running another stage.
- Selector: project name with previous/next buttons and `Refresh`; enumerate eligible ContentMods project directories using the existing project loader. Keep the selected name across refresh if it still exists. Empty selection stays empty; never silently switch the target after deletion. No arbitrary path text box.
- Always draw selector, global status, five stage rows, progress, Run all/Cancel controls and log tail. Draw placeholders and disabled controls before any result exists. Result text may change; the layout's control sequence must not.
- Each stage has separate freshness (`fresh`, `stale`, `never`), outcome (`PASS`, `FAIL`, `VOID`, or `—`), action button and one authoritative verdict line. An old PASS can be stale. An unattempted stage is `never / —`, not PASS or FAIL.
- `Run all` runs in displayed order; stop on the first FAIL, refused stage, acknowledged cancellation or restart barrier. Do not run later rows, erase earlier evidence or silently package after a Verify barrier. Standalone Package follows its producer's prerequisites; a ZIP is not proof of live installation.
- Disable project selection, Refresh, stage Run buttons, Run all and competing SHIP/source-edit actions while a run owns the job. Disable FitBench tab changes during that ownership. Cancel stays enabled only until requested or until a noninterruptible main-thread stage begins. Window close does not release ownership or imply cancellation.
- Apply/Verify use the inherited two-frame gate. While queued, Cancel can prevent entry. Once synchronous work starts, show `Cancel unavailable during Apply/Verify`; do not promise an IMGUI click can interrupt a blocked main thread.
- Keep one fixed-height tail under the rows, containing only this run's captured stage log. Retain the last completed run until the next run starts. A per-row verdict never depends on whatever line currently ends the tail.

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

- Existing anchors: FitBench tab switch and ShipPending guard, `src/Dev/FitBench.cs:1672`; fixed SHIP fields and tail, `src/Dev/ModelDoctor.cs:1520`; enqueue and Repaint acknowledgement, `src/Dev/ModelDoctor.cs:1557` and `:1572`; Layout intent drain and worker snapshots, `src/Dev/SlimPanel.cs:102`.
- Reuse `ModelDoctor.Message`'s status-field pattern (`src/Dev/ModelDoctor.cs:71`) and its Tail semantics (`:745`–`:754`); expose/move the existing helper only as needed for shared callers/offline tests. Progress uses the existing `GUILayout.Box("", GUILayout.Width(Mathf.Max(1f, 240f * done)), GUILayout.Height(6f))` pattern (`src/Dev/SlimPanel.cs:270`) inside a fixed track. Unknown progress shows the phase and the minimum fill, never an invented percentage.
- SHIP handoff uses the captured absolute `made.Root`, not a name reconstructed from a label (`src/Dev/ModelDoctor.cs:656`, `:695`). Transfer its authoritative Apply result; leave unobserved stages `never`. Change tabs only after SHIP releases ownership and the current GUI event finishes.
- Do not use `ContentMods.Enabled` or `LiveProjectIds` as the selector inventory: they omit author projects (`src/Project/ContentMods.cs:41`; `src/ContentToolMain.cs:82`). Enumerate sibling Mods directories, ContentTool's own child directories and roster roots; retain roots with `ppcontent.json`, canonicalize and deduplicate. Existing candidates/resolve rules: `src/Project/ContentMods.cs:116`–`:154`; marker `:25`. Include disabled projects. Bind selection to canonical root, show root on duplicate names and pass the selected absolute root to Apply. Use `ContentProject.LoadDeclared`, not source-importing `Load`, during refresh (`src/Project/ContentProject.cs:289`, `:305`). Perform enumeration/validation outside OnGUI drawing.
- S1: Apply may PASS while Verify is VOID. Keep `restart required` in the global status and Apply installation column. Restart alone does not turn a row green: re-observe the selected project and run Verify. Never clear the barrier with a dismiss button or forced bundle unload. Residency is sampled before install (`src/Bake/Route7.cs:397`); `Resident` wins over redirection (`:410`–`:413`).
- `Route7.Failed`: show `session block` whenever the selected ID belongs to the actual set (`src/Bake/Route7.cs:94`). Disable dashboard Apply and Run all; allow diagnosis and author-output work subject to normal path guards. No reset button and no direct set mutation. The current checkbox suppresses retry (`:129`); explicit console Apply bypasses that suppression. The dashboard must not use that bypass. The badge clears only when the set actually clears: a new process, or a successful external producer operation reaching its existing removal (`:405`). Merely fixing source files, refreshing or passing Validate does not clear it.
- Player-installation writes must remain within the existing checkbox's Route7 path and guards. No dashboard copy, delete, forced unload, installation-root picker or separate mod-manager setting write. Match both the checkbox's LegacyDisk guard (`src/Bake/Route7.cs:125`–`:127`) and Failed suppression before invoking ApplyProject. The overload `ApplyProject(string, string, out ApplyDisposition)` already exists (`:269`); outcomes are `Redirected`, `Resident`, `Refused`, `BakeFailed` (`:252`).
- Package calls `Package.Run` directly. Use a new output directory under `%LOCALAPPDATA%\ContentTool\Packages\<project>\<run-id>` outside the game installation, and display the returned path. Never call the destructive console wrapper: it recursively deletes the previous destination (`src/ContentToolMain.cs:511`); `Package.Run` itself refuses a nonempty destination (`src/Project/Package.cs:78`). A repeated run gets a new directory; no overwrite control.

## B2. Verdict and refusal contract

- Outcome and freshness are structured fields supplied by Part A. UI stores and displays the producer's final string verbatim; it does not infer success from log contents, file existence, an empty exception field or a green previous row.
- Existing strings below are message payloads; logging timestamps/prefixes are not verdict text. Braces denote substitution slots, never literal braces. Preserve punctuation/case. New lines are explicitly marked NEW and belong to the same result formatter used by dashboard actions and their callable/console execution path.
- Forward all existing producer refusals unchanged; this table adds dashboard-specific guards after wizard R24 (`internal-docs/planning/2026-09-02-replace-mesh-wizard-design.md:346`). Do not reimplement the wizard R1–R24 checks.

| ID | Condition / outcome | Exact message payload | Authority |
|---|---|---|---|
| R25 | No project selected; VOID | `Lifecycle: select a ContentMods project.` | NEW; admission guard |
| R26 | Another run owns the job; VOID | `Lifecycle: busy running {stage}.` | NEW; admission guard; current job/result untouched |
| R27 | Selected root vanished or no longer resolves to the selected project; VOID | `Lifecycle: selected project is unavailable; refresh the project list.` | NEW; admission guard |
| R28 | Required evidence is missing/stale; VOID | `Lifecycle: {stage} blocked; {prerequisite} is {freshness}.` | NEW; Part A supplies the prerequisite; no independent UI dependency graph |
| R29 | Selected ID in Route7.Failed; VOID | `'{id}' failed to bake earlier in this session - not baking it again. Fix the lines it printed, then {RetryHint}` | Existing checkbox text, `src/Bake/Route7.cs:129`–`:132`; reuse its RetryHint (`:158`–`:168`), including the reachable-name console retry hint; dashboard offers no bypass button |
| R30 | Verify cannot establish live routing because Apply is S1; VOID | `Verify: VOID - restart required for '{name}'.` | NEW; barrier, not failure |
| R31 | Cancellation acknowledged before successful publication; VOID | `Lifecycle: {stage} cancelled; later stages were not run.` | NEW; shared cancellation result; never claims rollback |
| R32 | Project inputs changed during the run; VOID | `Lifecycle: project changed during {stage}; validate again.` | NEW; freshness/commit guard owned by Part A |
| R33 | Unsupported callable stage token; VOID | `Lifecycle: unknown stage '{stage}'.` | NEW; accepted tokens are exactly `Validate`, `Bake`, `Apply`, `Verify`, `Package`, `All` |
| R34 | Game-root or destination admission guard fails; VOID | `Lifecycle: refused a write outside the mod-manager apply path or author output.` | NEW; fail before any write |
| R35 | Actual patch failure; FAIL | `NOT APPLIED: patching the shipped bundle(s) reported {n} failure(s), named in the P0/REFUSED line(s) above; nothing was installed and no copy was marked current.` | Existing `src/Bake/Route7.cs:349`–`:351`; display producer-returned line |
| R36 | Checkbox LegacyDisk guard denies Apply; VOID | `Lifecycle: Apply blocked while legacy disk patching is active.` | NEW dashboard line for existing guard, `src/Bake/Route7.cs:125`–`:127`; no migration/repair button |
| S1 | Apply PASS / Resident | `applied - restart the game and enable '{name}' in the mod manager. Phoenix Point already loaded {bundle}.` | Existing `src/Dev/ModelDoctor.cs:710`–`:712`; append exactly ` This session keeps showing your Doctor preview.` iff the originating Doctor has HasPreview |
| S2 | Apply PASS / Redirected | `applied and redirected LIVE - {bundle} now loads from the patched copy on the next load` | Existing `src/Dev/ModelDoctor.cs:714` |
| S3 | Validate PASS | `Validate: PASS - '{name}'.` | NEW; only after PatchCache.Key and Manifest.Validate complete; Validate has no existing success string (`src/Project/Manifest.cs:200`) |
| S4 | Bake PASS | `ct_project: ALL PASS - {outPath}` | Existing `src/Bake/ProjectBake.cs:404`; preserve producer special-case outcomes instead of fabricating this line |
| S5 | Bake FAIL | `ct_project: {n} FAILURE(S)` | Existing `src/Bake/ProjectBake.cs:406` |
| S6 | Verify PASS | `Verify: PASS - load-back gates passed; live claim held for '{name}'.` | NEW; only the selected project's Part A gates plus BundleLive.Holds; not visual correctness |
| S7 | Package PASS | `PACKAGED {n} file(s), {bytes} B into {outDir}` | Existing `src/Project/Package.cs:180`; requires `out ok == true` |

- A backend failure with its own string keeps that string. If Validate/Verify throws without a producer verdict, shared fallback: `Validate: FAIL - {reason}` or `Verify: FAIL - {reason}`. The tail retains details; the verdict uses the same single-line reason in every caller.
- Transient Message strings, not terminal verdicts: `Queued: {stage}`, `Running: {stage}`, `Cancel requested; waiting for {stage} to stop.`, `Cancel unavailable during {stage}.` After publication has already succeeded, preserve the stage's PASS; cancel only the continuation and show `Lifecycle: cancelled after {stage}; later stages were not run.`
- Idle row verdict placeholder: `—`. Global ready placeholder: `Ready.`. Failed/restart badges are independent of the last Message and cannot be hidden by a later successful Package.
- Do not use `ct_catalog: PASS - the game's own Addressables served the mod's own bundle, and nothing was written to the installation` as selected-project Verify. `CatalogVerify` checks all published keys (`src/Bake/Route7.cs:510`–`:525`), so that string proves a different scope.
- The shipped design's old S1/S2 wording (`baked OK`, `baked and redirected LIVE`, wizard design `:347`–`:348`) is stale. Use current Doctor strings, with the shared formatter extracted there; do not copy those old design strings into the panel.
- Preserve Bake special terminal strings from `src/Bake/ProjectBake.cs:125`–`:135`: `nothing to bake - put .png/.jpg under Content\Textures\, .glb under Content\Models\ or .wav under Content\Audio\`; `ct_project: ALL PASS - this project has no bundle of its own; the patched copy(ies) above are the whole output`; `ct_project: ALL PASS - nothing needed patching: none of this project's {n} replacement(s) names a shipped bundle, so no copy was written - the video row(s) above are served live by ct_video`. Their outcome comes from the producer; wording is not parsed to classify them.
- Preserve Package refusal exactly: `REFUSED: {outDir} already holds files. Name a folder that does not exist yet - a package is built from nothing, so no leftover of a previous run can be shipped by accident.` (`src/Project/Package.cs:78`–`:80`).

## B3. Offline acceptance

- Extend the existing `tests/ObjCodecTests` executable; no Unity player, mock UI framework or new test dependency. Link the minimal pure state/result/Tail code exactly as the harness links other pure source. State transition inputs are values; no filesystem or Unity probing inside the pure reducer.
- Run: `dotnet run --project tests\ObjCodecTests -c Release`. Require exit 0, the current `PROJECT-SCAFFOLD PASS, ` marker and a new `LIFECYCLE PASS` marker. Wizard design `:426` abbreviates the old marker; current authority is `tests/ObjCodecTests/ProjectScaffoldTests.cs:770`. The harness targets net472 and explicitly links source (`ObjCodecTests.csproj:4`, `:40`); register a new `Run()` alongside the unconditional scaffold gate (`Program.cs:142`). Do not invent a test-name CLI filter; only the existing `--bake`/`--u9probe` modes are present (`Program.cs:60`–`:63`).

| Gate | Arm / assertions |
|---|---|
| G1 state | Table inputs for missing receipt, same key, changed key, previous FAIL, S1, S2, Failed membership. Assert exact freshness/outcome/badges; stale PASS stays stale PASS; never is not VOID. |
| G2 wording | Golden expectations for R25–R36 and S1–S7, including variable substitution, backend passthrough and special bake outcomes. Compare Doctor/shared producer/dashboard string identity; no independently reconstructed UI sentence. |
| G3 Tail | Empty, fewer/exactly/more than limit, CRLF/LF, trailing newline and a long line. Freeze the reused helper's current semantics; constant output slot count is a panel rule, not a fabricated Tail behavior. |
| G4 cancel | Cancel before dispatch; while worker active at a cooperative boundary; after success publication; repeated Cancel. Assert one terminal result, busy retained until worker completion, no next-stage dispatch, no false rollback and no late result overwriting a newer run. |
| G5 sequence | All succeeds; each stage fails in turn; S1 barrier; prerequisite refusal; cancellation. Assert invocation order/count, first stop position and unchanged earlier receipts. Package is not entered after Run all stops at Verify. |
| G6 admission | Missing/deleted/duplicate project names; changed key before commit; Failed suppression; nonempty package output; attempts to route a write outside allowed roots. Assert rejection before the write callback is entered. |

## B4. In-game acceptance and callable seam

- Run only against `D:\PP-Instance2`. `connect state` proves readiness, not installation identity (`E:/DEV/PhoenixPoint/PPCLI/src/PPBridgeMain.cs:205`–`:212`). PPCLI validates endpoint PID/executable and filters `.install` to `-PPRoot` (`E:/DEV/PhoenixPoint/PPCLI/ppcli.ps1:212`–`:221`, `:249`–`:259`); additionally require the seam's in-process canonical game root to equal the test root. A mismatch fails closed before fixture creation. Commands below are future acceptance requirements, not executed evidence.
- Task 4 owns the public static seam on the dashboard class: `Open(string projectName)`, `Run(string stage)`, `Cancel()`, `Snapshot()`. Open-empty clears selection explicitly; do not pass it to the existing name resolver, whose empty-name default is Sample (`src/Project/ContentMods.cs:153`–`:154`). Resolve a unique name to a canonical root before LoadDeclared; reject ambiguous names. LoadDeclared takes a root containing ppcontent.json, not a project name (`src/Project/ContentProject.cs:289`–`:296`). Run enqueues the same intent as the button; it returns promptly and never performs synchronous Apply from the RPC call. The ordinary update pump owns dispatch even when the window is closed.
- Snapshot returns canonical game root, selected canonical root/ID, run ID, busy/current stage, cancel requested/acknowledged, five freshness/outcome/verdict entries, S1/S2, actual Failed membership and stage-start counts. It is observational: querying it cannot validate, apply or clear state. Tests compare its verdicts to captured producer payloads; screenshots prove panel shape.
- Task 7 owns test-only `Acceptance(string scenario)` on that same class. It refuses any game root except the test instance. Scenarios prepare isolated named fixtures and arm narrow gates; they call the public seam and the real producers. They must never install a fabricated PASS/FAIL snapshot.
- Deterministic cancel scenario: arm a worker barrier at the first supported cancellation boundary, start Bake through Run, then request Cancel through the same method once that boundary is observed. Cancel releases the barrier; normal worker completion publishes VOID. No sleep-based race, Thread.Abort, synthetic success or detached worker. An additional pure G4 case covers cancellation after publication.
- W8 onward continues the wizard's W1–W7 baseline; retain W5 failed-bake isolation, W6 restart/enable proof and W7 owner visual inspection (`wizard design:431`–`:433`). New screenshots use an enabled upscaler as required by the owner; no rendering-setting workaround is part of this slice.

Exact PowerShell command vocabulary (future class namespace follows `src/Dev/ModelDoctor.cs:13` and `src/Dev/FitBench.cs:28`):

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

- RPC/console JSON is grounded in `E:/DEV/PhoenixPoint/PPCLI/PLAYBOOK.md:306`, `:319`; screenshot JSON in `:323`–`:324`. For each asynchronous action below, repeat the exact call `D 'Snapshot'` until that run has `busy=false`, with a bounded timeout; timeout is a failed acceptance row, not a success. Every table entry `D 'Snapshot'` after an asynchronous Run/Acceptance denotes this bounded polling step; finish it before the next call. Capture queued/running screenshots before waiting where specified. Fixture-only Acceptance calls return after preparation; any asynchronous scenario exposes busy until completion.
- Fixture scenarios are NEW, narrow test implementations: `prepare` creates only dedicated `DashboardValid` and `DashboardPatchFail` author fixtures; `resident` prepares `DashboardResident` and actually loads its target bundle; `cancel-bake` arms the worker barrier; `change-source` changes only the selected dedicated fixture; `ship` drives the actual Doctor fixture through its existing selection and Enqueue path. `enable-resident` invokes the actual mod-manager enable callback after restart. Never set Failed, residency, Holds or verdict fields directly to manufacture a scenario.

| Row | Exact calls, in order | Required evidence |
|---|---|---|
| W8 empty | `D 'Open' @('')`; `D 'Run' @('Validate')`; `D 'Snapshot'`; `Shot 'W8'` | Open-empty is allowed; Run yields R25. Five `never / —` rows, placeholders, disabled unavailable actions; no layout exception. |
| W9 selector/Validate | `D 'Acceptance' @('prepare')`; `D 'Open' @('DashboardValid')`; `D 'Run' @('Validate')`; `D 'Snapshot'`; `Shot 'W9'` | Includes the disabled fixture, binds exact root, S3. Previous/next/Refresh use the same selection path; duplicate-name fixture is disambiguated by root. |
| W10 happy chain | `D 'Open' @('DashboardValid')`; `D 'Run' @('All')`; `Shot 'W10-running'`; `D 'Snapshot'`; `Shot 'W10'` | In a clean process with the chosen target not resident: five rows PASS, Apply S2, exact producer strings, Package new external path. If target is already resident, this run exercises W12 instead; it cannot count as W10. |
| W11 first failure | `D 'Open' @('DashboardPatchFail')`; `D 'Run' @('All')`; `D 'Snapshot'`; `Shot 'W11'` | Fixture passes manifest validation but causes a real bake patch gate failure. Bake FAIL; Apply/Verify/Package start counts remain zero for this run; prior receipts retained. |
| W12 restart required | `D 'Acceptance' @('resident')`; `D 'Open' @('DashboardResident')`; `D 'Run' @('All')`; `D 'Snapshot'`; `Shot 'W12'` | Actual resident bundle: Apply PASS/S1, exact S1 text, Verify VOID/R30; no Package dispatch, no forced unload. |
| W13 cancel | `D 'Open' @('DashboardValid')`; `D 'Acceptance' @('cancel-bake')`; `D 'Snapshot'`; `Shot 'W13'` | Barrier observes active worker before invoking the same Cancel used by UI; R31/VOID, one terminal receipt, later start counts zero, busy clears only after acknowledgement and worker completion. Call `D 'Cancel'` again: no duplicate result. |
| W14 Failed block | `C 'ct_route7' @('apply','DashboardPatchFail')`; `D 'Open' @('DashboardPatchFail')`; `D 'Run' @('Apply')`; `D 'Snapshot'`; `Shot 'W14'` | Console setup really sets Failed through patch failure. Dashboard admission R29; Apply/Run all disabled, no direct retry or set clearing. Validate/refresh cannot clear badge. |
| W15 restart proof | After a normal game restart: readiness/identity preflight above; `D 'Acceptance' @('enable-resident')`; `D 'Snapshot'`; `D 'Open' @('DashboardResident')`; `D 'Run' @('Verify')`; `D 'Snapshot'`; `Shot 'W15'` | Actual enable callback, fresh load-back and Holds evidence; S6 only if all pass. New-session Failed state observed clear. Missing fresh evidence yields refusal, never inherited green rows; normal Validate/Bake may be run first if Part A cannot re-derive their receipts. |
| W16 stale | `D 'Open' @('DashboardValid')`; `D 'Acceptance' @('change-source')`; `D 'Run' @('Verify')`; `D 'Snapshot'`; `Shot 'W16'` | Existing receipt becomes stale; Verify blocked by actual key comparison. No old PASS promoted to fresh; no automatic Apply. Run after W10 in the same process. |
| W17 SHIP landing | `D 'Acceptance' @('ship')`; `D 'Snapshot'`; `Shot 'W17'` | Actual successful Doctor SHIP opens Lifecycle after GUI dispatch, selects exactly made.Root, transfers the same Apply string/disposition, launches no duplicate bake/apply/package. |
| W18 console parity/package | `D 'Open' @('DashboardValid')`; `D 'Run' @('Validate')`; `D 'Snapshot'`; `C 'ct_project' @('DashboardValid')`; `D 'Run' @('Bake')`; `D 'Snapshot'`; `D 'Run' @('Package')`; `D 'Snapshot'`; `Shot 'W18'` | Match final bake payload for the same unchanged project/key. Package matches its captured producer payload with ok=true and writes only a new external directory; previous package remains intact. |

- Suite order: W8 → W9 → W10 → W16 → W11 → W13 → W14 → W12 → restart → W15 → W17 → W18. Rebuild/revalidate after W16 before further success cases. W12/W15 form a pair; preserve its fixture. For W18 explicitly reopen/revalidate `DashboardValid` if SHIP selected another project.
- A screenshot response with `ok=true` is not panel proof. Inspect the PNG for all five rows, status text, progress/tail and disabled controls. Screenshot response fields are documented by `E:/DEV/PhoenixPoint/PPCLI/src/Screenshot.cs:162`; the targetTexture branch may also write a separate `.scene.png` (`:169`–`:175`). Use the image that actually contains IMGUI. Visual mesh acceptance remains the existing W7.

## B5. Eight-task execution split

| Order | Ownership / limit of changed lines | Finish condition / ordering reason |
|---|---|---|
| 1 | Part A: producer result plumbing and shared exact verdict formatting; ≤250 lines | Doctor, console and returned result agree on existing strings. First: every later task depends on one truth. |
| 2 | Part A + selector boundary: pure row-state derivation, canonical project selection and admission; ≤250 lines | G1/G6 pass. No UI IO or second dependency model. Requires task 1 outcomes. |
| 3 | Part A: SlimJob-compatible worker progress and cancellation bookkeeping; ≤300 lines | G4 passes; cancellation cannot release busy early. Depends on stable result/state contracts. |
| 4 | Part A/B integration: Run all coordinator, main-thread two-frame gate and public callable seam; ≤300 lines | G5 passes; RPC and buttons enqueue one path. Owns Open/Run/Cancel/Snapshot and persistent dispatch pump. |
| 5 | B: Lifecycle drawing and FitBench third tab; ≤280 lines | Five rows, placeholders, fixed tail/progress and controls draw in every state. Build plus available seam/panel smoke checks; full W8/W9/W10 run in task 8 after task 7 fixtures. Depends on task 4 snapshots. |
| 6 | B: Doctor SHIP handoff, S1/Failed badges, checkbox-equivalent admission and safe Package output; ≤250 lines | Build plus available admission/handoff smoke checks use real result/root; full W11/W12/W14/W17 run in task 8 after task 7 fixtures. Requires panel and existing producer guards; no install-copy helper. |
| 7 | B: offline golden cases, deterministic acceptance fixtures/barrier and root guard; ≤300 lines | G1–G6 pass; fixture hooks are test-instance-only and bounded. Several green commits allowed. |
| 8 | B: PPCLI acceptance script, screenshots/log receipts and final memo corrections; ≤250 lines | Run W8–W18, preserve exact commands/results; distinguish PASS from unverified visual work. Last because it exercises the finished path. |

- Limits are per task's implementation diff, not permission to fill 300 lines. Split green commits within a task; do not create factories/interfaces to meet a file-size target. Reuse existing helper ownership; a small pure file is justified only to keep the existing offline harness Unity-free.
- Integrate in order on `main`, committing each verified logical change; no push without owner request. The present task writes this design half only. Part A owns runtime mechanisms; Part B must not implement competing state/progress/cancel machinery.

## B6. Risks and defaults

- Missing facts input: supplied `C:\Temp\claude\E--DEV-PhoenixPoint-ContentTool\a95e51d3-29cc-469d-9b3a-bfcccbe86333\scratchpad\dashboard-facts.md` was absent. Default: cite verified source for decisions; do not invent facts-file claims or repeat a full lifecycle audit.
- Stale design strings: current Doctor source wins. Default: preserve exact current S1/S2; update documentation through the same verdict tests.
- Shared Failed policy differs from explicit console Apply. Default: dashboard follows checkbox suppression; no force retry/reset. Successful Bake alone never clears the badge.
- Main-thread Apply/Verify cannot repaint or receive clicks while blocked. Default: honest queued/running state and cancellation before entry; no fake timed percent or interrupt promise.
- Multiple ContentMods roots and duplicate IDs: default to canonical-root identity; show root on collision and preserve SHIP's absolute root. Refuse unresolved ambiguity instead of applying whichever name resolves first.
- Verify evidence can be narrower than visual mesh correctness. Default: S6 promises only its load-back gates and held claim; keep the owner's W7 visual proof separate and unclaimed until observed.
- Inputs can change outside the panel. Default: Part A key check before publication and before every next stage; R32 stops the chain. No watcher or persistent receipt database in this slice.
- Package wrapper can destroy old output. Default: direct Package.Run into a fresh external directory, never wrapper cleanup. Cancel retains successfully published outputs; no speculative rollback delete.
- Restart invalidates session observations. Default: re-derive rows on startup, retain no in-memory PASS as current evidence, and require enable/Verify before claiming S2/live verification.
- First-half integration names may differ. Default: keep the seam/wording contract here; adapt internal field names once, with task 1 golden tests. No additional owner questions.
