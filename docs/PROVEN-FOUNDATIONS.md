# PROVEN FOUNDATIONS — closed by in-game measurement

> **WARNING — these are NOT roadmap items.**
> Every row below was established by a real in-game measurement and cost real sessions.
> Do not re-investigate, re-spike, re-open, or "verify" any of it. Touch it only if a
> REGRESSION appears in our own code — and then fix the regression, do not re-litigate
> the architecture.

Sources: `docs\research\pp-content-tool-findings-2026-08-12.md`,
`docs\research\pp-audio-architecture-FROZEN.md`.
Harness: mod `ResourceReplacer` (`E:\DEV\PhoenixPoint\ResourceReplacer\`), console commands
`rr_*`. Log: `C:\Users\Morgott\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\Player.log`.

## Unity / AssetBundle half

| # | Closed fact | Evidence |
|---|---|---|
| 1 | `AssetBundle.LoadFromFile` works inside PP (game itself uses Addressables, but the API is live) | `rr_bundle1` on `dlc5_assets_all.bundle`. Needs a reference to `UnityEngine.AssetBundleModule` (ships in `Managed\`) |
| 2 | A repacked bundle loads and repacked pixels reach the GPU | `rr_bundle2`: magenta `_EmissionMap`, other 4 textures unchanged |
| 3 | Mesh survives a repack (with in-run CONTROL) | `MESH 'ALN_Siren_Arm_Slasher_Right' verts=1783 subs=1 tris=3196 centre=100,217 1,015 0,288`; CONTROL same run `MESH 'Geo_Head02_V01' centre=0,000` untouched |
| 4 | Brand-new objects can be ADDED to a bundle | `GetAllAssetNames = 6` incl. 2 new names; `ADDED TEX ... centre=255,0,255,255 MAGENTA`; `ADDED BIN ... hex=00ffc3280000414280fe007f EXACT` |
| 5 | Engine accepts a FAKED `old_type_hash` (16 zero bytes) | same run, no rejection — Unity's real runtime type hash need not be computed |
| 6 | An `AssetBundle.m_Container` edit reaches the engine | added names appear in `GetAllAssetNames` and are reachable via `LoadAsset(name)` |
| 7 | Binary `TextAsset` is byte-exact through the engine | payload `00 FF C3 28 00 00 41 42 80 FE 00 7F` round-tripped ⇒ `.bnk`/`.wem` CAN ship inside the bundle; no loose-file fallback needed |
| 8 | AssetsTools.NET WRITES a bundle from inside PP's Mono | `rr_bake` 2026-08-12 09:49: read (`unity=2019.4.31f1 assets=93 cldbTypes=320`) → create Texture2D + TextAsset → `m_Container 4 -> 6` → `WROTE rr_baked.bundle 269874 B` → `LoadFromFile` that file → `TEX 16x16 RGBA32 centre=255,0,255,255`, `BIN len=12 hex=00-FF-C3-28-...`. No THREW, no LOAD FAILED, no TypeLoadException. Impl: `pp-native/src/Bake.cs`, commit `f111e5d` |

| 8a | The SAME write path reproduced from ContentTool's own productionized codebase (embedded `classdata.tpk`, no scratchpad path, AssetsTools.NET ILRepack-merged) | `ct_bake` 2026-08-12, twice in one session (11:12:17, 11:12:20), byte-identical both runs: `deleted stale ...\ContentTool\ct_selfcheck.bundle` → `WROTE ... 269907 B as ct_selfcheck / CAB-ct_selfcheck` (rename + CAB read back off the written fields, not the caller's constant) → `U0a PASS loaded, 6 asset name(s)` · `CONTROL PASS shipped asset 'Assets/Defs/Tactical/Actors/_Common/Mutoid/PLACEHOLDER_Mutoid_Head_Invisible_Ready.prefab' still resolves` · `U0b PASS TextAsset len=12 hex=00-FF-C3-28-00-00-41-42-80-FE-00-7F` · `U1 PASS 16x16 RGBA32 px[0,0]=255,0,255,255 px[8,8]=0,255,0,255` · `ALL PASS`. No THREW, no Unity error tapped across `LoadFromFile`. Impl: `src\Bake\BundleBaker.cs`, `src\Bake\BakeSelfCheck.cs`, commits `1121c3a`, `50c8489` |

Gate labels: **U0a** (UnityFS/SerializedFile load), **U0b** (binary TextAsset exact bytes),
**U1** (Texture2D), **U2** (static Mesh) — all PROVEN. They survive only as regression tests.

## Audio half (Wwise 2021.1.0 b7575, bank version 140)

| # | Closed fact | Evidence |
|---|---|---|
| 9 | Whole runtime audio path of a released mod = `LoadBankMemoryCopy(pinned bytes)` + `PostEvent` | FROZEN doc; Wwise inits at t=0.000, mods load at t=0.255 ⇒ all Ak APIs safe from `ModMain.OnModEnabled()` |
| 10 | Embedded short SFX bank plays from MEMORY | test A: bank 44324 B, `AK_Success bankId=1381173761`, POST dur=500ms mediaID=1381173765 `streaming=false(MEMORY)` endOfEvent=499ms |
| 11 | Streamed bank + loose `.wem` plays from FILE | test B: bank 132 B, `AK_Success bankId=1381174017`, POST dur=500ms mediaID=1381174021 `streaming=true(FILE)` endOfEvent=502ms |
| 12 | A media-only bank (`BKHD+DIDX+DATA`, NO HIRC) declaring the GAME's media ID REPLACES a shipped sound, with no `SetMedia` | test F (decisive): bank 44220 B; control dur=1200ms streaming=true(FILE); after `AK_Success bankId=1381175041` the same event = dur=**500ms** mediaID=18839791 `streaming=false(MEMORY)` endOfEvent=466ms |
| 13 | Replacement coverage is ALL media, embedded media included (measured set = 7697, see O3) | test G: media 272177053 in UIGeoscape DATA, control dur=101ms MEMORY → 500ms MEMORY after override |
| 14 | Bank hot reload works, same bankId reusable | test H2: `LoadBankMemoryCopy(v1)` → POST → `UnloadBank AK_Success` → `LoadBankMemoryCopy(v2, SAME bankId)` → POST 1870Hz |
| 15 | **Streamed `.wem` shipped INSIDE the bundle, extracted at load** — both arms | `rr_streamtest` 2026-08-12 10:12 (DLL 801792 B, commit `dc5f583`, `pp-native/src/StreamBundleSpike.cs`), both arms `streaming=true(FILE)`, 132 B bank carries no media |
| 15a | **ARM1 — `<modDir>\WwiseAudio\` + one own `AddBasePath`** (= production shape since the user decision of 2026-08-12; see FINAL-PLAN §4.4) | TextAsset 44164 B matches source, written bytes == TextAsset, `AddBasePath: AK_Success`, `LoadBankMemoryCopy: AK_Success bankId=1381232897`, `POST playingID=16 dur=500ms mediaID=1381232901 streaming=true(FILE) endOfEvent=499ms` |
| 15b | ARM2 — `Application.persistentDataPath`, `AddBasePath` NOT CALLED (proven, but NOT the shape the tool emits any more — superseded by 15a on 2026-08-12) | `bankId=1381233153`, `POST playingID=17 dur=500ms mediaID=1381233157 streaming=true(FILE) endOfEvent=501ms`. PP itself registers `AddBasePath(PersistentDataPath)` at init (`API/AK.Wwise.Unity.API.decompiled.cs:16632-16676`) |

| 10a | The SAME embedded-SFX path with the bank SHIPPED INSIDE THE BUNDLE — generated by ContentTool's own `BankGen` port, packaged as a binary TextAsset, read back out and handed to Wwise as those bundle bytes (gate **A1**) | `ct_audio` 2026-08-12 11:23:58: `bank 44324 B (wem 44164 B, mediaID 3338665985, bankID 2358375752, eventID 648566235) | selfcheck: clean` → `WROTE ...\ContentTool\ct_audio.bundle 311335 B as ct_audio / CAB-ct_audio, bank at 'assets/contenttool/audio/banks/selfcheck.bnk'` → `pre-run UnloadBank(2358375752): AK_UnknownBankID` · `RegisterGameObj: AK_Success` · **CONTROL** `POST CONTROL/before-bank-load: playingID=0 POST FAILED` (the event does not exist without our bank) · `A1a PASS bank TextAsset out of the bundle is 44324 B and byte-identical to the generated bank` · `LoadBankMemoryCopy: AK_Success bankId=2358375752` (`A1b PASS`) · `POST A1/bank-from-bundle-1760Hz: playingID=3 dur=500ms estDur=500ms mediaID=3338665985 streaming=false(MEMORY) endOfEvent=508ms` (`A1c PASS`) · `post-run UnloadBank: AK_Success` · `ct_audio: ALL PASS`. Same session, `ct_bake` 11:23:52 still `ALL PASS`. No THREW, no Unity error, no non-success AKRESULT other than the two expected `AK_UnknownBankID` pre-unloads. Impl: `src\Wwise\{BankGen,WwisePcm,WwiseId,AudioProbe}.cs`, `src\Bake\AudioSelfCheck.cs`, commit `c7bca37` |

Gate label **A1**: a generated v140 bank packaged in the bundle loads and plays. The rigour that
makes it readable: the packaging byte-identity (A1a) is asserted BEFORE and separately from any
Wwise call, the bytes handed to `LoadBankMemoryCopy` are the ones read out of the bundle, and the
control post runs in the SAME run before the bank exists. The audible beep is corroboration only.

| 15c | **ARM1 reproduced on ContentTool's own packaging** — our bundle, our stream manifest, our cache, our single `AddBasePath`, media in the MOD's folder (gate **A2**) | `ct_stream` 2026-08-12 11:39:43, commit `f709277`: `pre-existing D:\Steam\...\Mods\ContentTool\WwiseAudio\3338665986.wem: none` · **CONTROL** `POST CONTROL/before-bank-load: playingID=0 POST FAILED` · `extract: 1 stream(s), 1 rewritten | AddBasePath(D:\Steam\...\Mods\ContentTool\WwiseAudio\): AK_Success | 3338665986: wrote 44164 B` · `A2a PASS extracted 44164 B ... byte-identical to the packaged .wem` · `extract again: 1 stream(s), 0 rewritten | AddBasePath: already registered | 3338665986: cached` (`A2b PASS`, SHA-1 not mtime) · `LoadBankMemoryCopy: AK_Success bankId=1546164290` · `POST A2/streamed-from-bundle-1100Hz: playingID=3 dur=500ms mediaID=3338665986 streaming=true(FILE) endOfEvent=502ms` (`A2c PASS`) · `post-run UnloadBank: AK_Success` · `ct_stream: ALL PASS`. The bank carries no DIDX/DATA, and the only media path in the whole run is the mod folder — no `persistentDataPath` read. No THREW, no Unity error. Impl: `src\Wwise\StreamCache.cs` |

**ARM1 (`<modDir>\WwiseAudio\` + one setup-time `AddBasePath`) is the production shape** — user
decision 2026-08-12: a mod must be self-contained, and the AppData root must not accumulate flat
`<mediaId>.wem` from every mod built with this tool. 15b stays proven but is no longer emitted.
Shadowing, and any second `AddBasePath` call site, remain rejected.

The `ct_audio` / `ct_stream` pair was merged into a single `ct_audio` regression afterwards (one
bank, an embedded and a streamed sound side by side, gates A1/A2/A3) — the evidence above is what
it must keep reproducing. `ct_stream` no longer exists; row 15d is that merged run.

| 15d | **ONE bank carries both storage modes** — embedded and streamed sound side by side, each arm the other's control for the embed/stream flag (gate **A3**) | `ct_audio` 2026-08-12 11:47:45, commit `b4ce8a8`: `bank 44416 B (bankID 2358375752, embedded morgott_contenttool_selfcheck_tone/3338665985 eventID 648566235, streamed morgott_contenttool_stream_tone/3338665986 eventID 1711856237)` · **CONTROL** both pre-load posts `playingID=0 POST FAILED` · `extract: 1 stream(s), 1 rewritten | AddBasePath(...\Mods\ContentTool\WwiseAudio\): AK_Success` (`A2a PASS`), second call `0 rewritten | AddBasePath: already registered | 3338665986: cached` (`A2b PASS`) · `A1a PASS bank TextAsset ... 44416 B ... byte-identical` · `LoadBankMemoryCopy: AK_Success bankId=2358375752` (`A1b PASS`) · `POST A1/embedded-1760Hz: playingID=3 dur=500ms mediaID=3338665985 streaming=false(MEMORY) endOfEvent=499ms` (`A1c PASS`) · `POST A2/streamed-1100Hz: playingID=4 dur=500ms mediaID=3338665986 streaming=true(FILE) endOfEvent=507ms` (`A2c PASS`) · `A3 PASS one bank, two storage modes` · `ct_audio: ALL PASS`. Both arms carry a real `dur=500ms`, so neither reading is a missing callback. Same session, `ct_bake` 11:47:39 still `ALL PASS`. Multi-entry DIDX with 16-byte-aligned media offsets, 6 HIRC objects. Impl: `src\Wwise\BankGen.cs` (`Build(bankId, IList<Src>)`), `src\Bake\AudioBake.cs` |

| 15e | **15c/15d hold with replacement banks loaded** — the 2026-08-12 evidence was measured on `D:\Steam\…` with NO replacement bank in the session, so the rows read as unconditional guarantees they had never actually been tested for. RE-MEASURED 2026-08-27 on a cold `D:\PP-Instance2`, build `9af3d28e`, in the same session as `ct_sound: 6 shipped replacement bank(s) loaded from D:\PP-Instance2\Mods, 0 failed, 0 skipped` (IntroVideo 908611677, MenuMusic 208540756 + 423563089, ReplaceUiSounds 18839791 + 633458426 + 940964934) | `extract: 1 stream(s), 1 rewritten \| AddBasePath(D:\PP-Instance2\Mods\ContentTool\WwiseAudio\): AK_Success` · `A2a/A2b/A1a/A1b PASS` · `POST A1/embedded-1760Hz: playingID=1 dur=500ms mediaID=3338665985 streaming=false(MEMORY) endOfEvent=501ms` (`A1c PASS`) · `POST A2/streamed-1100Hz: playingID=2 dur=500ms mediaID=3338665986 streaming=true(FILE) endOfEvent=508ms` (`A2c PASS`) · `A3 PASS` · `A4 PASS` · `ct_audio: ALL PASS`. A loaded replacement bank does NOT claim a media it does not carry, so the stream arm is not environment-dependent. **Reading the failure mode:** `AudioProbe.Post` zeroes `media`/`streaming` before every post and only writes them from `AK_Duration`, so an `A2c FAIL mediaID=0 … streaming=false(MEMORY)` is NOT "resolved from memory" — it is NO DURATION CALLBACK (`GotDuration == false`), the same signature an unloaded/unreachable media gives (SoundLoad's UnloadMod ceiling). Judge the arm by `dur=`, never by the printed `streaming=` alone |
| 16 | The Wwise ID validator **rejects**, not merely runs — the collision matrix (FINAL-PLAN 9.4) against the loaded measured set, inside the game | `ct_audio` 2026-08-12 11:54:02, commit `974c131`: `A4 PASS index holds 7697 media + 1064 computed IDs; PP media 18839791 refused=True, allowed once declared as a replacement=True, name hashing onto PP event 784388130 refused=True, our own sound accepted=True`. Both negative arms are real shipped IDs (`18839791` is the media of test F; `fnv1_lower32("GUI_StatsPlusClick") == 784388130`), so a validator that silently accepted everything would fail this row — which is the failure mode that matters, since a shadowed ID produces no runtime error at all. The set is loaded from the embedded `lib\ppids.bin` (packed by `tools\pack_id_index.py`), never a constant. Same run: `UnregisterGameObj: AK_Success`, `ct_audio: ALL PASS`. Impl: `src\Wwise\IdIndex.cs`, `AudioBake.Validate` |

Rigour of #15 (keep this shape in regression tests): the 132 B bank carries NO DIDX/DATA so no
in-memory copy can exist; any pre-existing `.wem` at the target path was deleted and logged;
extraction byte-equality was asserted SEPARATELY from the Wwise result. Media IDs regenerate
per run — never hardcode one.

### Where the game plays music, and where it does not — measured 2026-08-12 (`ct_music`)

Instrument: `src\Dev\MusicProbe.cs`, `ct_music probe [waitSeconds] | gate <savename>`. Emitters from
two independent sides — the game's own `WwiseBanksEventsTracker` private maps by reflection, and every
`AkGameObj` in the scene — then `GetPlayingIDsFromGameObject` → `GetEventIDFromPlayingID` → the name
and Wwise Object Path read off the shipped `<bank>.txt` (764 events across 53 banks; no constant ID).
Measured on `D:\PP-Instance2` with demo mod #1 **reverted first and the revert proved by hash in the
same session**, so none of it is read through another mod's bank patch.

| Screen | Reading |
|---|---|
| Main menu | **OCCUPIED** — `MUSIC playingID=2 event=799408924 'MainMenuMusicStart' bank=MainMenuMusic path=\Music\MainMenuMusicStart persisted=True on 'HomeScreenLevel(Clone)'` (build `e872bcd0` in-phase) |
| Geoscape | **OCCUPIED** — `MUSIC playingID=4 event=276039877 'GeoscapeMusicStart' bank=GeoscapeMusic path=\Music\GeoscapeMusicStart persisted=True on 'GeoscapeLevel(Clone)' (_eventsOnPlayingStart)`, beside three non-music voices (`GUI_EncounterButtonHover`, `GUI_ChoiceClick`, dialogue `PROG_SY0_WIN_E2`). Reached unattended by `ct_music gate autosave` (build `fdedd33e` in-phase) |
| Tactical (in a mission, enemy turn, mission end) | **UNMEASURED** — neither occupied nor silent. Phoenix Point does not let a player save inside a mission, so no tactical save exists to reach one unattended, and the surface was dropped rather than guessed at. Do not read the two rows above as covering it |

**Music is bound to the LEVEL, not to a UI state.** The geoscape music voice is registered in
`_eventsOnPlayingStart` on the LEVEL's own GameObject; that map is replayed only on the
`Loaded->Playing` transition and cleared by `PlayEndEvents` on `Unloading`
(`Base.Audio\AudioManager.cs:88-95`), fed by `GamestateSoundDef.StartEvents`
(`PhoenixPoint.Common.Levels\GamestateSound.cs:44-70`). **Research, base and roster are UI states
inside the geoscape level and therefore inherit its music** — they are not silent, and a mod cannot
"add music where none plays" there. Consequence for authoring: a genuinely NEW trigger means adding
a bank + start event to a `GamestateSoundDef`, which lives in `_common_assets_all.bundle` (420 MB)
and is not a catalog key (4119 internal ids, 4 under `/Defs/`) — so route iii cannot reach it and
route vii would ship a 420 MB copy. Measured ceiling, not a tool limitation.

Two more findings that are expensive to re-derive:
- **There is no empty placeholder to fill.** All 55 shipped `<bank>.txt` "Streamed Audio" tables
  checked against the folder: **0** media IDs lack a `.wem`, and the smallest shipped `.wem` is
  **3971 B**. So the cheap shape "an event the game fires that resolves to nothing" does not exist.
- **`Symes_Enemy` is structurally absent** from `TacticalMusic.bnk`: every `MusicPlaylist` switch owns
  both a `Tactical\X_Player` and `Tactical\X_Enemy` interactive-music node except Symes (Player only),
  so Symes missions go silent on enemy turns. Filling it is HIRC surgery in an 18.9 MB shipped bank.
- **Vanilla vs TFTV**: any vanilla music target stays vanilla. `TFTVAudio.cs` only re-routes event
  posting; `TFTVVanillaFixes.cs:2697-2726` only ducks the Music mixer level. TFTV adds and replaces
  no bank or media.

**AN INSTRUMENT THAT BLOCKS THE MAIN THREAD MANUFACTURES THE ABSENCE IT THEN REPORTS.** The first
version of this probe pumped Wwise from a `while` loop on the main thread (the shape `AudioProbe.Post`
legitimately uses for a sound it posted itself). That stops Unity's player loop — which is the thing
that posts a level's start events — so the probe **froze the game it was measuring** and read
`attempt 14, 8498 ms into the wait, 3 emitter(s), 3 queried, 0 live voice(s)` on a main menu that was
audibly playing music. It was caught by the arming rule printing VOID rather than SILENT, not by luck.
Any check that waits on the game must yield (coroutine), never block. The lesson generalises to every
timing arm in this project.

Second measured constraint on the same instrument: **`GetSourcePlayPosition` returns `AK_Fail` for
every voice the GAME posted** — Wwise tracks a source position only when the post asked for it
(`AK_EnableGetSourcePlayPosition`), and Phoenix Point never does. "The position advanced" therefore
cannot be an arming rule for game-posted voices; arming is "a live voice the shipped `<bank>.txt`
NAMES", which proves enumeration and identification in-run. Zero named voices reads **VOID**, never
SILENT. The SILENT arm is **UNRUN** — every screen measured so far is occupied, so the verdict that
would name a silent surface has never fired on real data and is not to be treated as working until it
does.

**What this closed, and what it did not.** It closed the question demo mod #2 was opened for: there is
no silent surface among the screens a player spends the game on, so "add music where the game plays
none" has no honest target there, and the mod was cancelled rather than shipped against a manufactured
one. It did NOT close tactical — see the UNMEASURED row above.

## Unity gates opened by ContentTool itself

| # | Closed fact | Evidence |
|---|---|---|
| U3a | **A generated Material survives the bundle, with its texture reference as an INTERNAL PPtr and its float property intact** | `ct_bake` 2026-08-12 12:15:28, commits `0b2a5e9` + `c6507c5`: `U0a PASS loaded, 7 asset name(s)` · `CONTROL PASS shipped asset '...PLACEHOLDER_Mutoid_Head_Invisible_Ready.prefab' still resolves` · `U0b`/`U1 PASS` · `U3a PASS Material 'assets/contenttool/materials/selfcheck' loads from the bundle` · `U3a-refs PASS shader fileID=0 pathID=0 \| _MainTex -> fileID=0 pathID=8764458043431755281 \| _Glossiness=0.25` · `ct_bake: ALL PASS`. `m_Shader` is deliberately null (production assigns it from a PP Def donor at runtime, FINAL-PLAN 10.1). **The property block is read off the FILE** (`BundleBaker.ReadMaterialProperties`, before the engine opens the bundle) because a shaderless Material has no property sheet — `Material.GetTexture` would report nothing on perfectly correct data, so the engine API cannot be the oracle for this gate. Still open: U3b runtime donor-shader resolution, U3c properties surviving that assignment |

| U3d | **A hand-forged EXTERNAL PPtr into a shipped bundle resolves at runtime, ONCE THE TARGET BUNDLE IS LOADED** — a baked Material whose `m_Shader` points into another serialized file gets the real shader. The precondition is not optional and not a clock: an external resolves through the archive VFS (`archive:/cab-x/cab-x`), which exists only while that bundle is open | `ct_bake` 2026-08-12 12:45:10, commit `4aa7148`, Player.log 914-959: `read mutoid_assets_all.bundle: unity=2019.4.31f1 assets=93` → `WROTE …ct_selfcheck.bundle 270435 B` → `U0a/U0b/U1/U3a/U3a-refs PASS` (the shaderless material still reads `shader fileID=0 pathID=0` off the file) · `U3d-wrote PASS shader fileID=1 pathID=952725256833404699 (external 'cab-207b1100b7c0eac21654e77dc25fa206' is fileID=1)` · `Shader.Find("Standard") != null: True` · `U3d PASS forged external m_Shader {fileID=1, pathID=952725256833404699} -> 'Standard'` · `U3d-ctl-badid PASS same external, pathID+1 -> 'Hidden/InternalErrorShader'` · `U3d-ctl-noptr PASS the U3a material … reports 'Hidden/InternalErrorShader'` · `ct_bake: ALL PASS`. No THREW, no Unity error tapped. Impl: `BundleBaker.ExternalIdOf` + two optional `AddMaterial` params, `BakeSelfCheck`. **Re-proven unattended and timing-free** (`autogate.ps1 -Commands ct_bake`, 2026-08-12, build `9420e85f` confirmed in-phase, gate at t≈1.835 s): the gate now MOUNTS `defaultlocalgroup_unitybuiltinshaders.bundle` itself and re-opens the bake against it — `U3d-premount PASS with 'defaultlocalgroup_unitybuiltinshaders.bundle' NOT loaded the forged external reports 'Hidden/InternalErrorShader'` · `U3d PASS … -> 'Standard'` · `U3d-ctl-badid PASS … -> 'Hidden/InternalErrorShader' (expected 'Hidden/InternalErrorShader')` · `U3d-ctl-noptr PASS … (expected 'Hidden/InternalErrorShader')` |

| U3e | **An externals entry WE append also resolves, under the same precondition as U3d** — not just a forge into one the file already had; the bundle the appended entry names must be loaded | `ct_bake` 2026-08-12 13:42 (Player.log 884-941), after the `b7df4b9` fix: `WROTE …ct_selfcheck.bundle 176888 B` · `U0a PASS loaded, 12 asset name(s)` · `U3e-wrote PASS shader fileID=2 pathID=-1622058160989239334 (our added external 'cab-22f8ff865f4ca3fac668dbcaedfdbb9d' is fileID=2, beyond the clone's own 1)` · `Shader.Find("_PX_CHR/CHR_Character_shader") != null: True` · `U3e PASS an externals entry WE added, {fileID=2, pathID=-1622058160989239334} -> '_PX_CHR/CHR_Character_shader'` · `U3e-ctl-badid PASS our external, pathID+1 -> 'Hidden/InternalErrorShader'` · `U3e-ctl-wrongfile PASS same pathID through the clone's OWN external (fileID=1) -> 'Hidden/InternalErrorShader'` · U3d and its controls green in the same run · `ct_bake: ALL PASS`. Impl: `BundleBaker.AddExternal`. **Re-proven unattended and timing-free** in the same `9420e85f` run, with `_shaders_assets_all.bundle` mounted by the gate: `U3e PASS … -> '_PX_CHR/CHR_Character_shader'` · `U3e-ctl-badid PASS … (expected 'Hidden/InternalErrorShader')` · `U3e-ctl-wrongfile PASS … (expected 'Hidden/InternalErrorShader' - a different file)` · `ct_bake: ALL PASS` |

What U3e adds over U3d: U3d forged a PPtr into a CAB the cloned bundle **already declared**, so the
externals table was still the game's. U3e **appends the entry** — `archive:/cab-x/cab-x`, all-zero
GUID, type Normal, the shape measured in the shipped files — and the engine resolves through it.
`U3e-ctl-wrongfile` is what makes that readable: the same pathID through the clone's own
pre-existing external returns the error shader, so the added entry is doing the work rather than the
fileID landing somewhere useful by accident.

| U4 | **A GameObject hierarchy baked from nothing loads as a hierarchy** — root GameObject+Transform and a child GameObject carrying Transform+MeshFilter+MeshRenderer, six new objects wired only by INTERNAL PPtrs, addressed by ONE m_Container entry on the root | `autogate.ps1 -Commands ct_bake` 2026-08-12, build `0cc35ed4` asserted in-phase: `U0a PASS loaded, 14 asset name(s)` · `U4-wrote PASS root 'u4_root' comps=1 children=1 \| child 'u4_child' comps=3 fatherIsRoot=True pos=1.5,-2.25,3.75 scale=1,1,1 mesh='Geo_Head02_V01' material='extshader'` (read off the FILE, references reported by the target's NAME) · `U4-wrote-ctl-lone PASS root 'u4_lone' comps=1 children=0` · `U4 PASS 'u4_root' childCount=1 child='u4_child' localPosition=1,5,-2,25,3,75 MeshFilter.sharedMesh='Geo_Head02_V01' MeshRenderer.sharedMaterial='extshader'` (engine side: `LoadAsset<GameObject>` on the root, then `transform.GetChild(0)`) · `U4-ctl-lone PASS the root baked with NO child reports childCount=0`. Layout MEASURED off `aln_egg_explosive_assets_all.bundle` (2019.4.31f1) — the one shipped bundle under 30 MB carrying a MeshFilter+MeshRenderer pair — field by field, including `ComponentPair`'s single `component` field and the renderer's non-zero defaults (`m_RayTracingMode=2`, `m_LightmapIndex=65535`, `m_LightmapTilingOffset=1,1,0,0`). Impl: `src\Bake\PrefabFields.cs`, `BundleBaker.AddPrefab` + `ReadPrefabSummary`; offline round trip `tests\ObjCodecTests\PrefabRoundTrip.cs` |

Falsified in the same shape as it is claimed (2026-08-12, build `cb377dd2`): zeroing the child's
`m_Father` turned `U4-wrote` RED (`fatherIsRoot=False`) and the offline round trip threw. **What that
run also measured, and it matters for U5/U6:** the engine builds the parent link from the ROOT's
`m_Children` alone — with `m_Father` zeroed Unity still reported `childCount=1`, so a half-written
hierarchy is INVISIBLE to the engine arm. `U4-wrote` is the arm that catches it; do not drop it.

Ceilings of U4, deliberate: the MeshFilter points at a Mesh the cloned bundle already ships
(serializing a Mesh object from an empty template is U5's problem), and the renderer's material is
one of ours but its SHADER is an external reference — U3d's question, not U4's.

| U5 | **A SKINNED model baked from nothing loads AND deforms** — a Mesh serialized from the EMPTY class-database template (the ceiling U4 left), carrying bind poses, bone name hashes and per-vertex weights, plus two chained bone GameObjects and a SkinnedMeshRenderer wiring `m_Bones`/`m_RootBone`/`m_Mesh` | `autogate.ps1 -Commands ct_bake` 2026-08-12, build `71ec378f` asserted in-phase: `U5-wrote PASS root 'u5_root' children=2 \| skin 'u5_root_skin' bones=2 bone0='u5_root_bone0' bone1='u5_root_bone1' rootBone='u5_root_bone0' mesh='u5_root_mesh' material='extshader' \| mesh verts=4 bindposes=2 bindpose1.e13=-2 hashes=2:2929495074:1084240277 rootHash=2929495074 bonesAABB=2 weightCh=stream1/off0/fmt0/dim2 indexCh=stream1/off8/fmt10/dim2 bytes=192 vertex0=1/0->bone0 vertexLast=1/0->bone1` · `U5-wrote-ctl-flat PASS … vertexLast=1/0->bone0` · `U5 PASS sharedMesh='u5_root_mesh' vertexCount=4 bones=2 [u5_root_bone0,u5_root_bone1] rootBone='u5_root_bone0' bindposes=2` · **`U5-deform PASS rest y=[0,2], after lifting 'u5_root_bone1' by 10 y=[0,12]`** (instantiate → `SkinnedMeshRenderer.BakeMesh` before and after moving the bone) · `U5-ctl-flat PASS rest y=[0,2], after lifting 'u5_flat_bone1' by 10 y=[0,2]` · `ct_bake: ALL PASS`. Impl: `src\Bake\SkinFields.cs`, `BundleBaker.AddSkinnedPrefab` + `ReadSkinSummary`, 14-channel fill in `MeshFields.Fill`; offline round trip `tests\ObjCodecTests\SkinRoundTrip.cs` |

**What U5 had to MEASURE first** (2026-08-12, off shipped 2019.4.31f1 bundles — `mutoid` Mesh
`Geo_Head02_V01` + its SkinnedMeshRenderer, `aln_fireworm` `ALN_Fireworm`, `aln_poisonworm`).
None of it is a remembered Unity layout:

- **Skin data lives in its OWN vertex stream.** Shipped: stream 0 position/normal/tangent, stream 1
  uv0, stream 2 channel **12** `BlendWeight` (format 0 = float32, dimension **2**) at offset 0 and
  channel **13** `BlendIndices` (format **10** = UInt32, dimension 2) at offset 8. **Two** influences
  per vertex, not four.
- **Every stream starts 16-byte aligned.** `ALN_Siren_Arm_Slasher_Right`, 1783 verts:
  71320+8 \| 14264+8 \| 28528 = **114128 B**, which is exactly its `m_DataSize`. That padding is the
  only reason the arithmetic closes.
- **A Mesh from the empty template carries ZERO channels** — every shipped Mesh carries exactly
  **14** slots, used or not, and the slot INDEX is the semantic. This is the ceiling U4 flagged;
  `MeshFields.Fill` now grows the array to 14 before filling it.
- **`m_BoneNameHashes` is CRC-32 (reflected `0xEDB88320`, final xor) of the bone's model-relative
  transform PATH, not of its name.** `aln_fireworm`'s `Fireworm_head` carries `638923553`, which is
  `crc32("Fireworm_root/Fireworm_head")`; `crc32("Fireworm_head")` is `1476905596`. The offline test
  asserts that shipped number, so the identification is pinned to the game's own data.
  `m_RootBoneNameHash` is the root bone's own entry.
- **The SkinnedMeshRenderer's defaults are NOT the MeshRenderer's.** `m_RayTracingMode` is **0** here
  and 2 there; `m_SkinnedMotionVectors` is true. Copying U4's constants would have been wrong.

Falsified in the same shape as it is claimed (2026-08-12, build `07dc8c46`): binding every vertex to
bone0 while still asking for the split turned `U5-wrote` RED (`vertexLast=1/0->bone0`) and
`U5-deform` RED (`y=[0,2]` where `[0,12]` was expected), the offline round trip threw, and **both
control arms stayed green** — which is what makes the deformation arm non-vacuous: it cannot pass on
a renderer that merely follows a transform, and it cannot pass on a bake with no weights.

Ceilings of U5, deliberate: two bones and one full-weight influence per vertex (an `.obj` carries no
skin data to import, so the weights are SYNTHESISED by a y-split); no blend shapes / morph targets.
~~and the shipped-mesh REPLACEMENT path still does not rewrite the skin~~ — **closed by U5b below.**

| U5b | **A REPLACEMENT on a rigged shipped target comes out SKINNED** — `SkinFields.Rebind`, called by `BundleBaker.ReplaceMesh` right after `MeshFields.Fill`, keeps the target's `m_BindPose` / `m_BoneNameHashes` / `m_RootBoneNameHash` untouched (so they stay in step with the SkinnedMeshRenderer's own `m_Bones`, which it never sees) and derives the weights from the bind poses alone: each new vertex goes whole to the bone whose bind pose brings it CLOSEST to that bone's origin, `m_BonesAABB` re-measured per bone in bone space | `autogate.ps1 -Commands ct_bake,ct_project` 2026-08-12, build `8de79959` asserted in-phase. Deformation arm, on a prefab baked FLAT and then re-bound — the identical bake WITHOUT the `Rebind` call is `U5-ctl-flat`, and only one of the two moves: **`U5b-deform PASS rest y=[0,2], after lifting 'u5b_rebind_bone1' by 10 y=[0,12]`** vs `U5-ctl-flat PASS … y=[0,2]` · `U5b-wrote PASS … vertex0=1/0->bone0 vertexLast=1/0->bone1` (the SPLIT expectation asserted on a bake that asked for no split — nearest-bindpose produced it) · on the real armour, `P5 PASS mesh 'CHR_PX_ASS_TS_M_V01_02' in the copy is SKINNED to the shipped skeleton -> bindposes=10 hashes=10 rootHash=2424243207 bonesAABB=10 weightCh=stream1/off0/fmt0/dim2 indexCh=stream1/off8/fmt10/dim2 skinBytes=64 boneMax=0 inRange=yes` and `P5 PASS … 'CHR_PX_ASS_TS_F_V01' … bindposes=13 … boneMax=12 inRange=yes` — the expected skeleton is not a constant, it is read off the SHIPPED file in the same run, so Rebind clobbering it reads RED too · `ct_bake: ALL PASS` · `ct_project: ALL PASS`. Impl: `SkinFields.Rebind` + `SkinSummary`, `BundleBaker.ReplaceMesh`; offline `tests\ObjCodecTests\MeshRoundTrip.cs` (real rigged target) + `SkinRoundTrip.cs` |

**Measured while closing U5b:** the Assault torsos carry **4** influences per vertex
(`weightCh=stream2/off0/fmt0/dim4 indexCh=stream2/off16/fmt10/dim4`), not the 2 the mutoid/fireworm
meshes U5 measured carry — the count is per mesh, declared by the channel `dimension`, so writing 2
over a 4 target is legal and the gate proves the engine reads it.

**FALSIFIED twice, build `07304eb1` and `6ed6d7c8`.** (A) `Rebind` returning early, doing nothing —
this is EXACTLY the pre-U5b code — turned `U5b-wrote` RED, `U5b-deform` RED (`y=[0,2]`) and both `P5`
arms RED (`weightCh=stream0/off0/fmt0/dim0`), while `P4`, `P4-ctl-shipped` and every U5 arm stayed
green: P4 passing on a mesh with no skin at all is the defect, printed. (B) the nearest-bone search
disarmed so every vertex lands on bone 0 turned `U5b-deform` RED (`y=[0,2]`) and threw the offline
round trip, with `U5-deform` and `U5-ctl-flat` still green.

**EYE-CONFIRMED in-game 2026-08-12** (commit `0098342`), so U5b is no longer gate-green only. Roster /
character screen, Phoenix Assault soldier, torso `CHR_PX_ASS_TS_M_V01_02` replaced by the sample's
4-vertex `blade.obj` quad:
- the quad sits DOWN at LEG height, not at the torso — the character mesh's local origin is at the feet,
  so nearest-bindpose bound the whole quad to the PELVIS bone. Expected, not a defect.
- the quad MOVES with the idle animation, riding that one bone. That is the deformation proof at
  RENDER level, matching the `U5b-deform` gate arm — a static replacement could not move at all.
- the armour TEXTURE stretches over the 4 vertices — expected: only the mesh was replaced, the material
  is still the shipped one.
This is the declared ceiling behaving exactly as documented (one bone per vertex, rigid), nothing more.

~~Ceiling of U5b, deliberate: ONE full-weight influence per vertex, so a vertex follows exactly one
bone and a joint creases instead of bending. … Smooth weights need a skinned interchange format
(`.fbx`/`.gltf`) carrying its own `m_BoneWeights`~~ — **RETIRED 2026-08-12 by P6/R6 below.** That
format is `.glb` and it is now read: a `.glb` carrying an armature binds with its OWN `WEIGHTS_0`
onto the SHIPPED skeleton, bones matched BY NAME, so joint ORDER is free and a vertex can be shared
between bones. What survives of the ceiling: `.obj` (the format carries no skin data) and a FOREIGN
armature fall back to the nearest-bone synthesis described above — one full-weight influence per
vertex — and the bake log SAYS SO, so the downgrade is never silent. Weight TRANSFER from the
target's own vertices stays rejected, unchanged.

| P6 / R6 | **A mesh REPLACEMENT carries the FILE's OWN skin weights onto the SHIPPED skeleton.** `Content\Meshes\` accepts `.glb` beside `.obj` on the REPLACE path (previously `.obj` only, `ContentProject.cs:30`; the refusal was `ProjectBake.cs:480`). Bones are matched BY NAME, so the file's joint ORDER is free. The same path is live in the dev workbench: `ct_replace …@SkinnedMeshRenderer.mesh` | Commits `e7ece42` (feature), `9491b89` (vertex-count guard), `683f2b9` (autogate guard). **P6, the BYTES** — shipped `CHR_PX_ASS_TS_F_V01`, 13 bones, fixture joints REVERSED, read back out of the WRITTEN bytes: `v0=1/0->bone0+bone0`, `v1=0.5/0.5->bone0+bone12`, `v2=1/0->bone12+bone12`. 3 of 3 vertices land on a FILE slot that is not the live index, and the **0.5/0.5 is load-bearing**: nearest-bone yields one full-weight influence and cannot produce a fraction. **R6, the EFFECT** — live 14-bone rig, fixture joints REVERSED: lifting `'Spine_2'` moved `v0=(0,-0.002,-0.25) v1=(0,-0.001,-0.125) v2=(0,0,0)`, exactly what the FILE's weights predict; the control lifts `'R.UpLeg'` and gets the mirrored triple. Mod proven LOADED, not merely launched: in-phase `confirmed build=8a398f20` plus real `ct_` output. **PORTED, NOT INVENTED:** `SkinBinder` was already in the tree with NO caller at `src\Import\GlbReader.cs:1184` |

**FALSIFIED in game in ONE launch** (`a8c2742f`), both name maps disarmed: `R6-remap` and
`R6-CONTROL-bone` went RED, and P6 read the exact transposition `v0=1/0->bone12+bone12`,
`v1=0.5/0.5->bone12+bone0`, `v2=1/0->bone0+bone0`. Reverted; tree clean against HEAD.

**Anti-vacuity lesson, worth more than this row:** with 3 bones the MIDDLE slot is a FIXED POINT of a
reversal — a fixture that picks it measures NOTHING. Pick the FIRST and the LAST.

Project rule confirmed again by `SkinBinder`: a ContentTool "ceiling" is usually unported Resource
Replacer code — grep the donor before writing a limitation down.

| P7 | **A SHIPPED AnimationClip's own CURVES are edited in place.** One `replace` entry, `"clip": "position*3"`, scales one channel of a clip the game ships — same name, same bindings, same bank sizes, so every controller that plays it keeps playing it and no runtime code is involved. `ClipFields.MapCurves` (`src\Bake\ClipFields.cs`), `BundleBaker.ReplaceClipCurves` / `ReadClipCurves`, grammar + arms in `ProjectBake` | **PROVEN IN GAME** — `autogate.ps1 -PPRoot D:\PP-Instance2 -NoDeploy -Commands ct_project -UnityAudio`, build `9acc120b` asserted in-phase, `ct_project: ALL PASS`. `P7 PASS clip 'Fireworm_unfurl' position*3: all 195 curve float(s) in the copy are the shipped value x 3 … the largest the edit moved is float 66, shipped -0.994065 -> copy -2.982194 (expected -2.982194)` · `P7-ctl-channel PASS the copy's attribute-3 curves still ARE the shipped ones - all 30 float(s), first 1 and the copy reads 1` · **`P7-sample PASS 'Fireworm_unfurl' sampled on the rig: 1 of 16 transform(s) moved, the furthest is 'Fireworm_base' at (-0.006446,0.128418,0.138676) with the shipped clip and (-0.019338,0.385253,0.416029) with the edited one (expected … the author's x3)`** — the ENGINE's own answer, both clips mounted from a real bundle one at a time (same CAB). **FALSIFIED in game** (`aef18fed`) with the scale disarmed to `v => v`: `P7 FAIL … the largest the edit moved is float 7, shipped 0.194689 -> copy 0.194689 (expected 0.584066)` and `P7-sample VOID the sampled rig (16 transform(s)) moved by at most 0 …` — VOID, never a silent PASS. Restored, re-run green (`9acc120b`). Offline half, same tree: `dotnet run --project tests\ObjCodecTests`: `CLIP edit PASS on aln_fireworm_assets_all.bundle 'Fireworm_unfurl' position*3 — attribute 1: 10 binding(s), 30 of 100 curve float(s) — 45 streamed key(s), 0 dense curve(s) × 6 frame(s), 15 constant value(s), 30 delta(s); 195 float(s) walked · largest float 66: -0.9940646 -> -2.982194 · 616 rotation float(s) untouched · m_MuscleClipSize 7536` (unchanged). The expectation is the SHIPPED file read in the same run × the author's own factor, never a constant written into the assertion. **Falsified**: with the streamed write-back disarmed the round trip went RED naming the float and both numbers — `CLIP round trip FAILED: float 7 is 0,1946886 x 3 = 0,5840658, the copy reads 0,1946886` |

**Where a shipped clip actually keeps its curves — and why a dense-only editor would have measured
nothing.** A BAKED clip (U6) writes the DENSE bank. A SHIPPED clip does not use it: `Fireworm_unfurl`
= 47 streamed curves + 53 constant floats, `m_FrameCount` 6 but `m_CurveCount` **0**;
`MV_RocketJumpIdle` = 40 constant floats and nothing else. Both banks are reached under the one flat
curve index, and the streamed bank parses as `{float time; int keyCount; keyCount × {int curveIndex;
float coeff0..3}}` — pinned by consuming `Fireworm_unfurl`'s array EXACTLY (999/999 uints, 7 frames).
A key is a cubic in those coefficients and evaluation is linear in them, so one factor over all four
is that factor on the sampled value at every time.

**The binding widths are CHECKED, not assumed.** Position 3, rotation 4, scale 3, and anything else
(a weapon clip carries 7 such bindings) taken as 1 — then the total is compared to the number of
floats the three banks hold and the clip is REFUSED unless they are equal. `Fireworm_unfurl` closes
at 100 = 100. Without that check one odd binding would shift every later index and the edit would
land on a neighbour's curve, silently.

`rotation*k` is refused BY NAME: a rotation curve is a quaternion and scaling one denormalises it.

**Three things the in-game half cost, all of them silent failures:**
- **`AssetBundle.LoadAsset<AnimationClip>(name)` cannot reach a shipped clip.** It is a SUB-ASSET of
  its `.fbx`, and `m_Container` registers the `.fbx` PATH — `aln_fireworm` registers
  `ALN_Fireworm_Ball.fbx` seven times, once per sub-asset. `LoadAllAssets<AnimationClip>()` and match
  on `.name`. First run read `does not hand out an AnimationClip named 'Fireworm_unfurl'` — VOID.
- **`SampleAnimation` needs an `Animator` on the sampled object for a NON-LEGACY clip** outside the
  Editor, or it writes NOTHING and the clip samples as a still rest pose. Ported literally from
  `ResourceReplacer\pp-native\src\MeshReplacer.cs:930` (the donor's own comment names the engine
  message). No controller needed. Second run read `moved by at most 0` — VOID, which is exactly what
  a rest pose looks like.
- **A transform the clip does not BIND must not be asked to scale.** It keeps the prefab's rest value
  under both clips, and a rest pose is not part of the curve. Comparing every transform against
  `rest × factor` turned an engine result that was exactly ×3 into `P7-sample FAIL` on its third run.
  The arm now asks the ×factor question where the pose DIFFERS and asserts bit-identity where it does
  not — with the VOID guard (nothing moved by > 0.01) carrying the anti-vacuity.

| U6 | **A baked AnimationClip drives a transform, and MECANIM plays it through a baked AnimatorOverrideController** whose base is a SHIPPED `AnimatorController` reached by an external PPtr. `ClipFields.FillClip` writes the clip, `FillOverrideController` the 3-field AOC, `Build` the root+Animator+bone hierarchy | `autogate.ps1 -Commands ct_bake` 2026-08-12, build `f9e6476a` asserted in-phase. Three DIFFERENT numbers, one per arm, none of them the rest pose by accident: `U6-sample PASS 'u6_bone' rest y=-3.5, sampled at 0.5 s y=15 and at 1 s y=30` (`AnimationClip.SampleAnimation`, no controller in the loop — the clip alone) · `U6-mecanim PASS controller='u6_aoc_ramp' (AnimatorOverrideController), 'u6_bone' rest y=-3.5, after Animator.Update(0.5) y=15` · `U6-mecanim-ctl-flat PASS … y=0` (same base, same Animator, only the OVERRIDE clip differs — and it drives the bone off its −3.5 rest onto exactly 0, so the control is not "nothing happened") · `U6-mecanim-ctl-path PASS … 'u6_other' … y=-3.5` · `U6-aoc PASS … is a AnimatorOverrideController whose clips are [u6_ramp]` · `U6-clip PASS 'u6_ramp' legacy=False frameRate=30 length=1` · `U6-wrote PASS clip 'u6_ramp' bindings=1 path=1600060467 attr=1 typeID=4 dense=31x3@30 samples=93 first=(0,0,0) last=(0,30,0) streamed=2/0 const=0 delta=3 index=200 stop=1 muscleSize=2932 legacy=False \| aoc 'u6_aoc_ramp' controller=fileID3/pathID-8389213721431673559 overrides=1 original=fileID3/pathID-2054101139859036125 override='u6_ramp'` · `ct_bake: ALL PASS`. Impl: `ClipFields`, `BundleBaker.AddAnimationClip` / `AddAnimatorOverrideController` / `AddAnimatedPrefab`; offline `tests\ObjCodecTests\ClipRoundTrip.cs` |

**The three questions U6 was opened to settle, answered by measurement:**
- **Does an `AnimatorOverrideController` survive as a TYPE in a baked bundle? YES.**
  `LoadAsset<RuntimeAnimatorController>` hands back an object whose `GetType().Name` is literally
  `AnimatorOverrideController` and whose `animationClips` lists OUR clip. Asserted by the type NAME,
  not by non-null-ness: an AOC that came back as a plain `AnimatorController` is the interesting
  failure and a null test cannot tell those apart. `_common` ships one of its own
  (`ArmadilloHulkCrateAnimator`, 96 bytes) — three fields, `m_Controller` + `m_Clips` pairs.
- **Does Mecanim accept a clip from a FOREIGN file? YES** — and the base controller may live in a
  foreign file too. `u6_aoc_ramp` overrides `_common`'s `MedKitHeartBeat1` clip with ours, the
  Animator's `runtimeAnimatorController` resolves through a forged external PPtr, and one
  `Animator.Update(0.5)` puts the bone at 15. **This is the cheap route and it holds: no
  `ControllerConstant` is serialized at all** — the shipped state machine is reused verbatim and only
  the clip is ours. `MedKitHeartBeat1` is the base because it is the simplest controller Phoenix
  Point ships: ONE layer, ONE state, no transitions, no parameters, `m_DefaultState 0`, whose single
  blend-tree node plays `m_AnimationClips[0]` — local to that same file.
- **What "no automatic humanoid retargeting" MEANS, measured.** A curve is addressed by
  `GenericBinding.path` = **CRC-32 (reflected `0xEDB88320`) of the transform's path relative to the
  ANIMATOR's GameObject** — the same function `SkinFields.BoneHash` already identified for
  `m_BoneNameHashes`, which is why it is reused and not copied. `Fireworm_unfurl` binds `1095908316`
  = `crc32("Fireworm_root/Fireworm_base")`, and the animator root itself would be `crc32("")` = 0.
  So a clip is bound to PATH SPELLINGS, and `U6-sample-ctl-path` / `U6-mecanim-ctl-path` measure the
  consequence: the identical clip and controller on a hierarchy whose bone is named `u6_other`
  leaves it at its `-3.5` rest, at every sample time. **A mod author must author the clip against the
  exact skeleton paths of the model it ships with; there is no rescue layer.**

**The AnimationClip layout, MEASURED 2026-08-12** off `aln_fireworm` `Fireworm_unfurl`,
`px_equipment` `MV_RocketJumpIdle`/`Reload`/`Bind_Pose`/`MV_RocketJumpStartA`/`Turret_ShootStart`:
- A **BUILT** clip carries NOTHING in `m_RotationCurves`/`m_PositionCurves`/`m_ScaleCurves`/
  `m_FloatCurves` — those are editor-only and ship empty. The curves live in `m_MuscleClip.m_Clip` as
  three parallel banks of FLOATS (`StreamedClip`, `DenseClip`, `ConstantClip`) under ONE flat curve
  index: streamed occupies `[0, curveCount)`, dense follows it, constant follows that.
- `genericBindings` names the curves IN THAT SAME FLAT ORDER, each eating as many floats as its
  attribute is wide. `MV_RocketJumpIdle` closes the arithmetic and the semantics at once: 12
  bindings (4× attribute 1, 4× attribute 2, 4× attribute 3), 40 constant floats reading *12 small
  numbers, then 4 unit quaternions, then TWELVE 1.0s*. So **attribute 1 = localPosition (3 floats),
  2 = localRotation (4), 3 = localScale (3), typeID 4 = Transform** — read off the DATA, not off a
  remembered enum. `Fireworm_unfurl` confirms the widths: 10 bones × (3+4+3) = 100 = 47 streamed + 53
  constant.
- An **empty StreamedClip is not an empty array**: it carries TWO uints, `0x7F800000` (+infinity,
  the frame time) and `0` (that frame's curve count), with `curveCount = 0`.
- `m_MuscleClipSize` is a **RUNTIME** size, not the serialized one — `Turret_ShootStart` reports 2572
  inside a 2376-byte asset. It is
  `2528 + 4*streamedUints + 4*denseSamples + 4*constantFloats + 8*valueArrayDelta`, fitted on two
  clips and then closing EXACTLY on Turret_ShootStart 2572, MV_RocketJumpIdle 3016, Bind_Pose 3580,
  Reload 5220, MV_RocketJumpStartA 5412 and Fireworm_unfurl 7536. `ClipRoundTrip` re-derives
  fireworm's number offline every run, so the formula stays pinned to the game's own data.
- `m_ValueArrayDelta` holds one entry per curve FLOAT (that curve's value at start and stop);
  `m_IndexArray` is 200 entries of `-1` on every generic clip; every `xform` in a
  `ClipMuscleConstant` is the identity, which the empty template does NOT give (its `q` is all-zero,
  and a zero quaternion is not a rotation).
- Two `Animator` fields are not the template's: **`m_HasTransformHierarchy` must be true** (false
  means "optimize transform hierarchy", which DELETES the very child transforms a clip addresses by
  path), and the gate bakes `m_CullingMode = 0` (AlwaysAnimate) rather than the shipped 1, because a
  never-rendered instance is legitimately allowed to write no transform at all.

**FALSIFIED twice, builds `3e8eb62f` and `84b88631`, and the two falsifications separate the two
claims.** (A) the binding path hashed from `bonePath + "_FALSIFY"`: `U6-wrote` RED
(`path=2713989311`), `U6-sample` RED (`y=-3.5` at both times) and `U6-mecanim` RED — while
`U6-clip`, `U6-aoc` and both `-ctl-path` arms stayed green, i.e. the clip still LOADED and still
listed correctly and drove nothing. (B) the override pair not registered in `m_Clips`: `U6-mecanim`
RED, `U6-mecanim-ctl-flat` RED, and **`U6-aoc FAIL … whose clips are [MedKitHeartBeat1]`** — the
shipped base controller genuinely resolved through our forged external and Mecanim went back to
playing ITS clip, which is the positive proof that the green run's 15 came from OUR override.
`U6-sample` stayed GREEN through (B), because `SampleAnimation` never touches the controller — so
the two arms provably measure different halves.

Ceilings of U6, deliberate: ONE curve bank (dense), ONE binding kind (Transform `localPosition`),
and the base state machine is a shipped one. A dense bank is a uniformly sampled float per frame, so
it needs no keyframe/tangent format at all; streamed (variable-rate, with tangents) and constant
(one value, no time) are the two other banks the SAME flat index already reaches — add them when an
importer has data that needs them. Rotation/scale are the same three lines with widths 4 and 3.
Baking a `ControllerConstant` from scratch (own states, transitions, parameters) was NOT needed and
was not built: it additionally requires an `m_ControllerSize` runtime-size formula that nothing has
fitted. Route it that way only when a mod needs a state machine no shipped controller can stand in
for.

**Reporting defect fixed in the same commit:** `autogate.ps1` printed each gate line through
`-replace '^.*\| '`, which ate everything up to the LAST `|` — so every arm whose summary contains a
pipe (`U3a-refs`, `U4-wrote`, `U5-wrote`) was printed WITHOUT its gate name, and a FAIL there would
have appeared as an anonymous fragment. It now strips only the Unity prefix `[INFO] 34 (1,847): `.

**Why U3d/U3e read RED under `autogate` for a day, and what actually fixed it** (2026-08-12).
The recorded green above came from a human typing `ct_bake` at the main menu; `autorun` fires at
t≈1.85 s and reported `U3d FAIL … -> 'Hidden/InternalErrorShader'` on the pre-U4 build
`e9fb9aeb` too, so it was never a U4 regression. **Root cause, not the clock:** a Unity external
PPtr resolves through the archive VFS — the path `archive:/cab-x/cab-x` an externals entry names
exists only while that AssetBundle is LOADED. `PhoenixGame.FirstRunCrt` awaits
`Addressables.InitializeAsync()` (`PhoenixGame.cs`:738) which loads the CATALOG and no content, then
calls `InitMods()` (:758) — so at mod-init time not one shipped bundle is open and every external
in our bake dangles. At the main menu Addressables had already pulled both shader bundles in, which
is the whole of the difference. `Shader.Find("Standard") != null: True` in the red runs was a red
herring: `Standard` is a builtin, always present, and says nothing about whether an ARCHIVE is
mounted — those two log lines are gone.

Fix, entirely in the gate (`src\Bake\BakeSelfCheck.cs`) — no runtime code, nothing on the shipping
path: `ct_bake` mounts `defaultlocalgroup_unitybuiltinshaders.bundle` and `_shaders_assets_all.bundle`
with `AssetBundle.LoadFromFile` itself, re-opens the bake so the materials deserialize against them,
and `Unload(false)`s them at the end (the archive must not stay mounted, or a later Addressables load
of the same file collides). The gate no longer depends on WHEN it runs.

**The vacuous controls are gone.** `U3d-ctl-badid`, `U3d-ctl-noptr`, `U3e-ctl-badid` and
`U3e-ctl-wrongfile` were all phrased "must NOT be the shader name", which is trivially true of a run
in which nothing resolved — they PASSed through a whole day of total failure. Each now asserts the
POSITIVE identity `Hidden/InternalErrorShader`. New arm `U3d-premount` asserts the same value BEFORE
the mount, so the precondition is measured rather than assumed. And when a mount fails the arms report
**VOID**, never PASS.

Falsified 2026-08-12, build `eb991ea2`, by pointing both mounts at `aln_egg_explosive_assets_all.bundle`
(a real bundle that carries neither CAB): `U3d-premount PASS` · `U3d FAIL … -> 'Hidden/InternalErrorShader'
(expected 'Standard')` · `U3e VOID '…' did not load, so no U3e arm can resolve` (Unity refuses to load the
same file twice — the VOID rule working) · `ct_bake: 1 FAILURE(S)`. The mount is load-bearing, and the
controls no longer pass on a run that measures nothing.

**Same-shape risk elsewhere:** any row proven by a HUMAN at the main menu whose subject is an
EXTERNAL reference or an Addressables-loaded asset carries this precondition. U0a/U0b/U1/U3a/U3a-refs
and all of U4 are internal-PPtr or file-read gates and are unaffected — confirmed green in the same
t≈1.835 s run. The `ct_project`/`ct_route7` rows already run through `autogate`'s two-phase shape.

**Task 28's baked `AnimatorOverrideController` is now designable.** Its two ingredients are proven:
`m_Controller` as an external PPtr into the bundle holding the game's controller (U3e), and
`m_Clips` as a list of (original PPtr, override PPtr) pairs, which is an ordinary serialized array.
~~Still unknown … whether a baked `AnimatorOverrideController` survives the bundle as a *type*, and
whether Mecanim accepts an override whose clip came from a foreign file at runtime.~~ — **both
ANSWERED YES by U6 above**: the type survives (`GetType().Name == "AnimatorOverrideController"`),
and Mecanim plays the override, base controller and all, from a forged external PPtr.

One caveat on `AddExternal`, learned the hard way (`ct_bake` 13:33:34 went RED): a fresh
`AssetsFileExternal` leaves `VirtualAssetPathName` **null** while every shipped one carries `""`,
and `AssetsFileExternal.GetSize()` dereferences it unconditionally — so the omission NREs the whole
bundle write, not just that entry. Fixed in `b7df4b9`.

Why U3d is readable, and the one thing it does **not** prove:

- The oracle is the shader's **name**, not null-ness — and the run vindicated that choice: both
  negative arms returned `Hidden/InternalErrorShader`, not null. Under a null test the bad-pathID
  control would have looked *resolved* and the gate would have been void.
- `U3d-wrote` (read off the FILE, before the engine opens it) is asserted separately from `U3d`
  (read off the engine), so a failure names which half broke.
- Both constants are measured, never chosen: the fileID is read off the cloned file's own externals
  table at bake time, and pathID `952725256833404699` is the `Standard` shader dumped offline out of
  `defaultlocalgroup_unitybuiltinshaders.bundle` (CAB-207b1100…) with AssetsTools.NET.
- **Scope:** the target CAB is one the cloned `mutoid_assets_all.bundle` **already declares** as
  external \[1]. Whether an external entry we *add ourselves*, naming a bundle the clone does not
  reference, also resolves is a separate open question — and it is the case that
  `AnimatorOverrideController` and most real replacement targets need. Cheapest experiment (**U3e**):
  append a second externals entry naming `cab-22f8ff865f4ca3fac668dbcaedfdbb9d`
  (`_shaders_assets_all.bundle`) and point a third material at a real shader pathID inside it, with
  the same three controls.

## Zero-runtime replacement (route vii)

| # | Closed fact | Evidence |
|---|---|---|
| R7 | **A shipped Phoenix Point asset is replaced with NO runtime code** — the game's own Addressables loads a patched private copy of a shipped bundle, because one string in `catalog.json` points at it | `ct_route7` 2026-08-12, commit `7213575`. **apply** 13:13:01 (Player-prev.log 1081-1102): `WROTE …\Mods\ContentTool\Route7\mutoid_assets_all.bundle 266623 B as f60de03c7de6c58d1e0ab805494982dd.bundle / CAB-35c6207d8d79fb22e17ef121965f6b14; root 'PLACEHOLDER_Mutoid_Head_Invisible_Ready' -> 'CT_ROUTE7_PATCHED'` (identity KEPT, not renamed) · `R7-one-id PASS internalId strings rewritten: 1` · `m_Crc of e3b5586adb2c39e519afe730ac1f46ae: 917075962 -> 0` · `backup: …\aa\catalog.json.ct-backup` · `APPLIED. RESTART`. **verify** 13:15:09, the NEXT session (Player.log 934-946): `R7 PASS the game's own Addressables resolved 'Assets/Defs/Tactical/Actors/_Common/Mutoid/PLACEHOLDER_Mutoid_Head_Invisible_Ready.prefab' to 'CT_ROUTE7_PATCHED'` · `R7-ctl-intact PASS a second object in the SAME patched copy still reads 'PLACEHOLDER_Mutoid_Head_Spitter_Ready'` · `ct_route7: PASS - zero runtime code took part in this load`. **revert** 13:15:17 (Player.log 949-955): `R7-ctl-revert PASS catalog.json restored, sha1=2ecd6385b4104fa5c978801608d1a1625da6be92 (recorded before apply: 2ecd6385b4104fa5c978801608d1a1625da6be92)` · `ct_route7: REVERTED, byte-identical`. No THREW, no refusal. Impl: `src\Bake\Route7.cs` |

| P1 | **An author DECLARES a replacement in a project file and the tool bakes a patched copy from the player's own game files** | `ct_project` 2026-08-12 14:11 (Player-prev.log 884-932), commits `be2ab2e`/`2cb87c1`/`e011243`/`64f7392`: `project 'morgott.sample' ...: 1 texture(s), 2 sound(s), 1 replacement(s)` · `patch aln_fireworm_assets_all.bundle: 'fireworm_low_emissive' <- swatch 8x8` · `WROTE ...\Dist\Patched\aln_fireworm_assets_all.bundle 2604144 B as 61cc70135aa0759c681bbb39491cee08.bundle / CAB-460421ef154f38bdf559b1b7100a9674 (shipped source is 2597797 B)` — 1.002x, LZ4 identity held on a 2.5 MB bundle · `P1 PASS every replaced Texture2D ... reads back its new pixels` · `P1-ctl-shipped PASS the shipped ... does NOT contain them - it was never written`. Then `ct_route7 apply sample`: `installing 1 patched copy(ies) as 'morgott.sample'` · `R7-one-id PASS 2 edit(s) applied to a rebuild of the PRISTINE catalog` — **two mods' records stacked for real**, the demo's and the project's, which is S1 leaving the harness. Impl: `ContentProject.ParseReplace`, `ProjectBake.Patch`, `BundleBaker.ReplaceTexture2D`, `Route7.Register` |

Re-run on the Workshop shape (`46a1843`), `ct_project` 14:14 (Player-prev.log 695-740): identical
gates, and the copy now lands outside Steam's reach —
`WROTE C:\Users\...\LocalLow\...\ContentTool\Patched\morgott.sample\aln_fireworm_assets_all.bundle
2604144 B` · `P1 PASS` · `P1-ctl-shipped PASS` · `installing 1 patched copy(ies) as 'morgott.sample'`
· `R7-one-id PASS 2 edit(s)`.

| P2 | **The game's own Addressables hand back the project-authored replacement** — the loading half, and with it the whole chain | `ct_route7 verify` 2026-08-12 14:22 (Player.log 695-713), commit chain `be2ab2e` → `2cb87c1` → `e011243` → `64f7392` → `46a1843` → `dea9b8a`: `edit: contenttool -> mutoid_assets_all.bundle` · `edit: morgott.sample -> aln_fireworm_assets_all.bundle` · `P2 PASS the game's Addressables handed back 'fireworm_low_emissive' at width 8 (project-authored replacement is 8, shipped is 1024)` · `P2-ctl-sibling PASS 'fireworm_low_normal' on the same prefab is still 1024` · `R7 PASS` and `R7-ctl-intact PASS` in the same run. No restart guard, no exception |

**The milestone, end to end:** an author declares a replacement in `ppcontent.json`; the tool bakes a
patched copy of the shipped bundle **from the player's own game files**; the record stacks into
`catalog.json` beside another mod's; after a restart the game's own Addressables serve the
replacement — and the sibling texture on the same prefab is untouched, so the change is targeted and
not global. **Zero runtime code takes part in the load.**

The probe key must be a catalog KEY (the asset GUID or its address), not an `m_InternalIds` path:
the path names the same entry but Addressables never resolves it, and the prefab comes back null.
The mutoid probe hid this because there the path is also a key.

| P3 | **A declared MATERIAL change reaches the patched copy** — same `replace` array, one optional field | `autogate.ps1 -Commands ct_project` 2026-08-12, build `813df545` asserted in-phase: `2 replacement(s)` · `patch aln_fireworm_assets_all.bundle: material 'ALN_Fireworm_DMG' _Glossiness=0.875` · `WROTE …\Patched\morgott.sample\aln_fireworm_assets_all.bundle 2604151 B (shipped source is 2597797 B)` · `P3 PASS material 'ALN_Fireworm_DMG' in the copy carries _Glossiness=0.875 -> … \| _Glossiness=0.875 \| …` (the whole property block is printed, so the one changed value is read in context of the 30 that did not) · `P3-ctl-shipped PASS the shipped … does NOT carry it` · P1 and P1-ctl-shipped PASS in the same run · `ct_project: ALL PASS`. Impl: `BundleBaker.ReplaceMaterialFloat` + `FindUnique` |

| P4 | **A declared MESH change reaches the game, and it is VISIBLE** — OBJ on disk → baked mesh in the patched copy, route vii, ZERO runtime code. Confirmed by the user's own eyes, not by a log line — the static-mesh case; the rigged case is U5b, eye-confirmed in the same session | Target `px_assault_assets_all.bundle` / `CHR_PX_ASS_TS_M_V01_02` + `CHR_PX_ASS_TS_F_V01` (Phoenix Assault torso, both genders) replaced by the sample's 4-vertex `blade.obj` quad. In-game after restart: on the roster / character screen the soldier's **TORSO is flattened into a quad** while head, arms and legs stay normal — only `TS` (torso) was declared, so the untouched limbs are the control. Rotated to view from BEHIND the torso is **invisible** — one quad, one winding, backface-culled by Unity — which is positive evidence the engine renders OUR geometry rather than the shipped mesh, not a defect. Gate chain: `24ed69e` (mesh pipeline, P4 PASS in-game build `a038913a`) → `12793b4` (retarget to visible armour, build `12eeb3dd`) → `2d7c078` (`ApplyProject` installs what `ppcontent.json` DECLARES, not what the `Patched` folder holds, build `3f333c8c`). Incidental measurement worth keeping: a **110 MB** bundle bakes and LZ4-repacks in-game inside the gate's budget — only 175 KB had been measured before |

Standing ceiling of P4: all OBJ groups merge into one submesh. The skin ceiling is **closed** — P5,
in the same run, asserts the replacement is bound to the target's own skeleton (see U5b above), so a
rigged target now deforms instead of hanging as a static mesh.

| P4d | **IN-GAME GREEN 2026-08-27** (was PENDING-INGAME). A shipped WEAPON's mesh AND its whole texture set replaced together, zero runtime code — the **Ares AR-1** becomes an imported CC0 rifle. Demo `demos\WeaponMesh`: target `px_equipment_assets_all.bundle` / Mesh `WPN_PX_RG_Assault_Rifle_T01_V01` (1 submesh, 5771 v / 8572 t, a plain `Transform`+`MeshFilter`+`MeshRenderer` prefab) plus its five own `Texture2D`s, which carry the weapon's own name — so the swap touches this gun and nothing else. Six `"replace"` rows and the files they name; no DLL, no def edit, no runtime hook | **Measured live 2026-08-27, run R1** on `D:\PP-Instance2`, ContentTool `1.0.0.0 build=b078ff68`, 21 mods loaded incl. TFTV 1.1.4.5 (`VERIFIED-DEMOS.md`, commit `5d8fb39`). Both halves read off the ENGINE, with the shipped side read out of the installation's own untouched bundle in the SAME run: `AddonSkinDataBase.GetPrefabAsset(E_SkinData [PX_AssaultRifle_WeaponDef].DefaultPrefab)` → `MeshFilter.sharedMesh.vertexCount` = **5554**, `subMeshCount=1`, name `WPN_PX_RG_Assault_Rifle_T01_V01`, against the shipped bundle's **5771 verts / 8572 tris** (`ct_extract mesh px_equipment_assets_all.bundle WPN_PX_RG_Assault_Rifle_T01_V01`) — the two numbers are the discriminator, not a log line. Texture, same prefab → `MeshRenderer.sharedMaterial.mainTexture`: **1024×1024 `RGBA32`, mips=1**, name `..._albedo`, against the shipped **2048×2048 fmt=10 (DXT1) mips=12** (`ct_extract tex`, same run). Icon half (def-field write): `PX_AssaultRifle_WeaponDef.InventoryIcon.texture` = an unnamed standalone **450 ARGB32**, against control `PX_LaserPDW_WeaponDef` still on `UI_PX_WeaponIcon_Laser_PDW_INV` in the **4096 RGBA32** `sactx-…-UIAtlas_UI-c47c0ec5` atlas. Correction the run also forced: `ct_route7 apply` / `revert` NO LONGER EXIST — the redirect installs itself at startup (`1/1 bundle(s) redirected LIVE`) and `ct_route7 verify` answers `REMOVED: … wrote into your Phoenix Point installation and no longer exists`, so the "apply → RESTART → verify" run this row used to prescribe is stale (`demos\WeaponMesh\meta.json` still tells subscribers to run it — open item, `VERIFIED-DEMOS.md` §"What contradicts the existing documentation" 1). Commit `10ee4bc`. The FIT is derived, not tuned, and asserted offline by `tools\fit_rifle.py`: from the shipped `m_LocalAABB` centre `(0, 0.03420, 0.15292)` extent `(0.03385, 0.12561, 0.30142)` — basis map glTF **-X → Unity +Z** (determinant **+1**, so the gun is not mirrored), uniform scale **0.568735** = the smallest per-axis ratio (the new geometry fits INSIDE the silhouette the game already reserved, on every axis), translation `(0, -0.019437, 0.026721)` matching AABB centres; the script asserts the result fits the shipped box and that the barrel lands on +Z. **The mount is untouched by construction**: a weapon is an `Addon` parented to `AddonDef.ProvidedSlotBind.AttachmentPointName` (`AddonDef.cs:20`, `Addon.cs:49-53`, `Equipment.cs:144`), resolved against the rig by name — never a per-frame transform write — so IK, firing animations and the holster pose keep working and a mesh replacement changes geometry and nothing else. Textures: the kit's atlas recoloured at 1024 (a replaced `Texture2D` is written uncompressed RGBA32 with one mip, so 2048 costs 16 MB in the patched bundle against 4 MB) plus four 4×4 neutral maps, because the shipped maps were painted for the Ares' UV layout. Scale note: `px_equipment_assets_all.bundle` is **403 MB** and the patched copy is written by decompressing the whole archive |

Scope of P1/P2/P3, honestly: one texture and one float property, one bundle, one project. P3 is
verified in the FILE; no run has yet shown a material change through the game's loader (P2's shape,
applied to a material, is the missing arm).

Scope of P1/P2, honestly: one texture, one bundle, one project. The declaration is three flat fields
(`bundle`/`asset`/`texture`); mesh and material are not implemented, and audio stays on its
media-ID path deliberately (§3.2 — it needs no bundle copy, no catalog edit and no restart).
**The other half is NOT proven:** P2, the game loading a PROJECT-produced copy, has not yet
returned a verdict — its two arms both read `-1` at 14:11, meaning neither texture was reachable on
the prefab, so the run measured nothing. R7 itself passed in that same session, so the catalog and
loader half is unaffected; what is unknown is only whether that particular prefab is the right probe.

What R7 proves: the replacement is resolved by the game's own loader before any mod exists
(`Addressables.InitializeAsync` at `PhoenixGame.cs:738` precedes `InitMods()` at `:758`), so no
Harmony patch, no scan, no per-resolve work, nothing at play time. And it is fully reversible —
measured, by SHA-1, not asserted.

| R7-lz4 | **A patched private copy costs what the game already ships** — pack the copy with the SOURCE's own compression | Offline (AssetsTools.NET) on `mutoid_assets_all.bundle`, 175 599 B, `GetCompressionType() = LZ4`: our uncompressed write **266 623 B (1.52x)**, repacked **LZ4 175 838 B (1.00x)**, LZMA 125 276 B (0.71x). Live in `ct_bake` 13:2x after the change: `WROTE …ct_selfcheck.bundle 176707 B` (was 270 435 B) with `U0a PASS loaded, 9 asset name(s)` — U0a loads back the file it just wrote, so a broken pack could not pass. Impl: `BundleBaker.Write` |
| R7-stack | **Several mods share one `catalog.json`** — each write rebuilds from the PRISTINE backup plus the surviving edit records, so no edit is ever applied on top of another | `ct_route7 stacktest` 2026-08-12 13:29 (Player.log 938-950), all in memory, `ALL PASS (nothing was written)`: `S1-stack PASS two mods, two bundles: both paths present, both m_Crc 0 (were 917075962 and 2459119740)` · `S2-conflict PASS … refused, naming the owner: mod 'ct_a' already replaces mutoid_assets_all.bundle` · `S3-revert-one PASS ct_a reverted: its path is gone and mutoid_assets_all.bundle's m_Crc is back to 917075962, while ct_b's edit still stands` · `S4-orphan PASS records whose bundle file is missing are dropped by the rebuild`. S3 is S1's control and the load-bearing one: it is what shows rebuild-from-pristine restores a bundle exactly, instead of leaving a mod's leftovers behind |

Corrected assumption, worth its own line because it was a latent refusal nobody would have hit
until a mod targeted the wrong bundle:

- `ZeroCrc` first located a bundle's options block by searching `m_ExtraDataString` for its
  **`m_Hash`**. That only works where the name hash equals `m_Hash` — true for `mutoid`, **false for
  `dlc5`**, whose hash appears in neither encoding, so route vii would have refused on it with a
  confident message. The block is now found through the entry's **`dataIndex`**, measured to be a
  BYTE OFFSET into the blob with the `m_Crc` digits ~263 B past it: mutoid 44907 → 45170 (which is
  exactly where R7's own byte diff saw the 9 changed bytes), dlc5 38984 → 39247, `_common` 0 → 263.
  Measured at the same time: **all 45 options blocks are UTF-16, zero ASCII**.

What R7 does **not** prove, and none of it is a detail:

- **one** bundle, **one** internalId, **one** object. Nothing about several targets at once.
- **multi-mod**: `catalog.json` is a single shared file. Today `apply` refuses when a backup already
  exists — right for one mod, wrong for two. Two mods need the backup to stay the pristine catalog
  plus a per-mod record of its own edits, so edits stack and each reverts only its own.
- ~~**disk**~~ — closed by R7-lz4 above: 1.00x once the copy is packed like its source, so
  `nj_equipment` costs what it already costs. The entry-redirect variant existed only to avoid this
  and is struck (FINAL-PLAN §39.4).
- the **keepcrc** arm was never run, so "CRC 0 is what let it load" is inferred from the decompiled
  `AssetBundleResource` and Unity's docs, not measured here.

## Zero-runtime KEY publishing (route iii, gate C1) — the SECOND route

Harness: `ct_catalog apply [project] | verify | revert <modid> | selftest | status`
(`src\Bake\CatalogKeys.cs` + the route-iii verbs in `src\Bake\Route7.cs`). Author surface: a
`"publish"` array in `ppcontent.json` — `{ "key": …, "asset": …, "type": …, "deps": … }`. One
concept, not two: the tool looks the key up in the game's own catalog and ADDS it when it is absent,
REPOINTS it when it is present. Same ledger, same pristine `catalog.json.ct-backup`, same SHA-1
guard, same `File.Replace`, same orphan drop as route vii — **route vii is not replaced**, it still
owns everything buried INSIDE a shipped asset (design §7.4).

| # | Closed fact | Evidence |
|---|---|---|
| C1-boot | **An edited catalog with APPENDED keys/buckets/entries/internalIds/options BOOTS** | `ct_catalog verify` 2026-08-12, build `55802500` asserted in-phase: `C1-boot PASS the game booted on a catalog carrying 8236 keys (pristine ships 8232), key-count int = 8236, 2 published key(s)`. Unfakeable by construction: a duplicate key or a stale `m_KeyDataString[0..3]` makes `CreateLocator` throw inside `Addressables.InitializeAsync` (`PhoenixGame.cs:738`) and there is no session to print from |
| C1-add | **An asset that exists ONLY in the mod's own bundle is loaded through an APPENDED key, by the game's own Addressables, with zero runtime code** | Same run: `C1-add PASS … resolved 'morgott.sample/probe_tex' to Texture2D 'swatch' out of Sample.bundle` · `C1-add-size PASS it is 8x8` (every shipped texture in the neighbouring bundle is 1024). Expected name is derived from the record — `BundleBaker`'s own rule that `m_Name` is the last segment of the container key — never a constant in the arm |
| C1-replace | **A WHOLE addressable asset is served from the mod's bundle instead of the shipped one** | Same run: `C1-replace PASS … resolved '02_Bodyparts/ALN_Fireworm_BodyAll_DMG_Ready.prefab' to GameObject 'rig' out of Sample.bundle`. Shipped is `ALN_Fireworm_BodyAll_DMG_Ready`; the entry (1617) keeps its shipped `resourceTypeIdx` and `primaryKey`, only `internalIdIdx` and `dependencyKeyIdx` move |
| C1-shader | **A forged external PPtr resolves when ADDRESSABLES did the mount** — closes the `externals-under-addressables` UNMEASURED row of `design-one-bundle-mod.md` §9 | Same run: `C1-shader PASS an external PPtr in the mod's own asset, mounted by ADDRESSABLES and by no code of ours, resolved to shader 'Standard'`. d4e1814 had only proven this when WE called `AssetBundle.LoadFromFile`. Nothing in this run mounts anything — the repointed entry's dependency set does, by declaration. Falsification reading in the same arm: with the catalog restored the shipped prefab comes back and reports `_PX_CHR/CHR_Character_Damaged_shader`, so `Standard` cannot have come from the shipped object. **Caveat, stated rather than hidden:** the arm proves *an Addressables mount* satisfies the archive VFS, not that *our dep-set entry specifically* did it — the main menu may already hold `defaultlocalgroup_unitybuiltinshaders`. The declaration is asserted separately, in the writer |
| C1-clip | **An `AnimationClip` this mod BAKED is published as an Addressables key and the GAME'S OWN Addressables hands it back as a real, non-empty clip.** Closes the last "offline only" row of `90a4280`: type resolution for `AnimationClip` (it lives in `UnityEngine.AnimationModule`, not in the assembly holding `UnityEngine.Object` — `src\Bake\TypeNames.cs`) had never run in a live session. Impl: the sample publishes `morgott.sample/probe_clip_walk` and `…_idle` out of its own `Sample.bundle`; the arm is `CatalogKeys.Verify` | **PROVEN IN GAME 2026-08-28**, `D:\PP-Instance2`, build `27c7b58b`, `ct_project` → `ct_catalog apply Sample` → `ct_catalog verify`, driven through `PPCLI\ppcli.ps1 connect`. `published 'morgott.sample/probe_clip_walk' -> assets/morgott.sample/clips/spider_spider_walk in Sample.bundle for 'morgott.sample' **as AnimationClip**` · `C1-pub PASS the game's own Addressables resolved 'morgott.sample/probe_clip_walk' to **AnimationClip 'spider_spider_walk'**` · **`C1-clip PASS … length=0.8333s frameRate=24 empty=False legacy=False isLooping=True wrapMode=Default`** · **`C1-clip PASS 'morgott.sample/probe_clip_idle' -> AnimationClip 'spider_spider_idle' length=4.1667s frameRate=24 empty=False`** · `ct_catalog: PASS`. **Discriminating, three ways.** (1) **Negative control, same session, BEFORE the publish**: `Addressables.LoadAssetAsync<Object>("morgott.sample/probe_clip_walk").WaitForCompletion()` → **`null`**, `Status = **Failed**` — the key does not exist in the shipped catalog. (2) **Two clips, one code path, five-fold different lengths** — 0,8333 s vs 4,1667 s — so neither reading can be a default or a constant; both are exactly `(frames-1)/24` for the frame counts the same run's bake printed (`clip 'spider_spider_walk' … 21 frame(s) @ 24 Hz = 2898 dense float(s)` = **138 curves**, `'spider_spider_idle' … 101 frame(s) @ 24 Hz = 12928` = **128 curves**), and 24 Hz is the download's own keyframe-reduced rate, not the 30 Hz every baked U6 clip carries. (3) **`empty=False`** is the load-bearing field: a clip whose curve banks failed to deserialise still reports a name and a frameRate and reads `empty=True`. Controls in the same `verify`: `C1-live PASS 6 key(s) published LIVE while the game's own catalog.json still carries its shipped 8232 keys` and `C1-ctl-sibling PASS`. A shipped `AnimationClip` cannot serve as the control here — it is a sub-asset and unreachable by key (see the `LoadAsset<AnimationClip>` note above) |
| C1-ctl-sibling | **The blast radius is one KEY** | Same run: `C1-ctl-sibling PASS '02_Bodyparts/ALN_Fireworm_BodyAll_Ready.prefab', which nobody published, still resolves to 'ALN_Fireworm_BodyAll_Ready'` — same shipped bundle, same shipped dependency set, different entry. `ct_route7 verify` in the same session: `P2 PASS` · `P2-ctl-sibling PASS` · `R7 PASS` · `R7-ctl-intact PASS`, so both routes are live over one `catalog.json` at once |
| C1-roundtrip | **The codec does not corrupt the 8232 keys nobody asked it to touch** | `ct_catalog selftest`: `C1-roundtrip PASS a rebuild with no published key reproduces the pristine catalog exactly (1670824 chars in, 1670824 out, equal=True)`. This is also an ALWAYS-ON guard: `CatalogKeys.Apply` re-encodes the input first and refuses to append to any catalog it cannot reproduce |
| C1-dupkey | **A duplicate key is refused BEFORE anything is written** | `ct_catalog selftest`, all in memory: `C1-dupkey PASS … REFUSED: key 'ct/selftest_probe' is claimed twice in one rebuild - by mod 'ct_a' and by mod 'ct_b'` · `C1-dupkey-ctl PASS the same guard passes the same key published once` · `C1-dupkey-ledger PASS … mod 'ct_a' already publishes key 'ct/selftest_probe'` · `C1-keycount PASS one published ADD takes the catalog from 8232 to 8235 keys, and m_KeyDataString[0..3] moved with it` · `C1-add-needs-type PASS`. This arm was **RED first** (`NOT REFUSED`, autogate 2026-08-12) and found a real hole: once mod A had ADDED a key, mod B's record found it present and silently REPOINTED it — a hijack with no error anywhere. The `claimed` map in `CatalogKeys.Apply` is that fix |
| C1-ctl-revert | **Un-publishing is byte-identical** | `ct_catalog revert morgott.sample`: `C1-ctl-revert PASS 'morgott.sample' un-published 2 key(s); catalog.json rebuilt from PRISTINE with the remaining 3 record(s), sha1=60d8b51baa393d2cdda3c3ffa6d2f25a8cf32e8c`. Compared from OUTSIDE the game (PowerShell `Get-FileHash`): before C1 `60d8b51b…`, applied `65b4c0f9…`, re-applied after a hand restore `65b4c0f9…` (the rebuild is deterministic), after revert `60d8b51b…` |

**Falsification, pasted.** `catalog.json` overwritten by hand from `catalog.json.ct-backup` with the
ledger and `Sample.bundle` left in place, then the same command:
`C1-boot PASS … 8232 keys` · `C1-add FAIL … resolved 'morgott.sample/probe_tex' to (null)` ·
`C1-add-size FAIL it is not a Texture2D` ·
`C1-replace FAIL … to GameObject 'ALN_Fireworm_BodyAll_DMG_Ready'` ·
`C1-shader FAIL … resolved to shader '_PX_CHR/CHR_Character_Damaged_shader'` ·
`C1-ctl-sibling PASS` · `ct_catalog: 4 FAILURE(S)`.

Format, read off the game's OWN `Unity.Addressables.dll` (ilspycmd 9.1.0,
`ContentCatalogData.CreateLocator` + `SerializationUtilities`), never guessed — and now confirmed by
a booting game: `m_KeyDataString[0..3]` sizes `CreateLocator`'s key array and must be bumped per
appended key; a bundle location without an `AssetBundleRequestOptions` block in `m_ExtraDataString`
fails with `LoadType.None`; dependency-set keys are **int32**; and dependency ORDER is load-bearing —
`BundledAssetProvider.InternalOp.LoadBundleFromDependecies` mounts every dependency but loads the
asset out of the FIRST `AssetBundleResource`, so the mod's own bundle goes first.

