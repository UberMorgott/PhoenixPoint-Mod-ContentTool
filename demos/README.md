# The demos — conventions for adding one

Every folder here that carries a `meta.json` is a **separate mod**: `deploy.ps1` installs it to
`Mods\<Name>` beside ContentTool, so the player sees it in the mod manager and can switch it off.
A folder without `meta.json` is invisible — PPModLoader discovers only TOP-LEVEL directories under
`Mods\` that hold one (`decompiled\...\PhoenixPoint.Modding\PPModLoader.cs:29-46`).

Copy an existing `meta.json`. The four rules:

- **ID** `morgott.demo.<name>`, matching the project's `ppcontent.json` `"id"`.
- **Name** `ContentTool Demo: <X>` — one prefix, so the six sort together among hundreds of mods.
  The list uppercases it for you.
- **Dependencies** `[ "com.morgott.ContentTool" ]`. It is enforced: a missing dependency makes the
  mod un-enablable (`ModEntry.cs:53-63`), and enabling a demo auto-enables ContentTool
  (`ModManager.TryEnableMod:200-207`).
- **Description** — see below. This one bites.

## The description is TWO surfaces, and the row shows only the FIRST LINE

`ModItemController.Init` (`...Home.View.ViewControllers\ModItemController.cs:63`) does

```csharp
DescriptionLabel.text = mod.LocalizedDescription.Split('\n', RemoveEmptyEntries).FirstOrDefault()
```

so everything after the first `\n` **never appears in the list row**, and that row's `Text` label is
then clipped by its own width in the prefab — a pixel clip with no ellipsis, not a character limit.
Measured against the widest line that still fitted in game: **keep the first line ≤ 110 characters.**

Nothing is lost, because hovering a mod builds a tooltip from the **whole** description
(`...Home.View.ViewModules\UIModuleModManager.cs:212-216`), along with name, version, author, ID and
the resolved dependency list. So:

- **line 1** — self-contained, ≤110 chars, and **the caveat goes FIRST**. If the mod does nothing
  until you run a console command, or an `apply` it performs is not undone by switching the mod off,
  that is the half a player must read before filing a bug. `NOT AUTOMATIC: …`, `MAIN MENU ONLY: …`.
- **line 2+** — the full truth: what it does, how, what it costs, what it does not do.
- Keep both TRUE against what the code does today, not against what the README promises.
