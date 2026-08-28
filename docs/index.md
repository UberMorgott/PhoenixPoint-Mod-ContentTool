# ContentTool

ContentTool is an engine for Phoenix Point content mods. It lets those mods replace or add images,
materials, models, animations, sounds, videos, creatures and weapons. On its own, it changes nothing.

## Players

If another mod told you to install ContentTool:

1. Download `ContentTool-*.zip` from the [latest release](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/releases/latest).
2. Copy the `ContentTool` folder into `Phoenix Point\Mods\`.
3. Start the game, open **Mods**, and tick **Content Tool**.

You do not need the developer console or any bake command. A mod that declares ContentTool as a
dependency makes the mod manager enable it automatically; both mods remain visible as separate
checkboxes.

## Modders

Choose the path that matches what you are trying to do:

1. **Build a first mod from an empty folder.** Follow
   [Shipping a content mod](SHIPPING-A-CONTENT-MOD.md#from-an-empty-folder-to-a-release). It covers
   the two JSON files, source folders, baking, packaging, testing and the release zip in order.
2. **Start from working code and content.** The [ten demos](demos.md) each isolate one technique and
   point to the files worth reading.
3. **Follow a content-specific recipe.** The [recipe ladder](guides/index.md) covers textures,
   materials, meshes, animated models, sounds, videos, creatures and weapons.
4. **Look up a field or command.** Use the [shared reference](guides/reference.md) for the folder
   layout, `meta.json`, `ppcontent.json`, discovery commands and packaging details.

The shortest useful rule is: content files and JSON describe assets; a DLL is needed only when the
game needs new behaviour to reach them, such as a hotkey, cutscene trigger or new def.

## If something fails

Check the loaded build with `ct_version`, then search this log for `ct_`:

```text
%USERPROFILE%\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\Player.log
```

The log names the mod, resource and reason for a refusal. The
[shared reference](guides/reference.md#8-how-contenttool-discovers-you) explains the discovery and
status lines.

## License

[CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/) — Morgott. Content mods built with
ContentTool remain their authors' work.
