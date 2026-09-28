# What’s new

## 1.3.0 (in preparation)

### New

- A redesigned [bench](bench/index.md): a home screen with six task cards, `Home` / `< Back` / breadcrumb navigation (`Esc` goes back), clickable completed steps, a right-hand pane that fits the task (the 3D view only on model and weapon screens), and selectable logs with `Copy log` and `Open log folder`. See [Moving around and reading logs](bench/navigation.md).
- [Sounds](bench/sounds.md) screen: search the game’s sounds, preview them and your own file (built or not), then `Add to mod & build`. A player shows progress and has its own volume.
- [Videos](bench/videos.md) screen: preview game clips and your own in a player with pause, stop and seek, then replace a game video or add a new one.
- [Add a weapon](bench/add-weapon.md) screen: pick the weapon kind and the shipped weapon it copies, your `.glb` and a name, `Create weapon`, build, put it in the hand, fit it with move / turn / size sliders, `Save fit`, and check the kind’s moves on a filtered clip strip.
- A content mod without a DLL builds its `weapons` rows when it is enabled.
- `ct_voices` and `ct_mission list` are available as public console commands.
- A video replacement can use the path printed by `ct_list videos`, including a whole-path tail that identifies one clip.
- `meta.json` accepts comments and trailing commas, as the game’s reader does.

### Changed behaviour

- `Replace a model` builds on a `WARN` verdict too, including nearest-bone binding; only `FAIL` blocks `Build mod & apply`.
- `Build & share` shows the log beside the steps, runs a single stage from `Details - run one step`, and marks a result with `*` only when the mod’s project key changed since that step ran.
- Faster loading with many mods: patched copies are repacked with LZ4 fast compression instead of LZ4HC; ticking a mod on in the mod manager runs a patch-only bake (its replacement rows only, no read-back, no rebuild of its own bundle); a bake that failed is remembered and not repeated at every launch (the log says `NOT RE-BAKED`); a disabled mod keeps its patched copies. A stale weapon-mesh bake measured 9089 ms, and 118 ms once warm.
- `ct_voices watch` refuses an argument that is not a number.
- When mods claim the same shipped bundle or video key, the lower mod ID keeps it. Replacement sound banks also load in mod-ID order, and a later claim to the same media ID is refused.
- `ct_extract audio --all` decodes in the background. A second bulk decode started while the first is running is refused; run it again after the first finishes.
- A root `id` or `bundle` that is a Windows reserved device name, such as `CON`, `NUL` or `COM1`, is refused.
- The Lifecycle log retains stage output when a run stops and appends its final line.
- A new-sound bake extracts streamed media into that project’s own `WwiseAudio` folder.

### Fixed

- Invalid `ppcontent.json` and missing or ambiguous video sources produce counted project failures instead of ending the whole bake with an exception.
- Misspelled texture, mesh, audio, and video names produce `ct_extract` refusals instead of stack traces.
- Packaging refuses a video row whose clip is missing or ambiguous. It also checks sound replacement sources against their baked banks; when only file dates suggest a change and no ledger entry exists, it warns.
- Project validation checks the placement of video, sound, and published-model sources.
- Several model import cases now preserve material assignments, animation data, and source geometry more reliably.
- Replaced meshes carry a tangent channel, so the donor material’s normal map can shade them.
- `_EmissionColor` is sRGB-encoded like `_Color` instead of being written raw.
- When a content mod’s own DLL fails while enabling, content it had already published is taken back.
- A creature build no longer writes into the shipped animation action definitions it borrows from its donor.
- A mod ID with capital letters (for example one made by the bench, such as `CtWpnTest`) no longer gets its published model keys refused.
- A second weapon that clones the same shipped weapon as another one no longer fails to load its model.
- A bad material path in a seam swap is reported as a failure.
- Reapplying a patched bundle that the game already loaded through ContentTool’s redirect keeps the working redirect instead of demanding a restart.

For earlier versions, see the [GitHub releases](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/releases).
