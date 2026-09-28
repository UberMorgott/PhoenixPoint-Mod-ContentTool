# Recipes

Pick what you want to change. **Replace** changes a shipped asset. **Add** puts an asset in your mod's bundle or adds a catalog entry. **Build** creates game definitions from your manifest. Start with [Replace, Add or Build](../concepts/routes.md) if you are unsure.

| I want to... | Recipe | Route | Needs a DLL? | Demo |
|---|---|---|---|---|
| Change a shipped image | [Replace a texture](textures.md) | Replace | No | [WeaponMesh](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/WeaponMesh) |
| Change one number on a shipped material | [Change a material property](material-properties.md) | Replace | No | [MaterialTweak](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/MaterialTweak) |
| Replace shipped geometry | [Replace a mesh](meshes.md) | Replace | No for the mesh | [WeaponMesh](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/WeaponMesh) |
| Import and publish a complete GLB | [Add a complete model](animated-models.md) | Add | Only if your mod must spawn or otherwise use it | [WeaponAdd](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/WeaponAdd) |
| Prepare a rig and its clips | [Prepare an animated GLB](animation-contract.md) | Add or Replace | No for import | [CustomCreature](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/CustomCreature) |
| Replace a sound the game already plays | [Replace a sound](sounds-replace.md) | Replace | No | [ReplaceUiSounds](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/ReplaceUiSounds) |
| Add a new sound event | [Add sounds](sounds-add.md) | Add | Yes: the mod must load the bank and post the event | [AddUiSounds](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/AddUiSounds) |
| Replace a cutscene clip or add a video key | [Use a video](videos.md) | Replace or Add | Only to trigger a new clip or change its flow | [IntroVideo](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/IntroVideo), [QuitCutscene](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/QuitCutscene) |
| Add code that reacts to game events | [Build a behaviour DLL](behavior-dll.md) | Build | Yes | [WeaponMesh](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/WeaponMesh) |
| Add a creature with its own rig and clips | [Add a creature](creature.md) | Build | No for the creature manifest | [CustomCreature](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/CustomCreature) |
| Add a playable humanoid | [Add a humanoid soldier](humanoid-soldier.md) | Build | No for the creature manifest | [HumanoidSoldier](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/HumanoidSoldier) |
| Change an existing character's body | [Replace a character body](replace-character-body.md) | Replace | No for the documented body swap | [ReplaceCharacterBody](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/ReplaceCharacterBody) |
| Put new art on a shipped weapon | [Replace weapon art](weapon-replace.md) | Replace | No for mesh and textures; yes for extra def edits | [WeaponMesh](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/WeaponMesh) |
| Create new weapon definitions | [Add weapons](weapon-add.md) | Build | No for the weapons manifest | [WeaponAdd](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/WeaponAdd) |
| Replace a shipped armour set from Blender | [Replace an armour set](armour-set.md) | Replace | No for its meshes and textures | — |

After a recipe's bake, follow [the lifecycle](../concepts/lifecycle.md) to apply, verify and package it. If a line says `REFUSED`, `SKIPPED`, `FAIL` or `VOID`, use [messages](../reference/messages.md) to find the next action.
