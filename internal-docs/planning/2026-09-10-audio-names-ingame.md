# In-game proof — `ct_list audio` / `ct_extract audio` (commit `1de25d7`)

Bench `D:\PP-Instance2`, profile `76561197996210592`, 2026-09-10. Everything below is a verbatim
line off a real run; nothing was played.

## Setup

- Built by the repo's own `deploy.ps1` (default target `D:\PP-Instance2`, `dotnet build -c Release`).
  `D:\PP-Instance2\Mods\ContentTool\ContentTool.dll` = 1 989 120 B, 10.09.2026 23:39:39,
  SHA-256 `B5C2CFFB8A53813EC768C2227A63A9A21456A87FCD9E11CC03BD9A4C6729A368` — byte-identical to
  `bin\Release\ContentTool\ContentTool.dll`.
- In-game banner: `ContentTool 1.2.0.0 | build=b3b0fc1d | AssetsTools.NET merged: True | classdata.tpk embedded: 289605 B`.
- PPBridge NOT redeployed (`build=69a823ae` in every reply, no `stale:true`). `ppcli-enabled` created
  for the run, deleted at exit. Profile `Options.jopt` NEVER edited (`com.morgott.ContentTool` was
  already in `MOD_ACTIVATED`).
- Launched by hand: `Start-Process 'D:\PP-Instance2\PhoenixPointWin64.exe' -ArgumentList '-mods','-logFile','D:\PP-Instance2\ct-audio.log'`,
  gated on `connect state` → `{"phase":"menu","scene":"HomeScreen","level":"HomeScreenLevel(Clone)"}`.
- **Main menu IS enough** — verified. Every audio verb below ran at the HomeScreen with no level
  loaded; they read `StreamingAssets` and the shipped `SoundbanksInfo.xml`, nothing else. Nothing
  was refused for lack of a mission or a campaign.

## GATE — the commands register at t≈33 s, not at load

`ct_list` at t≈20 s → `{"ok":false,"error":"unknown command 'ct_list'"}`. The log shows registration
only at frame 7611 (33,386 s):
`[Mods] [com.morgott.ContentTool] Console commands: ct_version, ct_bake, …, ct_list, ct_extract, …`.
`connect state` answering is NOT a gate on ContentTool's console surface — wait for that log line,
or retry the first `ct_` call.

## (a) `ct_list audio taunt`

```
263 of 7696 media match 'taunt' - 258 loose (extractable), 5 in-bank (not extractable)
  32151022   1CBMN_Taunt1                      Barks                     loose
  1029598187 1CBMN_Taunt2                      Barks                     loose
  757596614  1CBMN_Taunt3                      Barks                     loose
  1019543737 1CBMN_Taunt4                      Barks                     loose
  590794994  1f_ANU_Taunt_1                    Barks                     loose
  654766181  1f_ANU_Taunt_2                    Barks                     loose
  264387037  1f_ANU_Taunt_3                    Barks                     loose
  136593175  1f_ANU_Taunt_4                    Barks                     loose
  376822706  1f_IND_Taunt_1                    Barks                     loose
  727362411  1f_IND_Taunt_2                    Barks                     loose
```

Total = **263 of 7696**. Names, banks and the loose/in-bank mark are all real. In-bank rows ARE
marked — none of the 5 taunt ones fit inside what PPCLI hands back (see the cap below), so proved on
a filter whose matches are all in-bank:

```
6 of 7696 media match 'SparksFly' - 0 loose (extractable), 6 in-bank (not extractable)
  516536836  SparksFly1                        EnvironmentElectricSparks in-bank
  30693822   SparksFly2                        EnvironmentElectricSparks in-bank
  152103498  SparksFly3                        EnvironmentElectricSparks in-bank
```

## (b) id filter and bank filter

```
1 of 7696 media match '6372602' - 1 loose (extractable), 0 in-bank (not extractable)
  6372602    5_IND_Taunt_03                    Barks                     loose
```

```
0 of 7696 media match 'VoiceBank' - 0 loose (extractable), 0 in-bank (not extractable)
```

`VoiceBank` is not a bank this build ships — the filter is honest, not broken. A real bank name
matches: `5627 of 7696 media match 'Barks' - 2303 loose (extractable), 3324 in-bank (not extractable)`.

## (c) `ct_list audio`, no filter

