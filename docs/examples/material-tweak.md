# MaterialTweak

This demo makes only the damaged Fireworm material less glossy. An injured Fireworm should look
matte; the normal Fireworm material is untouched.

**Corresponds to:** [Change a material number](../recipes/material-properties.md).

## Features and how they work

- **One serialized material float changes.** A **Replace** row sets `_GlossMapScale=0.15` on the
  shipped Material asset `ALN_Fireworm_DMG` in `aln_fireworm_assets_all.bundle`.
- **The edit is limited to the damaged skin.** The target is the uniquely named `_DMG` material,
  not the ambiguous `ALN_Fireworm` materials in that bundle.
- **There is no new art and no DLL.** `ppcontent.json` is the whole mod. ContentTool bakes a private
  patched copy of the shipped bundle when the mod is enabled.

## Project on disk

```text
MaterialTweak\
  meta.json                 <- ID is morgott.demo.materialtweak; depends on ContentTool
  ppcontent.json            <- one Replace row for ALN_Fireworm_DMG
  README.md                 <- demo notes
```

The operative block is:

```json
"replace": [
  {
    "bundle": "aln_fireworm_assets_all.bundle",
    "asset": "ALN_Fireworm_DMG",
    "material": "_GlossMapScale=0.15"
  }
]
```

## Rebuild and run it

```text
ct_list assets aln_fireworm_assets_all.bundle Material Fireworm
ct_list props aln_fireworm_assets_all.bundle ALN_Fireworm_DMG
ct_project MaterialTweak
ct_package MaterialTweak
```

Enable the mod and damage a Fireworm. ContentTool serves a private bundle copy while the mod is on;
you do not install a patched game bundle.

## What a good run prints

```text
patch aln_fireworm_assets_all.bundle: material 'ALN_Fireworm_DMG' _GlossMapScale=0.15
P3 PASS material 'ALN_Fireworm_DMG' in the copy carries _GlossMapScale=0.15 -> <read-back value>
copies ready in <path> - nothing to install: ticking 'MaterialTweak' on in the mod manager redirects them (dev-only shortcut: ct_route7 apply MaterialTweak)
ct_project: ALL PASS - this project has no bundle of its own; the patched copy(ies) above are the whole output
```

## Verification status

**Not recorded in the verification ledger.** The demo README describes a 2026-08-28 live property
reading, but `internal-docs/evidence/VERIFIED-DEMOS.md` has no `MaterialTweak` row. `TODO(verify)`:
add a ledger row with the normal and damaged material values from one measured run.
