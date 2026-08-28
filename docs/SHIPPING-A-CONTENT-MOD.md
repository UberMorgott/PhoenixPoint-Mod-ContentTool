# How a ContentTool mod is made

This is the complete authoring loop. The examples use a project named `MyMod`; substitute your own
folder name and identifiers.

```text
make the folder -> discover targets -> add your files -> preview -> bake -> package -> reinstall the package -> ship
```

The usual working layout puts your project beside ContentTool:

```text
Phoenix Point\
  Mods\
    ContentTool\
    MyMod\
      meta.json
      ppcontent.json
```

The project does **not** need to be ticked in the mod manager to bake or package it. Every authoring
command takes the bare name `MyMod`, never a path, and looks first for
`Mods\MyMod\ppcontent.json`, then for `Mods\ContentTool\MyMod\ppcontent.json`. This lookup does not
consult the mod roster or its enabled flags. The checkbox matters only when **running** the mod;
the sibling folder wins if both locations contain a manifest.

## 1. Understand the clone model

Cloning is the foundation. ContentTool takes a creature or weapon the game already has and copies
its definition. From that donor come the component structure, navigation agent type and footprint,
combat behavior, and—above all—the Animator state machine. What you bring is appearance and
movement: your model, textures and clips. What the donor brings is what the object is and what the
game knows how to ask it to do.

Choose a donor by capability, not by looks and not by the names of its clips. The states in its
controller are the ceiling on the actions your content can perform. Its movement and combat
machinery decide where it can go and how it fights. ContentTool replaces every overridable donor
clip with yours; it does not reuse donor motion or retarget it onto your rig.

A poor donor leaks its nature into the clone. An early small-spider build used a large donor and
inherited a 3×3 navigation footprint plus a component that demolished walls as it moved. The
creature route now defaults to `Swarmer_TacCharacterDef` because it is a one-tile donor with the
minimum required combat structure.

The donor and ContentTool's automatically selected one-tile reference unit are different jobs. The
donor supplies structure. The reference supplies the navigation agent, cursor and movement marker.
Nav-area names are scoped to the agent type, so the ground mask must come from that same reference;
copying a `Walkable*` area from a differently scoped donor can create a creature that walks nowhere.

Traversal rights come from navigation areas, not from animations. A creature with no climb area is
never offered a climb link. A creature with the area but no reachable clip can be routed onto it and
then stall. ContentTool therefore adds an area only after it has filled the corresponding controller
slots. It can synthesize ordinary climb/drop motion from a walk cycle, but it cannot add a state the
shipped controller does not have. In particular, climbing up one full level is unavailable on the
Humanoid controller used by current custom creatures.

This model applies beyond creatures: publishing a model only gives it an address, adding audio only
gives it an event/media identity, and cloning a weapon only gives it the donor's existing behavior.
If the game has no consumer, state or trigger for something, content files alone cannot invent one.

## 2. Create the two files

Create `Phoenix Point\Mods\MyMod\meta.json`:

```json
{
  "ID": "yourname.mymod",
  "AssemblyName": "",
  "Version": "1.0.0",
  "Author": [
    { "Key": "English", "Value": "Your Name" }
  ],
  "Name": [
    { "Key": "English", "Value": "My Content Mod" }
  ],
  "Description": [
    { "Key": "English", "Value": "Replaces one Phoenix Point asset. Requires ContentTool." }
  ],
  "Dependencies": [
    "com.morgott.ContentTool"
  ]
}
```

Create `Phoenix Point\Mods\MyMod\ppcontent.json`:

```json
{
  "id": "yourname.mymod",
  "bundle": "MyMod.bundle"
}
```

`meta.json` makes the folder a Phoenix Point mod. `ppcontent.json` makes it a ContentTool project.
Keep `ID` and `id` identical even though the current implementation does not cross-check them:
Phoenix Point uses the first as the mod identity, while ContentTool uses the second for bundles,
banks, keys and caches.

