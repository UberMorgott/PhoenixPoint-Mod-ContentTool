# Messages by stage

Values in angle brackets are filled in from your project or game installation. Read the last result line as well as the message that names the file or row.

## How to read a result

| Level | What it means |
|---|---|
| `PASS` | The named check succeeded. It does not prove that every other check succeeded. |
| `SKIPPED` | A source or declaration was not used. In a project bake, it counts toward `FAILURE(S)`; other work can continue. |
| `REFUSED` | The named operation or row was rejected. Read its reason and the final result. |
| `FAILURE(S)` | The number of counted failures in that bake. Earlier successful rows may still have written output. |
| `ALL PASS` | Every counted check in that bake passed. It does not mean the game has loaded the new asset yet. |
| `WARN` | A finding that did not fail the bake. Check what it describes before releasing the mod. |
| `VOID` | The check had nothing it could measure or prove. For Verify, a required `VOID` prevents a pass. |

## Validate

| Message | Meaning | Fix |
|---|---|---|
| `Validate: PASS - '<project>' - key <key>.` | The declaration and source placement passed the checks available before import. | Continue to Bake. The shipped bundle and its target assets are checked there. |
| `Validate: PASS - '<project>' - key <key>. NOTE: <notes>` | Validation passed with a note, such as a GLB without an armature. | Read the note. Check whether the target needs a rig before baking. |
| `Validate: FAIL - <reason>` | The manifest cannot be read or a declared source is missing, misplaced or ambiguous. | Correct the named file or declaration, then run Validate again. |

## Bake - importing sources

| Message | Meaning | Fix |
|---|---|---|
| `ct_project: <project root> could not be read - <reason> - fix ppcontent.json and run it again.` followed by `ct_project: 1 FAILURE(S)` | The manifest is missing, invalid, lacks a required root value or uses an unsafe name. Nothing was baked. | Fix `ppcontent.json` using the reason on the first line. |
| `SOURCE SKIPPED: <file>: <reason> - SKIPPED, the project's other sources are unaffected` | A texture, mesh or model importer could not read that file. | Re-export the named file in a supported form and bake again. |
| `SOURCE SKIPPED: <file> <reason> - SKIPPED, the project's other sources are unaffected` | An added audio source could not be decoded or packaged. | Re-export it as a usable mono or stereo WAV, OGG or MP3. |
| `SOURCE SKIPPED: Content\<folder>\ holds two files with the same name: <a> and <b> - a replacement names the stem, so one of them has to go; BOTH were SKIPPED, the project's other sources are unaffected` | Two accepted files share a stem, even if their extensions differ. Neither is imported. This also applies to videos. | Keep one file, or rename one and update its declaration. |
| `SOURCE SKIPPED: Content\Audio\ holds <n> file(s) this tool does not import: <names> - the accepted set is .wav, .ogg and .mp3, which this tool decodes itself at bake time. <details>` | Unsupported files in the added-audio folder are counted as skipped sources. | Convert them to WAV, OGG or MP3, then remove the unsupported copies. |
| `SOURCE SKIPPED: "replace" row REFUSED: <details>` | An incomplete replacement row could not be used. Other rows continue. | Give the row exactly one replacement kind and the fields that kind requires; see [ppcontent.json](ppcontent-json.md). |
| `SOURCE SKIPPED: "publish" row REFUSED: <details>` | A published key lacks its key or asset path. | Complete or remove the row, then bake again. |
| `nothing to bake - put .png/.jpg under Content\Textures\, .glb under Content\Models\ or .wav/.ogg/.mp3 under Content\Audio\; a mesh under Content\Meshes\ is only baked through a "replace" row in ppcontent.json` | No own-bundle source or replacement row was available, and no import failed. | Add a source in the named folder or declare the intended replacement. |

Every `SOURCE SKIPPED:` line is counted. The run can write its usable sources and still end with `ct_project: <n> FAILURE(S)`.

## Bake - patching shipped bundles (P0..P7)

