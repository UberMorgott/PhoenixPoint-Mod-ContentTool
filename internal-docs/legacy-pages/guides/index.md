# Recipes

Read [Replace vs Add](replace-vs-add.md) first if you are unsure which route you need and why
Replace mods bake on the player's machine.

Use [How a mod is made](../SHIPPING-A-CONTENT-MOD.md) for the lifecycle and
[Discover game content](discovery.md) for target names. A recipe begins where those shared steps
leave off: the route-specific folder, complete manifests, bake command, test and limits.

| Change | Content route | DLL? | Recipe |
|---|---|---|---|
| Replace a bundle `Texture2D` | `replace[].texture` | no | [Textures and icons](textures.md) |
| Set one float on a shipped material | `replace[].material` | no | [Materials](materials.md) |
| Replace a static or rigged mesh | `replace[].mesh` | no | [Meshes](meshes.md) and [animated models](animated-models.md) |
| Publish a new model | `Content\Models` + `publish[]` | only if behaviour must use it | [Meshes](meshes.md#publish-a-new-model) |
| Replace loose/streamed media | `sounds[]` or media-ID filename | no | [Sounds](sounds.md#replace-shipped-media) |
| Add a sound | `Content\Audio` | only to trigger it | [Sounds](sounds.md#add-a-sound) |
| Replace a video | `replace[].video` with `asset` | no | [Videos](videos.md#replace-a-shipped-video) |
| Add a video | `replace[].video` without `asset` | only to trigger it | [Videos](videos.md#add-a-video) |
| Build a creature from its own rig and clips | `creature` | yes | [Creature](creature.md) and [animation contract](animation-reference.md) |
| Retarget a foreign humanoid as a playable soldier | `creature` | no | [Humanoid soldier](humanoid-soldier.md) and [Creature](creature.md) |
| Give a SHIPPED character a different body, keeping her identity | `creature.replaceBody` | no | [A shipped character's new body](replace-character-body.md) |
| Clone, add and fit weapons | `weapons[]` | yes | [Weapon and in-game fit workbench](weapon.md#fit-the-model-in-the-workbench) |

One project may use any combination of these rows and folders. Read the
[combined example](combined-example.md) if you are about to create one mod per asset type.

For fields and command syntax, use the [manifest and command reference](reference.md).
A DLL is required for exactly three kinds of work: weapons, creatures, and anything that needs a
trigger or def edit. The conditional table rows are instances of that third rule, not additional
content routes that inherently need code. The one creature exception is `"startingRoster": true`,
which is the built-in injection point: the content builder boards the unit itself, so a creature
that only needs to appear at campaign start needs no DLL of its own. Use the shared, compilable
[behaviour-DLL project and deployment loop](behavior-dll.md).
