# RELEASE — standalone repo, and the Steam Workshop draft

> Prepared, **not submitted**. Publish order is decided: **GitHub first, Steam Workshop later**.
> Nothing here has been pushed, and no repository has been created.

## 1. Standalone repository

Target: `UberMorgott/PhoenixPoint-Mod-ContentTool` (same pattern as `-PerkOracle`, `-Cortex`,
`-TheTurned`, `-Multiplayer`). ContentTool currently lives *inside* the monorepo
(`E:\DEV\PhoenixPoint\ContentTool`, 256 tracked files) and is committed to the outer `main`.

**The tree is already self-contained** — `src\`, `demos\`, `docs\`, `lib\`, `data\`, `tests\`,
`tools\`, `ContentTool.csproj`, `meta.json`, `deploy.ps1`, `package.ps1`, `autogate.ps1`,
`ILRepack.targets`, `LICENSE`, `README.md`. Extraction is `git subtree split` (or a copy + fresh
`git init`, the way `Cortex\` was staged) plus the four fixes below.

What must move or change before it stands alone:

| # | Item | Why |
|---|---|---|
| 1 | `.gitignore` — **done** | It only listed `dist-package/`. `bin/`, `obj/`, `.serena/`, `Console.log` etc. were covered by the OUTER `.gitignore` and would have been committed in a standalone repo. Now covered locally. |
| 2 | Absolute monorepo paths in `deploy.ps1` — **done** | Three hardcoded `E:\DEV\PhoenixPoint\ContentTool\...` (lines 12, 14, 25), now `$PSScriptRoot`-relative. `package.ps1` and `autogate.ps1` were re-checked and are clean (both already resolve through `$PSScriptRoot`). |
| 3 | Outer-repo doc references — **done** | `docs\README.md` now names the two monorepo notes as NOT part of this repository and inlines the handful of facts that matter (product shape; the frozen `LoadBankMemoryCopy` audio decision, Wwise 2021.1.0/BKHD 140, t=0.255 mod load, `m_DisableAudio`). The documents themselves are not copied in. |
| 5 | `meta.json` description — **done** | It said "Type ct_version in the developer console", an author's line. Replaced with the player-facing text in §2 below. |
| 4 | Outer `.gitignore` already anticipates it | Line 57 ignores `/ContentTool-standalone/`, so a staging extraction beside the monorepo is already gitignored. |

Not required to move: `lib\*.dll` (AssetsTools.NET, NLayer, NVorbis — their licenses ship beside
them), `lib\classdata.tpk`, `lib\ppids.bin`, `data\pp_wwise_index.json`. All are ours to
redistribute and are already tracked. **No Phoenix Point asset is in the tree**, which is the
condition a public repo has to meet.

## 2. Steam Workshop — the draft

App id **839770**. Nothing below has been uploaded.

### Tags

**`Gameplay`, and only that.**

Phoenix Point's Workshop tag set is FIXED: `Geoscape`, `Tactical`, `Difficulty`, `Gameplay`,
`Augments`, `Bionics`, `Mutations` (plus DLC tags). There is no `Tools`, no `UI`, no
`Quality of Life` — an unknown tag makes `SubmitItemUpdate` fail outright, so inventing one is not
an option. ContentTool is an engine: it is not geoscape-only, not tactical-only, and changes no
difficulty, so `Gameplay` is the only honest member of the set. Do not add `Tactical` because the
demos spawn a creature — the tag describes the mod, and this mod on its own changes nothing.

### Title

`Content Tool`

### Description draft

```
ContentTool is the content engine other Phoenix Point mods build on. It lets a mod replace what the
game ships - textures, models, materials, animation curves, sounds, videos, whole asset bundles -
and add content the game never had, such as a new creature or a new weapon.

On its own it changes nothing. You are most likely here because a mod you subscribed to requires it.
Subscribe, and leave it enabled: a content mod declares ContentTool as a dependency, and the game's
mod manager turns it on for you.

YOUR GAME FILES ARE NEVER TOUCHED
Everything happens in memory while the game runs. Nothing is unpacked, patched, backed up or copied
into your Phoenix Point installation. Unsubscribing leaves your install exactly as a clean one - no
uninstaller, no repair step, nothing to undo.

WHAT TO KNOW
- Content is applied when you tick a mod on, and again on every launch. There is no install step.
- A replaced SOUND cannot be taken back in the same session. Unticking leaves it loaded until you
  restart - unloading it would make the game silent, not vanilla. Restarting is a clean undo.
- One shipped resource has exactly one owning mod. Two mods claiming the same one is refused by
  name, in your player log, never silently.

If a mod's content does not appear, its reason is a named line in Player.log - search it for "ct_".

Source, documentation and bug reports:
https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool
```

### Mod-manager description (`meta.json`)

The in-game list row shows **only the first line**, clipped at roughly 110 characters
(`ModItemController.cs:63`); hovering shows the whole text. **Applied** — `meta.json` now carries:

```
Content engine for other mods - replaces and adds game content in memory. Never writes to your game files.
Mods that need it list it as a dependency and enable it for you. On its own it changes nothing. The ct_* console commands are for mod authors.
```

(first line 105 chars, inside the ~110 the list row shows).

### Still blocking a submission

1. **Preview image — does not exist yet.** Steam requires one and the tooling refuses without it.
   Constraint verified from our own publish tooling: **≤ 1 MB**, JPG or PNG. The de-facto shape used
   by the other mods is `1024x1024` JPG (`PerkOracle\image\steam_preview.jpg`, 292 KB). Target
   `ContentTool\image\steam_preview.jpg`.
2. **No publish tooling here.** `PerkOracle\workshop\` holds the whole rig — `pack-dist.ps1`,
   `oracle.vdf` (SteamCMD item descriptor), `steamugc\publish_ugc.py`, `update.ps1`. Copy and retarget
   when the time comes; the content folder must be the **packaged** mod (`ContentTool.dll` +
   `meta.json`), never the repo.
3. **First publish is GUI-only.** The `publishedfileid` is created by SnapshotGames' PPWorkshopTool;
   SteamCMD / `publish_ugc.py` only UPDATE an existing id afterwards.
4. **Per-language gotcha** (bit both other mods): after `SetItemUpdateLanguage` you must also call
   `SetItemTitle` again, or the non-English pages publish with an empty heading.
5. GitHub release first — the README's install instructions point at
   `releases/latest`, which has to exist.
