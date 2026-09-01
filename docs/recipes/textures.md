# Replace a texture

This replaces one shipped `Texture2D` while your mod is enabled. Use it for albedo, normal,
metallic, emissive and UI texture assets that already exist in a shipped bundle.

## What you need before you start

- ContentTool 1.1.2 enabled in Phoenix Point.
- A PNG, JPG or JPEG. The extension is not case-sensitive. Put it directly in `Content\Textures`.
- The shipped bundle filename and the target texture's exact, case-sensitive Unity `m_Name`.
- A unique project ID. Source stems are matched without regard to case; two accepted files may not
  share a stem.

## Folder tree

```text
MyTextureMod\
  meta.json                    <- mod manager reads ID and Dependencies here
  ppcontent.json               <- asset is the game target; texture is your file stem
  Content\
    Textures\                  <- the only folder scanned for texture sources
      acidworm.png             <- becomes source "acidworm"
    Meshes\
      materials\               <- old Resource Replacer layout; never put textures here
```

## Steps

1. Find the shipped target. The optional last argument filters names:

   ```text
   ct_list assets aln_acidworm_assets_all.bundle Texture2D acidworm
   ```

2. If you want the shipped image as a starting point, extract it:

   ```text
   ct_extract tex aln_acidworm_assets_all.bundle acidworm_low_albedo
   ```

   The command prints the PNG path under
   `<persistentDataPath>\ContentTool\Extracted\aln_acidworm_assets_all`. Copy it into your project,
   edit it, and rename it `acidworm.png`. The source filename does not have to match the target.

3. Create `meta.json`:

   ```json
   {
     "ID": "example.mytexturemod",
     "AssemblyName": "",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "My texture mod" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

4. Create `ppcontent.json`. Copy `asset` from `ct_list` with the same case. `texture` is the source
   stem, so do not include `.png`:

   ```json
   {
     "id": "example.mytexturemod",
     "bundle": "MyTextureMod.bundle",
     "replace": [
       {
         "bundle": "aln_acidworm_assets_all.bundle",
         "asset": "acidworm_low_albedo",
         "texture": "acidworm"
       }
     ]
   }
   ```

5. Save `acidworm.png` directly under `Content\Textures`. Deeper folders are not scanned.

6. Bake, then package only after an all-pass result:

   ```text
   ct_project MyTextureMod
   ct_package MyTextureMod
   ```

## What success looks like

Sizes and absolute paths vary. These lines do not:

```text
patch aln_acidworm_assets_all.bundle: 'acidworm_low_albedo' <- acidworm <width>x<height>
WROTE <patched path> <bytes> B as <bundle identity>
P1 PASS 'acidworm_low_albedo' in the copy is <width>x<height> RGBA32 and its bytes are the imported source
copies ready in <path> - nothing to install: ticking 'MyTextureMod' on serves these private copies in memory; ticking it off hands the shipped bundles straight back
ct_project: ALL PASS - this project has no bundle of its own; the patched copy(ies) above are the whole output
```

## When it fails

| Exact output | Meaning | Fix |
|---|---|---|
| `P1 REFUSED 'acidworm' is not a .png/.jpg under Content\Textures\` | The source was not imported. | Move `acidworm.png`, `.jpg` or `.jpeg` directly into `Content\Textures`, or correct `texture`. Delete any stale copy under `Content\Meshes\materials`. |
| `P1 REFUSED target 'acidworm_low_albedo' is not a Texture2D in aln_acidworm_assets_all.bundle - <reason> - list the names it does hold with: ct_list assets aln_acidworm_assets_all.bundle Texture2D` | The source exists; the game target does not. | Run the printed command and copy the exact target name. Do not rename your source to hide a bad `asset`. |
| `SOURCE SKIPPED: <file>: <reason> - SKIPPED, the project's other sources are unaffected` | The image decoder rejected the file. | Re-export it as PNG/JPG/JPEG and remove the unreadable file. |

Read [the status glossary](../troubleshooting/bake-errors.md) before interpreting `SKIPPED`,
`P1 REFUSED`, `FAILURE(S)` or a package refusal.