| Message | Meaning | Fix |
|---|---|---|
| `P0 REFUSED "bundle": "<bundle>" is not a bundle this game ships - no file at <path> - check the spelling and list the real names with: ct_list bundles` | The declared shipped bundle is absent. | Run the printed `ct_list bundles` command and copy its bundle name. |
| `P1 REFUSED '<texture>' is not a .png/.jpg under Content\Textures\<location hint>` | The named texture source was not imported. A location hint appears if a matching file exists elsewhere in `Content\`. | Put a PNG or JPG directly in `Content\Textures\`, or correct the source stem. |
| `P1 REFUSED target '<asset>' is not a Texture2D in <bundle> - <reason> - list the names it does hold with: ct_list assets <bundle> Texture2D` | The shipped bundle has no unique texture target by that exact name. | Run the printed command and use the target name it lists. |
| `P3 REFUSED "material": "<value>" is not <property>=<number>` | The material property edit did not parse. | Use one property and one number, such as `_GlossMapScale=0.15`. Use a dot for a fraction. |
| `P3 REFUSED target '<asset>' is not a Material in <bundle> - <reason> - list the names it does hold with: ct_list assets <bundle> Material` | The material target is absent or ambiguous. | Run the printed command and choose a unique name. |
| `P4 REFUSED '<mesh>' is not a .obj or .glb under Content\Meshes\<location hint>` | The replacement mesh was not imported. | Put the OBJ or GLB directly in `Content\Meshes\`, or correct its stem. |
| `P4 REFUSED target '<asset>' is not a Mesh in <bundle> - <reason> - list the names it does hold with: ct_list assets <bundle> Mesh` | The mesh target is absent or ambiguous. | Run the printed command and choose the intended mesh. |
| `P4 REFUSED '<mesh>' -> <reason>` | The mesh could not be mapped onto that target. The line can also say why a sidecar was ignored. | Correct the geometry, skin or named bones described by the reason, then bake again. |
| `P4 WARN <mapping report>` | A mesh mapping looks suspicious, but the row was baked. The final line can append `- <n> warning(s), baked anyway`. | Inspect the mapping and the result in game. |
| `P4-ctl-shipped WARN the shipped <bundle>'s '<asset>' SUMMARISES the same as the replacement <details>` | Counts and rounded bounds cannot distinguish the two meshes. This diagnostic does not fail the bake; the byte check supplies separate evidence. | Read `P4-bytes` and inspect the result in game. |
| `P4-bytes VOID mesh '<asset>' has no readable vertex/index buffers in <path>` | That byte comparison could not run. | Check the named bundle or copy and the other mesh checks before trusting the replacement. |
| `P5 VOID '<asset>' is not rigged - <details>` | The shipped mesh has no skeleton for the skin check. | No skin fix is needed for an unrigged target; inspect its geometry checks. |
| `P6 VOID '<asset>' <- <mesh> carries no armature (<reason>), so there are no weights of its own to remap` | The replacement supplies no named rig to verify. | If the target requires the file's own weights, export a skinned GLB with an armature. |
| `P6 VOID '<asset>' <- <mesh> was bound nearest-bone, not by name, so there is no by-name binding to measure: <reason>` | The named-bone check does not apply to the binding used. | Read the reason and inspect deformation in game. |
| `P7 REFUSED "clip": "<value>" <reason>` | The clip edit is malformed, names an unsupported channel or changes nothing. | Use a meaningful `position*<number>` or `scale*<number>` edit. |
| `P7 REFUSED target '<asset>' is not a AnimationClip in <bundle> - <reason> - list the names it does hold with: ct_list assets <bundle> AnimationClip` | The clip target is absent or ambiguous. | Run the printed command and choose its exact name. |
| `P7 VOID clip '<asset>' binds no curve for the channel in "<edit>"<details>` | There is no matching curve to measure. | Check the clip's channels with `ct_list clip <bundle> <clipName>` and choose a channel it has. |
| `PARTIAL <bundle>: <n> row(s) above were REFUSED and the copy was rewritten anyway - <details>` | Successful rows were written into this run's copy; refused rows retain shipped data. The previous copy was replaced. | Fix every refusal above this line and bake again before applying. |
| `ct_project: <n> FAILURE(S)` | One or more sources, rows or read-back checks failed. | Fix the earlier named lines and rerun the bake. |

`P1`, `P3`, `P4`, `P5`, `P6` and `P7` also print `PASS` or `FAIL` read-back checks when they can measure a patched copy. A `FAIL` line names the check that failed. The current shipped-bundle patch path emits no P2 verdict.

