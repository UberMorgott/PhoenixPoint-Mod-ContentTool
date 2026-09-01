# VERIFIED-DEMOS — what every shipped demo actually does, measured off the live engine

> **Historical measurement ledger.** The WeaponAdd rows below preserve dated 2026-08-27/28 runs;
> they predate the current three-model AR/Sniper/Sidearm manifest. Use
> `demos\WeaponAdd\ppcontent.json` and its README for the current recipe.

> One row per demo. The **probe** column is the exact property read, written down before the
> measurement per `METHODOLOGY.md`. **Shipped** and **modded** were taken in the SAME game run for
> every demo **except CustomCreature** — never against an expectation carried over from another
> session. CustomCreature is the one exception, and it says so in its own row: `ct_creature gate`
> picks one template per run, so its shipped-template control (R1) could not be captured in the same
> launch as its subject (R2). A log line saying "applied" is
> not evidence and does not appear here; every number below came back through a `call`, an
> `AKRESULT`/Wwise callback, or an engine decoder.
>
> Rig: `D:\PP-Instance2`, ContentTool `1.0.0.0 build=b078ff68`, PPBridge `build=9af3d28e`, driven
> through `PPCLI\ppcli.ps1 connect`. Three launches, 2026-08-27:
> **R1** 23:31 (menu → tactical), **R2** 23:43 (menu → tactical), **R3** 23:48 (menu → quit).
> All eight demos were enabled together with 13 other mods (21 loaded), **TFTV 1.1.4.5 included** —
> these readings are from a heavy mod stack, not a clean install. See the Health.Max note below.

## The matrix

