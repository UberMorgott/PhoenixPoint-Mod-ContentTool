# Shipping a content mod

> What a mod folder must contain so that a player who switches it on gets the content, with no
> console command or authoring step.

## The contract

A shipped content mod is a folder under `Mods\<YourMod>\`, or the equivalent Workshop subscription,
containing:

| File | Required | What it does |
|---|---|---|
| `meta.json` | **yes** | Makes you a mod at all. **Declare `"Dependencies": [ "com.morgott.ContentTool" ]`** — not because ContentTool reads it (it never does; it asks the mod manager who is ON), but because it makes the manager switch ContentTool on for your player and load it before you. Without it a player can tick your mod with ContentTool off, and a code-less mod then fails to load outright with an error that names the loader instead of the missing prerequisite. `package.ps1` refuses to build a release without the line. The measurement is *What a mod that declares NO dependency really does* below. |
| `ppcontent.json` | **yes, always** | The manifest. Its PRESENCE is the declaration — and the tools take that literally: `package.ps1` refuses a folder without one *before* it looks at what the mod does (`REFUSED: there is no <path>\ppcontent.json`), and so does the bake. Two fields are required in every one of them, whatever the mod is: `"id"` matching `meta.json`'s `ID`, and `"bundle"` naming the bundle you produce — declared even by a project that never builds one. |
| `Content\...` and `Icons\...` | as needed | Images under `Textures\` or `Icons\`; replacement geometry under `Meshes\`; new models under `Models\`; audio under `Audio\` or `Audio\Replace\`; video and subtitles under `Videos\` and `Subtitles\`. Exact formats are in the [shared reference](guides/reference.md#1-the-folder). |
| `Dist\Sounds\<mediaId>.bnk` | for sound replacement | The **already-baked** bank you ship. `ct_sound bake` produces it; the player never bakes. |
| `Dist\<YourMod>.bundle` | for bundle / mesh / texture content | Your mod's **own** bundle, baked by `ct_project <YourMod>` and shipped. See [the distinction from a patched game bundle](#your-bundle-and-a-patched-game-bundle-are-different-files). |
| `<YourMod>.dll` | only for behaviour | A hotkey, a trigger, a patch. Content alone needs **no code**. |

A player runs no installer, console command or bake.

## From an empty folder to a release

This is the complete order of operations. The individual [recipes](guides/index.md) supply the
manifest rows for each content type.

1. **Create the project folder.** Work in `<Phoenix Point>\Mods\MyMod\`. Phoenix Point discovers
   only top-level folders under `Mods\`.
2. **Write `meta.json`.** Give the mod a stable `ID`, set `AssemblyName` to `""` unless you ship a
   DLL, and declare `"Dependencies": [ "com.morgott.ContentTool" ]`.
3. **Write `ppcontent.json`.** Its `"id"` must match `meta.json`, and `"bundle"` names your output.
   Add the `"replace"`, `"publish"`, `"sounds"`, `"creature"` or `"weapons"` rows required by your
   recipe. Copying the nearest [demo](demos.md) is safer than starting from memory.
4. **Add the source files.** Put them in the exact `Content\...` or `Icons\` folder named by the
   recipe. Scale, axes, media IDs and asset names are the fiddly part; use `ct_list` to discover
   targets and the fitting tools supplied by the relevant demo.
5. **Bake in game.** Enable ContentTool and your project, open the developer console, and run:

   ```text
   ct_project MyMod
   ct_sound bake MyMod        # also run this when the mod replaces shipped sounds
   ```

   For a project with its own assets, `ct_project MyMod` writes `Dist\MyMod.bundle`; a manifest-only
   material tweak correctly has no bundle of its own. `ct_sound bake MyMod` writes replacement banks
   under `Dist\Sounds\`. Do not continue past a refusal or failure line.
6. **Close the game and package the project.** From a checkout of the ContentTool repository, run:

   ```powershell
   .\package.ps1 -Project "<Phoenix Point>\Mods\MyMod"
   ```

   The default output is `dist-package\MyMod`. The script builds your DLL when a `.csproj` exists,
   copies only release files, and refuses missing sound bakes or redistributed game data.
7. **Test the staged folder as a player would.** Install `dist-package\MyMod` in a test setup, tick
   ContentTool and the mod on, and exercise the changed asset without running an authoring command.
   Use `ct_version` to record the loaded build, `ct_route7 status` for shipped-bundle replacements,
   and `ct_catalog status` for published keys. Check `Player.log` as well as the screen or sound.
8. **Ship the folder you tested.** Zip the `MyMod` folder itself, so the archive contains
   `MyMod\meta.json`, and publish that archive or the equivalent Workshop item.

## What applies by itself, and what does not

ContentTool runs one gated pass one frame after it is enabled and, for **every mod the manager says
is ON**, applies:

- **`Dist\Sounds\*.bnk`** → loaded into Wwise in memory. Replaces a shipped sound by `mediaId`.
- **`ppcontent.json` → `"replace": [ { "video": "<clip stem>", "asset": "<shipped path>" } ]`** →
  the clip is served out of **your own folder**, in memory. Omit `"asset"` and the row is ADDED
  under a derived RuntimeKey, which is printed for you to paste into your own def.

Both are **per session**: nothing is written into the game install. This runs at startup **and when
the player ticks your mod on mid-session** — same path, driven by `ModEntry.SetEnabled`.

### Unticking it mid-session — what actually happens, per route

The checkbox is not symmetric for every route, and ContentTool says which is which instead of
pretending:

| Route | Ticked ON mid-session | Ticked OFF mid-session |
|---|---|---|
| video (`"replace"` → `"video"`) | served immediately | **handed back immediately**, the shipped clip resolves again, no restart |
| sound (`Dist\Sounds\*.bnk`) | loaded immediately | **stays until a restart.** Measured: after `UnloadBank` the event dies at 17 ms instead of falling back to the shipped media — Wwise goes SILENT, it does not go vanilla. Silence is a broken game, not a restored one, so the bank is left alone and the log says so. Nothing was written to the install, so the restart is a clean undo. |
| route vii (`"replace"` → `"bundle"`) | redirected immediately, in memory | redirection dropped immediately, no restart |
| route iii (`"publish"` keys) | keys published immediately | keys un-published immediately, no restart |
| new defs (`"weapons"`, `"creature"`) | built immediately, when your DLL's `OnModEnabled` runs | **they stay until the game is restarted.** Nothing removes a def from the def repository once it is in, and neither demo assembly declares an `OnModDisabled`. The mod's *model keys* do go on the checkbox (route iii above), so the half that comes back is the art, not the weapon — un-tick a weapon mod mid-session and its gun is still in the repository with nothing to wear. Restart for a clean undo; nothing was written anywhere, so a restart is the whole of it. |

Both Addressables routes run from the mod-manager checkbox. The old "apply, RESTART, verify /
revert" phase pair is gone, and with it the
`ct_route7 dryrun|verify|revert|stacktest` and `ct_catalog revert|selftest` verbs: running one now
prints a REMOVED line pointing at `ct_route7 status` / `ct_catalog status`. Dev-only console entry
points that remain: `ct_route7 apply <YourMod> | status`, `ct_catalog apply <YourMod> | verify | status`.

## Your bundle and a patched game bundle are different files

`ct_project <YourMod>` overwrites your own `Mods\<YourMod>\Dist\<YourMod>.bundle`. That is the
portable output you test and ship.

When a `"replace"` row targets an asset inside a bundle shipped by Phoenix Point, ContentTool cannot
redistribute that bundle: it is the player's game data and may be hundreds of megabytes. Instead, it
builds a patched copy from that player's installation and redirects the live load to the copy. The
cache lives here, outside the game folder:

```text
%USERPROFILE%\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\ContentTool\Patched\
```

The first enable can therefore pause while a large target bundle is copied and patched. Never put
that cache in a release; `package.ps1` refuses it.

## What you see in `Player.log` when it worked

```
ct_content: 'morgott.demo.materialtweak' is ON in the mod manager, so its live registrations were installed at startup.
1/1 bundle(s) redirected LIVE for 'morgott.demo.materialtweak' - nothing was written to the game installation
ct_video: 12 content project(s) serving in memory, 3 skipped
  IntroVideo: 1 clip(s) served in memory from D:\PP-Instance2\Mods\IntroVideo; nothing in the install was written
  MaterialTweak: 0 clip(s) served in memory from D:\PP-Instance2\Mods\MaterialTweak; nothing in the install was written
  NoDepTexture: skipped, disabled in the mod manager
