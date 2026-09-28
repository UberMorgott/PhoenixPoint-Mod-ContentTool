# Start here

ContentTool lets Phoenix Point mods replace shipped content, add new content, and build game objects from content and shipped definitions. It is the engine those mods use; installing ContentTool alone does not change the game.

## Players: install in four steps

1. Download `ContentTool-*.zip` from the [latest release](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/releases/latest). You should have a ZIP containing a `ContentTool` folder.
2. Extract that folder into `<Phoenix Point>\Mods\`. You should now have `<Phoenix Point>\Mods\ContentTool\meta.json`, with no second `ContentTool` folder in between.
3. Start Phoenix Point and open **Mods**. You should see **Content Tool** listed.
4. Tick **Content Tool**. You should see it enabled. Mods that declare ContentTool as a dependency can also enable it through the mod manager.

## Modders

Start with the [10-minute quickstart](quickstart.md). It gives you a complete, copy-paste texture replacement and shows what each step should produce.

Then use this map:

- [Replace, Add or Build](concepts/routes.md) helps you choose the right kind of mod.
- [The lifecycle](concepts/lifecycle.md) explains baking, redirects, testing and packaging.
- [Recipes](recipes/index.md) give steps for individual content types.
- [Find game content](find-content/index.md) helps you identify shipped targets.
- [The in-game bench](bench/index.md) does the common jobs on screen, step by step: [replace a model](bench/model-doctor.md), [add a weapon](bench/add-weapon.md) and [fit it](bench/fit-weapon.md), replace [sounds](bench/sounds.md) and [videos](bench/videos.md), then [build & share](bench/lifecycle-tab.md) the mod.
- [Project files](reference/project-files.md), [console commands](reference/console-commands.md) and [messages](reference/messages.md) explain the exact inputs and results.
- [Examples](examples/index.md) show complete projects; [known limitations](known-limitations.md) records current boundaries.

ContentTool’s diagnostic developer commands require an empty `ct-dev` file beside `ContentTool.dll`; nothing on this site needs it.
