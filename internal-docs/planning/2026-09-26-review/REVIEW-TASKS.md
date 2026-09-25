# ContentTool review 2026-09-26 — checklist (survives compaction)
Task: review w/ Codex, fix what's possible offline (no game), fix docs wiki. Commit to ContentTool inner repo main; qgate must pass.
Codex review running: status E:\Temp\cx\0b33c45d92c6497484ed5e2e431e47be.status.txt, out E:\Temp\cx\0b33c45d92c6497484ed5e2e431e47be.out.md
Root untracked Wizard.ApocDesignation\ + .zip = user's real project -> never commit, ask user about moving.

## Code findings (Bake reviewer) — src\Bake\
- [ ] MED MeshFields.cs:65-71 + Import\MeshBuild.cs:15-16: replaced mesh written w/o tangents (stride 32, slot2 dim0) -> normal maps lost. Fix float4 tangents @32 stride 48, SkinOffset/SetSkinStream/SkinSummary. MeshMerge.cs:166/215/230 note. offline test + ingame visual
- [ ] MED ProjectBake.cs:344 vs :433, GlbReader.cs:469: _EmissionColor raw linear, _Color sRGB; Srgb clamps 1 (MaterialFields.cs:184). ingame check for look — verify color space first
- [ ] MED SoundReplace.cs:336 -> BankPrune.cs:69 Sweep keep-set = baked, deletes good bnk of declared-but-refused row. Fix keep = all reps[].Media. offline
- [ ] MED VideoCatalog.cs:484-486,:566 served=n after loop; exception leaves rows registered. Fix count incrementally + Unregister
- [ ] LOW SkinFields.cs:1254-1263, ReadBack.cs:475-510 O(n^2) string += -> StringBuilder
- [ ] LOW SoundLoad.cs:51 OrdinalIgnoreCase vs BundleClaims.cs:405 CompareOrdinal — unify comparer
- [ ] LOW ClipFields.cs:600-610 Wants glob last segment greedy ("*_Loop" vs "MV_Loop_Run_Loop") -> EndsWith
- [ ] LOW HexColor.cs:40 HexNumber allows whitespace
- [ ] LOW SkinFields.cs:528,:1162 (byte)ch truncates non-ASCII bone names (unverified hash encoding)
- [ ] LOW PrefabFields.cs:267-271 Get ignores m_FileID -> null when !=0
- [ ] LOW MaterialFields.cs:99 !=null should be IsDummy; SoundLoad.cs:94 UnauthorizedAccess after claim; SoundReplace LoadBank w/o UnloadBank (dev probes); SoundReplace.cs:323 no BankGen.SelfCheck; VideoCatalog.OpenArm.Run no finally (AsyncGate.Pending stuck)
- [ ] DUP: bank id SoundReplace.cs:322 vs BankPrune.BankId; BKHD read SoundLoad.cs:103 & BankPrune.cs:52; Sine/Check/Same helpers; AABB loops SkinFields 284/477/824/1371; channel layout 903/1243; V/F formatters
- [ ] CODE StageText.cs:147-148 "nothing to bake" says Content\Models\ but meshes in Content\Meshes\ (StageValidate.cs:102)
- [ ] DEAD StageText.cs:226 R32 (project changed during stage) + :304 CancelledAfter never emitted — wire or delete + docs

## Docs findings — bench (docs\bench\)
- index.md:18 ct_bench rescan missing (FitBench.cs:126-129; ContentToolMain.cs:652 desc omits); :86-93 refusal "opening threw" missing (FitBench.cs:488); FIT controls undocumented (scale slider 1858-1875, gizmo ARROWS/RINGS 1892, Advanced row 1825-1850, rescan/all/RE-EQUIP/UNEQUIP 1931/1973/1982, anim strip FitAnim.cs:503-561)
- model-doctor.md:23 Extend/Replace inverted (ModelDoctor.cs:1890-1896); :40-42 NOT RIGGED suffix, warnings only if any (Diagnostic.cs:76,90); :65 code only in Copy report (2054,2064-2080); :110,131 alias sidecar written from session map not copied (ProjectScaffold.cs:313-320, reuse 257-259); :141-151 disabled messages wrong (1651,1628-1632, LifecycleDashboard.cs:325, TargetRefusal 681); :158-172 ship results missing (833,794,StageText 46/54,821,ProjectScaffold 170,157,301-303); undocumented bone-map table Save aliases/Write skel plan 1553-1597, mode toggle 1705, ALIASES 1690, <Back 1804, [duplicate bone names] 1848, Advanced SLIM/ZIP/SKEL (FitBench 1772, SlimPanel 123-246)
- lifecycle-tab.md:23 " ..." marker (LifecycleState.cs:426-433); :43,173 R32 dead; :135 CancelledAfter dead (only R31, LifecycleJob 468,491); :164 Package exempt too (LifecycleState 743-746); :165 R36 cause = catalog.json.ct-edits old record, fix verify files + delete .ct-edits/.ct-backup (Route7.cs:276-289,318-320); :166 R34 = out dirs outside PatchedRoot/root e.g. ".." (LifecycleDashboard 959-970); :167 Mods\ retry 'ct_route7 apply <folder>' no restart (Route7 241-242,104); :39,169 R28 standalone names only "patched copies" (LifecycleState 720); :37 Run all stop rules (691,705,714); missing verdicts StageText 67-72,273-274,102,112,123,242,163,147; Queued (LifecycleDashboard 1012)

