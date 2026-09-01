# Project files and folders

Keep an authoring project beside ContentTool. Every author command takes the bare folder name. A
path such as `E:\Games\Phoenix Point\Mods\MyMod` is not a valid command argument.

```text
Phoenix Point\
  Mods\
    ContentTool\
      meta.json
      MyMod\                   <- fallback location, used only when no sibling project is found
        ppcontent.json
    MyMod\                     <- preferred; this sibling wins when it has ppcontent.json
      meta.json                <- mod manager and packager metadata
      ppcontent.json           <- ContentTool project and route declarations
      README.md                <- optional release notes
      SOURCES.md               <- optional source and licence notes
      LICENSE                  <- optional
      Icons\                   <- optional mod-manager art
      Content\                 <- author-owned source files
        Textures\              <- .png, .jpg, .jpeg
        Meshes\                <- .obj, .glb used as replacement geometry
        Models\                <- .glb published as a complete new model
        Audio\                 <- .wav, .ogg, .mp3 used as new sounds
          Replace\             <- shipped-sound replacement sources; not scanned as new sounds
        Videos\                <- .webm, .mp4, .mov
      Dist\                    <- baked mod-owned output and sound banks
```

The sibling is selected only when it has `ppcontent.json`. If it does not, a stale
`Mods\ContentTool\MyMod\ppcontent.json` can be selected instead. Keep one working copy.

## Root files

`meta.json` is required for the mod manager and packaging. `ct_package` checks these fields:

- `ID` must be present and non-empty. The mod manager keys mods on it.
- `Dependencies` must contain `com.morgott.ContentTool`.
- If `AssemblyName` names a DLL, that DLL must be present in the staged package. Use an empty string
  for a content-only mod.

`ppcontent.json` is required for every ContentTool project. Its root `id` and `bundle` values must be
present and non-empty. The current code does not compare `meta.json`'s `ID` with `ppcontent.json`'s
`id`; use the same globally distinct value so one mod is not given two identities.

A `replace[]` row needs exactly one of `texture`, `material`, `mesh`, `clip` or `video`. Texture,
material, mesh and clip rows also need `bundle` and `asset`. A video row has no bundle; omitting its
`asset` makes it an Add row instead of a replacement.

## What is scanned

ContentTool scans only files directly inside each named folder. It does not recurse into subfolders.

| Folder | Accepted extensions | Name used by the manifest | Other files |
|---|---|---|---|
| `Content\Textures\` | `.png`, `.jpg`, `.jpeg` | lower-case file stem | not enumerated; silently ignored |
| `Content\Meshes\` | `.obj`, `.glb` | lower-case file stem | not enumerated; silently ignored |
| `Content\Models\` | `.glb` | lower-case file stem | not enumerated; silently ignored |
| `Content\Audio\` | `.wav`, `.ogg`, `.mp3` | file stem for imported sound records | reported by name as unsupported |
| `Content\Videos\` | `.webm`, `.mp4`, `.mov` | lower-case file stem | not enumerated; silently ignored |

“Silently ignored” means the file is absent from the imported source list. If `ppcontent.json`
declares a replacement that needs its stem, the replacement row later prints a refusal and the run
ends with failures. A stray undeclared file can pass unnoticed.

Do not place sources in deeper folders such as `Content\Textures\Characters\`. Move the files up one
level. `Content\Audio\Replace\` is deliberately separate and is handled by the shipped-sound route.

## Names and case

- A source is named by its file stem. `Soldier_Albedo.png` becomes `soldier_albedo` when imported.
- Source references are matched without regard to case.
- Two accepted files in one source folder may not have the same stem ignoring case. This includes
  two formats such as `swatch.png` and `SWATCH.jpg`.
- A shipped Unity asset target is matched by exact, case-sensitive `m_Name`. Copy it from `ct_list`.
- That name must identify exactly one asset of the requested class in the bundle. Zero matches and
  duplicate names are both refused; ContentTool does not guess from path ID or list order.
- Replacement bundle rows are grouped without regard to case. The named shipped bundle must exist.
- Keep JSON field spelling exactly as shown. Several route arrays are read from the raw JSON text.

## Textures are not materials

This is the ContentTool layout:

```text
MyMod\
  ppcontent.json
  Content\
    Textures\
      soldier_albedo.png      <- imported as texture source "soldier_albedo"
    Meshes\
      soldier.glb             <- replacement geometry only
      materials\
        soldier_albedo.png    <- old Resource Replacer layout; ignored by texture import
```

A material change has no material source file. It is a value such as
`"material": "_GlossMapScale=0.15"` in a `replace[]` row. The old
`Content\Meshes\materials\` convention has no meaning to ContentTool.

Next: [find target names](../find-content/index.md) or
[read placement failures](../troubleshooting/bake-errors.md).
