# Facts sheet: docs/examples/*.md + docs/find-content/index.md (verified on main 0c9a626)

## examples/index.md
- examples/index.md:34 (insert after table) | replace-armour-set.md is in nav (mkdocs.yml:89) but not listed/linked; it is not a demo | add line: "Also: [Worked example: a PX Heavy armour set from a Blender export](replace-armour-set.md) - a transcript of an author's own project (not shipped, not downloadable) that walks Model Doctor, Validate and Bake diagnostics on a real Blender export. Use your own files." | mkdocs.yml:89; local\Wizard.ApocDesignation (gitignored .gitignore:51)
- examples/index.md:28 | AddUiSounds row must not imply ContentTool plays added sounds by itself | "Bake two new events into a mod bundle; the demo's own DLL loads that bank and plays it on a hotkey." | demos\AddUiSounds\src\AddUiSoundsMain.cs:57-67; src\Bake\SoundLoad.cs:34-64

## examples/menu-music.md
- menu-music.md:57 | bake summary string changed (20cd96b) | `ct_sound bake: 2/2 bank(s) in <project>\Dist\Sounds, 0 refused - NO game file was opened for writing. ContentTool loads these at init.` (template: `ct_sound bake: <baked>/<declared> bank(s) in <dir>, <refused> refused - NO game file was opened for writing. ContentTool loads these at init.`) | src\Bake\SoundReplace.cs:392-394
- menu-music.md:32 (after) | bake now also writes a source ledger that ships and ct_package checks (not committed in the demo) | add tree line `    sources.ledger                <- written by ct_sound bake; ct_package checks sources against it` | src\Project\Package.cs:544, :511-519; SoundReplace.cs:386
- menu-music.md:55-56 | loop report placeholder OK; actual forms are `, loop 0..<lastFrame> play count <n>` or `, no loop region` (no change needed; optional precision) | SoundReplace.cs:368-371

## examples/replace-ui-sounds.md
- replace-ui-sounds.md:62 | bake summary string changed | `ct_sound bake: 3/3 bank(s) in <project>\Dist\Sounds, 0 refused - NO game file was opened for writing. ContentTool loads these at init.` | SoundReplace.cs:392-394
- replace-ui-sounds.md:34 (after) | ledger line in tree | `    sources.ledger                <- written by ct_sound bake; ct_package checks sources against it` | Package.cs:544
- replace-ui-sounds.md:63 (after, optional) | per-row refusal now exists; one bad row no longer stops the rest | "A row that cannot be baked prints `bake REFUSED <file> <reason> - this project's other sounds are unaffected` (or `... is not one of the <n> media IDs Phoenix Point owns - nothing would ever play it`) and counts in `<refused> refused`; its previous bank is kept." | SoundReplace.cs:318-319, :328, :362-364, :377-382

## examples/intro-video.md
- intro-video.md:78 | bake summary string changed | `ct_sound bake: 1/1 bank(s) in <project>\Dist\Sounds, 0 refused - NO game file was opened for writing. ContentTool loads these at init.` | SoundReplace.cs:392-394
- intro-video.md:42 | bin\Release is build output, not committed | append to tree comment or add note after :49: "`bin\Release\` exists only after `dotnet build`; it is not in the repository." | git ls-files demos/IntroVideo (no bin/)
- intro-video.md:12-14 (optional add) | video `asset` now accepts the path `ct_list videos` prints | "The `asset` may be the full catalog path, the path `ct_list videos` prints (`Videos/Factions/Phoenix/PP_Intro.webm`), or any tail of it that starts at a `/` boundary, down to `PP_Intro.webm`." | src\Bake\CatalogText.cs FindKey (b3ff212); Extract.cs VideoRoot = StreamingAssets\StreamableCopiedAssets
- intro-video.md:79 (after, optional) | ct_project now CHECKS video rows before ALL PASS | failure forms: `VIDEO FAIL '<video>' is not a .webm/.mp4/.mov under Content\Videos\ - ct_video would skip this row` / `VIDEO FAIL '<asset>' <- <video>: no catalog row names '<asset>' - ct_video would skip this row` | ProjectBake.cs:1998-1999, :2014-2015; CatalogText.cs FindKey

## examples/quit-cutscene.md
- quit-cutscene.md:21-22 | 120 s is a fallback, not a cap | "A watchdog closes the failure path. Its deadline is the clip's decoded length plus ten seconds, or a flat 120 seconds when the player reports no length; if the clip does not come up within 8 seconds, the demo quits at once. It is armed even after a successful prepare, ..." | demos\QuitCutscene\src\QuitCutsceneMain.cs:175-176, :245, :285, :291-294
- quit-cutscene.md:73 | seconds printed with current culture (`ToString("0.0")`); ledger run shows `13,0s` | use placeholder: `Q1-watchdog armed: this quit happens in <seconds>s at the latest whatever the clip does next (clip length=3s, grace=10s)` + note "`<seconds>` is 13.0 (13,0 on comma-decimal systems)" | QuitCutsceneMain.cs:286-287; internal-docs\engine\PROVEN-FOUNDATIONS.md:1521 (`13,0s`)
- quit-cutscene.md:36 | bin\Release build output only | note "exists only after `dotnet build`" | QuitCutscene.csproj:16; not tracked