## Bake - own bundle and self-checks

| Message | Meaning | Fix |
|---|---|---|
| `WROTE <path> <bytes> B as <identity>` | An output file was written. This is an intermediate result, not the bake verdict. | Read through to the final `ct_project:` line. |
| `TEX PASS <details>` / `TEX FAIL <details>` | A texture in the own bundle was loaded back and compared with its source pixels. | For `FAIL`, inspect the named texture and rebake. |
| `BANK PASS <details>` / `BANK FAIL <details>` | The added-sound bank passed or failed the bake-time load check. | For `FAIL`, inspect the named audio source. A pass alone does not arrange runtime loading; see the final section. |
| `A6 PASS <details>` / `A6 VOID <details>` | An added audio source matched its decoded result, or declared nothing that check could compare. | Read the detail; for an unexpected `VOID`, check the source file. |
| `FAIL AssetBundle.LoadFromFile returned null - something still holds a bundle named '<identity>'. Restart, or switch that mod off in the mod manager, then bake again.` | Unity could not reopen the new own bundle for its self-check. | Disable the mod that holds it or restart, then bake again. |
| `ct_project: ALL PASS - <output path>` | The own-bundle bake and its counted checks passed. | Continue to Apply or Package as appropriate. |
| `ct_project: ALL PASS - this project has no bundle of its own; the patched copy(ies) above are the whole output` | A replacement-only project successfully patched shipped bundles. | Continue to Apply. |
| `ct_project: ALL PASS - nothing needed patching: none of this project's <n> replacement(s) names a shipped bundle, so no copy was written - the video row(s) above are served live by ct_video` | A video-only replacement needs no patched Unity bundle. | Use the video live route and check it in game. |
| `ct_project: '<path>' is already being written by another run - nothing was baked. Wait for it to finish, then bake again.` | Another run owns the output. | Wait for that run to finish before retrying. |
| `ct_project: '<path>' is being served to the game right now, so it was not rewritten - restart the game and bake again.` | Rewriting the copy while the game reads it was refused. | Restart, then bake again. |
| `ct_project: CANCELLED - '<id>' stopped before it published anything; the patched copies and this project's own bundle are exactly as they were.` | The Lifecycle bake stopped before publishing. | Resume with Bake when ready. |

## Apply (redirect, ownership)

| Message | Meaning | Fix |
|---|---|---|
| `redirected <bundle> -> <path> for '<id>'<details>` | The live bundle route now points at the patched copy for a future load. | Load the affected content and check it in game. Already loaded objects do not change retroactively. |
| `kept <bundle> -> <path> for '<id>' - the game loaded it through this redirect, so it is already serving this copy` | The game already loaded this mod's current copy through its redirect. | No retry is needed for this claim. |
| `REFUSED: restart required: <bundle> is already loaded (as '<identity>'). Unity rejects a second bundle of the same identity, and unloading the game's copy would pull it out from under live objects. Restart, then enable '<id>'.` | The shipped bundle loaded before this redirect could take effect. | Restart with the mod enabled before that bundle loads. |
| `REFUSED: mod '<owner>' already replaces <bundle> - '<id>' cannot also replace it. One shipped bundle has exactly one owner and the lower mod id keeps it; one of the two has to go.` | Two mods claim the same shipped bundle, even if they change different assets. | Use one mod or make a permitted compatibility project that combines the changes. |
| `mod '<old owner>' lost <bundle> to '<new owner>' (one owner per shipped bundle, lowest mod id keeps it); its redirection was undone and its CRC put back` | A lower mod ID became the owner. | Check which mod is intended to own the bundle. |
| `REFUSED: '<id>' has no patched copy at <path>` | There is no copy to redirect. | Bake the project, then apply it. |
| `NOT APPLIED: patching the shipped bundle(s) reported <n> failure(s), named in the P0/REFUSED line(s) above; nothing was installed and no copy was marked current.` | Apply's bake refused at least one shipped-bundle row. | Fix the named row and bake again. |
| `'<id>' failed to bake earlier in this session - not baking it again. Fix the lines it printed, then <retry hint>` | The mod-manager route has a session block after an earlier failed bake. | Fix the earlier errors and use the printed retry command. |
| `Apply: VOID - nothing to install for '<project>'; this project declares no non-video replacement target.` | The project has no shipped-bundle redirect to install. | For a video-only project, use its video route. |