## Docs findings — getting-started/reference/troubleshooting
- WRONG bake-errors.md:7,:49-51 + lifecycle.md:59-60: unsupported audio DOES count as failure (ContentProject.cs:439-440,463; ProjectBake.cs:227)
- WRONG bake-errors.md:216 same-stem collision -> SOURCE SKIPPED counted (ContentMods.cs:67-69,101-107); only Videos throws (ContentProject.cs:474)
- WRONG bake-errors.md:194 P7 fix: position*<n>/scale*<n>, rotation refused (ProjectBake.cs:1982-1997)
- WRONG first-mod.md:132-133 decimal point not required (float.TryParse Float, ProjectBake 1776-1779)
- WRONG lifecycle.md:28-29 path Patched\<8-hex install tag>\<ppcontent id>\ (ContentToolMain.cs:58; PatchCache.cs:111-117)
- WRONG bake-errors.md:3,:93 "1.1.2" -> 1.2.1
- lifecycle.md:96-101 + bake-errors.md:128-133 lower mod id = ppcontent id, CompareOrdinal case-sensitive, eviction msg (Route7 444, BundleClaims 403-405, BundleLive 129-131) vs project-files.md:43
- bake-errors.md:187 colon form (SourceImport.cs:30) vs audio no-colon (ContentProject 451); :44 SOURCE SKIPPED also manifest refusals (Manifest 239-243,85,139; ContentProject 546,631,771); :196 Identity lowercased . -> _ (BundleResidency 36); :217 nothing-to-bake condition (ProjectBake 268-281)
- lifecycle.md:116-126 package: DLL newest AssemblyName match (Package 209-221), Audio\Replace w/ bnk dropped LEFT BEHIND (119-126,180), pre-staging REFUSED lines (71-91)
- project-files.md:59 audio record name <id lower . _>_<stem lower>, .stream suffix (ContentProject 832-839); :33-48 missing fields scale, loop, play, publish(key,asset,type,deps), sounds(media,file), creature/weapons (ContentProject 388-391,575-647; Package 292)
- choose-a-route.md:40-41 video Add not baked (ct_video live), ct_sound bake -> Dist\Sounds (ProjectBake 1961-1969)
- missing refusals: P0 bundle not shipped (ProjectBake 1736); P4 REFUSED/WARN/PARTIAL (1833, StageText 87, ProjectBake 1884); R37/R38/R29 (StageText 212,261,267); Package ships nothing 134, sound NEVER BAKED 153, AssemblyName missing 323, SHIPPED BUNDLE IDENTITY 248

