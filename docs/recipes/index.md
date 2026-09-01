# Recipes

Start with the route, not the file extension. Replace changes a shipped target. Add writes content
to your mod's own bundle and usually needs a consumer or trigger.

```text
MyMod\
  meta.json                 <- dependency and optional DLL name
  ppcontent.json            <- Replace, publish, creature or weapon declarations
  Content\
    Textures\               <- PNG/JPG/JPEG, direct children only
    Meshes\                 <- replacement OBJ/GLB
    Models\                 <- complete new GLB models
    Audio\                  <- added WAV/OGG/MP3
      Replace\              <- shipped-media replacement sources
    Videos\                 <- WEBM/MP4/MOV
  Dist\                     <- output from ct_project or ct_sound bake
```

| You want to… | Use this recipe |
|---|---|
| Replace a shipped texture | [Replace a texture](textures.md) |
| Change one float on a shipped material | [Change a material number](material-properties.md) |
| Replace static or rigged geometry | [Replace a mesh](meshes.md) |
| Add and publish a complete GLB | [Bake and publish a complete model](animated-models.md) |
| Add or replace Wwise media | [Add or replace sounds](sounds.md) |
| Add or replace a streamed clip | [Add or replace a video](videos.md) |
| Add a non-humanoid with its own clips | [Add a creature](creature.md) |
| Retarget the worked foreign humanoid | [Add a playable humanoid soldier](humanoid-soldier.md) |
| Experiment with an existing character's body | [Replace a shipped character body](replace-character-body.md) |
| Replace weapon art or clone a new weapon def | [Replace weapon art or add a weapon](weapon.md) |
| Add a trigger or builder call | [Build a behaviour DLL](behavior-dll.md) |
| Prepare a rig/skin/clip source | [Prepare a rigged, animated GLB](animation-contract.md) |

Every recipe ends with exact failure lines and links to the
[status glossary](../troubleshooting/bake-errors.md). Fix the first refusal, then read the final
summary. Do not package a bake that did not end in `ct_project: ALL PASS`.
