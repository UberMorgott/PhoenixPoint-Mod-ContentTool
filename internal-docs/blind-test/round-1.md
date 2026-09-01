# Source-blind test — round 1 (2026-08-28)

> **Not published.** `mkdocs.yml` excludes `blind-test/` from the build, and that exclusion is the
> reason the test is worth anything: the agent under test may read only what a stranger on the
> internet can read.

## The setup

An agent that had never seen this codebase was given **only the published documentation site** and
told to build a texture-replacement mod. It could not finish. Its report is the specification for
this round; every item below is a **documentation defect**, not a user error.

Fixes were verified against a real game: `D:\PP-Instance2`, ContentTool `1.0.0.0`
`build=24cadf55`, one launch, all output captured from the running game rather than written from
memory.

## The five defects

### 1. HARD STOP — nothing said how to open the developer console

**What the site said.** The shared reference, on the two authoring commands:

> Both commands run in the game's **developer console**, because Unity's decoders and *your own copy
> of the installation* are what produce the output.

…and no page anywhere said how to open one, or whether something had to be enabled first. Every
recipe depends on that keystroke and none of them named it.

**What it says now.** A new *Opening the developer console* subsection in the shared reference:
`` ` `` toggles it, `/` opens it when closed, `Esc` closes, `Enter` submits; nothing needs enabling
first because the game's mod manager unlocks the console while it initialises; and the shipped
unlock code `↑ ↓ ← → S N A P S H O T` as the fallback. The home page's step 4 and the recipe index
both link to it.

**Rests on a real measurement.** The console's lock flag was read back live out of the running
game — `disable_console_access` → **`false`**, on an install with mods on, at the main menu. The
unlock code and the key bindings come from the game's own console input handler.

### 2. No documented way to FIND a target to replace

**What the site said.** The texture recipe, under the two facts about size:

> **Check whether the shipped texture is shared.** Aim a replacement at a map that rides a shared
> atlas and you repaint half the armoury.

No method was given, and `ct_extract tex <bundle> <asset>` takes both names as *input*. The engine
ships the discovery verb `ct_list` and the site never mentioned it.

**What it says now.** `ct_list` is documented in the shared reference (all four forms, both filters,
the 60-hit cap) and the texture recipe opens with a new **§0 Find the two names** carrying the real
transcript. The sharing check is now answered by that same listing.

**Rests on a real transcript** — captured, not written:

```text
> ct_list bundles acidworm
1 bundle(s) match 'acidworm' in ...\StreamingAssets\aa\StandaloneWindows64
  aln_acidworm_assets_all.bundle

> ct_list assets aln_acidworm_assets_all.bundle Texture2D
aln_acidworm_assets_all.bundle: 10 of 276 assets match type~'Texture2D' name~''
  ... acidworm_low_albedo ... plus six fireworm_* textures in the same bundle
```

That last detail is the sharing lesson itself: the acidworm's bundle also holds the fireworm's maps.

### 3. Contradiction about where a project lives

**What the site said.** The home page:

> 1. **Make the folder** — `Mods\MyMod\meta.json`

against every packaging example on the site:

> `.\package.ps1 -Project D:\MyMod`

and `ct_project <bare name>`'s resolution root was never stated at all.

**The truth**, read out of the resolution code and then confirmed in game: the bare name resolves to
the **sibling mod folder** `<Phoenix Point>\Mods\<Name>` first, and falls back to
`<Phoenix Point>\Mods\ContentTool\<Name>` only when no such mod folder exists. So the project must
live under `Mods\` while it is being authored, and `package.ps1 -Project` takes that same folder as
a path.

**What it says now.** A *They take a bare NAME* subsection in the reference states both branches;
the home page's step 1 says the project lives in the game's own `Mods\`; and all nine `package.ps1`
invocations across the site were changed from `D:\<Name>` to `"$PP\Mods\<Name>"` with `$PP` defined
in the same block.

**Rests on two real runs.** The hit:

```text
> ct_project NoDepTexture
project 'morgott.demo.nodeptexture' at D:\PP-Instance2\Mods\NoDepTexture: ...
```

and the miss, which names the fallback root out loud:

```text
> ct_project NoSuchModXyz
ct_project THREW System.IO.FileNotFoundException: no ppcontent.json in D:\PP-Instance2\Mods\ContentTool\NoSuchModXyz
```

### 4. `package.ps1` invoked on six pages, never sourced

**What the site said.** The install section implied the download is the whole story —

> Extract it and copy the `ContentTool` folder into `Phoenix Point\Mods\`, so you end up with
> `Phoenix Point\Mods\ContentTool\ContentTool.dll` and `meta.json` beside it.

— while six pages then told the reader to run `.\package.ps1`, a file that is in neither of those two.

**What it says now.** The install section states plainly that the release zip holds exactly
`ContentTool.dll` + `meta.json` and nothing else, and a new *Where `package.ps1` comes from*
subsection in the reference sends an author to the source repository (clone, or *Download ZIP*, or
the *Source code (zip)* asset on a release), says the script runs from that checkout without being
copied anywhere, and names its one prerequisite: the .NET SDK, because the script compiles the
packaging rule and the author's own `.csproj` rather than reimplementing them.

### 5. A quoted transcript from the wrong demo

**What the site said.** In the texture recipe, as the texture bake's success line:

> `ct_project: ALL PASS - ...\Dist\CustomCreature.bundle`

— the creature demo's output, on the texture page, against the recipe index's own promise of
*"the console commands with the output they really print"*. Sample output for `ct_extract tex`,
`ct_extract mesh` and `ct_route7 status` was missing from the site entirely.

**What it says now.** The texture recipe carries the real 12-line `ct_project NoDepTexture`
transcript ending in `ct_project: ALL PASS - D:\PP-Instance2\Mods\NoDepTexture\Dist\NoDepTexture.bundle`,
plus captured `ct_extract tex`, `ct_extract mesh` and `ct_route7 status` output, and the shared
reference gained the two `ct_extract` lines as well. The generic form in the reference now reads
`<your project folder>\Dist\<YourMod>.bundle` instead of naming a demo the reader is not building.

## Pages changed

`docs\index.md` · `docs\guides\index.md` · `docs\guides\reference.md` · `docs\guides\textures.md` ·
`docs\guides\materials.md` · `docs\guides\meshes.md` · `docs\guides\sounds.md` ·
`docs\guides\videos.md` · `docs\guides\creature.md` · `docs\guides\weapon.md`

## What round 2 should watch for

The four gaps this round could not close, because the blind agent never got far enough to hit them:

- Nothing on the site shows a **failed** bake being read and fixed — every transcript is a success.
- `ct_sound bake`'s output is documented from the sounds recipe's own text, not from a fresh capture.
- The icon rung asks an author to write an assembly and shows no complete file.
- No page states what to do when `ct_list` finds the asset but the replacement still does not appear;
  the failure table names the symptom but the reader has no next command. `ct_route7 status` is now
  the answer on the texture page — it is not yet on the other rungs.
