# ContentTool

The content engine for Phoenix Point mods. It lets a mod **replace** what the game ships —
textures, models, materials, sounds, videos, whole asset bundles — and **add** what it never had,
such as a new creature or a new weapon.

Everything happens **in memory, while the game runs**. Nothing is unpacked, patched or copied into
your Phoenix Point installation.

!!! success "The guarantee: your game installation is never written"
    Not a patched file, not a catalog edit, not a backup — a backup inside the install is a write
    too. **Deleting `Mods\ContentTool` leaves the installation byte-identical to a clean one.** No
    uninstaller, no restore step, nothing to undo.

## Are you a player or a modder?

<div class="grid cards" markdown>

-   :material-gamepad-variant: **Player**

    You are here because a mod you want lists ContentTool as a requirement. Install it, tick it on
    once, and forget it. On its own it changes nothing.

    [Install it](#install)

-   :material-hammer-wrench: **Modder**

    You have a texture, a model, a sound or a video and you want it in the game, packaged as a mod
    other people can install.

    [Start here](#for-modders)

</div>

## Install

1. Download `ContentTool-*.zip` from the
   [latest release](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/releases/latest).
2. Extract it and copy the `ContentTool` folder into `Phoenix Point\Mods\`, so you end up with
   `Phoenix Point\Mods\ContentTool\ContentTool.dll` and `meta.json` beside it.
3. Start the game, open **Mods** in the main menu, tick **Content Tool**.

**That zip holds exactly those two files** — the engine and its mod-manager entry. It is the whole
mod; there is nothing else to install and no tooling inside it. The **author's** tools —
`package.ps1` and the rest — are in the source repository instead, and
[§6 of the reference](guides/reference.md#where-packageps1-comes-from) says how to get them.

Requirements: the Phoenix Point base game. No other dependency.

### Which versions this is

| | |
|---|---|
| **ContentTool** | `1.0.0.0`. `ct_version` in the developer console prints the version and the build stamp of the DLL that is actually loaded — quote that line in a bug report. |
| **Phoenix Point** | Developed and measured against **`1.30.2.75117`** (`ReleaseCandidate2025`), Unity **2019.4.31f1**, the Steam build on Windows. The console command `version` prints yours. |
| **Other stores** | Epic and Game Pass are **untested**. Nothing in ContentTool is store-specific — it reads the installation's own bundles and Addressables catalog — but no one has run it there, so that is a reasonable expectation and not a claim. |
| **Other mods** | Measured with **15 other mods enabled at the same time, TFTV 1.1.4.5 included**, with no conflict. The one hard rule is one owner per shipped resource: two mods replacing the same bundle, key or sound is refused by name, never silently merged. |
| **A new game patch** | ContentTool reads the shipped bundles rather than patching them, so a game update cannot leave a broken file behind — the worst case is a `replace` row whose asset name no longer exists, which is reported by name in `Player.log`. |

**A content mod turns ContentTool on for you.** Content mods declare
`"Dependencies": [ "com.morgott.ContentTool" ]`, and ticking such a mod makes the game's own mod
manager switch ContentTool on with it. Both appear as separate toggles; leave ContentTool ticked —
a content mod that carries no code of its own **fails to load at all** while ContentTool is off.

Content is applied by the checkbox — at startup, and the moment you tick a content mod on
mid-session. It survives a restart because it is re-applied on every launch, never installed.

## For modders

The shortest path from a downloaded file to a mod folder other people can install:

1. **Make the folder** — `<Phoenix Point>\Mods\MyMod\meta.json`, declaring
   `"Dependencies": [ "com.morgott.ContentTool" ]`. Your project lives **in your own game's `Mods\`
   folder**, beside ContentTool, for the whole of authoring: that is where the bake command looks
   for it, and it is exactly where your player's copy will sit.
2. **Drop your sources in** — `Content\Meshes\*.glb .obj` (geometry that **replaces** a shipped
   mesh), `Content\Models\*.glb` (a whole **new** model — a weapon, a creature),
   `Content\Textures\*.png .jpg`, `Content\Audio\*.wav .ogg .mp3`,
   `Content\Videos\*.webm .mp4 .mov`, and `Icons\*.png` (inventory and UI images — top level, beside
   `Content\`, not inside it). Unconverted, as downloaded.
3. **Declare what they do** — `ppcontent.json`: one `"replace"` row per thing you swap, one
   `"publish"` row per thing you add. `ct_list` in the console tells you what to name in a
   `"replace"` row — [how to find a target](guides/reference.md#finding-a-target-ct_list).
4. **Bake, in game** — `ct_project MyMod` in the developer console, which opens with the
   `` ` `` key ([how](guides/reference.md#opening-the-developer-console)). This is the only step
   that needs the game, because Unity's decoders and *your own copy of the install* are what
   produce it.
5. **Package, with the game shut** — `.\package.ps1 -Project "<Phoenix Point>\Mods\MyMod"`, from a
   checkout of [the source repository](guides/reference.md#where-packageps1-comes-from).

The whole contract — what a mod folder may contain, what applies by itself, what the checkbox does
and does not undo per route, and the rules that are not negotiable — is in
[Shipping a content mod](SHIPPING-A-CONTENT-MOD.md).

**Then pick your rung.** [Recipes](guides/index.md) has one page per kind of content — texture,
sprite, icon, material, static mesh, animated model, sound, video, a whole new creature, a whole new
weapon — each with the folder tree, the manifest field by field, the commands and their real output,
the packaging step, and that rung's failure messages. The
[shared reference](guides/reference.md) is the field list every recipe assumes.

Step 4 above is the one people misread: **`ct_project` and `ct_sound bake` are AUTHORING commands.**
You ship the baked output in `Dist\`, and your player just ticks the mod on — measured on four demos
with no bake ever run in the install.

Content mods are distributed **from GitHub**, as a release zip. ContentTool itself is the only thing
published to the Steam Workshop.

## Limits, stated plainly

- **A replaced sound cannot be taken back in the same session.** Ticking a sound mod off does not
  restore the shipped sound: unloading the replacement bank makes Wwise go *silent* rather than
  vanilla (measured). ContentTool leaves the bank loaded and says so in the log. Restart the game —
  that is a clean undo, because nothing was installed anywhere.
- **One shipped resource has exactly one owning mod.** Two mods replacing the same bundle, the same
  Addressables key or the same sound is refused by name (the lower mod id keeps it), never silently
  last-writer-wins. The refusal is a line in `Player.log`.
- **A def `"guid"` shared by two mods is the one collision that is not refused.** Bundles, catalog
  keys and sounds are all refused by name when two mods claim the same one. A new weapon's or
  creature's `"guid"` is not: the def repository is keyed on it, so the first mod enabled wins and
  every later entry with that guid silently resolves to *its* def — the log says
  `already built this session` and the second mod's weapon never exists. Generate the guid at random
  per def and never copy one out of a recipe.
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

```text
%USERPROFILE%\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\Player.log
```

Search it for `ct_`. The line names the mod, the resource and the reason — *disabled in the mod
manager*, *no meta.json*, *already owned by …*. Attach that line to a bug report.

## License

[CC BY-NC 4.0](https://creativecommons.org/licenses/by-nc/4.0/) — Morgott. Non-commercial,
attribution required. Content mods you build with it are yours; this license covers ContentTool
itself.