`bundle` is unrelated to the project folder name. Choose any filename; matching `MyMod.bundle` to
`MyMod\` is only a convention used by the demos. A packaged mod may contain **at most one**
`.bundle`, and if present its name must equal the declared `bundle` (case-insensitively). A
manifest-only payload is valid: material-only, replacement-sound-only, added-video, and stat-only
weapon projects can legitimately package without a `.bundle`.

The dependency is mandatory for a real release. With ContentTool off, a code-less content mod does
**not load at all**: Phoenix Point logs `Failed to enable mod`, and the enabled state does not stick.
ContentTool supplies the in-memory loader shim that makes a folder with no assembly loadable. The
dependency auto-enables ContentTool and orders it before the content mod.

### No DLL is the normal case

`"AssemblyName": ""` is correct for a content-only mod. There is no stub DLL. ContentTool patches
Phoenix Point's loader in memory so the checkbox can enable a folder that contains only content.

Exactly three kinds of work need a DLL: weapons, creatures, and anything requiring a trigger or def
edit. Playing an added sound and triggering an added video are trigger examples; merely serving
either asset is content-only. Start from the complete [project, assembly references, deployment
loop and `ModMain` surface](guides/behavior-dll.md). Build that DLL in your own IDE before packaging;
`ct_package` never compiles.

A mod that ships its own DLL can be discovered without the shim, but that does not make its
ContentTool calls work while the dependency is absent. Declare the dependency for both code-less and
code-bearing ContentTool mods.

## 3. Discover the target before making art

Do not guess bundle names, asset names, def names, media IDs, bone names or shader properties. Use
the [discovery workflow](guides/discovery.md).

### Open the developer console

The console is **locked by default, but unlocks automatically whenever Phoenix Point is launched
with mods**. That is every reader who has installed ContentTool; ContentTool itself does not need to
unlock it.

Press the physical US-layout backquote-key position (left of `1`) to open or close the console.
Slash also opens it while it is hidden, and Escape closes it. These keys are hardcoded and cannot be
rebound; on a non-US keyboard use the same physical key position even if the printed character is
not a backquote.

If a launcher failed to start the game with mod support, press this fallback sequence in order:

```text
Up Down Left Right S N A P S H O T
```

It unlocks and opens the console. There is no settings-file switch for console access.

You can also run commands without opening the console. Put `autorun.txt` beside the mod, or point
the `CT_AUTORUN` environment variable at a command file. Each non-comment line goes through the
same command dispatcher, and output is written to `Player.log`.

For a texture replacement, for example:

```text
ct_list bundles acidworm
ct_list assets aln_acidworm_assets_all.bundle Texture2D acidworm
ct_extract tex aln_acidworm_assets_all.bundle acidworm_low_albedo
```

The listing's `m_Name` is the exact, case-sensitive `asset` value. The extracted PNG is a reference
for size, UV layout and alignment. Do not redistribute extracted Phoenix Point art.

## 4. Gather your source files

Put your own files in the folder that describes their route:

```text
MyMod\
  meta.json
  ppcontent.json
  README.md
  SOURCES.md
  Content\
    Textures\       .png .jpg .jpeg used by texture rows or added models
    Meshes\         .obj .glb used by mesh rows
    Models\         .glb added as whole prefabs
    Audio\          .wav .ogg .mp3 added to your own sound bank
      Replace\      .wav .ogg .mp3 aimed at shipped media IDs
    Videos\         .webm .mp4 .mov replaced or added as catalog rows
  Icons\            PNG files used by weapon entries or your own DLL
