# Full review + fix pass — handoff (2026-09-26)

Owner asked: review ContentTool together with Codex, finish what is possible WITHOUT launching the game, fix the user wiki (docs\). Owner will reboot; next session resumes here.

## State at handoff
- Code phase DONE: 79 commits `eb0ffc1..main` (local, NOT pushed). Build + tests\ObjCodecTests + tests\TargetPathTests green, `qgate -All` exit 0.
- Reviews: Codex review #1 (`codex-review-1.md`), 10 Claude reviewers, Claude cross-review of merged fixes (no HIGH left). All findings + status: `REVIEW-TASKS.md` (checkboxes in code sections were not ticked individually — the "[x] <track> merged" lines + track reports are the truth; items marked deferred below are the only open code items).
- Tracks merged: A Import, B Project/Package/Wwise, C1 Lifecycle/Route7/ProjectBake, C2 Bake fields/video, D Tactical + bench, E dev gate (`ct-dev` marker), F ct_extract freeze, G follow-ups.
- `Wizard.ApocDesignation\` + `.zip` moved from repo root to `local\` (owner: "решай сам").
- Codex is DOWN: every `cx` call -> `401 Unauthorized: Incorrect API key provided: sk-svcac...fvMA` (also with `codex --no-daemon`). Owner must `codex logout` + `codex login`. Codex review #2 of the merged diff never ran.

## NEXT (in order)
1. Probe Codex: `cx "Reply with the single word OK." -NoSession`. If OK -> Codex review #2 of `git diff eb0ffc1..main -- src tests autogate.ps1 deploy.ps1` (prompt template in this folder's history: regressions, earlier-findings status, risk spots DevGate/CatalogOwners/UnsafeName/Package parse/GlbReader fold/ServesCurrent/cancel/CreatureManifest). Fix findings, commit.
2. DOCS: rewrite wiki pages from the four fact sheets (`facts-start-ref.md` 65 items, `facts-recipes.md` 106, `facts-bench.md` 53, `facts-examples.md` 31) PLUS track G user-visible changes (in REVIEW-TASKS.md is NOT yet — list below). Prose written by Codex via cx (owner rule: docs prose = Codex; Claude gathers facts + checks accuracy). Then Claude accuracy pass vs code, `mkdocs build --strict`, commit.
   Track G user-visible changes (not in fact sheets): meta.json may have comments + trailing commas; package `WARN: <src> is newer than its bank ... no sources.ledger entry ...` (date fallback warns, ledger mismatch refuses); `ct_voices` + `ct_mission list` public, `ct_mission gate` unarmed -> "'ct_mission gate' is a dev command - REFUSED, no 'ct-dev' marker beside ContentTool.dll (...)"; dev command count 14; unticking a mod removes its video even if its DLL called anonymous Register; id/bundle CON/NUL/COM1.. refused ("is a Windows reserved device name"); broken ppcontent.json -> "ct_project: <root> could not be read - ppcontent.json is not valid JSON: ... - fix ppcontent.json and run it again." + "ct_project: 1 FAILURE(S)"; video stem collision SOURCE SKIPPED; bad `ct_extract video/audio` name -> REFUSED with 'ct_list videos'/'ct_list audio' hint; rest-pose-only helper-node curve imports; Lifecycle log keeps stage output above stop line; `ct_bench rescan` in description. Track F: `ct_extract audio --all` runs in background ("...decoding N matched loose media into <dir> in the background..."), second run REFUSED; bad tex/mesh name REFUSED.
3. Push main + GitHub release (standing order, memory `contenttool-push-and-release`): bump meta.json + csproj (1.2.1 -> 1.3.0: new behavior + dev gate), tag, build, zip w/o pdb, `gh release create`. Workshop stays off.
4. Update `internal-docs\planning\2026-09-03-handoff-replace-mesh-wizard.md` top block (stale "1.2.1 pending") to point here.
5. Cleanup: `git worktree list` -> remove `.claude\worktrees\agent-*` (all merged; branches `worktree-agent-*` can be deleted).

## Deferred — needs in-game (Instance2 via PPCLI)
- Tangents missing on replaced meshes (MeshFields/MeshBuild stride 32 -> normal maps lost) — biggest visual item.
- Emission colour: game is Linear colour space (globalgamemanagers m_ActiveColorSpace=1, Unity 2019.4.31f1); `_EmissionColor` written raw linear — likely too bright; verify then sRGB-encode w/o clamp.
- CreatureBuild SynthSkin with bashPoint vs tactical save load ("Serializing destroyed unity object").
- AlsoAccept mutates shipped donor anim action defs (clone-on-write).
- Runtime loader for added audio: added bank plays only if the mod's own DLL loads it; streamed `.stream.*` sounds never ship (WwiseAudio not packaged).
- ModRoster Harmony prefix publishes before DLL load; rollback on failed OnModEnabled.
- Bench un-quiesce autorefresh during rebuild window; weapon BuildAll missing (weapon mod w/o C# mints nothing).
- Everything tagged "needs in-game" in track reports: R29/R38/re-apply "kept", CatalogKeys release, video ownership, dev gate under autogate, ct_extract background run, LoadBankKeep, creature/weapon build atomicity, bench open/close.
- Non-ASCII bone-name hash encoding unproven.