Header: `7696 of 7696 media match '' - 3105 loose (extractable), 4591 in-bank (not extractable)`.

- **Through PPCLI you get 200 lines, not 7697.** `{"ok":true,"output":[…200…],"truncated":true}`.
  The cap is PPBridge's, not ours: `Protocol.MaxOutputLines = 200` (`PPCLI\src\Protocol.cs:55`)
  enforced in `PPBridgeMain.Capture.WriteLine` (`PPCLI\src\PPBridgeMain.cs:361`). ContentTool
  deliberately hands a capture console the WHOLE text in one unbounded call
  (`src\ContentToolMain.cs:382-388`), so the one machine-readable path is cut by the bridge.
  Logged in `PPCLI\ISSUES.md`.
- **In the game pane** the output is bounded to `ConsoleText.MaxLines = 60` and the rest spills to a
  file, exactly as designed. Verbatim from the pane (`ct-audio.log`, `[CONSOLE]` lines):
  `... 204 line(s) not shown`, then
  `... the whole output is in C:/Users/Morgott/AppData/LocalLow/Snapshot Games Inc/Phoenix Point\ContentTool\Logs\console-20260910-234549-121.txt`.
  One spill file per run, 5 runs, 5 files.

## (d) `ct_extract audio 6372602`

```
ct_extract wrote C:/Users/Morgott/AppData/LocalLow/Snapshot Games Inc/Phoenix Point\ContentTool\Extracted\audio\6372602.wem and C:\Users\Morgott\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\ContentTool\Extracted\audio\5_IND_Taunt_03__6372602.wav (166288 B, 2 ch, 24000 Hz, Wwise Vorbis)
```

On disk: `6372602.wem` 20 048 B, `5_IND_Taunt_03__6372602.wav` 166 288 B. The `.wav` carries the
sound's NAME plus the id; the `.wem` keeps the shipped numeric name.

## (e) `ct_extract audio --all taunt` — 4.4 s

```
ct_extract wrote C:/…\ContentTool\Extracted\audio\index.csv (7691 named media)
extracted 258 of 258 loose media (5 in-bank skipped) into C:/…\ContentTool\Extracted\audio
```

- 258 `.wav` written, no `FAILED` line. Names: `1_ANU_Taunt_1__479484627.wav`,
  `1_ANU_Taunt_2__502268333.wav`, `1_ANU_Taunt_3__653409667.wav`, `1_ANU_Taunt_4__603329847.wav`,
  `1_IND_Taunt_1__263718516.wav`, `1_IND_Taunt_2__180862907.wav` … `SIRN_Taunt2__992026775.wav`,
  `SIRN_Taunt3__688594374.wav`, `SIRN_Taunt4__1028140705.wav`.
- `index.csv` exists. Header `id,shortName,bank,loose,wav`; **7697 rows** = header + all 7696 media.
  First row: `1258614,4f_S_Select_01.wav,Barks,yes,`.
- In-bank media ARE in it — **4591 rows**, column `loose` = **`no`** (not `false`; the brief's
  `loose=false` is the wrong literal):
  `30693822,SparksFly2.wav,EnvironmentElectricSparks,no,`.
- Only the 258 extracted rows carry a `wav` value:
  `6372602,5_IND_Taunt_03.wav,Barks,yes,5_IND_Taunt_03__6372602.wav`.

## (f) `ct_extract audio --all`, no filter — 123.7 s wall

```
ct_extract wrote C:/…\ContentTool\Extracted\audio\index.csv (7691 named media)
extracted 3105 of 3105 loose media (4591 in-bank skipped) into C:/…\ContentTool\Extracted\audio
```

- **3105 of 3105, zero decode failures** — no `FAILED` line at all.
- On disk: **3105 `.wav`**, 3 218.8 MB total. `index.csv` 7697 rows, 3105 of them with a `wav` value.
- 5 files land under a bare id (`1055975960.wav`, `253449534.wav`, `339396934.wav`, …): those media
  carry no name in `SoundbanksInfo.xml` — 7691 named of 7696, and the fallback is the id, never a
  blank name.
- 2 minutes for the whole library is fine for a one-shot dump; the reply arrives via PPCLI's job
  polling, not a timeout.

## Defects found

