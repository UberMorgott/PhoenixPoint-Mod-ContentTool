# Demo mod — the assault rifle every soldier starts with is now somebody else's model

**A content mod is a FOLDER of assets - no code - and ContentTool plays it.**

> **This is a SEPARATE MOD.** It installs as `Mods\WeaponMesh\` and the mod manager lists it as
> **ContentTool Demo: Weapon Mesh**. It requires the **ContentTool** mod - `meta.json` declares
> `"Dependencies": [ "com.morgott.ContentTool" ]`, so enabling this one enables ContentTool too.
> Switching it off restores **both halves** immediately: route vii now applies and undoes itself on
> the checkbox, in memory, and nothing is written into the game installation.

One capability, one mod. This one replaces the **Ares AR-1** - Phoenix Point's starting assault
rifle - with a CC0 rifle imported from a `.gltf`: the 3D model in the soldier's hands, its five
textures, **and the picture in the inventory cell**.

Two halves, and they are not the same kind of thing:

| half | how | undone by |
|---|---|---|
| model + 5 textures | six `"replace"` rows, ContentTool route vii, **zero runtime code** | the mod switch |
| inventory icon | one def field written at mod-enable (`WeaponMesh.dll`, ~60 lines) | the mod switch |

```text
WeaponMesh\
  ppcontent.json                              six "replace" rows - one mesh, five textures
  meta.json                                   the mod manifest; AssemblyName = WeaponMesh.dll
  Content\Meshes\rifle.glb                    the imported geometry, already fitted (see below)
  Content\Textures\rifle_albedo.png           1024 grimdark recolour of the kit's own atlas
  Content\Textures\rifle_normal_flat.png      4x4 - the three neutral maps, and why they exist
  Content\Textures\rifle_metallic_flat.png    4x4
  Content\Textures\rifle_occlusion_white.png  4x4
  Content\Textures\rifle_emissive_off.png     4x4
  Icons\rifle_inv.png                         450x450 - the inventory cell, rendered FROM rifle.glb
  src\WeaponMeshMain.cs                       the icon half: which def field, and why it needs code
  tools\fit_rifle.py                          THE thing to read - orientation and scale, derived
  tools\render_icon.py                        the icon, rasterised offline from the shipped .glb
  tools\make_neutral_maps.py                  the four solid colours, and the reason for each
  tools\check_project.py                      reads ppcontent.json the way the TOOL reads it
  tools\grimdark.ps1                          how the albedo was recoloured, pixel-for-pixel
  tools\source\Gun_Rifle.gltf + .bin          the CC0 file as downloaded, so the fit re-runs
  SOURCES.md                                  CC0 attribution, kept with the files
```

## Install

Neither half needs an install step: enable the mod and both are there. The dev-only console entry
points, for driving the model half by hand:

```text
ct_route7 apply WeaponMesh          # patched private copy of px_equipment_assets_all.bundle,
                                    # baked into the mod's own AppData folder and redirected LIVE
