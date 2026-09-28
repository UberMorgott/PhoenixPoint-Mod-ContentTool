# Project files and folders

Make a folder for your mod beside the `ContentTool` folder under `Mods`. Pass its **folder name** to authoring commands. If that sibling has no `ppcontent.json`, ContentTool can instead select a same-named folder inside its own folder. Keep one working copy so the selected project is clear.

```text
<Phoenix Point>\
  Mods\
    ContentTool\
      ContentTool.dll
      <project>\                 <- fallback when no sibling project has ppcontent.json
        ppcontent.json
    <project>\                   <- preferred authoring project
      meta.json
      ppcontent.json
      README.md                  <- optional release notes
      SOURCES.md                 <- optional source and licence notes
      LICENSE                    <- optional
      Icons\                     <- optional mod-manager and weapon art
      Content\
        Textures\                <- .png, .jpg, .jpeg
        Meshes\                  <- .obj, .glb replacement geometry
        Models\                  <- .glb complete new models
        Audio\                   <- .wav, .ogg, .mp3 new sounds
          Replace\               <- source files for shipped-sound replacements
        Videos\                  <- .webm, .mp4, .mov
      Dist\
        <bundle>                 <- mod-owned baked bundle
        Sounds\
          <mediaId>.bnk          <- baked shipped-sound replacements
          sources.ledger
```

Baking streamed new sounds extracts their streams into the project's own `WwiseAudio\` folder; ContentTool's self-check uses `<ContentTool mod folder>\WwiseAudio\`. Neither is part of a release. Patched copies of **shipped** bundles live outside the project at `<persistentDataPath>\ContentTool\Patched\<install-tag>\<id>\`. Do not put them in the mod or a release archive. `ct_package` refuses a `Patched` folder in staged content. Its publishable output is `<persistentDataPath>\ContentTool\Packaged\<project>\`.

## Root files

- `ppcontent.json` is required. Its root `id` and `bundle` must be non-empty, safe single names. They determine the patched-copy folder and `Dist\<bundle>`. See [all manifest keys](ppcontent-json.md).
- `meta.json` is required for a mod the player can install. Its `ID` must be non-empty, `Dependencies` must contain `com.morgott.ContentTool`, and a declared `AssemblyName` must name a DLL in the package. See [metadata fields](meta-json.md).
- ContentTool does not compare the two IDs. Give `meta.json`'s `ID` and `ppcontent.json`'s `id` the same globally distinct value.

## Source files

ContentTool scans files **directly inside** the named source folders. It does not search their subfolders.

| Folder | Accepted extensions | How the name is used | Other files |
|---|---|---|---|
| `Content\Textures\` | `.png`, `.jpg`, `.jpeg` | Lowercase file stem for a texture source. | Not imported. |
| `Content\Meshes\` | `.obj`, `.glb` | Lowercase file stem for replacement geometry. A mesh needs a `replace` row to be baked. | Not imported. |
| `Content\Models\` | `.glb` | Lowercase file stem for a complete new model. | Not imported. |
| `Content\Audio\` | `.wav`, `.ogg`, `.mp3` | Stem contributes to an added-sound record. | Unsupported files are named and counted as skipped sources. |
| `Content\Audio\Replace\` | `.wav`, `.ogg`, `.mp3` | A numeric stem can identify shipped media directly; a `sounds` row can instead name a source file. | Handled by `ct_sound bake`, not the new-sound scan. |
| `Content\Videos\` | `.webm`, `.mp4`, `.mov` | Lowercase file stem for a live video row. | Not imported. |

For example, a texture row naming stem `<stem>` looks for an accepted file directly in `Content\Textures\`. A file in `Content\Textures\Characters\` is too deep. The old `Content\Meshes\materials\` layout is not a texture source folder. Material property changes are `material` values in `ppcontent.json`, not separate material files.

!!! warning "Added sounds"

    New sounds in `Content\Audio` bake and self-check, but ContentTool does not load their added bank when players run the mod. They are silent unless the mod's own DLL loads that bank. Replacement sources in `Content\Audio\Replace` become banks in `Dist\Sounds` and work without a DLL. New sources named `<name>.stream.wav`, `<name>.stream.ogg`, or `<name>.stream.mp3` are not packaged.

## Names and collisions

- An imported source gets its file stem as its name. Source references ignore case; imported stems are lowercased.
- Do not put two accepted files with the same stem, ignoring case, in one source folder. For example, `swatch.png` and `SWATCH.jpg` both claim `swatch`. **Both** files of a colliding stem are skipped and the failure is counted; other stems can still bake. This also applies to `Content\Videos`.
- `Content\Audio\Replace` must not contain two files aimed at the same media ID. `ct_sound bake` refuses that collision.
- A shipped Unity asset target is matched by its exact, case-sensitive `m_Name`. Use `ct_list` to copy the name. If no asset or more than one asset of the requested class has that name in the bundle, the row is refused.
- A replacement's shipped bundle name is matched without regard to case, but it must name a bundle the game ships.
- Root `id` and `bundle` must each be **one plain file or folder name**, not a path. They cannot be empty, `.` or `..`, contain a path separator, colon, or invalid filename character, start or end with a space, or end with a dot. Windows reserved device names are also refused, even with an extension: `CON`, `PRN`, `AUX`, `NUL`, `COM1`–`COM9`, and `LPT1`–`LPT9`.
- Keep JSON key spelling as shown in [the manifest reference](ppcontent-json.md). `replace`, `publish`, and `sounds` use a JSON parser; `creature` and `weapons` use their documented block and flat-row forms.

Before publishing, run the applicable bake commands and then `ct_package <project>`. Zip the **package folder itself**, so extraction into `Mods\` places `meta.json` inside the mod's own folder.
