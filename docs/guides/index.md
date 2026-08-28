# Recipes

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
| Clone and add weapons | `weapons[]` | yes | [Weapon](weapon.md) |

One project may use any combination of these rows and folders. Read the
[combined example](combined-example.md) if you are about to create one mod per asset type.

For fields and command syntax, use the [manifest and command reference](reference.md).
A DLL is required for exactly three kinds of work: weapons, creatures, and anything that needs a
trigger or def edit. The conditional table rows are instances of that third rule, not additional
content routes that inherently need code. Use the shared, compilable
[behaviour-DLL project and deployment loop](behavior-dll.md).