ct_route7 status                    # what is redirected right now
```

Look for `W1-icon PASS` in `Player.log` to know the icon half bound.

The mechanism is gate **P4** in `docs\PROVEN-FOUNDATIONS.md` — a declared mesh change reaching the
game with zero runtime code, eye-confirmed on `px_assault_assets_all.bundle` (110 MB). This demo
aims the same machinery at `px_equipment_assets_all.bundle`, which is **403 MB**: the patched copy is
written by decompressing the whole archive, so expect the `apply` step to take a while and to want
the memory.

Route vii: ContentTool writes a **patched copy** of the shipped bundle under its own folder and
repoints the game's Addressables catalog at it. The player's `px_equipment_assets_all.bundle` gets a
pristine `.ct-backup`, and `revert` proves the restore. Nothing of Snapshot Games' is redistributed
— the mod ships a mesh and five images and names the shipped objects it replaces.

## Which weapon, and how we know

`WPN_PX_RG_Assault_Rifle_T01_V01` in `px_equipment_assets_all.bundle`. That art name is the model
behind `PX_AssaultRifle_WeaponDef` — the Ares AR-1 — and the pairing is not a guess: the bundle also
holds `WPN_PX_RG_Assault_Rifle_T01_GOLD_V02`, and the game's def list holds
`PX_AssaultRifle_Gold_WeaponDef` next to `PX_AssaultRifle_WeaponDef`. One art variant per def, same
order, same two names.

It is the right target for a demo for three reasons:

- **It is the weapon a NEW CAMPAIGN starts with, and that is checkable.** The starting squad is
  `GameDifficultyLevelDef.StartingSquadTemplate` (`GameDifficultyLevelDef.cs:37`), and on Standard
  that array is `[PX_SniperStarting, PX_AssaultStarting, PX_AssaultStarting, PX_AssaultStarting,
  PX_HeavyStarting]` - and `PX_AssaultStarting_TacCharacterDef.Data.EquipmentItems` begins with
  `PX_AssaultRifle_WeaponDef`
  (`extracted\GameData\defs\TacCharacterDef\PX_AssaultStarting_TacCharacterDef.json`). **Three of
  the five soldiers a fresh campaign hands you carry this exact gun**, and 20 clips of its ammo are
  in `StartingStorage`. Nothing has to be researched, manufactured or found: apply, start a new
  game, look at the roster.
- **It is a plain static mesh.** The prefab `WPN_PX_RG_Assault_Rifle_T01_V01_Ready` is a
  `Transform` + `MeshFilter` + `MeshRenderer`, and the mesh is **one submesh, 5771 vertices, 8572
  triangles, no skin and no moving parts**. There is no armature to match and no second material to
  keep in step — the whole weapon is one Mesh object.
- **Its material owns its textures.** `WPN_PX_RG_Assault_Rifle_T01_V01` points at five Texture2Ds
  that carry its own name (`_albedo`, `_normal`, `_metallic`, `_occlusion`, `_emissive`), so
  replacing them touches this weapon and nothing else. Aim a mesh swap at a gun that rides a shared
  atlas and you repaint half the armoury.

The Mesh and the Material are both named `WPN_PX_RG_Assault_Rifle_T01_V01`. That is fine: a
`"replace"` row says which KIND it is (`"mesh"` vs `"texture"`), and the lookup is by name **and**
class.

## How the game mounts a weapon — and why that means "geometry only"

Phoenix Point does not attach a weapon by writing a transform every frame. A piece of equipment is
an **Addon**: `Equipment.TryAttachTo(TacticalActor.AddonsManager.RootAddon)`
(`Equipment.cs:144`), and the slot it lands in carries an **attachment-point name** —
`AddonDef.ProvidedSlotBind.AttachmentPointName` (`AddonDef.cs:20`), handed to the slot at
`AddonSlotImpl.SetupWithAddon` (`Addon.cs:49-53`) and resolved against the rig by name
(`RigRoot.FindTransformInChildren`, `AddonsManager.cs:120`). The weapon prefab is reparented under
that Transform; the hand IK, the firing animations and the holster pose all address the prefab's own
`Transform`, never the mesh.

So a mesh replacement changes **geometry and nothing else**. The socket, the pose, the animations and
the drop-on-death prop keep working untouched — *provided the new geometry arrives in the shipped
mesh's own local coordinates.* That is the entire job, and it is the next section.

## Scale and orientation — the part that is actually hard

An imported `.gltf` has no idea what a metre is in Phoenix Point, and its axes will not be yours.
`tools\fit_rifle.py` is the whole answer, and it derives every number it uses instead of tuning one:

```text
$ python tools\fit_rifle.py
source  bbox min ['-0.7519', '-0.0926', '-0.0435'] max ['0.3081', '0.2812', '0.0435']
per-axis ratios  x=0.7782 y=0.6720 z=0.5687  ->  uniform scale 0.568735 (smallest wins)
translate        ['-0.000000', '-0.019437', '0.026721']
unity bbox       min ['-0.0247', '-0.0721', '-0.1485'] max ['0.0247', '0.1405', '0.4543']
shipped bbox     min ['-0.0338', '-0.0914', '-0.1485'] max ['0.0338', '0.1598', '0.4543']
OK  ...\Content\Meshes\rifle.glb  5554 verts / 6194 tris / 215980 bytes
```

**The target box is measured, not assumed.** The shipped mesh's `m_LocalAABB` is
centre `(0, 0.03420, 0.15292)`, extent `(0.03385, 0.12561, 0.30142)` — 0.603 m of rifle down **+Z**,
0.251 m up **+Y**, 0.068 m thick on X, grip near the origin. That is the only statement the game
makes about where this weapon sits in the hand, so it is the whole specification.

**Orientation is a basis mapping, not three Euler angles.** The Quaternius rifle lies along **-X**
with the muzzle at `x = -0.752`, +Y up. Keeping "up" fixed, that gives exactly one mapping —
`glTF -X -> Unity +Z`, `glTF +Y -> Unity +Y`, `glTF +Z -> Unity +X` — whose determinant is `+1`, so
it is a rotation and the gun is not mirrored. A mirrored rifle puts the ejection port on the wrong
side and nobody notices for a week; the script asserts the sign rather than trusting it.

**Scale is one number with a rule behind it: the smallest per-axis ratio.** The new geometry then
fits INSIDE the silhouette the game already reserved for this weapon on every axis, so it cannot
clip through the soldier's hands or through cover. Here that is the length ratio, `0.568735`, and
the fitted rifle is exactly as long as the Ares and slightly slimmer.

**Translation matches AABB centres.** No taste involved, and it is the only placement the measured
data supports.

Two conversions the script has to get right and that are easy to miss:

- ContentTool's reader converts glTF to Unity with `S = diag(-1, 1, 1)` (`GlbCodec.Convert`) and
  reverses triangle winding because `det(S) = -1`. `S` is an involution, so the file must carry `S`
  applied to the Unity coordinates you want. The script does the round trip and asserts the result.
- A **negative** uniform scale is a point reflection. It survives the bounding-box check happily and
  ships an inside-out model. `assert scale > 0` costs one line.

`ct_extract` will hand you a shipped model as `.glb` for exactly this loop: extract, open in
Blender, edit, drop back into `Content\Meshes\`.

## The textures — a mesh swap is never just a mesh

The shipped material keeps pointing at the shipped 2048x2048 maps, and those were painted for the
**Ares' UV layout**. The imported mesh has its own. Replace only the mesh and another gun's panel
lines, wear, ambient occlusion and glowing bits smear across the new surface at the wrong places —
which reads as a bug, not as a mod.

| shipped Texture2D | gets | why |
|---|---|---|
| `..._albedo` | `rifle_albedo.png`, 1024 | the kit's own atlas, recoloured |
| `..._normal` | flat 4x4 `(128,128,255,128)` | see below |
| `..._metallic` | 4x4 `(179,179,179,102)` | metallic 0.70, smoothness 0.40 — cold iron |
| `..._occlusion` | white 4x4 | AO belongs to the mesh it was baked from |
| `..._emissive` | black 4x4 | the Ares glows; this rifle does not |

**Why the imported Normal and ORM maps are NOT shipped.** They have slots in principle and are dead
files in practice:

- The kit's normal map is a plain RGB tangent-space map. This shader is compiled with
  `_NORMALMAP` and reads a normal the DXT5nm way (X from alpha, Y from green), and — more decisive —
  a mesh that came through ContentTool carries **position, normal and uv0 and no tangents**, so
  there is no tangent frame for a normal map to lean on. The flat value `(128,128,255,128)` is
  flat under *both* unpack conventions, so the perturbation is zero whichever one the shader
  compiled to.
- The kit's ORM packs Occlusion/Roughness/Metallic into R/G/B. Unity's `_MetallicGlossMap` wants
  metallic in R and **smoothness in A**, and occlusion lives in a separate map. Porting it is a
  channel repack — a real and reasonable thing to do, and a different demo. Two uniform colours say
  "cold iron, dull sheen" with no repacking and no guesswork about a custom shader's channel
  semantics.

**Why 1024 and not 2048.** A replaced Texture2D is written **uncompressed RGBA32 with one mip**
(`BundleBaker.FillTexture2D`), so 2048 would be 16 MB inside the patched bundle against 4 MB at
1024, for a gun that is a few hundred pixels tall on screen. The kit's atlas is also shared across a
batch of guns and this rifle's UVs span `(0.002, 0.018)`–`(0.984, 0.998)` of it, so cropping is not
available — only downscaling is, and `tools\grimdark.ps1` downscales as its first step. Its `-In`
default points at the original 2048 `T_Guns_Batch1_BaseColor.png`, which this repo does not ship;
pass your own copy from the kit if you want to re-derive the recolour.

## The inventory cell — a pre-rendered Sprite, not a live render

This is the part a mesh swap silently misses, and the reason this demo ships a small DLL.

**The cell does not render the model.** It draws a Sprite the item's def points at:

```text
UIInventorySlot.cs:445          ImageNode.sprite = _item.ItemDef.ViewElementDef.InventoryIcon
UIInventoryItemDragIcon.cs:68   SetIcon(itemDef.ViewElementDef.InventoryIcon)
ItemDef.cs:228-236              GetSmallIcon() / GetLargeIcon() -> ViewElementDef?.InventoryIcon
ViewElementDef.cs:27/33         public Sprite SmallIcon;  public Sprite InventoryIcon;
```

The Ares' `E_View [PX_AssaultRifle_WeaponDef]` names the SAME sprite guid
`819abb6b55732ee4ba6d1c3cb907bcca` for both `InventoryIcon` and `SmallIcon`, so both are written.

**Why route vii cannot do it.** That sprite is `UI_PX_WeaponIcon_AssaultRifle_INV`, and it lives in
`PhoenixPointWin64_Data\sharedassets0.assets` — a Unity serialized file, **not** an Addressables
bundle. It has no `m_InternalIds` row, so route vii (which repoints one bundle's internalId) has
nothing to aim at. The def FIELD is the seam the game itself reads, so that is the seam this takes:
one write in `OnModEnabled`, the same thing TFTV does hundreds of times.

That has a pleasant consequence — **the icon half really is undone by switching the mod off**, and
nothing is written into the install for it.

**The image is rendered from the shipped model, offline.** `tools\render_icon.py` is an
orthographic z-buffered rasteriser over `Content\Meshes\rifle.glb`'s own positions and normals —
stdlib only, no Blender, no Unity. So the cell and the hand cannot disagree: they are the same
geometry. 450x450 because that is what the shipped icons measure (UnityPy on
`sharedassets0.assets`: `UI_PX_WeaponIcon_AssaultRifle_INV` is Rect 450x450 at 0,0; the `_LR`
variant is 800x450). The script asserts its own silhouette covers between 4% and 75% of the frame,
so an empty or solid-black render fails instead of shipping.

```text
$ python tools\render_icon.py
OK  ...\Icons\rifle_inv.png  450x450  13915 bytes  6194 tris  silhouette 15.1% of the frame
```

## Out of scope, and deliberately so

- **The RULES are untouched.** Damage, accuracy, weight, the weapon slot, the name and the
  description all live in def data and none of them changes here — this is still the Ares AR-1 in
  every way the rules can see. It looks like something else, in the hand and in the cell.
- **This replaces; it does not add a weapon.** Adding a NEW gun means a new `WeaponDef` served out
  of the mod's own bundle through route iii — that is the other half of the story, and it is the
  `WeaponAdd` demo next door.
- **The GOLD variant keeps its own LOOK, not its own shape.** Measured: the gold prefab SHARES the
  base mesh, so it reads the replaced 5554 vertex count too; only its material and textures stay
  the shipped ones. A soldier carrying the golden Ares carries the new shape in golden dress.
