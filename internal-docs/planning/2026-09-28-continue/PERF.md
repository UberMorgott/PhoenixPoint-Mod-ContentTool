# PERF — why many content mods load slowly, and the plan

Track PERF of `TASKS.md`. Static analysis + offline micro-benchmark, 2026-09-28. No game run yet (the
game is owned by another track); every in-game number below is marked **UNMEASURED** and has a
measurement recipe in §5.

## 1. Load path map (what runs, when, on which thread)

All of it runs on the **Unity main thread, synchronously**. There is no worker, no coroutine split and
no progress UI on the player's path.

| # | When | Entry (file:line) | Per mod | Work |
|---|---|---|---|---|
| A | mod-manager startup pass, ContentTool enabled | `ContentToolMain.OnModEnabled` `src/ContentToolMain.cs:103` | once | console table, Harmony patches (`ModRoster.Install` `src/Project/ModRoster.cs:66`), BuildStamp = SHA-1 of our own DLL (`:233`) |
| B | startup pass, EVERY later mod's `ModEntry.SetEnabled` | `ModRoster.BeforeSetEnabled` `src/Project/ModRoster.cs:233` (Harmony prefix) → `Route7.Toggle` `src/Bake/Route7.cs:179` | per content mod with `replace`/`publish` | **route vii apply** (below) + **route iii** `CatalogApply` `Route7.cs:624` |
| C | same call, postfix | `ModRoster.AfterSetEnabled` `ModRoster.cs:262` | per content mod | `SoundLoad.LoadMod` (banks), `VideoCatalog.LiveMod`, `Route7.Toggle` again (no-op if B held) |
| D | ONE frame after A | `ContentToolMain.LoadContent` `ContentToolMain.cs:151` | all mods | `VideoCatalog.LiveAll` → `SoundLoad.LoadAll` → `PatchCache.Prune` → `ModRoster.Reconcile` (Toggle for every enabled + every disabled mod) → `CreatureBuild.BuildAll` |
| E | whenever a mod's own DLL runs | `WeaponBuild` / `CreatureBuild.Build` | per mod | `AssetBundle.LoadFromFile` + `LoadAllAssets<AnimationClip>` + `GetAllDefs<TacCharacterDef>()` linear scan (`src/Tactical/CreatureBuild.cs:235-303`) |

### Route vii apply (`Route7.Applied` `Route7.cs:454`) — the heavy one