Corrected while building C1: the SHA-1 guard told the player to "restore `catalog.json.ct-backup` by
hand, then re-run" and then refused the re-run, because the restored file no longer matched
`written=`. `Route7.Foreign` now treats a catalog that equals the recorded PRISTINE hash as not
foreign — it contains nobody's content, so there is nothing to protect.

What C1 does **not** prove: the `bundle-mount-cost` and `options-fields` rows of
`design-one-bundle-mod.md` §9 are still open (a minimal options block was used and worked, but no
arm isolates which of its fields mattered); one project, one mod bundle, two keys; and nothing about
sub-object replacement, which is route vii's and stays route vii's.

## Video replacement (V1) — NOT route vii

Harness AS MEASURED (2026-08): `ct_video apply [project] | verify | revert [modid] | selftest | status`.
Those four verbs were DELETED in `c4570e6` — the route is live now and the harness is
`ct_video live [project] | status | defs | resolve <key> | open <key> | play <defname> | quit`
(`ContentTool\src\Bake\VideoCatalog.cs`). The evidence below is unchanged; only the verbs that
produced it are gone. Author surface: a `"replace"` entry with `"video"` and no
`"bundle"`, naming the shipped `StreamingPath` (or just its file name) in `"asset"`.

| # | Closed fact | Evidence |
|---|---|---|
| V1 | **A shipped cutscene plays the mod's own clip, with zero runtime code.** Copy the mod's `.webm` into `StreamableCopiedAssets\Videos\<modid>\` and mutate that row's `StreamingPath` in `Catalog.json`; `StreamableAssetsManager.Awake` reads it off disk before any mod exists | autogate 2026-08-12, build `e1ed8791` confirmed in BOTH phases. Phase 2: `V1-url PASS StreamableAssetsManager resolved e7a1ad78a926d9a4bb167824d8d103c6 to ...\StreamableCopiedAssets/Videos/morgott.sample/probe.webm` · `V1-frames PASS catalog-resolved clip is frameCount=60 256x144; the mod's own file (from the edits ledger) is frameCount=60 256x144; the SHIPPED clip named by ...Catalog.json.ct-backup (StreamableCopiedAssets/Videos/Tutorials/TestTutorialVideo.webm) is frameCount=1641 1280x720` · `ct_video verify: ALL PASS` |
| V1-ctl | **The control is positive identity, not absence.** An untouched row still resolves to its own pristine `StreamingPath` and decodes as itself | `V1-ctl PASS untouched row 37a0c730832838b439915cdd6326051e still resolves to ...Videos_DLC3/FesteringSkies_Cutscene_4FINAL.webm ... and decodes as frameCount=946 1920x1080` |
| V1-missing | **The oracle is falsifiable.** `VideoPlayer.Prepare()` fails SILENTLY through `errorReceived`, so a url-string check alone would pass on nothing at all | `V1-missing PASS a StreamingPath pointing at a file that does not exist reports NOT PREPARED (VideoPlayer cannot play url : ...ct_v1_no_such_clip.webm / Cannot read file.)` |
| V1-dupkey | **A duplicate `RuntimeKey` is provably REFUSED, offline.** `StreamableAssetsCatalog.cs:22` is `AllLocations.ToDictionary(l => l.RuntimeKey)` inside `Awake` — a collision throws `ArgumentException` and the boot scene never comes up. Replace therefore mutates in place and this code never appends a row | `ct_video selftest`, same run: `V1-dupkey PASS ... 'cdd4584fdc6b7ad4992c6abf18e40d6e' twice is REJECTED` · `V1-dupkey-write PASS ... the write guard every apply goes through refuses it by name` · `V1-dupkey-ctl PASS while a correctly rebuilt catalog passes the same guard` · `V1-rowcount PASS a rebuild neither adds nor drops rows: 69 == 69` |

