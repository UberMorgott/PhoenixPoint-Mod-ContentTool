# Find game content

Run discovery commands in Phoenix Point with ContentTool enabled. Start broad, then add filters. The
filters are case-insensitive substrings; the final asset name you copy into `ppcontent.json` is still
case-sensitive.

Long reports also appear in:

```text
%USERPROFILE%\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\Player.log
```

## Find a texture, mesh or material

Find likely bundles:

```text
ct_list bundles fireworm
```

List one class of asset inside a bundle:

```text
ct_list assets aln_fireworm_assets_all.bundle Texture2D fireworm
ct_list assets aln_fireworm_assets_all.bundle Mesh fireworm
ct_list assets aln_fireworm_assets_all.bundle Material fireworm
```

The type and name filters are optional. Narrow them when the report says more matches were omitted.
Copy the bundle filename and asset name exactly from the result.

For a material, list the properties that a `material` row can set:

```text
ct_list props aln_fireworm_assets_all.bundle ALN_Fireworm_DMG
```

For a rigged mesh, list the shipped bone names and their bind-pose order:

```text
ct_list bones <bundleFile> <meshName>
```

For one animation clip, inspect its serialised fields:

```text
ct_list clip <bundleFile> <clipName>
```

## Find media or definitions

```text
ct_list videos <nameFilter>
ct_list audio [filter]
ct_list defs <nameFilter> [typeFilter]
```

Use `ct_list audio [filter]` to find media by name, ID or bank; the filter is a case-insensitive
substring. ContentTool 1.2.1 reads names from the game's `SoundbanksInfo.xml` and lists
`<id>  <name>  <bank>  loose|in-bank`. You can extract `loose` media; you can only list `in-bank` media.
The pane shows the first 10 rows and then names a file; the WHOLE list is always written to ContentTool\Logs\ct_list-audio-<stamp>.txt, and a capture such as PPCLI still receives every row.
The pane shows the header before those rows. The complete file includes the header and every row:
`<persistentDataPath>\ContentTool\Logs\ct_list-audio-<stamp>.txt`.
For 70 matches, the trailer is `... 60 more - the whole list is in <path>`;
for 10 or fewer, it is `... the whole list is in <path>`.
For a bank filter, use `ct_list audio Barks`.

Videos are loose files; Wwise media can also live inside soundbanks. Definitions are live game defs.
Do not put a video name into a bundle field because no such bundle exists.

## Extract an editable starting point

```text
ct_extract tex <bundleFile> <assetName>
ct_extract mesh <bundleFile> <assetName>
ct_extract video <name>
ct_extract audio <mediaId>
ct_extract audio --all [filter]
```

Use the media ID from a `.wem` filename, not an `AK.Wwise.Event` ID; see [find a sound](../recipes/sounds.md#steps).
Extract one audio file as the numeric `.wem` byte for byte plus a `.wav` named after the sound.
Use `--all [filter]` to decode matching loose media to `<ShortName>__<id>.wav` and write `index.csv` for all media, including in-bank entries. Its `loose` column holds `yes`/`no`.

A successful texture or mesh extraction starts with `ct_extract wrote <path>`. Extracted files go
under:

```text
%USERPROFILE%\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\ContentTool\
  Extracted\
    <bundle-name>\            <- texture PNGs and mesh GLBs
    videos\                   <- copied WEBM files
    audio\                    <- numeric WEM, named WAV; index.csv with --all
```

Extraction does not put the file into a project. Copy the edited result into the source folder named
by your route. For a texture replacement, that is directly under `Content\Textures\`.

## When discovery refuses

- `ct_list VOID - no bundle at <path>` means the bundle filename is wrong. Return to
  `ct_list bundles <filter>`.
- `ct_list REFUSED - no <class> named '<name>' in <bundle>` means the target name does not match
  exactly.
- `<n> <class>s are named '<name>' (pathIds <ids>) - refusing to guess which one to use` means the
  name is ambiguous. Choose another target; ContentTool will not pick the first object silently.

Next: [place the source in a supported folder](../reference/project-files.md), then
[bake and test](../getting-started/lifecycle.md).
