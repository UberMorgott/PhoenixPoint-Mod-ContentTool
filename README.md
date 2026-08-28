# ContentTool

[![License](https://img.shields.io/badge/license-CC%20BY--NC%204.0-blue?style=flat-square)](LICENSE)
[![Issues](https://img.shields.io/github/issues/UberMorgott/PhoenixPoint-Mod-ContentTool?style=flat-square)](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/issues)

The content engine for Phoenix Point mods. It lets a mod **replace** things the game ships —
textures, models, materials, animation curves, sounds, videos, whole asset bundles — and **add**
things the game never had, such as a new creature or a new weapon model.

Everything it does happens **in memory, while the game runs**. Nothing is unpacked, patched or
copied into your Phoenix Point installation.

## Are you a player or a modder?

- **Player** — you are here because a mod you want lists ContentTool as a requirement. Install it
  (below), tick it on once, and forget about it. It changes nothing on its own.
- **Modder** — jump to [For modders](#for-modders).

## The guarantee: your game installation is never written

ContentTool does not write into your Phoenix Point folder. Not a patched file, not a catalog edit,
not a backup — a backup inside the install is a write too. **Deleting `Mods\ContentTool` leaves the
installation byte-identical to a clean one.** No uninstaller, no "restore" step, nothing to undo.

Where its working files actually go:

| What | Where |
|---|---|
| Patched copies of game bundles, built on your machine from your own files | `%USERPROFILE%\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\ContentTool\Patched\` |
| A content mod's own media (sounds, videos, models) | that mod's own folder under `Mods\` |
| The game installation | **never** |

If an *older* ContentTool (before this rule) once wrote into your install, the current one detects
those leftovers, **refuses** the affected content by name, and prints the one sanctioned repair:
Steam → Phoenix Point → Properties → Installed Files → *Verify integrity of game files*. It will not
"repair" your install itself, because that would be another write.

## Install

1. Download `ContentTool-*.zip` from the
   [latest release](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/releases/latest).
2. Extract it and copy the `ContentTool` folder into `Phoenix Point\Mods\`, so you end up with
   `Phoenix Point\Mods\ContentTool\ContentTool.dll` and `...\meta.json` beside it.
3. Start the game, open **Mods** in the main menu, tick **Content Tool**.

Requirements: Phoenix Point base game. No other dependency. It does not conflict with TFTV.

**A content mod turns ContentTool on for you.** Content mods declare
`"Dependencies": [ "com.morgott.ContentTool" ]`, and the game's own mod manager refuses to enable
them without it and switches ContentTool on automatically. Both appear as separate toggles; leave
ContentTool ticked.

Content is applied by the checkbox — at startup, and also the moment you tick a content mod on
mid-session. It survives a restart because it is re-applied on every launch, never installed.

## Limits, stated plainly

- **A replaced sound cannot be taken back in the same session.** Ticking a sound mod off does not
  restore the shipped sound: unloading the replacement bank makes Wwise go *silent* rather than
  vanilla (measured). ContentTool leaves the bank loaded and says so in the log. Restart the game —
  that is a clean undo, because nothing was installed anywhere.
- **One shipped resource has exactly one owning mod.** Two mods replacing the same bundle, the same
  Addressables key or the same sound is refused by name (the lower mod id keeps it), never silently
  last-writer-wins. The refusal is a line in `Player.log`.
- **A mod can add a new asset key, but cannot override a key the game already ships.** New keys are
  appended to the game's live catalog, and an appended entry can never outrank the shipped one.
  Replacing shipped content is the bundle-replacement route, not the new-key route.
- **Never reference an assembly nothing loads for you.** Phoenix Point installs no `AssemblyResolve`
  handler, and it answers a failed mod load by rewriting the activated-mods list empty — silently
  disabling *every* other mod on the machine. The real trap is a Unity module under
  `PhoenixPointWin64_Data\Managed\` that `ModSDK\` does not ship; reach those **by reflection**.
  Referencing `ContentTool.dll` itself is fine — the mod manager loads a declared dependency before
  its dependents — provided `meta.json` declares the dependency and the reference is
  `<Private>false</Private>`. Reflection is still the right call when you want your mod to survive an
  older or absent ContentTool, because `Dependencies` carries no minimum version.

## Something did not work

Every refusal is a named line in your player log:

```
%USERPROFILE%\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\Player.log
```

Search it for `ct_`. The line says which mod, which resource and why — "disabled in the mod
manager", "no meta.json", "already owned by ...". Attach that line to a bug report:
[open an issue](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/issues/new).

---

## For modders

**Shortest path from a downloaded model to a mod folder other people can install:**

1. **Make the folder.** `Mods\MyMod\meta.json`, with
   `"Dependencies": [ "com.morgott.ContentTool" ]`. Copy one from `demos\`.
2. **Drop your sources in.** `Content\Meshes\*.glb .obj`, `Content\Textures\*.png .jpg`,
   `Content\Audio\*.wav .ogg .mp3`, `Content\Videos\*.webm .mp4 .mov`. Unconverted, as downloaded.
3. **Declare what they do.** `ppcontent.json` — one `"replace"` row per thing you swap, one
   `"publish"` row per thing you add. Copy the closest demo's file; the grammar is in
   [`docs/SHIPPING-A-CONTENT-MOD.md`](docs/SHIPPING-A-CONTENT-MOD.md).
4. **Bake, in game.** Launch with ContentTool and your mod enabled, open the developer console and
   run `ct_project MyMod` (and `ct_sound bake MyMod` if you replace a sound). This writes your
   mod's own `Dist\` — the only step that needs the game, because Unity's decoders and *your* copy
   of the install are what produce it.
5. **Package, with the game shut.**
   ```powershell
   .\package.ps1 -Project demos\MyMod        # or: -Project D:\MyMod
   ```
   It builds your DLL if you have one, refuses a project with nothing baked, refuses redistributed
   Phoenix Point data, and writes a clean mod folder under `dist-package\MyMod`. Zip **that** and
   publish it.

The full contract — what a mod folder may contain, what applies by itself, what the checkbox does
and does not undo per route, and the rules that are not negotiable — is
**[`docs/SHIPPING-A-CONTENT-MOD.md`](docs/SHIPPING-A-CONTENT-MOD.md)**. Working examples are in
[`demos/`](demos/), one capability each. Developer documentation index:
[`docs/README.md`](docs/README.md).

The `ct_*` console commands are an author's workbench. A player never types one.

## License

[CC BY-NC 4.0](LICENSE) — Morgott. Non-commercial, attribution required.
Content mods you build with it are yours; this license covers ContentTool itself.