1. **ContentTool — the game pane still blanks on a `ct_list` run.** `ct_version` (1 line) renders
   fine; `ct_list audio taunt` immediately after leaves the pane EMPTY, and the log gains one
   `ArgumentException: Mesh can not have more than 65000 vertices` at
   `UnityEngine.UI.Text.UpdateGeometry` → `CanvasUpdateRegistry.PerformUpdate` per run — 5 `ct_list`
   runs, 5 throws, 5 spill files. So `adb70f3`'s 60-line/12000-char bound did NOT close it: the
   spill file is written and named correctly, but what the pane should still be showing is not
   drawn. Screenshot of the blank pane + the working `ct_version` line in this session's scratchpad
   (`docs\console-ct_list-audio.png`, `docs\pane3-crop.png`).
2. **PPCLI — `console` output is cut at 200 lines** (`Protocol.MaxOutputLines`), so a long ct_
   listing cannot be read back whole through the one machine-readable path. Appended to
   `PPCLI\ISSUES.md`.
3. **Pre-existing, unrelated:** `[com.morgott.ContentTool] ct_sound defer THREW
   System.InvalidOperationException: Timing.Current should be called from inside a running
   IUpdateable` at `ContentToolMain.OnModEnabled`. Fires at menu; harmless to everything above.

## Bench state at exit

Instance2 process stopped by path filter (`Path -like 'D:\PP-Instance2\*'`, never `-Name`);
`D:\PP-Instance2\Mods\PPBridge\ppcli-enabled` deleted; profile untouched. **3.2 GB of extracted
`.wav` left behind** at `C:\Users\Morgott\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\ContentTool\Extracted\audio`
— delete when the evidence is no longer wanted.

---

# Re-check 2026-09-11 — commits `4d3ff46` + `c2ab336`

Same bench `D:\PP-Instance2`, profile `76561197996210592`. ContentTool at HEAD `c2ab336`, built and
deployed by the repo's own `deploy.ps1`; `D:\PP-Instance2\Mods\ContentTool\ContentTool.dll`
1 990 144 B, SHA-256 `65A392448C963FEA0F4483494A875C9D260CFAE2CDBD1E44B874E9ACBFC0B50C`,
byte-identical to `bin\Release\ContentTool\ContentTool.dll`. Banner:
`ContentTool 1.2.0.0 | build=30fd27e4 | AssetsTools.NET merged: True | classdata.tpk embedded: 289605 B`.
PPBridge NOT redeployed (`build=69a823ae`, no `stale:true`). Log `D:\PP-Instance2\ct-audio-0911.log`.

## Registration was NOT slow this time

`[Mods] [com.morgott.ContentTool] Console commands: ct_version, …` at **frame 5 (0,412 s)**, i.e.
before `connect state` answered — yesterday's t≈33 s was not a fixed cost. Gate on the log line
anyway; it is the only thing that is actually true in both runs.

## How the PANE is reached at all (yesterday's method, written down)

`ppcli connect console` hands ContentTool a **capture** `IConsole`, and `Out()`
(`src\ContentToolMain.cs:409`) only takes the bounded pane path when
`console is GameConsoleWindow` — so a PPCLI `console` call never touches the pane and never writes
a `[CONSOLE]` line. To drive the real pane, reflect into the game's own window:

```powershell
.\ppcli.ps1 connect call '{"op":"invoke","type":"Base.Utils.GameConsole.GameConsoleWindow","member":"Create","args":[]}'   # -> h:1:1
.\ppcli.ps1 connect call '{"op":"set","type":"Base.Utils.GameConsole.GameConsoleWindow","member":"DisableConsoleAccess","value":false}'
.\ppcli.ps1 connect call '{"op":"invoke","target":"h:1:1","member":"ToggleVisibility","args":[]}'
.\ppcli.ps1 connect call '{"op":"invoke","target":"h:1:1","member":"ExecuteCommandLine","args":["ct_list audio taunt"],"sig":["System.String"]}'
```

`DisableConsoleAccess` defaults to `true` and `ToggleVisibility` (`GameConsoleWindow.cs:222`) folds
it into `enabled`, so without the `set` the pane stays invisible.

## (1) `ct_list audio taunt` — PASS, the blank pane is GONE

Pane, verbatim (`[CONSOLE]` lines): header + 10 rows + the trailer

```
263 of 7696 media match 'taunt' - 258 loose (extractable), 5 in-bank (not extractable)
  32151022   1CBMN_Taunt1                      Barks                     loose
  … 9 more rows …
... 253 more - the whole list is in C:/Users/Morgott/AppData/LocalLow/Snapshot Games Inc/Phoenix Point\ContentTool\Logs\ct_list-audio-20260911-001803-931.txt
```

