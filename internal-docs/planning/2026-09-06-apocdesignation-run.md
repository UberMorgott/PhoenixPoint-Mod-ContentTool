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

---

# Run 3 — corrected sources, 2026-09-06

The same bench (`D:\PP-Instance2`, profile `76561197996210592`, project NOT activated), ContentTool at
HEAD `559c2d1`. Deployed DLL verified current rather than rebuilt: `D:\PP-Instance2\Mods\ContentTool\
ContentTool.dll` 1 974 784 B / 06.09.2026 17:02:27 is byte-for-byte the `bin\Release` output, and no
`src\*.cs` is newer. Launch `-mods -logFile D:\PP-Instance2\ct-apocd3.log`, PID 30900,
`build=3068ae67`, never `stale`. Recipe from run 2 followed exactly: start-campaign (37 steps, 14.8 s)
→ `ct_bench open` → `FitBench.tab=2` → `Open` → `Run`. Nothing parked.

## What was changed in the INSTANCE2 COPY only

| Change | Why |
|---|---|
| `Content\Meshes\CHR_PX_HVY_{TS,RL,LL}_M_V01.glb` ← the three raw GLBs, renamed (hash suffix dropped); old files kept as `*.glb.broken` beside | run 2 proved the raw export is the correct one |
| `Content\Textures\RR_soldier_albedo.png` ← **COPIED** from `Content\Meshes\materials\` | P1's placement rule. **Copied, not moved**: every `Content\Meshes\materials\*.mat.json` names `"file":"materials/RR_soldier_albedo.png"` relative to the mesh folder, so a move breaks all 16 sidecars |
| `ppcontent.json` | **untouched** — but P1 now says the texture ROW itself is wrong, see below |

The repo folder and the raw-GLB folder were not written to.

## Stage verdicts, verbatim

| Stage | freshness / outcome / starts | Verdict |
|---|---|---|
| Validate | stale / **pass** / 1 | `Validate: PASS - 'Wizard.ApocDesignation' - key 40816436338c15798cc9d8bf5b2eee164eb9e329.` (key moved — sources changed) |
| Bake | stale / **fail** / 1 | 9 811 chars, `ct_project: 2 FAILURE(S)` — rows below |
| Apply | stale / **fail** / 1 | 10 203 chars; it re-baked (`…were built from a different project, game build or ContentTool format - re-baking them`) and ended `NOT APPLIED: patching the shipped bundle(s) reported 2 failure(s), named in the P0/REFUSED line(s) above; nothing was installed and no copy was marked current.` `installation` empty |
| Verify | stale / none / **0** | never entered |
| Package | stale / none / **0** | never entered |

Restart required: header `restartRequired:true` from the geoscape load, but **irrelevant this run — no
Apply ever completed**. Bundles served: **none**. Package dir: none. `Run("Apply")` was accepted
(`runId 3`, `refusal:null`) rather than refused — the admission gate lets Apply run after a failed
Bake and the producer re-bakes; the refusal is the producer's, not the gate's.

Panel proof `apoc2-lifecycle-run3.png` (+ `.scene.png`) — Validate pass / Bake fail / Apply fail /
Verify none / Package none, `Session Ready. restart required session block: Wizard.ApocDesignation`.

**Wire trap:** the seam clips a section at ~2 000 chars (`truncated:true`) and this run's verdict never
reached the game log, so the full text was taken from `connect console ct_project Wizard.ApocDesignation`,
which returns `truncated:false` — that is the way to read a long verdict. Saved to scratchpad `bake3.txt`.

## The three meshes are FIXED — the raw GLBs work

`patch px_heavy_assets_all.bundle: mesh 'CHR_PX_HVY_{TS,RL,LL}_M_V01' <- … - skinned BY NAME onto the
target's own 10 bones, carrying 4 of the file's own influences per vertex` for all three. Read-back:
`P4 PASS`, `P4-ctl-shipped PASS`, `P4-bytes PASS`, `P5 PASS` (bindposes=10, rootHash=2424243207,
boneMax 9/8/9, inRange=yes), `P6 VOID` on each. No armature complaint, no added-bone complaint, no
nearest-bone anywhere. Torso verts 5 566 → 17 561, RL 3 234 → 13 222, LL 3 241 → 12 599.

## The two failures that remain — both are in the author's own files

**F1 — the torso GLB carries a 1-triangle stray part, and that is a COUNTED failure.**

> `P4 WARN chr_px_hvy_ts_m_v01 part 1 of 2 has only 1 triangle while part 2 has 15647. The game paints
> part N with the target's material N, so part 1 (1 triangle) -> material 'CHR_PX_HVY_TS_M_GOLD_V02 or
> CHR_PX_HVY_TS_M_XMAS_V02 or CHR_PX_HVY_TS_M_V01 (varies by renderer variant)', part 2 (15647
> triangles) -> material 'CHR_PX_HVY_SHD_M_GOLD_V02 or CHR_PX_HVY_SHD_M_XMAS_V02 or
> CHR_PX_HVY_SHD_M_V01 (varies by renderer variant)'. A part that small is almost always a leftover
> shard, and every part after it takes the material meant for the part before - which is why your real
> geometry is painted wrongly. In Blender select the mesh, Edit Mode, select all (A) and Mesh > Merge >
> By Distance, or assign every face to ONE material slot, then re-export - or order the parts to match
> the target's materials. Baked anyway; nothing was skipped.`

