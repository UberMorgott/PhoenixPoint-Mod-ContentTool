# Change a material property

Change one numeric property on a shipped Unity `Material`. This example makes the damaged Fireworm material less glossy.

## You need

- ContentTool installed and enabled.
- The shipped bundle and exact material name.
- The serialized property name and a number. Fractions use `.`; values such as `1` and `1e-3` are also accepted.
- No image or material source file.

## Folder layout

```text
Mods\
  MyMaterialMod\
    meta.json                 <- declares the ContentTool dependency
    ppcontent.json            <- holds the material edit
```

## Steps

1. Find the material in the game console. You should see the exact name `ALN_Fireworm_DMG`.

   ```text
   ct_list assets aln_fireworm_assets_all.bundle Material ALN_Fireworm_DMG
   ```

2. List its serialized properties. Check that `_GlossMapScale` is present.

   ```text
   ct_list props aln_fireworm_assets_all.bundle ALN_Fireworm_DMG
   ```

3. Create `meta.json`:

   ```json
   {
     "ID": "example.mymaterialmod",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "My material mod" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

4. Create `ppcontent.json`. The `material` value is one property, `=`, then one number:

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

5. Bake, read the final line, and package only after `ALL PASS`:

   ```text
   ct_project MyMaterialMod
   ct_package MyMaterialMod
   ```

6. Enable the mod and load a Fireworm with damaged skin. Its material should look less glossy. Follow [the lifecycle](../concepts/lifecycle.md) when checking a bundle that may already be loaded.

## Check it worked

```text
patch aln_fireworm_assets_all.bundle: material 'ALN_Fireworm_DMG' _GlossMapScale=0.15
P3 PASS material 'ALN_Fireworm_DMG' in the copy carries _GlossMapScale=0.15 -> <read-back value>
ct_project: ALL PASS - this project has no bundle of its own; the patched copy(ies) above are the whole output
```

The first line says what was requested. `P3 PASS` reads the value back from the patched copy.

## Common errors

| What you see | Why | Fix |
|---|---|---|
| `P3 REFUSED "material": "_GlossMapScale,0.15" is not <property>=<number>` | The edit does not use `=`. | Write `_GlossMapScale=0.15`. |
| `P3 REFUSED target 'ALN_Fireworm_DMG' is not a Material in aln_fireworm_assets_all.bundle - <reason> - list the names it does hold with: ct_list assets aln_fireworm_assets_all.bundle Material` | The target name is missing or ambiguous. | Run the printed list command and copy an exact, unique name. |
| `P0 REFUSED "bundle": "<file>" is not a bundle this game ships - no file at <path> - check the spelling and list the real names with: ct_list bundles` | The bundle filename is wrong. | Run `ct_list bundles` and use the shipped filename. |

See [messages](../reference/messages.md) for other refusals.

## Example

[MaterialTweak](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/MaterialTweak) contains one `_GlossMapScale` replacement row and no art or DLL.