The named file exists: **264 lines** = header + all 263 rows, first `  32151022   1CBMN_Taunt1 …`,
last `  1028140705 SIRN_Taunt4 …`. Screenshot `docs\console-ct_list-audio.png` — the pane RENDERS,
rows and trailer readable. **Zero `Mesh can not have more than 65000 vertices` in the whole run** up
to this point (yesterday: one per `ct_list`).

## (2) `ct_list audio`, no filter — PASS

Pane: `7696 of 7696 media match '' - 3105 loose (extractable), 4591 in-bank (not extractable)`,
10 rows, then `... 7686 more - the whole list is in …\ct_list-audio-20260911-001832-629.txt`.
That file: **7697 lines**. Still 0 mesh throws.

## (3) `ct_list audio Barks` — PASS

`5635 of 7696 media match 'Barks' - 2305 loose (extractable), 3330 in-bank (not extractable)`,
10 rows (in-bank rows among them, e.g. `220988377  1CBMN_DeployShield … in-bank`), trailer
`... 5625 more - the whole list is in …\ct_list-audio-20260911-001838-764.txt`, file **5636 lines**.
Counts differ from 2026-09-10's `5627 / 2303 / 3324` — same build family, so the small drift is
worth a look, but it is not what this re-check was about.

## (4) `ct_project` — STILL BLANKS THE PANE (the fix did not reach this path)

`ct_project DashboardValid` does not exist on this bench, and nowhere in the repo — the only
`ppcontent.json` projects are `demos\*`, `dist-package\*`, `local\PpFit`, `Wizard.ApocDesignation`.
It printed a clean, handled refusal, no crash:
`ct_project THREW System.IO.FileNotFoundException: no ppcontent.json in D:\PP-Instance2\Mods\ContentTool\DashboardValid`.

Substituted the deployed `Sample` project, which is the long verdict the step wanted.
`ct_project Sample` → bounded exactly as designed (60 lines, then `... 21 line(s) not shown`, then
`... the whole output is in …\ContentTool\Logs\console-20260911-001928-565.txt`, last verdict line
`ct_project: 1 FAILURE(S)`) — **and the pane went completely blank, with 6
`ArgumentException: Mesh can not have more than 65000 vertices` at
`UnityEngine.UI.VertexHelper.FillMesh` → `Graphic.DoMeshGeneration` → `Graphic.UpdateGeometry`.**
Screenshot `docs\console-ct_project.png`.

The failure is one-shot, not per-frame: the count stopped at 6 and did not grow while idle. A
`ct_version` run straight afterwards renders normally on the now-empty pane
(`docs\console-after-project.png`) — so the aborted canvas rebuild permanently loses the lines that
were already written and the pane recovers only for NEW content.