`ProjectBake.cs:1827`–`:1828`: the WARN prints and then `if (suspect) failures++`. So the row bakes but
the RUN fails, and a failing run installs nothing. The Doctor never saw this in run 2 — its preview
does not compare part order against the target's material list, so "zero diagnostic rows" and this
warning are both true. **This is the single thing standing between the project and a served render.**

**F2 — the texture row names an asset that does not exist.** The placement fix worked (the file is
imported, `1 texture(s)`); the refusal moved on to the TARGET:

> `P1 REFUSED target 'RR_soldier_albedo' is not a Texture2D in px_assault_assets_all.bundle - no
> Texture2D named 'RR_soldier_albedo' in unity=2019.4.31f1 assets=1735 cldbTypes=320 - list the names
> it does hold with: ct_list assets px_assault_assets_all.bundle Texture2D`

`RR_soldier_albedo` is the author's OWN png stem. `asset` must name a **shipped** Texture2D; `texture`
is the stem of his png. Real names, read live with `ct_list` — `px_assault_assets_all.bundle` holds
only `CHR_PX_ASS_*` / `CHR_PX_OP_*` albedos, and the PX **Heavy** ones live in the other bundle:
`CHR_PX_HVY_TS_M_V01_Albedo`, `CHR_PX_HVY_SHD_M_V01_Albedo`, `CHR_PX_HVY_ARM_M_V01_Albedo`,
`CHR_PX_HVY_Legs_M_V01_albedo`, `CHR_PX_HVY_HG_M_V01_Albedo` (note the capital `A` on the `_V01_Albedo`
ones). Not corrected on the bench: which one he means is his decision, not a guess to make for him.

Both bundle copies were still written (`px_heavy_assets_all.bundle` 124 027 774 B,
`px_assault_assets_all.bundle` 116 009 227 B) and each carries `PARTIAL … 1 row(s) above were REFUSED
and the copy was rewritten anyway`, plus `Dist\ApocDesignation.bundle` 2 902 086 B with
`TEX PASS assets/wizard.apocdesignation/textures/rr_soldier_albedo -> 2048x2048 RGBA32 px[0,0]=79,77,60,255`.

## Visual — NOT delivered, and why

No after-screenshot of a PX Heavy wearing the replacements exists, because nothing could be served:
`ct_route7 apply Wizard.ApocDesignation` (the dev shortcut, run as the last resort) ends with the same
`NOT APPLIED: … nothing was installed and no copy was marked current.` A served render needs F1 fixed
first. The Doctor's live preview cannot substitute for a full set either — `ModelDoctor.PickTarget`
calls `Revert()` before arming the next slot (`ModelDoctor.cs:160`, `:183`), so only ONE slot can be
previewed at a time; run 2's `apoc-torso-B-raw.scene.png` remains the best picture of the new torso.

