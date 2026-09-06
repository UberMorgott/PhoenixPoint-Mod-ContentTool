# `Wizard.ApocDesignation` — end-to-end run on the bench, 2026-09-06

First run of a REAL author project (not a fixture) through the lifecycle dashboard on
`D:\PP-Instance2` (profile `76561197996210592`). ContentTool at HEAD `48a1e12`.

## Setup

- `deploy.ps1` (default target `D:\PP-Instance2`) → `D:\PP-Instance2\Mods\ContentTool\ContentTool.dll`,
  `Ошибок: 0`. PPBridge NOT redeployed — `build=3068ae67` in every reply, no `stale:true` anywhere.
- Project COPIED (never symlinked) repo → `D:\PP-Instance2\Mods\Wizard.ApocDesignation\` (26 files,
  21 427 383 B). The repo copy and the raw-GLB folder were never written to.
- `D:\PP-Instance2\Mods\PPBridge\ppcli-enabled` created for the run, deleted at exit.
- Launched BY HAND `Start-Process 'D:\PP-Instance2\PhoenixPointWin64.exe' -ArgumentList '-mods','-logFile',…`
  — run 1 `ct-apocd.log` PID 30484, run 2 `ct-apocd2.log` PID 36124. Gate: `connect state -PPRoot
  'D:\PP-Instance2' -ProfileId 76561197996210592` answered before anything else was sent, both runs.
  Identity preflight: the seam's `gameRoot` read `D:\PP-Instance2` in both runs.

## Two traps found before a single stage could run

**T1 — an ACTIVATED content project is baked at startup, and a failing startup bake locks the
dashboard out for the whole session.** Run 1 added `Wizard.ApocDesignation` to the profile's
`MOD_ACTIVATED` (19 → 20, count mirrored in `ArrayDimensions.CollectionValues`, file byte-copied
first). `ct_content` then baked it during `OnModEnabled`, the bake failed, and the dashboard answered
`Run("All")` synchronously with

> `'Wizard.ApocDesignation' failed to bake earlier in this session - not baking it again. Fix the lines it printed, then 'ct_route7 apply Wizard.ApocDesignation'.`

`runId 0`, nothing dispatched. **A dashboard run needs the project NOT activated.** Run 2 restored
`Options.jopt` from the byte copy (back to 19 entries, no `Wizard.ApocDesignation`) and the session
opened clean — `failedMember: null`, all five rows `never`.

**T2 — the Bake's main-thread segment PARKS until the panel is painted.** `Run("All")` from the
HomeScreen reached `Validate: PASS`, then sat at `busy:true, stage:"Bake", parkedForPaint:true`
indefinitely: the blocking segment waits on `LifecycleDashboard.Pump(open && tab == TabLifecycle)`
(`FitBench.cs:2163`, policy at `LifecycleJob.cs:450`), and `ct_bench` needs a loaded geoscape. Order
that works, and the only one that does:

1. `plan .\plans\start-campaign.json '{"difficultyIndex":1}'` (37 steps, 14.5 s)
2. `console ct_bench open` (791 unit templates, 201 weapons)
3. `call {"op":"set","type":"Morgott.ContentTool.Dev.FitBench","member":"tab","value":2}` (LIFECYCLE)
4. `Open` / `Run("All")` / poll `Snapshot`

The parked segment resumed the instant the tab flipped — nothing was re-run, as the design says.
Consequence: the geoscape has already loaded `px_heavy_assets_all.bundle`, so `restartRequired` is
`true` from the first poll and any Apply on this path is an S1 restart, never a live redirect.

## Stage verdicts, verbatim (run 2, `Open` bound `D:\PP-Instance2\Mods\Wizard.ApocDesignation` / id `Wizard.ApocDesignation`)

| Stage | freshness / outcome / starts | Verdict |
|---|---|---|
| Validate | stale / **pass** / 1 | `Validate: PASS - 'Wizard.ApocDesignation' - key c13f495e430889997a905a38927911519bcc815f.` |
| Bake | stale / **fail** / 1 | 7 000-char verdict, four rows — below |
| Apply | stale / none / **0** | never entered — the chain stopped at Bake |
| Verify | stale / none / **0** | never entered |
| Package | stale / none / **0** | never entered |

`Snapshot("s1s2")` → `s1: null, s2: null`. Restart required: not reached (Apply never ran), though the
header already carried `restartRequired: true` from the geoscape load. Bundles served: **none** —
nothing was applied, nothing was redirected, nothing was written into the installation.

Panel proof `apoc-lifecycle-running.png` (all five rows, `Session Ready. restart required`,
`Progress publishing 0/0`, Log tail scrolled to the P6 VOID row).

### The four Bake rows — this is what the project actually is

1. **Torso — REFUSED.** `P4 REFUSED 'chr_px_hvy_ts_m_v01' -> 'CHR_PX_HVY_TS_M_V01' is a rigged model
   … and the replacement file carries no armature, so there are no weights to follow that skeleton
   with. … In Blender, give the mesh an Armature modifier with vertex groups, weight it to the bones
   the target already has, and export it as .glb.`
2. **Right leg — patched, but NEAREST-BONE.** `mesh 'CHR_PX_HVY_RL_M_V01' <- chr_px_hvy_rl_m_v01
   verts=13341 indices=35901 … - skinned nearest-bone - the file's own weights were NOT used: the
   file adds the bone '#L.UpLeg_Roll_1_Addon => PX_Heavy_RightLeg_BodyPartDef', which this model's
   skeleton does not have; the skeleton is never replaced, so delete the added bone in Blender and
   re-export`. Read-back: `P4 PASS`, `P4-bytes PASS`, `P5 PASS` (skinned to the shipped skeleton,
   bindposes=10, rootHash=2424243207), **`P6 VOID`** — no by-name binding to measure.
3. **Left leg — same shape**, added bone `'#L.Foot_Toes_Addon => PX_Heavy_LeftLeg_BodyPartDef'`.
   `P4/P4-bytes/P5 PASS`, `P6 VOID`.
4. **Texture — REFUSED.** `P1 REFUSED 'RR_soldier_albedo' is not a .png/.jpg under Content\Textures\
   - the file IS in the project, at Content\Meshes\materials\RR_soldier_albedo.png; move it into
   Content\Textures\ and bake again`.

Both bundles were still written (`px_heavy_assets_all.bundle` 123 707 078 B,
`px_assault_assets_all.bundle` 116 009 227 B, under `…\ContentTool\Patched\d29f58a2\Wizard.ApocDesignation\`)
and each carries `PARTIAL … 1 row(s) above were REFUSED and the copy was rewritten anyway`. Terminal:
`ct_project: 2 FAILURE(S)` (torso + texture). Full text in `D:\PP-Instance2\ct-apocd.log` — the wire
clips the verdict at 2 000 chars (`Protocol.Clip`), the log does not; run 1 and run 2 agree word for word.

**So: 3 declared mesh replacements → 0 usable. 1 declared texture → 0. Two fixes are Blender-side,
one is a folder move.**

## The raw GLBs — `APOCD GLBs for content tool without apply tranforms\` — ALL THREE ARE GOOD

Checked in place (never copied into the project) through the real Doctor: `FitBench.ShowPrototype`
→ `Human / PX_HeavyStarting` (record `CHR_Human_Rig_Ready`, 166 variants), `SlotTargets()` → 10
targets, then `Doctor.PickTarget(<slot>)` + `Doctor.PickFile(<raw file>)`.

| File | Target slot / shipped pair | Verdict |
|---|---|---|
| `CHR_PX_HVY_TS_M_V01_7c71cfba6f4e08f7.glb` | `Human_Torso_SlotDef` → `px_heavy_assets_all.bundle` / `CHR_PX_HVY_TS_M_V01` | **`Outcome = ByName`**, `BY NAME - your weights will be used`, **zero diagnostic rows** |
| `CHR_PX_HVY_RL_M_V01_559e4dcb43d8484b.glb` | `Human_RightLeg_SlotDef` → `…/CHR_PX_HVY_RL_M_V01` | **`ByName`**, same header, **zero rows** |
| `CHR_PX_HVY_LL_M_V01_0fa9bde0c679e665.glb` | `Human_LeftLeg_SlotDef` → `…/CHR_PX_HVY_LL_M_V01` | **`ByName`**, same header, **zero rows** |

No transform flag, no armature complaint, no added-bone complaint on any of the three. The
un-applied-transform export is not merely usable — it is the CORRECT one, and the files currently in
`Wizard.ApocDesignation\Content\Meshes\` are the broken ones (note the sizes differ: raw torso
4 675 544 B vs project torso 10 237 696 B — they are different exports, not the same file).

Live preview on the standing prototype confirmed it in the engine:
`preview: skinned BY NAME onto the target's own 10 bones, carrying the file's own weights (bind poses
from the shipped mesh, 10 joints matched, order remapped; vertex 65 is shared, weight0=0.947)` for the
torso, and the same sentence with `vertex 0 … weight0=0.609` for the left leg.

## Visual

`Human / PX_HeavyStarting` on the bench platform, camera pinned at `YawTarget 180 / PitchTarget 0 /
ZoomTarget 0.85` for both frames of the pair. Every capture also wrote a `.scene.png` — the 3D is in
the `.scene.png` (Renderforge's upscaler renders `Camera.main` into a targetTexture, so the primary
PNG carries only the IMGUI layer).

| File | What it shows |
|---|---|
| `apoc-torso-A-stock.scene.png` | stock PX Heavy torso — bulky back plate, round vents |
| `apoc-torso-B-raw.scene.png` | the SAME frame with the raw Apoc torso previewed — slimmer chest, different shoulder, orange trim. Unmistakably changed, correctly posed, correctly skinned |
| `apoc-after-raw-torso.scene.png`, `apoc-after-raw-leftleg.scene.png` | full-body, raw torso / raw left leg previewed |
| `apoc-before-front.scene.png`, `apoc-before-back.scene.png` | stock baseline, yaw 180 / yaw 0 |
| `apoc-lifecycle-running.png` | the LIFECYCLE panel with all five rows |

Scratchpad `E:\Temp\claude\E--DEV-PhoenixPoint-ContentTool\703f4713-9cda-496f-8354-1515702f87f2\scratchpad\`.

**Only the Doctor's live preview shows the replacement — the baked path never got there.** A rendered
proof through Apply/Verify is still owed and needs the project's own meshes fixed first.

## Observations (not defects, recorded because this run is where they were seen)

- **`Validate` PASSes a project whose every replacement row will be refused or degraded.** Validate is
  manifest shape + key; it does not preflight mesh armature or texture placement. An author reading
  only the Validate row learns nothing about the four problems below it. Whether Validate should reach
  that far is a design question, not a bug.
- The two traps above (T1, T2) are both design-correct and both cost real time here. T2 in particular
  is not spelled as a precondition in `2026-09-05-lifecycle-dashboard-plan.md` §Task 8 — a seam-driven
  `Run` from the menu looks exactly like a hang.
- `FitBench.ShowPrototype` takes handles, so PPCLI needs the `{"$h":"h:4:53"}` envelope; a bare string
  is refused with `code:"overload"`. Expected, noted for the next driver.
- **No PPCLI defect was hit**, so nothing was appended to `PPCLI\ISSUES.md`.

## Bench state at exit

`ct_bench close` → `ct_bench closed - the screen you came from was never left, so it is still there.`
Doctor preview reverted first. Instance2 stopped path-filtered
(`Get-Process PhoenixPointWin64 | Where-Object { $_.Path -like 'D:\PP-Instance2\*' } | Stop-Process`,
never by name); **no `PhoenixPointWin64` process anywhere afterwards**.
`D:\PP-Instance2\Mods\PPBridge\ppcli-enabled` deleted.
`D:\PP-Instance2\Mods\Wizard.ApocDesignation\` LEFT in place.

**The project was deliberately left NOT activated** in `…\Steam\76561197996210592\Options.jopt` (20
entries → restored to the original 19; byte copy kept as `Options.jopt.bak-apocd`). Reason: T1 — with
it activated, every launch of Instance2 spends minutes re-baking two ~120 MB bundles into a failure and
poisons the session for the dashboard. To look at it in game, add `"Wizard.ApocDesignation"` to
`MOD_ACTIVATED` and bump the count in `ArrayDimensions.CollectionValues` to match — or tick it in the
in-game mod manager.

## What the author has to do

1. Re-export `CHR_PX_HVY_TS_M_V01` **with** an Armature modifier and vertex groups — or just use
   `CHR_PX_HVY_TS_M_V01_7c71cfba6f4e08f7.glb`, which already binds by name.
2. Delete the added bones `#L.UpLeg_Roll_1_Addon` (right leg) and `#L.Foot_Toes_Addon` (left leg) and
   re-export — or use the two raw leg GLBs, which have neither.
3. Move `Content\Meshes\materials\RR_soldier_albedo.png` to `Content\Textures\RR_soldier_albedo.png`.

All three raw files under `APOCD GLBs for content tool without apply tranforms\` already satisfy 1 and 2.