> **FALSIFIED, same build.** `Catalog.json` restored by hand from `.ct-backup` with the ledger and
> the copied clip left in place, then `ct_video verify` re-run: `V1-url FAIL ... resolved ... to
> .../Videos/Tutorials/TestTutorialVideo.webm (expected it to end with .../morgott.sample/probe.webm)`
> and `V1-frames FAIL catalog-resolved clip is frameCount=1641 1280x720` — `ct_video verify: 2
> FAILURE(S)`, while `V1-ctl` and `V1-missing` stayed green. `ct_video revert morgott.sample` then
> put the install back byte-identically: `V1-ctl-revert PASS Catalog.json restored,
> sha1=23fbaa4e1d8383a43858ccd2439082231ff01f9d (recorded before the first apply: same)`.

- **69 rows, no bundle.** `V1-rows PASS the shipped catalog parses to 69 row(s) with no duplicate
  RuntimeKey`. Route vii's bundle machinery does not apply and none was built.
- The probe is `lib\v1_probe.webm`, 6,265 B, embedded in the assembly: 256x144, 2 s @ 30 fps = 60
  frames, VP8 + Vorbis in WebM — the container every shipped clip uses.
- No `..`-escaping `StreamingPath` was shipped; everything is written inside `StreamingAssets`.
- Gate V1 is the first ASYNC gate: `VideoPlayer.Prepare()` needs the player loop, so `ct_autorun`
  now holds its `DONE` marker until `Dev.AsyncGate.Pending` drains (else autogate kills the game
  mid-measurement and a run that measured nothing reads like one that measured everything).