## examples/custom-creature.md
- custom-creature.md:68-74 | gate loads a save with no confirmation; trailing arg optional; `links` subverb undocumented | replace block with:
  - "`ct_creature gate` loads the named tactical save at once, with no confirmation: the running campaign is dropped and unsaved progress is lost. Save first and start from the main menu."
  - ```text
    ct_creature list
    ct_creature gate <tactical-save-name> [template-name-fragment]
    ct_creature links
    ```
  - "The template fragment is optional and is the LAST word (a save name may contain spaces); without it the gate uses the first candidate template (`ct_creature list`). `ct_creature links` counts the loaded map's nav links per climb area, so a traversal the map cannot pose is not read as a creature failure."
  | src\Tactical\CreatureGate.cs:59-77, :90-114, :1256-1275 (FinishLevelAndLoadGame, no prompt)
- custom-creature.md:69 | save name source: usage/refusal texts say `ct_mission list`, which is a DEV command (only with `ct-dev` marker) | do not tell readers to run ct_mission; say "the save's name (matched case-insensitively against the game's savegame names)" | CreatureGate.cs:67, :1262, :1265; src\Dev\DevGate.cs:23-31
- custom-creature.md:95-98 | mixes R2 and R4 | "**Verified in-game on 2026-08-28** from a fresh copy with no bake (run R4). All 19 gate arms passed: the spider bashed a target from 190 to 130 health, spat it from 130 to 120, walked 2.83 tiles in 0.71 seconds (3.98 tiles/s), played its own death clip and measured `Health.Max = 60`. The first measured run (R2, 2026-08-27) walked the same 2.83 tiles in 0.69 s; its shipped-template control came from a separate launch. The 60 measured health versus manifest 40 remains an open interaction with the resident TFTV stack." | internal-docs\evidence\VERIFIED-DEMOS.md:35, :64-69, :92, :234-238
- custom-creature.md:47 | bin\Release build output only | note "exists only after `dotnet build`" | CustomCreature.csproj:16; not tracked

## examples/add-ui-sounds.md
- add-ui-sounds.md:16-18 (after) | KNOWN LIMITATION applies: ContentTool never loads an added bank for players; this demo works only because its DLL loads it | add: "ContentTool itself bakes and self-checks the added bank but does not load it when players run the mod - a project that adds sounds (`Content\Audio`) without its own loader is silent in play. This demo plays because `AddUiSounds.dll` loads the bank from its bundle with `LoadBankMemoryCopy`. Replacing existing sounds (`Content\Audio\Replace` -> `Dist\Sounds`) needs no DLL." | ProjectBake.cs:563-580; SoundLoad.cs:34-64; ContentToolMain.cs:158; demos\AddUiSounds\src\AddUiSoundsMain.cs:57-67
- add-ui-sounds.md:33 | bin\Release build output only | note "exists only after `dotnet build`" | not tracked

## examples/weapon-mesh.md
- weapon-mesh.md:50-53 | tree omits tracked tools | tools list = `check_project.py`, `fit_rifle.py`, `grimdark.ps1`, `make_neutral_maps.py`, `render_icon.py`, `source\Gun_Rifle.gltf`, `source\Gun_Rifle.bin` | git ls-files demos/WeaponMesh
- weapon-mesh.md:47 | bin\Release build output only | note "exists only after `dotnet build`" | WeaponMesh.csproj:16

## examples/weapon-add.md
- weapon-add.md:62 | bin\Release build output only | note "exists only after `dotnet build`" | WeaponAdd.csproj:16
- weapon-add.md:111 (after, optional) | re-build no longer duplicates storage; new marker | "A second build in the same session reports `<def name> already seeded` in that list instead of adding the weapons again." | src\Tactical\WeaponBuild.cs:639-644, :659-660

## examples/humanoid-soldier.md
- humanoid-soldier.md:56 | string has a slot list and prints only when >1 material | `model 'soldier' kept <n> material(s) as <n> submesh(es) [<slot list>]` (only when the model has more than one material) | ProjectBake.cs:448-451

## examples/replace-character-body.md
- replace-character-body.md:62 | same string | `model 'body' kept <n> material(s) as <n> submesh(es) [<slot list>]` (only when >1 material) | ProjectBake.cs:448-451
- (lines 71-79 verified byte-exact vs CreatureBuild.cs:306-310, :337-338, :787-795 - no change)

