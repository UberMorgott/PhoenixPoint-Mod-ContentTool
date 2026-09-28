# Replace a texture

Change one shipped `Texture2D` while your mod is enabled. This example changes the Fireworm's shipped emissive image.

## You need

- ContentTool installed and enabled in Phoenix Point.
- An edited PNG, JPG or JPEG.
- The shipped bundle filename and the texture's exact Unity asset name. Find them with `ct_list`; the source filename can be different.
- A project folder beside `ContentTool` under `Mods`.

## Folder layout

```text
Mods\
  MyTextureMod\
    meta.json                 <- lets the mod manager list the mod
    ppcontent.json            <- names the shipped target and your source stem
    Content\
      Textures\
        swatch.png            <- put the image directly here
```

## Steps

1. In the game console, list the shipped target. You should see `fireworm_low_emissive` among the matching `Texture2D` assets.

   ```text
   ct_list assets aln_fireworm_assets_all.bundle Texture2D fireworm_low_emissive
   ```

2. If you want the original image as a starting point, extract it. The command prints its path under `<persistentDataPath>\ContentTool\Extracted`. Edit a copy and save it as `swatch.png`.

   ```text
   ct_extract tex aln_fireworm_assets_all.bundle fireworm_low_emissive
   ```

3. Create `Mods\MyTextureMod\meta.json`:

   ```json
   {
     "ID": "example.mytexturemod",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "My texture mod" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

4. Create `ppcontent.json`. `texture` names the file stem, without `.png`:

   ```json
   {
     "id": "example.mytexturemod",
     "bundle": "MyTextureMod.bundle",
     "replace": [
       {
         "bundle": "aln_fireworm_assets_all.bundle",
         "asset": "fireworm_low_emissive",
         "texture": "swatch"
       }
     ]
   }
   ```

5. Put `swatch.png` directly in `Content\Textures`. Run the bake, then package after its final line says `ALL PASS`:

   ```text
   ct_project MyTextureMod
   ct_package MyTextureMod
   ```

6. Enable the mod in the mod manager and load a scene containing a Fireworm. The replacement takes effect when the game next loads the redirected bundle; see [the lifecycle](../concepts/lifecycle.md).

## Check it worked

Look for these bake lines; sizes and paths vary:

```text
patch aln_fireworm_assets_all.bundle: 'fireworm_low_emissive' <- swatch <width>x<height>
P1 PASS every replaced Texture2D in aln_fireworm_assets_all.bundle reads back its new pixels
ct_project: ALL PASS - <output path>
```

Then inspect the Fireworm in game. `P1 PASS` proves the private bundle copy contains the new pixels; the in-game view confirms the game loaded that copy.

## Common errors

| What you see | Why | Fix |
|---|---|---|
| `P1 REFUSED 'swatch' is not a .png/.jpg under Content\Textures\` | The source stem was not imported. | Put the image directly in `Content\Textures` and check its stem. If the line names another folder, move the file from there. |
| `P1 REFUSED target 'fireworm_low_emissive' is not a Texture2D in aln_fireworm_assets_all.bundle - <reason> - list the names it does hold with: ct_list assets aln_fireworm_assets_all.bundle Texture2D` | The shipped target is missing or ambiguous. | Run the command printed by the refusal and copy the exact name. |
| `SOURCE SKIPPED: Content\Textures\ holds two files with the same name: <a> and <b> - a replacement names the stem, so one of them has to go; BOTH were SKIPPED, the project's other sources are unaffected` | Two source files share a stem, ignoring case. | Keep one image for that stem and bake again. |
| `SOURCE SKIPPED: <file>: <reason> - SKIPPED, the project's other sources are unaffected` | The image could not be imported. This counts as a bake failure. | Re-export the named image as PNG, JPG or JPEG. |

See [messages](../reference/messages.md) for other refusals.

## Example

[WeaponMesh](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/WeaponMesh) replaces five shipped rifle textures with separate `replace` rows.