## Sound replacement (S1) — a shipped `.wem` overwritten, NOT route vii

Harness AS MEASURED (2026-08): `ct_sound apply [project] | verify | revert [modid] | selftest | status [mediaId]`.
`apply|verify|revert` were DELETED in `c4570e6` — no shipped `.wem` is overwritten any more; the
harness is `ct_sound bake [project] | selftest | probe <mediaId> | shapec [mediaId] | status [mediaId]`
(`ContentTool\src\Bake\SoundReplace.cs`). The evidence below is unchanged; only the verbs that
produced it are gone. Author surface: no JSON at all — a `.wav` in
`<project>\Content\Audio\Replace\` **named after the media ID it replaces**. Coverage is the 3105
STREAMED media that are loose files; an embedded one is refused by name.

| # | Closed fact | Evidence |
|---|---|---|
| S1a | **The engine serves a shipped media ID out of the mod's own file, zero runtime code.** Overwrite `StreamingAssets\Audio\GeneratedSoundBanks\Windows\<mediaId>.wem`; Wwise resolves a streamed source BY FILE NAME off its own base path | autogate 2026-08-12, build `b93afbdd` confirmed in BOTH phases (phase 1 `ct_sound apply`, restart, phase 2 `ct_sound verify`). `S1a PASS the engine served media 18839791 ... streaming=true(FILE)` — FILE means it read our file; no bank of ours took part |
| S1b | **The DECODED length is the mod's, not the shipped one.** A path can resolve and still decode the old bytes, so the second identity is duration, computed offline from the two files by the tool's own header parser | `S1b PASS the decoded media is 731ms, which is the length of the .wem this mod installed (730ms, +/-60); the pristine .wem it replaced is 1200ms` |
| S1-ctl | **Positive control in the same run** — an untouched streamed sibling in the same bank still serves ITS OWN media at ITS OWN file's length, so a global clobber of the audio folder comes out RED | `S1-ctl PASS untouched GUI_HavenIndependentOpen served media 26984411 (its shipped ID is 26984411) at 10324ms, which is its own file's length (10324ms)` |
| S1-ear | **CONFIRMED BY EAR, in game, by the author, 2026-08-12.** Event `GUI_StatsPlusClick` (784388130, `\Events\...\UI_Geoscape\GEO_UI\`) played the probe: a clean, steady, NON-GAME tone — not the shipped UI click, and not distortion, crackle or truncation | Author's own listening on his main install (`Options.jopt` `…591`, `com.morgott.ContentTool` added to `MOD_ACTIVATED` that session). The probe is a 1650 Hz sine, 32237 frames @ 44100 = 731 ms, replacing the shipped 1200 ms click in `UIGeoscape` |

- **The human ear is the oracle here and no automated arm can be.** `S1a`/`S1b` prove routing and
  decoded LENGTH; neither can hear a voice that is audibly wrong at the right duration. What the ear
  added is CONTENT identity — the sound is the mod's sample, cleanly, and the codec swap did not
  mangle it. That is why this row exists separately from the three above it.
- **How to TRIGGER it, and what is NOT known** (this cost a dig, do not repeat it). The certain
  trigger is `ct_sound verify` from the console — it posts event 784388130 itself, then a 10.3 s
  control. The NATURAL screen is **UNVERIFIED**: `GUI_StatsPlusClick` has **zero hits in the whole
  decompile**, so the event is wired BY ID in prefabs, not by name in code, and no `file:line` can
  be cited. `SoundbanksInfo.xml` gives only `ObjectPath="\Events\Default Work Unit\UI_Geoscape\
  GEO_UI\GUI_StatsPlusClick"` (`DurationMin=1.069 DurationMax=1.346`), which SUGGESTS a geoscape
  "+" stat button — a guess, not a fact, and it must not be promoted to one without a run.
- **The file alone is not the whole declaration.** Dropping a PCM `.wem` over the shipped Vorbis one
  gave `playingID=1`, NO `AK_Duration` callback and `endOfEvent=23ms` — the engine handed our file to
  the codec the BANK names. `apply` therefore also rewrites `ulPluginID` to PCM (and a zero-latency
  prefetch source to a plain stream) in every bank that declares that media: `UIGeoscape.bnk` here.
- **Reverted after the check** — the author plays on this install. Restored from the pristine
  `.ct-backup` of both files and verified BY HASH: `18839791.wem`
  `6581eb150ca332660f68c68665e29c26cc4f560b` (= the SHA-1 the ledger recorded before the first
  apply), `UIGeoscape.bnk` `00185fffb31a590c425c59e0089aafd3a587a6f4`. Backups and
  `sounds.ct-edits` removed; the install is back to shipped audio.

### S1 on MUSIC — demo mod 1, `demos\MenuMusic\` (2026-08-12, build `22aced0d`)

The vanilla MAIN MENU music replaced by the demo mod's own chiptune track. Same route, no new
mechanism — what it added is what a MUSIC target does differently from a one-shot.

| # | Closed fact | Evidence |
|---|---|---|
| S1-music | **A shipped music track is replaced by the same file-overwrite route, two tracks at once, and the engine streams ours.** `autogate -PPRoot D:\PP-Instance2 -NoDeploy -Commands 'ct_sound verify'`, build `22aced0d` confirmed in-phase | `S1a[208540756] PASS ... streaming=true(FILE)` · `S1a[423563089] PASS` · `S1c[208540756] PASS ... hashes 66c4fdb3… - the .wem this mod built, not the shipped media (82c123d0…)` · `S1c[423563089] PASS … not the shipped media (d5d2006c…)` · `S1-codec[…] PASS 2 source record(s) in MainMenuMusic,TacticalMusic.bnk declare this media: 2 say PCM, 0 say something else, 0 are still prefetch` (per track) · `S1-alive[…] PASS the voice was still playing when the 3000ms wait ran out, while the control ended at 1071ms in this same run` · `S1-ctl PASS untouched ArmaRamPrepare in Armadillo served media 77838672 … at 1071ms` · `ct_sound verify: ALL PASS` |
| S1-multimod | **Two mods' sound replacements coexist, across three banks, all resolving in one run** | Same gate, earlier run (build `607b6ab1`): `morgott.demo.menumusic` (208540756 + 423563089, banks `MainMenuMusic` + `TacticalMusic`) and `morgott.sample` (18839791, bank `UIGeoscape`) applied together — `measured 3 of 3 applied record(s)`, every `S1a`/`S1c`/`S1-codec` green, `S1b[18839791] PASS 731ms`, control in a FOURTH bank. Then `ct_sound revert morgott.sample`: `S1-restore PASS 18839791.wem sha1=6581eb15…` · `S1-restore-bank PASS UIGeoscape.bnk sha1=00185fff… (pristine 00185fff…)`, and the re-run verify still measured the survivor's two tracks |

**What a MUSIC target does differently — measured, not assumed:**
- **Wwise reports NO duration for it: `dur=0ms estDur=0ms`, `endOfEvent=TIMEOUT`.** So `S1b`, the
  decoded-LENGTH identity that closed S1 on a one-shot, **cannot run here** and says **VOID**.
  The zero is a property of the SOUND, not of our file — measured on a PRISTINE install with the new
  `ct_sound probe <mediaId>` verb: `probe 208540756 'MainMenuMusic' … ledger says not replaced … POST
  probe/MainMenuMusicStart: dur=0ms estDur=0ms mediaID=208540756 streaming=true(FILE)
  endOfEvent=TIMEOUT`, same for `423563089`. A looping track has no duration to report.
- **What carries the content half instead:** `S1c` (the file at the path the engine read hashes to
  what the mod BUILT and not to the shipped media — both directions asserted), `S1-codec` (every
  source record in every bank naming that media says PCM and none is still a prefetch) and
  **`S1-alive`** (the voice was still playing when the 3 s wait expired, while the control ENDED at
  its own length in the same run — a media the codec rejects is killed in ~23 ms).
- **An event is not always named after its sound.** `MainMenuMusic.bnk` ships the sound
  `MainMenuMusic` and the events `MainMenuMusicStart`/`MainMenuMusicStop` — the old exact-name-only
  lookup came out VOID on it. `EventFor` reads `<sound>` then `<sound>Start` off the shipped
  `<bank>.txt`; a third spelling is refused BY NAME, never guessed. Offline arm `S1-eventname`.
- **One media can be declared by SEVERAL banks.** Both menu tracks are declared by `MainMenuMusic`
  AND `TacticalMusic`; `apply` patches and `revert` restores both, hash-proven.
- **VANILLA, not TFTV.** TFTV ships exactly one file, `TFTV.dll` (no bundle, no `.bnk`, no `.wem`),
  and its only `AkSoundEngine` lines are commented out (`refs\TFTV-src\TFTV\TFTVVanillaFixes.cs:2704-2719`),
  so it cannot supply audio media. Which of the two vanilla tracks plays is an EDITION question:
  `UIStateInitial.cs:74-77` asks the platform for the YOE entitlement + `CheckIsCompleteEdition`, and
  `EditionVisualsController.GetCorrectMusicEvent` (`:94-99`) picks `MainMenuMusicStart` or
  `MainMenuYOEStart`. The demo replaces BOTH, so no edition escapes it.

**The track is the author's own `.mp3`, decoded by the ENGINE at install time** (2026-08-12, build
`d8768c56`, `ct_sound verify: ALL PASS`). `Content\Audio\Replace\` takes the same whitelist
`Content\Audio\` does — `.wav` through the tool's reader, `.ogg`/`.mp3` through the armed runner
`ct_project` already used (`EngineAudio.Arm` now takes what to run after the decode, so there is one
coroutine and not two). Measured: `A6-decode 208540756.mp3 -> 2ch 48000Hz 5523840 frames` = 115.08 s,
`replaced …208540756.wem: 3687722 B / 142978ms shipped -> 22095424 B / 115080ms from 208540756.mp3
(2ch 48000Hz, peak 0,645)`. The peak the engine's decode produced (0.645 = −3.8 dBFS) is EXACTLY
ffprobe/ffmpeg's `max_volume: -3.8 dB` on the source, measured independently before the run —
integrated loudness −15.3 LUFS, so the track is installed unaltered.

**FALSIFIED, second axis** (build `cc481050`): with the arming disarmed so the decoder never runs,
`S1 REFUSED 208540756.mp3 was never handed to the engine's decoder …` and NOTHING was written — the
silent-empty-install failure mode is refused by name, not baked.

**A streamed media the engine is PLAYING cannot be replaced by `File.Replace`.** Measured, build
`d2c68af9`: `ct_sound THREW System.IO.IOException: Sharing violation on path` at
`SoundReplace.Swap`, right after both decodes. A `.wav` install never hit it because it lands at
t≈1.5 s, before `UIStateMainMenu` starts the music; an `.mp3` has to be decoded first and lands at
t≈2.5 s, with the menu track streaming out of the very file being replaced. Fix in `Swap`:
`AkSoundEngine.StopAll()` on the first failure, then retry the replace while pumping `RenderAudio`
(20 × 50 ms) — the stream manager closes handles on its own thread, so one attempt is not enough.
`StopAll` was verified present in `PhoenixPointWin64_Data\Managed\AK.Wwise.Unity.API.dll` before use.

**Non-ASCII source filenames work end to end** — `Аве! Император.mp3` (Cyrillic + space + `!`):
`A6 PASS Аве! Император.mp3 decoded 2ch 48000Hz 147456 frames peak=0,515` → bank →
`LoadBankMemoryCopy: AK_Success bankId=3449159320` → `ct_project: ALL PASS`. So
`Directory.GetFiles`, `new Uri(path).AbsoluteUri` and `UnityWebRequestMultimedia.GetAudioClip` all
carry the name. On the REPLACE path a file must still be named `<mediaId>.<ext>` — that is the
grammar, not an encoding limit.

**FALSIFIED in game**, build `76533bc5`, by not writing the codec bytes (the `ulPluginID` rewrite
skipped, everything else identical): `S1-codec[208540756] FAIL … 0 say PCM, 2 say something else, 2
are still prefetch` and `S1-alive[208540756] FAIL`, with `endOfEvent=162ms` / `104ms` on the two
tracks — the Vorbis codec killed the voice, exactly the 23 ms failure mode this row's note below
records. **`S1a`, `S1c` and `S1-ctl` stayed GREEN through it**, which is the whole reason
`S1-codec`/`S1-alive` exist: routing and byte identity are both true on an install where nothing is
audible. Restored, re-run green (`22aced0d`).

| S1-ear-music | **The replaced menu music is CORRECT BY EAR, and the overlap is GONE** — author's own listening on `D:\PP-Instance2`, build `ae88b1f0`, 2026-08-13. He had reported the previous build restarting and doubling ("after about 4 seconds it starts again, and the copies play simultaneously"); on the build carrying the `smpl` loop region the track plays through cleanly, once, at a level he did not complain about (the disclosed −6.3 dB) | Ear, on the applied demo `morgott.demo.menumusic` |

**So the missing loop region WAS the cause** — row S1-music's own fix, confirmed the only way it could
be. **Why the in-run arm could not settle it, and this is the lesson worth more than the row:**
`S1-cont` samples for 15 s against a 128 s track, and a wrap can only be observed at the wrap. The
falsification that withheld the region therefore stayed GREEN on continuity and RED only on
`S1-loop`, and the report at the time said in as many words that the loop gap was NOT what the
author heard. That was the correct call on the evidence then and the wrong conclusion. A window
shorter than the phenomenon measures nothing about it; state the window, and do not read silence
inside it as absence.

| S1-zero-runtime | **ZERO RUNTIME CODE, proven in the strongest available form: the mod was not even ENABLED.** The author launched with NO mod activated at all and the replaced main-menu music still played, cleanly | Author's own run, 2026-08-13, after the `ae88b1f0` apply |

What makes this stronger than "the tool was absent": not one line of ours executed and there was no
mod ENTRY either — no DLL loaded, no `MOD_ACTIVATED` line, no ContentTool. The replaced bytes live in
`StreamingAssets\Audio\<mediaId>.wem` plus the codec byte in the shipped banks, so the game's own
Wwise serves mod content off its own base path. A player who has only the files hears the author's
track.

**Do not overstate it: this is the SOUND-REPLACE route only.** The whole shape, with the counterpart
`videodemo` measured the same day: **replacing** shipped content is zero-runtime, **adding** a file
is zero-runtime — and making the game PLAY something new costs a hook, because nothing shipped
references it. A new video row installs with no code and no shipped `VideoPlaybackSourceDef` plays
it; a new sound needs someone to post its event.

**Embedded (In Memory) media are NOT out of reach — the ceiling is about the ROUTE, not the media.**
Checked the donor before writing this down, as the rule says: `ResourceReplacer\pp-native\src\` holds
`Spike07_SetMediaInBank.cs`, which measured the question head-on and found `SetMedia` is IGNORED for
an embedded source. But rows 12/13 above already closed it another way — a **media-only bank**
(`BKHD+DIDX+DATA`, no HIRC) declaring the game's media ID replaces embedded media, and test G's own
example is **272177053 = `GUI_MenuClick`**, one of the two busiest UI sounds. That route needs
`LoadBankMemoryCopy`, i.e. RUNTIME CODE, which is why the zero-runtime file-overwrite route reaches
only the 3105 STREAMED media and refuses the rest by name. ContentTool has not wired the media-only
bank into the author route; a mod that already ships a hook (demo #3 does) could carry it.

## Mod-manager gate (G1) — content from a DISABLED mod is never applied

Harness: `Project\ModGate.cs` (pure decision, offline arm in `tests\TargetPathTests`) +
`Project\ModRoster.cs` (live roster + the loader patch). Every cross-mod discovery routes through
`ModGate.Decide`; there is exactly one such seam, `Bake\SoundLoad.cs:LoadAll`.

| # | Closed fact | Evidence |
|---|---|---|
| G1-gate | **A folder on disk is not a player's consent.** `SoundLoad.LoadAll` walked `Mods\*` and loaded every `Dist\Sounds\*.bnk` it found, so a mod switched OFF in the manager still played — the author heard the disabled `demos\MenuMusic` track on his main menu. The verdict now comes from `ModManager.Mods -> ModEntry.Enabled`, keyed on `ModEntry.Directory` | **PENDING-INGAME (offline green).** `G1-enabled/-disabled/-unknown/-noroster/-noroster-empty/-key-shape/-why` all PASS in `dotnet run --project tests\TargetPathTests` (`R0: ALL PASS`, 51 checks). FALSIFIED: forcing `Decide` to return `Apply` (= the old behaviour) turns **5 of 7** G1 arms RED and the suite exits 1 |
| G1-timing | **The roster is unreadable at the moment ContentTool is enabled.** `OnModEnabled` runs INSIDE the startup enable pass (`PhoenixGame.cs:851` → `ModManager.EnableModsFromStore` → `TryEnableMod` → `ModEntry.cs:219`), and a dependency is enabled BEFORE its dependents (`ModManager.cs:200-207`) — so every mod that depends on ContentTool still reads `Enabled=false` there. The gated pass is deferred one frame (`Timing.Current.Start(..., NextUpdate.NextFrame)`); the enable pass is synchronous, so the next frame sees final flags | Read off the decompile, not assumed. A gate that ran in `OnModEnabled` would skip exactly the content mods the player switched ON |
| G1-loadable | **PP has no concept of a mod without an assembly, so `Enabled` meant nothing for a media-only mod.** `PPModLoader.LoadMod` (`PPModLoader.cs:50-64`) returns null when neither `<AssemblyName>` nor `<FolderName>.dll` exists, and `ModEntry.SetEnabled` (`ModEntry.cs:198-204`) throws on that null → `TryEnableMod` logs "Failed to enable mod" and `Enabled` stays false forever. Gating on `Enabled` without fixing this would have BANNED every code-less content mod, not fixed the bug. One Harmony postfix supplies a `ModInstance` with an empty `ContentMod : ModMain` — only where the game had already failed (`__result == null`) and only for a folder carrying `Dist\Sounds\` or `ppcontent.json` | `ModRoster.AfterLoadMod`. The patched path is one the game could only fail on, so no other mod's load changes shape |
| G1-refuse | **An unreadable manager loses content LOUDLY, never silently applies everything.** No `ModManager`, `CanUseMods=false` or an empty roster ⇒ `NoRoster` ⇒ nothing is applied; falling back to "apply all" is the bug itself | `G1-noroster`, `G1-noroster-empty` |

- **Report-out.** Every refusal prints one line naming the mod and the reason — `skipped, disabled in
  the mod manager` · `skipped, the mod manager never discovered it (no meta.json)` · `skipped, the
  mod manager could not be read` — and the summary carries a third counter:
  `ct_sound: N shipped replacement bank(s) from <root>, F failed, S skipped`.
- **Rulings on the edges, all three deliberate:**
  - **ContentTool's OWN subfolders** (`Mods\ContentTool\MenuMusic\`, `…\Sample\`, …) are not separate
    mods and are not gated — they are ContentTool's content, governed by ContentTool's own switch.
    `VideoCatalog.LiveAll` scans only that folder and is therefore not a cross-mod seam.
  - **A plain folder with no `meta.json`** is not a mod the manager can know (`PPModLoader.cs:35`
    keeps only folders `ModMeta.FromDir` accepts). It reads `Unknown` and is REFUSED, not applied —
    explicit, not accidental. A content mod must ship a `meta.json`.
  - **A content mod must declare `"Dependencies": ["com.morgott.ContentTool"]`** so the loader patch
    is installed before it loads (`ModManager.cs:200-207`). Every `demos\*\meta.json` already does.
- **ponytail ceiling:** enabling a content mod mid-session stores the flag but loads its banks only
  on the next start — the gated pass runs once, at startup, and a Wwise bank cannot be unloaded
  anyway (see the SoundLoad header). Wire a per-mod apply onto `ModEntry.SetEnabled` if that ever
  matters.

## End-to-end authoring (the tool doing its actual job)

| # | Closed fact | Evidence |
|---|---|---|
| 17 | **Author files on disk -> import -> one bundle -> read back in-game.** A `ppcontent.json` project with a `.png` and two `.wav` (one streamed) becomes `Dist\<bundle>`, and every part comes back out of the file that was just written | `ct_project` 2026-08-12 12:12:03, commits `50dfaaa` + `f647088`: `project 'morgott.sample' at ...\Mods\ContentTool\Sample: 1 texture(s), 2 sound(s)` → `WROTE ...\Sample\Dist\Sample.bundle 336029 B as morgott_sample / CAB-morgott_sample` → `TEX PASS assets/morgott.sample/textures/swatch -> 8x8 RGBA32 px[0,0]=0,255,0,255` (ALL pixels compared, not a sample) · `extract: 1 stream(s), 0 rewritten | AddBasePath(...\WwiseAudio\): AK_Success | 3338666241: cached` (second run of the cache, so the no-rewrite arm is live here too) · `BANK PASS ...morgott_sample.bnk -> LoadBankMemoryCopy: AK_Success bankId=157178304` · `ct_project: ALL PASS`. Media IDs were allocated at import (`3338666241`), not hardcoded. The texture and the bank are read from `AssetBundle.LoadFromFile` on the written path; the IR buffers are only the comparison baseline |

> The first `ct_project` run (12:02:19) logged `TEX FAIL` on this same data: the check assumed a
> row flip that does not exist. That was a false RED, not a false green — no row was recorded from
> it, and `f647088` replaced the comparison with a full-buffer one.

| # | Closed fact | Evidence |
|---|---|---|
| 18 | **An author ADDS a whole new model by dropping one `.glb` into `Content\Models\`.** No grammar: the file is the declaration, the way `Content\Textures\` already works. It becomes a prefab in the mod's own bundle (`assets/<id>/models/<stem>`) — one bone per joint the file lists, each resting at the inverse of its own bind pose, and the file's REAL per-vertex weights, so a vertex can be SHARED between bones. That last part lifts the standing ceiling: `SkinFields.Rebind` (the `.obj` route) can only synthesise ONE full-weight influence per vertex | `ct_project` 2026-08-12, build `c15220af` confirmed in-phase. `M1 PASS 'rig' lifting bone 'sample_head' by 10: rest y=[0,0,2,2] -> y=[10,5,2,2], and the file's own weights predict [10,5,2,2]` · `M1-ctl-bone0 PASS ... lifting bone 'sample_hip' ... -> y=[0,5,12,12] ... predict [0,5,12,12]` · `M1-wrote PASS ... bones=2 bone0='sample_hip' bone1='sample_head' ... bindpose1.e13=-3 ... vertex0=1/0->bone1`. The oracle is the EFFECT: the prefab is instantiated and `SkinnedMeshRenderer.BakeMesh` read before and after one bone moves, and Unity skins with `sum(weight * boneMatrix)`, so the prediction is `rest + thatBone'sWeight * 10` taken from the author's own file. The **5** is the whole point — a half-and-half vertex, a number no one-influence bake can produce. Both arms assert their own positive vector; neither is a "nothing happened" |

> FALSIFIED, build `51af473a`: the bake writing one full-weight influence on bone 0 (the `.obj`
> ceiling put back) while the import still read the file turned all three arms RED —
> `M1 ... -> y=[0,0,2,2] ... predict [10,5,2,2]`, `M1-ctl-bone0 ... -> y=[10,10,12,12] ... predict
> [0,5,12,12]`, `M1-wrote FAIL ... (MISSING vertex0=1/0->bone1)` — and `ModelRoundTrip` threw
> offline on the same edit. The sample rig's weighting is ANTI-GEOMETRIC on purpose (the two
> vertices at y=0 belong to the HIGH bone), so no nearest-bone or split-at-the-centre synthesis can
> imitate it.
>
> Ceilings, deliberate: two influences per vertex (the shipped layout's room), one submesh, and the
> model's Material — builtin `Standard` through an external PPtr, with a same-named
> `Content\Textures\` file in `_MainTex` — is UNGATED: shading needs the builtin shader bundle
> mounted (U4's note) and deformation does not depend on it. The flat-bone ceiling this row shipped
> with is closed by row 19.

| # | Closed fact | Evidence |
|---|---|---|
| 19 | **An imported rig's bones CARRY each other — the .glb's node tree is read and written, so the rig can ANIMATE and not merely pose.** `GlbReader.Hierarchy` turns glTF's children arrays into a parent link per JOINT SLOT (a joint whose nearest ancestor is not a joint hangs off the model root, which is what a flat rig already was); `SkinFields.BuildModel` writes BOTH halves of each link (`m_Father` and the parent's `m_Children`) and each bone's rest transform LOCAL to its parent. Rest poses stay derived from the bind poses alone (`ModelBuild.LocalRest`), so the tree contributes exactly one fact: who carries whom. Consequence, not a side effect: a bone's `m_BoneNameHashes` entry is now the CRC of its whole path (`sample_hip/sample_head`) — the same paths a Mecanim curve binds by (U6) | `autogate.ps1 -Commands ct_project` 2026-08-12, build `5b881c9f` confirmed in-phase (full default list, `ct_bake`/`ct_audio`/`ct_project` all ALL PASS). `M1-parent PASS vertices [0,4,5] have NO weight of their own on 'sample_hip' and move only because it carries 'sample_head','sample_arm': 'rig' lifting bone 'sample_hip' by 10: rest y=[0,0,2,2,3,3] -> y=[10,10,12,12,13,13], and the file's own weights predict [10,10,12,12,13,13]` · `M1-rest PASS 'rig' at rest bakes to y=[0,0,2,2,3,3], the .glb's own vertices [0,0,2,2,3,3]` · `M1 PASS ... lifting 'sample_head' ... -> y=[10,5,2,2,3,8] ... predict [10,5,2,2,3,8]` · `M1-ctl-bone0 PASS ... lifting 'sample_hip' ... -> y=[10,10,12,12,13,13]` · `M1-wrote PASS ... root 'rig' children=2 ... hashes=3:220116457:1392138863:2654395075 ... \| tree 'sample_hip'<'rig'#2,'sample_head'<'sample_hip'#0,'sample_arm'<'sample_hip'#0`. The prediction is the file's own arithmetic: the summed weight of everything that bone CARRIES, times the lift — a flat rig gives the direct weight instead, and the vertices named in the arm have none |

> FALSIFIED, build `6a7b084e`: the bake parenting every bone to the root again (the flat rig put
> back) while the import still read the tree turned four arms RED — `M1-parent` and `M1-ctl-bone0`
> `-> y=[-1,4.5,12,12,2,2]` against a predicted `[9,9.5,12,12,12,12]`, `M1-rest -> y=[-1,-0.5,2,2,2,2]`
> against the file's `[0,0,2,2,3,3]`, and `M1-wrote FAIL ... (MISSING | tree 'sample_hip'<'rig'#2,...
> | root 'rig' children=2)` — and `ModelRoundTrip` threw offline on the same edit. **`M1` stayed
> GREEN**, which is the ceiling row 18 stated, measured: a single-bone move of a leaf cannot tell a
> parented rig from a flat one, and only the new arms can.
>
> The sample rig is ANTI-CHAIN on purpose: `sample_head` and `sample_arm` are SIBLINGS under
> `sample_hip`, so a bake that parented bones in index order would carry the arm with the head and
> move vertices the file says must stay still. And no bone rests where its parent does (hip 1, head
> 4, arm 2), so a bake that wrote a bone's WORLD rest into its local transform stacks the parent's
> lift on top of it — which is what `M1-rest` reads, an arm no deformation arm can ask because they
> all take the rest bake as their own baseline. `SampleStamp` 9 → 10.

> **CORRECTED 2026-08-23, build `96f51821` — the arm's PREDICTOR was wrong on a real rig, the bake
> was not.** `M1` lifts a bone's `localPosition` by 10 and predicted `rest.y + carriedWeight * 10`,
> comparing **y alone**. A local +Y lift is a mesh-space +Y translation only when the bone's
> ancestors are pure translations — true of the sample rig by construction, false of an author's
> file. Measured off `lib\u8_probe.glb` offline: the spider's bone 0 `'Root'` rests
> `rot=(0.7071,0,0,0.7071)` (a quarter turn about X) with uniform `scale=33.688`, so a local +10 y
> on its child `'Body'` is **(0,0,336.883)** in the mesh's space and leaves y untouched. The bake was
> right and the gate read it as `M1 FAIL 'spider' lifting bone 'Body' by 10: rest y unchanged ...
> predict [10.49,...]`. The earlier reading — "those vertices carry no `'Body'` weight" — is **wrong**:
> `CarriedWeight(v,1) = 1.0` for all eight sampled vertices, and `M1-ctl-bone0` moving them all by
> exactly 10 says only that `'Root'` carries the whole rig, which every rig's root does.
> **Fix** (`ProjectBake.Deform`, and the same assumption in U7's `Drive`): measure the displacement
> where the vertices are measured — `smr.transform.InverseTransformPoint(bone.position)` before and
> after — assert the whole **vector** rather than y, scale the tolerance with the displacement
> (`1e-3 * max(1,|delta|)`), and count how many vertices the file's own weights say MUST move,
> failing when that count is 0. Green: `M1 PASS 'spider' lifting bone 'Body' by 10 moves it by
> (0,0,336.883) in the mesh's own space ... 5086 of 5461 vertices must move, worst off by 0
> (tolerance 0.337)` · `M1 PASS 'rig' ... moves it by (0,10,0) ... 3 of 6 vertices must move, worst
> off by 0 (tolerance 0.01)`, `U7`/`U7-mecanim`/`U9`/`U9-mecanim` unchanged.
>
> FALSIFIED, build `eea3d752`: `SkinFields.FillModelMesh` writing bone index **0** for every
> influence (a bake that threw the file's weights away and gave the whole mesh to the root) —
> `M1 FAIL 'rig' ... -> y=[0,0,2,2,3,3] ... predict [10,5,2,2,3,8] ... worst off by 10` and
> `M1 FAIL 'spider' lifting bone 'Body' ...`, `U7`/`U7-mecanim` RED with them, `ct_project: 6
> FAILURE(S)`. The spider half is the point: that wrong bake leaves **y** exactly where the correct
> bake leaves it, so the old y-only predictor would have called it GREEN. `M1-ctl-bone0` stayed
> PASS under it — everything really does hang off `'Root'` — which is why the mover count and the
> vector comparison, not that control, are what carry the arm.
>
> ~~Still ungated: nothing yet drives an imported rig with a CLIP.~~ — the arm is row **U7** below;
> it is OFFLINE-GREEN and awaiting an in-game run.

| # | Closed fact | Evidence |
|---|---|---|
| U7 | **IN-GAME GREEN 2026-08-23** (was PENDING-INGAME; closed by `ct_project` on the strengthened arms — see the IN-GAME half below). A baked `AnimationClip` drives the IMPORTED rig — the arm row 19 said nobody had built. `ct_project` bakes, beside every rigged model of its OWN sample project (gated on `p.Id == "morgott.sample"` so no author's release bundle carries gate scaffolding): the clip (`ProjectBake.LiftClip`), an `AnimatorOverrideController` over the SAME shipped base U6 uses (`_common`'s `MedKitHeartBeat1`, reached by an appended external — U3e), and an `Animator` ON THE MODEL ROOT (`SkinFields.BuildModel`'s new `controllerPathId`). The clip is bound to a bone's path UNDER THAT ROOT — the same spelling `SkinFields` hashes into `m_BoneNameHashes` (row 19) and the same CRC-32 a curve is addressed by (U6). Four arms: **U7-wrote** / **U7-wrote-anim** off the FILE, **U7** (clip alone) and **U7-mecanim** (the shipping shape) off the ENGINE | **OFFLINE half, green and FALSIFIED TWICE, 2026-08-23**: `dotnet run --project tests\ObjCodecTests` → `MODEL round trip PASS on mutoid_assets_all.bundle` · `clip 'glbmodel_lift' bindings=1 path=1392138863 attr=1 typeID=4 dense=3x3@1 samples=9 first=(0,3,0) last=(0,13,0) streamed=2/0 const=0 delta=3 index=200 stop=2 muscleSize=2596 legacy=False` · `root animator controller='glbmodel_aoc' avatar=0 culling=0 hierarchy=True`, and the MESH in the same copy reads `hashes=3:220116457:1392138863:2654395075` — one number written by two different writers out of one path spelling, which is the entire join. `first=(0,3,0)`/`last=(0,13,0)` are the file's own numbers: `sample_head` rests 3 LOCAL to a hip at 1, plus the `ModelLift` of 10. **FALSIFIED (A)** by binding the clip to `BoneNames[1]` instead of `BonePath(1)` — the flat spelling, i.e. exactly the pre-row-19 rig: `MODEL round trip FAILED: … path=465949905 … (wanted … path=1392138863)`. **(B)** by building the model with `controllerPathId = 0`: `MODEL round trip FAILED: the model ROOT carries the Animator that plays it: (no Animator) …`. Every other arm in the suite stayed green through both; reverted, re-run `exit=0`. **IN-GAME half GREEN, `ct_project` 2026-08-23** (the second run of the day, on the arms `6ef415d` strengthened — so no arm could report PASS by skipping): `U7 PASS` · `U7-mecanim PASS` · `M1 PASS` for both `rig` and `spider` · `M1-ctl-bone0`/`M1-rest`/`M1-parent PASS` · `ct_project: ALL PASS`. `U7-wrote` (the clip binds the CRC of the rig's own bone path), `U7-wrote-anim` (the root's Animator names the AOC), `U7` (`SampleAnimation`) and `U7-mecanim` (`Animator.Update` through the baked AOC) — both engine arms assert the driven bone's own localPosition BY IDENTITY and then every vertex against `rest + CarriedWeight(v,bone) * delta`, the author's own weights. Impl: `src\Bake\ProjectBake.cs` (`LiftClip`/`ClipWrote`/`Animated`/`Drive`/`LiftBone`), `ClipFields.FillAnimator` + `Summary(aocName: null)`, `SkinFields.BuildModel(controllerPathId)`, `BundleBaker.AddModel(controllerAssetName)` + `ReadAnimatorOn`, `tests\ObjCodecTests\ModelRoundTrip.cs` |

**Why U7 needs no new serializer.** Every writer already existed: `AddAnimationClip` +
`AddAnimatorOverrideController` (U6) and `BuildModel` (rows 18/19). What was added is one component on
the model root — through `ClipFields.FillAnimator`, which is U6's own measured `Animator` fill
EXTRACTED rather than copied, so the two shapes cannot drift — plus the arms. The missing thing was
never a format, it was one agreement: the PATH.

**`m_AnimationType` does not exist here — MEASURED, not assumed.** A remembered "Generic = 2" is a
field from an older Unity. Read through the class database in one run, a SHIPPED generic clip
(`aln_fireworm`'s) and ours both report `(no field)`; the arm is permanent in
`tests\ObjCodecTests\ClipRoundTrip.cs` and prints both sides, so a future class database that DOES
carry it compares two real numbers instead of two zeroes. Nothing writes it.

**Two engine arms because they fail apart.** `U7` drives the clip with no controller in the loop
(`AnimationClip.SampleAnimation`) — RED there is the clip's bytes or the binding path. `U7-mecanim`
drives it through the baked `Animator` + AOC — RED only there is Mecanim or the external base
controller, and `_common_assets_all.bundle` is MOUNTED BY THE GATE before our bundle is opened
(U3d's precondition; the gate no longer depends on when it runs). The `SampleAnimation` arm still
needs an `Animator` present — the P7 sampler's line (`ProjectBake.cs`, ported from
`ResourceReplacer\pp-native\src\MeshReplacer.cs:930`): outside the Editor the engine refuses a
NON-LEGACY clip *without an Animator* and writes NOTHING, which is indistinguishable from a binding
path nobody matches. On this bake the BAKED Animator serves it.

**The clip is 3 frames, sampled on the SECOND.** Two reasons, both anti-vacuity: the sample lands
exactly ON a frame (no interpolation rule is assumed) and strictly INSIDE the clip, so a looping state
machine cannot have wrapped back to frame 0 and handed back the rest pose — which would read exactly
like a dead binding.

**Anti-vacuity, in the same instance rather than a second run.** The rest bake taken before the sample
is the control: a clip that evaluated to nothing leaves the WEIGHTED vertices at rest and reads RED,
and a rig that moved as one object fails on the vertices whose `CarriedWeight` on that bone is 0. The
driven bone is index 1 (`sample_head`) — a CARRIED bone, so its path is not merely its name and the
falsification above is a real transposition rather than a fixed point.

**Ceilings of U7, deliberate.** (a) The `Animator` + AOC are baked ONLY for the sample project. A
model with nothing to play it gets no Animator at all, deliberately: the AOC's base is an external
PPtr, and a dangling controller reference on an author's shipped prefab is worse than none. A mod that
wants one declares it — that grammar does not exist yet. (b) The clip is SYNTHESISED (a three-frame
lift), not imported: ~~reading animation OUT of
a `.glb` is an importer nobody has written~~ — **written, row U8 below** — note that
`GlbCodec.WriteAnimation`/`SampledClip`
(`src\Import\GlbCodec.cs:37-68,499`) is the EXPORT side, already ported from Resource Replacer, so
the format details for that importer are in the tree. ~~(c) The vertex prediction assumes the driven
bone's ancestors are translation-only, exactly as the existing `M1`/`M1-parent` arms do.~~ —
**CLOSED 2026-08-23**: both predictors now MEASURE the driven bone's displacement in the renderer's
own space (`smr.transform.InverseTransformPoint`) instead of assuming it is the lift. See the M1
note under row 19.

| # | Closed fact | Evidence |
|---|---|---|
| U8 | **CLOSED IN-GAME 2026-08-23, THROUGH U9** (was PENDING-INGAME; the missing half this row named was the BAKE, which is U9 — and `U9`/`U9-mecanim` now assert in the game, by identity, the very per-frame samples this reader took out of the `.glb`). The animation clips a REAL downloaded `.glb` already carries are READ, into the internal clip representation U6/U7 bake — per bone, per frame, on a uniform grid, in UNITY space. `GlbReader` parsed only `meshes`/`nodes`/`skins`; `animations` is the arm it never had. `GlbReader.Read(byte[], List<SampledClip>)` fills the EXISTING `SampledClip`/`SampledTrack` (the export side's own type, `GlbCodec.cs:37-68`), and each `SampledTrack.Node` is a **JOINT SLOT**, so a track's bone path is `BakedSkin.BonePath(track.Node)` and the CRC a curve will bind is the one the MESH already wrote into `m_BoneNameHashes` — U7's join, by construction rather than by agreement | `dotnet run --project tests\ObjCodecTests` (gate **U8**, `tests\ObjCodecTests\ClipImport.cs`), 2026-08-23, both halves green in the same run and every other suite unchanged: `CLIP import PASS` · **hand-built glTF-space bytes** — `2 track(s) @ 4 Hz x 5 frame(s) \| head x=-1,-2,-3,-4,-5 \| head rot y=-0.707 quarter=-0.19509 \| hip scale=(1,2,3) \| STEP holds -1 \| CUBICSPLINE refused` · **the real file** `lib\u8_probe.glb` (Quaternius' CC0 spider, 349 468 B, see `lib\u8_probe-SOURCE.md`) — `39 bones, 5 clip(s)`: `'Spider_Attack' 34 bone(s) @ 24 Hz x 19 frame(s), 0.75 s` · `'Spider_Death' 35 @ 24 Hz x 26, 1.042 s` · `'Spider_Idle' 34 @ 24 Hz x 101, 4.167 s` · `'Spider_Jump' 35 @ 24 Hz x 18, 0.708 s` · `'Spider_Walk' 35 @ 24 Hz x 21, 0.833 s` · `furthest a bone travels = 0.114` · `'Spider_Walk' binds 'Root/Body' -> 1197768663`. Impl: `GlbReader.Animations`/`Animation`/`Times`/`Rate`/`Channel`/`Segment`/`Slerp` |

**Why the fixture is TWO files, and why one of them is not ours.** A round trip through `GlbCodec`
alone proves nothing about the coordinates: every rule is an INVOLUTION (the reader's own class
remark), so both halves being wrong together survives it. `ClipImport.Hand` therefore assembles a
whole `.glb` — header, JSON, BIN, accessors — with the numbers stated in glTF's RIGHT-handed space
and no line of our writer in the loop. And a real download is the only thing that produces what our
exporter never does: 39 joints, five clips in one file, a separate time array per sampler (19 of
them in `Spider_Attack` alone), and keyframe-reduced timing.

**The four conversions, each grounded rather than remembered:**
- **translation** — the vector rule `S = diag(-1, 1, 1)`, `GlbCodec.Convert(ObjVector3)`
  (`GlbCodec.cs:183`), which is exactly what `WriteAnimation` applies to a track on the way out
  (`:520`). Asserted as the ladder `glTF x = 1,3,5` → `Unity x = -1..-5`.
- **rotation** — `(x, y, z, w) -> (x, -y, -z, w)`, `GlbCodec.Convert(ObjQuaternion)` (`:202`,
  `WriteAnimation` `:532`). It is **NOT** the vector rule, and copying the vector rule here is the
  classic way to mirror an animation while the mesh stays right. Asserted on a +90° turn about
  glTF +Y coming back as `y = -0.707`.
- **scale** — unchanged, because `S*diag(sx,sy,sz)*S = diag(sx,sy,sz)` (`WriteAnimation` `:548`).
  Asserted as `(1,2,3)` in and `(1,2,3)` out, so a rule copied from the translation arm reads RED.
- **time** — glTF's sampler input is SECONDS, and a serialized clip's dense bank is a uniform float
  per frame at `m_SampleRate` with `m_StopTime = (frames-1)/rate` (`ClipFields.FillClip`
  `ClipFields.cs:112-141`). So every channel is RESAMPLED onto ONE grid per clip.

**The grid is derived from the file, not chosen.** `GlbReader.Rate` takes the COARSEST whole rate in
[1, 120] Hz that EVERY key time of the clip lands on. This is not a flourish: Blender writes keys at
the authoring rate with the unchanged ones DROPPED, so **no single channel is uniformly spaced** —
measured on the probe, one sampler's deltas run `0.04167 / 0.08333 / 0.125 / 0.20833`, all multiples
of 1/24 — and asking any one channel for its spacing answers differently per channel. All five clips
come back at **24 Hz**, every key lands exactly ON a frame, and the resampling therefore costs no
accuracy at all. The arm asserts that in the clip's own `LossyReason`, so a future file that needs
real interpolation says so instead of hiding it.

**Interpolation, and what is refused.** LINEAR and STEP are read; **CUBICSPLINE is REFUSED BY NAME**,
naming the Blender setting that avoids it (`Animation > Sampling`). Silence there would be the worst
outcome available: a CUBICSPLINE output accessor holds THREE elements per key (in-tangent, value,
out-tangent), so a reader that ignored the mode would take tangents for values at a third of the
clip's rate and produce a confidently wrong curve. LINEAR rotations use **SLERP**, which the glTF 2.0
spec makes normative ("For rotations, spherical linear interpolation (SLERP) **MUST** be used to
interpolate quaternions"), not a component-wise lerp.

**Channels the bake has nowhere to put are DROPPED and COUNTED**, never silently: one whose target
node is not a bone of the armature (the mesh object, an empty, a prop) and one whose path is
`weights` (blend shapes — the clip format's morph bank is unwritten). Both counts land in
`SampledClip.LossyReason`, and the hand-built arm asserts both sentences. Measured: on the probe
**zero** channels are dropped — every animated node in it is a skin joint.

**FALSIFIED THREE TIMES, 2026-08-23, and the three separate different claims.** (A) the vector rule
dropped (`vectors[i] = v`): `CLIP import FAILED: the head's glTF x = 1..5 comes back as Unity
x = -1..-5: 1,2,3,4,5`, while `MODEL round trip` and `CLIP round trip` stayed GREEN — the mesh path
has its own conversion and cannot see this. (B) the node→slot map reversed
(`Nodes.Count - 1 - slotOfNode[node]`): `... comes back as Unity x = -1..-5: 0,0,0,0,0` — the head's
curve landed on the hip, which is precisely the silent wrong-bone failure the whole join exists to
prevent. (C) SLERP disarmed to nlerp: `and a quarter of the way along that arc is SLERP's 22.5 deg
(y = -0.19509), not nlerp's -0.187369: -0.187366`. All three reverted, suite re-run `exit=0`.

