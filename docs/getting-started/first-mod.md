# First green bake

Make this small material mod before you bring in your own art. It changes one number in a shipped
material. There is no source image, model or sound to decode, so a failure points to setup or target
discovery instead of an export setting.

## Before you start

You need Phoenix Point, ContentTool installed under `Mods\ContentTool\`, and ContentTool enabled in
the mod manager. The project itself does not need to be enabled while you bake or package it.

The commands take a folder name such as `MyFirstMod`. Do not paste a full path. ContentTool looks for
a sibling project first, then for a project inside its own folder.

```text
Phoenix Point\
  Mods\
    ContentTool\
      meta.json                 <- ContentTool must be installed and enabled
    MyFirstMod\                 <- pass this folder name to the commands
      meta.json                 <- the mod manager reads "ID" here
      ppcontent.json            <- ContentTool reads the change here
```

## 1. Create the folder

Create `MyFirstMod` beside `ContentTool`, exactly as shown above.

## 2. Write `meta.json`

Create `MyFirstMod\meta.json` with this content. Replace `yourname` with a stable name you control.
Use the same ID in both files in this walkthrough.

```json
{
  "ID": "yourname.firstgreen",
  "Version": "1.0.0",
  "Author": [ { "Key": "English", "Value": "Your Name" } ],
  "Name": [ { "Key": "English", "Value": "My First ContentTool Mod" } ],
  "Description": [ { "Key": "English", "Value": "A setup check for ContentTool." } ],
  "AssemblyName": "",
  "Dependencies": [ "com.morgott.ContentTool" ]
}
```

`"AssemblyName": ""` means this content-only mod has no DLL. The dependency is required for a
publishable package; without it, a player can enable your mod while ContentTool is off.

## 3. Write `ppcontent.json`

Create `MyFirstMod\ppcontent.json` with this content:

```json
{
  "id": "yourname.firstgreen",
  "bundle": "MyFirstMod.bundle",
  "replace": [
    {
      "bundle": "aln_fireworm_assets_all.bundle",
      "asset": "ALN_Fireworm_DMG",
      "material": "_GlossMapScale=0.15"
    }
  ]
}
```

The root `id` and `bundle` fields are mandatory. This replacement row names one shipped bundle, one
Material inside it, and one floating-point property to set.

## 4. Bake

Start Phoenix Point with ContentTool enabled. Press the physical key immediately left of `1` to open
the developer console, then run:

```text
ct_project MyFirstMod
```

The report starts with a count. The absolute path on your machine will differ:

```text
project 'yourname.firstgreen' at <absolute-project-path>: 0 texture(s), 0 mesh(es), 0 model(s), 0 video(s), 0 sound(s), 1 replacement(s)
```

These lines show the declared change and its read-back check:

```text
patch aln_fireworm_assets_all.bundle: material 'ALN_Fireworm_DMG' _GlossMapScale=0.15
P3 PASS material 'ALN_Fireworm_DMG' in the copy carries _GlossMapScale=0.15 -> <property-report>
P3-ctl-shipped PASS the shipped aln_fireworm_assets_all.bundle's 'ALN_Fireworm_DMG' does NOT carry it
ct_project: ALL PASS - this project has no bundle of its own; the patched copy(ies) above are the whole output
```

`ALL PASS` is the success condition. ContentTool writes the private patched copy under Phoenix
Point's `AppData\LocalLow` tree, not into the game installation and not into your release folder.

## 5. Package

Run:

```text
ct_package MyFirstMod
```

The success report begins and ends like this:

```text
PACKAGED <count> file(s), <bytes> B into <package-path>
Zip the FOLDER itself, so the archive holds MyFirstMod\meta.json, and upload it. <installation guidance>
```

The staged folder is under:

```text
%USERPROFILE%\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\ContentTool\Packaged\
  MyFirstMod\
    meta.json                 <- this folder is the root of the zip
    ppcontent.json
```

Package the `MyFirstMod` folder itself. Do not put `meta.json` at the root of `Mods\`.

## If it fails

- `ct_project THREW System.IO.FileNotFoundException: no ppcontent.json in <root>` means the command
  resolved a folder without the manifest. Check the folder name and put `ppcontent.json` at its root.
- `ppcontent.json needs both "id" and "bundle"` means one of those root fields is missing or empty.
- `P3 REFUSED "material": "<value>" is not <property>=<number>` means the material value is not one
  property name, `=`, and a number written with a decimal point.
- `P3 REFUSED target '<asset>' is not a Material in <bundle> - <reason> - list the names it does hold with: ct_list assets <bundle> Material`
  means the shipped target is missing or ambiguous. Copy the `ct_list` command from the end of the
  line and correct `asset`; do not rename your project files.
- `ct_project: N FAILURE(S)` means at least one declared operation was not fulfilled. Read upward to
  the first `REFUSED`, `FAIL` or `SOURCE SKIPPED` line.
- `meta.json does not declare "Dependencies": [ "com.morgott.ContentTool" ] - without it the player can install this mod with the engine switched off and it will silently do nothing. With it, Phoenix Point enables ContentTool for them.`
  is a packaging refusal. Restore the dependency exactly as shown, then run `ct_package MyFirstMod`
  again.

Next: [choose the route for your real mod](choose-a-route.md).