## Code findings (Import reviewer) — src\Import\ (all offline-testable, tests\ObjCodecTests)
- [ ] HIGH GlbReader.cs:408 PrimitiveMaterials: no-material primitive continues past MaterialImages/MaterialEmissive.Add (416-417) -> lists misalign vs submesh (ProjectBake.cs:369/415). Fix add null both. test SubmeshSlots
- [ ] HIGH GlbReader.cs:875-883+1319-1365 Hierarchy skips non-joint nodes between joints ($AssimpFbx$ PreRotation); anim samples miss them -> snap. Fold chain or refuse by name. test ClipImport
- [ ] MED GlbReader.cs:1338 non-root rotation tracks not hemisphere-fixed (Root() 1404-1406 only) -> ClipFields.cs:172 spin frame. negate dot<0 all tracks
- [ ] MED Bake\MeshMerge.cs:134-137+262 StaticMerge (GlbReader.cs:291) takes Materials[0] per piece; group per submesh. MeshMergeTests
- [ ] MED GlbReader.cs:490-527+ModelBuild.cs:171 KHR_texture_transform in extensionsUsed only / texCoord=1 silently ignored; refuse. stale comment 64-66
- [ ] MED GlbReader.cs:2070-2077+Draco.cs:2445-2450 Draco normalized ints not divided (Value() 2211-2214). DracoTests
- [ ] MED GlbSkel.cs:728-744 Validate misses animated parent of collapse. GlbSkelTests
- [ ] LOW GlbReader.cs:1578-1609 neg det no tangent w flip; normals need inverse transpose (latent, Stride=32) — ties to tangent finding
- [ ] LOW ObjCodec.cs:108,113 RequireCount exact -> accept >= min (vt u v w, v x y z r g b); MeshBuild.cs:76 all normals recomputed if one vn missing
- [ ] LOW SlimJob.cs:88-96 trim no ReadBack verify (Zip 169, Skel 280); GlbSlim.Trim drops ext-only accessors 251-253
- [ ] LOW GlbReader.cs:2273-2280 dead Draco Unreadable branch; GlbDocument.cs:22-24 comment false
- [ ] LOW SourceAudio.cs:121-124 Xing only in first frame. SourceAudioTests

## Code findings (Bake pipeline core)
- [ ] HIGH Route7.cs:494 Applied adds Failed on every failed apply (not only Toggle :94-100); only Install clears (:560) -> R29 dead end (LifecycleState 702,729). Fix: drop Failed.Add in Applied; Toggle (:201-204) out how, add only BakeFailed; Failed.Remove after Success bake PatchFailed==0. build + ingame
- [ ] HIGH LifecycleState.cs:316-327,289-294 Clip 463-468; LifecycleDashboard 532,546; LifecycleJob 181: summary line clipped. Fix summary key = last non-empty line clipped FieldRoom budgeted before Bounded; log cut from tail. offline LifecycleView arms ObjCodecTests
- [ ] HIGH BundleLive.cs:76-88 +:114 re-Apply own claim -> Outdated=true, R30 cosmetic restart (Apoc T4 2026-09-06-apocdesignation-run.md:642). Fix lookup claim before Register; same path not Outdated -> Redirected. ingame
- [ ] MED LifecycleJob.cs:385-392 Validate no Stopped check; LifecycleState 101 Begin, 639; LifecycleDashboard 554-562: Cancel lost during Run all Validate / Bake worker. Fix pass CancelRequested to Sequence.Report, stop R31. offline G4
- [ ] MED BundleBaker.cs:526-527,606-608,653-654,938-939,1048-1049 loads before try -> handle leak on corrupt. move into try, route via Read<T> (:520). offline corrupt test
- [ ] MED CatalogKeys.cs:124,217 LoadAssetAsync handles never Addressables.Release. ingame
- [ ] MED LifecycleState.cs:696-698 vs LifecycleDashboard.cs:529 Apply inner re-bake doesn't fill Bake row (half-wired). ingame
- [ ] MED DUP bundle census x4 Route7 130-135, LifecycleJob 106-111, ProjectBake 180-186, 2247; normalizers Route7.Norm:247, ProjectBake.Slashed:2230, OutputClaim.Canonical:40 -> Declared.Targets()+Canonical()
- [ ] LOW ProjectBake.cs:511-512 _common mount no ProbeArchive (BakeSelfCheck :984)
- [ ] LOW ProjectBake.cs:2220-2227 R38 also require BundleLive.ResidentNow