## Observations

- `Run("Apply")` after a failed Bake is ADMITTED and re-bakes from scratch (~90 s) instead of being
  refused on the spot. Not a defect — but the fastest way to burn two minutes learning nothing new.
- `PARTIAL … 1 row(s) above were REFUSED` counts the torso row, which was **not** refused: its own line
  says `Baked anyway; nothing was skipped`. Wording only; the count is right, the verb is not.
- **No PPCLI defect** — nothing appended to `PPCLI\ISSUES.md`. One usage note: `connect call` takes
  `op:"invoke"` for a method; `op:"call"` is refused with `code:"op"`.

## Bench state at exit

`ct_bench close` → `ct_bench closed - the screen you came from was never left, so it is still there.`
Instance2 stopped path-filtered (`Where-Object { $_.Path -like 'D:\PP-Instance2\*' }`); no
`PhoenixPointWin64` process anywhere afterwards. `ppcli-enabled` deleted. The project stays on the
bench with the CORRECTED sources in place (raw GLBs + `Content\Textures\RR_soldier_albedo.png`, old
meshes kept as `*.glb.broken`) and **NOT activated** in `Options.jopt` (0 occurrences) — activation
would serve nothing anyway, since Apply installed nothing, and it would re-run the failing bake at
every launch (T1).

## What the author has to do — the exact file-level changes in HIS repo folder

1. **Meshes (done, verified):** replace `Content\Meshes\CHR_PX_HVY_TS_M_V01.glb` /
   `_RL_M_V01.glb` / `_LL_M_V01.glb` with `CHR_PX_HVY_TS_M_V01_7c71cfba6f4e08f7.glb` /
   `CHR_PX_HVY_RL_M_V01_559e4dcb43d8484b.glb` / `CHR_PX_HVY_LL_M_V01_0fa9bde0c679e665.glb` from
   `APOCD GLBs for content tool without apply tranforms\`, renamed to the plain names.
2. **Torso, in Blender — the blocker.** `CHR_PX_HVY_TS_M_V01_7c71cfba6f4e08f7.glb` exports 2 parts,
   the first holding 1 triangle. Edit Mode → select all (A) → `Mesh > Merge > By Distance`, or put
   every face in ONE material slot; re-export. Until this is done the bake reports a failure and
   **nothing is ever installed**.
3. **Texture file:** put a copy of `RR_soldier_albedo.png` at `Content\Textures\RR_soldier_albedo.png`.
   Keep the one under `Content\Meshes\materials\` — the `.mat.json` sidecars point at it by that path.
4. **Texture row in `ppcontent.json`:** `"asset": "RR_soldier_albedo"` names nothing that exists.
   Point it at the shipped Texture2D to overwrite and fix the bundle, e.g.
   `{ "bundle": "px_heavy_assets_all.bundle", "asset": "CHR_PX_HVY_TS_M_V01_Albedo", "texture": "RR_soldier_albedo" }`
   — pick the target from the `ct_list` names above.

---

# Run 4 — suspect part as warning, 2026-09-06

Same bench (`D:\PP-Instance2`, profile `76561197996210592`), ContentTool at HEAD `380f4ab`, REBUILT:
`dotnet build -c Release` → `bin\Release\ContentTool\ContentTool.dll` 1 978 368 B / 06.09.2026 18:58:45,
SHA-256 `CDFEB536CCC6D2C7244B123547882613507D71F4E443A31F27B39D6F70EDBE09`, and `deploy.ps1` put a
BYTE-IDENTICAL file at `D:\PP-Instance2\Mods\ContentTool\ContentTool.dll` (hashes compared, same
length and timestamp). PPBridge not redeployed — `build=3068ae67` in every reply, never `stale`.
Three hand launches, each gated on `connect state` answering first: `ct-apocd4.log` PID 2272,
`ct-apocd4b.log` PID 34544, `ct-apocd4c.log` PID 9528. Project sources unchanged from run 3 (raw
GLBs + `Content\Textures\RR_soldier_albedo.png`).

## R39 probe — `Validate` is NOT covered by it, `All` is

From the MAIN MENU, bench never opened, straight after `Open`:

| Press | Reply |
|---|---|
| `Run("Validate")` | **ADMITTED** — `{"ok":true,"runId":1,"refusal":null}`, and it really ran: `Validate: PASS - 'Wizard.ApocDesignation' - key 40816436338c15798cc9d8bf5b2eee164eb9e329.` |
| `Run("All")` | **REFUSED** — `{"ok":false,"runId":0,"refusal":"Lifecycle: All blocked; its main-thread work has to run behind an OPEN, painted panel. Open the bench (ct_bench open, from a loaded geoscape) and select the LIFECYCLE tab, then run it again."}` |

By design: `LifecycleState.NeedsPaint` (`LifecycleState.cs:743`) is `Bake || Apply || Verify || All`,
so Validate never sees R39 and never parks — it has no blocking main segment. The trap-T2 case
(`Run("All")` from the menu, which used to sit at `parkedForPaint:true` forever) is now a one-line
refusal that names the way in.

## Doctor — the suspect part is now visible BEFORE the bake

`FitBench.tab = 1`, `ShowPrototype(CHR_Human_Rig_Ready, "Human / PX_HeavyStarting")`, `SlotTargets()`
→ 10 targets, `Doctor.PickTarget(Human_Torso_SlotDef)`, `Doctor.PickFile(D:\PP-Instance2\Mods\
Wizard.ApocDesignation\Content\Meshes\CHR_PX_HVY_TS_M_V01.glb)`.

**Header, verbatim:** `BY NAME - your weights will be used (1 warning(s) below)`
**Rows: 1**, `Code = SubmeshMaterials`, `Severity = Warning`, `Side = File`:

> `CHR_PX_HVY_TS_M_V01.glb part 1 of 2 has only 1 triangle while part 2 has 15647. The game paints
> part N with the target's material N, so part 1 (1 triangle) -> material 'CHR_PX_HVY_TS_M_V01',
> part 2 (15647 triangles) -> material 'CHR_PX_HVY_SHD_M_V01'. A part that small is almost always a
> leftover shard, and every part after it takes the material meant for the part before - which is why
> your real geometry is painted wrongly. In Blender select the mesh, Edit Mode, select all (A) and
> Mesh > Merge > By Distance, or assign every face to ONE material slot, then re-export - or order the
> parts to match the target's materials. Baked anyway; nothing was skipped.`

