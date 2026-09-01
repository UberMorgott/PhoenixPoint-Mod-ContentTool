# Bake, test and package

Use the same order for every project:

```text
discover target -> place sources -> edit manifests -> bake -> read the summary -> enable and test -> package -> test the package
```

Do not use `ct_package` as a build check. It does not call `ct_project`, compile a DLL, or inspect
whether a texture was placed in the import folder.

## Working project and outputs

```text
Phoenix Point\
  Mods\
    ContentTool\
    MyMod\                  <- pass "MyMod", never this path
      meta.json
      ppcontent.json
      Content\              <- author sources
      Dist\
        MyMod.bundle        <- mod-owned bundle, when this route creates one

%USERPROFILE%\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\ContentTool\
  Patched\
    yourname.mymod\         <- private copies of shipped bundles; never distribute these
  Packaged\
    MyMod\                  <- the folder you inspect and zip
```

## 1. Discover before editing

Find the target with `ct_list` and extract it when a supported extractor exists. Target asset names
are exact and case-sensitive. See [Find game content](../find-content/index.md).

## 2. Bake

Run this in the Phoenix Point developer console:

```text
ct_project MyMod
```

The first line counts what ContentTool actually imported and parsed. The last line is the run result.
Do not judge the run from a `WROTE` line in the middle.

```text
project '<id>' at <root>: <n> texture(s), <n> mesh(es), <n> model(s), <n> video(s), <n> sound(s), <n> replacement(s)
...
ct_project: ALL PASS - <output>
```

A replacement refusal is skipped so ContentTool can report and process the remaining rows. A thrown
texture, mesh or model import is handled the same way. Those cases still end in
`ct_project: N FAILURE(S)`. “Build continued” does not mean the declared change succeeded. The
[failure page](../troubleshooting/bake-errors.md#skipped-does-not-mean-successful) separates these
counted failures from a reported unsupported audio file.

## 3. Test with the mod enabled

Return to the mod manager and enable `MyMod`. Bundle replacements are redirected when the checkbox is
enabled. The line printed by a successful replacement bake says the same thing:

```text
copies ready in <path> - nothing to install: ticking 'MyMod' on in the mod manager redirects them (dev-only shortcut: ct_route7 apply MyMod)
```

`ct_route7 apply MyMod` is a developer shortcut. It is not an installation step for authors or
players. Test the real checkbox path before release.

## 4. Package

Build your DLL in your IDE first if `meta.json` names one. Then run:

```text
ct_package MyMod
```

The packager copies only its release allowlist: `meta.json`, `ppcontent.json`, the mod's named DLL,
`README.md`, `SOURCES.md`, `LICENSE`, `LICENSE.md`, and the `Content`, `Icons` and `Dist` trees. It does
not copy your project file, source code, `bin` or `obj` directories.

It refuses patched game bundles, backup files and game catalogs. On a validation refusal it deletes
the staged package instead of leaving a partial release:

```text
REFUSED - this package is NOT publishable, and <outDir> has been deleted rather than half-written.
  REFUSED: <specific reason>
```

## 5. Test the staged folder

Move or copy the staged `Packaged\MyMod` folder into a clean `Mods\` setup as a player would receive
it. Confirm that `Mods\MyMod\meta.json` exists, the mod is listed, its dependency enables ContentTool,
and the change works. Zip the folder only after that test.

Next: [read project file rules](../reference/project-files.md) or
[diagnose a failed bake](../troubleshooting/bake-errors.md).
