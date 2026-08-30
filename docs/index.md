# ContentTool

ContentTool is an engine for Phoenix Point content mods. It lets those mods replace or add textures,
materials, models, animations, sounds, videos, creatures and weapons. It changes nothing by itself.

## Players

1. Download `ContentTool-*.zip` from the [latest release](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/releases/latest).
2. Copy the `ContentTool` folder into
   `<Steam library>\steamapps\common\Phoenix Point\Mods\`.
3. Start the game, open **Mods**, and tick **Content Tool**.

That is all. You do not run author commands or build another mod's files. A content mod that declares
ContentTool as a dependency makes the mod manager enable it automatically.

If ContentTool does not appear, the folder is probably nested one level too deeply. The file must be
at `<Steam library>\steamapps\common\Phoenix Point\Mods\ContentTool\meta.json`.

## Modders

Start here, in this order:

1. [How a mod is made](SHIPPING-A-CONTENT-MOD.md) — create the folder, preview changes, bake,
   package, reinstall as a player, and ship.
2. [Open the developer console](SHIPPING-A-CONTENT-MOD.md#open-the-developer-console) — the
   physical backquote-key position opens it; nothing needs enabling when the game launched with mods.
3. [Discover game content](guides/discovery.md) — find the bundle, asset, def, media ID, video row,
   bone names, material properties and clip data you need.
4. [Pick a recipe](guides/index.md) — texture, material, mesh, animated model, sound, video,
   creature, weapon, or a [foreign humanoid soldier](guides/humanoid-soldier.md).
5. [Manifest and command reference](guides/reference.md) — every supported field and author-facing
   console command.

One project can use several routes at once. The
[combined example](guides/combined-example.md) replaces a texture, adds a sound, replaces another
sound and publishes a model from one `ppcontent.json`.

### The DLL answer

A content-only mod needs no DLL and no stub DLL. Use `"AssemblyName": ""` in `meta.json`, or omit
that field. Add a real DLL only when your mod needs behaviour: a hotkey, a trigger, a def change, or
the call that builds a declared creature or weapon — with one exception, `"startingRoster": true`,
which boards a declared creature at campaign start with no code of your own. ContentTool supplies an in-memory loader shim so
the mod-manager checkbox works for a code-less content mod; there is no fake file on disk to hit,
protect, rename or delete.

When behaviour is required, use the complete [DLL project and `ModMain` skeleton](guides/behavior-dll.md).

### Supported and tested versions

ContentTool `1.0.0.0` is verified with Phoenix Point **1.30.2.75117**
(`ReleaseCandidate2025`), Unity **2019.4.31f1**, on Windows. A `meta.json` dependency carries only the
ContentTool ID and no minimum version, so an author must test version skew and a game update against
the exact package they ship.

## If something fails

[Open Phoenix Point's developer console](SHIPPING-A-CONTENT-MOD.md#open-the-developer-console) and
run:

```text
ct_version
```

Then search this file for your mod ID and `ct_`:

```text
%USERPROFILE%\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\Player.log
```

Long console reports are written there in full. Start with the first refusal, not the last symptom.

## License

[CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/) — Morgott. Content mods built with
ContentTool remain their authors' work.
