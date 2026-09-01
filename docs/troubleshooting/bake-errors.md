# Read the result, not the last file line

ContentTool 1.1.2 reports work at three levels. Do not treat them as synonyms.

| Text | What happened | Can later work continue? |
|---|---|---|
| `SOURCE SKIPPED:` | One source file could not be read. That source is absent from this bake. | Yes. Other sources still bake, but the run ends in `FAILURE(S)`. |
| `P<n> REFUSED` | One declared replacement was not fulfilled. | Yes. Other rows still run, but the summary counts this refusal. |
| `ct_project: ALL PASS` | Every check in this bake passed. | This is the result you want before packaging. |
| `ct_project: N FAILURE(S)` | The bake finished, but `N` checks or declared rows failed. | Fix every earlier failure and bake again. |
| `ct_package REFUSED` or `REFUSED - this package is NOT publishable` | Packaging itself stopped. | No publishable package was left behind. |

`WROTE`, `patch` and `copies ready` describe intermediate work. They do not cancel a later
`ct_project: N FAILURE(S)`.

## Textures versus materials

These are three different things:

- `Content\Textures\` is the only texture import folder. Put PNG, JPG and JPEG files directly in it.
- `Content\Meshes\materials\` is the old Resource Replacer layout. ContentTool does not scan it for
  textures, even if a file there has the right name.
- A ContentTool `material` row changes one serialized float property such as
  `_GlossMapScale=0.15`. It does not load a material file or a texture.

```text
MyMod\
  meta.json
  ppcontent.json               <- texture row or material-property row
  Content\
    Textures\
      soldier_albedo.png       <- imported texture source
    Meshes\
      soldier.glb              <- replacement geometry
      materials\
        soldier_albedo.png     <- old layout; ignored by the texture importer
```

If your row has `"texture": "soldier_albedo"`, move the PNG to `Content\Textures`. If your row has
`"material": "_GlossMapScale=0.15"`, no image is involved.

## Skipped does not mean successful

`SOURCE SKIPPED:` means one accepted source file was found but could not be read. `ct_project`
continues with the other sources and can still write their output. The skipped import counts as a
failure, so the run does not become `ALL PASS`. If you run `ct_package` anyway, packaging can still
stage the author files because it does not repeat the import.

`P<n> REFUSED` means a declared replacement did not happen. The row is counted, later rows continue,
and the run ends with `ct_project: N FAILURE(S)`.

`ct_package REFUSED` is different. Packaging itself stopped and removed its partial output folder.
The packager never checks whether a texture came from `Content\Textures`; that check belongs to P1
inside `ct_project`.

## A missing source is not a missing target

This is a **source placement** failure:

```text
P1 REFUSED 'rr_soldier_albedo' is not a .png/.jpg under Content\Textures\ - the file IS in the project, at Content\Meshes\materials\RR_soldier_albedo.png; move it into Content\Textures\ and bake again
```

`texture` names your file stem. The file exists, but it is in the old Resource Replacer location.
Move it to the only texture import folder:

```text
MyMod\
  meta.json
  ppcontent.json
  Content\
    Textures\
      RR_soldier_albedo.png  <- source "rr_soldier_albedo" is read here
    Meshes\
      materials\             <- old Resource Replacer layout; ContentTool does not scan it
