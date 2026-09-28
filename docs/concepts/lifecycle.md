# Validate, bake, apply, verify, package

Keep the project beside ContentTool under `<Phoenix Point>\Mods\`. The bench's [Build & share](../bench/lifecycle-tab.md) screen runs the five stages in order under plain names: `Check files` (Validate), `Build` (Bake), `Install` (Apply), `Verify install` (Verify) and `Package for sharing` (Package). For a console workflow, bake with `ct_project <project>` and stage the release with `ct_package <project>`; enable and test the mod through the game.

## Where the files land

```text
<Phoenix Point>\
  Mods\
    ContentTool\
    MyMod\
      meta.json
      ppcontent.json
      Content\                    <- author sources
      Dist\
        MyMod.bundle              <- mod-owned bundle, when this route makes one
        Sounds\
          <mediaId>.bnk           <- sound replacement bank
          sources.ledger          <- records the bank's source
      WwiseAudio\                 <- bake-time stream extraction; not packaged

<persistentDataPath>\
  ContentTool\
    Patched\
      <8-hex install tag>\
        <ppcontent.json id>\
          <shipped bundle>        <- private copy; never distribute
    Packaged\
      MyMod\                      <- console package output; inspect and zip this folder
```

`<persistentDataPath>` is Phoenix Point’s writable player-data directory. The eight-character tag separates patched copies made for different game installations. The bench’s `Package for sharing` chooses a new output directory per run and displays its path.

## 1. Validate

In the bench, select your project, open `Details and log` and press `Run` on **Check files**. It checks the manifest and source placement without writing output. You should see a line beginning `Validate: PASS - ` and a cache key. Fix any refusal before baking. There is no separate console Validate command in this workflow.

## 2. Bake

=== "Bench"

    Press `Run` on **Build** after **Check files** passes, or press `Build & test` to run Check files, Build, Install and Verify install in order. Keep Build & share open. You should see the log finish with `ct_project: ALL PASS - <output>`.

=== "Console"

    Enter the project **folder name**:

    ```text
    ct_project MyMod
    ```

    You should see an import report, checks of the written output, and a final `ct_project: ALL PASS - <output>` line.

Bake imports sources and checks what it wrote. Bundle replacement rows produce private patched copies under `Patched\<8-hex install tag>\<id>\`; mod-owned content may also produce `Dist\<bundle>`. A project made only of bundle replacements can end with:

```text
ct_project: ALL PASS - this project has no bundle of its own; the patched copy(ies) above are the whole output
```

Read the **last line**. Every `SOURCE SKIPPED:` or refused replacement counts as a failure even if later rows continue and some output is written. A failed run ends in `ct_project: <n> FAILURE(S)`. See [messages](../reference/messages.md).

### Sound replacements have a separate bake

`ct_project` does not bake `Content\Audio\Replace`. For a project replacing shipped sound media, run:

```text
ct_sound bake MyMod
```

This writes one `Dist\Sounds\<mediaId>.bnk` per replacement and a `Dist\Sounds\sources.ledger`. You should see a final line beginning `ct_sound bake: <baked>/<declared> bank(s) in `; check its refused count before packaging. Replacement sources must be mono or stereo.

!!! warning "Added sounds"
    New sounds under `Content\Audio` bake and self-check, but ContentTool does not load their added bank when players run the mod. They are silent unless the mod’s own DLL loads that bank. Sound replacements through `Content\Audio\Replace` work without a DLL. Streamed added sounds are extracted to `WwiseAudio` during baking and are not packaged.

A video row is checked during bake, but its clip stays under `Content\Videos\` and is served live when the mod is enabled.

## 3. Apply

Tick the mod in the mod manager, or run **Install** in the bench. You should see the mod enabled or an Install result in Build & share. ContentTool redirects future requests for the named shipped bundles to their private copies. It does not change files in the game installation.

A redirect cannot replace a bundle Unity has already loaded. If Install reports **restart required**, restart with the mod enabled before the game first requests that bundle. A copy already being served by the game also cannot be rewritten by another bake in that session; restart before baking again.

### One owner for a shared target

If two enabled mods claim the same shipped bundle, only the mod whose `ppcontent.json` `id` sorts lower owns it. The comparison is ordinal and case-sensitive, so uppercase letters sort before lowercase ones. Enable order does not make conflicting bundle replacements compatible.

The same lower-ID rule governs one replacement sound media ID and one video key. Sound banks load in lower-ID order; a conflicting bank is refused because an already loaded bank cannot be taken back during that session. A conflicting video clip is held behind the serving lower-ID mod and can take over if that mod lets go. Combine changes into one mod or use a compatibility version when two mods need the same target.

## 4. Verify

Run **Verify install** in the bench after Install, then inspect the change in the game. You should see a result beginning `Verify: PASS - ` when the load-back checks pass and all declared targets are served from this project’s copies. A shortfall leaves targets unproven. If Install requested a restart, begin a new game process before verifying.

For a visible replacement, also load the screen, mission or object that actually uses the target. A successful bake alone does not prove the game requested that asset.

## 5. Package and test

=== "Bench"

    Press `Package for sharing` after the required stages pass. You should see a `PACKAGED <count> file(s), <bytes> B into <package-path>` line and a fresh output directory.

=== "Console"

    Enter:

    ```text
    ct_package MyMod
    ```

    You should see `PACKAGED <count> file(s), <bytes> B into <package-path>` and the staged folder under `<persistentDataPath>\ContentTool\Packaged\MyMod\`.

Packaging does **not** bake sources or compile a DLL. Build the DLL yourself first if `meta.json` names one. The packager takes the named DLL, `meta.json`, `ppcontent.json`, optional `README.md`, `SOURCES.md` and license files, plus the allowed `Content`, `Icons` and `Dist` trees. It leaves out source code, build directories and `WwiseAudio`. Baked sound replacement sources are left out of the release; their `Dist\Sounds` banks are what players load.

The packager refuses invalid manifests, missing or stale sound replacement banks, video rows without a usable clip, patched game bundles and other files that must not ship. On a staged-package refusal it removes the partial output when it can. Fix the named problem and package again.

Install the **staged** folder into a clean `Mods\` setup as a player would. You should see `Mods\MyMod\meta.json`, the mod listed with its ContentTool dependency, and the change working in game. Zip the `MyMod` folder itself only after this test.
