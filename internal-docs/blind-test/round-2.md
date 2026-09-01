# Source-blind test — round 2 (2026-08-28)

> **Not published.** `mkdocs.yml` excludes `blind-test/` from the build, and that exclusion is the
> reason the test is worth anything: the agent under test may read only what a stranger on the
> internet can read.

## The setup

An agent that had never seen this codebase was given **only the published documentation site** and
told to build a texture-replacement mod. Unlike round 1 it finished: it shipped `AcidChecker`, a
content-only mod that repaints the Acidworm's body albedo with a 256×256 magenta/acid-green
checkerboard reading *MOD OK*, plus `COMMANDS.md` (a cold-start command list with the output it
predicted for every step, each line either quoted from the site or marked `GUESS:`) and `FINDINGS.md`
(ten documentation defects, in its own words).

It had no game, so it could not bake, and shipped no `Dist\`.

Rig for everything below: `D:\PP-Instance2`, Phoenix Point **1.30.2.75117** (`ReleaseCandidate2025`,
Unity 2019.4.31f1), 16 mods enabled including TFTV 1.1.4.5, driven through `PPCLI\ppcli.ps1 connect`.
**Three launches.** Launch 1 and 2 on ContentTool `build=66f12aa7` and `build=0dedc643` ran the blind
mod; launch 3 recaptured the `NoDepTexture` transcript on the fixed build. The mod folder was taken
**exactly as delivered** — not one byte of it was edited — and `blindtest.acidchecker` was swapped
into the profile's `MOD_ACTIVATED` in place of `morgott.demo.nodeptexture`, which owns the same
shipped bundle. Both were restored afterwards and the mod was removed from the install.

## PART 1 — the verdict: it worked, unaided, with **zero** fixes

**Measured, live, launch 2, one run:**

| Probe | Value |
|---|---|
| `Addressables.LoadAssetAsync<GameObject>("02_Bodyparts/ALN_Acidworm_BodyAll_Ready.prefab")` → renderer → `sharedMaterial.mainTexture` | name `acidworm_low_albedo`, **256×256, `RGBA32`, `mipmapCount=1`** |
| CONTROL, same run — the shipped bundle read straight off disk, `ct_extract tex aln_acidworm_assets_all.bundle acidworm_low_albedo` | **`w=1024 h=1024 fmt=10` (DXT1) `mips=11`** |

The blind agent's texture is what the engine is holding. Nothing was fixed, nothing was patched,
no file of its was touched.

**It worked before its own step 5 ever ran.** Its `COMMANDS.md` treats `ct_project AcidChecker` as
the step that makes the mod real. In fact the mod was already live at the main menu of launch 1: with
no `Dist\` and no patched copy on disk, ContentTool baked both from *this machine's* installation the
moment the manager reported the mod ON, and redirected the live location at the result —

```text
ct_content: 'blindtest.acidchecker' is ON in the mod manager, so its live registrations were installed at startup.
project 'blindtest.acidchecker' at D:\PP-Instance2\Mods\AcidChecker: 1 texture(s), 0 mesh(es), 0 model(s), 0 video(s), 0 sound(s), 1 replacement(s)
WROTE ...\ContentTool\Patched\blindtest.acidchecker\aln_acidworm_assets_all.bundle 5001329 B ... (shipped source is 4986241 B)
WROTE D:\PP-Instance2\Mods\AcidChecker\Dist\AcidChecker.bundle 179456 B as blindtest_acidchecker / CAB-blindtest_acidchecker
1/1 bundle(s) redirected LIVE for 'blindtest.acidchecker' - nothing was written to the game installation
```

That is the player path, and it makes its own B1 (*"a mod folder is not shippable until it has been
inside a running Phoenix Point once"*) wrong in the direction nobody had measured.

### Predicted vs actual, step by step

| Step | Predicted | Actual |
|---|---|---|
| 3 `ct_list bundles acidworm` | 2 lines, verbatim from the site | **exact match** |
| 3 `ct_list assets … Texture2D` | 11 lines, verbatim | **exact match**, all ten pathIds |
| 4 `ct_extract tex` | verbatim, `<you>` substituted | **exact match** |
| 5 `patch … <- acid_checker 256x256` | its own file, documented shape | **exact match** |
| 5 patched-copy size `4999442 B` (`GUESS:`) | copied from the site's fixture | **5001329 B** — its own PNG, as it predicted it would differ |
| 5 hashed name `aacab30947f9c740247e47cc63254879.bundle` (`GUESS:`) | expected to differ | **identical** — the name is the shipped bundle's identity, not the mod's |
| 5 `Dist` bundle `177572 B` (`GUESS:`) | copied from the fixture | **179456 B** |
| 5 identity `as blindtest_acidchecker / CAB-blindtest_acidchecker` (`GUESS:`, dots→underscores) | inferred from one example | **exactly right** |
| 5 no `deleted stale …` on a first bake (`GUESS:`) | inferred | **right** — absent on the first bake, present on the second |
| 5 `TEX PASS … px[0,0]=255,0,255,255` | its checker's top-left texel | **`px[0,0]=0,255,64,255`** — its own image's corner is green, not magenta. The only wrong prediction about its own file |
| 5 — | three `A6-ctl-…` control lines | **unpredicted, and unpredictable**: the tool's audio-decoder self-checks print on every bake and the site's transcripts do not show them |
| 5 `copies ready in … - install them with: ct_route7 apply AcidChecker` | quoted from the site | printed as quoted — **and it is the defect A2 below**; the line now reads differently |
| 6 `ct_route7 status`: `live bundle redirections: 1`, `live published keys: 0` (`GUESS:`) | its own rig would hold one | **3 and 3** — this rig runs eight demos; its row was present and exact, `crc 1151466550 -> 0` **as guessed** |
| 6 `1/1 bundle(s) redirected LIVE` | quoted from the site | **the site's quote is stale**; the real line carries `for '<id>' - nothing was written to the game installation` |
| 7 `PACKAGED <n> file(s), <n> B into dist-package\AcidChecker` | site's placeholder shape | **`PACKAGED 6 file(s), 10133 B`** — and it packaged its un-baked project without complaint, settling B2. The default `dist-package\` path was not exercised: the run passed an explicit `-Out` |
| 8 zip layout | flagged as unresolvable | **its flag was right**; the two instructions could not both hold |

**Guess accuracy: of the 7 lines it marked `GUESS:`, four came back right** — the hashed bundle
name, the absent `deleted stale` on a first bake, the dots-to-underscores bundle identity, and the
crc in `ct_route7 status`. **Two were wrong and it had flagged both as rig-specific** (the byte
sizes, the redirection counts). The last two were not exercised: the step-0 `Test-Path` output, and
the default `dist-package\` folder name.

**One of its ten reports is false.** A7 claims the textures page's in-page table of contents drops
the icon rung. Read out of the delivered `guides/textures/index.html`: `md-nav--secondary` carries
`The icon rung` and all six of its subsections. Nothing was changed for it.

## PART 2 — the ten defects

### 1. A1 — the zip layout contradicted the install instruction *(fatal, settled by running it)*

> §6: "Zip the CONTENTS of that folder (**meta.json at the top of the archive**)"
> §7: "Unzip it into `Phoenix Point\Mods\`, so they end up with `Mods\MyMod\meta.json`"

**Ran it.** `package.ps1` stages a *folder* whose root holds `meta.json`; both zip layouts install
correctly *if* the player unzips to the matching place, so this was never a tool bug — it was one
instruction contradicting the other, with the failure silent (an archive rooted at `meta.json`
extracted into `Mods\` leaves `Mods\meta.json`, which the loader never discovers, and no error is
printed because nothing was discovered).

**The one true instruction, now everywhere: zip the FOLDER.** §6 carries a worked
`Compress-Archive` yes/no pair and the reason; §9 and the textures recipe agree; §7 was already
right. **The engine's own success message was changed to match** (`src\Project\Package.cs`), because
it was the source of the "contents" half:

```text
Zip the FOLDER itself, so the archive holds MyMod\meta.json, and upload it. The player unzips it
into Mods\ (ending up with Mods\MyMod\meta.json) or subscribes on the Workshop; the mod manager
enables ContentTool for them because meta.json declares it.
```

### 2. A2 — the bake told authors to run a verb the site says does not exist

> textures §3: `copies ready in … - install them with: ct_route7 apply NoDepTexture`
> recipes index: "There is no `apply`, no `revert` and nothing to uninstall"

**Fixed in the engine, not in the prose** — the tool's own output was the misleading half, and a
doc note saying "ignore that line" would leave every future reader hitting it first. The line now
reads (captured live on `build=0dedc643`):

```text
copies ready in ...\ContentTool\Patched\morgott.demo.nodeptexture - nothing to install: ticking 'NoDepTexture' on in the mod manager redirects them (dev-only shortcut: ct_route7 apply NoDepTexture)
```

The recipes index now says the same thing from the other side: the checkbox is the whole install
step, `ct_route7 apply` survives as a developer shortcut, a player never needs it.

### 3. B2 — "refuses a project with nothing baked" vs the only quoted refusal

> §6: "It does not bake … and it **refuses a project with nothing baked**"
> the refusal: "this package has **neither `Content\` nor `Dist\`**"

**Ran it** on the blind mod exactly as delivered (`Content\` present, `Dist\` absent):

```text
PACKAGED 6 file(s), 10133 B into ...\pkg-nodist
```

No warning. **The real rule is now documented**: the refusal fires only when *both* are missing, a
`Content\`-only project packages, and such a mod still works because the player's first tick bakes
it from their own installation — with the live readback above as the evidence. The advice to ship
`Dist\` anyway stays, with its real reasons (tested artefact, no first-tick bake) rather than a
refusal that does not exist.

### 4. B5 — no validity rule for a replacement image anywhere

Established from the import path and then measured. `guides\textures.md` now carries the whole rule
as a table: **`.png`/`.jpg` only**; **need not match the shipped size, need not be square, need not
be a power of two** (measured: a **300×150** source baked and read back `300x150 RGBA32 PASS`);
**alpha is kept byte for byte** (measured: `px[0,0]=255,0,0,128`); **supply sRGB art**, because the
bake stamps `m_ColorSpace = 1` on every replacement — with the corollary that a normal/metallic/
occlusion map is linear data and will be mis-tagged; no enforced maximum, but RGBA32 × 1 mip is the
cost, and one mip means no distance filtering.

### 5. A3 — "mandatory" against the page's own measurement

The contract table said `Dependencies` "turns ContentTool on for the player **and puts our loader
patch in place before you load**", which the same page's four-cell measurement disproves. The row now
says what is true: declare it **because it auto-enables ContentTool and orders the load**; ContentTool
never reads it; a code-less mod with ContentTool off fails to load outright; and `package.ps1`
refuses a release without it.

### 6. A5 — "hand it straight back" vs "never redistribute"

`ct_extract`'s description no longer says "hand straight back to `Content\`" — it says *open, measure
and paint over while you author*. The admonition now draws the line explicitly: authoring with an
extracted file is fine, shipping one — or a repaint that is still mostly Snapshot's pixels — is not,
with the test *"could you have produced this without the extracted file in front of you?"*.

### 7. A6 — two success formats, no rule for which you get

Both quoted forms were wrong. `<Mod>: <n> replacement(s) redirected in memory` **is not printed by
any current build**; the real pair, captured this round, is
`1/1 bundle(s) redirected LIVE for '<id>' - nothing was written to the game installation` and
`<Mod>: <n> clip(s) served in memory from <path>`. The reference gained a table of all three `ct_`
lines, what each counts (roster verdict · shipped **bundles** · **video** rows) and when you get it —
including that `0 clip(s)` on a texture mod is correct. Every stale quote on the reference, shipping,
textures and meshes pages was replaced with a measured one.

### 8. B3 — `AssemblyName` omitted in the sample, `""` in the table

The sample now sets `"AssemblyName": ""`, and the table records what the loader really does: both
`""` and an absent field load and apply their content, and `package.ps1` refuses neither — the
refusal is only for a *declared* `.dll` that is not in the package. Both halves were exercised this
round: the blind mod shipped `""` and loaded; the `MaterialTweak` demo omits the field and loaded.

### 9. A4 — "See §0 below" when §0 is above · A7 — the in-page nav

A4 fixed ("§0 above has the real listing"). **A7 is false** and nothing was changed for it — see the
end of Part 1.

### 10. B7 — no way to look at your result, and no version statement

Two additions. The reference gained *Looking at it, not just at the log*: there is **no asset viewer**
in Phoenix Point, so confirm the redirect in the log, then put the thing on screen — a creature
bundle means loading a mission, a weapon shows on the roster screen, a menu proves nothing — and make
the first version unmistakable. The engineer's Addressables probe is named as the optional route for
a number, not as the answer. The home page gained *Which versions this is*: ContentTool `1.0.0.0`
against Phoenix Point **1.30.2.75117** / Unity 2019.4.31f1 on Steam/Windows, Epic and Game Pass
**untested** and said to be untested, TFTV 1.1.4.5 plus 15 mods measured together, and what a game
patch can and cannot break.

## The seal script ate this round's evidence, once

Re-sealing the workspace to verify the fixes deleted `round2\work\` — the agent's mod, its
`COMMANDS.md` and its `FINDINGS.md` — because the script removes the whole round directory before
rebuilding it. `COMMANDS.md` and `FINDINGS.md` were lost that way; everything of theirs quoted above
was captured before the seal ran, and the `AcidChecker` folder itself survived in a working copy and
was put back. `tools\seal-blind-workspace.ps1` now **moves a non-empty `work\` aside** to
`<sealed>-work-<timestamp>` and prints where it went, instead of deleting it.

## Engine changes this round

Two strings, both because the tool's own output was the misleading source:

- `src\Project\Package.cs` — the packaging success message now says *zip the FOLDER*.
- `src\Bake\ProjectBake.cs` — the bake no longer tells an author to `ct_route7 apply`; it names the
  checkbox and marks the verb dev-only.

`tests\TargetPathTests` passes unchanged: its assertion is on the `"copies ready in "` prefix and on
the structure, not on the sentence tail.

## Pages changed

`docs\index.md` · `docs\SHIPPING-A-CONTENT-MOD.md` · `docs\guides\index.md` ·
`docs\guides\reference.md` · `docs\guides\textures.md` · `docs\guides\meshes.md`

## What round 3 should watch for

- The blind agent **could not find a target it had not been handed**. `ct_list` turns a known name
  into two strings; nothing on the site maps *"that thing on screen"* to a bundle. Every worked
  example starts from a name the reader is assumed to have.
- **No failed bake is shown anywhere.** Every transcript on the site is a success, so a reader who
  gets `ct_project: 2 FAILURE(S)` has no worked example of reading and fixing one.
- **The demos are cited constantly and never linked or reproduced**, so a stuck author cannot diff
  their folder against a known-good one.
- Round 1's remaining gaps stand: `ct_sound bake`'s output is still quoted from prose rather than a
  capture, and the icon rung still asks for an assembly without showing one.
