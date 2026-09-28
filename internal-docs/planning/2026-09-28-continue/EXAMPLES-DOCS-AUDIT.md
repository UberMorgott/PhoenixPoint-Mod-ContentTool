# Examples + wiki audit (2026-09-28, HEAD 358d618, audit only - no edits)

Inputs: `demos\` (12 folders + `demos\tools`), `docs\` (35 pages, last touched cee21d1 2026-09-11), fact sheets
`..\2026-09-26-review\facts-*.md` (verified on 0c9a626), HANDOFF track G list, src at HEAD.

## Headline

- Wiki untouched since 2026-09-11; code moved ~96 commits (79 review fixes). ALL ~254 fact-sheet bullet items still pending
  (start-ref 65, recipes 98, bench 53, examples 33 + find-content 5; counted by `- ` lines per `## page`). Plus track G + 17 commits after 0c9a626 that
  supersede some fact-sheet lines (see "Superseded" below). Plus bench pages will be superseded again by UI track.
- `mkdocs build --strict` -> exit 0, 0.70 s, no warnings (links/nav valid; content is what's stale).
- Demos: 11 public + 1 fixture. `package.ps1` offline: 11/11 public PACKAGED OK; NoDepTexture REFUSED (by design:
  no Dependencies). Bake NOT tried (needs game). All committed Dist artifacts baked 2026-08-28/29 by 1.0.0 - never
  re-baked after the 79 fixes.

## Offline build / package results

Command per demo: `.\package.ps1 -Project demos\<X> -Out <scratchpad>\pkg\<X>` (builds demo csproj if any, then
`tools\Package`). Prereq NOT stated anywhere: demo csproj references `..\..\bin\Release\ContentTool\ContentTool.dll`
(e.g. `demos\WeaponAdd\WeaponAdd.csproj` HintPath) -> fresh clone must `dotnet build ContentTool.csproj -c Release` first;
`package.ps1` does not do it. Game refs default to `D:\Steam\...\ModSDK` (`PPRoot` property).

| Demo | Result | Files / bytes | Notes |
|---|---|---|---|
| AddUiSounds | PACKAGED | 8 / 270 KB | ships `Content\Audio\*.mp3` sources too (Add sources not "LEFT BEHIND" - check if needed) |
| CustomCreature | PACKAGED | 7 / 2.6 MB | glb + Dist bundle both ship |
| HumanoidSoldier | PACKAGED | 4 / 36.3 MB | no Dist; raw 36 MB glb ships |
| IntroVideo | PACKAGED | 8 / 871 KB | 1 source left behind (ok) |
| MaterialTweak | PACKAGED | 3 / 5 KB | content-only |
| MenuMusic | PACKAGED | 6 / 2.1 MB | 2 sources left behind |
| NoDepTexture | REFUSED | - | `REFUSED: meta.json does not declare "Dependencies"` - intended fixture behaviour |
| QuitCutscene | PACKAGED | 5 / 214 KB | |
| ReplaceCharacterBody | PACKAGED | 4 / 36.3 MB | same glb as HumanoidSoldier |
| ReplaceUiSounds | PACKAGED | 7 / 132 KB | 3 sources left behind |
| WeaponAdd | PACKAGED | 12 / 15.3 MB | |
| WeaponMesh | PACKAGED | 13 / 2.0 MB | |

No `WARN ... newer than its bank ... no sources.ledger entry` fired (sound demos have no `sources.ledger`; date
fallback passed on this checkout - fragile after a fresh clone / touched mtimes).

## Examples table

| Demo | Demonstrates | Build | Problems | Fix |
|---|---|---|---|---|
| MaterialTweak | Replace: one material float (route7) | content-only, no Dist | wiki material-tweak.md ok per fact sheets | none beyond doc sweep |
| NoDepTexture | FIXTURE: mod with no Dependencies (texture replace) | content-only; package REFUSED by design | not a demo but lives in `demos\` -> `deploy.ps1` installs it as a mod on every deploy; meta.json:6 + README:37 link dead `docs\SHIPPING-A-CONTENT-MOD.md` (now `internal-docs\legacy-pages\`); README says "other eight demos" (11) | move to `tests\fixtures\` (or `local\`), fix links |
| WeaponMesh | Replace weapon art (mesh + 5 maps, icon) + DLL | csproj -> DLL; Dist bundle committed | README:56 dead `docs\...` link; Dist 2026-08-28 older than manifest fit edit f717ffa (08-29, fit only - bundle likely unaffected); wiki tree omits tools (fact sheet); authoring tools `grimdark.ps1`, `make_neutral_maps.py` etc. undocumented as optional | re-bake + diff; README link; doc tools as "how the art was made (optional)" |
| WeaponAdd | Add 3 new weapons (clone defs) + DLL + icons | csproj; Dist committed | README:13 dead doc link; 5 authoring tools + `tools\source\` raw art (gltf/bin/png/glb) committed - fine as provenance but heavy | keep sources, mark optional; fix link |
| ReplaceUiSounds | Replace 3 shipped UI stings (`ct_sound bake`) | no DLL; Dist\Sounds banks committed | no `sources.ledger`; bake summary string changed (fact sheet) | re-bake to add ledger |
| MenuMusic | Replace 2 menu music media | same | same | same |
| AddUiSounds | Add 2 new events; own DLL loads bank + hotkey | csproj; Dist bundle | depends on DLL loader (ContentTool never loads added banks = known limitation, HANDOFF deferred); wiki must say so | doc limitation; decide if Add sources should ship |
| IntroVideo | Add/replace intro video + subtitles + replaced audio bank + DLL | csproj; Dist bank | `bin\Release` shown in wiki tree (build output) | doc note |
| QuitCutscene | Play a clip on quit (DLL, watchdog) | csproj; no Dist | README:169 dead doc link; no own `.gitignore` (root covers bin/obj) | link fix |
| CustomCreature | Build: new creature (spider) + DLL + check scripts | csproj; Dist bundle | README 768 lines (wall of evidence, not a guide); `ct_creature gate` loads save w/o confirm (fact sheet); R2/R4 mixed | split README: 1-page how-to + move evidence to internal-docs |
| HumanoidSoldier | Build: humanoid soldier from Tiffany Cox GLB | no csproj, no Dist; 36 MB glb | glb byte-identical to ReplaceCharacterBody body.glb (SHA256 B6E54D58...ABE) -> 72 MB duplicated; no SOURCES.md (third-party model, record only inside glb asset); README:11,158 + meta.json:6 link dead `docs/guides/humanoid-soldier.md` | one shared glb (or LFS/release asset), add SOURCES.md, fix links |
| ReplaceCharacterBody | Replace a shipped character body with same glb | no csproj/Dist | README:18 + meta.json:6 dead `docs/guides/replace-character-body.md`; duplicate glb | as above |

Cross-demo problems:
- `demos\README.md` "so the ten sort together" -> 11 (+fixture). `deploy.ps1:73` "two demos ... other four" -> 6 csproj demos / 6 content-only.
- Every committed `Dist\` (bundles + banks) baked by 1.0.0 on 2026-08-28/29; tangents/emission/etc fixes since. Re-bake all in Instance2 via Lifecycle, diff, commit, add `sources.ledger`.
- Orphans: `demos\tools\glbfit.py`, `demos\tools\pngopt.py` referenced nowhere (git grep). `tools\sync-standalone.ps1` dead (copy publishing dead 2026-08-29; refs `docs\VERIFIED-DEMOS.md`, mkdocs `exclude_docs` no longer exists).
- Repo-root clutter (gitignored, not tracked): `tiffany_cox_*.glb` x3, `APOCD GLBs ...\` + `.zip`, `Console.log` -> move under `local\`.
- Demo READMEs duplicate the wiki example pages (two sources of truth, already diverged).

## Wiki table

Status: OK = accurate; STALE = fact-sheet/track-G items pending; WRONG = claim false for a public reader.
Item counts = pending lines in `facts-*.md`.

| Page | Status | Problems |
|---|---|---|
| index.md | STALE (2) | no ct-dev note; demos count; armour page not flagged private |
| getting-started/first-mod.md | STALE (4) | quickstart exists but is a "first bake" not a 10-min result; `1.0.0` pin :37; `ct_project THREW` failure lines (:126-130) - invalid JSON now counted refusal (854107e) |
| getting-started/choose-a-route.md | STALE (6) | sound Replace uses `ct_sound bake`; video not baked; manifest arrays miss `sounds` |
| getting-started/lifecycle.md | STALE (16) | heavy; overlaps bench/lifecycle-tab |
| bench/index.md | STALE (10) + UI redesign pending | rewrite after UI track |
| bench/model-doctor.md | STALE (15) + UI | screenshots will be outdated |
| bench/lifecycle-tab.md | STALE (28) + UI | worst bench page; stage log behaviour changed (485d3a3) |
| reference/project-files.md | STALE (14) + INCOMPLETE | no ppcontent.json schema: `replace`/`publish`/`sounds`/`creature`/`weapons` blocks each 0 hits here (only in recipes); meta.json comments/trailing commas now allowed (ecd788a); reserved names CON/NUL refused (e5a1fcf) |
| find-content/index.md | STALE (5) + fact sheet partly SUPERSEDED | `ct_extract` misspelled name is now `ct_extract REFUSED - ...` (Extract.cs:285, d67d675/525fb60), not THREW as fact sheet says; `audio --all` now background (1414414); `1.2.1` pin :59 |
| troubleshooting/bake-errors.md | STALE (23) + WRONG rows | :216 `ct_project THREW ... holds two files with the same name` -> now SKIPPED, bake continues (ContentMods.cs:110-113, 52bacd0); version pins `1.1.2` :3, :93; very long (221 lines) mixed glossary |
| recipes/index.md | STALE (3) | tree misses sounds block; good table |
| recipes/textures.md | STALE (5) | `1.1.2` pin :8 |
| recipes/material-properties.md | STALE (2) | |
| recipes/meshes.md | STALE (8) | tangent limitation (deferred) not mentioned |
| recipes/animated-models.md | STALE (7) | helper-node rest-pose curve now drops (52e5165) |
| recipes/animation-contract.md | STALE (5) | |
| recipes/sounds.md | STALE (25) - worst recipe | THREW rows :187-191 check vs StageValidate.cs:155; ledger/WARN (14025f9) absent; added-audio "no runtime loader" limitation absent; `1.2.1` pin :38 |
| recipes/videos.md | STALE (10) | video row checks before ALL PASS; stem collision SKIPPED; unticking drops anon Register (b445e9c) |
| recipes/creature.md | STALE (10) | `1.1.2` pin :4 |
| recipes/humanoid-soldier.md | WRONG + STALE (4) | :3, :12 "the included `tiffany_cox_idle_animation.glb`" - gitignored (.gitignore:55), NOT in repo; public reader cannot follow ("repository-only recipe" with a file the repo lacks) |
| recipes/replace-character-body.md | STALE (2) | same source dependency |
| recipes/weapon.md | STALE (12) | 243 lines, longest recipe; `ct_weapon` is a LOG prefix, not a console command - say so |
| recipes/behavior-dll.md | STALE (5) | `1.1.2` pin :105 |
| examples/index.md | STALE (2) | armour page not listed; AddUiSounds wording |
| examples/material-tweak.md | OK (fact sheet: no change) | |
| examples/weapon-mesh.md | STALE (2) | |
| examples/weapon-add.md | STALE (2) | |
| examples/replace-ui-sounds.md | STALE (3) | bake summary string |
| examples/menu-music.md | STALE (3) | |
| examples/add-ui-sounds.md | STALE (2) | loader limitation missing |
| examples/intro-video.md | STALE (4) | |
| examples/quit-cutscene.md | STALE (3) | watchdog 120 s is fallback not cap |
| examples/custom-creature.md | STALE (4) | gate loads save w/o confirm; `ct_mission list` now public (bcff3d1) - fact sheet line saying "dev" is SUPERSEDED |
| examples/humanoid-soldier.md | STALE (1) | |
| examples/replace-character-body.md | STALE (2) | |
| examples/replace-armour-set.md | WRONG (5) | private project (local\, gitignored) presented as example; author machine paths `C:\Users\Morgott\...`, `D:\PP-Instance2\...` (privacy) |

Command coverage vs src (`src\ContentToolMain.cs:489-769`):
- Public (15): ct_version, ct_dump, ct_project, ct_package, ct_route7, ct_catalog, ct_video, ct_sound, ct_voices, ct_list, ct_extract, ct_fit, ct_bench, ct_mission (list), ct_creature. Dev (`[DevOnly]`, 13 + `ct_mission gate` = 14): ct_bake, ct_audio, ct_outtest, ct_fmt, ct_replace, ct_revert, ct_texswap, ct_meshswap, ct_liveswap, ct_dev, ct_scan, ct_music, ct_seamprobe.
- Wiki leaks NO dev command (good). Never documented: ct_version, ct_dump, ct_voices, ct_mission list. No command reference page exists.
- Descriptions of public ct_fit/ct_bench still say "(dev workbench)" (ContentToolMain.cs:675, :682) - confusing next to the ct-dev gate (code issue, not docs).
- Version pins: `1.0.0` x13, `1.1.2` x5, `1.2.1` x2 across pages; meta.json = 1.2.1, release 1.3.0 planned -> drop pins or single "since" table.

Superseded fact-sheet lines (re-verify before writing): find-content :101-106 (ct_extract now REFUSED), custom-creature :69 (ct_mission list public), bake-errors collision/invalid-JSON rows, videos stem collision, sounds package WARN. Rule: fact sheets are 0c9a626; diff `0c9a626..HEAD -- src` (17 commits) before trusting a string.

## Structure problems

- No true quickstart: first-mod.md = 143 lines before a result; index.md is a link list of 8 steps.
- Duplicated flows: getting-started/lifecycle.md vs bench/lifecycle-tab.md vs troubleshooting (three places describe bake->apply->verify->package).
- Reference is thin: no ppcontent.json schema, no meta.json field page, no console command page, no known-limitations page. Schema is scattered over 12 recipes.
- Walls of text: bake-errors.md (221 lines, one glossary for all stages), weapon.md (243), bench/lifecycle-tab.md (188), replace-armour-set (205, transcript), CustomCreature README (768).
- Example pages retell demo READMEs (two sources of truth).
- Nothing tells a reader what is verified in game vs not (deferred list: tangents, emission, added-audio loader).

## Proposed structure (quickstart-first, per-workflow)

1. Start here - what it is; Players: install (4 steps); Modders: "go to Quickstart".
2. Quickstart (10 min) - replace one texture: folder, meta.json, ppcontent.json (copy-paste), Lifecycle tab or `ct_project`, expected `ALL PASS` line, see it in game, `ct_package`. One page, no branches.
3. Concepts - Replace / Add / Build (choose-a-route); project layout; lifecycle stages (one canonical page, bench + console both shown in tabs).
4. Workflows (fixed template: goal / need / tree / manifest block / steps / success line / top-5 failures / demo link):
   Textures; Material values; Meshes; Complete models + animation (+ animation contract as sub-page); Weapons (replace art | add new); Creatures; Humanoid soldier; Character body; Sounds - replace; Sounds - add (+ own-DLL loader requirement); Videos; Behaviour DLL.
5. Tools - In-game bench (after UI redesign): Model Doctor, Lifecycle tab, Fit bench; Finding content (`ct_list` / `ct_extract`).
6. Reference - console commands (15 public, generated from `[ConsoleCommand]` attrs); ppcontent.json schema (every block/field); meta.json; message glossary split per stage (Validate/Bake/Apply/Verify/Package/Sound/Video); formats + limits.
7. Examples - one table (demo, workflow, what to look at); each row links demo README on GitHub (single source) - drop the per-demo retell pages or cut them to 20 lines.
8. Known limitations + FAQ - added audio needs own loader, tangents/normal maps, emission colour, Windows reserved names, one mod per shipped bundle.
9. What's new / versions - replaces inline version pins.
Armour-set transcript -> internal-docs (or rewrite as anonymised "diagnose a real Blender export" case study).

## Cleanup plan (order)

1. Code-first freeze: wait for UI track (bench) + R2 fixes, then diff `0c9a626..HEAD -- src` and refresh fact sheets (strings changed in 17+ commits).
2. Demos: re-bake every Dist in Instance2 (Lifecycle), commit with `sources.ledger`; dedupe HumanoidSoldier/ReplaceCharacterBody glb (one copy or release asset); add SOURCES.md for Tiffany glb; move NoDepTexture to `tests\fixtures\`; fix 10 dead `docs\...` links in demo READMEs/meta.json (targets now `internal-docs\legacy-pages\`, `internal-docs\engine\`, `internal-docs\evidence\` or wiki URLs); split CustomCreature README; fix counts in `demos\README.md` + `deploy.ps1:73`.
3. Delete orphans: `demos\tools\glbfit.py`, `demos\tools\pngopt.py`, `tools\sync-standalone.ps1`; move root clutter (`tiffany_cox_*.glb`, `APOCD GLBs*`, `Console.log`) into `local\`.
4. Package prereq: `package.ps1` builds ContentTool first (or doc it) so demo csproj HintPath resolves on a fresh clone.
5. Humanoid recipe: ship the Tiffany source (with licence) or mark recipe "owner-only" and remove from public nav.
6. Wiki: restructure per above; write Quickstart + Reference (commands/schema) first (highest value); apply fact sheets page by page; Codex prose, Claude accuracy pass; remove version pins; anonymise/remove armour transcript.
7. Gate: `mkdocs build --strict` (currently exit 0) + add a doc check that every `ct_*` name in docs is a public command and every `docs\...` path in demos exists.
