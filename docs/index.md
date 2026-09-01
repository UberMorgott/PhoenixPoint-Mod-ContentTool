# Start here

ContentTool is an engine for Phoenix Point content mods. It changes nothing by itself. A content mod
tells it what to replace or add.

## Players

1. Download `ContentTool-*.zip` from the [latest release](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/releases/latest).
2. Extract the `ContentTool` folder into `<Phoenix Point>\Mods\`.
3. Check that this file exists:

```text
Phoenix Point\
  Mods\
    ContentTool\
      meta.json              <- the mod manager finds ContentTool here
```

4. Start the game, open **Mods**, and tick **Content Tool**.

If another mod declares ContentTool as a dependency, the mod manager can enable ContentTool for it.
You do not need the authoring commands below just to play with a mod.

## Modders

Follow this route once before you pick a specialised recipe:

1. [Make your first green bake](getting-started/first-mod.md). This proves that your folder, manifests,
   console and installation agree.
2. [Choose Replace, Add or Build](getting-started/choose-a-route.md).
3. [Learn the project layout and file rules](reference/project-files.md).
4. [Find the bundle, asset or media you need](find-content/index.md).
5. [Bake, test and package](getting-started/lifecycle.md).
6. [Read a failed bake](troubleshooting/bake-errors.md) before moving files at random.
7. [Pick a recipe](recipes/index.md).
8. [Open the closest worked demo](examples/index.md) and compare its manifest to yours.

The first five pages are the golden path. They use one vocabulary throughout:

- **source**: a file you made, such as a PNG, GLB or WAV;
- **target**: shipped content named by a replacement row;
- **bake**: `ct_project` imports sources and produces or checks game-ready output;
- **package**: `ct_package` stages the folder you may distribute. It does not bake.

## The texture rule

Put texture sources directly under `Content\Textures\`.

```text
MyMod\
  meta.json
  ppcontent.json
  Content\
    Textures\              <- .png, .jpg and .jpeg are scanned here
      soldier_albedo.png   <- the manifest names this as "soldier_albedo"
    Meshes\
      materials\           <- old Resource Replacer layout; ContentTool does not scan textures here
```

`Content\Meshes\materials\` is not a ContentTool texture folder. A texture left there may still be
copied by the packager, but `ct_project` will not import it. See
[Textures versus materials](troubleshooting/bake-errors.md#textures-versus-materials).

## Worked demos

The site documents all [11 public demo projects](examples/index.md), including the route, targets,
folder tree, authoring commands, success output and measured status of each. `NoDepTexture` is an
internal fixture and is deliberately not presented as a modding example.
