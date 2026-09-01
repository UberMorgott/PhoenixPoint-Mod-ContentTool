# Replace weapon art or add a weapon

Use **Replace** when an existing weapon should keep its definition and receive new mesh/textures.
Use **Add** when you need a new `WeaponDef` cloned from a shipped weapon, with its own name, stats and
optional model. These routes are not interchangeable.

`demos\WeaponMesh` is Replace: its `ppcontent.json` contains one mesh row and five texture rows, all
against `px_equipment_assets_all.bundle`. It is not an Add mod and it does not ship a pre-baked copy
of that game bundle. ContentTool bakes the private patched copy from the player's installation.

## What you need before you start

- For Replace: a GLB/OBJ mesh in `Content\Meshes`, any PNG/JPG/JPEG maps in `Content\Textures`, and
  the exact shipped `Mesh` and `Texture2D` names.
- For Add: a shipped `WeaponDef` to clone, a fixed unique GUID, and a DLL that calls
  `WeaponBuild.Build`. Match donor class to silhouette; the donor's tags choose hold/fire animations.
- For an added custom model: one GLB in `Content\Models`, a new Addressables key, and `"fit": "auto"`
  or declared `shoot`, `aim` and `shell` socket vectors.
- A new campaign to receive `count` weapons and `clips` ammunition in starting storage.

## Folder tree

```text
WeaponReplace\
  meta.json
  ppcontent.json               <- six Replace rows; no weapons[] block
  Content\
    Meshes\
      rifle.glb                <- replacement geometry
    Textures\                  <- texture files belong only here
      rifle_albedo.png
      rifle_normal_flat.png
      rifle_metallic_flat.png
      rifle_occlusion_white.png
      rifle_emissive_off.png

MyAddedWeapon\
  meta.json                    <- AssemblyName is MyAddedWeapon.dll
  ppcontent.json               <- publish[] plus weapons[]
  MyAddedWeapon.csproj
  src\
    MyAddedWeaponMain.cs       <- calls WeaponBuild.Build
  Content\
    Models\
      ar181.glb                <- optional new weapon prefab
  Dist\
    MyAddedWeapon.bundle
  bin\Release\MyAddedWeapon\
    MyAddedWeapon.dll
```

## Steps

1. Choose the route. If you only want the Ares AR-1 to look different, use Replace. If the old and
   new weapon must coexist as separate inventory items, use Add.

2. Discover exact targets. The Replace commands take bundle, type, then optional name filter. The
   Add command takes name filter, then type filter:

   ```text
   ct_list assets px_equipment_assets_all.bundle Mesh WPN_PX_RG_Assault_Rifle
   ct_list assets px_equipment_assets_all.bundle Texture2D WPN_PX_RG_Assault_Rifle
   ct_list defs SY_LaserAssaultRifle WeaponDef
   ```

