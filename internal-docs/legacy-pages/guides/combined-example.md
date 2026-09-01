# One mod, several kinds of content

This example exists to make one rule unmistakable: a ContentTool project is not “a texture mod” or
“a sound mod.” Every section is parsed independently, and one project can use them together.

`FieldKit` is composed for documentation; it is not one of the shipped demos. Its target names and
schema come from proven demo routes, combined into one manifest for illustration.

It does four things:

- replaces the Acidworm albedo;
- adds `scanner_ping.wav` to the mod's own Wwise bank;
- replaces shipped media `633458426` with `ui_confirm.mp3`;
- adds and publishes `field_scanner.glb` under a new Addressables key.

## Working folder

```text
Phoenix Point\Mods\FieldKit\
  meta.json
  ppcontent.json
  README.md
  SOURCES.md
  Content\
    Textures\
      acidworm_field.png
    Models\
      field_scanner.glb
    Audio\
      scanner_ping.wav
      Replace\
        ui_confirm.mp3
```

The added sound and model are discovered from their folders; there is no top-level `models` or
`videos` array. The `publish` row gives other code an address for the model. Added audio is packed
into the mod's own bank and receives a generated media/event identity during `ct_project`.

## Complete `meta.json`

```json
{
  "ID": "example.fieldkit",
  "AssemblyName": "",
  "Version": "1.0.0",
  "Author": [
    { "Key": "English", "Value": "Example Author" }
  ],
  "Name": [
    { "Key": "English", "Value": "Field Kit" }
  ],
  "Description": [
    { "Key": "English", "Value": "A composed ContentTool example: texture, sound replacement, added sound and published model." }
  ],
  "Dependencies": [
    "com.morgott.ContentTool"
  ]
}
```

## Complete `ppcontent.json`

```json
{
  "id": "example.fieldkit",
  "bundle": "FieldKit.bundle",
  "replace": [
    {
      "bundle": "aln_acidworm_assets_all.bundle",
      "asset": "acidworm_low_albedo",
      "texture": "acidworm_field"
    }
  ],
  "publish": [
    {
      "key": "example.fieldkit/models/field_scanner",
      "asset": "models/field_scanner",
      "type": "GameObject",
      "deps": "defaultlocalgroup_unitybuiltinshaders.bundle"
    }
  ],
  "sounds": [
    {
      "media": 633458426,
      "file": "ui_confirm.mp3"
    }
  ]
}
```

There is one `replace` row here, but it could hold more rows of any replacement kind. The one-kind
rule applies per row, not per array and not per mod.

## What this content-only example does not do

No DLL is required to bake, package, publish or load these assets. That is why `AssemblyName` is
empty and why there is no stub file.

The replacement routes already have consumers: Phoenix Point asks for the Acidworm texture and posts
media `633458426`. The additions do not. Publishing a model makes it addressable; it does not place
it in a scene. Adding a sound makes it loadable; it does not invent a moment to post its event. A
real mod that wants those two additions to appear would use its existing behaviour DLL—or another
dependent mod—to load the model key and post the added sound. The DLL supplies the trigger, not the
content. Any such code route must follow the shared
[profile-wide `Managed\` module warning](behavior-dll.md#managed-module-load-failure).

## Bake both output families

This project needs both bake commands:

```text
ct_project FieldKit
ct_sound bake FieldKit
```

Afterward:

```text
FieldKit\
  ...author files...
  Dist\
    FieldKit.bundle
    Sounds\
      633458426.bnk
```

`ct_project` writes the one bundle containing the replacement texture source, added model and added
sound bank. `ct_sound bake` separately writes the shipped-media replacement bank.

Package it with:

```text
ct_package FieldKit
```

The packaged folder keeps one mod bundle. It never contains the patched
`aln_acidworm_assets_all.bundle`; ContentTool builds that private copy from each player's own
installation.
