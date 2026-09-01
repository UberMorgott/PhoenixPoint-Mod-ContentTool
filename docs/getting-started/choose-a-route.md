# Choose Replace or Add

Decide whether the game already has the thing you want to change.

| Your goal | Route | What your manifest names | Typical source folder |
|---|---|---|---|
| Change a shipped texture, material, mesh, clip, sound or video | Replace | The shipped target and your replacement | `Content\Textures`, `Content\Meshes`, `Content\Audio\Replace`, or `Content\Videos` |
| Publish a model, sound or video that the game does not have | Add | Your source and a new ContentTool key | `Content\Models`, `Content\Audio`, or `Content\Videos` |
| Build a creature or weapon from game definitions plus your content | Build | A donor definition and a creature or weapon declaration | Usually `Content\Models` plus a behaviour DLL when the route requires code |

Replace and Add may live in one project. They still use different declarations and may produce
different outputs.

```text
MyMod\
  meta.json
  ppcontent.json             <- may hold replace[], publish[], creature or weapons[] rows
  Content\
    Textures\                <- replacement or mod-owned textures: .png/.jpg/.jpeg
    Meshes\                  <- replacement geometry: .obj/.glb
    Models\                  <- new complete models: .glb
    Audio\                   <- new sounds: .wav/.ogg/.mp3
      Replace\               <- shipped-sound replacement sources; separate bake route
    Videos\                  <- .webm/.mp4/.mov
```

## Choose Replace when

You can name a shipped bundle object or loose-media row that already performs the right job. A
replacement changes that target while the mod is enabled. A bundle replacement is baked from the
player's own shipped file into a private copy. Do not distribute that patched copy.

For a texture row, `asset` is the exact, case-sensitive `m_Name` in the shipped bundle. `texture` is
your source file's stem and is matched without regard to case. The source still has to be directly
under `Content\Textures\`.

## Choose Add when

No shipped target should be overwritten. ContentTool writes mod-owned content to the project's own
bundle in `Dist\`. Adding content does not by itself tell the game when to use it. Some routes need a
DLL for a trigger, a definition change or other behaviour.

## Do not choose by folder name

`Content\Meshes\materials\` came from Resource Replacer. It is not a material route and it is not a
texture import folder. ContentTool material changes are numbers written in a `replace[]` row. Texture
files belong in `Content\Textures\`.

Next: [learn every accepted folder and extension](../reference/project-files.md).