| Demo | Rung | Probe (the exact property read) | Shipped value | Modded value | Verdict | Date |
|---|---|---|---|---|---|---|
| **WeaponMesh** (mesh) | route vii, bundle replace | `AddonSkinDataBase.GetPrefabAsset(E_SkinData [PX_AssaultRifle_WeaponDef].DefaultPrefab)` → `MeshFilter.sharedMesh.vertexCount` | **5771** verts / 8572 tris (`ct_extract mesh px_equipment_assets_all.bundle WPN_PX_RG_Assault_Rifle_T01_V01`, read off the untouched shipped bundle in the same run) | **5554** verts, `subMeshCount=1`, mesh name `WPN_PX_RG_Assault_Rifle_T01_V01` | **VERIFIED** | 2026-08-27 R1 |
| **WeaponMesh** (texture) | route vii | same prefab → `MeshRenderer.sharedMaterial.mainTexture` `.width/.format/.mipmapCount` | **2048×2048, fmt=10 (DXT1), mips=12** (`ct_extract tex`, shipped bundle, same run) | **1024×1024, `RGBA32`, mips=1**, name `..._albedo` | **VERIFIED** | 2026-08-27 R1 |
| **WeaponMesh** (icon) | def field write | `E_View [<weapon>].InventoryIcon.texture` `.name/.width/.format` | control `PX_LaserPDW_WeaponDef` → `UI_PX_WeaponIcon_Laser_PDW_INV` on `sactx-4096x4096-Uncompressed-UIAtlas_UI-c47c0ec5`, **4096 RGBA32** | `PX_AssaultRifle_WeaponDef` → unnamed standalone texture, **450 ARGB32** | **VERIFIED** | 2026-08-27 R1 |
| **ReplaceUiSounds** | Wwise media replace | `ct_sound probe <mediaId>` → `fDuration`, `mediaID`, streaming state; shipped side = `ct_sound status <mediaId>` on the untouched `.wem` | 18839791 **1200 ms**; 633458426 **3533 ms**; 940964934 **2231 ms** — all loose `.wem`, `vorbis` | 18839791 **340 ms**; 633458426 **444 ms**; 940964934 **601 ms** — all `mediaID` correct, all `streaming=false(MEMORY)` | **VERIFIED** | 2026-08-28 R6 |
| **MenuMusic** | Wwise media replace | `ct_sound probe 208540756` → posts `MainMenuMusicStart`=799408924; read `mediaID` + storage | on disk `208540756.wem` 3 687 722 B / **142 978 ms** / sha1 `82c123d0…`, untouched. FILE-control (unreplaced media 1013630856): `dur=15212ms` == on-disk 15212 ms, **`streaming=true(FILE)`** | `mediaID=208540756`, **`streaming=false(MEMORY)`**, served from the mod bank `bankId=4045954504` (**1 062 328 B**, `LoadBankMemoryCopy: AK_Success`); the YOE twin `423563089` likewise `MEMORY` against 284 164 ms on disk | **VERIFIED** (storage, not duration — see note) | 2026-08-28 R6 |
| **AddUiSounds** | Wwise media ADD | `AudioProbe.Post(GameController, <eventId>, 4000 ms)` → `playingID`, `mediaID`, `fDuration`, `AK_EndOfEvent` | no shipped equivalent. Control: unregistered event `4000000001` → **`playingID=0`, POST FAILED** | `1781464403` (`blip_rise`) → `playingID=6 mediaID=3338666241 dur=392 ms` **`endOfEvent=405 ms`** MEMORY; `2693404503` (`blip_fall`) → `playingID=7 mediaID=3338666240 dur=496 ms` **`endOfEvent=507 ms`** MEMORY | **VERIFIED** | 2026-08-28 R6 |
| **IntroVideo** (video) | Addressables key repoint | `ct_video resolve <key>` then `ct_video open <key>` → the engine's own `VideoPlayer` `frameCount`/`WxH` | control key `23b0f5ba…` (`Game_Intro_Cutscene`) → `StreamingAssets/StreamableCopiedAssets/Videos/GameIntro.webm`, **1934 frames 1920×1080** | key `e574fca8…` (`PP_Intro_Cutscene`) → `…/../../Mods/IntroVideo/Content/Videos/campaign_intro.webm`, **180 frames 1280×720** | **VERIFIED** | 2026-08-27 R1 |
| **IntroVideo** (sound) | Wwise media replace | `AudioProbe.Post(GameController, 1015492702 /* PP_Intro */)` → `mediaID`, `fDuration`, storage | on disk `908611677.wem` 3 192 625 B / **121 355 ms**, untouched. FILE-control same run: 1013630856 `dur=15212ms` `streaming=true(FILE)` | `mediaID=908611677`, **`dur=6034 ms`**, **`streaming=false(MEMORY)`** | **VERIFIED** | 2026-08-27 R3 |
| **QuitCutscene** | Addressables key ADD + Harmony trigger | `ct_video open <key>` frame count; then a real `PhoenixGame.FinishLevelAndQuitGame` and the game's own `HomeScreenView.ToCutsceneState` | key did not exist before the mod; every one of the 67 shipped `VideoPlaybackSourceDef`s resolves into `StreamingAssets` | key `6f3d8e3d…` → `…/Mods/QuitCutscene/Content/Videos/quit_outro.webm`, **90 frames 1280×720**; on quit: `prepared=True playing=True frameCount=90 length=3s 1280x720 playbackSource=QuitCutscene_Runtime`, then the process exited | **VERIFIED** | 2026-08-27 R3 |
| **CustomCreature** | new creature (own rig, own clips) | `ct_creature gate spider_demo_before customcreature` — 19 arms on a live spawned actor | control, same instrument on the first shipped candidate template (`Acidworm`/`Fireworm_1`, R1): animator `[Fireworm_idle_loop → Fireworm_move_loop]`, `Data.Strength=0` → **CONTENT-DEFECT, born dead**, `C1-melee FAIL` (no attack ability resolves), 2,32 tile/s | own hitbox `ct_hitbox`, own aim point `ct_creature_…_BashPoint`, `Data.Strength=4` → `Health.Max=60,0`, 3 health slots, bash `Fishman_12` **190,0 → 130,0**, spit **130,0 → 120,0** (4 → 5 statuses), walk 2,83 tiles in 0,69 s = **4,12 tile/s**, animator played **`cyborg_spider_spider_attack_1 / _attack_2 / _walk / _idle / _death`** | **VERIFIED** | 2026-08-27 R2 |
| **WeaponAdd** (defs) | def clone + tuning | `DefRepository.GetDef<WeaponDef>(guid)` → `SpreadDegrees`, `EffectiveRange` | donors, same run: `PX_LaserPDW` **2 / 20**, `PX_AssaultRifle` **1.6 / 25**, `SY_LaserPistol` **1.5 / 27** — all three still at their shipped values | `Morgott_VulturePDW` **3 / 13**, `Morgott_VultureAR` **2.4 / 17**, `Morgott_VultureSidearm` **2.25 / 18** | **VERIFIED** | 2026-08-27 R1 |
| **WeaponAdd** (models) | route iii, publish new keys | `ct_catalog verify` — the game's own Addressables resolving each published key | shipped catalog still carries **8232 keys**; control `02_Bodyparts/ALN_Fireworm_BodyAll_Ready.prefab`, published by nobody, still resolves to `ALN_Fireworm_BodyAll_Ready` | `…4b60` → GameObject **`sniper`**, `…4b61` → **`ar181`**, each out of `WeaponAdd.bundle`; forged external PPtr resolved to shader **`Standard`** (a dangling one reads `Hidden/InternalErrorShader`). The third key `…4b62` (`taupistol`) was **removed 2026-08-28** with the model — see R6 below | **VERIFIED** (key resolution only — for what each weapon actually *wears*, see the R5/R6 sections below) | 2026-08-27 R1 |