Run 2's "zero diagnostic rows" for the same file is gone (`cefde46`). The Doctor names the LIVE
renderer's two materials; the bake names the three renderer variants it can be (`GOLD/XMAS/V01`) —
same rule, different amount of context, both correct.

## Stage verdicts — pass A, the project EXACTLY as the author has it

`ct_bench open` → `FitBench.tab = 2` → `Open` → `Run("All")`.

| Stage | freshness / outcome / starts | Verdict |
|---|---|---|
| Validate | stale / **pass** / 1 | `Validate: PASS - 'Wizard.ApocDesignation' - key 40816436338c15798cc9d8bf5b2eee164eb9e329.` |
| Bake | stale / **fail** / 1 | 9 543 chars — the torso row is now a WARNING, the texture row is still the one refusal |
| Apply | stale / **fail** / 1 | 9 935 chars, `installation` EMPTY |
| Verify | stale / none / **0** | never entered |
| Package | stale / none / **0** | never entered |

**The torso row baked.** `patch px_heavy_assets_all.bundle: mesh 'CHR_PX_HVY_TS_M_V01' <-
chr_px_hvy_ts_m_v01 verts=17561 indices=46944 … - skinned BY NAME onto the target's own 10 bones,
carrying 4 of the file's own influences per vertex`, followed by `P4 WARN … Baked anyway; nothing was
skipped.` All three meshes bake BY NAME; no nearest-bone, no armature complaint anywhere.

**The only failure left is F2, the texture ROW.** Verbatim, read with `connect console ct_project
Wizard.ApocDesignation` (`truncated:false`, saved as scratchpad `bake4.txt`):