## examples/replace-armour-set.md
- replace-armour-set.md:3 | Wizard.ApocDesignation is the owner's private project (local\, gitignored), not shipped | open with: "This page is a transcript of one author's own project, `Wizard.ApocDesignation`. It is not shipped with ContentTool and cannot be downloaded; follow the steps with your own GLBs and textures. The project replaces a PX Heavy torso and both legs with three GLBs and one albedo." | .gitignore:51 (local/); local\Wizard.ApocDesignation
- replace-armour-set.md:22 | tree heading implies reader has the project | caption before tree: "The author's project held these inputs (yours will use your own names):" | same
- replace-armour-set.md:139-143 | "offline shard removal" is a repository test utility, not a ContentTool feature | "Blender was unavailable for this run, so the author removed the shard with a repository test utility (`ObjCodecTests --dropshard`, not part of ContentTool). Readers fix it in Blender:" and keep/label the output as that utility's | tests\ObjCodecTests\Program.cs:66, :99, :261
- replace-armour-set.md:161, :169, :182 | transcripts carry the author's machine paths (`D:\PP-Instance2\Mods\...`, `C:/Users/Morgott/...`, `C:\Users\Morgott\AppData\Local\...`) | replace with `<Mods>\Wizard.ApocDesignation\...`, `<persistentDataPath>\ContentTool\Patched\<hash>\...`, `%LOCALAPPDATA%\ContentTool\Packages\...` | privacy; strings otherwise still current (StageText.cs:100, :131, :241, :266; BundleLive.cs:124)
- replace-armour-set.md:165-170 (optional) | R38 now fires only when the game has actually loaded that copy (6be4761) | add "(this refusal appears only when the game has that copy loaded)" | StageText.cs:266; commit 6be4761

## find-content/index.md
- find-content/index.md:53 | videos filter optional | `ct_list videos [nameFilter]` | src\Dev\Extract.cs:63-64, usage :85
- find-content/index.md:68 (after) | what ct_list videos prints + that it pastes into `asset` | "`ct_list videos` prints `<n> .webm file(s) match '<filter>' under <root>` and one `<name>  <path>` row per clip, the path relative to `StreamableCopiedAssets` (e.g. `Videos/Factions/Phoenix/PP_Intro.webm`). A video Replace row's `asset` accepts that path, the full catalog path, or any tail of it starting at a `/`." | src\Bake\LooseFiles.cs:39-51; Extract.cs VideoRoot; CatalogText.cs FindKey (b3ff212)
- find-content/index.md:59 | version pin | "ContentTool reads names from the game's `SoundbanksInfo.xml` and lists ..." (drop "1.2.1") | meta.json Version 1.2.1
- find-content/index.md:29 (after) | ct_list assets with no match is not a refusal | "A filter that matches nothing is not an error: the report reads `<bundle>: 0 of <n> assets match type~'<type>' name~'<name>'`." | src\Bake\AssetIndex.cs:72-74; Extract.cs:257
- find-content/index.md:101-106 | refusal list incomplete/misattributed: `ct_list REFUSED - ...` covers only `ct_list bones/props/clip`; a misspelled `ct_extract` name still THROWS | replace with:
  - "`ct_list VOID - no bundle at <path>` / `ct_extract VOID - no bundle at <path>` - wrong bundle filename; return to `ct_list bundles <filter>`."
  - "`ct_list REFUSED - no <class> named '<name>' in <bundle path>` (from `ct_list props` / `clip`) - the name does not match exactly."
  - "`ct_list REFUSED - <n> <class>s are named '<name>' (pathIds <ids>) - refusing to guess which one to use` - ambiguous name."
  - "`ct_extract` with a misspelled name currently prints `ct_extract THREW System.InvalidOperationException: no <class> named '<name>' in <bundle path>` followed by a stack trace (video: `no .webm named '<name>' under <root> (there are <n>; list them first)`). Read the first line; the name is wrong."
  | Extract.cs:69-83, :262-276, :434-457; ContentToolMain.cs:655; AssetIndex.cs:118, :137-139; LooseFiles.cs:69-74

<!-- dropped: 1 finding moot: quit-cutscene.md:79 date 2026-09-01 is correct (PROVEN-FOUNDATIONS.md:1521 Q1 CLOSED 2026-09-01, 3,0 s vs 13,0 s; ledger R3 2026-08-27 is the earlier run). Tactical code findings (REVIEW-TASKS 80-91) and Dev-probe findings (145-156) have no text on these pages; only doc-visible effects kept (weapon "already seeded"). No page names any of the 15 dev commands; "dev-only shortcut: ct_route7 apply" lines (material-tweak.md:55, weapon-mesh.md:80) are the current code string (ProjectBake.cs:1972-1974) and ct_route7 is public - keep. -->
<!-- unverified: (1) ct_extract misspelled-name THREW - Track F (worktree) is changing Extract.cs; re-check final string before writing find-content :101-106. (2) ct_extract audio --all freezes the game during 3105-wem decode (REVIEW-TASKS 150) - Track F; add warning to find-content :78 only if not fixed. (3) Which name `ct_creature gate` matches (SavegameMetaData.Name) vs what the Load screen shows - not measured. (4) custom-creature "ranged accuracy" FAIL/PASS lines not on page; no ledger run since 64a7025. (5) Demo Dist\Sounds banks committed without sources.ledger: ct_package after a fresh clone without re-bake falls back to file-date check (Package.cs:518) - may refuse "CHANGED SINCE IT WAS BAKED" depending on checkout mtimes; not run. -->