Bundle ownership uses the `ppcontent.json` ID in ordinal, case-sensitive order. Load order does not merge two private copies of one shipped bundle.

## Verify

| Message | Meaning | Fix |
|---|---|---|
| `Verify: PASS - load-back gates passed; <served> of <declared> declared target(s) served from this project's copies for '<project>'.` | The measured copies passed and this project serves every declared shipped-bundle target. | Check the visible result in game. |
| `Verify: VOID - only <served> of <declared> declared target(s) are served from this project's copies for '<project>'; the target(s) named above are unproven.` | The live ownership census is short. | Read the preceding `VERIFY` lines and resolve the missing or competing claim. |
| `Verify: VOID - restart required for '<project>'.` | The game still serves a previously loaded bundle. | Restart with the mod enabled, then verify again. |
| `VERIFY <bundle>: no patched copy at <path> - this project serves no copy of that target.` | A declared target has no copy to measure. | Bake the replacement again. |
| `VERIFY <bundle>: the live claim is <claim>, not this project's <path>.` | A copy exists but this project is not serving it. | Check Apply's ownership or restart refusal. |
| `P1 VOID '<texture>' -> '<asset>' in <bundle> did not import, so nothing measured it` | Verify could not prove a declared texture whose source failed import. | Fix the source and bake again. |
| `Verify: FAIL - <n> load-back gate(s) failed for '<project>'; the FAIL line(s) above name them.` | One or more copy checks failed. | Fix the named `FAIL` lines and rebake. |
| `Verify: PASS - nothing to verify for '<project>'; this project declares no patched target - its row(s) are served live by ct_video.` | There is no patched Unity bundle to verify. | Check the live video result in game. |

Verify reads existing copies; it does not rebake or install them.

## Package