**`demos\HumanoidSoldier\` and `demos\ReplaceCharacterBody\` are not in this matrix.** Both now ship
their model, so the reason is no longer "there is nothing to spawn" — it is simply that no measured
in-game run exists for either. A row here needs a named instrument, a control read in the same run
and a subject reading, and neither has one yet. They stay out until one is measured, because a row
asserted from a bake log is exactly the carried-over expectation this file refuses.

**Not verified: none of the measurable demos.** Every demo with shipped content reproduced. Two measurements are weaker than the rest and say so
in place (MenuMusic's duration, and the CustomCreature control coming from a different launch than
its subject — see notes).

## Per-row notes

- **The FILE/MEMORY discriminator.** An unreplaced loose `.wem` probes as `streaming=true(FILE)`
  with `fDuration` exactly equal to the file's own header duration (measured twice: 15212 ms in R1
  and again in R3). Every replaced media probes as `streaming=false(MEMORY)`. That pairing is what
  makes "the engine is not reading the shipped file" a measurement rather than an inference.
- **MenuMusic's duration is not available.** `MainMenuMusicStart` returns
  `dur=0ms … endOfEvent=TIMEOUT` — the music event yields no duration callback, so the only
  discriminator for this row is `mediaID` + MEMORY-vs-FILE storage. The three UI sounds and the
  intro theme all *do* return durations, and those differ from the shipped file by 312 / 3077 /
  1317 / 115 321 ms respectively.
- **The AudioProbe slot is single and static, and it showed.** In R2, after three consecutive
  `ct_sound probe` calls, a fourth probe (the FILE control) came back
  `dur=NO-DURATION-CB mediaID=? streaming=?`. The same control on a fresh session (R3) was clean.
  Take the control FIRST, or take it in its own session.
- **CustomCreature's control is cross-launch.** `ct_creature gate` picks one template per run, so
  the shipped-template reading (R1, `Acidworm`) and the spider reading (R2) are from different
  launches of the same build. Everything the spider row asserts about *its own* content — clip
  names, hitbox name, aim-point name, stat values — is internally discriminating without the
  control; the control only shows the harness is capable of reporting failure, which it did
  (`C1-hp CONTENT-DEFECT`, `C1-melee FAIL` on the shipped template).
- **`ct_creature gate` must start from the main menu.** Issued a second time while a mission from a
  previous gate was already live it answered `C1 VOID - no arm ran` with a `NullReferenceException`
  and measured nothing. Relaunching and re-issuing it produced the full 19-arm run.
- **~~WeaponAdd's model keys are published but not yet worn.~~ REFUTED 2026-08-28, R5** — see *All
  three added weapons wear their own mesh* below. The R1 note read the demo's own README instead of
  measuring the def, and the README was stale. All three defs carried their own `SimpleSkinDataDef`
  pointing at their own key. **Two of them still do**: the third model was deleted for licence
  reasons on the same day (R6), and its weapon now measurably wears the donor's skin instead.

## A DOWNLOAD NEEDS NO BAKE — measured 2026-08-28, R4

Four demos told the player to run a bake before the mod would do anything. **All four were wrong**,
including the one expected to be the control. Rig: the same `D:\PP-Instance2`, ContentTool
`build=27c7b58b`, the four mod folders DELETED and re-deployed from the repo's shipped files only
(`meta.json`, `ppcontent.json`, `README.md`, `SOURCES.md`, `Content\`, `Icons\`, `Dist\`, the DLL) and
`%LocalLow%\...\ContentTool\Patched\morgott.demo.introvideo` moved aside, so nothing an earlier bake
wrote could still be on disk. **No `ct_sound bake` and no `ct_project` was issued in that launch.**

| Demo | The claim | Measured with NO bake | Why |
|---|---|---|---|
| **ReplaceUiSounds** | `NEEDS ONE BAKE` | 18839791 **1200 → 888 ms**, 633458426 **3533 → 456 ms**, 940964934 **2231 → 914 ms**, all `streaming=false(MEMORY)`. Control taken FIRST in the same run: unreplaced 1013630856 = **15212 ms `streaming=true(FILE)`** | its three `Dist\Sounds\*.bnk` are COMMITTED and `SoundLoad.LoadAll` loads every enabled mod's banks at init |
| **IntroVideo** (sound) | `SOUND NEEDS ONE BAKE` | `PP_Intro` (1015492702) → **`dur=6034ms mediaID=908611677 streaming=false(MEMORY)`**, against 121355 ms on disk | same route; `Dist\Sounds\908611677.bnk` is committed |
| **CustomCreature** | `BAKE FIRST` | `ct_creature gate spider_demo_before customcreature` → **all 19 arms PASS**: bash `Fishman_12` **190,0 → 130,0**, spit **130,0 → 120,0** (4 → 5 statuses), walk 2,83 tiles in 0,71 s = **3,98 tile/s**, death clip `cyborg_spider_spider_death`, `Health.Max=60,0` | `Dist\CustomCreature.bundle` is committed and `CreatureBuild.Build` reads it at mod enable |
| **AddUiSounds** — *the intended control* | `NEEDS ONE BAKE` (expected to be genuinely required, since it ships no `.bnk`) | **802143502 → `dur=1489ms mediaID=3338666240` MEMORY**; **3282871088 → `dur=653ms mediaID=3338666241` MEMORY** — identical to the R3 post-bake reading | **the hypothesis was wrong**: its bank is not in `Dist\Sounds` at all, it is packaged INSIDE the committed `Dist\AddUiSounds.bundle` and its own DLL loads it at enable (`AddUiSounds: bank 246920 B loaded as 432470233, 2 clip(s) on Alt+B`). The event IDs and clips changed in R6; the mechanism did not |

**The rule.** `ct_sound bake` / `ct_project` are AUTHORING commands. A demo is redistributed with its
bake OUTPUT committed, so the only thing a downloader does is enable the mod. The four descriptions
were rewritten accordingly; the caveat that survives is a different one (a Wwise bank cannot be
unloaded in-session, so switching a sound demo OFF needs a restart).

## What contradicts the existing documentation

1. **`demos\WeaponMesh\meta.json` — the player-facing Workshop description — names a command that
   no longer exists and describes a workflow the mod no longer uses.** It says to run
   `ct_route7 apply WeaponMesh` once and restart, that the model then "SURVIVES this mod being
   switched off", and to undo with `ct_route7 revert morgott.demo.weaponmesh`. Measured: the
   redirect installs itself at startup with no `apply` (`1/1 bundle(s) redirected LIVE`, R1), and
   `ct_route7 verify` answers `REMOVED: … wrote into your Phoenix Point installation and no longer
   exists`. `revert` is gone from the dispatcher for the same reason. A subscriber following that
   description gets an unknown-command error.
2. **`demos\MenuMusic\README.md` documents `ct_sound probe <mediaId>` as the way to "read duration
   / storage back".** It reads storage correctly and returns **no duration at all** for this demo's
   own media (`dur=0ms`, `endOfEvent=TIMEOUT`). The README should say the duration half does not
   apply to a music event.
3. **`ct_sound probe 908611677` cannot probe IntroVideo's own sound.** It answers
   `probe VOID bank Cinematics declares no event for 'PhoenixProject_Intro'`. The reachable route is
   posting the event the demo's README already names, `PP_Intro` = 1015492702 — which is how the
   6034 ms reading above was taken. IntroVideo's README lists `ct_sound bake` and `ct_video live`
   but no working verification command for the audio half.
4. **CustomCreature's health does not land where its own build log says.** `ppcontent.json` asks for
   `health: 40` and ContentTool's build line computes `Health.Max = Toughness 0 + 4 x 10,00 = 40`;
   the spawned actor measured **`Health.Max = 60,0`** from the same `Data.Strength=4`. TFTV was
   resident, and the most likely explanation is a TFTV strength→health multiplier — but that means
   the number ContentTool prints at bake time is not the number the game gives the creature under a
   mod stack, and neither document mentions it.
5. **Confirms, does not contradict, `PROVEN-FOUNDATIONS.md` ZW2.** The claimed "5554 verts vs
   shipped 5771" and "1024 RGBA32 vs shipped 2048 DXT1" reproduced exactly, this time with the
   shipped side read from the installation's own bundle in the same run.
6. **~~MenuMusic ships 49 MB of duplicated payload.~~ FIXED 2026-08-28, R6.** The two banks were
   both 24 583 864 B — the same 128 s track baked twice, once per edition. The track was replaced by
   a 12 s generated loop (licence, see R6) and the banks are now **1 062 328 B each**. Still the
   same loop twice, which is correct: the edition decides which media the game asks for.

## ALL THREE ADDED WEAPONS WEAR THEIR OWN MESH — measured 2026-08-28, R5

`demos\WeaponAdd\README.md` said only the PDW shipped a model and that the AR-181 and Tau pistol
`.glb`s were "not in yet", wearing their donor's art. `ppcontent.json` said the opposite: three
`publish` rows, and a `"model"` key on all three weapons. **The manifest was right and the README
was stale.**

R1's model row only proved the three *keys* resolve through `ct_catalog verify`. A key that resolves
is not a weapon that wears it, so R5 probed the **def's own binding** instead, ending at geometry:
`DefRepository.GetDef(guid)` → `WeaponDef.SkinData` → `.DefaultPrefab` → the engine's own
`AddonSkinDataBase.GetPrefabAsset(assetReference)` → `GetComponentInChildren<MeshFilter>().sharedMesh`
→ `vertexCount`. Each donor was read through the identical chain in the **same launch**.

Rig: `D:\PP-Instance2`, PPBridge `build=e0ccf41f`, **one** launch (menu, `phase:"menu"`), driven by
`ppcli.ps1 connect` and one 9-step plan run six times.

| Def | `SkinData` | `DefaultPrefab.AssetGUID` | prefab | mesh | verts / submeshes |
|---|---|---|---|---|---|
| `Morgott_VulturePDW_WeaponDef` | `E_SkinData [Morgott_VulturePDW_WeaponDef]` | `c7a9f1d24b6e4a3c8f5b7d1e9a2c4b60` | `sniper` | `sniper_mesh` | **8249** / 1 |
| **control** `PX_LaserPDW_WeaponDef` | `E_SkinData [PX_LaserPDW_WeaponDef]` | `b959e705c2972f34a8de8da15fcb13a0` | `WPN_PX_Laser_PDW_V01_Ready` | `WPN_PX_Laser_PDW_V01_mesh` | **3305** / 1 |
| `Morgott_VultureAR_WeaponDef` | `E_SkinData [Morgott_VultureAR_WeaponDef]` | `c7a9f1d24b6e4a3c8f5b7d1e9a2c4b61` | `ar181` | `ar181_mesh` | **5778** / **3** |
| **control** `PX_AssaultRifle_WeaponDef` | `E_SkinData [PX_AssaultRifle_WeaponDef]` | `604561be7de7cb6479711b4e31bdc02d` | `WPN_PX_RG_Assault_Rifle_T01_V01_Ready` | `WPN_PX_RG_Assault_Rifle_T01_V01` | **5554** / 1 |
| `Morgott_VultureSidearm_WeaponDef` | `E_SkinData [Morgott_VultureSidearm_WeaponDef]` | `c7a9f1d24b6e4a3c8f5b7d1e9a2c4b62` | `taupistol` | `taupistol_mesh` | **4582** / 1 |
| **control** `SY_LaserPistol_WeaponDef` | `E_SkinData [SY_LaserPistol_WeaponDef]` | `87db86228bf665d4b9ed60caa3770608` | `WPN_SY_Laser_Pistol_V01_Ready` | `WPN_SY_Laser_Pistol_V01_mesh` | **2750** / 1 |

> **The Sidearm row is HISTORY as of R6 below.** Its model was deleted on 2026-08-28 for licence
> reasons and the weapon now resolves to the control row's own skin. The R5 measurement stands as
> what a weapon WITH a model reads like; R6 is the same probe on the same def with the model gone.

**Verdict: VERIFIED.** Six distinct keys, six distinct prefabs, six distinct meshes, six distinct
vertex counts. The skin is the discriminator that key resolution cannot give: a weapon with no
`"model"` keeps the donor's `SimpleSkinDataDef` — the control rows are exactly what that looks like —
and every added weapon has its own instead.

Second, independent observation in the same run: each of the three prefabs reports **6 transforms**
(root + mesh + the four `EXT_` sockets) and a material on shader **`Standard`**, not
`Hidden/InternalErrorShader` — so the `deps` row on the shipped builtin-shaders bundle is live, and
the sockets the def's `ProjectileOrigin`/`AimPoint` name are physically present on what was loaded.
The same launch's log carries `ct_weapon PASS 'ar181' … four EXT_ sockets derived from
PX_AssaultRifle_WeaponDef's own box` and the matching `taupistol` line.

