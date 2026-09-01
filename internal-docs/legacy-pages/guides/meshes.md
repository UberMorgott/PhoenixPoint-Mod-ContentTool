# Meshes: replace one or publish a new model

There are two different operations:

- `replace[].mesh` changes geometry at a shipped Mesh identity. The existing prefab, renderer,
  materials and—unless you provide a compatible skin—skeleton remain in charge.
- `Content\Models` plus `publish[]` bakes a new model into your bundle and gives it an Addressables
  key. Publishing does not place it in a scene; behavior code or another content route must use it.

## Replace a shipped mesh

Find and extract the target:

```text
ct_list bundles mutoid
ct_list assets mutoid_assets_all.bundle Mesh Geo_Head02_V01
ct_extract mesh mutoid_assets_all.bundle Geo_Head02_V01
```

The extracted GLB retains geometry, UV0/1, normals, tangents, submeshes, weights and bind poses. Its
bone nodes are synthesized and do not preserve the real shipped hierarchy or names. For a rigged
replacement, run this separately:

```text
ct_list bones mutoid_assets_all.bundle Geo_Head02_V01
```

Put your replacement under `Content\Meshes`:

```text
MyMesh\
  meta.json
  ppcontent.json
  Content\
    Meshes\
      mutoid_head.glb
```

```json
{
  "id": "yourname.mymesh",
  "bundle": "MyMesh.bundle",
  "replace": [
    {
      "bundle": "mutoid_assets_all.bundle",
      "asset": "Geo_Head02_V01",
      "mesh": "mutoid_head"
    }
  ]
}
```

`mesh` is the lowercased file stem. OBJ and GLB are accepted. An OBJ carries no skin; ContentTool
assigns geometry to the nearest shipped bone where a skinned target requires one. A rigged GLB takes
the strict path described in [Animated models](animated-models.md).

Keep the replacement inside the shipped mesh's local bounds. The existing renderer still uses its
old bounds for culling, so geometry outside them can disappear when the camera moves. Preserve the
target's local origin, forward direction, scale, submesh/material expectations and UV layout unless
you are deliberately replacing the corresponding textures too.

Bake, apply and inspect animation and camera culling:

```text
ct_project MyMesh
ct_route7 apply MyMesh
```

Then rebuild the final output and package:

```text
ct_project MyMesh
ct_package MyMesh
```

## Publish a new model

Place a binary glTF model under `Content\Models`:

```text
FieldScanner\
  meta.json
  ppcontent.json
  Content\
    Models\
      field_scanner.glb
```

Choose a stable Addressables key and declare the asset path inside your bundle. Namespace the key
with your mod ID so it cannot collide accidentally, for example
`yourname.fieldscanner/models/field_scanner`. The key must be non-empty; a key already in the game's
catalog is refused by name, and two mods cannot publish the same key. A bare 32-character lowercase
hex string is also legal, but carries no human-readable namespace.

```json
{
  "id": "yourname.fieldscanner",
  "bundle": "FieldScanner.bundle",
  "publish": [
    {
      "key": "yourname.fieldscanner/models/field_scanner",
      "asset": "models/field_scanner",
      "type": "GameObject",
      "deps": "defaultlocalgroup_unitybuiltinshaders.bundle"
    }
  ]
}
```

The folder importer lowercases the source stem and writes it below `models/`. `type` is required
when the key is new. `deps` is a semicolon-separated list of shipped bundles that must be mounted so
external shader or asset references resolve. Do not list your own bundle there.

Bake and verify the key:

```text
ct_project FieldScanner
ct_catalog apply FieldScanner
ct_catalog verify
```

The model is now addressable, not visible. A weapon row can consume the key as its `model`; a
creature block consumes a model directly from `Content\Models`; other uses require a behavior DLL
that loads the key and places the object. Before adding references, read the shared
[profile-wide `Managed\` module warning](behavior-dll.md#managed-module-load-failure).

Package after the final bake:

```text
ct_package FieldScanner
```

## Limits

- Only one mod-owned `.bundle` may ship.
- Model publishing does not create a def, spawn rule, icon, inventory entry or trigger.
- Mesh extraction does not preserve materials, blendshapes or a real bone hierarchy.
- A new model's imported shader references must resolve through its declared dependencies.
- Scale and orientation are properties of the exported scene. The general model route has no
  automatic semantic understanding of “muzzle,” “front,” or “feet.” Weapon `fit` and creature
  `scale`/`up`/`lift` solve those route-specific cases.