## Code findings (Wwise + Project)
- [ ] HIGH ContentProject.cs:229,446,873-883 + StreamCache.cs:44-53: all projects allocate media ids from 0xC7000100 -> cross-mod collision; streams written to ContentTool ModDir\WwiseAudio not mod's (doc StreamCache 19-23). Fix per-project base WwiseId.Hash(id) skip PP ids + per-mod TargetDir. offline allocator test
- [ ] MED half-wired: added-audio bank + streams loaded only at bake (ProjectBake 558-564); runtime SoundLoad 74-78 loads only Dist\Sounds replacement; StreamCache.Extract no runtime caller -> Content\Audio sounds never play for players. runtime loader or refuse/doc. ingame
- [ ] MED AudioProbe.cs:62 unconditional UnloadBank kills live media on toggle (SoundLoad 20-23). treat AlreadyLoaded as success
- [ ] MED ContentMods.cs:56-72 stem collision assumes adjacency (swatch.jpg, swatch.old.png, swatch.png) -> Dictionary OrdinalIgnoreCase. offline
- [ ] MED Replacements.cs:27-31,42 + ContentProject 456,463: JsonUtility List<Record> probably always empty; throws outside try; ReplacementRefusals not counted; Replacements/ReplacementFile.Save/AssetSha1.OfMesh unused -> Json.Parse or remove dead
- [ ] MED ContentProject.cs:575-658 + Package.cs:403-415,502-505 publish/sounds/meta via regex (breaks on "]" in string, escapes, uint.Parse overflow :595); Package.DeclaredSounds dup ParseSounds -> shared Manifest tree reader
- [ ] MED Package.cs:117-176 post-staging steps outside try -> half-staged outDir refused forever (:77). extend try
- [ ] LOW TargetPath.cs:158,185-193,273-291 leading zeros [01]/media:05 dedupe. TargetPathTests
- [ ] LOW StreamCache.cs:160-163, WwiseWem.ToWav 164,177-188, Replacements.cs:62 -> AtomicFile
- [ ] LOW WwisePcm.cs:42-43 catch only IOException (Unauthorized aborts Load)
- [ ] LOW BankGen.cs:265,274 int overflow -> long; ModRoster.cs:385 empty catch -> log

## Code findings (Tactical + Doctor) — Doctor clean
- [ ] HIGH CreatureBuild.cs:857-872 comment says SynthSkin breaks tactical save load, code assigns it when bashPoint set (since 094f345). ingame verify, then null or fix
- [ ] MED CreatureBuild.cs:933-951 (+1379-1391,1360-1367) AlsoAccept mutates shipped donor anim action defs (IsDefault, TacActorSimpleAbilityAnimActionDef, useGameAnimations); ignores BaseAnimActions (TacActorAnimActions.cs:144-147), _equipmentIds not cleared. clone-on-write
- [ ] MED WeaponBuild.cs:327-334 Forget GetField _equipmentIds on subclass -> never found (private on base TacActorAnimActionEquipmentFilteredDef.cs:23). use typeof(base). offline
- [ ] MED WeaponBuild.cs:95-98,111,134,144 One() not atomic (CreateDef registers, DefRepository.cs:266-269) -> half-built "PASS already built" / dup guid. resolve+cast before CreateDef
- [ ] MED WeaponBuild.cs:575-599 Seed duplicates StartingStorage rows each Build (admitted :1188-1191). skip present
- [ ] MED CreatureBuild.cs:122-196 Build no already-built guard; bundle stays mounted on fail returns 218,258,305,389; holder leak BuildRig 1097-1101
- [ ] MED CreatureBuild.cs:2090-2095 harmony assigned before PatchAll -> failed patch = later builds skip Install. assign after. offline
- [ ] MED/LOW CreatureFit.cs:247-254,330-331,381-393,458-463 same name ct_hitbox spheres+hull, Find hits sphere; refusal leaves bone colliders. keep list
- [ ] LOW CreatureBuild.cs:609,628-631 shallow Data.Clone shares donor inventory/lists; scratch CharacterTemplateDef shares donor.Data :727
- [ ] LOW CreatureBuild.cs:2182-2196, WeaponManifest.cs:45,53-56 regex JSON readers: first "id"/"scale" anywhere, empty id same GUIDs :2112, "flip": true -> false (WeaponBuild 1653), lazy \] -> one top-level reader + validate id
- [ ] LOW dup AlsoAccept vs WeaponBuild.Animate; no weapon BuildAll (only ContentToolMain.cs:179 CreatureBuild.BuildAll) -> weapon mod w/o C# mints nothing; CreatureRanged.cs:284 NRE accuracy+null aspect; StampEvents 1784-1788 returns on first failure

## Code findings (FitBench cluster)
- [ ] HIGH FitBench.cs:2239,:120,:433-436,:451-466,:419 partial Close -> entered=true open=false; hotkey/open verb check open -> Open() re-snapshots bench state, loses restore lists. Fix Open runs Close first when entered, refuse if fails; hotkey entered?Close:Open. ingame
- [ ] MED FitBench.cs:2223-2275 one catch around Update -> inputBroken for session disables hotkey + StillThere auto-close. narrow catch to Input reads
- [ ] LOW FitBench.cs:590-619 ResetView always reports success (use Step); Fits :1626, Bodyparts :1556 silent
- [ ] LOW hot-path allocs FitBench 2374-2375->1530, WeaponBuild 1023, FitGizmo 517->170,214-218, FitBench 1960
- [ ] LOW stale line refs FitBench 1731/1759,1738,1739,2212; comment 1735-1738 wrong cause (harmless)

