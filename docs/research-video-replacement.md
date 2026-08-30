# Research — video replacement (spike, 2026-08-12)

**VERDICT: TRIVIAL. Videos are loose `.webm` files on disk plus a plain-JSON side catalog. No bundle,
no Addressables, no route vii. Replacement is a file copy + one JSON string edit — zero runtime code
by construction.** One exception (the boot logo reveal) is a real embedded `VideoClip`; it is not
worth touching and the example mod does not need it.

## 1. What ships, and where

- **69 loose `.webm` files**, all under
  `D:\Steam\steamapps\common\Phoenix Point\PhoenixPointWin64_Data\StreamingAssets\StreamableCopiedAssets\`.
  Total ≈ 1.8 GB. Sizes 10.2–60.8 MB each.
- Six folders — base `Videos\` plus `Videos_DLC1`..`Videos_DLC5` (one per DLC):

  | Folder | Contents (examples) |
  |---|---|
  | `Videos\` | `GameIntro.webm` (24.48 MB) |
  | `Videos\Factions\<Anu\|NewJericho\|Phoenix\|Synedrion>\` | `PP_Intro.webm` 49.09 MB, `PX_Ending.webm` 51.00 MB, `NJ_Ending.webm` 57.21 MB, `SYN_Intro.webm` 35.23 MB, `DA_Intro.webm` 25.66 MB |
  | `Videos\Factions\Phoenix\Symes\` | 9 research cinematics, e.g. `Pandoravirus_research_cinematic.webm` 26.73 MB |
  | `Videos\Tutorials\` | 14 clips incl. `TestTutorialVideo.webm` 10.81 MB, `tut_cin1.webm` 27.80 MB |
  | `Videos\VehicleLandings\` | 10 clips, day/night per aircraft — `RhinoLandingDay.webm` 20.69 MB, `IcarusLandingNight.webm` 10.52 MB |
  | `Videos_DLC1..5\` | `Blood&Titanium_Cutscene_1.webm` 60.84 MB, `LOA_Cutscene_5.webm` 56.69 MB, `Hypnos_Cutscene1.webm` 16.78 MB, `KS_Intro.webm` 45.94 MB |

- **Not in any bundle.** `StreamingAssets\aa\catalog.json` (1,670,855 B) has **0** occurrences of
  `webm` and **0** of `VideoClip`. Its 49 case-insensitive `video` hits are all
  `Assets/Art/VideoThumbnails/*.png` — Sprites for the cinematics library, not video data.
- **The one exception:** exactly 1 `VideoClip` asset exists in the whole install —
  `PhoenixPoint_LogoReveal_FootageMP4`, PathID 10, in `sharedassets3.assets`, 2,096,063 B
  (`extracted\GameData\inventory\assets.csv`). `sharedassets3.assets` is only 23,058 B while
  `sharedassets3.resource` is 2,751,492 B → the clip's payload is an **external** `StreamedResource`
  in the `.resource`, not embedded in the asset. This is the Snapshot logo at boot.

## 2. How they are played

- Streaming route (68 of 69 clips) — `Base.UI.VideoPlayback\VideoPlaybackController.cs:146-158`:
  ```
  VideoPlayer.renderMode = PlaybackSource.RenderSource;     // :148
  VideoPlayer.source     = VideoSource.Url;                 // :149  <-- URL, not clip
  VideoPlayer.url        = PlaybackSource.VideoClipSource.GetStreamingPath();  // :150
  VideoPlayer.Prepare();                                    // :155
  ```
- Path resolution is a plain string concat off a plain-JSON catalog:
  - `StreamableAssetReference.cs:13-26` — `GetStreamingPath()` → `StreamableAssetsManager.Instance.GetStreamingPath(this)`.
  - `StreamableAssetsManager.cs:47-51` — `return StreamingRoot + "/" + _catalog.GetAssetLocation(reference).StreamingPath;`
    with `StreamingRoot => Application.streamingAssetsPath` (`:13`).
  - `StreamableAssetsManager.cs:27-29` — `File.ReadAllText(StreamingCatalog)` +
    `JsonUtility.FromJson<StreamableAssetsCatalog>`, called from `Awake()` (`:53-56`).
    `StreamingCatalog` = `<StreamingAssets>/StreamableCopiedAssets/Catalog.json` (`:15`).
  - **No hash, no CRC, no signature on that catalog.** Read once, cached in a private field.
    `StreamableAssetsManager` is referenced from **no** C# call site — it is a scene-placed
    MonoBehaviour, Awake-driven.
- The catalog itself: `StreamingAssets\StreamableCopiedAssets\Catalog.json`, **16,880 B, 69 entries**:
  ```json
  { "Collection": "Videos_CopyFolderLocatorDef",
    "RuntimeKey": "cdd4584fdc6b7ad4992c6abf18e40d6e",
    "StreamingPath": "StreamableCopiedAssets/Videos/Factions/Anu/DA_Ending.webm" }
  ```
  `RuntimeKey` = the Unity asset GUID; `VideoPlaybackSourceDef.VideoClipSource` is a
  `StreamableVideoClipReference` holding only that string (`StreamableAssetReference.cs:9`).
- Boot-logo route (the 1 exception) — `PhoenixPoint.Common.Levels\IntroLevelController.cs:43,89,100`:
  `GetComponentInChildren<VideoPlayer>()` then reads `_videoPlayer.clip.width/height` and assigns a
  `RenderTexture`. It never sets `url` → this one plays the embedded `VideoClip`, resolved by the
  scene, not by the streamable catalog.
- Def surface (`Base.UI.VideoPlayback\VideoPlaybackSourceDef.cs`): video, audio and subtitles are
  **three separate assets** — `VideoClipSource` (`:32`), `VideoSoundDef AudioSource` (`:34`, Wwise),
  `TextAsset Subtitles` (`:36`). The `.webm` does carry a Vorbis track, but PP drives cutscene audio
  through Wwise (`VideoPlaybackController.cs:97-101`) — a replaced video does **not** bring its own
  soundtrack along that path.
- Consumers of the streaming route: `UIStateGeoCutscene.cs`, `UIStateTacticalCutscene.cs`,
  `UIStateHomeScreenCutscene.cs`, `UIStateCinematicsHome.cs`, `UIModuleCutscenesPlayer.cs`,
  `CinematicItemController.cs` — all through `_cutscenePlayer.VideoPlayer.PlaybackSource = <def>` +
  `.Setup()`.

## 3. Does route vii apply? — NO, and that is the whole finding

- Route vii (patched bundle copy + `aa\catalog.json` repoint) exists because textures/meshes/materials
  are **inside** bundles. Videos are not in a bundle at all. **Do not build any bundle machinery for
  video.**
- Replacement is one of two file-level moves, both already zero-runtime (the game reads them off disk
  before any mod code exists):
  - **Repoint (preferred, additive):** drop the mod's clip at
    `StreamingAssets\StreamableCopiedAssets\Videos\<modid>\<name>.webm` and **mutate the existing
    row's `StreamingPath`** in `Catalog.json` to that relative path. No PP asset is overwritten, no
    PP asset is redistributed, and the mod ships only its own clip. Directly analogous to route vii's
    catalog repoint — same backup/edits-ledger discipline applies
    (`Catalog.json.ct-backup` pristine + `Catalog.json.ct-edits` one line per mod, rebuild from
    pristine on every write).
  - **Overwrite in place:** replace the `.webm` byte-for-byte. Simpler, but destroys the player's
    file and needs its own backup. Steam file-validation restores it. Only worth it if the repoint
    turns out to be blocked, which nothing measured suggests.
- **HARD GOTCHA — duplicate keys crash the game.** `StreamableAssetsCatalog.cs:22` is
  `AllLocations.ToDictionary(l => l.RuntimeKey)`. `ToDictionary` throws `ArgumentException` on a
  duplicate key, inside `Awake` → a mod that *appends* a row reusing an existing `RuntimeKey` breaks
  the boot scene. Rule: **replace ⇒ mutate the existing row in place; add ⇒ append with a genuinely
  new GUID.** A validator that provably rejects a collision is mandatory (same shape as A4).
- **Adding a new video** (the example mod's "video after new game, before the level loads") is the
  same one-line JSON append plus a `VideoPlaybackSourceDef` — and defs are the mod author's job, not
  ContentTool's. ContentTool's remit ends at the clip + the catalog row.
- **Path constraint — NOT measured:** `GetStreamingPath` is a raw concat, so a `..`-escaping
  `StreamingPath` pointing at the mod folder is *plausible* but untested. It is also unnecessary:
  writing the clip **inside** `StreamingAssets\StreamableCopiedAssets\` needs no escaping at all and
  is what the preferred route above does. Do not ship a `..` path without a run that proves it.
- **~~Dev-workbench seam: a reflection poke on `_dictionary[key].StreamingPath`~~ — WRONG, CORRECTED
  2026-08-13.** `StreamableAssetLocation` is a **`struct`** (`StreamableAssetLocation.cs:6`), so
  `_dictionary[key]` returns a **copy** and the write is discarded. Left uncorrected this would have
  sent the next person down a dead end.
  **What actually works — and it is the SHIPPING route now, not a dev seam:** the catalog is barely
  private. `StreamableAssetsCatalog.AllLocations` is a **public field** and `InitializeCache()` is a
  **public method** that rebuilds the lookup from it; only `StreamableAssetsManager._catalog` needs
  reflection. Replace = mutate the **array element** `AllLocations[i]`, add = append, then
  `InitializeCache()`. The manager is scene-placed (`Awake -> Initialize`, `OnDestroy ->
  Uninitialize`) and `Initialize` re-reads the file every time, so **one Harmony postfix on
  `Initialize`** re-injects across every scene load — no `Uninitialize()+Initialize()` dance.
  Implementation: `src\Bake\CatalogLive.cs`. It modifies **nothing in the install** (no
  `Catalog.json` edit, no `.ct-backup`, no edits ledger, no revert), and it is *safer* than the file
  route: `ToDictionary` still throws on a duplicate `RuntimeKey`, but inside **our** call instead of
  the game's `Awake`, so a bad key can no longer kill the boot scene.
  Still UNMEASURED: `GetStreamingPath` is `StreamingRoot + "/" + StreamingPath`, so a mod-folder file
  needs a `..`-escaping relative path. If the engine refuses it, the fallback is a postfix on
  `GetStreamingPath` returning an absolute path for our keys — which still writes nothing.

## 4. Codec / container (measured, not assumed)

- `GameIntro.webm` header: magic `1A-45-DF-A3` (EBML/Matroska), doctype `webm`.
- Codec strings present in the first 2 KB: **`V_VP8`** and **`A_VORBIS`**.
- Muxer strings: `Unity VP8VideoMedia 2019.4.31f1 (bd5abf232a62)`, `vp8 v1.3.0`,
  `Xiph.Org libVorbis I 20101101`. → the shipped `.webm` were produced by **Unity's own VideoClip
  transcoder** (engine 2019.4.31f1) and copied out of the project at build time — which is exactly
  what the folder name `StreamableCopiedAssets` means.
- Constraint for an author: match this — **WebM / VP8 video / Vorbis audio**. VP9 and H.264-in-mp4
  are what Unity's `VideoPlayer` *usually* also accepts on Windows Media Foundation, but that is a
  remembered default, not a measurement, and this project has been burned by those three times.
  Ship VP8/Vorbis WebM until a run proves otherwise.
- Q4's `m_ExternalResources`/`m_OriginalPath` question applies only to the single boot-logo
  `VideoClip`, and the size split (23,058 B asset file vs 2,751,492 B `.resource` holding a
  2,096,063 B clip) already answers it: **external**, in `sharedassets3.resource`. The
  d4e1814 mounted-CAB constraint has no analogue here — the streaming route resolves an OS file path,
  not a PPtr.

## 5. Smallest proof-of-concept (spec only — NOT built)

Gate **`V1`**, one arm, falsifiable, in the house style:

- **Setup (install-time, on disk, no runtime code):** back up `Catalog.json` → `.ct-backup`. Pick
  `TestTutorialVideo.webm` (10.81 MB, `RuntimeKey` read from the catalog — smallest clip, and a
  tutorial clip nobody's save depends on). Encode a **6-second, visually unmistakable** VP8/Vorbis
  WebM at the same resolution, write it to
  `StreamingAssets\StreamableCopiedAssets\Videos\ct_v1\v1_probe.webm`, and **mutate that row's
  `StreamingPath`** to it. Restart.
- **Positive-identity oracle (must not be fakeable — non-null is not proof):** in-game, read back
  `VideoPlaybackController.VideoPlayer.url` **and** `.frameCount` / `.width` / `.height` after
  `Prepare()` completes, and assert the url **ends with `ct_v1/v1_probe.webm`** AND `frameCount`
  equals the probe's own frame count and **differs from** the original clip's. Two independent
  identities — the path (proves the catalog edit landed) and the frame count (proves the *decoder*
  actually opened our file, not merely that a string was assigned). Print both numbers.
- **Control in the same run:** a second, untouched row (e.g. `Tutorial_1.webm`) resolved the same
  way, asserting its url still ends with the shipped path and its `frameCount` is the original's.
  Catches a global-clobber false pass.
- **Falsification:** revert the row from `.ct-backup`, rerun — V1 must go RED on both identities.
  A second falsification worth having: point `StreamingPath` at a nonexistent file and assert the
  gate says **VOID/FAIL**, never PASS (`VideoPlayer.Prepare()` on a missing url fails silently in the
  `errorReceived` channel — a gate that only checks the url string would pass and prove nothing).
- **Duplicate-key control (cheap, offline, no restart):** feed the writer a row whose `RuntimeKey`
  already exists and assert it **refuses**. `ToDictionary` at `StreamableAssetsCatalog.cs:22` would
  otherwise throw inside `Awake` and brick the boot scene — this is the A4-shaped
  must-provably-reject arm.
- **Cost:** no bundle work, no baker, no new `src\` mechanism — a JSON read/mutate/write plus the
  existing backup+edits-ledger pattern. If V1 passes, video replacement is done; there is no U-series
  layout work to do, because there is no serialized asset to lay out.

## Bottom line for the plan

Video is **not** a route-vii kind and must not be planned as one. It is the cheapest content kind in
the whole remit: copy a file, mutate one JSON string, restart. The only real engineering is the
duplicate-`RuntimeKey` validator and the shared backup/edits ledger — both of which already exist for
`aa\catalog.json` and should be reused rather than rebuilt.
