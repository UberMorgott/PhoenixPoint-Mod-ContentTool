# Choose Replace, Add or Build

Choose what the game should do with your content before making files. These routes can coexist in one project.

| Goal | Route | What you declare | Typical source |
|---|---|---|---|
| Change content the game already uses | **Replace** | A shipped target and your replacement | `Content\Textures`, `Content\Meshes`, `Content\Audio\Replace`, or `Content\Videos` |
| Give the mod its own content | **Add** | A new model, texture, sound or video and, where needed, a key that uses it | `Content\Models`, `Content\Textures`, `Content\Audio`, or `Content\Videos` |
| Make a game object around new art | **Build** | A shipped donor definition and a `creature` or `weapons` declaration | Usually `Content\Models`, sometimes with a behaviour DLL |

## Replace

Choose Replace when an existing texture, material, mesh, animation clip, sound or video already has the job you want. For a bundle replacement, ContentTool makes a private patched copy from each player’s shipped bundle. Your release contains your declarations and source files, never that patched game bundle.

For a texture, the manifest’s `asset` is the shipped object’s exact, case-sensitive name. Its `texture` is your source file’s stem; put that file directly under `Content\Textures\`. Start with [the quickstart](../quickstart.md).

Sound replacements have their own bake: `Content\Audio\Replace` → `ct_sound bake <YourMod>` → `Dist\Sounds\<mediaId>.bnk`. `ct_project` does not bake that folder.

## Add

Choose Add when no shipped object should be overwritten. ContentTool bakes mod-owned textures, models and new sounds into `Dist\<bundle>`, where `<bundle>` is the manifest’s root `bundle` value. A video stays under `Content\Videos\` and is served live when the mod is enabled; it is not baked into that bundle.

Added content still needs a way for the game to use it. A catalog key, definition change or your own DLL may supply that connection.

!!! warning "Added sounds"
    New sounds under `Content\Audio` bake and self-check, but ContentTool does not load their added bank when players run the mod. They are silent unless the mod’s own DLL loads that bank. Replacing an existing sound through `Content\Audio\Replace` works without a DLL. Streamed added sounds are not packaged. See [known limitations](../known-limitations.md).

## Build

Choose Build when you need a creature or weapon definition around your content. The declaration names a shipped donor for the game-facing structure, then supplies your model and other changes. Follow a [creature](../recipes/creature.md) or [weapon](../recipes/weapon-add.md) recipe instead of guessing the required fields.

A folder name alone does not select a route. In particular, `Content\Meshes\materials` is an old Resource Replacer layout; ContentTool does not scan it for texture sources. See [project files](../reference/project-files.md) for the accepted layout, then [the lifecycle](lifecycle.md) for the build and release sequence.