| Message | Meaning | Fix |
|---|---|---|
| `PACKAGED <files> file(s), <bytes> B into <folder>` | A publishable folder was built. | Inspect that folder before distributing it. |
| `REFUSED: <folder> already holds files. Name a folder that does not exist yet - a package is built from nothing, so no leftover of a previous run can be shipped by accident.` | The staging destination is not empty. | Choose an empty destination or inspect and clear the earlier output. |
| `REFUSED: <path> is EMPTY OR NOT VALID JSON - <reason><details>` | `ppcontent.json` cannot be read by the runtime. Packaging stopped before staging. | Correct the manifest and package again. |
| `REFUSED: <path> - <reason>` | `ppcontent.json` parses, but a key that must be an array is not one, or `id`/`bundle` is not a safe single name. Packaging stopped before staging. | Correct the named key and package again. |
| `REFUSED - this package is NOT publishable, and <folder> has been deleted rather than half-written.` | One or more package checks failed. | Read every indented `REFUSED:` line that follows. |
| `REFUSED - this package is NOT publishable, and <folder> could NOT be deleted: <reason>` | The rejected staging folder remains on disk. | Close anything holding its files, remove that rejected folder, and package again. Do not ship it. |
| `REFUSED: STAGING FAILED while copying into <folder> - <reason><details>` | Copying or checking the staged files failed. | Resolve the named file or access problem and retry. Check whether the line says the folder remains. |
| `REFUSED: <file> - a PATCHED COPY of a Phoenix Point bundle. <details>` | A local patched game bundle entered the project. | Remove `Patched\` from the author project. Players generate their own copies. |
| `REFUSED: <file> - a SHIPPED PHOENIX POINT BUNDLE IDENTITY. <details>` | The package contains a game bundle rather than only its own declared bundle. | Remove the shipped bundle. Keep the replacement row and source files. |
| `REFUSED: meta.json declares no "ID" - the mod manager keys every mod on it.` | The mod has no manager ID. | Set `ID` in `meta.json`. |
| `REFUSED: meta.json does not declare "Dependencies": [ "com.morgott.ContentTool" ] - <details>` | The player could enable the mod without ContentTool. | Add that dependency to `meta.json`. |
| `REFUSED: meta.json declares "AssemblyName": "<dll>" but the package does not contain that file - the game refuses to load the mod. Build it, or set "AssemblyName": "" for a content-only mod.` | The declared DLL was not staged. | Build the DLL, or clear `AssemblyName` if the mod has no DLL. |
| `REFUSED: this package ships nothing at all - no asset file, no assembly, and a ppcontent.json that declares no "replace", "publish", "sounds", "creature" or "weapons" row. <details>` | The staged project has no playable payload. | Add the intended declaration or build its asset or assembly. |
| `REFUSED: the "video" row '<video>' names no .webm/.mp4/.mov under Content\Videos\ - <details>` | A declared video clip is absent from the package. | Add or rename the clip and package again. |
| `REFUSED: the "video" row '<video>' is answered by <a> and <b> in Content\Videos\ - both are SKIPPED, so the row plays nothing. Keep one, then package again.` | Two clips share the declared stem. | Keep one. |
| `REFUSED: <file> - a sound replacement that was NEVER BAKED. <details>` | A replacement source has no runtime bank. | Run `ct_sound bake <YourMod>`, then package again. |
| `REFUSED: <file> - CHANGED SINCE IT WAS BAKED. <details>` | The source differs from the bank recorded in `sources.ledger`. | Run `ct_sound bake <YourMod>`, then package again. |
| `WARN: <file> is newer than its bank in Dist\Sounds and no sources.ledger entry says which bytes that bank holds. <details>` | Without a ledger entry, the file date alone cannot prove the bank is stale. Packaging warns instead of refusing. | If you edited that source after baking, run `ct_sound bake` again. |
| `LEFT BEHIND <n> source file(s), <bytes> B: <files> - <details>` | Baked replacement-audio sources stayed in the author project but were omitted from the package; their banks ship instead. | No fix if the bank is current. |

A successful package check does not rerun `ct_project`. Require a successful bake before packaging.

## Sound replacement (ct_sound bake)

| Message | Meaning | Fix |
|---|---|---|
| `ct_sound bake: nothing declared` | No replacement row or numeric media-ID source was found. | Add a `sounds` row or a `<mediaId>.wav`, `.ogg` or `.mp3` file under `Content\Audio\Replace\`. |
| `ct_sound bake REFUSED: "sounds" names '<file>' for media <id>, and there is no such file in <folder> - nothing was baked` | The declared source file is absent. | Put it in the named folder or correct `file`. |
| `ct_sound bake REFUSED: two files aim at media <id>: '<a>' and '<b>' - nothing was baked` | Two declarations target one shipped media ID. | Remove or correct one declaration. |
| `ct_sound bake REFUSED: Content\Audio\Replace\ holds two files for the same media: <a> and <b> - one of them has to go - nothing was baked` | Two source files share a stem. | Keep one file. |
| `ct_sound bake REFUSED: "sounds" row REFUSED: <details> - nothing was baked` | A `sounds` row lacks a usable media ID or file name. | Complete or remove the row. |
| `bake REFUSED <id> is not one of the <count> media IDs Phoenix Point owns - nothing would ever play it` | The ID is not a shipped sound target. | Find the shipped media ID with `ct_list audio <filter>`. |
| `bake REFUSED <file> <reason>` | That source could not be decoded. Other replacement rows continue. | Re-export the file as WAV, OGG or MP3. |
| `bake REFUSED <file> <reason> - replacements are mono or stereo only; export a stereo mix - this project's other sounds are unaffected` | The replacement has more than two channels. | Export a mono or stereo mix. |
| `baked <path>: <bytes> B, bankId=<id>, media <media> = <details> from <file>` | One replacement bank was built. | Read the final bank count; a different row may still have refused. |
| `ct_sound bake: <baked>/<declared> bank(s) in <folder>, <refused> refused - NO game file was opened for writing. ContentTool loads these at init.` | Final replacement-bank result. | Resolve every refusal before packaging. A declared row that refused this run can retain its earlier bank. |

## Video (ct_video)