> `P1 REFUSED target 'RR_soldier_albedo' is not a Texture2D in px_assault_assets_all.bundle - no
> Texture2D named 'RR_soldier_albedo' in unity=2019.4.31f1 assets=1735 cldbTypes=320 - list the names
> it does hold with: ct_list assets px_assault_assets_all.bundle Texture2D`

and the run ends `ct_project: 1 FAILURE(S) - 1 warning(s), baked anyway` — the new summary line
counts the two kinds apart. Apply's own last line (`ct_route7 apply Wizard.ApocDesignation`, saved as
`apply4.txt`):

> `NOT APPLIED: patching the shipped bundle(s) reported 1 failure(s), named in the P0/REFUSED line(s)
> above; nothing was installed and no copy was marked current.`

**So `31cab05` did exactly what it says: run 3's 2 failures are now 1 failure + 1 warning, the torso
bakes, and the project is one bad `asset` string away from installing.** Nothing else changed.

## Stage verdicts — pass B, the texture row PARKED (bench copy only)

The goal of run 4 was a served bundle and a render, and F2 is the author's decision, not ours. So the
BENCH copy's `ppcontent.json` had the fourth (texture) replacement moved aside, byte copy kept beside
it as `ppcontent.json.run4bak`. The three mesh rows are untouched; the repo copy and the raw-GLB
folder were never written to.

`ct_route7 apply` in pass A had marked the project failed for the session (`'Wizard.ApocDesignation'
failed to bake earlier in this session - not baking it again.` — T1's refusal, from the dev shortcut
rather than a startup bake), so the game was restarted before pass B.

| Stage | outcome / starts | Verdict |
|---|---|---|
| Validate | **pass** / 1 | `Validate: PASS - 'Wizard.ApocDesignation' - key f573240953f4b2756bf98df78717a5324d609392.` (key moves with the manifest) |
| Bake | **pass** / 1 | 8 690 chars, 3 replacement(s), all three meshes BY NAME, the `P4 WARN` torso row still printed |
| Apply | **pass** / 1 | 769 chars — `installing 1 patched copy(ies) as 'Wizard.ApocDesignation'` / `REFUSED: restart required: px_heavy_assets_all.bundle is already loaded (as '4e130b87ae4219d20db6fde21aa06aaa.bundle'). Unity rejects a second bundle of the same identity, and unloading the game's copy would pull it out from under live objects. Restart, then enable 'Wizard.ApocDesignation'.` / `0/1 bundle(s) redirected LIVE for 'Wizard.ApocDesignation' - nothing was written to the game installation`; `installation: restart required` |
| Verify | **pass** / 1 | after the restart — `Verify: PASS - load-back gates passed; 1 of 1 declared target(s) served from this project's copies for 'Wizard.ApocDesignation'.` |
| Package | **pass** / 1 | `PACKAGED 31 file(s), 40809953 B into C:\Users\Morgott\AppData\Local\ContentTool\Packages\Wizard.ApocDesignation\20260906-191239-2` |

Serving needed the S1 restart AND activation: `"Wizard.ApocDesignation"` was added to the profile's
`MOD_ACTIVATED` (19 → 20, count mirrored in `ArrayDimensions.CollectionValues`; byte copy kept as
`Options.jopt.bak-run4`) and Instance2 relaunched. With the bake now PASSING, T1 does not bite — the
startup bake succeeds and the dashboard opens clean. After the restart the header reads
`restartRequired: false`.

**Bundles served: `px_heavy_assets_all.bundle`** (1 of 1 declared), from
`…\ContentTool\Patched\<hash>\Wizard.ApocDesignation`. **Package dir:**
`C:\Users\Morgott\AppData\Local\ContentTool\Packages\Wizard.ApocDesignation\20260906-191239-2`
(31 files, 40 809 953 B).

## Visual — the render that runs 2 and 3 could not produce

`Human / PX_HeavyStarting` standing on the bench platform with the bundle SERVED, camera pinned the
same way as the run-2 baseline (`YawTarget/Yaw 180`, `PitchTarget/Pitch 0`, back frame at yaw 0).
The 3D is in the `.scene.png` as always.