## Docs findings — examples + find-content
- WRONG menu-music.md:57, replace-ui-sounds.md:62, intro-video.md:78 ct_sound bake summary = `ct_sound bake: N/N bank(s) in <dir>, 0 refused - NO game file was opened for writing. ContentTool loads these at init.` (SoundReplace.cs:338-340)
- quit-cutscene.md:21-22 120s not a cap (QuitCutsceneMain.cs:285,294); :73 13.0s culture-dependent (:286); :79 verified date ledger 2026-08-27 R3 (VERIFIED-DEMOS.md:34)
- custom-creature.md:95-96 mixes R2 (2026-08-27, 0,69s 4,12 tile/s VERIFIED-DEMOS.md:35) with R4 (0.71s, :79,92); :73 trailing arg = optional template fragment last token + `links` subverb undocumented (CreatureGate.cs:63,66-71)
- replace-armour-set.md:139-143 "offline shard removal" = test harness only (ObjCodecTests --dropshard); :3,22 Wizard.ApocDesignation not a shipped demo, readers can't get it
- examples/index.md:3-34 omits replace-armour-set (in nav mkdocs.yml:89)
- find-content/index.md:99-106 ct_extract misspelled -> THREW InvalidOperationException (Extract.cs:262-276 no catch); ct_list assets 0 of N; :53 videos filter optional (Extract.cs:63-64)
- weapon-mesh.md:50-53 tree omits tools\grimdark.ps1, tools\check_project.py; bin\Release dll in trees exists only after build (weapon-add 62, weapon-mesh 47, custom-creature 47, add-ui-sounds 33, intro-video 42, quit-cutscene 36)
- CODE? Extract.cs:262-276 ct_extract misspelled name throws raw -> proper refusal

## Code findings (Lifecycle/BenchList cluster)
- [ ] HIGH LifecycleDashboard.cs:757 StageResult.Tail(log,12) per OnGUI over ~1.2M log (StageResult.cs:234 Replace+Split) -> stutter. Fix Tail scan backwards LastIndexOf, or cache. offline unit test
- [ ] MED SlimPanel.cs:106 w/ :292/:302 post-run Pick re-read overwrites result -> SKEL run shows "no animation clips". keep result
- [ ] LOW FitBench.cs:1200-1205 after :1183 (PrototypeBaySession 214/225-231) un-quiesce autorefresh inside rebuild window (contradicts 1358-1363)
- [ ] LOW ModelDoctor.cs:1126-1130 silent catch per frame -> log first via ChunkedLog.Fail
- [ ] LOW ModelDoctor.cs:2086-2137 Dispose leaves Message/mapOpen/... stale
- [ ] LOW dup progress bar LifecycleDashboard 726-736 vs SlimPanel 263-271; stale refs ModelDoctor 658, LifecycleDashboard 947

## Docs findings — recipes
- WRONG sounds.md:168 ct_sound bake summary (SoundReplace.cs:338); :65 progress only Player.log (Extract.cs:395-396); :191 collision = same stem diff ext (SoundReplace 127-131)
- WRONG weapon.md:183 `ct_fit X show` throws -> `ct_fit X` or `ct_fit show` (WeaponBuild 1241,1253,1271,1375); :18 only shoot required when fit != auto (1694)
- WRONG videos.md:10,128 ct_list videos path relative -> only bare filename matches (LooseFiles 32, Extract 419, CatalogText 91-92) — CODE: make paths match or print catalog form; :84,106 ct_project doesn't validate video row (ProjectBake 1961-1970, VideoCatalog 536,545)
- WRONG animated-models 95, humanoid-soldier 160, replace-character-body 130 "kept n material(s)" only if >1, ends [names] (ProjectBake 446-449)
- WRONG SOURCE SKIPPED colon form textures 106, meshes 113, animated-models 115, animation-contract 98
- WRONG version 1.1.2 textures 8, creature 4, behavior-dll 105 -> 1.2.1 (better: remove version pins)
- CODE BUG CreatureManifest.cs:443,345,454-456 scalar reader stops at comma: "up":"0,1,0" -> "0" silently, hitBones "a,b" first only, name cut. docs creature 106, humanoid-soldier 128. FIX CODE. offline
- CODE BUG WwisePcm.cs:239 surround -> ct_sound bake THREW (SoundReplace 319 no catch); Add path skips (ContentProject 825-830). catch + doc mono/stereo only
- MISSING: replace clip position*k/scale*k (ProjectBake 1793-1810,1982-1998); .stream.wav; numeric filename in Audio\Replace no row needed; loop only from loose wem (SoundReplace 315-320); ct_package unbaked refusal; Add event name + bank asset audio/banks/<modid_>.bnk (ProjectBake 483); creature keys pace, accuracy, aiAction, shootBone, useGameAnimations, colliders:"off", aim, hitRadius, hitBones, jump (CreatureManifest 185-242,355-365, CreatureRoles 61); creature-climb FAIL (ProjectBake 1011-1018); weapon keys blurb, icon, damagetype, keywords, projectile, flash, tint, trail, flip, malformed row refuses whole block, GUID first hex digit (WeaponBuild 1636-1724, Package 43-46); meshes .glb.aliases.json, bind matrices dropped, nearest-bone fallback (AliasMap 135, SkinFields 726-729, BundleBaker 204-237); animated-models only _MainTex+base colour, emissive texture refused, no normal/metallic (ProjectBake 414-437); publish[] auto on enable, ct_catalog apply dev shortcut (Route7 589-593); P0 bundle refusal