**Anti-vacuity, learned from row 19's own lesson.** The rotation arm samples a QUARTER of the way
along the arc, never half: nlerp and slerp agree EXACTLY at the midpoint, so a fixture that sampled
there would have measured nothing and passed under falsification (C). The real-file arm additionally
asserts that some bone actually MOVES (`furthest a bone travels = 0.114`) — a reader that returned
the rest pose at every frame satisfies every other arm in it.

**Ceiling of U8** — ~~nothing BAKES an imported clip yet, so this row has no in-game half of its
own~~ — **closed, row U9 below.** As it stood: `ClipFields.FillClip` wrote ONE binding, `attribute 1` (localPosition),
three floats wide, and its samples are a single `float[] yPerFrame` — it cannot express a second
bone, a rotation (attribute 2, four floats wide) or a scale. An imported clip is all three. What
that needs is not a new format — the flat curve order, the per-attribute widths and the
`MuscleClipSize` formula are all already MEASURED in `ClipFields`' own class remark — it is
`FillClip` generalised to N bindings over the dense bank, plus a gate on the shape of U7's
(`U7`/`U7-mecanim`) with the samples coming from the file instead of `ProjectBake.LiftClip`. Until
that exists, "the import is right" is settled here in bytes and the engine has nothing to add.
Second ceiling: ~~the probe had to be DEQUANTIZED offline once, because `GlbReader` reads no glTF
extension and the download requires `EXT_meshopt_compression` — a real ceiling for authors, recorded
in `lib\u8_probe-SOURCE.md` with the one command that removes it~~ — **closed, row U10 below**, which
reads `EXT_meshopt_compression` and `KHR_mesh_quantization` in-house. `u8_probe.glb` is KEPT as U8/U9's
fixture and becomes U10's independent oracle; the untouched download sits beside it as
`lib\u10_probe.glb`.