ct_sound: 6 shipped replacement bank(s) loaded from D:\PP-Instance2\Mods, 0 failed, 0 skipped
  MenuMusic\208540756.bnk 24583864 B -> AK_Success ...
```

Three passes print there and they count different things — the bundle line is the one a texture or
mesh mod is proved by, and `0 clip(s) served in memory` on such a mod is correct, because that line
counts **video** rows. The full rule is in
[the reference](guides/reference.md#which-success-line-is-yours-there-are-three-and-they-count-different-things).

Every refusal is a named line. If your content does not appear, the reason is on that line:

- `skipped, disabled in the mod manager` — the player has you switched off. Working as intended.
- `skipped, the mod manager never discovered it (no meta.json)` — you shipped no `meta.json`, so the
  manager cannot know you and nothing of yours is applied. Add one.
- `skipped, the mod manager could not be read` — no readable roster; nothing at all is applied. We
  never fall back to "apply everything", because that is the bug this gate closed.

## What a mod that declares NO dependency really does — measured

The `Dependencies` line above is called mandatory, and until 2026-08-28 nobody had run the case
where it is missing: every demo declares it. The fixture that does is `demos\NoDepTexture` — one
texture replacement, two `meta.json` variants, four launches.

**The prediction, written down before the runs** (`ModMeta.Dependencies` is
`public string[] Dependencies = new string[0]` read through `JsonConvert`, so an absent field and an
explicit `[]` both end up as `string[0]`): the two variants would be indistinguishable; the mod
would be **enablable** in every cell, because `ResolveDependencies` finds nothing missing; enabling
it would **not** switch ContentTool on; with ContentTool ON the replacement would apply anyway; and
with ContentTool OFF **nothing would happen and nothing would be printed** — a mod that reads ON and
silently does nothing.

**Measured.** Rig `D:\PP-Instance2`, ContentTool `1.0.0.0 build=9872a6b9`, from the main menu, mod
roster cut to PPBridge + the fixture (+ ContentTool where the column says ON). The probe is the
Acidworm's own albedo read off the engine —
`Addressables.LoadAssetAsync<GameObject>("02_Bodyparts/ALN_Acidworm_BodyAll_Ready.prefab")` →
`Renderer.sharedMaterial.mainTexture` → `width`/`format`/`mipmapCount`. Shipped is
**1024x1024 DXT1, 11 mips**; the fixture's checker bakes to **256x256 RGBA32, 1 mip**.

| `meta.json` | ContentTool | Mod roster | `acidworm_low_albedo` reads | Verdict |
|---|---|---|---|---|
| `Dependencies` **omitted** | **ON** | ContentTool loaded; `ct_content: 'morgott.demo.nodeptexture' is ON in the mod manager`; `1/1 bundle(s) redirected LIVE` | **256x256 RGBA32, 1 mip** | **applies** |
| `Dependencies` **omitted** | **OFF** | ContentTool discovered, never loaded; `[ERROR] [Mods] Failed to enable mod 'morgott.demo.nodeptexture', loader 'Default'` → `InvalidOperationException: Loader.LoadMod() returned null!` | **1024x1024 DXT1, 11 mips** | **does not apply, and does not load** |
| `"Dependencies": []` | **ON** | identical to row 1, byte for byte | **256x256 RGBA32, 1 mip** | **applies** |
| `"Dependencies": []` | **OFF** | identical to row 2, same exception | **1024x1024 DXT1, 11 mips** | **does not apply, and does not load** |

### What that changes

- **Omitted and `[]` are the same input.** Confirmed on all four cells, in both directions. The
  prediction held here.
- **The dependency does not gate the CONTENT.** With ContentTool on, the replacement applies exactly
  as if it had been declared — ContentTool's startup pass asks the mod manager who is ON and never
  reads anybody's `Dependencies`. What the line actually buys is the **auto-enable**
  (`ModManager.TryEnableMod:200-207` turns ContentTool on for you) and the ordering that comes with
  it. That is worth having, and it is not what "mandatory" implied.
- **The prediction was WRONG about the OFF case, and the truth is better.** A content-only mod has
  no assembly, so Phoenix Point's own `Default` loader returns `null` from `LoadMod()` and the mod
  **fails to enable outright**. Code-less mods are loadable only because ContentTool patches that
  path (`ct_content: code-less content mods are loadable, so the mod manager's switch governs
  them`) — and with ContentTool off there is no patch. So the feared silent no-op does not happen:
  the mod is not quietly ON-and-inert, it is refused, with a named `[ERROR]` line in `Player.log`.
- **It is still not a good failure.** The error names the loader, not the missing prerequisite: a
  player reads `Failed to enable mod '<id>', loader 'Default'` and has no way to learn that the
  answer is "switch ContentTool on". Declaring the dependency is what turns that dead end into an
  auto-enable, which is the real reason the rule stands.
- One more consequence worth stating: a content mod that ships **its own DLL** would load fine here
  and then do nothing at all, because the loader failure is the only thing standing in for the
  missing warning. That case is untested.

## Rules that are not negotiable

- **Never reference an assembly nothing loads for you.** `PPModLoader` loads a mod with
  `Assembly.Load` over raw bytes and installs no `AssemblyResolve` handler, so the CLR can only
  satisfy your references from assemblies already in memory. A reference it cannot satisfy fails the
  mod load — and Phoenix Point answers a failed mod load by rewriting `MOD_ACTIVATED` empty, silently
  disabling *every* other mod on the machine (measured 2026-08-13). The reference that actually does
  this is a Unity module under `PhoenixPointWin64_Data\Managed\` which `ModSDK\` does not ship:
  referencing `UnityEngine.VideoModule` took the whole mod list down, and so did the `Managed\`
  reference the video demo carried before that. **Reference only what `ModSDK\` ships** and reach
  anything else **by reflection**.
- **`ContentTool.dll` is not one of those.** You may reference it — the weapon and creature recipes
  do, and both are confirmed in game — because `meta.json` declares
  `"Dependencies": [ "com.morgott.ContentTool" ]` and the mod manager enables and loads a dependency
  before its dependents, so ContentTool is already in memory when your code first mentions its types.
  Two conditions, both required: the dependency line in `meta.json`, and `<Private>false</Private>`
  on the reference so a second rival copy is not loaded beside the player's.
  The real fragility here is **version skew, not resolution**: `Dependencies` carries only an id and
  no minimum version, so an *older* ContentTool satisfies the dependency and the load order while
  lacking a type or method you referenced — a `TypeLoadException` or `MissingMethodException` at
  runtime. Call `CatalogLive.Register` **by reflection** when you want your mod to log and degrade
  instead; a hard reference cannot.
- **Ship your own media only.** Never redistribute a Phoenix Point asset. `package.ps1` refuses a
  release containing a shipped bundle, backup, catalog or patch cache.
- **Your folder must be top-level under `Mods\`.** `PPModLoader` discovers only top-level
  directories holding a `meta.json` (`PPModLoader.cs:29-46`); a folder nested inside another mod can
  never be listed or switched off.

## Where the engine half lives

Inside ContentTool, and you do not need to read it to ship a mod: discovery and the enabled /
disabled / unknown gate are one rule shared by every route, and the two routes that apply by
themselves — sound banks and video — hang off that same gate. Everything a mod author touches is on
this page and in the guides.

*(This page is the modder's contract. The file-by-file map of the engine belongs with the engine's
own architecture notes, which are not part of the published documentation — naming implementation
files here would also hand them to the source-blind documentation test.)*
