<!-- quality-gate -->
## Completion gate (mandatory)

After changing files, run `qgate` from the repository root. Use `qgate -All` when
dependencies, build configuration, generated files or several stacks changed.

Exit code 0 means done. Anything else means NOT done: the output names the exact
failures -- fix them and run it again. Do not report completion while the gate is
failing, and never edit or disable the gate to make it pass. Include the command
you ran and its pass/fail result in your final response.

If `qgate` is unavailable, report that as a blocker, do not skip it. It installs with
`irm https://raw.githubusercontent.com/UberMorgott/quality-gate/main/bootstrap.ps1 | iex`

If the gate itself is wrong -- it crashes, blames code that is provably correct,
misses a whole stack, or cannot be satisfied at all -- do not work around it and do
not disable it. Open an issue against the gate and say so in your final response:

```powershell
qgate where   # install path + commit, paste this into the issue
gh issue create --repo UberMorgott/quality-gate --title "<what broke>" --body "<qgate output, the command you ran, the file it blamed, `qgate where` output>"
```
<!-- /quality-gate -->
# ContentTool — project rules

## PPCLI is a SEPARATE project — never mix the two (hard rule)

`ContentTool\` and `PPCLI\` = two separate projects, two folders, two git repos. Never cross:

- **Never commit PPCLI change from ContentTool session, never reverse.** One commit = one repo. Work in one seems to need edit in other → stop, it doesn't.
- **Never edit PPCLI source.** We CONSUMER, not author. Use it to drive game for diagnostics + test own mods. That whole relationship.
- **PPCLI misbehaves → only write it down** in `PPCLI\ISSUES.md` (log its maintainer reads). Then work around, continue ContentTool task. No fix, no patch in its source, no "improve" while there.

## Anything that touches the RUNNING GAME goes through PPCLI (standing rule)

Never ask user how to drive game, never hand-roll way. Task needs game itself — call game function, run console command, read def's real value, check patch took effect, spawn something, open screen, confirm model/texture renders → use **PPCLI** (`E:\DEV\PhoenixPoint\PPCLI\`).

- Read **`PPCLI\PLAYBOOK.md` FIRST** — maps plain intent to exact command line. Don't dig PPCLI source. Deep reference: `PPCLI\docs\REFERENCE.md`.
- Normal mode: `connect` / `plan` against ALREADY-RUNNING game (17–60 ms). `run` / `batch` cold-launch (~17 s), fallback when nothing running.
- Three surfaces: 344 native console commands, ~74 console variables (`var`, NOT `console`), arbitrary reflection via `call`.
- Multi-step work = plan in `PPCLI\plans\*.json`, not loop of `connect` calls. Plans have waits, timeouts, mandatory `finally` → clean up on fail.
- Bridge opt-in: arms only when file `ppcli-enabled` sits beside PPBridge.dll.
- `deploy` after EVERY PPBridge edit, else game silently runs old DLL (`stale:true` guard).
- Wait until `connect state` actually answers before sending anything. Querying still-initialising game hangs minutes, looks exactly like engine bug.

### Installs

- `D:\PP-Instance2` (profile `...592`) — automated runs + cold launches go here.
- `D:\Steam\steamapps\common\Phoenix Point` (profile `...591`) — USER'S OWN GAME. Reach via `-PPRoot "D:\Steam\steamapps\common\Phoenix Point"`. Reads free; anything WRITING to real save needs explicit permission each time. Don't kill process there.

### When PPCLI itself misbehaves

Log it, ONLY log it — see separation rule at top. Append to **`E:\DEV\PhoenixPoint\PPCLI\ISSUES.md`** — log in PPCLI's own repo root so PPCLI agent finds it at session start untold. Record only what actual run showed (attempted → happened → expected → evidence → severity), never suspicion from reading source. Don't stop current task to fix PPCLI; note + work around.

### Checking a model/texture without playing

Viewing replaced content needs no save load or mission start — game's own model viewer / editor screen opens directly. Drive via PPCLI (see `PLAYBOOK.md`); cold-start plans (`plans\start-mission.json`, `start-campaign.json`, `build-mission.json`) exist for cases genuinely needing live level.

## Repo

`ContentTool\` = OWN inner git repo (`UberMorgott/PhoenixPoint-Mod-ContentTool`, branch `main`), ignored by outer monorepo — commit ContentTool changes HERE. Push only on explicit ask. `local\` gitignored, never publish.

## Code-graph

Code-only graphify graph at `ContentTool\graphify-out\` (auto-refreshed by `.githooks\post-commit`). Query from ContentTool root. Name symbols in question — broad natural-language query returns BFS dump truncated at token budget, answer may be in cut part.