```

This is a **target name** failure:

```text
P1 REFUSED target 'RR_soldier_albedo' is not a Texture2D in px_assault_assets_all.bundle - no Texture2D named 'RR_soldier_albedo' in unity=2019.4.31f1 assets=1735 cldbTypes=320 - list the names it does hold with: ct_list assets px_assault_assets_all.bundle Texture2D
```

The source may be fine. `asset` names a `Texture2D` inside the shipped bundle, and that exact,
case-sensitive name is absent. Run the command printed in the refusal. Copy a real name into
`ppcontent.json`.

Both are per-row `ct_project` failures. Version 1.1.2 contains missing-target failures in the P1
texture, P3 material, P4 mesh and P7 clip arms. It reports the bad row, continues with later rows and
still prints a summary.

## Texture placement never belongs to packaging

`ct_package` does not run an importer, bake a bundle or inspect where a texture sits. It stages the
allowed author files and checks package rules. This means it can package a misplaced texture even
though `ct_project` refused the declared replacement.

Always run these as separate gates:

```text
ct_project MyMod
ct_package MyMod
```

Require `ct_project: ALL PASS` before the second command. If packaging refuses, read its own
indented `REFUSED:` lines. Moving a texture fixes P1; it does not fix a package rule.

## Exact bake refusals

Angle-bracketed words below stand for values printed from your project.

| Exact output | Meaning | Fix |
|---|---|---|
| `SOURCE SKIPPED: <filename>: <reason> - SKIPPED, the project's other sources are unaffected` | An accepted source extension was found, but its decoder or parser threw. | Re-export the named file in a supported form, or delete it if it is not part of the mod. |
| `P1 REFUSED '<name>' is not a .png/.jpg under Content\Textures\` | No imported PNG/JPG/JPEG has that stem. | Put the file directly in `Content\Textures\`, or correct `texture`. Do not use `Content\Meshes\materials\`. |
| `P1 REFUSED target '<asset>' is not a Texture2D in <bundle> - <reason> - list the names it does hold with: ct_list assets <bundle> Texture2D` | The game bundle has no unique `Texture2D` with that exact name. | Run the printed command and copy the exact case. |
| `P3 REFUSED "material": "<value>" is not <property>=<number>` | The property edit did not parse. | Write one property, one `=`, and an invariant decimal such as `_GlossMapScale=0.15`. |
| `P3 REFUSED target '<asset>' is not a Material in <bundle> - <reason> - list the names it does hold with: ct_list assets <bundle> Material` | The material target is absent or not unique. | Run the printed command; choose a unique exact name. |
| `P4 REFUSED '<name>' is not a .obj or .glb under Content\Meshes\` | No imported replacement mesh has that stem. | Put the OBJ/GLB directly in `Content\Meshes\`, or correct `mesh`. |
| `P4 REFUSED target '<asset>' is not a Mesh in <bundle> - <reason> - list the names it does hold with: ct_list assets <bundle> Mesh` | The mesh target is absent or not unique. | Run the printed command; copy the exact target name. |
| `P7 REFUSED "clip": "<value>" <reason>` | The clip edit is not an accepted attribute and multiplier. | Use the attribute and numeric form reported by `ct_list clip`. |
| `P7 REFUSED target '<asset>' is not a AnimationClip in <bundle> - <reason> - list the names it does hold with: ct_list assets <bundle> AnimationClip` | The clip target is absent or not unique. | Run the printed command; copy the exact target name. |
| `FAIL AssetBundle.LoadFromFile returned null - something still holds a bundle named '<id>'. Restart, or switch that mod off in the mod manager, then bake again.` | Unity still has a bundle with the same identity open. | Disable the mod or restart the game, then bake again. |

`<reason>` is exact too. A zero-match target says `no <Type> named '<name>' in <bundle details>`.
Duplicate names say how many were found and end with `refusing to guess which one to use`.

## Sound replacement refusals

| Exact output | Meaning | Fix |
|---|---|---|
| `"sounds" names '<file>' for media <id>, and there is no such file in <dir>` | The manifest names a source that is absent from `Content\Audio\Replace`. | Move the named WAV/OGG/MP3 there or correct `file`. |
| `two files aim at media <id>: '<a>' and '<b>'` | Two rows replace one shipped media ID. | Delete one row or give it the intended different media ID. |
| `bake REFUSED <id> is not one of the <count> media IDs Phoenix Point owns - nothing would ever play it` | The target is not a shipped Wwise media ID. | Find the correct shipped ID; do not use this route to add a sound. |
| `bake REFUSED <filename> <reason>` | The source decoder refused the file. | Re-export it as a supported WAV, OGG or MP3 and remove the bad file. |

## Project and package refusals

| Exact output | Meaning | Fix |
|---|---|---|
| `ct_project THREW System.IO.FileNotFoundException: no ppcontent.json in <root>` | The command resolved the wrong or incomplete project folder. | Put `ppcontent.json` at the project root. Remove or rename a stale fallback project. |
| `ppcontent.json needs both "id" and "bundle"` | A required root value is empty or absent. | Add both root strings. |
| `Content\<folder>\ holds two files with the same name: <a> and <b> - a replacement names the stem, so one of them has to go` | Two accepted sources have the same stem ignoring case. | Delete or rename one and update its manifest reference. |
| `nothing to bake - put .png/.jpg under Content\Textures\, .glb under Content\Models\ or .wav under Content\Audio\` | No own-bundle source and no usable replacement row was present. | Check direct placement and the parsed manifest. A stat-only weapon is intentionally built at runtime instead. |
| `REFUSED - this package is NOT publishable, and <outDir> has been deleted rather than half-written.` | One or more package validation rules failed. | Read every indented `REFUSED:` below it. The partial output directory was deleted. |

The packager commonly asks you to remove `Patched\`, build the DLL named by `AssemblyName`, add the
`com.morgott.ContentTool` dependency, or run `ct_sound bake` for declared sound replacements.
