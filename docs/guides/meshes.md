# Static meshes — replacing one, and adding a whole new model

Two rungs, one page, because they are the same geometry pipeline pointed at two different ends.

| You want | Route | Code? |
|---|---|---|
| a shipped prop to have **your geometry** | one `"mesh"` row in `ppcontent.json` | **no** |
| a **new model** the game does not ship, reachable by name | drop it in `Content\Models\`, publish a key | no to bake; **yes** to make the game *use* it |

If the mesh you are replacing is **rigged** — a character, a creature — read
[Animated models](animated-models.md) first. The binding rules there are different and they are the
thing that goes wrong.

---

## Replacing a shipped mesh

### 1. The folder

```text
WeaponMesh\
  meta.json
  ppcontent.json
  Content\
    Meshes\
      rifle.glb                 the imported geometry, already fitted
    Textures\
      rifle_albedo.png          because a mesh swap is never just a mesh - see below
      rifle_normal_flat.png
      rifle_metallic_flat.png
      rifle_occlusion_white.png
      rifle_emissive_off.png
  Dist\
    WeaponMesh.bundle           written by `ct_project` - COMMIT AND SHIP IT
  SOURCES.md                    your model's licence and attribution
```

`Content\Meshes\` takes **`.glb` and `.obj`**.

### 2. The manifest, field by field

```json
{
  "id": "morgott.demo.weaponmesh",
  "bundle": "WeaponMesh.bundle",

  "replace": [
    { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01",           "mesh":    "rifle" },
    { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_albedo",    "texture": "rifle_albedo" },
    { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_normal",    "texture": "rifle_normal_flat" },
    { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_metallic",  "texture": "rifle_metallic_flat" },
    { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_occlusion", "texture": "rifle_occlusion_white" },
    { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_emissive",  "texture": "rifle_emissive_off" }
  ]
}
```

| Field | Value | Notes |
|---|---|---|
| `id` / `bundle` | `morgott.demo.weaponmesh` / `WeaponMesh.bundle` | required in every project |
| `replace[].bundle` | `px_equipment_assets_all.bundle` | the shipped bundle holding the target |
| `replace[].asset` | `WPN_PX_RG_Assault_Rifle_T01_V01` | the Mesh's name |
| `replace[].mesh` | `rifle` | the stem of `Content\Meshes\rifle.glb` |

The Mesh and the Material here are **both** named `WPN_PX_RG_Assault_Rifle_T01_V01`. That is fine:
the row's kind key (`"mesh"` vs `"texture"`) says which class you mean, and the lookup is by name
**and** class.

### Geometry only — and why that is enough

Phoenix Point does not attach a weapon by writing a transform every frame. A piece of equipment is an
**Addon**, reparented under a named attachment transform on the rig; the hand IK, the firing
animations and the holster pose all address the prefab's own `Transform`, never the mesh.

So a mesh replacement changes **geometry and nothing else**. The socket, the pose, the animations and
the drop-on-death prop keep working untouched — *provided the new geometry arrives in the shipped
mesh's own local coordinates.* That is the whole job.

### Scale and orientation — the part that is actually hard

An imported `.glb` has no idea what a metre is in Phoenix Point, and its axes will not be yours.
**Derive every number; do not tune one.**

- **The target box is measured, not assumed.** The shipped mesh's local AABB is the only statement the
  game makes about where this object sits in the hand, so it is the whole specification. For the Ares:
  centre `(0, 0.03420, 0.15292)`, extent `(0.03385, 0.12561, 0.30142)` — 0.603 m of rifle down **+Z**,
  0.251 m up **+Y**, 0.068 m thick on X, grip near the origin.
- **Orientation is a basis mapping, not three Euler angles.** The source rifle lay along **-X** with
  the muzzle at `x = -0.752`, +Y up. Keeping "up" fixed gives exactly one mapping —
  `glTF -X → Unity +Z`, `glTF +Y → Unity +Y`, `glTF +Z → Unity +X` — whose determinant is `+1`, so it
  is a rotation and the model is not mirrored. **Assert that sign.** A mirrored rifle puts the
  ejection port on the wrong side and nobody notices for a week.
- **Scale is one number with a rule behind it: the smallest per-axis ratio.** The new geometry then
  fits INSIDE the silhouette the game already reserved, on every axis, so it cannot clip through
  hands or cover.
- **Translation matches AABB centres.** No taste involved.
- **A negative uniform scale is a point reflection.** It passes a bounding-box check happily and ships
  an inside-out model. `assert scale > 0` costs one line.

A fitting step that does this prints its own working — this is what the demo's produced:

```text
source  bbox min ['-0.7519', '-0.0926', '-0.0435'] max ['0.3081', '0.2812', '0.0435']
per-axis ratios  x=0.7782 y=0.6720 z=0.5687  ->  uniform scale 0.568735 (smallest wins)
translate        ['-0.000000', '-0.019437', '0.026721']
unity bbox       min ['-0.0247', '-0.0721', '-0.1485'] max ['0.0247', '0.1405', '0.4543']
shipped bbox     min ['-0.0338', '-0.0914', '-0.1485'] max ['0.0338', '0.1598', '0.4543']
OK  ...\Content\Meshes\rifle.glb  5554 verts / 6194 tris / 215980 bytes
```

`ct_extract` will hand you a shipped model as `.glb` for exactly this loop: extract, open in Blender,
edit, drop back into `Content\Meshes\`.

### Exporting from Blender

**glTF Binary (`.glb`)**, **Normals ON**, and the **Compression (Draco)** box **UNTICKED**. Draco is
the one compression still refused, and it is refused by name with the fix in the message.
`EXT_meshopt_compression` and `KHR_mesh_quantization` — what `gltfpack` and "optimised for the web"
produce — are decoded in-house, so a file carrying those needs no conversion step at all.

### 3. The commands, and what they print

```text
ct_extract mesh <shipped bundle> <asset name>    read the shipped geometry, to fit against it
ct_project WeaponMesh                            bake Content\ -> Dist\WeaponMesh.bundle
ct_route7 status                                 what is redirected right now
```

```text
ct_project: ALL PASS - <your project folder>\Dist\<YourMod>.bundle
ct_project: <n> FAILURE(S)
```

At mod-enable, in `Player.log` — the count is **shipped bundles**, so six rows against one bundle
still read `1/1`:

```text
ct_content: 'morgott.demo.weaponmesh' is ON in the mod manager, so its live registrations were installed at startup.
1/1 bundle(s) redirected LIVE for 'morgott.demo.weaponmesh' - nothing was written to the game installation
```

The `WeaponMesh: 0 clip(s) served in memory …` line beside it counts **videos** and says nothing
about a mesh mod —
[which success line is yours](reference.md#which-success-line-is-yours-there-are-three-and-they-count-different-things).

**Measured, live, in one run.** The prefab's `MeshFilter.sharedMesh.vertexCount` off the untouched
shipped bundle: **5771 verts / 8572 tris**. With the mod on: **5554 verts, `subMeshCount=1`**, mesh
name still `WPN_PX_RG_Assault_Rifle_T01_V01`. The five textures went **2048×2048 fmt=10 (DXT1)
mips=12 → 1024×1024 `RGBA32` mips=1** in the same run.

A note on the target bundle's size: `px_equipment_assets_all.bundle` is **403 MB**, and the patched
copy is written by decompressing the whole archive. Expect that first bake to take a while and to
want the memory. It happens once, on the player's machine, and lands in their AppData.

### 4. Bake and package

```powershell
ct_project WeaponMesh                    # in game, after editing the .glb or a .png
$PP = 'D:\Steam\steamapps\common\Phoenix Point'   # your own game folder
.\package.ps1 -Project "$PP\Mods\WeaponMesh"     # with the game shut
```

### 5. How a player installs it

Unzip into `Phoenix Point\Mods\`, tick it on. **Both halves apply immediately, and switching the mod
off hands the shipped ones straight back — in memory, in the same session, no restart.**

### 6. Discovery and the dependency line

`"Dependencies": [ "com.morgott.ContentTool" ]`, and everything in
[the reference](reference.md#3-the-dependency-line-what-it-actually-buys) applies unchanged.

### 7. When it does not work

| Line or symptom | What it means |
|---|---|
| the bake refuses the file, naming **Draco** | re-export from Blender with the Compression box unticked. |
| the bake names your `.glb` and says nothing says which mesh is the model | your file holds several meshes and **no armature drives any of them**. Measured on two real CC-BY downloads: one with **14 meshes / 3 materials**, another with **9 meshes / 1 material** — both refused. Join the pieces before exporting. |
| the model is inside out | a negative uniform scale. Assert `scale > 0` in your fitting step. |
| the model floats, clips through the hand, or is the wrong size | your fit was not derived from the shipped AABB. |
| the surface looks smeared or wrong | you replaced the mesh and not the textures. The shipped maps were painted for the shipped UVs. |
| `mod '<a>' lost <bundle> to '<b>' (one owner per shipped bundle, lowest mod id keeps it)` | two mods aimed at the same shipped bundle. |
| the packager says `<file> - a SHIPPED PHOENIX POINT BUNDLE IDENTITY` | a `.bundle` that is not your own is in your project. A package carries exactly one bundle, yours. |

---

## Adding a whole new model

### 1. The folder

```text
MyProps\
  meta.json
  ppcontent.json
  Content\
    Models\
      sniper.glb                the model. THE FILE IS THE DECLARATION.
    Textures\
      sniper.png                OPTIONAL: same stem -> the model's _MainTex slot
  Dist\
    MyProps.bundle              written by `ct_project` - COMMIT AND SHIP IT
```

**Nothing goes in `ppcontent.json` for the file itself** — dropping it in `Content\Models\` is the
declaration, exactly as `Content\Textures\*.png` is. `ct_project` bakes it into your bundle as a
prefab addressed `assets/<your id>/models/<stem>`.

- A **static** `.glb` (no armature) becomes root + `MeshFilter`/`MeshRenderer`.
- A **rigged** `.glb` becomes root, one bone per joint, a `SkinnedMeshRenderer`, and a Mesh carrying
  the file's own bind poses and per-vertex weights. The armature's **shape** comes across too — a
  bone is parented where your file parents it, so the rig can be animated rather than only posed.
- A `.glb`'s **own animation clips** come across with it. `"loop"` and `"play"` at the top level of
  `ppcontent.json` name the file's own clips, because glTF carries no loop flag and an un-looped run
  cycle plays once and freezes.

### 2. Making the game able to reach it — the `publish` row

A baked prefab exists in your bundle. To let the game load it **by key**, claim a catalog key:

```json
{
  "id": "morgott.demo.weaponadd",
  "bundle": "WeaponAdd.bundle",

  "publish": [
    { "key": "c7a9f1d24b6e4a3c8f5b7d1e9a2c4b60", "asset": "models/sniper",    "type": "GameObject", "deps": "defaultlocalgroup_unitybuiltinshaders.bundle" },
    { "key": "c7a9f1d24b6e4a3c8f5b7d1e9a2c4b61", "asset": "models/ar181",     "type": "GameObject", "deps": "defaultlocalgroup_unitybuiltinshaders.bundle" },
    { "key": "c7a9f1d24b6e4a3c8f5b7d1e9a2c4b62", "asset": "models/taupistol", "type": "GameObject", "deps": "defaultlocalgroup_unitybuiltinshaders.bundle" }
  ]
}
```

| Field | Value | Notes |
|---|---|---|
| `key` | `c7a9f1d24b6e4a3c8f5b7d1e9a2c4b60` | the address you will load by. **32 lowercase hex digits on purpose** — that is the exact shape Phoenix Point's own asset references carry, and the engine validates the runtime key before anything else. Verify yours does not collide with a shipped one. |
| `asset` | `models/sniper` | the path **inside your own bundle**, exactly as `Content\` spells it. A model becomes `models/<stem>`. |
| `type` | `GameObject` | required for a NEW key. One of the catalog's own resource types: `Texture2D`, `GameObject`, `Mesh`, `Material`, `AnimationClip`… |
| `deps` | `defaultlocalgroup_unitybuiltinshaders.bundle` | **not decoration.** Every model ContentTool bakes gets a Material whose shader is the builtin `Standard` through an *external* reference, which only resolves while that archive is mounted. Without the dep the model renders with `Hidden/InternalErrorShader`. Several deps are `;`-separated. Your own bundle is always first in the set. |

**`asset` is checked against the built bundle before anything is written** — a key pointing at a name
your bundle does not contain would load as `null` forever, silently.

!!! warning "New keys only"
    A mod can **add** a new asset key. It cannot override a key the game already ships: new keys are
    appended to the live catalog, and an appended entry can never outrank the shipped one. **To
    replace shipped content, use the bundle-replacement route above.**

    Route `publish` also cannot reach anything that is not a catalog key — a Texture2D, Material or
    Mesh *inside* a shipped prefab, a loose video, Wwise media, non-addressable scene props. Those
    are all `"replace"` rows.

### 3. The commands, and what they print

```text
ct_project WeaponAdd            # bake the mod's own bundle first - publishing never bakes
ct_catalog apply WeaponAdd      # publish the keys LIVE - no restart, nothing written to the install
ct_catalog verify
ct_catalog status               # what is published right now
```

Un-publishing is disabling the mod; there is no catalog edit to revert.

**Measured, `ct_catalog verify`, one run** — the game's own Addressables resolving each key:

| Key | Resolves to |
|---|---|
| `…4b60` | GameObject **`sniper`** |
| `…4b61` | GameObject **`ar181`** |
| `…4b62` | GameObject **`taupistol`** |

…each out of `WeaponAdd.bundle`. The control in the same run: the shipped catalog still carries
**8232 keys**, and `02_Bodyparts/ALN_Fireworm_BodyAll_Ready.prefab`, published by nobody, still
resolves to `ALN_Fireworm_BodyAll_Ready`. The external shader reference resolved to **`Standard`** —
a dangling one reads `Hidden/InternalErrorShader`, which is the discriminator for a missing `deps`.

### 4. Bake and package · 5. Install · 6. Discovery

Identical to the replace half above: `ct_project`, then `package.ps1` with the game shut; the player
unzips and ticks it on; `"Dependencies": [ "com.morgott.ContentTool" ]` is what turns ContentTool on
for them. Keys are published on the checkbox and un-published on the checkbox, in the same session.

### 7. When it does not work

| Line or symptom | What it means |
|---|---|
| the model loads but is untextured magenta / `Hidden/InternalErrorShader` | the `deps` entry is missing. Add `defaultlocalgroup_unitybuiltinshaders.bundle`. |
| `ct_catalog apply` refuses the key by name | `asset` names something your bundle does not contain. That refusal is the point — a key pointing at nothing would resolve to `null` forever with no error. |
| the key is refused as already claimed | **two mods published the same key.** This matters more than it sounds: a duplicate key does not degrade, it makes the game's Addressables initialisation throw and the game unlaunchable for **every** installed mod. The refusal happens twice over, and the whole rebuild is validated in memory before a byte lands. |
| loading a shipped key still gives the shipped asset | expected. Appended keys never outrank shipped ones. Use the replace route. |
| `skipped, disabled in the mod manager` | the player has you switched off. |

### Known ceilings on the model pipeline

- **Two skin influences per vertex** on the import path.
- The Material's shading is ungated — one texture in `_MainTex`, no normal map and no ORM.
- Cost is one float per curve per frame for every imported clip, whether the bone moves or not.