So `c2ab336` fixed the `ct_list` path only. `ct_project`'s verdict lines are individually much
longer than a listing row (several carry ContentTool's own `…(clipped)` marker at ~600 chars) and
its spill still calls `Say(msg)`; something on that path — most likely the game's log overlay
concentrating the chunked `Debug.Log` into one `UI.Text` — still crosses 65000 vertices.
`ct_list`'s bounded path does not call `Say` at all, which is exactly why it is now clean.

## Bench state at exit

Process stopped by the path filter (`Path -like 'D:\PP-Instance2\*'`); `ppcli-enabled` deleted;
profile untouched. **Yesterday's 3105 `.wav` (3.2 GB) deleted** from
`…\Phoenix Point\ContentTool\Extracted\audio`. `ct_project Sample` baked into
`…\ContentTool\Patched\d29f58a2\morgott.sample\` (bench-only output of our own demo project) and
left it there.

---

# Re-check 2 (`2e2c39f`) — 2026-09-11

Same bench `D:\PP-Instance2`, profile `76561197996210592`, main menu only. Built + deployed by the
repo's own `deploy.ps1`; `D:\PP-Instance2\Mods\ContentTool\ContentTool.dll` 1 991 168 B, SHA-256
`AF8C1EE9E86987177C45566ACF897C4EC43E10E639EC54B799BEB5DDE2A8068B`, byte-identical to
`bin\Release\ContentTool\ContentTool.dll`. Banner
`ContentTool 1.2.0.0 | build=0f81298c | AssetsTools.NET merged: True | classdata.tpk embedded: 289605 B`.
PPBridge NOT redeployed (`build=69a823ae`, no `stale:true`). Log `D:\PP-Instance2\ct-recheck2-0911.log`
(explicit `-logFile`). Registration again early: frame 5 (0,383 s). Real pane driven by the
`GameConsoleWindow.Create` -> `DisableConsoleAccess=false` -> `ToggleVisibility` ->
`ExecuteCommandLine` recipe above.

## (1) `ct_project Sample` on the real pane — PARTIAL PASS: pane SURVIVES, one line lost

The blanking is GONE. After `ct_version` / `ct_project Sample` / `ct_version` the pane renders, and
the whole bounded verdict reached it — the game's own `AppendToLogFile` mirror carries all of it:

```
... 21 line(s) not shown
extract: 1 stream(s), 0 rewritten | AddBasePath(D:\PP-Instance2\Mods\ContentTool\WwiseAudio\): AK_Success | ...
BANK PASS assets/morgott.sample/audio/banks/morgott_sample.bnk -> UnloadBank: AK_UnknownBankID | ...
ct_project: 1 FAILURE(S)
... the whole output is in C:/Users/Morgott/AppData/LocalLow/Snapshot Games Inc/Phoenix Point\ContentTool\Logs\console-20260911-003533-131.txt
```

Spill file exists, 47 149 B (byte-identical in size to yesterday's `console-20260911-001928-565.txt`).

**But on screen the spill trailer is MISSING**: the rendered pane goes straight from
`ct_project: 1 FAILURE(S)` to the next `ct_version` banner. It is the LAST line written before
`Say(msg)`, it was accepted by the console (it is in the `[CONSOLE]` mirror at log line 833), and it
is the only line lost — yesterday the entire pane went blank. Screenshot
`docs\console-ct_project.png` (crop `docs\crop-tail.png`).

## (2) Mesh exceptions — FAIL, 5 (was 6)

`ChunkedLog` works: the long transcript went out as `(part 1/7)` … `(part 7/7)`, each its own
`Debug.Log`. But **5 `ArgumentException: Mesh can not have more than 65000 vertices`** still fire,
all AFTER `(part 7/7)` (log lines 944, 961, 978, 995, 1012), at
`UnityEngine.UI.VertexHelper.FillMesh` -> `Graphic.DoMeshGeneration` -> `Graphic.UpdateGeometry`.

So the per-message cap is not the whole limit: the sink ACCUMULATES. 7 messages of <=8000 chars
each still push one `UI.Text` over 65000 vertices when they land back-to-back. Bounding each message
turned a total blank into one dropped line; it cannot reach zero while the budget is per-canvas and
the spend is per-run.

## (3) `ct_list audio taunt` — PASS, unchanged

`263 of 7696 media match 'taunt' - 258 loose (extractable), 5 in-bank (not extractable)`, 10 rows
(`32151022 1CBMN_Taunt1 … loose` first, `727362411 1f_IND_Taunt_2` last), trailer
`... 253 more - the whole list is in …\ct_list-audio-20260911-003739-623.txt` (20 873 B).
Rendered and readable; the mesh count did NOT move (still 5) — this path stays clean.
Screenshot `docs\console-ct_list-audio.png`.

## Bench state at exit

Process stopped by the path filter (`Path -like 'D:\PP-Instance2\*'`); `ppcli-enabled` deleted;
profile untouched. `ct_project Sample` re-baked into
`…\ContentTool\Patched\d29f58a2\morgott.sample\` and left there. Log kept at
`D:\PP-Instance2\ct-recheck2-0911.log`.

---

# Re-check 3 (`47e757e`) — 2026-09-11

Same bench `D:\PP-Instance2`, profile `76561197996210592`, main menu only. `dotnet build -c Release`
then the repo's own `deploy.ps1`; `D:\PP-Instance2\Mods\ContentTool\ContentTool.dll` 1 991 680 B,
SHA-256 `2A4B871FE944412C60AE68CFB5E0135288FB6F3FC06CECF75DC98C8B51F53A09`, byte-identical to
`bin\Release\ContentTool\ContentTool.dll`. Banner
`ContentTool 1.2.0.0 | build=c0b5d02a | AssetsTools.NET merged: True | classdata.tpk embedded: 289605 B`.
PPBridge NOT redeployed (`build=69a823ae`, no `stale:true`). Registration again at frame 5 (0,384 s).
Real pane driven by the `GameConsoleWindow.Create` -> `DisableConsoleAccess=false` ->
`ToggleVisibility` -> `ExecuteCommandLine` recipe. Logs `D:\PP-Instance2\ct-recheck3-0911.log`
(console run) and `D:\PP-Instance2\ct-recheck3-startup.log` (startup run).

## (1) `ct_version` / `ct_project Sample` / `ct_version` — PASS, trailer included

The pane renders the whole bounded verdict AND the spill trailer, which re-check 2 was still losing.
Verbatim tail on screen (`docs\console-ct_project.png`, crop `docs\crop-top.png`):

```
... 21 line(s) not shown
extract: 1 stream(s), 0 rewritten | AddBasePath(D:\PP-Instance2\Mods\ContentTool\WwiseAudio\): AK_Success | 3338666242: cached
BANK PASS assets/morgott.sample/audio/banks/morgott_sample.bnk -> UnloadBank: AK_UnknownBankID | LoadBankMemoryCopy: AK_Success bankId=157178304
ct_project: 1 FAILURE(S)
... the whole output is in C:/Users/Morgott/AppData/LocalLow/Snapshot Games Inc/Phoenix Point\ContentTool\Logs\console-20260911-005758-690.txt
ContentTool 1.2.0.0 | build=c0b5d02a | ...
```

Spill file `…\ContentTool\Logs\console-20260911-005758-690.txt` exists, **47 149 B / 81 lines**
(= the 60 shown + the 21 not shown), last line `ct_project: 1 FAILURE(S)` — the full verdict.

## (2) Mesh exceptions — PASS, **0** (was 5, was 6)

`Mesh can not have more than 65000 vertices`: **0** in the whole console run. `Exception` anywhere in
that log: **0**. Bounding the whole command to ONE message finished what per-message chunking started.

## (3) `ct_list audio taunt` — PASS, unchanged

`263 of 7696 media match 'taunt' - 258 loose (extractable), 5 in-bank (not extractable)`, 10 rows
(`32151022 1CBMN_Taunt1 … loose` first, `727362411 1f_IND_Taunt_2` last), trailer
`... 253 more - the whole list is in …\ct_list-audio-…txt`. Mesh count did not move (still 0).

## (4) Startup path (`OnModEnabled` bake) — PASS

`Wizard.ApocDesignation` was **NOT** in the bench profile's `MOD_ACTIVATED` (first run logged
`Wizard.ApocDesignation: skipped, disabled in the mod manager`), so the array was edited by hand
(7 -> 8 entries, `ArrayDimensions.CollectionValues` bumped to match; backup
`Options.jopt.bak-recheck3`) and the game restarted.

The bake's whole transcript reached the log as ONE `Debug.Log` (one `(Filename: …Debug.bindings.h)`
trailer), bounded exactly like a command:

```
... 11 line(s) not shown
copies ready in …\ContentTool\Patched\d29f58a2\Wizard.ApocDesignation - nothing to install: ticking …
ct_project: 1 FAILURE(S) - 1 warning(s), baked anyway
NOT APPLIED: patching the shipped bundle(s) reported 1 failure(s), named in the P0/REFUSED line(s) above; …
... the whole message is in C:/Users/Morgott/AppData/LocalLow/Snapshot Games Inc/Phoenix Point\ContentTool\Logs\log-20260911-010031-953.txt
```

Spill file 8 576 B / **31 lines** (20 shown + 11 not shown). Startup log: **0** mesh throws,
**0** `Exception`. (The `1 FAILURE(S)` is the pre-existing P4 one-triangle-part warning on
`chr_px_hvy_ts_m_v01`, not a logging defect.)

## Bench state at exit

Process stopped by the path filter (`Path -like 'D:\PP-Instance2\*'`); `ppcli-enabled` deleted.
Profile CHANGED on purpose: `Wizard.ApocDesignation` left ON in `MOD_ACTIVATED` (backup kept beside
it). `ct_project Sample` re-baked into `…\Patched\d29f58a2\morgott.sample\`, and the Apoc bake wrote
`…\Patched\d29f58a2\Wizard.ApocDesignation\px_heavy_assets_all.bundle` (124 027 774 B); both left in
place. Both logs kept.
