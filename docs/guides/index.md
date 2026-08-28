# Recipes — pick your rung

Every kind of content you can replace or add, one recipe each. Each recipe carries the exact folder
tree, the manifest field by field with a real working example, the console commands with the output
they really print, how to bake and package, how a player installs it, how ContentTool finds it, and
that rung's real failure messages.

If you prefer to begin from a complete project, use the [working demos](../demos.md); every demo
links back to the recipe that explains its technique.

Read these two pages once before following a recipe:

- [Shipping a content mod](../SHIPPING-A-CONTENT-MOD.md) — the contract: what a mod folder may
  contain, what applies by itself, what the checkbox does and does not undo.
- [The shared reference](reference.md) — the folder, `meta.json`, `ppcontent.json`, the two
  authoring commands, `package.ps1`, install, discovery, distribution.

## The ladder

| Rung | Needs a DLL? | Recipe |
|---|---|---|
| **texture** — swap a shipped image | no | [Textures, sprites and icons](textures.md) |
| **sprite / icon** — an inventory cell, a UI picture | **yes** | [Textures, sprites and icons](textures.md#the-icon-rung) |
| **material** — change one property of a shipped material | no | [Materials](materials.md) |
| **static mesh** — replace a shipped prop's geometry | no | [Meshes](meshes.md) |
| **a whole new model** — publish it under your own key | no to bake, yes to *use* | [Meshes](meshes.md#adding-a-whole-new-model) |
| **animated model, with an adapter** — a new unit, its own rig and clips | yes | [Animated models](animated-models.md#with-an-adapter) · [New creature](creature.md) |
| **animated model, no adapter** — your geometry on a shipped skeleton | no | [Animated models](animated-models.md#without-an-adapter) |
| **sound replace** — a shipped sound becomes yours | no | [Sounds](sounds.md#replacing-a-shipped-sound) |
| **sound add** — a sound the game never had | **yes** | [Sounds](sounds.md#adding-a-sound-the-game-never-had) |
| **video replace** — a shipped cutscene becomes yours | no | [Videos](videos.md#replacing-a-shipped-video) |
| **video add** — a clip the game never played | **yes** | [Videos](videos.md#adding-a-video-the-game-never-played) |
| **new creature** — a downloaded model in your squad | yes | [A new creature](creature.md) |
| **new weapon** — a gun the game does not ship | yes | [A new weapon](weapon.md) |

## The line that runs through all of it

**Content is free. Deciding *when* something happens costs a DLL.**

Replacing a picture, a mesh, a material, a sound or a clip the game already plays is a folder of
files and a few lines of JSON — no code at all. Adding something the game has no reason to ever
reach — a hotkey, a new cutscene trigger, a new weapon def, a field on a def — means somebody has to
say when, and that somebody is an assembly of yours.

Every recipe below says which side of that line it is on, in its first paragraph.

## Four things worth knowing before you start

- **The developer console opens with the `` ` `` key** — backquote/tilde, left of `1` — and needs
  nothing enabled first on an install that runs mods.
  [How, and the fallback if that key does nothing](reference.md#opening-the-developer-console).
- **You do not have to know an asset's name to replace it.** `ct_list` prints the shipped bundles and
  what is inside each one, which is how a wish becomes the two names a manifest row needs.
  [Finding a target](reference.md#finding-a-target-ct_list).
- **A download needs no bake.** `ct_project` and `ct_sound bake` are AUTHORING commands, for when
  *you* change a source file. You ship the baked output in `Dist\`; your player just ticks the mod
  on. [Measured](reference.md#5-the-two-authoring-commands-and-why-a-player-never-runs-them).
- **The mod-manager checkbox is the player's install step.** The old `revert` workflow has been
  removed. `ct_route7 apply <YourMod>` remains only as an author shortcut for refreshing a
  shipped-bundle replacement during a test session; route-specific off-switch behaviour is in the
  [shipping contract](../SHIPPING-A-CONTENT-MOD.md#unticking-it-mid-session-what-actually-happens-per-route).