## Codex findings (E:\Temp\cx\0b33c45d92c6497484ed5e2e431e47be.out.md)
- [ ] HIGH ContentProject.cs:347, ProjectBake.cs:288, ContentToolMain.cs:56: id/bundle only non-empty checked, used in paths -> abs/.. escapes Dist/patched. validate as single safe component. offline
- [ ] HIGH Package.cs:87,286,311 packager brace-balance + regex -> invalid JSON PACKAGED. real parse (merge w/ Wwise+Project regex finding)
- [ ] HIGH ProjectBake.cs:1961, VideoCatalog.cs:535 video row not validated before ALL PASS / packaging (same as docs finding)
- [ ] HIGH ModRoster.cs:233 Harmony prefix publishes content before DLL load/OnModEnabled (ModEntry.cs:198, ModManager.cs:208 decompile); load throw -> mod off but registrations stay. finalizer rollback. ingame
- [ ] MED Package.cs:364,387 replacement audio .bnk staleness not checked vs source; missing sounds[] file not walked. fingerprint
- [ ] MED CatalogLive.cs:57,82, VideoCatalog.cs:495 video key no owner -> two mods clobber, Unregister removes other's. ownership
- [ ] MED Package.cs:209 BuiltAssembly newest DLL any subfolder incl obj/ref -> exclude obj, prefer bin/Release
- [ ] MED StageValidate.cs:41,80 source placement only texture/mesh (doc lifecycle-tab.md:47 says all)
- [ ] MED Package.cs:105 cleanup failure hidden, message claims deleted
- [ ] LOW LifecycleJob.cs:603,622 ManualResetEvent not disposed (test arm)
- [ ] LOW CatalogLive.Register/Unregister public API undocumented (videos.md:27)
- [ ] LOW ProjectBake.cs 2572 lines: split sample/probe + patch pipeline
- [ ] LOW workshop vdf placeholder (on hold by user)
- Codex: 36 nav pages present, 0 broken relative links

## Code findings (Dev probes/logging) -> track E
- [ ] HIGH LiveMesh.cs:318 vs :323 Gate finally SeamSwap.Revert wipes user swaps even when refused. revert only own
- [ ] MED SeamSwap.cs:346-403, DevScan.cs:101-105,185 TexSwapGate/DevScan no MarkCount>0 guard (MeshSwapGate:498 has); DevScan forces Enabled=false instead of restore; MissionGate.cs:188
- [ ] MED MissionGate.cs:135-140 load wait accepts current level -> copy MusicProbe 326,334 lvl!=before guard
- [ ] MED ContentToolMain.cs:343-357,631-726 ALL dev probes registered for every player (ct_fmt, ct_seamprobe Harmony, ct_texswap, ct_meshswap, ct_liveswap, ct_scan, ct_dev, ct_outtest, ct_voices, ct_music, ct_mission); ct_music gate / ct_mission gate FinishLevelAndLoadGame drops campaign w/o confirm (MusicProbe 389, MissionGate 250). register dev set only w/ dev flag file; autogate.ps1 must arm it
- [ ] MED MusicProbe.cs:124-131 ProbeRunner.Go no try/finally (AsyncGate.Pending leak); MissionGate.List 88-98 same
- [ ] MED Extract.cs:367-397 ct_extract audio --all sync 3105 wem freeze -> coroutine/worker (Extract owned by C1 -> after C1)
- [ ] LOW ContentToolMain.cs:193 disable reverts only if SeamSwap.Active; also when MarkCount>0
- [ ] LOW FormatProbe.cs:95 AudioSettings.Reset not restored; :224-256 UnityWebRequest/AudioClip leak
- [ ] LOW SpillFile.cs:34-43 retry on write failure -> up to 1000 partial files
- [ ] LOW DevLoop.cs:142-152 FileSystemWatcher no Error handler
- [ ] LOW dup MusicProbe 370-390 ≈ MissionGate 209-251 loaders; Runner+AsyncGate pattern x5

