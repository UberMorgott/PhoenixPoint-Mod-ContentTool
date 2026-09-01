# Replace vs Add

Two routes, one choice: are you changing something that already ships, or bringing something new?

## Replace

- Swaps a mesh, texture, material or clip inside one of the **game's own** bundles.
- The game's skeleton stays authoritative. Your imported vertex weights are mapped onto the
  target's bones **by name**. A bone name that does not match falls back to a nearest-bone weld
  (one full-weight influence per vertex), which creases badly. Matching names exactly is what makes
  a replacement good.
- An arbitrary skeleton **cannot** be substituted this way. The renderer's bones, bind poses,
  bone-name hashes, Avatar and every shipped animation clip must stay index-parallel.
- Rule of thumb: if you want to change how something existing **looks**, use Replace.

## Add

- Builds a **new** model from your file, using the file's own joints, hierarchy, rest transforms
  and bind poses.
- This is the route for a genuinely custom rig, a new creature, a new unit.
- If you want your own skeleton, you want Add, not Replace.
- Rule of thumb: if you are bringing something **new** into the game, use Add.

## Why Replace bakes on the player's machine

Add produces a self-contained bundle built only from the mod's own content. It can be baked by the
modder and shipped ready — the player gets it working immediately. The shipped demos
(`Sample.bundle`, `WeaponMesh.bundle`) are exactly this.

Replace has to patch one of the game's bundles. For a Phoenix Heavy body part that is
`px_heavy_assets_all.bundle`, about 124 MB, holding every Heavy mesh rather than just the three
being replaced. A modder cannot ship that pre-baked:

- The file would contain the game's own assets (redistributing them is not acceptable).
- It is enormous relative to the change.
- It would go stale the moment the game patches.

So a Replace mod ships only the delta — your `.glb` plus the manifest — and ContentTool bakes the
patched copy **from the player's own installation**, once, then caches it. First launch pays that
cost (roughly two minutes for a bundle that size); later launches are instant. Nothing is ever
written into the game installation itself.

### A bake that repeats every launch

If a bake reports **any** failure, the folder is deliberately not marked current, so the bake
repeats on every launch until the failure is fixed. This is intentional — it prevents a player from
silently testing a stale bundle — but it means a single broken manifest line (a texture in the
wrong folder, say) costs the two minutes every single start.

If your mod re-bakes every launch, look at the failure in the
[player log](../SHIPPING-A-CONTENT-MOD.md#open-the-developer-console); that is the cause.
