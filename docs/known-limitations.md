# Known limitations

- **New sounds need the mod’s own loader.** ContentTool bakes and checks sounds placed in `Content\Audio`, but it does not load their new bank when a player enables the mod. Without a DLL that loads the bank, those sounds are silent. [AddUiSounds](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/AddUiSounds) includes such a DLL. Replacing shipped sounds through `Content\Audio\Replace` and `Dist\Sounds` does not need one. See [add sounds](recipes/sounds-add.md) and [replace sounds](recipes/sounds-replace.md).

- **The extracted files for streamed new sounds do not ship in a package.** A bake puts stream data in the mod’s bundle and extracts it into that mod’s `WwiseAudio` folder for its check. The packager excludes `WwiseAudio`. Player-side playback of a packaged streamed addition has not yet been verified in game.

- **Normal-map shading on replaced meshes needs an in-game check.** The mesh writer emits a tangent channel, taken from the `.glb` when usable and otherwise computed from the UVs, but normal-map lighting on a replaced mesh has not yet been verified in game. See [replace meshes](recipes/meshes.md).

- **Emission colour needs an in-game check.** The importer sRGB-encodes `_EmissionColor` like `_Color` and enables `_EMISSION`, but the result on replaced materials has not yet been verified in the game’s Linear colour space.

- **One mod owns each contested target.** For a shipped bundle or video key, the lower mod ID keeps the target. For a shipped sound media ID, replacement banks load in mod-ID order; a later mod that claims an already-owned ID is refused. Give conflicting mods different targets or use only one of them.

- **New weapons require a DLL.** A `weapons` declaration does not build weapon definitions on its own. The mod must call `WeaponBuild.Build`, as [WeaponAdd](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/WeaponAdd) does. See [add a weapon](recipes/weapon-add.md).

- **Project IDs and bundle names cannot use Windows device names.** Names such as `CON`, `NUL`, and `COM1` are refused, including when the device name appears before an extension.

- **Non-ASCII bone names are not yet verified in game.** Bone-path hashing reads each character as a byte. Use tested bone names for a rig you plan to distribute.

Some runtime edge cases still need an in-game run: loading a tactical save with a creature’s synthesised melee bash-point skin; interactions with donor animation actions; recovery after a mod DLL fails while enabling; bench reopening during a rebuild; and creature or weapon builds after a failure. A successful offline build or bake does not establish those outcomes.
