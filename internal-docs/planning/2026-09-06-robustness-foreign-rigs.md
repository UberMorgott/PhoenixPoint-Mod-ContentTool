# Robustness against foreign rigged models — 2026-09-06

Five Khronos glTF-Sample-Assets models (CC / permissive, test data only) pushed through the Model
Doctor and through the offline preflight arm, to see what the tool does with a file that was never
authored for this game's skeleton. ContentTool at HEAD `9398a6e`, bench `D:\PP-Instance2`
(profile `76561197996210592`), PPBridge `build=3068ae67`, never `stale`.

Samples: `CesiumMan.glb`, `RiggedFigure.glb`, `RiggedSimple.glb`, `BrainStem.glb`, `Fox.glb`
(scratchpad `samples\`).

## How each side was driven

- **Offline** — `tests\ObjCodecTests\bin\Release\net472\ObjCodecTests.exe --fit <bundle> <Mesh> <file.glb>`
  (note the argument order: bundle, mesh, file). Two targets:
  - RIGGED: `px_heavy_assets_all.bundle` / `CHR_PX_HVY_TS_M_V01` — 2 material slots, 10 bind poses,
    10 named bones.
  - STATIC: `px_equipment_assets_all.bundle` / `WPN_PX_RG_Assault_Rifle_T01_V01` — 1 material slot,
    **0 bind poses, 0 named bones**.
  Both bundles read from `D:\PP-Instance2\PhoenixPointWin64_Data\StreamingAssets\aa\StandaloneWindows64\`.
- **In game** — geoscape → `ct_bench open` → `FitBench.ShowPrototype(CHR_Human_Rig_Ready,
  "Human / PX_HeavyStarting")` → `SlotTargets()` (10) → `Doctor.PickTarget(Human_Torso_SlotDef)`
  → `Doctor.PickFile(<sample>)`, then `Doctor.Ready.Report.Header()` / `.Rows` / `.Outcome` and
  `Doctor.ShipRefusal` read back over PPCLI.

## Verdict per sample — offline and in game AGREE word for word

| File | Rigged target (offline `--fit`, exit) | Rigged target (in game) | Static target (offline `--fit`, exit) | SHIP | Exception |
|---|---|---|---|---|---|
| `CesiumMan.glb` | `NEAREST-BONE - the bake would import this but NOT use your weights (29 reason(s))`, `outcome NearestBone`, 30 rows (exit 1) | identical header, `NearestBone`, 30 rows | `NOT RIGGED - the target carries no bind poses`, `outcome NotRigged`, 1 row (exit **0**) | refused — `the report is not green by name` | no |
| `RiggedFigure.glb` | same header, `NearestBone`, 30 rows (exit 1) | identical | `NotRigged`, 1 row (exit 0) | refused, same line | no |
| `RiggedSimple.glb` | `NEAREST-BONE … (12 reason(s))`, `NearestBone`, 13 rows (exit 1) | identical | `NotRigged`, 1 row (exit 0) | refused, same line | no |
| `BrainStem.glb` | `IMPORT REFUSED (1 reason(s))`, `outcome Refused`, 1 row (exit 1) | identical | same refusal, 1 row (exit 1) | refused, same line | no (caught, see below) |
| `Fox.glb` | `IMPORT REFUSED (1 reason(s))`, `Refused`, 1 row (exit 1) | identical | same refusal, 1 row (exit 1) | refused, same line | no (caught) |

**No sample produced a BY NAME header against a foreign rig, and none threw.** The two refusals are
decided in the reader, before any target is consulted, which is why they read the same against the
rigged and the static target.

### The rows, verbatim

- `CesiumMan` / `RiggedFigure`: `Downgrade/File MissingBone` x10 + `Downgrade/File ExtraBone` x19 +
  one `Info/File SubmeshMaterials`. The missing ten are exactly the target's own bones —
  `Root`, `Spine_1`, `Spine_2`, `Spine_3`, `Chest`, `R.Shoulder`, `R.Arm`, `L.Shoulder`, `L.Arm`,
  `Neck` — each as `the file does not contain the bone 'X', which this model's skeleton has; the
  skeleton is never replaced, so in Blender keep the imported armature exactly as it came, with
  every bone and its name unchanged, and re-export`.
- `RiggedSimple`: `MissingBone` x10 + `ExtraBone` x2 + one `Info/File SubmeshMaterials` — the file
  carries only two joints, so it adds two and misses all ten.
- `BrainStem`: one `Blocking/File MalformedGlb` —
  `the armature's bone 0 has no name, and bones are matched to the game's skeleton by name; name
  every bone in Blender and re-export`.
- `Fox`: one `Blocking/File NotIndexed` —
  `primitive 0 has no index buffer, and this mod only reads indexed triangles; re-export from
  Blender, whose exporter always writes indices`.
- Static target, all three rigged-but-foreign files: one `Info/Target TargetNotRigged` —
  `not rigged - the target carries no bind poses`. `--fit` treats that as a PASS (exit 0) by design
  (`Program.cs:178` — `NotRigged` is a verdict about the TARGET, not a fault in the replacement).

## Exceptions — none escaped

`D:\PP-Instance2\ct-robust.log`, whole session:

- **No unhandled exception from any ContentTool frame.** The only ContentTool stack traces in the
  log belong to four `[ERROR] [ContentTool] Model Doctor: '<path>' - ImportRefusedException: <the
  sentence above>` lines (BrainStem x2, Fox x2 — each file was picked twice). Those are the CAUGHT
  refusals; the panel showed the friendly text and the Doctor stayed usable.
- The 13 `ArgumentException: Mesh can not have more than 65000 vertices` entries are
  `UnityEngine.UI.Text.UpdateGeometry` / `UI.VertexHelper.FillMesh` — the game's own console text
  buffer, present from frame 6 before anything was picked. Not ContentTool. See the console finding
  below, which is the same limit seen from the other side.

**Conclusion: graceful on all five. Refusals where the file cannot be read, nearest-bone downgrades
where it can be read but does not fit the skeleton, SHIP refused every time, no crash, no false
BY NAME.**

## Defects and observations

1. **`ct_project`'s verdict cannot be read in the in-game console.** `GameConsoleWindow.Create()` →
   `ToggleVisibility()` → `ExecuteCommandLine("ct_project DashboardResident", true, true)` returns
   `true` after 19.8 s and the console's output pane goes **completely blank**;
   `ct-robust2.log` collects `ArgumentException: Mesh can not have more than 65000 vertices` at
   `UI.VertexHelper.FillMesh` (22 occurrences). `ExecuteCommandLine("clear")` answers `false` — there
   is no clear command to trim the buffer with. The same command through
   `ppcli connect console ct_project DashboardResident` returns the whole verdict ending
   `ct_project: ALL PASS - this project has no bundle of its own; the patched copy(ies) above are
   the whole output`. `ct_list bundles` renders in the pane normally, so the console itself works —
   only a long verdict kills it. **Anything the user docs say about reading `ct_project` output in
   the console is wrong; point them at the log or at a shorter command.** Evidence:
   scratchpad `docs\console_ct_project.png` (blank) and `docs\console_ct_list.png` (working).
2. **The panel clips text instead of wrapping it.** At 1280x720 the bench panel is a fixed
   `BenchList.PanelWidth` (`FitBench.cs:1635`, a `const`), so lines longer than it are cut mid-word
   with no ellipsis: the Doctor header reads `| prototype Hun` and the browser's own
   `36 of 36 prototype(s)` counter breaks into a vertical column of characters at the panel edge.
   Cosmetic, but every documentation screenshot inherits it.
3. **An expected user-file refusal is logged at `[ERROR]` with a full stack trace.** Four such
   entries for two sample files. The refusal is handled and shown correctly; only the log level and
   the stack are noise.
4. **`--fit` exits 0 for a rigged file against a STATIC target.** Correct by its own rule, but a
   caller sweeping many files by exit code will read "CesiumMan fits the assault rifle" as a pass.
   The header (`NOT RIGGED - the target carries no bind poses`) is the thing to read, not the code.
5. **The dashboard fixtures all target `an_assault_assets_all.bundle`**, which the activated
   `Replace_Leftleg` mod owns, so `Run("All")` on any of them ends
   `Apply / void` with `REFUSED: mod 'Replace_Leftleg' already replaces an_assault_assets_all.bundle
   - 'acceptance.dashboardvalid' cannot also replace it. One shipped bundle has exactly one owner and
   the lower mod id keeps it`. An all-pass dashboard run on this bench needs `Replace_Leftleg`
   deactivated first. Not a defect — worth a line in the acceptance runbook.
6. **No PPCLI defect** — nothing appended to `PPCLI\ISSUES.md`. Usage notes for the next driver:
   a `Vector2` field needs the `{"$v2":[x,y]}` envelope (a plain `{"x":…,"y":…}` is refused with
   `code:"bind"`); `connect multi` needs a real JSON ARRAY, so a single-element PowerShell array has
   to be forced with `@(...)` or it serialises to an object and the call is refused; and
   `connect call` polls its job to completion, so `Run` + `screenshot` in one `multi` lands AFTER the
   stage finished — a mid-run frame needs the call fired from a background job.

## Screenshots for the user docs

Eleven of the twelve asked for are in the scratchpad `docs\` folder with `SHOTS.md` describing each
one. The twelfth (`console_ct_project.png` showing ALL PASS) is impossible for the reason in
finding 1; the file that stands in its place is the evidence of that.

All frames are single full-resolution captures because Renderforge's DLSS was switched OFF for the
session (`Renderforge.RenderforgeMod.SetMode("Off","None")`), which puts the 3D and the IMGUI in the
same PNG at 1280x720 instead of splitting them between `.png` and a 640x360 `.scene.png`. Restored
to `Auto` before exit.

## Bench state at exit

- `ct_bench close` → `ct_bench closed - the screen you came from was never left, so it is still there.`
- Instance2 stopped path-filtered (`Where-Object { $_.Path -like 'D:\PP-Instance2\*' }`, never by
  name); **0 `PhoenixPointWin64` processes anywhere afterwards**.
- `D:\PP-Instance2\Mods\PPBridge\ppcli-enabled` deleted (verified absent).
- `Options.jopt` restored byte-for-byte from `Options.jopt.bak-robust`: `Replace_Leftleg` is back in
  `MOD_ACTIVATED`, 20 entries, `ArrayDimensions.CollectionValues` back to 20.
- Renderforge back to `Auto`; the game console hidden again and `DisableConsoleAccess` back to
  `true`.
- `Wizard.ApocDesignation` untouched — still ACTIVATED, still the 5-row manifest, still served at
  every launch, exactly as run 5 left it. The Doctor preview was reverted by `PickTarget`/`PickFile`
  before the bench closed; nothing was written into the project or into the game installation.
- The acceptance fixtures `DashboardValid` / `DashboardAuthor` / `DashboardResident` were baked and
  (for DashboardValid) applied LIVE for that session only — none is activated, so a restart clears
  the redirect. Their patched copies live under
  `…\LocalLow\Snapshot Games Inc\Phoenix Point\ContentTool\Patched\d29f58a2\acceptance.dashboard*`.