**Two later fixes to U8's resampler, both offline-proven, both on paths the spider probe cannot
reach.** Filed as `U8-grid`/`U8-step` rather than under the ids their commits proposed (`U11`/`U12`),
which are taken below by the CustomCreature demo and the loop flag.

| # | Closed fact | Evidence |
|---|---|---|
| U8-grid | **PENDING-INGAME (offline green).** The uniform clip grid must REACH the last key. `GlbReader.Animation` sized it `Round(duration * rate) + 1`, which (a) put the last frame BEFORE the last key whenever that key sits in the front half of a frame — 0.511 s at 30 Hz ended the clip at 0.500 s and threw the last 11 ms of every curve away — and (b) wrapped a finite but enormous timestamp to `int.MinValue` on the cast, sailing under the `MaxFrames` guard and falling through the `frames < 2` floor into a SILENT two-frame clip. Now `Ceiling(duration * rate - GridTolerance)` with the frame ceiling compared as a **double, before any cast**, so a huge duration is REFUSED BY NAME. `GridTolerance` is the 0.01 frame `Rate()` already used as a literal — ONE number decides both "this key is on the grid" and "this many frames reach it", so a snapped clip keeps exactly the frame count it always had | `dotnet run --project tests\ObjCodecTests` (`ClipImport.Grids`), commit `c58f3be`: 17 frames, last frame 0.533 s ≥ last key 0.511 s. Falsified twice — `Round` → the clip ends at 0.5; a post-cast guard → `OverflowException` instead of a refusal by name. **Lives only on the FALLBACK path** (no whole rate in [1, 120] Hz lands on every key), which is why no test in this repo had ever executed it: all five spider clips snap at 24 Hz. The fixture is hand-built and had to be — Dirichlet's approximation theorem makes a SINGLE key time always snap, so falsifying it needs two incommensurate times (0.1 / 0.511) |
| U8-step | **PENDING-INGAME (offline green).** A STEP curve's HOLD survives to within one frame of its jump. A dense bank cannot hold a discontinuity, so a STEP channel resampled at the clip's own snapped rate smears the jump over a whole frame of that rate. `GlbReader.StepGrid` resamples a clip carrying any STEP channel on the **highest whole multiple of its own snapped rate the mod allows**, so the forced ramp is at most one frame at up to 120 Hz — 8.333 ms — and the bound is PRINTED in `SampledClip.LossyReason` rather than left for the author to discover | `dotnet run --project tests\ObjCodecTests` (`ClipImport.HandBuilt`), commit `c3be088`: 121 frames @ 120 Hz, every frame an exact key value, the hold reaching frame 59, ramp 8.333 ms. Falsified by disarming `StepGrid` → 250 ms. Ceiling: an EXACT hold needs a `StreamedClip` keyframe bank, which this writer does not have |

| # | Closed fact | Evidence |
|---|---|---|
| U9 | **IN-GAME GREEN 2026-08-23** (was PENDING-INGAME; offline green and falsified three times, then confirmed in the game — `ct_project`: `U9 PASS` · `U9-mecanim PASS` · `ct_project: ALL PASS`, on the arms `6ef415d` strengthened so a drive that could not RUN fails instead of skipping). An IMPORTED clip is BAKED — U8's named missing half. `ClipFields.FillClip` is generalised from ONE binding of `attribute 1` over a `float[] yPerFrame` to **N bindings over the dense bank**, all three measured Transform attributes (1 position ×3, 2 rotation ×4, 3 scale ×3), sample array frame-major over the flat curve order. No second code path: the single-binding gates (U6/U7) now call the same writer through `ClipFields.LiftY`. `ClipFields.Bindings(SampledClip, BakedSkin)` is the join — a track's `Node` is a JOINT SLOT, so its binding path is `BakedSkin.BonePath(node)`, the same spelling `SkinFields` hashed into the MESH's `m_BoneNameHashes` in the same file. It is also a real FEATURE, not scaffolding: `ContentProject.ImportModel` now fills `ImportedModel.Clips`, and `ProjectBake.ImportedClips` bakes **every** clip a project's `.glb` carries, for every project — U7's synthetic lift clip is what a model with NO animation of its own gets | `dotnet run --project tests\ObjCodecTests` (gate **U9**, `tests\ObjCodecTests\ClipBake.cs`), 2026-08-23, `exit=0`, every pre-existing arm unchanged. Fixture = the same real CC0 download U8 reads, `lib\u8_probe.glb`, baked into a copy of `mutoid_assets_all.bundle` with the U7 shipping shape (model root + `Animator` + `AnimatorOverrideController` over `_common`'s shipped `MedKitHeartBeat1` controller): `CLIP bake PASS on mutoid_assets_all.bundle (341297 B with 5 imported clip(s))` · `'Spider_Attack' 37 binding(s) over 34 bone(s), dense 19x140@24 = 2660 float(s), muscleSize=14296 sig=121395541, furthest a curve travels 1.375` · `'Spider_Death' 37 / 35 bone(s), dense 26x139@24 = 3614, muscleSize=18104` · `'Spider_Idle' 34 / 34, dense 101x128@24 = 12928, muscleSize=55272` · `'Spider_Jump' 36 / 35, dense 18x135@24 = 2430, muscleSize=13336` · `'Spider_Walk' 36 / 35, dense 21x135@24 = 2835, muscleSize=14956` · `root animator controller='spider_aoc' avatar=0 culling=0 hierarchy=True`. Impl: `ClipFields.FillClip`/`Binding`/`Bindings`/`LiftY`/`Sig`, `BundleBaker.AddAnimationClip(IList<Binding>,…)`, `ProjectBake.ImportedClips`/`ClipWrote`/`DriveImported`, `ContentProject.ImportedModel.Clips` |

**Three oracles, because they fail apart.** (a) the BINDING LIST — every curve's `path` must be the
CRC of the bone's path under the model root, and `ClipBake.Predict` spells the expected list out in
its OWN loop (every position, then every rotation, then every scale, in track order), so the flat
order is pinned by a second author instead of read back off the writer. (b) EVERY FLOAT of the dense
bank against the numbers `GlbReader` read out of the `.glb` — 2 660 to 12 928 of them per clip. (c)
the SIZES: `m_FrameCount`, `m_CurveCount`, the sample count and `m_MuscleClipSize`, which is the
measured function of the bank sizes and nothing else. Plus two anti-vacuity arms: a bank that
actually CHANGES across its frames (`furthest a curve travels 1.375`), and the other two banks left
the way a shipped generic clip leaves them (`streamed=2/0 const=0 delta=<curves> index=200`).

**FALSIFIED THREE TIMES, three different claims, all reverted and the suite re-run `exit=0`.**
**(A)** the sample array written CURVE-major instead of frame-major → `CLIP bake FAILED: clip
'Spider_Attack' dense bank IS the file's own samples, frame-major over the flat curve order: frame 11
curve 107 is off by 1.545135`, while `MODEL round trip` and `CLIP round trip` stayed **green** — with
ONE binding of width 3 the two interleaves are the SAME order, which is exactly why no gate before
U9 could see this bug. **(B)** the binding addressed by `BoneNames[node]` (the flat spelling, the
pre-row-19 rig) instead of `BonePath(node)` → `clip 'Spider_Attack' binding 0 is the CRC of
'Root/Body' (1197768663), not 2073732236` — the silent wrong-bone failure the join exists to prevent,
with `CLIP import` still green. **(C)** `m_MuscleClipSize` computed with the old hardcoded
`PositionCurves` → `m_MuscleClipSize is the measured function of its bank sizes: 13200 (the formula
says 14296)`. A fourth attempt — declaring a rotation 3 floats wide — is caught EARLIER, by
`MapCurves`' own width check on a SHIPPED clip (`clip 'Fireworm_unfurl': its bindings account for 90
curve float(s) but its banks hold 100`), so that contract is guarded twice.

**The dense-bank ceiling, MEASURED rather than assumed.** A dense bank spends one float per curve per
frame whether the bone moves or not, so cost is `frames × curves × 4 B` and `m_MuscleClipSize` is
linear in it. The five spider clips: 2 430–12 928 floats, `m_MuscleClipSize` 13 336–55 272, and all
five plus the 39-bone skinned mesh fit in a **341 297 B** bundle. Nothing here approaches a limit —
`GlbReader` refuses a clip past `MaxFrames` long before the bank could — but a 30-clip character at
this rate is a few MB of curves, and the upgrade path is already named in `ClipFields`' class remark:
the CONSTANT bank costs one float for a curve that never changes, and the same flat curve index
already reaches it.

**What the game answered, and what it still has not.** (i) — **ANSWERED 2026-08-23, `U9 PASS` /
`U9-mecanim PASS`.** (ii) is still open, and it is not blocking: nothing measures whether the engine
cares about the ORDER of `genericBindings`, only that the order shipped here binds. (i) `U9`
(`AnimationClip.SampleAnimation`) and `U9-mecanim` (`Animator.Update` through the baked AOC) —
`ProjectBake.DriveImported` asserts, at the clip's MIDDLE frame (on the grid, so no interpolation
rule is assumed, and strictly inside the clip, so a looping state cannot have wrapped to frame 0),
every driven bone's `localPosition`/`localRotation`/`localScale` BY IDENTITY against the sample the
`.glb` itself carries, with rotations compared by `|dot|` because q and −q are the same rotation; the
control in the same arm is a count of how many bones actually LEFT their rest pose, since a rig frozen
at its bind pose satisfies "no bone is wrong" perfectly. (ii) whether the engine cares about the
ORDER of `genericBindings` — U9 groups them by attribute because that is the order the one shipped
clip whose bindings were counted carries (`MV_RocketJumpIdle`, 4×1 then 4×2 then 4×3), which is a
measured layout, but nothing measured says a per-bone grouping would be refused. The in-game fixture
is the sample project's new `spider.glb` (`SampleStamp` bumped to `"sample": 15`), copied from
`<mod>\u8_probe.glb`, which the csproj now puts beside the DLL — 349 468 B in `Mods\ContentTool\`,
gate scaffolding whose line can be deleted with the gate. A missing probe makes `WriteSample` say so
out loud rather than skip the arms silently.

**One weakness of the `sig` fingerprint, stated rather than discovered later.** `ClipFields.Sig` is an
order-sensitive CRC over `(pathCRC : attribute)` — it fingerprints the binding STRUCTURE, not the
curve values, so two clips over the same bones and channels legitimately share it (`Spider_Jump` and
`Spider_Walk` are both `198129824`). It exists to make a hundred-binding clip's one-line oracle
falsifiable; the values are covered by (b) above.

**Two U9 review findings, both VERIFIED against the code before anything was written, both fixed in
ONE place each.** The spider probe has five uniquely named clips that all drive bones, so no arm in
`ClipBake` or `ClipImport` could reach either path.

| # | Closed fact | Evidence |
|---|---|---|
| U9-plan | **PENDING-INGAME (offline green).** A `.glb` whose animations are not all bakeable, and not all uniquely named, still bakes. `ClipFields.Bakeable(modelName, clips, skipped)` is the one plan: a clip with ZERO tracks is LEFT OUT and REPORTED with the reader's own `LossyReason` — `GlbReader` produces exactly that clip by design when every channel is a blend-shape `weights` channel or targets a node outside the armature (`GlbReader.cs:661,667,671`), and `ClipFields.cs:156` then threw `"drives no bone of the rig"`, taking down the bake of that model AND of the whole project while its other clips were fine — and a colliding asset name gains its own index (`u9_walk`, then `u9_walk_1`) instead of letting `AddAnimationClip` throw `"duplicate asset name"` (glTF does not require animation names to be unique, and the container key is lowercased). The first clip of a name keeps the readable spelling, so the common case is unchanged | `dotnet run --project tests\ObjCodecTests` (`tests\ObjCodecTests\ClipPlan.cs`), commit `9a3747b`: `CLIP plan PASS` · `three animations in one hand-built .glb: 'Walk', 'walk', 'Morphs' (0 track(s))` · `planned 'u9_walk' + 'u9_walk_1', skipped: clip 'Morphs' drives no bone of this rig and was SKIPPED — the model's other clips are unaffected`. The fixture is assembled byte by byte out of `ClipImport`'s OWN `Bin`/`Container`/`Sampler` (a file our writer never produces — `GlbCodec` emits no animation at all). Falsified twice: the zero-track skip disarmed → `the OTHER two clips still bake: 3 planned`; the dedup disarmed → `'u9_walk' vs 'u9_walk'`. Anti-vacuity: the common case must NOT be indexed (`u9_walk`, not `u9_walk_0`), because a plan that indexed every name would satisfy the uniqueness arm while making every asset name unreadable. Ceiling: a colliding name is disambiguated by clip ORDER, so re-exporting the same file with its clips reordered can rename an asset |
| U9-verdict | **PENDING-INGAME (offline green; it is what made the U7/U9 IN-GAME runs above mean something).** A drive arm that could not RUN now FAILS, and its anti-vacuity control has a number behind it. `ProjectBake.DriveImported` had three paths that logged `VOID` and counted ZERO failures — no baked `Animator` on the model root, a null `runtimeAnimatorController` (i.e. `_common_assets_all.bundle` not mounting), no transform at the path the clip binds — and a `VOID` is not a failure, so `ct_project` could report `ALL PASS` with the Animator/AOC shipping path never exercised once. U7's sibling `Drive()` carried the same three, plus a missing `SkinnedMeshRenderer`. `ClipFields.DriveVerdict(missing, bones, wrong, moved, travel)` is now the single pass/fail decision of every drive arm: every former `VOID` reason is a returned reason, and it REFUSES `moved > 0 && travel <= RestTravel` — the count of movers and the distance behind it are two readings of ONE fact, and a control that contradicts itself is not a control | `dotnet run --project tests\ObjCodecTests` (`ClipPlan.cs` `Verdict()`), commit `6ef415d`: six readings, including the real in-game one, which must still PASS. The broken control was visible in the game's own log — `18 of them off their rest pose (furthest 'BackLeg3.R' by 0)` printed `worstBy`, the worst ERROR against the file's own sample, which on a passing run is zero BY DEFINITION: the count worked, the distance beside it measured nothing. Falsified twice (a missing Animator back to a skip; the travel rule off). Ceiling: `travel` ignores scale, and one epsilon (`ClipFields.RestTravel`, 1e-3) serves the mover, wrong and travel rules alike |

| # | Closed fact | Evidence |
|---|---|---|
| U10 | **IN-GAME GREEN 2026-08-27, THROUGH U11** (was PENDING-INGAME — offline green and falsified five times, waiting only on a COMPRESSED file being baked inside the game; this row named the `U11` demo as its closing run, and that run happened. `demos\CustomCreature\Content\Models\spider.glb` IS the compressed file — 130 436 B, `EXT_meshopt_compression` + `KHR_mesh_quantization`, 13 of 13 bufferViews compressed, the same byte count as `lib\u10_probe.glb` — and on 2026-08-27 R2 the creature it bakes into spawned and its animator played the file's own `cyborg_spider_spider_attack_1 / _attack_2 / _walk / _idle / _death`, 19 of 19 arms. `Meshopt` therefore decoded inside the game, not only in the offline suite. Offline evidence below stands unchanged.) A COMPRESSED `.glb` is read with nothing but this mod — no gltf-transform, no gltfpack, no npx, no Blender round trip.** `GlbReader` refused every glTF extension, so U8's probe had to be dequantized once by an external CLI; the mandate calls that a ContentTool bug rather than the author's job, and it is now closed. `EXT_meshopt_compression` is DECODED here (`src\Import\Meshopt.cs`, hand-written from the ratified Khronos bitstream text): all three modes — `ATTRIBUTES`, `TRIANGLES`, `INDICES` — and all four filters — `NONE`, `OCTAHEDRAL`, `QUATERNION`, `EXPONENTIAL`. `KHR_mesh_quantization` needed **no decode step at all**, which is a measured fact rather than a shortcut: it only widens the allowed component types, and every mechanism it states its dequantization through was already honoured (`GlbReader.Value`'s normalized divisors ARE its own "Decoding Quantized Data" table; a static mesh's scale is folded by `Bake`; a skinned mesh's rides in `inverseBindMatrices`, which `ModelBuild.Invert` already treats as the authority). Compressed views land in exactly ONE place, `GlbReader.Resolve`, so interleaved, normalized, sparse and animation accessors all read a compressed file with no arm of their own. **No new dependency**; +16 KB | `dotnet run --project tests\ObjCodecTests` (gate **U10**, `tests\ObjCodecTests\Compressed.cs`), 2026-08-23, `exit=0`, every pre-existing arm unchanged, `COMPRESSED import PASS, 52323 check(s)`: `mesh u10_probe.glb (130436 B, compressed) == u8_probe.glb (349468 B, decompressed by gltf-transform): 5461 vertices, 39 bones, 8136 triangle indices EXACT \| worst bone-space vertex error 0 \| normals: worst 1-dot 0, worst component 0 = 0 of one 8-bit step, 0 of 5461 differ, 2637 in the folded hemisphere` · `clips: 5 identical \| 4958 QUATERNION rotation sample(s) worst 1-\|dot\| 0.0000001 \| 1545 EXPONENTIAL translation sample(s) (1169 negative) worst 0` · `hand-built TRIANGLES (mode 1) stream: 0xfe reset, 0xXf explicit delta, 0xXe last+1, 0xXd last-1 -> 0,1,2,0,2,5,0,5,6,0,6,5` · `hand-built INDICES (mode 2) stream: 0,1000,1,1001,2,1002,3,1003,5,900 over two interleaved baselines, 17 B` · `refusals name the extension and the fix: Draco, KHR_texture_transform, unknown`. `dotnet run --project tests\TargetPathTests` → `R0: ALL PASS`, `exit=0`. Deployed, `ContentTool.dll` **1 244 672 → 1 261 056 B, +16 384 B (+16 KB)** — measured against a build of THIS tree with `Meshopt.cs` removed and `GlbReader.cs` reverted to HEAD, so no parallel slice's bytes are counted in it. Impl: `src\Import\Meshopt.cs` (new), `GlbReader.Read`/`Resolve`/`Expand`/`Value`/`Unreadable` |

