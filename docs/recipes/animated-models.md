# Bake and publish a complete model

This imports a complete GLB into your mod's own bundle and publishes a new Addressables key for its
prefab. Use it when you are adding a model rather than replacing a `Mesh` inside a shipped bundle.

## What you need before you start

- One GLB 2.0 file directly under `Content\Models`. OBJ belongs to mesh replacement and is not read
  here.
- Geometry, UVs and any rig, skin and clips that the model needs.
- Base-colour images embedded in the GLB, or PNG/JPG/JPEG files in `Content\Textures`.
- A new globally distinct runtime key. A published key must not already exist in the shipped catalog.
- If the model will animate, meet the [animation contract](animation-contract.md).

## Folder tree

```text
MyModelMod\
  meta.json
  ppcontent.json               <- publish maps your new key to models/soldier
  Content\
    Models\
      soldier.glb              <- complete model; file stem becomes "soldier"
    Textures\
      soldier.png              <- optional fallback for every material slot
      soldier_uniform.png      <- optional override for material slot "uniform"
  Dist\
    MyModelMod.bundle          <- written by ct_project
```

Only direct children are scanned. An author-supplied `soldier_<material>.png` wins for that material;
`soldier.png` is the shared fallback; an embedded base-colour image is used last. If an embedded
image cannot be decoded, the bake tells you which external filename to supply.

## Steps

1. Export your complete model as `soldier.glb`. Keep material names stable if you use per-material
   images. Put it directly in `Content\Models`.

2. Put optional external images directly in `Content\Textures`. For a material named `uniform`, use
   `soldier_uniform.png`. Source matching ignores case, but duplicate stems ignoring case are refused.

3. Create `meta.json`:

   ```json
   {
     "ID": "example.mymodelmod",
     "AssemblyName": "",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "My model mod" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

4. Create `ppcontent.json`. `asset` is the relative baked path, not the source filename extension:

   ```json
   {
     "id": "example.mymodelmod",
     "bundle": "MyModelMod.bundle",
     "publish": [
       {
         "key": "8f924c3a6d7e4b22a5f149b3cd881001",
         "asset": "models/soldier",
         "type": "GameObject",
         "deps": "defaultlocalgroup_unitybuiltinshaders.bundle"
       }
     ]
   }
   ```

5. Bake the bundle, publish the key live, then verify it through the game's own Addressables:

   ```text
   ct_project MyModelMod
   ct_catalog apply MyModelMod
   ct_catalog verify
   ```

6. Put the runtime key `8f924c3a6d7e4b22a5f149b3cd881001` in the creature, weapon or behaviour
   code that consumes the model. Publishing makes a key resolvable; it does not decide when to spawn it.

7. Package only after the bake and catalog checks pass:

   ```text
   ct_package MyModelMod
   ```

## What success looks like

Material names and geometry counts come from your GLB:

```text
material '<slot>' -> <material asset> <binding report>
model 'soldier' kept <n> material(s) as <n> submesh(es)
model 'soldier' -> assets/example.mymodelmod/models/soldier <geometry/rig summary>
WROTE <project>\Dist\MyModelMod.bundle <bytes> B as example_mymodelmod
M1-wrote PASS <file-level hierarchy and skin check>
M1 PASS <loaded model and skin check>
ct_project: ALL PASS - <project>\Dist\MyModelMod.bundle
```

Publishing then ends with these actual summary shapes:

```text
1/1 key(s) published LIVE for 'example.mymodelmod' - nothing was written to the game installation
No restart needed, nothing was written to your game installation, and disabling 'example.mymodelmod' removes whatever published again.
ct_catalog: PASS - the game's own Addressables served the mod's own bundle, and nothing was written to the installation
```

## When it fails

| Exact output | Meaning | Fix |
|---|---|---|
| `SOURCE SKIPPED: <file> <reason> - SKIPPED, the project's other sources are unaffected` | The GLB or an external image could not be imported. | Re-export the named file or delete it, then bake again. |
| `texture REFUSED 'soldier' material 'uniform' carries a <bytes> B embedded image that could not be decoded (<reason>); it would render white. Supply Content\Textures\soldier_uniform.png instead.` | The GLB image is unreadable. | Export `soldier_uniform.png` and put it directly in `Content\Textures`. |
| `REFUSED: no mod bundle at <path> - bake it first with 'ct_project MyModelMod'. Installing a key does not bake, on purpose: a build command must not mutate the player's game installation.` | `ct_catalog apply` ran before a successful bake. | Run `ct_project MyModelMod` and fix its failures first. |
| `REFUSED: 'assets/example.mymodelmod/models/soldier' is not in MyModelMod.bundle (it holds <n> asset(s)); the key '8f924c3a6d7e4b22a5f149b3cd881001' would resolve to null forever. Nothing was published.` | `publish.asset` does not match the baked relative path. | Use `models/soldier`; bake again, then apply. |
| `REFUSED: '<mod>' publishes key '<key>', which the game's own catalog already has. A locator ContentTool appends is appended AFTER the shipped one, so the shipped asset would keep winning and this key would silently do nothing. Publishing ADDS new keys only. To REPLACE what an existing key already serves, declare it under "replace" instead - that route serves a patched private copy of the shipped bundle and needs no catalog key at all.` | The key is not new. | Generate a different key. Use Replace if the intent is to overwrite shipped content. |

Read [the status glossary](../troubleshooting/bake-errors.md). `ct_catalog` publishes; it never bakes.

## Worked demo

[WeaponAdd](../examples/weapon-add.md) publishes three mod-owned GameObject keys from one bundle.