```

Only create the folders you use. The file stem is its identifier and is lowercased by the importer:
`Content\Textures\AcidSkin.png` is named `acidskin` in the manifest. Renaming the file therefore
renames the identifier; update every reference in the same edit. Two files with the same stem in one
folder are refused rather than chosen by extension.

`Icons\` is relative to the mod folder, accepts PNG files only, and is included by `ct_package`.
Inside JSON, `"Icons\\rifle.png"` is the path `Icons\rifle.png`; the doubled backslash is only JSON
escaping.

Add all route declarations to the same `ppcontent.json`. Root-key order and `replace` row order do
not matter. One mod may replace textures and sounds, add models and videos, and declare a creature or
weapons together. The only exclusivity rule is inside one `replace` row: it must contain exactly one
of `texture`, `material`, `mesh`, `clip` or `video`.

See the [combined project](guides/combined-example.md) before splitting related work into separate
mods.

## 5. Preview and iterate

There are two loops. Use the ordinary loop for every route. Use live file preview where its current
target-path plumbing can reach the object.

### Ordinary loop: always available

1. Save your source and manifest.
2. Run the route's bake or live-apply command.
3. Put the target on screen or make the sound/video happen.
4. Read the first refusal in the console or `Player.log`.
5. Edit and repeat.

For bundle content and shipped-bundle replacements:

```text
ct_project MyMod
ct_route7 apply MyMod
```

For published keys:

```text
ct_catalog apply MyMod
ct_catalog verify
```

`ct_route7 apply` is the author's preview for bundle replacements; `ct_catalog apply` is the
author's preview for newly published keys. They apply the current project in memory so you can
inspect it without restarting, and they are not release steps. A player's game applies every
declared route automatically when the packaged mod is enabled, and reconciles video, sound and
catalog state during startup. Players run neither command.

`ct_route7 apply` applies live unless the target shipped bundle is already loaded. In that case it
refuses by bundle name with `REFUSED: restart required: <bundle> is already loaded`; restart, then
apply before opening the screen or scene that loads it.

For a video row:

```text
ct_video live MyMod
```

For replaced audio:

```text
ct_sound bake MyMod
```

`ct_project` rewrites `ppcontent.json` when a `creature` block needs its discovered clip list. Save
or close the file in your editor before running the command; do not overwrite the generated list
with an older unsaved buffer.

### Live file preview: textures and meshes

`ct_dev` watches files behind an existing `ct_replace` preview binding. It does **not** read
`ppcontent.json` and create those bindings automatically. Once a binding exists:

```text
ct_dev on MyMod
```

Save the bound PNG, JPG, GLB or OBJ under the project and it is re-read after a 0.5-second quiet
period. Scene changes are rescanned every 3 seconds. Check the watcher and binding count with:

```text
ct_dev status
```

Variant sets live here:

```text
MyMod\
  Content\Textures\acidworm.png
  select\
    Red\acidworm.png
    Blue\acidworm.png