**The oracle is not ours, which is the whole basis of the row.** `lib\u8_probe.glb` IS
`lib\u10_probe.glb` decompressed — by gltf-transform's JavaScript meshopt decoder, an implementation
that shares no line of code with this one. So the pair is the same model stated twice and every
number above is cross-validation between two independent decoders, not a round trip through our own
writer (which, as `GlbReader`'s class remark says, both halves being wrong together would survive).
`u10_probe.glb` is the UNMODIFIED 130 436 B download, Quaternius' CC0 "Spider",
SHA-1 `973ee4d7c16378c249f3f8c69b028bc9970372f5`, provenance in `lib\u10_probe-SOURCE.md`. It is not
embedded and not copied into the mod folder, so it costs the shipped assembly nothing.

**Positions are NOT compared position-by-position, and the reason is the extension.** The compressed
file keeps its vertices as 16-bit integers and states the scale back to real units in the skin's
`inverseBindMatrices`; the dequantized file folds that scale into the vertices and takes it back out
of the matrices. Both describe the same model. The invariant is `inverseBindMatrix * position` — the
vertex in BONE space, which is exactly what skinning consumes — because the dequantization Q cancels:
`(IBM · Q⁻¹)(Q · p) = IBM · p`. Measured error over all 5 461 vertices × their weighted bones: **0**.

**THE MARKET WAS MEASURED BEFORE ANYTHING WAS WRITTEN, 63 real `.glb` files, and it decided the
scope.** Every file was parsed for its own `extensionsUsed`/`extensionsRequired`:

| Extension | Files | Where |
|---|---|---|
| `KHR_texture_transform` | 42 | Kenney's Blaster Kit, `extensionsUsed` only — never *required* |
| `EXT_meshopt_compression` | 2 | both from a web app's asset folder, i.e. run through `gltfpack` |
| `KHR_mesh_quantization` | 2 | the same two files — the pair always travels together |
| `KHR_draco_mesh_compression` | **0** | — |
| `KHR_texture_basisu` | **0** | — |
| none at all | 19 | 12 fresh Poly Pizza downloads, 6 Quaternius models, 1 |

The sample: 45 files already on disk (Kenney Blaster Kit ×42, the two Spiders, one dequantized), 12
downloaded fresh from Poly Pizza across six searches, 6 from a republished Quaternius pack. Two
findings changed the plan. **(a) A Poly Pizza *download* is plain glTF — 12 of 12 with no extension
at all.** The compressed files did not come from a model site; they came from a project that ran
`gltfpack` over its assets, which is what "optimised for the web" means in practice — and when it
happens, meshopt and quantization arrive TOGETHER, which is why both are implemented here rather than
one. **(b) `KHR_texture_transform` is common but never required**, so the 42 Kenney files already
read today and the guard that only inspects `extensionsRequired` was already right about them.

**DRACO IS DEFERRED, and it is written down as a ceiling rather than left to surprise someone.**
0 of 63 files in the sample use it, and its decoder — Edgebreaker connectivity plus the attribute
prediction schemes — is genuinely larger than everything else this importer does put together. It is
refused BY NAME, at BOTH places it can appear (`extensionsRequired` and a primitive's own
`extensions`), with what it is and the exact fix: *import into Blender, which reads Draco, and export
again with the Compression box unticked*. The two extensions this row adds are named in the same
sentence so an author learns they need no such step for the common case. Where Draco WILL be met if
it is met at all: Blender's own export panel has a "Compression" checkbox that writes it, and
Sketchfab serves it for some uploads.

**The second, narrower ceiling: quantized TEXCOORDs that need `KHR_texture_transform` to scale back.**
That is the one dequantization route KHR_mesh_quantization names which this mod has nowhere to read —
paint travels through `Meshes\materials\<name>.mat.json` and no glTF material is consulted, by
design. Refused by name, with the fix. It is not exercised by the probe (which carries no TEXCOORD at
all), which is stated here rather than implied by the row being green.

**FALSIFIED FIVE TIMES, five different claims, all reverted and the suite re-run `exit=0`.**
**(A)** the octahedral fold's `copysign(t, x)` written as `t * sign(x)` — the same expression with the
wrong sign, and the bug this decoder nearly shipped → `every octahedral normal unfolds within one
8-bit step: worst component 0.6771654 (86.0000044 step(s)), 2709 of 5461 differ at all`, while every
vertex and index arm stayed **green**: the fold is a no-op for the whole upper hemisphere, so a
fixture whose normals all pointed outward would have measured nothing. The arm therefore also asserts
the fixture CONTAINS folded-hemisphere normals (2 637 of them). **(B)** the 4-bit delta nibbles read
low-first — the packing the extension's own worked example misprints (it prints `delta1` twice) →
`vertex 160 references bone 255 but the file's armature has 39`, i.e. the trust boundary caught a
corrupted stream instead of building a garbage mesh. **(C)** the triangle codec's `fec` cutoff moved
from 13 to 15, which is the index codec's VERSION-0 behaviour → `hand-built TRIANGLES stream: index 8
is 0, not 6`. **(D)** the exponential filter's 24-bit mantissa read unsigned → `every
EXPONENTIAL-filtered translation sample matches: worst 2048`, with every mesh arm still green (that
filter rides only the animation views). **(E)** the quaternion filter's max-component cyclic rotation
pinned to 3 → `every QUATERNION-filtered rotation sample matches: worst 1-|dot| 0.9999459`.

**One falsification that did NOT fire, reported because it is the honest result.** Removing the
`| 3` from the quaternion filter's `one = input[3] | 3` left every arm green. It is not a bug in the
gate: `gltfpack` writes K = 16 for this file, so the bottom two bits it borrows for the
max-component index cost at most `3/32767` of the scale, which moves a unit quaternion by ~1e-4 rad —
below the `1e-6` the rotation arm asserts and below what any oracle over this file could see. The
line stays because the specification requires it; the gate does not claim to prove it.

**Anti-vacuity, three arms whose absence would have made the row measure less than it says.**
(a) mode 2 (`INDICES`) appears in NEITHER probe, so it is asserted on a stream built in the test from
the specification's own encoder, over a sequence that interleaves two runs — because the format's one
distinguishing feature is a pair of running baselines and a decoder keeping a single `last` decodes
the first two indices correctly. (b) the `0xXd`/`0xXe` triangle codes appear in neither probe either —
MEASURED, by falsification (C) passing before the hand-built arm existed — so the version fact the
`0xe1` header carries would have gone in unfalsified. (c) the exponential arm counts how many of its
1 545 samples are NEGATIVE (1 169), since reading that mantissa unsigned is exact for every positive
value.

| # | Closed fact | Evidence |
|---|---|---|
| A7 | **An author's `.ogg` and `.mp3` become PCM with nothing but this mod — no Unity audio device, no byte flipped in `globalgamemanagers`, no external converter.** The bake used to REFUSE compressed audio unless the session was launched with `autogate -UnityAudio`, which flipped `m_DisableAudio` in a CORE GAME FILE so the engine's own decoders would run. That violated both the author mandate ("if a file needs an external tool, that is a ContentTool bug") and the standing rule that ContentTool never modifies original game files. `WwisePcm.ReadAudio` now decodes all three accepted formats in-house: `.wav` by the tool's own reader, `.ogg` through the **already-merged** NVorbis (a plain Ogg container is the EASY case for it — `WwiseWem.PacketSource` exists only because Wwise strips the header packets, and none of that reconstruction applies), `.mp3` through NLayer (MIT, 70.5 KB, vendored from ResourceReplacer's `pp-native\lib\`, merged by `ILRepack.targets` exactly like NVorbis). `-UnityAudio`, `tools\audio-flag.ps1` and `src\Project\EngineAudio.cs` are DELETED. | **The oracle is Unity's own decoder, which is what makes this a replacement and not a claim.** The one run that ever had `m_DisableAudio` off measured `F1-aud-aud-ogg PASS decoded 22050 samples 1ch 44100Hz peak=0,128` and `F1-aud-aud-mp3 PASS decoded 27648 samples 1ch 44100Hz peak=0,119` (`HANDOFF-2026-08-12.md:186-188`). In-house, on the same two probes: **`.ogg` 22050 frames peak=0,128** — identical; **`.mp3` peak=0,119** — identical to the thousandth; **`.mp3` 24192 frames** where Unity gave 27648, and 24192 is exactly what the container declares (21 × 1152, `SourceAudio` reads it off the frame headers), so NLayer is the CLOSER of the two to the file — Unity was adding three whole MPEG frames of its own padding. In game, no flag: `A6 PASS chime.ogg decoded 1ch 44100Hz 22050 frames peak=0,128`, `A6 PASS tone.mp3 decoded 1ch 44100Hz 24192 frames peak=0,119`, both against the source's own header. Offline arm `tests\ObjCodecTests\SourceDecodeTests.cs` (`SOURCEDECODE: ALL PASS, 16 check(s)`), which also measures the decoded **frequency** (438,3 Hz / 438,0 Hz against the probes' 440 Hz sine) — the arm that separates "a buffer exists" from "the sound came through". Falsified twice, both reverted: (a) the sample write zeroed → counts still 22050/24192 (proving the count arms alone are VACUOUS) but `peak=0,000` and `measured 0,0 Hz` on both formats; (b) the Ogg refusal made to throw instead of returning a reason → the three `.ogg` refusal arms failed by name ("must be REFUSED with a reason, but it threw InvalidDataException") while the `.mp3` arms stayed green, proving the arms are per-decoder. Ceiling: NLayer reads **MPEG-1 only**, so a sub-32 kHz MPEG-2/2.5 `.mp3` is refused BY NAME with the rate to re-export at, not silently mangled. |
| A7-skip | **A source the tool cannot use no longer aborts the whole bake.** Same defect `9a3747b` fixed in the clip path, in the audio path: one `.ogg` made `ContentProject.Load` throw, which took down the bake of every texture, mesh and model in the project. `ImportAudio` now returns a reason instead of throwing, `RefuseUnsupported` reports instead of aborting, and `BuildWem`'s "no speaker layout for 3+ channels" refusal is caught per file — each lands in `ContentProject.SourceRefusals` and is printed by the bake as `SOURCE SKIPPED: …`, never silently. | The blocked gates now run to completion in the same session, with no flag: `U7 PASS`, `U7-mecanim PASS`, `U9 PASS`, `U9-mecanim PASS`, `M1-wrote PASS` for both `rig` and `spider` — all of them previously unreachable, because the sample project ships `chime.ogg` and `tone.mp3` and the refusal fired before the bundle was written. Controls preserved and now synchronous (the coroutine existed only because the ENGINE could not answer in the frame it was asked): `A6-ctl-junk PASS` for random bytes named `.ogg` AND `.mp3` (two different libraries, so one arm each), `A6-ctl-name PASS` for `.flac/.m4a/.aac/.wma/.opus` refused by name with `.wav` not swept up. Offline: the refusal contract is asserted directly — a decoder that THROWS instead of returning a reason fails the gate, which is falsification (b) above. |

## A downloaded creature, its own rig, its own clips (U11–U13, 2026-08-23)

| # | Closed fact | Evidence |
|---|---|---|
| U11 | **IN-GAME GREEN 2026-08-27** (was PENDING-INGAME). An UNMODIFIED CC0 download bakes into a prefab + 39-bone skeleton + its own five clips + `Animator`/AOC, and Mecanim plays them — **zero runtime code, zero external tools**. The demo is `demos\CustomCreature`: `ppcontent.json`, `Content\Models\spider.glb` (Quaternius' "Spider", 130 436 B, SHA-1 `973ee4d7…`, `EXT_meshopt_compression` + `KHR_mesh_quantization`, 13 of 13 bufferViews compressed) and `SOURCES.md`. There is no JSON for the model: a file under `Content\Models\` IS the declaration, and its clips come with it because they are inside it. This is also the run that CLOSED **U10** in the game, being the first bake of a COMPRESSED file there | **Measured live 2026-08-27, run R2** — `ct_creature gate spider_demo_before customcreature` on a spawned actor, **19 of 19 arms**, same rig as P4d (`VERIFIED-DEMOS.md`, commit `5d8fb39`). The discriminating readings are the spider's OWN content: the animator played **`cyborg_spider_spider_attack_1` / `_attack_2` / `_walk` / `_idle` / `_death`** — the file's own five clips, under names no shipped template carries — plus its own hitbox (`ct_hitbox`) and its own aim point (`ct_creature_…_BashPoint`); `Data.Strength=4` → 3 health slots; it took a bash `Fishman_12` **190,0 → 130,0** and a spit **130,0 → 120,0** (4 → 5 statuses); it walked 2,83 tiles in 0,69 s = **4,12 tile/s**. **The control is CROSS-LAUNCH, not same-run** — `ct_creature gate` picks one template per run, so the shipped-template reading (R1, `Acidworm`/`Fireworm_1`: animator `[Fireworm_idle_loop → Fireworm_move_loop]`, `Data.Strength=0` → `CONTENT-DEFECT, born dead`, `C1-melee FAIL`, 2,32 tile/s) came from a DIFFERENT launch of the same build. Per `METHODOLOGY.md` that control establishes only that the harness can report failure, which it did; everything this row asserts about the spider's own clip names, hitbox name, aim-point name and stat values is internally discriminating without it. **OPEN, carried not closed:** `ppcontent.json` asks `health: 40` and the bake line prints `Health.Max = Toughness 0 + 4 x 10,00 = 40`, but the spawned actor measured **`Health.Max = 60,0`** with TFTV 1.1.4.5 resident — the bake-time number and the in-game number disagree under a mod stack and no layer has been identified. `ct_creature gate` must be issued from the MAIN MENU: re-issued while a previous gate's mission was live it answered `C1 VOID - no arm ran` and measured nothing. Bake commit `77b1c79`. Ceilings: ~~a baked clip does not loop~~ — **closed, U12** · ~~the AOC always takes the first bakeable clip~~ — **closed, U13** · the actor half (defs, AI, spawn) is the AUTHOR's, and is not a body-part swap: `Addon.AttachVisuals` reparents a body-part prefab's bones onto the ACTOR's rig BY NAME (`Addon.cs:1203-1232`) and calls `VisualRoot.ResetTransform()` (`:1080`), so a foreign 39-bone skeleton matches nothing and a rotation or scale baked onto the prefab root is thrown away on that path |
| U12 | **PENDING-INGAME (offline green).** **A baked clip LOOPS through `m_MuscleClip.m_LoopTime`, and through nothing else.** MEASURED over all **650** `AnimationClip`s of nine shipped bundles: `m_WrapMode` is **0 on every one of them**, looping or not (it is the legacy `Animation` component's field; these are `m_Legacy=false`), while `m_LoopTime` is true on **132** and false on **518** — `px_equipment` ships `Turret_ShootLoop` (true) beside `Turret_ShootEnd` (false), both wrap 0. Nothing travels with it: `m_CycleOffset` 0 on all 650, `m_StartAtOrigin` true on all 650, `m_LoopBlend` true on only 20 — every one of those already `m_LoopTime=true`, so it is an extra, not a requirement. glTF carries **no loop flag** (checked: the probe's only `extras` are material entries), so the AUTHOR declares it: one optional `ppcontent.json` string `"loop": "Spider_Idle, Spider_Walk"`, naming the `.glb`'s own clip names, case-blind | `dotnet run --project tests\ObjCodecTests`, commit `c057113`: a SHIPPED loop/one-shot PIN plus both directions asserted on the five baked spider clips (`ClipBake.cs`), and the parse plus the refusal (`ClipPlan.cs`). A name no model carries is a bake FAILURE (`clip-names`) that lists the names the project DOES carry — never a silent no-op. Falsified three times: writing `m_WrapMode=2`; hardcoding the flag; making the name match case-sensitive |
| U13 | **PENDING-INGAME (offline green).** **The author picks WHICH clip the baked `Animator` plays**: optional `ppcontent.json` `"play": "Spider_Walk"`, the `.glb`'s own clip name, case-blind — absent = the first bakeable clip, which is what every bake did before. `ClipFields.Chosen(plan, play)` is the ONE resolver, used both by the bake (which clip the AOC overrides) and by the `U9`/`U9-mecanim` drive arms (whose samples the rig is asserted against), so the two cannot disagree | `dotnet run --project tests\ObjCodecTests`, commit `93f3901`: the AOC's override clip read back off the FILE by name in `ClipBake.cs`, with `"play"` deliberately NOT the file's first clip, and `'Walk'`/`'walk'`/unbakeable `'Morphs'` in `ClipPlan.cs`. A declared name that is not in the plan returns **-1, never a silent fallback to clip 0**; the project-level `clip-names` arm validates `"loop"` and `"play"` against the BAKEABLE clip names of every model and FAILS by name, which also catches declaring a clip that drives no bone and is therefore never written. Falsified by returning 0 for every match, and by falling back to 0 instead of -1. Ceiling: **ONE clip per model** — a state machine that switches Idle/Walk needs a `ControllerConstant`, which this route does not serialize |

## Replacement seam (FINAL-PLAN §39) — developer-mode binding

Harness: `ct_seamprobe on | mutate | report | off` (`ContentTool\src\Probe\SeamProbe.cs`), commits
`646a75d`, `8985aed`, `59b83f1`, `65c8366`, `beff36d`. Four in-game runs on 2026-08-12; the first
three produced no R-U2b number and it was the instrument, not the game, that was corrected each time.

| # | Closed fact | Evidence |
|---|---|---|
| 18 | **`AddonSkinDataBase.GetPrefabAsset` is a real, usable identity seam.** A Harmony postfix there hands over the `AssetGUID` and the resolved prefab together, and the subpath built from that prefab resolves back to a live component | Bind proof printed at `on` (13:11:52): `patched PhoenixPoint.Common.Entities.Addons.AddonSkinDataBase.GetPrefabAsset(AssetReferenceGameObject assetReference) -> GameObject in Assembly-CSharp; postfixes now attached to THAT method: 1`. Report 13:12:35: `R-U1 seam: guids=26 resolvableTargets=25 riggedRenderersUnderSeamPrefabs=32 resolvesWithNoGuid=0 postfixErrors=0`. Larger run 12:53:10, inside a tactical mission: `guids=61 resolvableTargets=60 riggedRenderersUnderSeamPrefabs=73 resolvesWithNoGuid=0 postfixErrors=0`. Sample target: `guid:441cd78eea3f7d54bbac35a1c80c55fd#CHR_PX_UNA_TS_V01@SkinnedMeshRenderer.mesh resolvable=True`; weapons arrive through the same seam as `@MeshFilter.mesh` |
| 19 | **A prefab-level write REACHES rendered objects.** Instances built after the write wear our mesh; already-built ones do not | 13:12:08 `mutate: marked 25 new prefab slot(s), 0 already marked, 1 skipped` → `R-U2b instancesWearingOurs=0 instancesWearingOriginal=7` (before any rebuild). After re-dressing a soldier, 13:12:35: `R-U2b instance side: instancesWearingOurs=4 instancesWearingOriginal=3 -> instances DO inherit the prefab write`. Corroborated visually in the same run — taking the armour off a soldier showed NO body, i.e. a live instance wearing the 3-vertex `ct_probe_mesh`. The log is the evidence; the observation only agrees with it. Restore was complete: 13:12:41 `R-U2 restored 25 slot(s), 0 already released` |
| 20 | **The write is NOT permanent: Addressables releases and re-acquires prefabs and a one-shot write goes with them.** The binding is apply + RE-APPLY at the seam | 12:53:10, after a mission load: `R-U2a ... resolvesSinceWrite=308 stillOurs=206` — 102 of 308 resolves came back without our mesh; per-guid `seen=410 sameInstance=308`, the same ~25% split on every other prefab (`CHR_PX_Sniper_M_V01_LeftArm_Ready seen=100 sameInstance=75`); `off` reported `R-U2 could NOT restore - the renderer we wrote is gone`. With no scene transition the write does hold: 13:12:35 `resolvesSinceWrite=88 stillOurs=88 reapplied=0`, `R-U2c re-acquire: 0 of 190 resolves returned a DIFFERENT prefab object` |
| 21 | **Seam coverage of rigged renderers, with a same-run scan control** | 13:12:35: `R1 COVERAGE 72,7% = 8 of 11 in-scene rigged renderers reachable through the seam`; control `CONTROL scan: SkinnedMeshRenderer loaded=375 inScene=11 attributableToASeamPrefab=8 coverage=72,7%`. Attribution is by Mesh reference re-read from the live prefabs at report time |

> **Scope of row 21, so it is not over-read.** 72,7% was measured OUTSIDE a tactical mission (11
> in-scene rigged renderers; `R-U2c 0 of 190` re-acquires ⇒ no scene transition happened). The one
> run that did load a mission (12:53, 125 in-scene rigged renderers) reported `coverage=0,0%` from an
> attribution bug — instance ids captured at first sight are dead after re-acquisition — fixed in
> `65c8366` and never re-run at mission scale. The earlier `100,0% = 11 of 11` readings (12:37, 13:02)
> are the same small non-mission scene. **Mission-scale coverage was UNMEASURED until row 24 below;
> 72,7% is the roster figure and is not the mission figure.**

| 24 | **Mission-scale seam coverage, measured INSIDE a loaded tactical mission: `100,0% = 134 of 134`.** The seam holds at mission scale — it is not a roster-only property. Reached unattended: the gate names a savegame, refuses anything that does not declare `IsTacticalSave` in its own metadata, and hands it to the game's own `PhoenixGame.FinishLevelAndLoadGame` (what `load_game` does, `Base.Serialization\SerializationCommands.cs:41`) | `ct_mission gate 2` 2026-08-12, build `c47e1f6d` confirmed in-phase by autogate. `M1-in-mission PASS ... TacticalLevelController, mission=...TacMission turn=1 factions=4 actors=142` · `M1-scale PASS inSceneRiggedRenderers roster=70 -> mission=134` (the same-run baseline, recorded before the load) · `R1 COVERAGE 100,0% = 134 of 134 in-scene rigged renderers reachable through the seam (seam guids=126 resolvableTargets=124)` · `R-U1 seam: guids=126 resolvableTargets=124 riggedRenderersUnderSeamPrefabs=135 resolvesWithNoGuid=0 postfixErrors=0` · control `CONTROL scan: SkinnedMeshRenderer loaded=611 inScene=134 attributableToASeamPrefab=134` · `R-U2c re-acquire: 0 of 1697 resolves returned a DIFFERENT prefab object`. The seam still WRITES there, not merely sees: R2 (six arms) and R3 (six arms) both ran inside the mission and are green, revert asserted by object identity. Impl: `src\Dev\MissionGate.cs` |

> **Row 24's ceilings, so it is not over-read.** (a) The number is one mission (save `2`, 4 factions,
> 142 actors, turn 1) on this machine — coverage is a property of what that scene instantiates, and a
> mission with scenery no resolve goes through would read lower; that is what `ct_scan` (row R4)
> exists for. (b) `R-U2c 0 of 1697` says no prefab was re-acquired **during this measurement window**;
> it does NOT retract row 20, which measured re-acquisition across a scene transition. (c) The gate
> depends on a tactical save existing on the machine (`ct_mission list` prints which), so it is
> deliberately NOT in autogate's default command list.
>
> **Falsified twice**, build `0e666180`, same shape: with `FinishLevelAndLoadGame` commented out and
> the wait cut to 20 s, `M1 VOID no tactical mission became live within 20s of loading '2'
> (level=HomeScreenLevel(Clone) actors=0)` and no R1/R2/R3 arm printed at all — the gate cannot pass
> at roster scale. Second, on the un-disarmed build `c47e1f6d`, pointing it at a geoscape save:
> `M1 VOID REFUSED: 'autosave' declares IsTacticalSave=False (saveType=Autosave)`.
> The first falsification also caught a real defect in the gate: it printed `M1 VOID` and then signed
> off `ct_mission: M1 arms PASS`, because zero arms means zero failures. The trailer now reports
> `M1 VOID - no arm ran` whenever no arm ran.

| 22 | **Dev-mode mesh AND material replace + revert, reverting to the exact origin OBJECTS.** RR's mechanics ported: skinned `sharedMesh` assign without touching the skeleton (`MeshReplacer.cs:1896-1901`), material via `new Material(original)` with the whole `sharedMaterials` array assigned in one go so the game's own Material is never written (`:2110-2132`), revert from an origin map (`:2335-2368`) | `ct_meshswap` 2026-08-12, gate R3, TWICE on different subjects, all six checks green both times. 13:34:10 on `Head_Afro1_M_V01_Ready`: `R3-mesh PASS wears 'ct_swap_quad' verts=4 bounds=(0.4, 0.4, 0.0) (origin verts=2925)` · `R3-material PASS material 'Head_Afro1_M_V01 [ct]' shader='_PX_CHR/CHR_Character_Corrupted_shader' _Color=RGBA(1.000, 0.000, 1.000, 1.000) (the game's own material object untouched: True)` · `R3-CONTROL-mid PASS` · `R3-revert-mesh PASS sharedMesh == the origin Mesh object: True` · `R3-revert-material PASS sharedMaterials[0] == the origin Material object: True` · `R3-CONTROL-end PASS sibling unchanged throughout` · `ct_meshswap: R3 PASS`. Repeat 13:35:08 on `Head - Tutorial - Female_Ready` (origin verts=2960, shader `_PX_CHR/CHR_Character_shader_OLD`), same six. The shader name is asserted UNCHANGED and revert is asserted by object identity, not by value. Impl: `src\Dev\SeamSwap.cs`, commit `d036985` |

