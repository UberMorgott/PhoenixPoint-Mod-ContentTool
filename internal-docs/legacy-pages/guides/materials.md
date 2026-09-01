# Materials: change one number

The material route edits one serialized float on a shipped Material. It does not replace a shader,
texture or color and it does not create a new Material.

Find the exact Material and property:

```text
ct_list bundles fireworm
ct_list assets aln_fireworm_assets_all.bundle Material Fireworm
ct_list props aln_fireworm_assets_all.bundle ALN_Fireworm_DMG
```

The property report includes the shader reference and serialized texture, float and color entries.
Only a float entry is writable by this route, and a serialized entry does not prove the shader has
that property. On `ALN_Fireworm_DMG`, `_GlossMapScale` is a real shader property; `_Glossiness` is
serialized on disk but `Material.HasProperty("_Glossiness")` is false. The baker accepts a write to
the latter, but it is silently ineffective in the running shader.

A material-only project needs no `Content` folder:

```text
MaterialTweak\
  meta.json
  ppcontent.json
```

Use a complete manifest:

```json
{
  "id": "yourname.materialtweak",
  "bundle": "MaterialTweak.bundle",
  "replace": [
    {
      "bundle": "aln_fireworm_assets_all.bundle",
      "asset": "ALN_Fireworm_DMG",
      "material": "_GlossMapScale=0.15"
    }
  ]
}
```

The left side of `material` is case-sensitive property text from `ct_list props`; the right side is
a floating-point value. Verify the property exists on the live shader; do not rely on a
serialized-only `_Glossiness` write. Do not place a nested property object in the row.

Apply and inspect it:

```text
ct_project MaterialTweak
ct_route7 apply MaterialTweak
```

Load the exact renderer that uses the material. Materials with similar names can be separate
serialized objects, and changing one does not change every variant. When finished:

```text
ct_package MaterialTweak
```

## Limits

- Texture references, colors, vectors, keywords, render queues and shaders are not writable here.
- `ct_list props` reports values serialized on the Material, not the shader's complete declared
  property schema.
- Ambiguous Material names are refused with their path IDs. ContentTool will not choose one.
- A valid property can produce no visible difference if that shader variant does not use it in the
  active pass. Visual inspection is still required.
