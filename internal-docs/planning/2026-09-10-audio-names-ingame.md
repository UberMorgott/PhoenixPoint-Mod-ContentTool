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