| 23 | **Dev-mode texture replace + revert, exact.** The replacement is OUR `Texture2D` bound through a CLONED material (`SetTexture`, whole `sharedMaterials` array assigned at once); the game's own `Texture2D` is never written, so revert is a reference restore of the origin array with nothing to reconstruct | `ct_texswap` 2026-08-12 13:59:16, gate R2, subject `'UI_ArbitraryIcon_Circle_Fuel_uinomipmaps' 128x128 BC7 on 'Circle (2)' materials[0]`: `R2-replaced PASS the renderer now shows 'ct_swap_magenta' sha1=cdb816de… (was e8c39d7d…)` · `R2-untouched PASS the game's texture object during the swap: e8c39d7d…` · `R2-reverted PASS pre=e8c39d7dd7cb608ee3998b48814295287a43b87b after-revert=e8c39d7dd7cb608ee3998b48814295287a43b87b` (EQUAL) · `R2-revert-identity PASS materials[0] and its _MainTex are the origin OBJECTS again` · `R2-CONTROL PASS control sha1 e3ebc6cd… / e3ebc6cd…` · `ct_texswap: R2 PASS`. No exception. `R2-untouched` is the check that keeps the destructive path from returning: an in-place `LoadImage` fails it by construction. Impl: `src\Dev\SeamSwap.cs`, commit `d764513` |
| 25 | **The AUTHOR's own model goes into a renderer that is already on screen — an author iterates on a mesh with the game running.** `ct_replace <target> <file.glb|.obj>` reads the model through the SAME importers the bake uses (`GlbReader`+`ModelBuild`, `ObjCodec`+`MeshBuild`) and assigns the resulting live `Mesh`; a RIGGED target keeps its shipped skeleton via the runtime twin of `SkinFields.Rebind` (target's own bind poses, one full-weight influence per vertex by nearest bind pose), and `localBounds` moves with the mesh. Material properties beyond textures are writable the same way (`.col:_Prop` / `.num:_Prop`), always on a CLONE. The target path names the slot, so there is one command and one apply path | `ct_liveswap` 2026-08-12, gate R5, build `2915792a` confirmed in-phase by autogate, subject `'CHR_PX_OP_LL_M_V01'` (SkinnedMeshRenderer, unique name, origin mesh verts=2225, 14 bones), control `'star'` mesh `'Quad'`: `R5-mesh PASS the renderer wears 'ct_live_gate.glb' verts=6 indices=9 bounds=(1.0, 3.0, 0.0)` (the SAMPLE .glb's own numbers) · `R5-skin PASS ours=True verts=6 bindposes=14 bones=14 weights=6 boneMax=13 inRange=yes` · `R5-CONTROL-during PASS the control still wears 'Quad' and its own materials` · `R5-revert PASS sharedMesh == the origin Mesh OBJECT again: True` · `R5-material PASS materials[0] is 'CHR_PX_ASS_LG_Accessories_M_V01 [ct]' shader='_PX_CHR/CHR_Character_shader' _Color=RGBA(1,0,1,1) (the game's own material still reads RGBA(1,1,1,1))` · `R5-mat-revert PASS` · `R5-CONTROL-after PASS` · `ct_liveswap: R5 PASS`. FALSIFIED TWICE, each arm alone: (a) the live rebind disarmed (build `1a40afe8`) -> `R5-skin FAIL bindposes=0 bones=14 weights=0 boneMax=-1`, every other arm still green; (b) the mesh write disarmed (build `c27caef4`) -> `R5-mesh FAIL the renderer wears 'CHR_PX_OP_LL_M_V01' verts=2225` AND `R5-skin FAIL ours=False verts=2225`. That second run also caught a vacuous arm and closed it: the SHIPPED mesh hands its own `bindposes`/`boneWeights` over, so R5-skin now asserts the mesh is OURS by reference and by the file's vertex count. Ceilings: ~~one full-weight influence per vertex (a joint creases); the .glb's own WEIGHTS_0 is unusable live (its joint indices are the file's, the renderer wears the game's skeleton)~~ — **RETIRED by P6/R6**: a `.glb` with an armature binds with its own `WEIGHTS_0`, bones matched by NAME, and `.obj` / a foreign armature fall back to nearest-bone and say so; when the shipped mesh will not hand its bind poses over they are derived from the skeleton AT ITS CURRENT POSE; one swap per anchored object at a time. Impl: `src\Dev\LiveMesh.cs`, `src\Dev\SeamSwap.cs` |

> **Why row 23 is green is the point:** the previous attempt wrote the game's texture in place with
> `ImageConversion.LoadImage` (RR's mechanic, `Resource_Replacer.cs:1165-1167`) and captured the
> original first so it could be put back. That failed twice - see the R-U4 note below - and the fix
> was not a better capture but not needing one. FINAL-PLAN §39.3's Texture2D row is amended to match.

> **R-U4 is ANSWERED NO, and then made MOOT - two different things, both true.** Answered:
> `ct_texswap` 13:47 measured that a §29.5 blit readback does NOT round-trip. On
> `UI_ArbitraryIcon_Circle_Fuel_uinomipmaps` the captured PNG decoded into a fresh texture hashed
> `10217be4…` against a pre of `e8c39d7d…`, and the restored texture hashed exactly that same
> `10217be4…` — the restore was faithful to a capture that was already wrong. A second, independent
> loss sat on top: `LoadImage` re-encodes the container (`BC7 -> ARGB32`; on
> `CHR_PX_ASS_LG_Accessories_V01_albedo` `DXT1 -> DXT5`). Moot: the dev swap no longer reads or
> writes through that path at all, so the answer no longer gates anything. If a future feature needs
> a faithful readback of a compressed texture, this question is open again and the answer so far
> is no.

> **The earlier R2 attempt is NOT recorded - it FAILED.** 13:34:00, subject
> `CHR_PX_UNA_TS_V01_albedo 2048x2048 DXT1`: `R2-replaced PASS pre=77d7e2c8… post=e1e648c6…` but
> `R2-reverted FAIL pre=77d7e2c822952be0adcc10d0089949f5508f55fc
> after-revert=681a5488dcf95cc454666655368479350f322ac9`, with `R2-CONTROL PASS` (one sha1 three
> times) proving the measurement was isolated and the failure real. The 29.5 capture did NOT refuse -
> it captured, and the restore came back with different pixels. R-U4 therefore stays OPEN.

> (Superseded by row 24 — mission-scale coverage is now measured.) The 13:33:50 report says
> `R1 COVERAGE 100,0% = 11 of 11` with `CONTROL scan: ... inScene=11` and
> `R-U2c re-acquire: 0 of 92` - eleven rigged renderers and zero re-acquisitions is the roster/geoscape
> again, not a mission. Row 21's scope note stands unchanged.

> Not evidence, but present in the log: a `NullReferenceException` in `ModManager.GetInstance` under
> `GeoLevelController.OnLevelEnd_Patch1` at `GAME STOPPED 13:13:42` — another mod's teardown patch
> firing at application exit, unrelated to the probe.

## Also closed (decompilation, `decompiled\AkSoundEngine\`)

- Bank load validation: version range 118..140; `dwProjectID` (BKHD+0x18) is **never read** ⇒
  foreign banks are accepted; no checksum/signature; unknown chunks skipped; duplicate bankID
  → `0x45`; buffer < 28 B → `0x1F`.
- v140 has **no chunk restriction** ⇒ a HIRC-less bank is legal.
- `LoadBankMemoryView` checks `addr % uAlignment` as a real division for version ≥ 135;
  `uAlignment == 0` is a **#DE process crash**, not an AKRESULT. Always Copy.
- Oracle: `hirc_parse.py` parses all 53 shipped banks / 19110 objects / 0 mismatches.
- PP ships `StreamingAssets\Audio\GeneratedSoundBanks\Windows\` = 53 `.bnk` (117.9 MB) +
  3105 loose `.wem` (511.2 MB); game-wide `.pck` count = 0.

## OFFLINE-proven (2026-08-12) — measured against shipped data/binaries, not in-game

> Same standing as the rows above (closed, do not re-litigate), but established by measuring
> shipped files rather than by an in-game run. Oracle: `SoundbanksInfo.xml` + the 53 `.bnk`
> STID chunks. Script `ContentTool\tools\wwise_hash_check.py`, index
> `ContentTool\data\pp_wwise_index.json`.

| # | Closed fact | Evidence |
|---|---|---|
| O1 | Wwise name→ID = **FNV-1 32-bit (multiply-then-XOR), name LOWERCASED (UTF-8)**, basis 2166136261, prime 16777619, **no masking** | exact on **1078/1078** name-based pairs: Event 764/764, SoundBank 53/53, Switch 221, SwitchGroup 13, GameParameter 9, State 7, StateGroup 5, Trigger 2, SetState 2, TriggerEntry 2. Falsifiable: FNV-1a scores **0/1078** in every form and non-lowercased FNV-1 **0/1078**; 30-bit-masked FNV-1-lower matches only a strict subset (201/764 events — those with top 2 bits already zero), never exact. Cross-checks: 5/5 STID bank-name→bankID pairs inside the `.bnk` binaries; `fnv1_lower32("UI") == 0x5C770DB7` |
| O2 | Media/WEM IDs are **NOT** name-hashed — they are Wwise-allocated | **0/7691** match under any candidate; 242 distinct `ShortName`s map to MULTIPLE IDs (one source file, several conversions) ⇒ no name→media-ID function can exist |
| O3 | PP ID index is a measured SET (validator must load it, never hold a constant) | **7697** known media IDs = 7692 distinct `File` ids in `SoundbanksInfo.xml` (1 of them has no `ShortName`) + 5 loose `.wem` on disk absent from the manifest. Also 764 events, 53 banks, 221 switches, 13 switch groups, 9 RTPCs, 7 states, 5 state groups, 2 triggers. (Earlier "7696" was imprecise.) Use the JSON's `_media_ids_all` (7697), NOT `_media_ids` (7691 named only) |
| O4 | `0x5C770DB7` really exists as a bus | HIRC object id `0x5C770DB7` in `Init.bnk`, **type 8 (AuxBus)**, 115 B, name `"UI"`. A missing bus is a hard bank-LOAD failure, so existence had to be shown, not assumed |
| O5 | PPModLoader installs no `AssemblyResolve` ⇒ the ILRepack merge requirement stands | `decompiled\AssemblyCSharp\Assembly-CSharp\src\PhoenixPoint.Modding\ModSDKContext.cs:51-63` — `LoadModdingAssembly` = `Assembly.Load(rawAssembly[, pdb])`; **zero** `AssemblyResolve` hits in the whole `Assembly-CSharp` tree |
| O6 | DIDX `size % 12 == 0` holds in shipped data | all **45** DIDX chunks across the 53 banks, **0** violations |
| O7 | Supporting file facts | `classdata.tpk` is exactly **289 605 B**; `UnityEngine.AssetBundleModule.dll` present in `PhoenixPointWin64_Data\Managed\` (21.5 KB); `%LOCALLOW%\Snapshot Games Inc\Phoenix Point\` exists |

## Video (the streamable side catalog — NOT route vii)

| # | Closed fact | Evidence |
|---|---|---|
| V1-add | **An author ADDS a video Phoenix Point never shipped** — a new `Catalog.json` row with a DERIVED `RuntimeKey`, resolvable at game start AND, in the same launch that wrote it, from the RUNNING game | `autogate.ps1 -PPRoot D:\PP-Instance2 -UnityAudio -Commands ct_video revert,ct_project,ct_video apply,ct_video verify`, 2026-08-12, build `b93afbdd` asserted in-phase (first proven on `428b373d`): `V1-write PASS 2 row(s) rewritten on a rebuild of the PRISTINE catalog, 70 rows total, no duplicate RuntimeKey` (69 shipped + our one add) · **`V1-live PASS 7a0d77654bdf44e81f3233dceaef42f2 resolved to 'D:/PP-Instance2/PhoenixPointWin64_Data/StreamingAssets/' (the bare streaming root - unknown key) BEFORE the write, and to '…/StreamableCopiedAssets/Videos/morgott.sample/probe_add.webm' after the write plus Uninitialize()+Initialize(), in ONE launch with no restart`** · `V1-add-live PASS the ADDED RuntimeKey … (in no shipped catalog row: the .ct-backup has 69 rows and none of them carries it) resolved to …/probe_add.webm and decodes as frameCount=60 256x144; the mod's own file opened directly is frameCount=60 256x144` · `V1-ctl PASS untouched row 37a0c730… still resolves to …/FesteringSkies_Cutscene_4FINAL.webm and decodes as frameCount=946 1920x1080` · `V1-missing PASS … NOT PREPARED` · `V1-url`/`V1-frames PASS` (the REPLACEMENT half, 60 frames against the shipped clip's 1641 at 1280x720) · `ct_video verify: ALL PASS` · `ct_project: ALL PASS` at sample stamp 13. Impl: `src\Bake\CatalogText.cs` (`Append`, `KeyFor`, `Guard`), `src\Bake\VideoCatalog.cs` |

**AT GAME START, the shipping claim, second launch** (`ct_video status, ct_video verify` only —
nothing applied and nothing reloaded in that session, build `428b373d`): the game **BOOTED with the
70-row catalog** (no `ToDictionary` throw, no dead loading scene) and `V1-add-live PASS` off the FILE
alone. Zero runtime code takes part in that resolution.

**The key is DERIVED, never random:** `CatalogText.KeyFor` = MD5(`<modid>/<clipstem>`), 32 lowercase
hex, the shipped GUID shape. The author pastes the printed string into their own
`VideoPlaybackSourceDef`, so it has to survive a re-apply and be identical on every machine.

**The duplicate-`RuntimeKey` refusal, in game, against the game's OWN shipped key, BEFORE any write**
(`StreamableAssetsCatalog.cs:22` `ToDictionary` throws inside `Awake` and the boot scene never comes
up): `V1-dupkey` / `V1-dupkey-write` / `V1-add-dupkey PASS` — `cdd4584fdc6b7ad4992c6abf18e40d6e`
twice is `REFUSED: … the game would throw in Awake and fail to boot. Nothing was written.` The
appending arm asserts the append really happened (70 rows) first, so the refusal cannot be vacuous.

**FALSIFIED, build `8ec1fb0b`**, by disarming `Reload()` (the `Uninitialize()+Initialize()` pair):
`V1-live FAIL … and to '…/StreamingAssets/' after the write` · `V1-add-live FAIL … decodes as NOT
PREPARED (Can't play movie […/StreamingAssets/])` — while **`V1-write` stayed PASS at 70 rows in the
same run**, so the file was written correctly and only the live half broke. The two arms provably
measure different halves.

Proven OFFLINE against the real 69-row shipped `Catalog.json` as well
(`tests\ObjCodecTests\VideoCatalogTests.cs`, `VIDEO catalog PASS, 18 check(s)`), falsified there
twice — `Guard`'s duplicate lookup disarmed → `(NOT REFUSED)`, and `Append` disarmed → `69 == 69 + 1`.

Ceilings, deliberate: ContentTool's remit ends at the clip and the row. Whether a cutscene PLAYS the
added video needs a `VideoPlaybackSourceDef`, and **defs are the mod author's job** — `V1-add-live`
proves the game's own `StreamableAssetsManager` hands the key back and Unity's decoder opens the
file. Both `apply` and `revert` re-read through the manager, so a live session is never left serving
a clip that was just uninstalled.

## Video, served from the MOD's folder — nothing in the install is written (V1-open)

Harness: `ct_video live <project> | resolve <key> | open <key>` + `src\Bake\CatalogLive.cs`.
Supersedes the `Catalog.json`-editing route for shipping (that route still exists and still works).

| # | Closed fact | Evidence |
|---|---|---|
| V1-open | **A mod's own clip, in the mod's own folder, replaces a shipped cutscene with NO game file modified.** The catalog is extended in memory: `StreamableAssetsCatalog.AllLocations` is a public field and `InitializeCache()` a public method, so only `StreamableAssetsManager._catalog` needs reflection | autogate `-PPRoot D:\PP-Instance2`, build `20d37af3` confirmed in-phase, 2026-08-13. Same key, same run, before and after `ct_video live IntroVideo`: `V1-open PASS e574fca8ff2123b48850c43faa7e08c1 -> …/StreamableCopiedAssets/Videos/Factions/Phoenix/PP_Intro.webm decodes as frameCount=3652 1920x1080` then `V1-open PASS … -> …/StreamingAssets/../../Mods/ContentTool/IntroVideo/Content/Videos/campaign_intro.webm decodes as frameCount=180 1280x720`. Two identities (the resolved path AND the decoded frame count), and the shipped clip's own 3652 frames is the positive control |
| V1-dotdot | **The `..`-escaping `StreamingPath` WORKS** — the open question the spike flagged as untested. `GetStreamingPath` is `StreamingRoot + "/" + StreamingPath`, so a mod-folder file is reached by `../../Mods/…`, and the decoder opens it | same run, the `after` line above. The planned fallback (a `GetStreamingPath` postfix returning an absolute path) is **not needed** |

- **Safer than editing the file.** `InitializeCache`'s `ToDictionary` still throws on a duplicate
  `RuntimeKey`, but now inside OUR call instead of the game's `Awake` — a bad key can no longer kill
  the boot scene. `CatalogLive.Inject` refuses and leaves the live catalog untouched.
- **CORRECTED 2026-08-28.** This row previously read "a content mod must call `CatalogLive` BY
  REFLECTION, never with an assembly reference", attributing the incident to the `ContentTool.dll`
  reference. **The consequence is real, the cause was misattributed.** The measured 2026-08-13
  incident is commit `632fba7`, and it names the failed reference as **`UnityEngine.VideoModule`** — a
  `Managed\` Unity module `ModSDK\` does not ship; commit `2176249` says the same of QuitCutscene's
  earlier `Managed\` reference. PPModLoader installs no `AssemblyResolve` (O5), so such a reference
  fails the mod LOAD — and Phoenix Point answers a failed mod load by **rewriting `MOD_ACTIVATED`
  empty**, silently disabling every other mod including ContentTool. It cost two runs and reset the
  test profile. A direct `ContentTool.dll` reference does NOT hit this: Mono caches
  `Assembly.Load(byte[])` results by full name in the AppDomain, and the loader recursively enables a
  declared dependency before its dependents (`ModEntry.cs:126`, `ModManager.cs:200`) for both the
  local and the Workshop loader. `demos\WeaponAdd` and `demos\CustomCreature` have shipped a direct
  `<Reference Include="ContentTool">` with `<Private>false</Private>` since `ee8b3ff` / `e28c43c3`,
  both verified in game 2026-08-28 (`ct_weapon PASS`, a 19/19 creature gate — both printed only from
  inside the engine call, so the binding resolved). The genuine fragility is **API/version skew**:
  `Dependencies` carries an id and no minimum version (`ModMeta.cs:46`), so an older ContentTool
  satisfies the dependency and the load order while lacking a referenced type or method →
  `TypeLoadException` / `MissingMethodException` at runtime. Reflection can log and degrade; a hard
  reference cannot. That, not resolution, is why the video demo reflects.
- **UNMEASURED:** that the `Initialize` postfix re-injects across a scene change (the run that would
  have shown it died on `ct_video defs autosave VOID no GeoscapeView came up within 420s`). The
  postfix is installed and the mechanism is sound; the survival itself is not yet a fact.

| Q1 | **PARTIALLY CLOSED 2026-08-27 — evidence-incomplete, NOT green** (was PENDING-INGAME). A video plays when the player quits, then the game exits: one Harmony prefix on `PhoenixGame.FinishLevelAndQuitGame` hands a runtime `VideoPlaybackSourceDef` to `HomeScreenView.ToCutsceneState`, and the game's own machinery does the rest (`UIStateHomeScreenCutscene.OnInputEvent:92-104` already routes `Cancel`/`Submit` to `OnCancel()` when `IsInterruptible`, so ESC needed no input handling — only `SkipOnPlayerInput = true` on the def). Demo `demos\QuitCutscene`; the CONTENT half needs no code (ContentTool serves the clip from the mod's folder, V1-open), the TRIGGER half ships ~40 lines, and that split is the whole point of the demo | **Measured live 2026-08-27, run R3** (menu → quit) on `D:\PP-Instance2`, ContentTool `1.0.0.0 build=b078ff68` (`VERIFIED-DEMOS.md`, commit `5d8fb39`). A REAL `PhoenixGame.FinishLevelAndQuitGame` was intercepted and the game's own `HomeScreenView.ToCutsceneState` played it: `prepared=True playing=True` **`frameCount=90`** `length=3s 1280x720 playbackSource=QuitCutscene_Runtime`, and then **the process exited**. **WHAT THE EXIT DOES NOT ESTABLISH:** the watchdog (`QuitCutsceneMain.cs:270-291`) is armed UNCONDITIONALLY and calls the same `Quit()` after `length + 10s` — 13s for this clip — so "playback started and the process later exited" is equally consistent with the intended path (`ToCutsceneState`'s callback → `Q1-exit the cutscene finished or was skipped; quitting for real now`, `:341`) and with the deadline firing (`Q1-watchdog the clip's 13.0s are up and nothing quit the game`, `:287-289`). The discriminating line was NEVER CAPTURED: the recorded evidence (`VERIFIED-DEMOS.md`, row QuitCutscene) ends at the `Q1-play` reading and "then the process exited", with no `Q1-exit` and no statement that `Q1-watchdog … are up` was absent. **To close Q1 properly, one future run must capture `Q1-exit the cutscene finished or was skipped; quitting for real now` in `Player.log` AND the absence of `Q1-watchdog … are up`.** What IS measured stands: the interception, the resolve, and the game's own decoder on our clip. The 90 frames are the discriminator: the same key read cold, `ct_video open 6f3d8e3d…` → `…\Mods\QuitCutscene\Content\Videos\quit_outro.webm`, **90 frames 1280×720**, against the shipped control where the key does not exist at all and every one of the 67 shipped `VideoPlaybackSourceDef`s resolves into `StreamingAssets`. Trigger commit `8f86b38`. The def MUST come from `DefRepository.CreateRuntimeDef` (`DefRepository.cs:214`, the same factory `BaseDef.cs:128` uses) and NOT `ScriptableObject.CreateInstance`, which leaves `BaseDef.Guid` and `BaseDef.ResourcePath` null and never registers it — playback reads neither, so the defect is invisible until TFTV's `UIStateHomeScreenCutscene.EnterState` postfix (`refs\TFTV-src\TFTV\TFTVUI\Common\Various.cs:108-127`) runs `_sourcePlaybackDef.ResourcePath.Contains("Game_Intro_Cutscene")` with *skip movies* on: it guards a null DEF (`:119`) and not a null `ResourcePath`, so ours threw `NullReferenceException` inside that postfix on every entry, caught and logged (`:130`) and announcing nothing. And the `ResourcePath` must not LIE — ours is `Morgott/QuitCutscene/quit_outro`, deliberately not containing `Game_Intro_Cutscene`, or that same postfix cancels our cutscene on sight. The arm chain this row was written against, and the shape of the run that produced the reading above — quit from the **MAIN MENU**, then `Q1-bound PASS` → `Q1-src PASS … exists=True` → `Q1-trigger` → `Q1-play PASS … frameCount=90 length=<seconds>` → `Q1-watchdog armed: this quit happens in <length+10>s at the latest` → `Q1-exit` (and NOT `Q1-watchdog … are up`, which would mean the clip stalled or `VideoPlaybackStopped` never fired and the deadline had to do the quitting). The watchdog is armed UNCONDITIONALLY: it used to be suppressed the moment the probe saw `isPrepared` with frames, so a clip that prepared and then stalled left the intercepted quit hanging forever with no way out but force-killing the game — the probe is a measurement, not a promise that the clip ends. Its length is the clip's own `VideoPlayer.length` (`Double length`, read off the shipped `UnityEngine.VideoModule.dll`) plus 10s of grace, and a flat 120s when the player will not report one; `Quit()`'s idempotence is what lets the deadline exist without cutting short a clip that ended normally. Also corrected here: the earlier "entered `UIStateHomeScreenCutscene`, unwound to `UIStateInitial`" evidence is the BOOT INTRO being skipped by that same TFTV option — it is in every `Player.log` on this machine with no mod of ours involved — so the quit's own outcome was never in the log at all. Ceiling: quitting from an IN-GAME escape menu exits with no clip, because `GeoscapeView.ToCutsceneState` takes a priority and not a callback (`GeoscapeView.cs:672`); the patch says so in the log and quits normally |

## Permanently ruled out — do not re-research

External Sources (`ExternalSourcesInputFile=""`), Wwise Audio Input plugin, Unity
`AudioClip`/`AudioSource` **at runtime on a player's install** (`m_DisableAudio = true`) — **NARROWED
2026-08-12: this row said "Unity audio, full stop", and that is now measured wrong. `m_DisableAudio`
is one byte at `globalgamemanagers` offset 7168; flipped on the AUTHOR'S machine the engine decodes
`.ogg`/`.mp3` to real PCM (`research-format-coverage.md` §2.1, falsified + hash-restored), which
was taken to delete the whole Vorbis/MP3 decoder slice. What stays permanently ruled out is Unity
audio in anything SHIPPED — that is the zero-runtime-code mandate, not a property of the engine.
**RE-NARROWED 2026-08-23 (gate A7): the flip is ruled out too, and the decoder slice is BUILT.**
Reaching those decoders means editing a file in the player's own install, which the author mandate
and the "ContentTool never modifies original game files" rule forbid — so the measurement stands
and the RECIPE is withdrawn. `WwisePcm.ReadAudio` decodes `.wav`/`.ogg`/`.mp3` in-house (NVorbis
was already merged for `.wem`; NLayer added, +62.5 KB), reproducing Unity's own numbers on the same
probes. `-UnityAudio`, `tools\audio-flag.ps1` and `EngineAudio.cs` are deleted** —
own Wwise Vorbis **encoder** (PCM is the
only feasible output codec), `StartOutputCapture`/`StopOutputCapture` (AK_Success but 0 files
written), a managed Wwise I/O hook / hand-built `IAkFileLocationResolver` vtable, `SetMedia`
for the production path, session-long pinning, bank patching, `AddBasePath` **shadowing**,
staging orchestration.

Distinct from the above: a single `.pck` via `LoadFilePackage` is only **skipped for v1**
(needs a `.pck` writer, buys nothing over `stream`) — not permanently rejected.

## Production gates (2026-08-27) — zero-write shipping, all four routes live

Measured on `D:\PP-Instance2`, all in-game unless stated. Commits `b16f881`, `ce7afac`, `c4570e6`
deleted 2708 lines of install-writing code.

| # | Closed fact | Evidence |
|---|---|---|
| ZW1 | **ZERO-WRITE GUARANTEE — the mod leaves a game installation byte-identical after a full session.** Three independent full-install hash runs, SHA-256 per file: 8210 files (route vii proof), 8207 files (all four routes), 5715/5710 files outside `Mods\` (later runs) — every one ZERO differences except the game's own logs. No `.ct-backup`/`.ct-edits`/`.ct-new` produced | Commits `b16f881`, `ce7afac`, `c4570e6` (2708 lines of install-writing code deleted). 2026-08-27 |
| ZW2 | **Route vii (bundle replace) is LIVE via `ResourceManager.InternalIdTransformFunc`.** The game's own Addressables served our mesh (5554 verts vs shipped 5771) and albedo (1024 RGBA32 vs shipped 2048 DXT1); CRC suppressed 3454164017 -> 0 in memory; `aa\catalog.json` untouched at its shipped 8232 keys | 2026-08-27. Route vii now reads the catalog IN MEMORY, never from disk |
| ZW3 | **Route iii (publish new keys) is LIVE via `Addressables.AddResourceLocator`** (`ce7afac`). 3/3 keys resolved by the engine's own `BundledAssetProvider` from the mod's own bundle, forged external PPtrs resolving to shader `Standard`. **NEGATIVE proven by measurement:** an appended locator CANNOT override a key the shipped catalog already knows — `GetResourceLocations` unions locators and the shipped locator at index 0 wins. Repoint of an existing key is therefore refused by name, not faked | 2026-08-27 |
| ZW4 | **Sound replacement decodes from MEMORY** (888 ms) while the shipped `.wem` on disk is untouched (1200 ms). **WWISE LIFECYCLE SETTLED:** unloading replacement banks does NOT restore shipped audio — the media dies for the session (`NO-DURATION-CB`, event ends at 18 ms). The old `C-restore` gate arm asserted the opposite and was inverted (`cce6302`). Cross-mod ownership: one media = one owner, lowest mod id keeps it, later claimant refused BY NAME | 2026-08-27 |
| ZW5 | **Video: the engine's own decoder opened a replaced clip** (`campaign_intro`, 180 frames 1280x720) **and an added one** (`quit_outro`, 90 frames) directly from the mod folder | 2026-08-27 |
| ZW6 | **RESTART: all four routes come back by themselves** after a clean exit and relaunch with no mod-manager interaction. `c1242d4` startup pass; in the measured runs the `SetEnabled` postfix restored them. The startup ON-pass is the belt for a content mod that does not declare ContentTool as a dependency — that path is still unexercised | 2026-08-27 |
| ZW7 | **ORDERING** (`84bb9f4`): a content mod's own `OnModEnabled` used to run before its keys were published (`ModEntry.SetEnabled` calls `OnModEnabled()` inside its own body, `ModEntry.cs:198-220`). Fixed with a prefix. Proven: zero `ct_weapon FAIL` on cold launch, three weapons load with real geometry | 2026-08-27 |
| ZW8 | **FIRST-TIME SUBSCRIBER** (`4c661a5`) — the release blocker. `ModManager.EnableModsFromStore` enables the activated list (`:281-292`), recursively enables declared dependencies (`TryEnableMod:200-207`), then disables everything the array does not name (`:293-299`), and `TryDisableMod:233-240` cascade-disables dependents FIRST — so a subscriber's content mod silently reverted. Fixed by a veto on `TryDisableMod` for ContentTool's id only, while the player's own list still names an ENABLED mod that requires it; disarmed one frame after startup. PROVEN in game: packaged mod in `Mods\AresRifleRemodel`, `MOD_ACTIVATED` naming only PPBridge + the demo, content live post-startup (5554/1024 read twice, control launch 5771/2048), no cascade, and a deliberate player disable still works in both windows. The game re-saves `Options.jopt` without adding ContentTool, so the veto is re-derived every launch | 2026-08-27 |
| ZW9 | **DEVELOPER LIVE LOOP** (`b807f50`, S2 / RR parity): editing a PNG on disk updates the live game in ~3 s with no restart (64x64 -> 32x32 read back off the renderer); 6 writes in 0.5 s coalesce to one reapply taking the LAST write; `ct_dev next` switches variant sets (`select\<Set>\`); after a scene load a lost `name:` binding is re-resolved by the budgeted rescan; with dev mode OFF: `watchers=0 loop=off hotkey=off` and file changes do nothing | 2026-08-27 |
| ZW10 | **PACKAGING** (`f6e88da`, S3): `package.ps1` stages an allowlist and REFUSES to ship redistributed game data (`Patched\`, shipped bundle identities, `.ct-backup`/`.ct-edits`/`catalog.json`), deleting the staged folder rather than half-writing a release. A packaged mod installed from a zip under a different folder name served its content from the player's own 403 MB source bundle. Cache freshness key (`PatchCache.cs`): format version + manifest SHA1 + `Content\` sources + shipped source bundle identity | 2026-08-27 |

**Known limits (stated, not hidden, 2026-08-27):**
- A replacement bank cannot be taken back within a session.
- A mid-session disable removes the redirect but cannot un-swap already-resident assets (needs relaunch).
- One shipped bundle has exactly one owning mod.
- A published key cannot override a shipped key.
- The bake rewrites `Dist\<name>.bundle` INSIDE the installed package folder, so an installed package
  is not byte-identical to its zip (inside `Mods\`, so the zero-write guarantee is unaffected).

## NOT proven — the actual remaining frontier

`U3b`/`U3c` donor shader (now optional in the baked path — U3d gives the shader at bake time) ·
~~`U4` GameObject hierarchy~~ (closed above) · ~~`U5` SkinnedMeshRenderer~~ (closed above — bake
side; see its ceilings) · ~~`U6` AnimationClip + Mecanim~~ (closed above — the "highest-risk Unity
serialization item" turned out to be the cheapest, because an `AnimatorOverrideController` over a
shipped base controller replaces the whole `ControllerConstant`; see its ceilings) · ~~a **skinned
REPLACEMENT** (OBJ onto a
shipped rigged mesh, keeping the target's skeleton)~~ (closed above as U5b — one rigid influence per
vertex) · ~~SMOOTH weights from a skinned interchange format~~ (closed above as P6/R6 — a `.glb`'s own
`WEIGHTS_0` onto the shipped skeleton, bones matched by NAME; `.obj` and a foreign armature still fall
back to nearest-bone and say so in the bake log).
All of these are "how many fields", not "does it work".

**Open as of 2026-08-27, and each one is open for a different reason:**
- **`KHR_draco_mesh_compression` — IN FLIGHT.** 0 of the 63 real `.glb` files in U10's market survey
  use it, and it is refused BY NAME at both places it can appear, with the fix (import into Blender,
  export again with the Compression box unticked). Its decoder — Edgebreaker connectivity plus the
  attribute prediction schemes — is larger than the rest of the importer put together.
- **Quantized `TEXCOORD`s that need `KHR_texture_transform` to scale back** — the one dequantization
  route `KHR_mesh_quantization` names that this mod has nowhere to read, since paint travels through
  `Meshes\materials\<name>.mat.json` and no glTF material is consulted. Refused by name.
- **The startup ON-pass for a content mod that does NOT declare ContentTool as a dependency** is
  unexercised (ZW6's belt path).
- ~~**`AnimationClip` publish resolution** (`90a4280`) is proven offline against the game's own DLLs
  but not in a live session.~~ — **closed 2026-08-28**, gate row `C1-clip` above: the sample publishes
  two baked clips and the game's own Addressables hands them back as `AnimationClip`s reading
  `length=0.8333s` / `4.1667s`, `frameRate=24`, `empty=False`, against a same-session control where
  the key resolved to `null`/`Failed` before the publish.
- ~~**The offline suite still reports `SKIN-ABOVE VOID`** — `no demos\CustomCreature\Content\Models\spider.glb`~~
  — **closed (`c6268aa`)**. The arm now reads `lib\u8_probe.glb`, the file its oracle was measured on,
  and a missing fixture THROWS (`SKIN-ABOVE FAILURE: the fixture is gone`) instead of returning a green
  VOID, so a rename cannot switch the gate off in silence again.
- **Everything still filed PENDING-INGAME above** — `U8-grid`, `U8-step`, `U9-plan`, `U9-verdict`,
  `U12`, `U13`. Offline green is not this document's standard; a row leaves PENDING-INGAME when the
  game says so, and not before. `U10`, `U11` and `P4d` left this list on **2026-08-27**, closed
  by the eight-demo measurement run recorded in `VERIFIED-DEMOS.md` (commit `5d8fb39`). `U12` (loop
  flag) and `U13` (chosen clip) did NOT: that run reports which clips the animator played, not that a
  clip looped or that a declared `"play"` name was the one chosen, so neither has an in-game
  discriminator yet. **`Q1` did not leave either** — the run measured playback (`frameCount=90` off
  the game's own controller) but not WHO quit the game, because the unconditional watchdog
  (`QuitCutsceneMain.cs:270-291`) exits too. Missing measurement: a `Player.log` containing
  `Q1-exit the cutscene finished or was skipped; quitting for real now` and NOT
  `Q1-watchdog … are up`.
