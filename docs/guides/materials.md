# Materials — one number out of a shipped material

The rung between "replace a whole texture" and "replace a whole mesh". **No art, no DLL, no
`Content\` folder at all** — two JSON files are the entire mod.

!!! warning "This route writes exactly ONE NUMERIC property. It does not replace a material."
    There is no material authoring here, no property-block traversal and no shader-property walk. One
    row sets **one float** in the shipped material's float list. Colours are a different list and are
    not wired up. **Changing the albedo is the [texture](textures.md) route, not this one.**

## 1. The folder

```text
MaterialTweak\
  meta.json          the mod manager entry (ID, name, description, dependency)
  ppcontent.json     the whole mod
  README.md          your attribution and notes
```

There is no `Content\`, no `Dist\`, no `.dll`. There is also no `Dist\Patched\`, and there must never
be one: the patched copy of the shipped bundle is baked on the player's machine out of the player's
own game files. Shipping one would put Phoenix Point's own assets inside your release, and the
packager refuses it by name.

## 2. The manifest, field by field

The proven working example, in full:

```json
{
  "id": "morgott.demo.materialtweak",
  "bundle": "MaterialTweak.bundle",

  "replace": [
    { "bundle": "aln_fireworm_assets_all.bundle", "asset": "ALN_Fireworm_DMG", "material": "_GlossMapScale=0.15" }
  ]
}
```

| Field | Value | Notes |
|---|---|---|
| `id` | `morgott.demo.materialtweak` | must equal `meta.json`'s `ID` |
| `bundle` | `MaterialTweak.bundle` | **your own** output bundle. This mod adds nothing so it is never written — the field is still required by the manifest reader. |
| `replace[].bundle` | `aln_fireworm_assets_all.bundle` | the shipped bundle holding the material |
| `replace[].asset` | `ALN_Fireworm_DMG` | the Material's name, **unique inside that bundle** |
| `replace[].material` | `_GlossMapScale=0.15` | `<property>=<number>`. One float, in the material's saved float list. |

Two things about `asset` that bite:

- **It must be unique inside that bundle.** `aln_fireworm_assets_all.bundle` holds three Materials
  and two of them are both called `ALN_Fireworm`, so only the `_DMG` one can be addressed at all.
- **Pick a different bundle from your other mods'.** One shipped bundle has exactly one owning mod;
  two mods aiming at the same bundle means one of them is refused by name.

## The trap that costs a session: a property in the FILE is not a property of the SHADER

!!! danger "A name the shader does not declare is written, passes the bake, and is invisible forever"
    If the property is not already in the material's float list it is **appended**. That is a trap,
    not a feature. The bake's own check passes, the file genuinely contains your value — and the
    engine never reads it.

**Read the shader's property list before choosing a name.** `ALN_Fireworm_DMG` uses
`_PX_CHR/CHR_Character_shader`, whose only floats are:

```text
_SkinSaturation  _NormalScale  _GlossMapScale  _OcclusionScale  _MaxAdd  _tilingX  _tilingY  __dirty
```

`_Glossiness` is **not** among them — and it *is* in the file's float block, which is exactly why it
looks like a valid target. Measured live on the modded material:

```text
HasProperty("_Glossiness")  ->  False
```

So the check that matters is not "is the name in the file" but "does the shader declare it". Ask the
engine, on the material the game actually loaded, before you write the row.

## 3. The commands, and what they print

There is nothing to bake for a material-only mod: it has no `Content\` for `ct_project` to read. Tick
the mod on and ContentTool bakes the patched copy the first time and redirects the live Addressables
location at it — no restart, no console command.

```text
ct_route7 status        what is redirected right now
```

To see the **number** rather than the pixels, ask the engine for the material the game actually
loaded: resolve the prefab that wears it, walk to the `Renderer`'s `sharedMaterial`, and call
`GetFloat`. Measured in game 2026-08-28, one run:

| Probe, on the live `ALN_Fireworm_DMG` | Value |
|---|---|
| `GetFloat("_GlossMapScale")` with this mod on | **0.15** |
| the same property off the untouched shipped bundle, same run | **1** |
| `GetFloat("_GlossMapScale")` on the sibling material `ALN_Fireworm`, which this mod does not name, out of the SAME patched bundle | **1** |
| `GetFloat("_OcclusionScale")` / `_MaxAdd` / `_SkinSaturation` on the modded material | **1 / 1 / 0**, unchanged |
| `HasProperty("_Glossiness")` on that same material | **False** — the trap above, live |

The third row is the one to copy into your own testing: a sibling material out of the same patched
bundle proves the edit hit *your* asset and not the bundle.

## 4. Bake and package

```powershell
# nothing to bake - this mod has no Content\ folder

# with the game shut
$PP = 'D:\Steam\steamapps\common\Phoenix Point'   # your own game folder
.\package.ps1 -Project "$PP\Mods\MaterialTweak"
```

The packager needs `meta.json`, `ppcontent.json` and something to ship — and **a declared `replace`
row is something to ship**, so a material-only mod with no `Content\` and no `Dist\` packages exactly
like any other:

```text
PACKAGED 3 file(s), 331 B into ...\dist-package\MaterialTweak
```

Those three are `meta.json`, `ppcontent.json` and `README.md`. Zip the folder, with `meta.json` at
the top of the archive. The *"ships nothing at all"* refusal you may have read about is aimed at a
folder that declares no rung **and** carries no file — a forgotten bake, not this.

## 5. How a player installs it

Unzip into `Phoenix Point\Mods\`, tick it on. ContentTool bakes the patched copy on **their** machine
the first time, in their own AppData, and redirects the live Addressables at it. Ticking the mod off
drops the redirect in the same session — no restart, and nothing was written into the installation.

## 6. Discovery and the dependency line

```json
"Dependencies": [ "com.morgott.ContentTool" ]
```

ContentTool applies your `replace` rows for every mod the manager says is ON. The dependency line is
what switches ContentTool on for your player; with ContentTool off, a mod with no assembly does not
even load. See [the reference](reference.md#3-the-dependency-line-what-it-actually-buys).

## 7. When it does not work

| Symptom or line | What it means |
|---|---|
| the value reads back **unchanged** and there is no error anywhere | the shader does not declare that property. It was appended to the file and the engine ignores it. Check `HasProperty` live. |
| `skipped, disabled in the mod manager` | the player has you switched off. |
| `mod '<a>' lost <bundle> to '<b>' (one owner per shipped bundle, lowest mod id keeps it)` | another mod already owns that shipped bundle. |
| `[ERROR] [Mods] Failed to enable mod '<id>', loader 'Default'` → `Loader.LoadMod() returned null!` | ContentTool is OFF and your mod ships no assembly. |
| nothing at all happens and no `ct_` line names your mod | `asset` matches no Material in that bundle, or matches more than one. |
| the packager refuses `<file> - a PATCHED COPY of a Phoenix Point bundle` | a `Patched\` folder is in your project. Delete it; it is built on the player's machine. |

## What this route cannot do

- **Colours.** They live in the material's colour list; only the float list is wired up.
- **Textures.** Swapping the albedo is the [texture route](textures.md).
- **A whole material.** There is no way to author a new material over a shipped one here; you set one
  declared float.
