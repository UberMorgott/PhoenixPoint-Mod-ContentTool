# MaterialTweak — one number out of a shipped material

The smallest content mod in this folder. No art, no `.dll`, no `Content\` directory: two JSON files
and a README. It exists to show the rung between "replace a whole texture" and "replace a whole
mesh" — **change one property of a material the game already ships**.

```
demos\MaterialTweak\
  meta.json          the mod manager entry (ID, name, description, dependency)
  ppcontent.json     the whole mod
  README.md          this file
```

`Dist\Patched\` is not here and must never be committed: the patched copy of the shipped bundle is
baked on the player's machine, out of the player's own game files (`Route7.ApplyProject`). Shipping
one would put Phoenix Point's own assets inside a Workshop item.

## The one interesting file

```json
{
  "id": "morgott.demo.materialtweak",
  "bundle": "MaterialTweak.bundle",

  "replace": [
    { "bundle": "aln_fireworm_assets_all.bundle", "asset": "ALN_Fireworm_DMG", "material": "_GlossMapScale=0.15" }
  ]
}
```

- `"material"` is `<property>=<number>` and sets **one float** in the material's `m_SavedProperties.m_Floats`
  (`BundleBaker.ReplaceMaterialFloat`). Colors live in `m_Colors` and are not wired up; there is no
  property-block traversal and no shader-property walk.
- If the property is not already in the list it is **appended**. That is a trap, not a feature: a
  name the shader does not declare is written to the file, passes the bake's own check, and is then
  invisible to the engine forever. Read the shader's property list before choosing a name —
  `ALN_Fireworm_DMG` uses `_PX_CHR/CHR_Character_shader`, whose only floats are `_SkinSaturation`,
  `_NormalScale`, `_GlossMapScale`, `_OcclusionScale`, `_MaxAdd`, `_tilingX`, `_tilingY` and
  `__dirty`. `_Glossiness` is **not** among them.
- `"asset"` must name the material **uniquely inside that bundle**. `aln_fireworm_assets_all.bundle`
  holds three Materials and two of them are both called `ALN_Fireworm`, so only the `_DMG` one can
  be addressed at all.
- `"bundle"` at the top is the project's own output bundle name. This mod adds nothing, so nothing
  is ever written to it — the field is still required by the manifest reader.

## Running it

Tick the mod on. ContentTool bakes the patched copy the first time and redirects the live
Addressables location at it; no restart, no console command, and switching the mod off drops the
redirect in the same session.

To see the number rather than the pixels, ask the engine for the material the game actually loaded:

```powershell
cd E:\DEV\PhoenixPoint\PPCLI
# the damaged-Fireworm prefab is the only one wearing ALN_Fireworm_DMG
.\ppcli.ps1 connect call '{"op":"invoke","type":"UnityEngine.AddressableAssets.Addressables","member":"LoadAssetAsync","typeArgs":["UnityEngine.GameObject"],"args":["02_Bodyparts/ALN_Fireworm_BodyAll_DMG_Ready.prefab"]}'
.\ppcli.ps1 connect call '{"op":"invoke","target":"<handle>","member":"WaitForCompletion","args":[]}'
#   -> GetComponentsInChildren<Renderer> -> [0].sharedMaterial  (name: ALN_Fireworm_DMG)
.\ppcli.ps1 connect call '{"op":"invoke","target":"<material>","member":"GetFloat","args":["_GlossMapScale"]}'
```

Measured in game 2026-08-28 (`D:\PP-Instance2`, ContentTool `build=9872a6b9`, menu, one run):

| Probe, on the live `ALN_Fireworm_DMG` | Value |
|---|---|
| `GetFloat("_GlossMapScale")` with this mod on | **0.15** |
| same property off the untouched shipped bundle (gate `P3-ctl-shipped`, same run) | **1** |
| `GetFloat("_GlossMapScale")` on the sibling material `ALN_Fireworm`, which this mod does not name, out of the SAME patched bundle | **1** |
| `GetFloat("_OcclusionScale")` / `_MaxAdd` / `_SkinSaturation` on the modded material | **1 / 1 / 0**, unchanged |
| `HasProperty("_Glossiness")` on that same material | **False** — the trap above, live |

