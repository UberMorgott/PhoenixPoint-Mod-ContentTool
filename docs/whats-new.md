# What’s new

## 1.3.0 (in preparation)

### New

- `ct_voices` and `ct_mission list` are available as public console commands.
- A video replacement can use the path printed by `ct_list videos`, including a whole-path tail that identifies one clip.
- `meta.json` accepts comments and trailing commas, as the game’s reader does.

### Changed behaviour

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
- Reapplying a patched bundle that the game already loaded through ContentTool’s redirect keeps the working redirect instead of demanding a restart.

For earlier versions, see the [GitHub releases](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/releases).