## PHASE 2 — fix tracks launched 2026-09-26 ~01:00 (worktrees, brief FIX-BRIEF-COMMON.md)
A Import | B Project/Package/Wwise | C1 Lifecycle/Route7/ProjectBake | C2 Bake fields/video | D Tactical+Dev bench
Next: merge branches into main (sequential, resolve conflicts) -> build+tests+qgate on main -> Codex review of merged diff (cx -Review / -Base eb0ffc1) -> fix -> docs track (facts from agents' "user-visible changes" + docs findings above; prose by Codex) -> push+release (standing order) -> handoff update.
Merge recipe: worktrees based on 9cbc293 -> `git -C <wt> rebase main` then `git -C <repo> merge --ff-only <branch>`.
- [x] C2 merged (main 10f5d2e). Docs facts: CatalogLive.RegisterFor/UnregisterFor(modId,key,path) new, Register/Unregister = anonymous owner "" outranks all; lower mod id ordinal (BundleClaims.Keeps) serves, higher QUEUED; msgs "QUEUED: mod 'X' already serves key 'K'...", "registered ... (taken from 'X' ...)", ct_video ", N held behind a lower mod id"; tint space refused; suffix globs. Game = Linear color space (m_ActiveColorSpace=1, Unity 2019.4.31f1) -> emission finding plausible, deferred. Non-ASCII bone hash unproven.
- [x] D merged (main 7fc9335; combined build+ObjCodec+TargetPath green). Asked agent D: inventory clearing on ADD path safe for ranged/abilities? Docs facts: comma syntax "up"/"hitBones"/"name" works, unquoted "flip": true; msgs ct_creature VOID no top-level "id", PASS already built, ct_weapon FAIL no ViewElementDef/not SimpleSkinDataDef Nothing was minted, "<weapon> already seeded", FAIL ranged accuracy N% NOT applied, ct_bench REFUSED last close did not finish, RESET VIEW "BUT N step(s) FAILED", collider refusal "REFUSED, N added shape object(s) were removed." Deferred: bench un-quiesce, SynthSkin, AlsoAccept, weapon BuildAll missing.
- [x] A merged (13 commits). Docs facts: refusals TEXCOORD_N "FIRST UV map", KHR_texture_transform "delete the Mapping node" (even w/ own PNG), animated in-between node, GlbSkel animated collapse, slim/zip Guard extension refusal, slim "trimmed file does not import"; slim 6 stages (Verify); Assimp FBX helpers folded; OBJ extra values read; Draco msg gone.
- [x] B merged (main e6e6b75, 11 commits; combined build+tests green). Docs facts: unsafe id/bundle refusal; ct_sound bake REFUSED per row, "mono or stereo only"; packager "CHANGED SINCE IT WAS BAKED", NEVER BAKED declared sound, NOT VALID JSON names cause, meta.json "did not read as JSON at character N"/"is not a JSON object.", staging "could NOT be deleted"; Dist\Sounds\sources.ledger ships; DLL bin\Release first never obj\; sounds media whole number; target paths no leading zeros; banks load in ppcontent-id order; per-project media ids.
- [ ] LEFTOVER for C1 (owns ProjectBake): ProjectBake.cs:558 StreamCache.Extract -> pass StreamCache.TargetDirFor(p.Root). Deferred: runtime loader for added audio (Content\Audio never plays for players!) — doc as limitation.
- [x] E merged (main 6233dcf). Docs facts: 15 dev cmds (ct_bake, ct_audio, ct_outtest, ct_voices, ct_fmt, ct_replace, ct_revert, ct_texswap, ct_meshswap, ct_liveswap, ct_dev, ct_scan, ct_mission, ct_music, ct_seamprobe) registered only with empty file Mods\ContentTool\ct-dev; init line "Dev commands: ARMED/not armed"; autogate creates marker; deploy.ps1 prints status. ct_creature gate loads save w/o confirm but stays public (documented) — mention in docs. Game console has no hidden flag (ConsoleCommandAttribute.cs:9-21).
- [x] C1 merged (main 0c9a626, 63 commits total, qgate -All exit 0). Docs facts in agent report: nothing-to-bake text, seam summary key, cancel msg "Lifecycle: cancelled after X; later stages were not run.", R32 removed, mount already mounted, BundleLive "kept ...", R29 cleared by clean bake, R38 only when loaded, Validate FAIL for video/sounds/publish, VIDEO FAIL lines, ct_list videos paths work.
- [x] F merged (main 525fb60). Docs: ct_extract audio --all answers immediately "...decoding N matched loose media into <dir> in the background...", progress "D of P decoded so far, M matched" to log, second run REFUSED, bad tex/mesh name "ct_extract REFUSED - no Texture2D named '<x>' in <path>. The names it holds: 'ct_list assets <bundle> Texture2D <nameFilter>'".
- CODEX DOWN: 401 Incorrect API key (sk-svcac...fvMA) since ~02:xx; asked owner to re-login. Substitute Claude cross-review agent running.
- codex --no-daemon also 401 -> bad key in codex login itself; owner must codex logout/login.
## Follow-up fixes (after cross-review)
- [ ] ct_creature gate usage/refusal points to `ct_mission list` which is now dev-only -> point to a public way (or make ct_mission list public)
- [ ] Package stale-bank date fallback w/o sources.ledger: on fresh git clone mtimes = checkout time -> may refuse sound demos. Check demos have ledger / make fallback not refuse (warn) — verify
- [ ] ct_extract audio <id> / video <name> bad name may still THREW (LooseFiles.CopyOut) — check
- [ ] SlimPanel.cs:394 SLIM shows 0/5 before start (6 stages now); ModelDoctor.cs:1572 comment wrong (warning fails run); ct_bench console Description omits rescan
- [ ] Content\Videos same-stem collision still throws (ContentProject ~:474 null refusal list) -> SOURCE SKIPPED like others
- [ ] broken-JSON ppcontent.json in ct_project: Unity JsonUtility may throw first ("ct_project THREW ArgumentException") before Manifest "is not valid JSON" -> check order, make it a counted refusal
- [ ] streamed added sounds (.stream.*) written only at bake, ct_package doesn't ship them -> ship or refuse/doc
- Cross-review done (no HIGH). Track G (direct on main, agent) running: 14 items (video anon Register undo MED, meta.json lenient, in-between rest curve, weapon bare values, SoundLoad ModId, stale date -> warn, ct_voices watch + ct_mission list public, cancel log, reserved names, demo .gitignore WwiseAudio, Videos stem collision, broken JSON ct_project, ct_extract audio/video bad name, SlimPanel 0/6 etc.)
- facts-recipes.md written (106 items). LIMITATION wording: added sounds silent only w/o DLL loading bank; streamed .stream.* never play for players (WwiseAudio not packaged, no runtime registration).
- DOCS PLAN: after G -> facts sheets facts-{examples,bench,start-ref,recipes}.md + G's user-visible changes -> Codex rewrites pages (if login fixed; else ask owner / scribe fallback) -> Claude accuracy check -> mkdocs build --strict -> commit
- facts-start-ref.md written (65 items)
- facts-bench.md written (53 items)
- facts-examples.md written (examples + find-content, 31 items)
## PHASE 3 (running)
- Track F Extract.cs freeze + misspelled refusal (worktree agent)
- Codex review #2 of eb0ffc1..main: status E:\Temp\cx\879f537876ad467a9a404a00d3a70ad8.status.txt, out .out.md (poller bg task)
- Docs fact sheet agent -> scratchpad\DOCS-FACTS.md; then Codex rewrites pages (after review #2 done, same cx thread), Claude checks accuracy, mkdocs build, commit
- Then: push main + GitHub release (standing order memory contenttool-push-and-release), update handoff internal-docs\planning, cleanup .claude\worktrees
Deferred to in-game: tangents stride, emission sRGB, SynthSkin, AlsoAccept, ModRoster rollback?, BundleLive/CatalogKeys runtime proof.
DONE: Wizard.ApocDesignation\ + .zip moved to local\ (owner: "решай сам"). docs replace-armour-set.md / lifecycle-tab.md mention it -> docs track: say it's not a shipped demo.
TODO track E after merge: dev-probe ct_* commands keep working (autogate/tests) but hide from user listing if game console supports hidden (check decompile), else mark [dev] in Description; exclude from user docs.

## Pending reports
- code-review parent (6 reviewers) + docs parent (4 auditors) + Codex
