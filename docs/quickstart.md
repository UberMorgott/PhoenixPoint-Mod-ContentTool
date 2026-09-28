# Replace one texture in 10 minutes

Make the Acidworm’s body visibly different. You will create one content-only mod, bake its replacement, see it in a tactical encounter, and stage a folder you can share. Install and enable ContentTool first, following [Start here](index.md). Keep the new mod unticked until its first bake.

## 1. Make the folder

Create `MyTextureMod` beside `ContentTool`:

```text
<Phoenix Point>\
  Mods\
    ContentTool\
    MyTextureMod\
      Content\
        Textures\
```

You should see two sibling mod folders under `Mods`. The commands below take `MyTextureMod`, the folder name, rather than a full path.

## 2. Add the mod-manager file

Create `MyTextureMod\meta.json` and paste:

```json
{
  "ID": "example.mytexturemod",
  "Version": "1.0.0",
  "Author": [{ "Key": "English", "Value": "Your Name" }],
  "Name": [{ "Key": "English", "Value": "My Texture Mod" }],
  "Description": [{ "Key": "English", "Value": "Replaces the Acidworm body texture." }],
  "AssemblyName": "",
  "Dependencies": ["com.morgott.ContentTool"]
}
```

You should see `meta.json` directly inside `MyTextureMod`. The empty `AssemblyName` means this mod has no DLL. The dependency lets the mod manager enable ContentTool for players.

## 3. Add the replacement declaration

Create `MyTextureMod\ppcontent.json` and paste:

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

You should now have both JSON files at the project root. The `asset` is the shipped texture’s exact name. The `texture` value names your PNG without its extension. Keep `ID` and `id` the same.

## 4. Put in the PNG

Make a bright, unmistakable PNG in an image editor and save it as `MyTextureMod\Content\Textures\acidworm.png`. A solid magenta 256 × 256 image is enough for this test.

You should see the PNG directly inside `Textures`, with no extra subfolder. ContentTool scans this folder for PNG, JPG and JPEG sources.

## 5. Bake

Start the game with ContentTool enabled. Use either route:

=== "Bench"

    Load a geoscape campaign, open the developer console, and enter:

    ```text
    ct_bench open
    ```

    Choose `Build & share`, pick `MyTextureMod` with `<` / `>` beside `Mod`, open `Details - run one step`, then press `Run` on **Check files** followed by `Run` on **Build**. You should see **Check files** pass and the `Log` on the right end with the `ct_project: ALL PASS` line below. Keep the screen open while its steps run. See [Build & share](bench/lifecycle-tab.md).

=== "Console"

    Open the developer console and enter:

    ```text
    ct_project MyTextureMod
    ```

    You should see a report of the imported texture and patched shipped bundle. Its last line should be:

    ```text
    ct_project: ALL PASS - <project-path>\Dist\MyTextureMod.bundle
    ```

`ALL PASS` is the result to wait for. A `WROTE` line earlier in the report does not mean every declared change succeeded. The mod-owned bundle lands in `MyTextureMod\Dist\`; the private patched copy of the shipped bundle is stored separately under `<persistentDataPath>\ContentTool\Patched\`.

## 6. See it in game

Tick **My Texture Mod** in the mod manager, then load a tactical encounter containing an Acidworm. You should see your bright colour on its body. The redirect affects future bundle loads: if the game loaded that bundle before you ticked the mod, restart with the mod already enabled and enter the encounter again.

## 7. Stage the release

In the console, enter:

```text
ct_package MyTextureMod
```

You should see a line beginning `PACKAGED <count> file(s), <bytes> B into ` and a folder at `<persistentDataPath>\ContentTool\Packaged\MyTextureMod\`. Inspect that folder. Zip the **MyTextureMod folder**, so the archive contains `MyTextureMod\meta.json`. Test that staged folder as a player would before sharing it.

For another texture, use [the texture recipe](recipes/textures.md) and [Find game content](find-content/index.md) to choose its real shipped target.