3. For **Replace**, name the source files exactly as shown in the first tree and create this valid
   `ppcontent.json` (the checked-in `WeaponMesh` manifest):

   ```json
   {
     "id": "example.weaponreplace",
     "bundle": "WeaponReplace.bundle",
     "replace": [
       { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01", "mesh": "rifle" },
       { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_albedo", "texture": "rifle_albedo" },
       { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_normal", "texture": "rifle_normal_flat" },
       { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_metallic", "texture": "rifle_metallic_flat" },
       { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_occlusion", "texture": "rifle_occlusion_white" },
       { "bundle": "px_equipment_assets_all.bundle", "asset": "WPN_PX_RG_Assault_Rifle_T01_V01_emissive", "texture": "rifle_emissive_off" }
     ]
   }
   ```

   Use content-only `meta.json` unless separate code changes the inventory sprite:

   ```json
   {
     "ID": "example.weaponreplace",
     "AssemblyName": "",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "Weapon replacement" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

4. Bake Replace. Do not put the images under `Content\Meshes\materials`:

   ```text
   ct_project WeaponReplace
   ct_package WeaponReplace
   ```

5. For **Add**, put `ar181.glb` directly in `Content\Models` and create `meta.json`:

   ```json
   {
     "ID": "example.myaddedweapon",
     "AssemblyName": "MyAddedWeapon.dll",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "My added weapon" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

6. Create `ppcontent.json`. `id`, `clone` and `guid` are mandatory. The fixed GUID is the saved def
   identity; do not generate a new one on each launch. `model` must equal the new published key:

   ```json
   {
     "id": "example.myaddedweapon",
     "bundle": "MyAddedWeapon.bundle",
     "publish": [
       {
         "key": "8f924c3a6d7e4b22a5f149b3cd882001",
         "asset": "models/ar181",
         "type": "GameObject",
         "deps": "defaultlocalgroup_unitybuiltinshaders.bundle"
       }
     ],
     "weapons": [
       {
         "id": "Example_AR_WeaponDef",
         "name": "Example AR",
         "clone": "SY_LaserAssaultRifle_WeaponDef",
         "guid": "8f924c3a-6d7e-4b22-a5f1-49b3cd882011",
         "model": "8f924c3a6d7e4b22a5f149b3cd882001",
         "fit": "auto",
         "damage": "45",
         "spread": "2.4",
         "count": "10",
         "clips": "10"
       }
     ]
   }
   ```

7. Create the .NET 4.7.2 project exactly as shown in [Build a behaviour DLL](behavior-dll.md), using
   assembly name `MyAddedWeapon`. Put this in `src\MyAddedWeaponMain.cs`:

   ```csharp
   using Morgott.ContentTool.Tactical;
   using PhoenixPoint.Modding;

   namespace Example.MyAddedWeapon
   {
       public sealed class MyAddedWeaponMain : ModMain
       {
           public override bool CanSafelyDisable => true;

           public override void OnModEnabled()
           {
               WeaponBuild.Build(Instance.Entry.Directory, line => Logger.LogInfo(line));
           }
       }
   }
   ```

8. Build, bake, publish the model key for the current session, and verify it:

   ```text
   dotnet build MyAddedWeapon.csproj -c Release -p:PPRoot="D:\Steam\steamapps\common\Phoenix Point"
   ct_project MyAddedWeapon
   ct_catalog apply MyAddedWeapon
   ct_catalog verify
   ct_package MyAddedWeapon
   ```

9. Enable both ContentTool and the mod, restart, and start a new campaign. For visual fitting, load a
   geoscape campaign and run `ct_bench open` (or press Ctrl+Alt+B). Select the new weapon, adjust it,
   and save. The console equivalent is:

   ```text
   ct_fit Example_AR_WeaponDef show
   ct_fit Example_AR_WeaponDef save
   ```

   A successful save writes `scale`, `rotate` and `offset` back into this mod's own `ppcontent.json`.

## What success looks like

Replace prints all six patch lines, read-back gates, and this final result:

```text
patch px_equipment_assets_all.bundle: mesh 'WPN_PX_RG_Assault_Rifle_T01_V01' <- rifle <geometry summary> - skinned <static result>
patch px_equipment_assets_all.bundle: 'WPN_PX_RG_Assault_Rifle_T01_V01_albedo' <- rifle_albedo <width>x<height>
copies ready in <path> - nothing to install: ticking 'WeaponReplace' on in the mod manager redirects them (dev-only shortcut: ct_route7 apply WeaponReplace)
ct_project: ALL PASS - <project>\Dist\WeaponReplace.bundle
```

Because the five source textures also become mod-owned assets, this project writes its own bundle as
well as private patched copies. That own bundle is not a pre-baked replacement for the shipped one.

Add prints:

```text
model 'ar181' -> assets/example.myaddedweapon/models/ar181 <model summary>
ct_project: ALL PASS - <project>\Dist\MyAddedWeapon.bundle
1/1 key(s) published LIVE for 'example.myaddedweapon' - nothing was written to the game installation
ct_catalog: PASS - the game's own Addressables served the mod's own bundle, and nothing was written to the installation
ct_weapon PASS 'Example AR' (Example_AR_WeaponDef) cloned from SY_LaserAssaultRifle_WeaponDef; icon (none declared); prefab load started for key 8f924c3a6d7e4b22a5f149b3cd882001; <tuning and VFX report>
ct_weapon PASS StartingStorage of <n> difficulty def(s) now carries [10x Example_AR_WeaponDef + <ammo>]
```

The fit save ends with `ct_fit saved 'Example_AR_WeaponDef' into <ppcontent path>` and prints the
saved manifest block.

## When it fails

| Exact output | Meaning | Fix |
|---|---|---|
| `P4 REFUSED 'rifle' is not a .obj or .glb under Content\Meshes\` | Replace cannot find its mesh source. | Move `rifle.glb` directly into `Content\Meshes`, or correct `mesh`. |
| `P1 REFUSED '<name>' is not a .png/.jpg under Content\Textures\` | Replace cannot find one texture source. | Move the PNG/JPG/JPEG directly into `Content\Textures`; delete the old `Content\Meshes\materials` copy. |
| `P4 REFUSED target '<asset>' is not a Mesh in px_equipment_assets_all.bundle - <reason> - list the names it does hold with: ct_list assets px_equipment_assets_all.bundle Mesh` | The shipped mesh target is wrong or ambiguous. | Run the printed command and copy the exact name. |
| `ct_weapon FAIL 'SY_LaserAssaultRifle_WeaponDef' is not in the def repository - nothing to clone from` | `clone` is not an exact shipped `WeaponDef` name. | Run `ct_list defs SY_LaserAssaultRifle WeaponDef` and copy the name exactly. |
| `ct_weapon FAIL key '8f924c3a6d7e4b22a5f149b3cd882001' did not load (<status>) - 'Example_AR_WeaponDef' exists but has no model. Keys are published live when the mod is enabled, so there is nothing to apply and nothing to restart: either the key is not declared in this mod's "publish" block, or its bundle was never baked ('ct_project <mod>'). 'ct_catalog status' lists what IS published.` | The weapon def exists, but its model key did not resolve. | Match `weapons[].model` to `publish[].key`, bake, and inspect `ct_catalog status`. |
| `ct_fit save REFUSED for 'Example_AR_WeaponDef': <reason> -> <manifest>` | The workbench could not update the manifest row. | Fix the named manifest/path problem. Do not copy values by eye while this refusal remains. |
| `ct_bench REFUSED: the workbench stands a unit in the SQUAD BAY, and the squad bay is part of a loaded geoscape campaign. Load or start a campaign first.` | The visual bench has no geoscape squad bay. | Load or start a campaign, then open it again. |

Read [the status glossary](../troubleshooting/bake-errors.md). A P1/P4 refusal belongs to Replace;
a `ct_weapon FAIL` belongs to runtime construction; a refusal from `ct_package` is a third, separate
gate.

For the Replace route, read [when a shipped-bundle redirect takes effect and why only one mod can own
a bundle](../getting-started/lifecycle.md#redirects-affect-future-loads). The
[weapon workbench](../troubleshooting/bake-errors.md#inspect-a-weapon-replacement-without-a-mission)
is the quickest visual check.

## Worked demos

- [WeaponMesh](../examples/weapon-mesh.md) is the Replace route: one shipped mesh, five shipped
  textures and a DLL-written inventory icon.
- [WeaponAdd](../examples/weapon-add.md) is the Add route: three published model keys and three cloned
  weapon defs. Its current three-model revision still needs an in-game verification run.