| File | What it shows |
|---|---|
| `apoc4-front.scene.png` | PX Heavy wearing the author's torso + both legs, yaw 180 |
| `apoc4-back.scene.png` | same soldier, yaw 0 |
| `apoc4-front-close.scene.png` | tighter front frame |

Against `apoc-before-front.scene.png` (identical framing, stock soldier) the change is unmistakable:
the stock torso's tan/olive camo plate with the row of circular back vents is gone, replaced by a
smooth dark-grey chest and back plate with an orange stripe and a red-orange helmet-height accent;
both legs are new geometry too — segmented thigh and shin plates instead of the shipped rounded ones.
Torso and both legs changed; head, arms and jetpack are untouched slots and look identical.

**The 1-triangle shard itself is invisible** (one triangle at this scale is sub-pixel), but its
CONSEQUENCE is on screen: the whole torso is painted in ONE dark material rather than the shipped
tan camo, which is what "part 2 takes the material meant for part 1" produces. It reads as a plausible
alternate colourway rather than as corruption, so an author could easily ship it without noticing —
which is exactly why the WARNING earns its place.

## Observations

- **`Run("Validate")` is reachable from the main menu** and really validates. Useful: the cheapest
  possible smoke test of a project needs no campaign, no bench and no tab.
- **`ct_route7 apply` poisons the session the same way an activated startup bake does.** Pass A used
  it to read the untruncated Apply tail, and the next `Run("All")` came back with the T1 refusal
  (`failed to bake earlier in this session`) even though `ppcontent.json` had changed in between. The
  block is keyed on the project id, not on its content — a restart is the only way out. Prefer
  `connect console ct_project <id>` for reading a long verdict: same text, no session block.
- **The wire clip is still the thing to plan around.** `Snapshot("Bake")` returned `truncated:true`
  at 9 543 bytes and `Snapshot("log")` clips from the FRONT, so neither carries the summary line.
  `connect console ct_project <id>` returns `truncated:false` and is the way to read a whole verdict.
- `PARTIAL … 1 row(s) above were REFUSED and the copy was rewritten anyway` still counts correctly
  (1, the texture row) — run 3's wording complaint is resolved by the torso no longer being a failure.
- **No PPCLI defect** — nothing appended to `PPCLI\ISSUES.md`. Usage notes for the next driver:
  an INSTANCE call needs `"target":{"$h":"h:4:N"}` (a bare `"h":…` is refused `code:"args"`,
  `call needs "type" (static) or "target" (instance)`); `items` rows carry only `h`/`type`, so a name
  has to be read per row with a follow-up `get`.

## Bench state at exit

`ct_bench close` → `ct_bench closed - the screen you came from was never left, so it is still there.`
Instance2 stopped path-filtered (`Where-Object { $_.Path -like 'D:\PP-Instance2\*' }`, never by name);
no `PhoenixPointWin64` process anywhere afterwards. `D:\PP-Instance2\Mods\PPBridge\ppcli-enabled`
deleted.

**The project is ACTIVATED** in `…\Steam\76561197996210592\Options.jopt` (20 entries; original kept as
`Options.jopt.bak-run4`) and **the patched `px_heavy_assets_all.bundle` is left SERVED** — that is what
makes the render above reproducible on the next launch. The bench copy's `ppcontent.json` is the
3-row (mesh-only) manifest; the author's original 4-row file is beside it as `ppcontent.json.run4bak`.

To put the bench back exactly as the author has it:
`Copy-Item 'D:\PP-Instance2\Mods\Wizard.ApocDesignation\ppcontent.json.run4bak' 'D:\PP-Instance2\Mods\Wizard.ApocDesignation\ppcontent.json' -Force`
— and then DEACTIVATE it again (restore `Options.jopt.bak-run4`), because with the texture row back the
startup bake fails and T1 poisons every session.

## What the author still has to fix — TWO things, both in his own files