Notes on the controls:

- **The AR control is not the vanilla number.** `WeaponMesh` was enabled in the same stack and
  replaces that exact mesh, so `PX_AssaultRifle` read **5554** rather than its shipped 5771 (which
  the WeaponMesh row above measures). It is still a valid control for *this* question — it is what
  the Vulture AR would be wearing if it wore the donor's skin, and 5778 ≠ 5554 — but do not quote
  5554 as vanilla.
- **`MeshMerge` is wired and its predicted shapes reproduced.** The AR-181's 14 source meshes came
  back as **one mesh with 3 submeshes** (one per distinct material) and the Tau pistol's 9 as **one
  mesh with 1**, which are the numbers gate `U11` asserts offline. `demos\WeaponAdd\README.md`'s
  "Not wired yet" blockquote is therefore also stale, and was corrected in the same pass.
- **`ct_project` was not run.** `Dist\WeaponAdd.bundle` is committed and was deployed as-is,
  consistent with *A DOWNLOAD NEEDS NO BAKE* above.

## EVERY SHIPPED MEDIA FILE IS NOW ONE WE MAY PUBLISH — measured 2026-08-28, R6

A pre-publication audit found four files in `demos\` that this repository has no right to
redistribute: three demos' third-party audio with **no licence at all**, and one model whose licence
forbids shipping it as a stand-alone file. All four are gone, and each demo was re-measured after the
replacement rather than assumed to still work.

Rig: `D:\PP-Instance2`, ContentTool `build=4f48ed0c`, **two** launches (bake, then probe) driven by
`autogate.ps1`, plus `ppcli.ps1 connect` against the second one for the weapon reads.

| Demo | What replaced what | The bake | The live probe |
|---|---|---|---|
| **MenuMusic** | a copyrighted music remix (2 × 3.88 MB mp3, 2 × 23.44 MB bank) → a **12.000 s generated loop**, mono 44100 Hz, 96 kbps, 144 867 B, −15.7 LUFS / −4.2 dBFS, by `demos\tools\make_demo_audio.ps1` | `208540756.bnk` / `423563089.bnk` **1 062 328 B** each — `12042 ms 1ch 44100Hz, loop 0..531071 play count 0` | `mediaID=208540756` and `mediaID=423563089`, both **`streaming=false(MEMORY)`**, against untouched on-disk media of 142 978 ms and 284 164 ms |
| **ReplaceUiSounds** | three unlicensed sfx → `sting_plus` **0.300 s**, `sting_confirm` **0.400 s**, `sting_cancel` **0.550 s**, same script | 30 136 / 39 352 / 53 176 B (`339` / `444` / `600 ms`) | 18839791 **340 ms**, 633458426 **444 ms**, 940964934 **601 ms**, all `mediaID` correct, all **MEMORY** — against shipped 1200 / 3533 / 2231 ms |
| **AddUiSounds** | two unlicensed blips → `blip_rise` **0.350 s**, `blip_fall` **0.450 s**, same script | bank **78 728 B** inside `Dist\AddUiSounds.bundle` (was 246 920 B), `BANK PASS … LoadBankMemoryCopy: AK_Success bankId=432470233` | `1781464403` → `playingID=6 mediaID=3338666241 dur=392 ms endOfEvent=405 ms` MEMORY; `2693404503` → `playingID=7 mediaID=3338666240 dur=496 ms endOfEvent=507 ms` MEMORY |
| **WeaponAdd** | `taupistol.glb` (3.68 MB, Sketchfab "Free Standard" + NoAI, and Games Workshop fan art) **deleted**, with its `publish` row and its weapon's `"model"` key | `ct_project: ALL PASS` writing only `ar181` (5778 v) and `sniper` (8249 v); `WeaponAdd.bundle` **5 643 935 → 3 977 967 B** | see below |

**What the three weapons now wear**, read off the live engine through
`DefRepository.GetDef<WeaponDef>(guid)` → `.SkinData` → `.DefaultPrefab.AssetGUID`:

| Def | `SkinData` | `DefaultPrefab.AssetGUID` |
|---|---|---|
| `Morgott_VulturePDW_WeaponDef` | `E_SkinData [Morgott_VulturePDW_WeaponDef]` (ours, `instanceId=-3446`) | `c7a9f1d24b6e4a3c8f5b7d1e9a2c4b60` — **ours** |
| `Morgott_VultureAR_WeaponDef` | `E_SkinData [Morgott_VultureAR_WeaponDef]` (ours, `instanceId=-3464`) | `c7a9f1d24b6e4a3c8f5b7d1e9a2c4b61` — **ours** |
| `Morgott_VultureSidearm_WeaponDef` | **`E_SkinData [SY_LaserPistol_WeaponDef]`** — guid `cf182892-be3d-1eb1-f190-64c347a53fdb`, `instanceId=193886` (**positive** = a shipped def, not one we created) | **`87db86228bf665d4b9ed60caa3770608`** — the **shipped** Synedrion pistol prefab, the very key R5 recorded as this weapon's donor CONTROL |

**Verdict: VERIFIED.** The Sidearm does not merely lack a key of ours; it points at the donor's own
skin def and the donor's own prefab, which is what "a weapon with no `"model"` keeps its donor's
art" means when it is measured instead of asserted. The mod's own log agrees —
`ct_weapon PASS 'Vulture Sidearm' … prefab (no "model" - wears SY_LaserPistol_WeaponDef's own art)` —
but the log line is the claim and the table is the measurement.

Sizes: `demos\MenuMusic\` **54.6 MB → 2.30 MB**. Every remaining media file under `demos\` is either
generated by a script committed beside it (`demos\tools\make_demo_audio.ps1`,
`demos\tools\make_placeholders.ps1`, the `WeaponMesh` and `WeaponAdd` `tools\`) or carries a named
licence in its demo's `SOURCES.md` — CC0 for the Quaternius kit, CC BY 4.0 for `ar181.glb` and
`cyborg_spider.glb`.

## Measured limits and open items

- **`ct_sound probe` cannot reach a media whose bank declares no event.** `ct_sound probe 908611677`
  — IntroVideo's own theme — answers `probe VOID bank Cinematics declares no event for
  'PhoenixProject_Intro'`. That is a limit of the probe, not of the replacement: the media IS replaced,
  and the reachable route is posting the event by id (`PP_Intro` = 1015492702), which is how the
  6034 ms / MEMORY reading in the matrix was taken. A probe by mediaId only works where some bank
  declares an event for that media.
- **OPEN — CustomCreature's health does not land on the number ContentTool prints.** `ppcontent.json`
  asks for `health: 40`; the build line prints `Health.Max = Toughness 0 + 4 x 10,00 = 40`; the spawned
  actor measured **`Health.Max = 60,0`** (R2, `ct_creature gate`, `Data.Strength=4`) with TFTV 1.1.4.5
  resident in a 21-mod stack. Recorded, not investigated: the bake-time number and the in-game number
  disagree under a mod stack, and nothing yet establishes which layer applies the multiplier.
