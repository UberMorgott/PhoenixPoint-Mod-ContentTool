# Worked examples

These 11 public demos are in the repository’s `demos` folder. Choose one close to your goal, then use its README alongside the linked recipe. `NoDepTexture` is a test fixture under `tests/fixtures`, not a demo.

| Demo | What it shows | Route | Recipe link | Needs DLL | GitHub link to demo folder |
|---|---|---|---|---|---|
| MaterialTweak | Change a value on a shipped material. | Replace | [Material properties](../recipes/material-properties.md) | No | [Open demo](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/MaterialTweak) |
| WeaponMesh | Replace a shipped rifle’s mesh and five textures; its DLL changes the inventory icon. | Replace | [Replace weapon art](../recipes/weapon-replace.md) | Yes | [Open demo](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/WeaponMesh) |
| WeaponAdd | Publish three models and build three new weapons from shipped definitions. | Add + Build | [Add a weapon](../recipes/weapon-add.md) | Yes | [Open demo](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/WeaponAdd) |
| ReplaceUiSounds | Replace three shipped UI sounds. | Replace | [Replace sounds](../recipes/sounds-replace.md) | No | [Open demo](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/ReplaceUiSounds) |
| MenuMusic | Replace two shipped menu music tracks by media ID. | Replace | [Replace sounds](../recipes/sounds-replace.md) | No | [Open demo](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/MenuMusic) |
| AddUiSounds | Bake two new sounds; its DLL loads their bank and plays them on a hotkey. | Add | [Add sounds](../recipes/sounds-add.md) | Yes | [Open demo](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/AddUiSounds) |
| IntroVideo | Replace an intro video, its sound, and its subtitles. | Replace | [Videos](../recipes/videos.md) | Yes | [Open demo](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/IntroVideo) |
| QuitCutscene | Add a video key; its DLL plays the clip when leaving the main menu. | Add | [Videos](../recipes/videos.md) | Yes | [Open demo](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/QuitCutscene) |
| CustomCreature | Build a new creature from a model, rig, clips, and a shipped donor. | Build | [Creatures](../recipes/creature.md) | Yes | [Open demo](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/CustomCreature) |
| HumanoidSoldier | Build a playable soldier with a retargeted model and game clips. | Build | [Humanoid soldiers](../recipes/humanoid-soldier.md) | No | [Open demo](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/HumanoidSoldier) |
| ReplaceCharacterBody | Build a new body for an existing DLC character while keeping her identity. | Build | [Replace a character body](../recipes/replace-character-body.md) | No | [Open demo](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/ReplaceCharacterBody) |

## How to try a demo

1. Clone or download the repository. Open a terminal at its root. Build ContentTool first:

   ```text
   dotnet build ContentTool.csproj -c Release
   ```

   Of the six demo projects with DLLs, CustomCreature and WeaponAdd reference `bin\Release\ContentTool\ContentTool.dll` through their project files, so build ContentTool before building either of them. ContentTool and every demo project find the game’s assemblies through `PPRoot`; if Phoenix Point is outside the project file’s default location, supply `-p:PPRoot="<Phoenix Point>"` to each build.

2. Copy the built `bin\Release\ContentTool` folder into `<Phoenix Point>\Mods\`. Copy your chosen demo into its own sibling folder, `<Phoenix Point>\Mods\<Demo>\`. Keep its `meta.json`, `ppcontent.json`, and any `Content`, `Icons`, or `Dist` folders it contains. The demo must not sit inside `Mods\ContentTool\`.

3. If the table says **Yes** under **Needs DLL**, build that demo’s `.csproj` in Release configuration. Copy its built `<Demo>.dll` into `Mods\<Demo>\`, beside `meta.json`. The demo README names its build project and any game dependency.

4. Start Phoenix Point. In **Mods**, tick **Content Tool**. Bake the copied demo from the game console: use `ct_sound bake <Demo>` for **ReplaceUiSounds** and **MenuMusic**; use `ct_project <Demo>` for the other demos. **IntroVideo** replaces sound as well as video, so run both commands for it. Read the final result and fix any refusal before continuing.

5. Tick the demo in **Mods** and follow its README’s in-game check. A clean bake checks files and declarations; the in-game check tells you whether the intended effect appeared.

For the full sequence from validation through packaging, see [the lifecycle](../concepts/lifecycle.md).
