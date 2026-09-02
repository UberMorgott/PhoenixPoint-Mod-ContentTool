# Handoff 2026-09-03 — "Replace one mesh" wizard, mid-implementation

Read this first next session, then continue the plan task-by-task. Nothing here needs the user.

## Where we are
- Repo `E:\DEV\PhoenixPoint\ContentTool`, branch `main`, HEAD `13e6361`, tree clean (only the user's untracked `APOCD…` / `Wizard.ApocDesignation` zips — never `git add -A`). NOT pushed since `b79f4d8`; push only on the user's explicit command.
- Manifest core slice: DONE and accepted in-game (`689215f`, see `2026-09-02-manifest-core-plan.md` Acceptance run).
- Wizard slice: spec `2026-09-02-replace-mesh-wizard-design.md` (376 lines, Codex spec check applied), plan `2026-09-02-replace-mesh-wizard-plan.md` (1898 lines, 8 tasks).
  - Task 1 (`ProjectScaffold` name rules + template ppcontent.json + meta.json + RootOf, `tests\ObjCodecTests\ProjectScaffoldTests.cs`) — DONE `13e6361`, Codex `-Review` clean; cavecrew review result: see "Pending results" below.
  - Tasks 2–8 — NOT started: 2 row append/reuse/conflict, 3 GLB copy + sidecar (pre-write SHA), 4 `Route7.ApplyDisposition` + `BundleLive.ResidentNow`, 5 `ShippedTarget.Resolve` (addon graph → dependency bundles → `BundleBaker.WhyNot` exactly-one, R14–R22), 6 Doctor SHIP section + `Intent.Ship` + two-frame gate + `RigTarget.SameRigAs`, 7 offline gates + W-rows, 8 in-game acceptance on `D:\PP-Instance3` via PPCLI (incl. restart + enable in MOD_ACTIVATED on the Instance3 profile, never the user's game).
- Gate counts at HEAD: `PROJECT-SCAFFOLD PASS, 29 check(s)`, `MANIFEST PASS, 53`, `ALIAS PASS, 32`, `REFUSAL-COUNT PASS, 16`, TargetPathTests `R0: ALL PASS`, build `Ошибок: 0` (warnings 4: the known CS0649 + 3 transient CS0649 on `ProjectScaffold.Result` fields Tasks 2–3 assign).

## Pending results (read these before dispatching Task 2)
- Codex DEEP review of the plan (relaunched with `-TimeoutSec 1800` after a 900 s timeout): output `C:\Temp\cx\ec5951d509df433a980182e8a0b1d624.out.md`, status `C:\Temp\cx\ec5951d509df433a980182e8a0b1d624.status.txt`. If the status is `TIMEOUT` again, relaunch with the prompt `C:\Temp\claude\E--DEV-PhoenixPoint-ContentTool\f9375261-422d-40bc-ac5c-ccfc64676d2f\scratchpad\wizard-plan-review-prompt.md` (it is self-contained) or split it: one run for Tasks 1–4, one for 5–8. Digest via scout, apply accepted findings to spec+plan with one opus agent, commit, then continue.
- cavecrew review of `13e6361` (Task 1) — if it reported findings, they are appended at the bottom of this file; otherwise treat Task 1 as closed.

## Process (unchanged, from memory `codex-in-the-loop` + the user's rules)
- Per task: one fresh opus implementer (reads ONLY its task from the plan; disk wins over plan; TDD red→green; commit by path) → `cx -Review E:\DEV\PhoenixPoint\ContentTool -Commit <sha> -TimeoutSec 600` (background) + `caveman:cavecrew-reviewer` in parallel → triage (ponytail: reject pedantry, fix real defects with a fix agent, re-review) → next task. Never two implementers at once (shared `obj\`, shared test files).
- Final: `cx -Review -Base 13e6361` for the whole wizard slice; memory `model-doctor-roadmap` updated; then next slice = lifecycle dashboard (Validate/Bake/Apply/Verify/Package, progress, cancel) — brainstorm with Codex (`cx -PromptFile … -Deep`), never ask the user about design/layout.
- Instance3 traps: launch `D:\PP-Instance3\PhoenixPointWin64.exe -mods` by hand (never `run`/`batch`), deploy first with `deploy.ps1 -PPRoot 'D:\PP-Instance3'`, gate on `connect state -PPRoot 'D:\PP-Instance3' -ProfileId 76561197996210593`, read `failed`, `op:"set"` for primitives, PPCLI defects → `PPCLI\ISSUES.md` + SendMessage the open `ppcli-*` session. Instance3 was driven into a mission by ANOTHER session on 2026-09-02 — check `connect state` before assuming the bench is reachable; a process may still be up (pid 37268 then). Instance2 = Renderforge session, untouchable.
- Open user-owed items (do not chase): visual check of camera/overlay/Esc/zip-model; `FitBench.ShowPrototype` on the tool's own mod creature gives no `OnCharacterRebuilded` (SlotTargets empty) — shipped prototypes fine.

## Scratchpad (session-specific, may vanish)
- Facts: `…\scratchpad\wizard-facts.md`, design prompt `wizard-design-prompt.md`, Codex design `C:\Temp\cx\e4203269fa524780b07d19694674874b.out.md`, Codex spec check `C:\Temp\cx\53fa61095ec3431bbb9cd679da89e818.out.md`.