```

`Default` means the authored file. A set supplies a same-named alternative; files absent from that
set fall back to the authored version. Switch with F12 or explicitly:

```text
ct_dev sets
ct_dev set Red
ct_dev next
```

Turn the loop off when finished:

```text
ct_dev off
ct_revert
```

!!! warning "Advanced preview plumbing"
    The first `ct_replace` binding currently depends on engineering-oriented discovery commands:
    `ct_seamprobe on` for an Addressables GUID target or `ct_scan on` for a unique live object name.
    Load the screen containing the object, obtain the anchor, and bind a supported slot:

    ```text
    ct_replace guid:<32-lowercase-hex>#<transform>@Renderer.materials[0].tex:_MainTex Mods/MyMod/Content/Textures/acidworm.png
    ct_replace name:<unique-object-name>#<transform>@MeshFilter.mesh Mods/MyMod/Content/Meshes/prop.glb
    ```

    Those commands are preview instrumentation, not manifest syntax and not part of a release. The
    shipped tooling still cannot list all live object paths, renderer indices or Addressables GUIDs
    from a manifest target, so this immediate loop is not available for every asset. Use the ordinary
    bake/apply loop when no unambiguous target path can be established. Material-number previews are
    not file-backed, so F12 does not vary them.

## 6. Bake every route the project uses

The two bake commands are independent:

```text
ct_project MyMod
ct_sound bake MyMod
```

Run `ct_project` after changes to non-video `replace` or `publish` rows, creature/model content,
textures, meshes or added audio. It writes your own bundle to `MyMod\Dist\MyMod.bundle` when that
route needs one. It also builds private patched copies of shipped bundles under the user's AppData;
those copies are Phoenix Point data and never belong in your mod.

Run `ct_sound bake` after changes under `Content\Audio\Replace\`. It writes one replacement bank per
media ID under `MyMod\Dist\Sounds\`.

If the project uses both bake families, run both commands. Neither invokes the other. Video rows
need neither bake: `ct_video live` reads `ppcontent.json` and the loose file under `Content\Videos`
directly during authoring, and enabling the packaged mod applies the same route for players.

Before moving on, deal with every `REFUSED`, `FAILURE(S)` and `SOURCE SKIPPED` line. A successful
bundle bake ends with:

```text
ct_project: ALL PASS - <project>\Dist\MyMod.bundle
```

## 7. Package inside the game

Run:

```text
ct_package MyMod
```

This command does not bake and does not compile. It stages only player-facing files from the current
project into:

```text
%USERPROFILE%\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\ContentTool\Packaged\MyMod\
```

It replaces an earlier package folder before staging a new one. On success it prints:

```text
PACKAGED 5 file(s), 335 B into C:\Users\<you>\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\ContentTool\Packaged\MyMod
Zip the FOLDER itself, so the archive holds MyMod\meta.json, and upload it.
```

The package allowlist is `meta.json`, `ppcontent.json`, readme/licence/source notes, `Content\`,
`Icons\`, `Dist\`, plus the real DLL named by `AssemblyName`. Source audio already represented by a
`Dist\Sounds\<mediaId>.bnk` is left out of the release.

The packager refuses and deletes a half-staged output if it finds, among other causes:

- no `meta.json` or `ppcontent.json`;
- no `ID`, no ContentTool dependency, or a declared DLL that is missing;
- no actual payload or declared content route;
- an unbaked replacement sound;
- `Patched\`, a shipped Phoenix Point bundle, `.ct-backup`, `.ct-new`, `.ct-edits` or `catalog.json`.

Fix the named cause in the working project, bake again if necessary, and rerun `ct_package`.

## 8. Test the packaged folder as a player

Do not call the authoring folder “tested.” Test the exact staged package:

1. Exit Phoenix Point.
2. Move `Phoenix Point\Mods\MyMod\` somewhere outside `Mods\`. Do not merely rename it inside
   `Mods\`; it would still be a discoverable top-level mod.
3. Copy the packaged `MyMod\` folder into `Phoenix Point\Mods\`.
4. Confirm the final path is `Phoenix Point\Mods\MyMod\meta.json`.
5. Start the game and tick **My Content Mod**. The dependency should enable ContentTool.
6. Exercise every route: load the asset, trigger the sound/video, begin a new campaign when the mod
   changes starting storage or squad composition.
7. Disable the mod and test the result using the route-specific behaviour below.
8. Exit, relaunch, and test once more. This catches output that existed only in the author session.
9. Delete the packaged test copy and move the authoring folder back to
   `Phoenix Point\Mods\MyMod\` before editing again.

Players never run `ct_project`, `ct_sound bake`, `ct_dev`, `ct_route7`, `ct_catalog`, `ct_video` or
`ct_package`. They install the folder and tick the checkbox.

### What unticking removes

| Route | What happens in the current session | Clean undo |
|---|---|---|
| Texture, mesh, material, or clip replacement | Its live bundle redirection is removed immediately. An object already loaded from the patched copy can remain until its screen or scene reloads. | Reload the screen or scene; restart if the object stays resident. |
| Published key | The appended Addressables locator and ownership are removed immediately. An asset already loaded through the key remains resident. | Restart to clear an already-loaded asset. |
| Video replacement or addition | The row's live mapping is removed and a shipped cutscene resolves to its shipped file again without a restart. Let an active cutscene finish or reload its screen. | No restart for the mapping. |
| Added or replacement sound | The bank deliberately remains loaded: unloading it made the event go silent instead of falling back to shipped media. It cannot be undone safely in-session. | Restart is the clean undo. |
| Added weapon | The created defs remain for the session, while a published model key is removed as described above. The weapon can remain with its art unavailable. | Restart is the clean undo. |

## 9. Zip and ship

Zip the **folder**, not just its contents. The archive must begin like this:

```text
MyMod.zip
  MyMod\
    meta.json
    ppcontent.json
    ...
```

A contents-rooted archive puts `meta.json` directly into `Mods\` when a player chooses “Extract
here”; Phoenix Point discovers only top-level directories containing `meta.json`.

Ship only files you have the right to redistribute. Keep `SOURCES.md` and required attribution in
the package. Extracted Phoenix Point files are references for authoring, not release assets.

## Working and shipped trees

This is a real combined route shape before packaging:

```text
MyMod\
  meta.json
  ppcontent.json
  README.md
  SOURCES.md
  Content\
    Textures\acidworm.png
    Models\field_scanner.glb
    Audio\scanner_ping.wav
    Audio\Replace\ui_confirm.mp3
  Icons\scanner.png
  Dist\
    MyMod.bundle
    Sounds\633458426.bnk
```

After `ct_package MyMod`, the packaged tree keeps the texture, model and added sound, keeps both
baked outputs, and drops only the baked replacement sound's source. Texture and mesh replacements
are rebuilt on the player's machine from the loose sources under `Content\`, so those sources must
ship; the player's game does not read them from your bundle.

```text
MyMod\
  meta.json
  ppcontent.json
  README.md
  SOURCES.md
  Content\
    Textures\acidworm.png
    Models\field_scanner.glb
    Audio\scanner_ping.wav
  Icons\scanner.png
  Dist\
    MyMod.bundle
    Sounds\633458426.bnk
```

Nothing under `select\` is on the allowlist. Keep variants in the author project; move the chosen
file into `Content\` before the final bake.
