# Find game content

Use the developer console in Phoenix Point with ContentTool enabled. Start with a broad list, then narrow it with a name filter. Copy the final asset names exactly.

## Find a bundled asset

1. Find a likely bundle:

    ```text
    ct_list bundles [nameFilter]
    ```

2. List assets in it. The type and name filters are optional:

    ```text
    ct_list assets <bundleFile> [typeFilter] [nameFilter]
    ```

    Use `Texture2D`, `Mesh`, or `Material` as the type filter when you know what you need. A filter with no matches reports zero matches; it does not choose a different asset.
3. For a material’s properties, a rigged mesh’s bones, or an animation clip’s fields, use:

    ```text
    ct_list props <bundleFile> <materialName>
    ct_list bones <bundleFile> <meshName> [nameFilter]
    ct_list clip <bundleFile> <clipName>
    ```

You can also search live game definitions:

```text
ct_list defs <nameFilter> [typeFilter]
```

## Find a video or sound

```text
ct_list videos [nameFilter]
ct_list audio [filter]
```

Videos are loose files. `ct_list videos` shows a clip’s name and its path relative to `StreamableCopiedAssets`; a video replacement can use that listed path as its `asset` value. See [Replace a video](../recipes/videos.md).

For audio, filter by name, media ID, or bank. The console pane shows the first rows of a long list; the complete report is written to:

```text
<persistentDataPath>\ContentTool\Logs\ct_list-audio-<stamp>.txt
```

Use the media ID from the list to extract a loose sound. Media inside a soundbank can be listed but cannot be extracted by this command.

## Extract an editable copy

```text
ct_extract tex <bundleFile> <assetName>
ct_extract mesh <bundleFile> <assetName>
ct_extract video <name>
ct_extract audio <mediaId>
ct_extract audio --all [filter]
```

A texture is written as PNG, a mesh as GLB, and a video as a copied WEBM. Extracting one loose audio item writes its numeric WEM and a named WAV when decoding succeeds.

`ct_extract audio --all [filter]` decodes matching loose media in the background. It writes `index.csv` for all listed media, including entries inside soundbanks. Only one `audio --all` run can decode at a time; wait for its closing `extracted <n> of <m>` line before starting another.

The files go under `<persistentDataPath>\ContentTool\Extracted\`:

```text
Extracted\
  <bundle-stem>\    <- texture PNGs and mesh GLBs
  videos\           <- copied WEBM files
  audio\            <- WEM and WAV files; index.csv from --all
```

Extraction does not add files to a mod project. Copy the edited file into the source folder required by its route; see [Project files](../reference/project-files.md).

## If a name is refused

- `ct_list VOID - no bundle at <path>` or `ct_extract VOID - no bundle at <path>` means the bundle filename is wrong. Return to `ct_list bundles [nameFilter]`.
- A misspelled or ambiguous texture, mesh, video, or audio name produces a `ct_extract REFUSED -` line that points to the relevant listing command. List the names, then copy the exact one.
- `ct_extract REFUSED - an 'audio --all' run is already decoding` means the first batch is still running. Wait for its closing log line.

Next, [build and test the project](../concepts/lifecycle.md).
