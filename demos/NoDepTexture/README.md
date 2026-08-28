# NoDepTexture — the fixture that leaves the dependency out

Not a demo of a feature. A **measurement fixture** for one question the other eight demos can never
ask, because every one of them declares `"Dependencies": [ "com.morgott.ContentTool" ]`:

> what does a content mod that declares NO dependency actually do?

```
demos\NoDepTexture\
  meta.json                the mod manager entry, with the Dependencies field OMITTED ENTIRELY
  meta.deps-empty.json     the same entry with "Dependencies": [] written out explicitly
  ppcontent.json           one texture replacement
  Content\Textures\
    acidworm.png           256x256 magenta/green checker - two flat colours, not anyone's art,
                           so there is no licence to chase and no SOURCES.md to write
  README.md                this file
```

`meta.json` and `meta.deps-empty.json` are two different **serializer inputs**, and they are not
obviously the same thing: `ModMeta.Dependencies` is declared `public string[] Dependencies = new
string[0];` and read with `JsonConvert.DeserializeObject<ModMeta>`, so an absent field keeps the
field initialiser and an explicit `[]` produces an empty array too. Both were measured rather than
argued about — swap the second file over `meta.json` **in the install** to run that arm.

## What it replaces

`acidworm_low_albedo` in `aln_acidworm_assets_all.bundle` — the Acidworm's body albedo, shipped at
**1024x1024 DXT1 with 11 mips**. The checker bakes in as **256x256 RGBA32, 1 mip**, so the two are
told apart by reading the texture's own `width`/`format`/`mipmapCount` off the engine rather than by
looking at it. It is deliberately a *different* bundle from `MaterialTweak`'s: one shipped bundle has
exactly one owner (`BundleClaims.Keeps`, lower mod id wins) and two demos aiming at the same bundle
would have one of them refused by name.

## The result

The four-cell measurement — `{Dependencies omitted, Dependencies: []}` x `{ContentTool ON, OFF}` —
is recorded in `docs\SHIPPING-A-CONTENT-MOD.md`, together with the prediction that was written down
before the runs.