1. `Observe` `Route7.cs:141` → `PatchCache.Key` `src/Project/PatchCache.cs:49`: SHA-1 of ppcontent.json,
   stat of every file under `Content\`, stat of every shipped target bundle. Cheap (ms).
2. **Fresh** (`ct-cache.key` matches) → straight to `BundleLive.Install` (redirect + CRC zero). Per bundle
   `BundleLive.Locate` walks every key of every Addressables locator (`src/Bake/BundleLive.cs:242`
   "ponytail"). **UNMEASURED**.
3. **Stale or absent** → `ProjectBake.Bake(root, claimHeld:true)` `src/Bake/ProjectBake.cs:104`, i.e. the
   **author's whole `ct_project`**, on the player's machine, inside the mod manager's enable call:
   - `ContentProject.Load` `src/Project/ContentProject.cs:375`: decode every PNG (`Texture2D.LoadImage`,
     `:720`), parse every .glb + `ModelBuild` (`:758`, `:786`), decode every .ogg/.mp3/.wav and encode a
     WEM (`:855-863`).
   - `Patch` `ProjectBake.cs:1721`: per target bundle, `new BundleBaker(shipped)` + row edits +
     `baker.Write` (`src/Bake/BundleBaker.cs:1100-1125`): serialize the WHOLE bundle into a
     `MemoryStream`, re-read it, **repack with the source's compression = LZ4HC**.
   - `ReadBack.Run` `src/Bake/ReadBack.cs:196`: per replacement row, 2–7 fresh AssetsTools opens of the
     copy AND the shipped file, each `GetBaseField` over every asset of that class
     (`BundleBaker.cs:942-1054`).
   - then, ONLY when the project has any texture/audio/model source (`ProjectBake.cs:279-294` returns
     before it otherwise - replace-only material/clip projects stop after Patch), the mod's OWN bundle
     is rebuilt into `Dist\` (`ProjectBake.cs:299-510`). NB a texture used only by a replace row still
     counts (it lives in `Content\Textures\`), so WeaponMesh-shaped mods DO pay this - written into the mod
     folder, i.e. into `steamapps\workshop\content\...` for a Workshop mod - and read back through Unity:
     `LoadFromFile`, rig instantiate/deform gates, full-pixel texture compare, Wwise bank load/unload
     (`:543-598`).

   Stale happens on: first ever enable; any game update (shipped bundle mtime/size); any mod update;
   a ContentTool `FormatVersion` bump (all mods at once); a mod switched OFF and back ON (the startup
   `Prune` deleted its copies, `PatchCache.cs:138-140`); and **every launch for a mod whose patch bake
   failed** — `Failed` is session-only (`Route7.cs:105`) and no receipt is written, so it re-bakes and
   fails again each start.

## 2. Measured (offline, same vendored AssetsTools.NET + classdata.tpk, Release x64)

Bench: `scratchpad\perfbench` (not committed) mirrors `BundleBaker` ctor+`Write` and `BundleBaker.Read<T>`.
Bundles from `D:\PP-Instance2`, dev machine, warm file cache, single run each (open arms repeated twice, within 10%).

**Patched-copy write (per target bundle, no rows mutated — row edits are tiny next to this):**

| shipped bundle | size | layout | open | serialize | **repack LZ4HC (current)** | total | peak RAM |
|---|---|---|---|---|---|---|---|
| aln_acidworm | 4.8 MB | LZ4HC | 125 ms | 108 ms | 455 ms | **0.7 s** | 74 MB |
| aln_crabman | 242 MB | CAB 4 MB + .resS 390 MB | 107 ms | 1.05 s | **15.5 s** | **16.6 s** | 915 MB |
| px_equipment (WeaponMesh demo) | 403 MB | CAB 10 MB + .resS 634 MB | 114 ms | 1.8 s | **25.8 s** | **27.8 s** | 1.7 GB |
| kaos_content | 809 MB | CAB 59 MB + .resS 1530 MB | — | — | not run; linear extrapolation ~60 s | — | est. ~4 GB |

Same bake, other compression (measured):

| bundle | LZ4Fast total / out size | None total / out size |
|---|---|---|
| aln_crabman | 2.8 s / 268 MB (+11%) | 1.2 s / 395 MB (+63%) |
| px_equipment | 5.4 s / 447 MB (+11%) | 2.5 s / 645 MB (+60%) |

File copy of px_equipment: 108 ms (the floor).

**Read-back arms** (one `BundleBaker.Read<T>` = open + tpk + class DB + scan one class): open 108-122 ms
regardless of bundle size (LZ4 blocks decompress lazily); Mesh scan of px_equipment (57 meshes) 272 ms,
Texture2D 168 ms, Material 139 ms. A WeaponMesh-shaped mod (1 mesh + 5 textures on one bundle) costs
~12 arms ≈ **2-3 s**. `classdata.tpk` load alone is 25-37 ms and is repeated for every arm.

**Author sources** (in `ContentProject.Load`): .glb read+ModelBuild: rifle 8 ms, ar181 (8 MB) 4 ms,
cyborg_spider 13 ms, **soldier.glb (35 MB, 300 clips) 706 ms**. Audio decode+WEM: 12 s mp3 100 ms,
6 s mp3 24 ms. PNG decode is Unity-only — **UNMEASURED**.

## 3. Ranked hotspots

1. **LZ4HC repack of whole shipped bundles on the player's enable path** — 15-28 s per 250-400 MB
   target, ~60 s for kaos_content, 1-4 GB RAM, main thread. >95% of the bytes are the untouched `.resS`
   (measured layouts above). Scales with (mods × distinct target bundles). Two mods targeting the same
   bundle each pay it, and only one wins the claim.
2. **The whole author bake re-runs on the player's machine**: own-bundle rebuild into Dist + Unity
   read-back + source re-import (0.7 s per big .glb, PNG decode unmeasured) — none of which route vii
   needs; the shipped `Dist\*.bundle` is already the mod's own bundle.
3. **Stale triggers that should not re-bake**: permanent re-bake of a failing mod every launch; Prune
   deleting a merely-disabled mod's copies; one FormatVersion bump rebuilding every mod at once;
   mtime-only changes (Workshop re-download) invalidating.
4. **Read-back gates on the player path** (~2-3 s per bundle-mod): an author proof, re-run for players.
5. **Per-launch fresh path, UNMEASURED but suspect**: `BundleLive.Locate` full-catalog walk per bundle
   per enable; `SoundLoad` reading every bank whole (`File.ReadAllBytes`, up to 24 MB each) + Wwise
   `LoadBankMemoryCopy` on main thread; `CreatureBuild` `LoadAllAssets<AnimationClip>`; `CatalogApply`
   `LoadFromFile`+`GetAllAssetNames`+`Unload(true)` of the mod bundle per publish mod.
6. Small: `ContentMods.Enabled` re-discovered 5x per startup (`LiveProjectIds`, `Reconcile`,
   `SoundLoad.LoadAll`, `VideoCatalog.LiveAll`, `CreatureBuild.BuildAll`), `SoundLoad` sort re-reads and
   re-parses every manifest per comparison (`src/Bake/SoundLoad.cs:54,71-84`, O(n log n) file reads).

## 4. Recommended design (ordered by win / risk)

1. **Split the player path from the author path.** Route vii on enable runs `PatchOnly(project)`:
   import only the sources the `replace` rows name, run `Patch`, write the receipt — no own-bundle
   rebuild, no Unity read-back, no audio/model import of unrelated sources. Author verbs (`ct_project`,
   dashboard Bake/Verify) keep the full gates. Expected: removes hotspot 2 and 4 entirely.
2. **Stop recompressing the untouched `.resS`.** Option a (cheap, now): repack patched copies with
   `LZ4Fast` instead of the source's LZ4HC — measured 5.2x faster (27.8 → 5.4 s), +11% disk.
   Option b (best, needs an in-game load proof): block-reuse writer — copy the original compressed
   blocks verbatim and append only the re-serialized CAB as new blocks, pointing directory entry 0 at
   it. Expected ~1-2 s for px_equipment, RAM ~CAB size. Option c: `None` (2.5 s, +60% disk).
3. **Receipts that do not rot:** write a NEGATIVE receipt (same key + "failed") so a broken mod is not
   re-baked every launch; keep disabled mods' copies (Prune only mods no longer installed, or by age);
   key sources by content hash (cheap for small files, size+mtime fallback for shipped bundles);
   bump FormatVersion per route, not globally.
4. **Get it off the enable call.** Stale copies bake on a worker (AssetsTools + System.IO are
   UnityEngine-free — `Observe(string…)` overload already exists for this) behind a one-time
   "ContentTool is preparing N mods" notice; the redirect installs when the copy is published.
   Addressables resolves the path at LOAD time, so installing before first use of that bundle is
   enough — needs the residency check (`BundleLive.ResidentNow`) to stay authoritative.
5. **Dedup per target bundle**: bake only for the claim winner (`BundleClaims.Keeps`, lowest mod id);
   or later, one merged copy per shipped bundle carrying every enabled mod's rows.
6. Fresh-path hygiene: index the Addressables locations by bundle file once per session; discover the
   enabled roster once per startup and pass it down; cache manifest ids for the sound sort; load banks
   by path (`AkSoundEngine.LoadBank(string)`) instead of read-all+memory-copy if Wwise accepts
   generated banks from a path (UNMEASURED).
7. Alternatives rejected for now: shipping prebuilt patched bundles (redistributes game data — forbidden
   by the packager rule); Unity AssetBundle prebuild at package time (same problem for route vii; the
   own bundle is ALREADY prebuilt); parallel LZ4HC across bundles (still minutes of CPU, RAM x N).

## 4a. Codex review (agent-link, 2026-09-28) — folded in

- Path confirmed (`ModRoster.cs:233-244` → `Route7.cs:211-222` → `Applied:467-503` → `ProjectBake.cs:104-125`
  → `Patch:1721-1924` → `BundleBaker.cs:1093-1125`); fresh copies skip Bake.
- Correction taken: own-bundle rebuild is conditional (see §1). Read-back "2-3 s" is per-arm arithmetic,
  not a whole-route timing — measure arm count + total in game.
- PatchOnly: sound if it imports only the row-referenced sources and keeps row-refusal + receipt rules
  (`ProjectBake.cs:247-270, 1944-1954`).
- Block reuse: plausible, unproven. Must rebuild block-info/header (counts, sizes, per-block codec flags,
  v>=7 alignment, total size), keep old blocks' flags (mixed LZ4/raw legal), point dir[0] at the old
  uncompressed length, never copy a stale hash; verify Unity accepts out-of-order directory offsets.
  Output = source + new CAB (old CAB stays as dead bytes). Proof = AssetsTools reopen with .resS
  byte-identical, then in-game `LoadFromFile` + patched asset + streamed mesh/texture in a fresh process.
- Negative receipt only for deterministic source/row failures tied to the exact key, visible + explicit
  retry; never for I/O, contention or residency refusals (`Route7.cs:498-506`).
- Dedup-before-bake: the winner is decided AFTER the bake today (`BundleLive.Register:103-117`);
  preselection must keep lowest-id ownership, toggle order, residency and failed-winner fallback.
- Worker bake later: `ContentProject.Load` touches Unity (LoadImage); install + residency stay main
  thread; a copy finished after its bundle loaded keeps restart-required semantics.
- Order: instrument first → LZ4Fast → PatchOnly → negative receipts / keep disabled copies → block reuse
  if large bundles still dominate. `None` less attractive (+60% disk).
- Falsifier: stage wall time + allocations and time to first playable frame, cold/fresh/stale ×
  small/large × replace-only/mixed × 1/5/20 mods. If the fresh path (Locate/SoundLoad/CreatureBuild)
  dominates, re-rank; if LZ4Fast cuts bake time but not playable latency, it is not the user's bottleneck.
  `connect state` alone is an incomplete endpoint.

## 5. What needs in-game measurement, and how (PPCLI, Instance2 only)

Instrument first: a `ct_perf` Stopwatch line per stage and per mod (`LoadContent` stages, each
`Route7.Toggle`, `Observe`, `Bake`, `Patch` per bundle, `ReadBack.Run`, own-bundle rebuild,
`BundleLive.Install`/`Locate`, `CatalogApply`, `SoundLoad.LoadMod` per bank, `CreatureBuild.Build`),
written to Player.log. Then:

1. **Cold start, fresh cache** — `ppcli.ps1 run` with N content mods enabled; time to `connect state`
   answering, minus a no-mods baseline; grep `ct_perf`.
2. **Cold start, stale cache** — delete `...\LocalLow\...\ContentTool\Patched\<tag>` first; same run.
   Confirms hotspot 1+2 in the real process (Unity main thread, not the bench).
3. **Toggle cost** — `connect call {"op":"invoke","type":"Morgott.ContentTool.Bake.Route7","member":"ApplyProject","args":["WeaponMesh"]}`
   with a stale and a fresh cache; PPCLI reply latency = the checkbox's cost.
4. **Scale** — duplicate a demo under N ids (5/10/20) targeting small vs large bundles; plot startup.
5. Unmeasured items to settle: PNG decode (`LoadImage`) per MB, `BundleLive.Locate` per bundle,
   Wwise `LoadBankMemoryCopy` per MB, `LoadAllAssets<AnimationClip>` for soldier (300 clips), Unity
   `LoadFromFile` of a block-reuse copy (design 2b acceptance).
