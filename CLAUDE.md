# ContentTool — project rules

## Anything that touches the RUNNING GAME goes through PPCLI (standing rule)

Never ask the user how to drive the game, and never hand-roll a way to do it. Whenever a task needs
the game itself — calling a game function, running a console command, reading a def's real value,
checking whether a patch actually took effect, spawning something, opening a screen, or confirming a
model/texture renders — use **PPCLI** (`E:\DEV\PhoenixPoint\PPCLI\`).

- Read **`PPCLI\PLAYBOOK.md` FIRST** — it maps plain intent to the exact command line. Do not dig
  PPCLI source. Deep reference: `PPCLI\docs\REFERENCE.md`.
- Normal mode is `connect` / `plan` against an ALREADY-RUNNING game (17–60 ms). `run` / `batch`
  cold-launch (~17 s) and are the fallback when nothing is running.
- Three surfaces: 344 native console commands, ~74 console variables (`var`, NOT `console`), and
  arbitrary reflection via `call`.
- Multi-step work = a plan in `PPCLI\plans\*.json`, not a loop of `connect` calls. Plans have waits,
  timeouts and a mandatory `finally`, so they clean up when they fail.
- The bridge is opt-in: it arms only when a file named `ppcli-enabled` sits beside PPBridge.dll.
- `deploy` after EVERY PPBridge edit, or the game silently runs the old DLL (`stale:true` guard).
- Wait until `connect state` actually answers before sending anything. Querying a still-initialising
  game hangs for minutes and looks exactly like an engine bug.

### Installs

- `D:\PP-Instance2` (profile `...592`) — automated runs and cold launches belong here.
- `D:\Steam\steamapps\common\Phoenix Point` (profile `...591`) — the USER'S OWN GAME. Reach it with
  `-PPRoot "D:\Steam\steamapps\common\Phoenix Point"`. Reads are free; anything that WRITES to a real
  save needs explicit permission each time. Do not kill a process there.

### Checking a model/texture without playing

To look at replaced content there is no need to load a save or start a mission — the game's own
model viewer / editor screen can be opened directly. Drive it through PPCLI (see `PLAYBOOK.md`); the
cold-start plans (`plans\start-mission.json`, `start-campaign.json`, `build-mission.json`) exist for
the cases that genuinely need a live level.

## Repo

`ContentTool\` is its OWN inner git repo (`UberMorgott/PhoenixPoint-Mod-ContentTool`, branch `main`),
ignored by the outer monorepo — commit ContentTool changes HERE. Push only when explicitly asked.
`local\` is gitignored and must never be published.

## Code-graph

Code-only graphify graph at `ContentTool\graphify-out\` (auto-refreshed by `.githooks\post-commit`).
Query from the ContentTool root. Name the symbols in the question — a broad natural-language query
returns a BFS dump truncated at the token budget and the answer can be in the cut part.
