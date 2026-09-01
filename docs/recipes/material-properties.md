# Change a material number

This changes one numeric property on a shipped Unity `Material`. Use it when the existing shader and
textures are right but a value such as gloss strength is not.

## What you need before you start

- The shipped bundle filename and the material's exact, case-sensitive `m_Name`.
- The serialized property name and a decimal value written with `.`.
- No source image and no material file. This route is one manifest row.

## Folder tree

```text
MyMaterialMod\
  meta.json                    <- declares the ContentTool dependency
  ppcontent.json               <- holds the complete material edit
```

## Steps

1. Find a unique material name:

   ```text
   ct_list assets aln_fireworm_assets_all.bundle Material Fireworm
   ```

2. List that material's serialized properties:

   ```text
   ct_list props aln_fireworm_assets_all.bundle ALN_Fireworm_DMG
   ```

3. Create `meta.json`:

   ```json
   {
     "ID": "example.mymaterialmod",
     "AssemblyName": "",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "My material mod" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

4. Create `ppcontent.json`. The `material` value is one `<property>=<number>` string:

   ```json
   {
     "id": "example.mymaterialmod",
     "bundle": "MyMaterialMod.bundle",
     "replace": [
       {
         "bundle": "aln_fireworm_assets_all.bundle",
         "asset": "ALN_Fireworm_DMG",
         "material": "_GlossMapScale=0.15"
       }
     ]
   }
   ```

5. Bake and inspect the result. Package only after it passes:

   ```text
   ct_project MyMaterialMod
   ct_package MyMaterialMod
   ```

## What success looks like

```text
patch aln_fireworm_assets_all.bundle: material 'ALN_Fireworm_DMG' _GlossMapScale=0.15
WROTE <patched path> <bytes> B as <bundle identity> (shipped source is <bytes> B)
P3 PASS material 'ALN_Fireworm_DMG' in the copy carries _GlossMapScale=0.15 -> <read-back value>
copies ready in <path> - nothing to install: ticking 'MyMaterialMod' on in the mod manager redirects them (dev-only shortcut: ct_route7 apply MyMaterialMod)
ct_project: ALL PASS - this project has no bundle of its own; the patched copy(ies) above are the whole output
```

## When it fails

| Exact output | Meaning | Fix |
|---|---|---|
| `P3 REFUSED "material": "_GlossMapScale,0.15" is not <property>=<number>` | The edit is not one property, `=`, and a number. | Change it to `_GlossMapScale=0.15`. |
| `P3 REFUSED target 'ALN_Fireworm_DMG' is not a Material in aln_fireworm_assets_all.bundle - <reason> - list the names it does hold with: ct_list assets aln_fireworm_assets_all.bundle Material` | No unique material has that exact name. | Run the printed command and copy the exact case. If it reports duplicates, choose a different addressable target; ContentTool will not guess a path ID. |

Read [the status glossary](../troubleshooting/bake-errors.md). A P3 refusal is a failed row in
`ct_project`; it is not `ct_package` stopping.

Before testing, read [when a shipped-bundle redirect takes effect and why only one mod can own a
bundle](../getting-started/lifecycle.md#redirects-affect-future-loads).

## Worked demo

[MaterialTweak](../examples/material-tweak.md) is this recipe reduced to one `_GlossMapScale` row.