1. **The texture ROW (F2) — the only thing blocking a clean install.** `"asset": "RR_soldier_albedo"`
   names his own png stem; `asset` must name a **shipped** Texture2D. Real candidates, read live with
   `ct_list assets px_heavy_assets_all.bundle Texture2D`: `CHR_PX_HVY_TS_M_V01_Albedo`,
   `CHR_PX_HVY_SHD_M_V01_Albedo`, `CHR_PX_HVY_ARM_M_V01_Albedo`, `CHR_PX_HVY_Legs_M_V01_albedo`,
   `CHR_PX_HVY_HG_M_V01_Albedo`. E.g.
   `{ "bundle": "px_heavy_assets_all.bundle", "asset": "CHR_PX_HVY_TS_M_V01_Albedo", "texture": "RR_soldier_albedo" }`.
   Note the bundle changes too: the PX **Heavy** albedos are NOT in `px_assault_assets_all.bundle`.
2. **The torso's 1-triangle part (F1) — no longer fatal, still wrong on screen.** The run installs and
   renders with it, but the torso is painted with the material meant for the shoulder pad. In Blender:
   Edit Mode → select all (A) → `Mesh > Merge > By Distance`, or put every face in ONE material slot,
   then re-export.

Everything else is done: the three raw GLBs from `APOCD GLBs for content tool without apply
tranforms\` bind BY NAME, and `Content\Textures\RR_soldier_albedo.png` is where P1 wants it.

---

# Data fix (2026-09-06) — both remaining defects fixed FOR the author, offline

No game was launched. **Blender is not installed on this machine** (`Get-Command blender` → not found,
no `C:\Program Files\Blender Foundation`), so the torso was fixed with the tool's own code instead:
three offline arms added to `tests\ObjCodecTests\Program.cs`, which already links every production
type this needs (`GlbDocument`, `MeshFields`, `SkinFields`, `AssetIndex`, `ReplacementPreflight`,
`StageValidate`).

| Arm | What it does |
|---|---|
| `--dropshard <in.glb> <out.glb>` | deletes every primitive drawn by ≤8 triangles while a bigger one survives — `MeshFields`' own shard rule (`ShardTriangles`), applied instead of only reported. Container surgery through `GlbDocument`, so weights, skin, nodes and BIN are untouched |
| `--fit <bundle> <Mesh> [file.glb]` | the shipped target read offline: material slots in PAINT order, the Texture2D each of those materials samples, bind poses and bone names — then `ReplacementPreflight` on the file, the Doctor's own verdict |
| `--uses <bundle> <Texture2D>` | what a texture row would actually repaint: the shipped texture's size/format, every material sampling it, every mesh those materials draw |
| `--validate <projectDir> [bundle…]` | the dashboard's Validate stage on a real folder, offline |

## F1 — the torso

`--dropshard` on `APOCD GLBs…\CHR_PX_HVY_TS_M_V01_7c71cfba6f4e08f7.glb`:
`dropping part 1 of 2 (1 triangle(s)) from mesh 'CHR_PX_HVY_TS_M_V01'` → 4 675 412 B (was 4 675 544 B).

**Which slot is the body**, read off the shipped bundle (`--fit … CHR_PX_HVY_TS_M_V01`):
`2 material slot(s), 10 bind pose(s), 10 named bone(s)`, **slot 0 = `CHR_PX_HVY_TS_M_GOLD_V02 or
CHR_PX_HVY_TS_M_XMAS_V02 or CHR_PX_HVY_TS_M_V01`** (the torso material — `_MainTex`
`CHR_PX_HVY_TS_M_V01_Albedo`), slot 1 = the `CHR_PX_HVY_SHD_*` shoulder-pad material. So the body has
to be primitive 1, which is exactly what dropping the leading shard leaves.

Preflight on the fixed file, verbatim:

> `CHR_PX_HVY_TS_M_V01.glb: BY NAME - your weights will be used | outcome ByName | 1 row(s)`
> `  [Info/File] SubmeshMaterials: CHR_PX_HVY_TS_M_V01.glb: part 1 (15647 triangles) -> material 'CHR_PX_HVY_TS_M_GOLD_V02 or CHR_PX_HVY_TS_M_XMAS_V02 or CHR_PX_HVY_TS_M_V01 (varies by renderer variant)'`

The warning is gone — the one row left is INFO, the mapping statement, and it names the BODY material.
Both legs re-checked the same way: `BY NAME …| outcome ByName | 0 row(s)` each.

## F2 — the texture rows

`--uses` on the candidates (all `2048x2048 format=10 (DXT1) mips=12 streamed=True`):

| Texture2D | materials | meshes those materials draw |
|---|---|---|
| `CHR_PX_HVY_TS_M_V01_Albedo` | `CHR_PX_HVY_TS_M_V01` `_MainTex` | `CHR_PX_HVY_TS_M_V01` (replaced), `CHR_PX_HVY_TS_F_V01` |
| `CHR_PX_HVY_Legs_M_V01_albedo` | `CHR_PX_HVY_Legs_M_V01` `_MainTex` | `CHR_PX_HVY_{LL,RL}_M_V01` (both replaced), `CHR_PX_HVY_{LL,RL}_F_V01` |
| `CHR_PX_HVY_SHD_M_V01_Albedo` | `CHR_PX_HVY_SHD_M_V01` `_MainTex` | `CHR_PX_HVY_TS_M_V01`, `CHR_PX_HVY_TS_F_V01` |

So the two rows written are **`CHR_PX_HVY_TS_M_V01_Albedo`** and **`CHR_PX_HVY_Legs_M_V01_albedo`**,
both in `px_heavy_assets_all.bundle`, both sourced from the author's one `RR_soldier_albedo` png (his
16 `.mat.json` all name it, and the GLB's own material is `rr_source_atlas` — one atlas for the set).
Two rows with the same `texture` stem are legal: `Manifest.Validate` dedups on `bundle+asset+kind`
(`Manifest.cs:214-222`), not on the source file.

**`CHR_PX_HVY_SHD_M_V01_Albedo` deliberately NOT written.** With the shard gone the torso replacement
has ONE part, so it paints slot 0 only and the SHD material now draws nothing on the male torso — the
only geometry that row could still repaint is the shipped FEMALE torso's shoulder pad, with an atlas
whose UVs were never meant for it.

**What the bake will do to those two textures:** it does not require parity. `FillTexture2D`
(`BundleBaker.cs:731-761`) rewrites the header from the png — `m_Width/m_Height` = the png's,
`m_TextureFormat = 4` (RGBA32), `m_MipCount = 1`, `colorSpace = 1`, `m_StreamData` cleared. The png is
2048×2048, same as both shipped textures, so UVs are safe; the cost is **12 mip levels lost** (some
shimmer at distance) and ~16 MB uncompressed in place of a streamed DXT1.

## Files changed (both copies; every replaced file kept beside as `.orig`)

`E:\DEV\PhoenixPoint\ContentTool\Wizard.ApocDesignation\` (the master copy now):
`Content\Meshes\CHR_PX_HVY_TS_M_V01.glb` (the de-sharded file), `_RL_`/`_LL_M_V01.glb` (the raw
exports), new `Content\Textures\RR_soldier_albedo.png` (copied, the `.mat.json` sidecars still need the
one under `Content\Meshes\materials\`), `ppcontent.json` (5 rows). Four `.orig` files beside them.

`D:\PP-Instance2\Mods\Wizard.ApocDesignation\`: the same fixed torso (old raw file → `.glb.orig`, the
author's original still `.glb.broken`) and the same `ppcontent.json` (previous 3-row bench manifest →
`.orig`, author's 4-row original still `.run4bak`). Legs and texture were already correct there.

`--validate` on both, offline: **`Pass - Validate: PASS - 'Wizard.ApocDesignation' - key
2c3185381500a7016b997cd49727ff179e0535ee.`** (repo copy) and the same PASS for the bench copy
(key `5cc12cda…`, the folders differ by the kept `.orig`/`.broken` files).

**Still owed:** a game run. Nothing here was baked, applied or rendered — the next in-game pass should
see 5 rows, 0 failures, and a torso painted with the body material instead of the shoulder pad's.