| Message | Meaning | Fix |
|---|---|---|
| `VIDEO FAIL '<video>' is not a .webm/.mp4/.mov under Content\Videos\ - ct_video would skip this row` | The bake found no usable clip for a declared video row. This counts toward `FAILURE(S)`. | Put the clip directly in `Content\Videos\` or correct the stem. |
| `VIDEO FAIL '<asset>' <- <video>: <reason> - ct_video would skip this row` | The named shipped catalog target is absent or ambiguous. | Find the intended video with `ct_list videos <nameFilter>` and correct the row. |
| `video ADD '<video>' (its RuntimeKey is printed by the command) - serve it with: ct_video live <project>` | An added video row passed the bake-side checks; no Unity bundle was patched for it. | Run the printed live command and check the clip. |
| `SKIP '<video>' is not a .webm/.mp4/.mov under Content\Videos\` | `ct_video live` could not find that source. | Correct its location or name. |
| `SKIP <reason>` | `ct_video live` could not resolve the named catalog target. | Use a unique catalog path or name from `ct_list videos`. |
| `REFUSED: no file at <path>` | Live registration cannot serve a missing clip. | Restore or correct the clip path. |
| `QUEUED: mod '<serving>' already serves key '<key>' and the lower mod id keeps it - '<owner>''s clip is held and takes over if '<serving>' lets go` | Another mod currently serves the same video key. This clip is held, not playing now. | Use one owner if the two replacements should not compete. |
| `<project>: <n> clip(s) served in memory from <path><details>; nothing in the install was written` | The live summary reports clips served, plus any held or refused rows. | Check the optional held and refused counts, then test the clip in game. |

## Extract (ct_extract/ct_list)

| Message | Meaning | Fix |
|---|---|---|
| `ct_list VOID - no shipped bundle folder at <path>` / `ct_list VOID - no bundle at <path>` | The game folder or named bundle is absent. | Check the installation and bundle name. |
| `ct_list REFUSED - <reason>` | A requested bone, property or clip name is missing or ambiguous. | Narrow the request using a name from `ct_list assets`. |
| `ct_extract VOID - no bundle at <path>` | The named bundle cannot be extracted from. | Use a bundle listed by `ct_list bundles`. |
| `ct_extract VOID - no audio folder at <path>` / `ct_extract VOID - no video folder at <path>` | The corresponding shipped-media folder is unavailable. | Check that the game installation is present. |
| `ct_extract REFUSED - <reason>. The names it holds: <list command>` | A texture, mesh, sound or video name is missing or ambiguous. | Run the printed `ct_list` command and use an exact or unique name. |
| `ct_extract wrote <path> (<bytes> B) from <details>` | A texture or mesh was extracted. | Open the written PNG or GLB and inspect it. |
| `ct_extract wrote <wem> and <wav> (<details>)` | A loose shipped sound and its decoded WAV were written. | Edit the WAV copy for a replacement source. |
| `ct_extract wrote <wem> (<bytes> B) - NO .wav: <reason>` | The original WEM was copied, but decoding to WAV failed. | Read the reason; choose another source or fix the decoder issue before using it. |
| `ct_extract audio --all: decoding <n> matched loose media into <folder> in the background - the game stays playable; progress every 200 and the closing 'extracted N of M' line print to the log` | Bulk audio extraction started. The command returns before it finishes. | Wait for the closing line in the log. |
| `ct_extract REFUSED - an 'audio --all' run is already decoding; wait for its 'extracted N of M' line in the log, then run it again` | One bulk audio run already owns the extractor. | Wait for it to finish. |
| `extracted <done> of <matched> loose media (<in-bank> in-bank skipped) into <folder><details>` | Bulk extraction finished; any decode failures are named above it. | Check preceding `FAILED` lines if the count is short. |

## Textures versus materials

`Content\Textures\` imports PNG and JPG sources. Put the image directly in that folder. `Content\Meshes\materials\` is an older Resource Replacer layout; ContentTool does not import textures from it. A `material` replacement row changes one serialized number, such as `_GlossMapScale=0.15`; it does not load an image or a material file.

A misplaced source produces a `P1 REFUSED` line. A missing shipped `Texture2D` produces `P1 REFUSED target`. Fix the source folder in the first case; use the printed `ct_list assets` command in the second.

## Added sounds bake but do not play

New sounds under `Content\Audio\` can bake and pass the `BANK` self-check, but ContentTool unloads that bank after the check. It does not load the added bank when players run the mod. The mod's own DLL must load it, or the new sounds are silent. Streamed added sounds are not packaged. Replacement banks made from `Content\Audio\Replace\` by `ct_sound bake` are loaded by ContentTool without a mod DLL. See [Known limitations](../known-limitations.md).
