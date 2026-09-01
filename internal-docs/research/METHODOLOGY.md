# METHODOLOGY — test discipline that already prevented false conclusions

> Every rule below exists because breaking it produced a WRONG verdict that cost real sessions.
> They are mandatory for any spike, regression test, or in-game measurement in this project.

## General

- **Always take a CONTROL measurement inside the SAME run**, with the feature under test
  disabled. Never compare only against an expectation from another process/session.
  - Cost: "mesh does not survive repack" was WRONG — the test bundle
    (`aln_poisonworm_assets_all.bundle`) contains zero Mesh objects.
- **Assert independent things independently.** Extraction byte-equality must be asserted
  SEPARATELY from the engine/Wwise result, so an extraction bug can never read as an engine
  failure (and vice versa).
- **Delete any pre-existing target file before the run, and LOG the deletion**, so a stale
  artifact from an earlier run cannot masquerade as success. Same idea for bundles: rename the
  bundle internally (`m_Name` + CAB entry) and write to a fresh path, so an already-loaded
  vanilla bundle cannot masquerade as a successful test.
- Every critical bake test must LOAD the freshly written file again in the same run.

## Unity / bundles

- **Before using a game bundle as a Mesh repack subject, verify `m_Mesh` `fileID == 0`.**
  A `fileID = 1` PPtr points into an external bundle nobody loaded. Materials in the same
  subject were `fileID = 0`, which is exactly why they resolved and the mesh did not — that
  asymmetry produced the false failure.
- **`AssetBundle.GetAllAssetNames` / `LoadAllAssets<T>` return TOP-LEVEL assets only.** They do
  not prove the absence of nested dependencies. A "Mesh objects in bundle" counter built from
  `LoadAllAssets<Object>` is MEANINGLESS (observed live: `meshes=1 (Mesh objects in bundle=0)`).
  The trustworthy signal is a per-renderer `sharedMesh` line from walking the prefab graph.
- Shipped game textures may not be CPU-readable — measure via `RenderTexture` + `Graphics.Blit`
  + `ReadPixels`. Textures we author are readable with `GetPixel`.

## Audio

- **Never judge audio by ear.** Two conclusions were false because of it. Use an objective
  instrument (`AK_EndOfEvent | AK_Duration` → `fDuration`, `mediaID`, `bStreaming`).
- A **non-zero `playingID` proves nothing** about audibility. A **zero** `playingID` usually
  means the Event did not exist, not that the API under test failed.
- **Never `Thread.Sleep` on Unity's main thread.** It stalls the frame loop, so
  `AkSoundEngineController.LateUpdate` never runs, `AkCallbackManager.PostCallbacks()` never
  dispatches, and queued `PostEvent`s flush together on wake — two identical tones then overlap
  and comb-filter, which sounds like a pitch change. Pump `AkSoundEngine.RenderAudio()` +
  `AkCallbackManager.PostCallbacks()` instead.
- **Always log the `LoadBank` result.** An early spike ran from the MAIN MENU where
  `UIGeoscape.bnk` is not resident, so the target event did not exist and every reading was
  void.
- For every critical audio test, log: function result (`AKRESULT`), event ID, media ID, bank ID,
  duration, FILE vs MEMORY, streaming state, EndOfEvent timing, bank load result, bank unload
  result.
- A successful `UnsetMedia` is NOT permission to free: the native call removes the slot by
  matching the `pMediaMemory` VALUE, has no is-it-playing check, and returns `AK_Success`
  unconditionally.

## Reporting

- Cite the measurement, not the intention: DLL size / commit / timestamp / the exact log line.
  Every fact in `PROVEN-FOUNDATIONS.md` carries its evidence line for this reason.
- Offline/unit green is not done. A Harmony/reflection/engine-facing change is confirmed only by
  an in-game run.
