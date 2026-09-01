# Blind round 5 — "add a new weapon", and the loop that did not close

*Not published: `blind-test/` is in `exclude_docs`, and this file names engine internals.*

## What the round was

A source-blind agent, given only the rendered site, tried to **add a new weapon**. It got further
than any round before it: it **found its own donor** — `AN_ShreddingShotgun_WeaponDef` — through the
newly documented `ct_list defs`, which is the discovery step that used to have to be handed to it.
It still could not finish. Eight defects came out of the attempt; seven were documentation, one was
the product.

## Defect 1 — the product defect. A modeless weapon mod could not be packaged

Three published statements, all true at once, formed a closed loop:

- `guides/weapon.md` §1 — `WeaponAdd.dll`, "the built output, **staged by package.ps1**"
- `guides/weapon.md` §5 — the packager refuses that shape, "ship it by zipping the folder yourself"
- `guides/reference.md` §6 — "A refusal **deletes the staged folder**"

`"model"` is optional on a `weapons` entry, so the smallest legal weapon mod is `meta.json` +
`ppcontent.json` + a `.dll`. It has no `Content\` and no `Dist\`, `bin\` is on the packager's
exclusion list, and the only documented builder deleted its staging and told it to bake something
that does not exist.

**Fixed in the engine, not in the prose.** `src\Project\Package.cs`:

| | |
|---|---|
| old rule | `Refusals()` set `anyContent` from the first path segment being `Content` or `Dist`; if neither appeared, refuse |
| new rule | `Package.Ships(stagedFiles, manifestText)` — a **payload** is either a staged file that is not paperwork (`meta.json`, `ppcontent.json`, `README.md`, `SOURCES.md`, `LICENSE`, `LICENSE.md`), which covers `Content\`, `Dist\`, `Icons\` and the mod's own assembly, or a `ppcontent.json` declaring `replace` / `publish` / `sounds` / `creature` / `weapons` |

The check moved out of `Refusals()` (which has no manifest) into `Run()` (which does). `package.ps1`
is unchanged — it was only ever a wrapper, and two implementations of one rule are forbidden by
`Package.cs`'s own header.

**Arm `S14-modeless`** (`tests\TargetPathTests\Program.cs`, `ModelessArm`), four cells on one folder,
one variable at a time — falsified in both directions:

| cell | staged assembly | manifest | expected |
|---|---|---|---|
| `dll+rung` | yes | declares `weapons` | packages — **the blocker case** |
| `rung-only` | no | declares `weapons` | packages — a declared rung is a payload |
| `dll-only` | yes | declares nothing | packages — an assembly is a payload |
| `nothing` | no | declares nothing | **refused**, staging deleted |

A regression to "a `Content\` or a `Dist\` folder" fails the first three; an implementation that
simply stopped refusing fails the fourth. Real run of the first cell through `tools\Package`:
`PACKAGED 3 file(s), 412 B` — `meta.json`, `ppcontent.json`, `ModelessGun.dll`.

Two other rungs were unblocked by the same change and their pages were wrong about themselves:
the **material tweak** (`guides/materials.md`, a `replace` row and no file — measured
`PACKAGED 3 file(s), 331 B`) and the **icon-only mod** (`guides/textures.md`, an `Icons\` PNG and a
DLL).

## Defect 2 — `ct_list defs _DamageKeywordDataDef` cannot work

`reference.md` §5 offered that as the way to see the damage-keyword family, on a page that also says
"every filter is a case-insensitive substring". The two filters are not the same kind of thing
(`Dev\Extract.cs`, `Defs` + `IsA`): the **name** filter is a substring of `d.name`, the **type**
filter walks `Type.BaseType` upward. `_DamageKeywordDataDef` is a class-shaped string in the name
slot, and the real def is `Burning_DamageKeywordEffectorDef`, so it matches nothing.

Replaced with an admonition separating the two slots, and `ct_list defs Def DamageKeywordDef` as the
family listing — every def name ends in `Def`, so `Def` is the "all of them" name filter. **No output
was invented**: the page says to run it and warns about the 60-hit cap. The two single-answer
searches already on the page are real transcripts and were left alone.

## Defect 3 — a reader tunes blind

Nothing on the site printed a donor's own numbers, so the blind agent guessed damage 55 / spread 4.0.
There is no read-out verb, and inventing one was not the answer: `WeaponBuild.Tuning` already prints
the donor's shipped value on the **left** of every arrow and is called unconditionally
(`WeaponBuild.cs:197`). So `weapon.md` now says to write the entry with **no `damage` and no
`spread`**, tick the mod on, and read both sides of the arrows off the donor — one enable, no
campaign. No fabricated transcript: it points at the captured line already on the page.

## Defect 4 — "your mod missing from `status` is the finding"

False for any mod that declares no `replace` and no `publish` row — `status` lists exactly bundle
redirections and published keys, so a sound mod's or a modeless weapon's absence is **correct**.
Qualified, with the lines those rungs are actually proved by.

## Defect 5 — `ppcontent.json` "for video / bundle content"

`SHIPPING-A-CONTENT-MOD.md`'s contract table said "for video / bundle content"; `Package.Run` refuses
its absence unconditionally, before it looks at anything else, and so does the bake. The code is
right and the table was wrong: now **yes, always**, with the refusal text and the two required fields.

## Defect 6 — "verify yours does not collide with a shipped key"

No tool lists the ~8232 shipped keys, so the instruction could not be followed. It does not need to
be: `BundleClaims.ShippedKeyRefusal` refuses a key the shipped catalog already has, **by name**, and
`KeyClaims.Claim` refuses one another mod already publishes. `weapon.md` now says to generate at
random and states that the engine does the checking, quoting both refusals.

## Defect 7 — `index.md`'s source list

Missing the two a weapon needs. Added `Content\Models\*.glb` (a whole new model, as against
`Content\Meshes\` which replaces a shipped one) and `Icons\*.png`, noting Icons is **top level**,
beside `Content\` and not inside it — which is what `Package.Shipped` and the recipes both do.

## Defect 8 — two undocumented behaviours

- **Un-ticking a weapon mod.** Determined from code, not guessed: nothing anywhere calls
  `RemoveDef`, and no demo weapon assembly declares `OnModDisabled`, so the defs stay for the
  session; the model keys, being route iii, do come down on the checkbox. So the half that comes back
  is the art, not the weapon. Added as a row in `SHIPPING-A-CONTENT-MOD.md`'s per-route table and as
  a table in `weapon.md` §6, with "restart for a clean undo".
- **Guid collision between two mods.** `WeaponBuild.One` opens with
  `repo.GetDef(e.Guid(1)) is WeaponDef already` → `"already built this session"`. The distinctness
  check in `Parse` is **per manifest**, so across two mods there is no refusal at all: the first mod
  enabled wins and the second silently gets the first's gun. This is the one collision class that is
  *not* refused by name, so it went in `index.md`'s limits beside the ones that are, and in
  `weapon.md` §8.

## Verification

- `TargetPathTests` — `R0: ALL PASS` (was 1 FAILURE on the first cut of `ModelessArm`: the
  `staged` assertion was `want && …`, which is false for the refusal cell — the arm was wrong, not
  the engine)
- `ObjCodecTests` — exit 0, all suites `ALL PASS`
- `mkdocs build --strict` — built, no warnings
- `seal-blind-workspace.ps1 -Round 9` — `sealed 59 file(s), no leaks`

## Left for a live run

Nothing blocks a modder, but one line on the site is an instruction rather than a transcript:

```text
ct_list defs Def DamageKeywordDef
```

Capture it from the main menu of a running game and paste the real listing (and its
`... n more (narrow the filter)` tail) into `reference.md` §5, replacing the "run this yourself"
sentence.
