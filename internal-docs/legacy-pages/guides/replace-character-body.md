# Give a shipped character a different body

Use this track when a character already exists in Phoenix Point and you want to change what she LOOKS
like without changing who she is. She keeps her def, her name, her class, her story role, her events
and her rewards; she gets your model. **Same person, new body.**

It is the REPLACE half of a pair. The ADD half is
[Put a foreign humanoid into Phoenix Point as a playable soldier](humanoid-soldier.md) — the same
model, the same offline toolchain, the same five clip roles, put into the game as a NEW person. The
two differ by exactly one manifest key, and reading that page first is the cheapest way to
understand this one: everything about converting a foreign rig lives there and is not repeated here.

| | Humanoid soldier (ADD) | This page (REPLACE) |
|---|---|---|
| Result | a NEW playable soldier | a SHIPPED character with a new body |
| Manifest key | `"donor"` + `"startingRoster"` | `"replaceBody"` |
| What is minted | a new `TacCharacterDef` | nothing — the shipped def is rewritten |
| Identity | random, generated at campaign start | the character's own, untouched |
| Needs a new campaign | yes | no |
| Demo | [`demos\HumanoidSoldier`](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/HumanoidSoldier) | [`demos\ReplaceCharacterBody`](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/ReplaceCharacterBody) |

## The seam — what actually binds a character to a body

A character is a `TacCharacterDef`, and NOTHING about her appearance is stored on that def directly.
The path from the def to the geometry on screen is four hops, and every one of them is a def field
you can rewrite:

1. **`TacCharacterDef.Data.ComponentSetTemplate`** — a flat list of component defs, looked up BY TYPE
   (`ComponentSetDef.cs:19-29`). The engine's own accessor spells the first hop out:

   ```csharp
   // TacCharacterDef.cs:172-174
   public AddonsManagerDef GetAddonsMangerDef()
   {
       return Data.ComponentSetTemplate.GetComponentDef<AddonsComponentDef>()?.AddonsManagerDef;
   }
   ```

2. **`AddonsComponentDef.AddonsManagerDef`** — the manager def, which owns the rig.
3. **`AddonsManagerDef.Rig`** — a plain `GameObject` reference. `AddonsManager.SetupRig` does nothing
   cleverer than instantiate it (`AddonsManager.cs:112-120`), then collects every `EXT_*` child as a
   hot transform (`:135-143`). **This is the field. Everything else on this page exists to make
   writing it safe.**
4. **The chassis and the items hung on that rig.** `SetupRig` also attaches
   `AddonsManagerDef.SkeletonChassisAddonDef` (`AddonsManager.cs:145-148`), and the character builder
   adds her own template items on top:

   ```csharp
   // AddonsCharacterBuilder.cs:162-166
   Addons.AddRange(TacCharacterDef.Data.BodypartItems);
   if (TacCharacterDef.Data.EquipmentItems.Length != 0)
       Addons.Add(TacCharacterDef.Data.EquipmentItems[0]);
   ```

   Each of those addons carries its own `SkinData`, which `Addon.AttachVisuals` instantiates
   (`Addon.cs:1024`, returning early at `:1029-1032` when the prefab resolves null), attached BY NAME
   to a transform found across the whole rig (`Addon.GetAttachTransform:1194` →
   `AddonsManager.FindTransform(name, rigBonesOnly: false)`).

The cheapest seam is hop 3, and hop 4 is the reason a body swap is not free: **on a human, PP's body
IS its addons.** The chassis and the torso/legs bodypart items are the visible person; the rig is the
skeleton they hang on. Write only `Rig` and you get your model wearing a Phoenix Point soldier.

## Why a def rebind and not a bundle repoint

ContentTool's other half is zero-runtime replacement — a patched bundle copy and a catalog repoint —
and it is the obvious tool to reach for. It is the wrong one here, for a reason that is about the
GAME's data and not about the tool:

- **There is no single "her body" asset to repoint.** The four hops above assemble the character at
  runtime out of a chassis prefab, per-item skin prefabs and customisation pieces.
- **Every one of those assets is shared by the entire human roster.** Repointing the human torso
  prefab does not re-body one character; it re-bodies all of them, plus every faction NPC that uses
  the same chassis.

A def rebind is per-character by construction, because each character can be given her OWN clone of
the manager. That is the whole argument, and it is why `"replaceBody"` is a manifest key on the
creature block rather than a new entry in the `replace[]` array.

**Nothing shipped is mutated on the way, and only two members at the end.** Everything is built on a
scratch def nobody is looking at — the component set, the addons component, the addons manager, the
chassis tree, the anim actions, the actor base are all clones, and the shipped defs a character
shares with the rest of the roster are only READ. When every step has succeeded, two assignments hand
her the finished state: `Data` (whose one meaningful edit is `ComponentSetTemplate`, re-pointed at the
set that was built) and `Volume`. They are two because those are the only def members the build
writes.

That ordering is not tidiness. `Build()` catches and reports that nothing was wired
(`src\Tactical\CreatureBuild.cs`, `Build`), and several steps after the mint can throw — the melee
bodypart, the ranged wiring, the audit. Building on the shipped def would have made that message a
lie and left a half-swapped character for the session. Until the publish line, the game still has the
character it loaded.

Her def name, GUID, `Name`, `LocalizeName`, class tags, base stats and every event that names her
come through `TacCharacterData.Clone()` — the game's own copy — with the values she shipped with.

## The manifest key

One key, in the `creature` block, holding the name of a shipped `TacCharacterDef`:

```json
"creature": {
  "clips": { "HL_IdleAlert_NoGun": "idle", "MV_RunFwd_Loop_NoGunA": "walk",
             "FF_Punch_ShotLoop": "attack", "HL_Death_AR": "death",
             "HL_HurtFront_AR": "reaction" },
  "events": { "attack": "ActionDo 0.3, ShootShot 0.5, ActionEnd 0.9",
              "death": "Ragdoll 0.9" },
  "model": "body",
  "replaceBody": "S_SY_Eileen_CharacterTemplateDef"
}
```

- **Absent or empty = nothing shipped is touched.** That is the standing behaviour of every creature
  mod written before this key existed, and it is the default the engine ships with.
- **It REPLACES `"donor"`.** The named character is both the structural template and the def written
  back to, so the two are never written together. Writing `"donor"` as well changes nothing — the
  replace target wins — and the demo leaves it out entirely.
- **`"startingRoster"` is not for this route.** The character is already wherever the story puts her.
- **Leave `"name"`, `"health"`, `"will"` and `"speed"` out** if you want the identity kept. Each of
  them overwrites part of it, and each still applies when you set it: there is one build path and not
  two, so `"replaceBody"` does not override the other keys. That is deliberate — a swap that also
  renames and re-stats is a legitimate thing to ask for, and dropping a key the author typed would be
  a silent failure. The identity claim on this page is about what `"replaceBody"` does BY ITSELF.
- **The model is prepared exactly as for the ADD half.** Same offline tools, same reason: PP's
  clips bind to the CRC-32 of a bone's transform path, so a raw download loads and never animates.
  See [the humanoid soldier toolchain](humanoid-soldier.md#end-to-end-conversion). **Ship it with the
  full clip set** — the character you re-body is one the player fights with, and `ppslim.py` is not
  part of this route. `ppzip.py` IS: run it last, it compresses how the curves are stored rather than
  dropping any (104,511,576 B -> 36,254,816 B, -65.3%, all 300 clips kept).

## Choosing a target

Do **not** aim at the starting squad. Those units are generated: `CreateInitialSquad` walks
`GameDifficultyLevelDef.StartingSquadTemplate` and then calls `RandomizeIdentity` on every one of
them (`GeoPhoenixFaction.cs:1968-1974`), so "the starting sniper" has no fixed person behind it. The
characters worth re-bodying are the NAMED ones the story ships, which carry their own def and their
own `Data.Name`.

Read live out of a running game's `DefRepository` (`PPCLI\catalog\defs.ndjson`), those are:

`S_SY_Eileen_CharacterTemplateDef`, `S_Helena_TacCharacterDef`, `S_Felipe_TacCharacterDef`,
`S_DrKalindar_TacCharacterDef`, `S_RaviChaudri_TacCharacterDef`, `S_MrSpark_TacCharacterDef`,
`S_IN_AbdonTusk_TacCharacterDef`.

The demo uses Eileen, the Synedrion character from the Festering Skies DLC. **A DLC character is
absent unless the player owns that DLC**, and the mod then refuses BY NAME and changes nothing:

```text
ct_creature FAIL ppcontent.json "creature": "replaceBody" is 'S_SY_Eileen_CharacterTemplateDef', which is not the name of a shipped TacCharacterDef with a component set. It names the ONE character whose body this model replaces - write a def name such as "S_SY_Eileen_CharacterTemplateDef". A DLC character is absent unless that DLC is installed. Nothing was changed.
```

## Bake and run

```text
ct_project ReplaceCharacterBody
```

Restart. She wears it — in the roster, on the geoscape and in tactical. No new campaign is needed:
nothing on this route touches the campaign start, and the component set is read when her actor is
built, so an existing save shows the swap the next time she is drawn.

## She arrives with no armour and no weapon

This is the price of the swap and it is the one to know about. `Data.BodypartItems` and
`Data.EquipmentItems` are emptied, because of `AddonsCharacterBuilder.cs:162-166` above: kept, those
items hang a Phoenix Point soldier's torso and legs over your model — attached by name to points a
retargeted rig also provides, so they land perfectly and look exactly wrong. Cleared, your model is
the whole visible character.

She keeps her stats that live on the def (`Data.Strength`, `Will`, `Speed`), her class and her
progression; what she loses is the gear the template handed her, and she is re-equipped from stores.

**The upgrade path, if a real mod needs it:** de-skinned CLONES of her items would keep the aspects
and drop the geometry. It is not free, and the melee bodypart in the ADD path is the warning —
nulling `SkinData` leaves an addon owning no transforms at all, which is how a bash button ended up
permanently grey (`src\Tactical\CreatureBuild.cs`, the `BashPoint` note). Marked in the source as a
`ponytail:` shortcut rather than silently taken.

## What else does not follow the body

- **Animations are the rig's, not the character's, and the file must carry ALL of them.** She plays
  the clips in YOUR file, driven through the `AnimatorOverrideController` the engine builds
  (`TacticalActor.cs:724-726`). A state whose clip is missing is a stall, not a fallback: the
  measured symptom is an aimed pistol shot that never returns and a camera frozen on the actor,
  because the ability waits on an event no clip will fire (`AnimEventReceiver.cs:100,126`). The
  manifest's five roles are only the ones the bake can check — a soldier reaches far more, so ship
  the full clip set and do not run `ppslim.py` on her. Do run `ppzip.py`: it makes the same 300 clips
  65.3% smaller without removing one. If your rig differs from PP's, the animations
  are wrong in exactly the way the
  [conversion pipeline](humanoid-soldier.md#why-renaming-the-bones-is-not-enough) exists to prevent;
  there is no runtime retargeter to save you.
- **Weapon sockets are the source rig's own**, decided offline by the bone rename, not by anything
  the game stores per weapon. The in-game fit workbench cannot correct them for PP's shipped weapons
  — it has nowhere to save a fit for a weapon with no manifest row (`src\Dev\FitBench.cs:53-55`). The
  full account is in
  [the sibling guide](humanoid-soldier.md#load-the-soldier-and-how-its-weapons-sit-in-its-hands).
- **Her portrait and UI headshot do not change.** They are rendered from customisation data on her
  identity, which this route deliberately never touches — that is the same decision that keeps her
  name and class.
- **Armour and gear the PLAYER equips later still attach.** They are addons resolved by attachment
  point name against the whole rig (`Addon.GetAttachTransform:1194`), so a retargeted rig satisfies
  them and a foreign one does not. This is a property of your model, not of the swap.
- **Two mods cannot re-body the same character.** The second is refused by name and changes nothing
  — one shipped def would otherwise carry two runtime entries, and the engine's `ByTemplate()` lookup
  answers with the first, so the second mod's actors would silently run on the first mod's rig state.
- **Another mod that rewrites the same def wins or loses by load order.** TFTV clones this very def
  to build a character of its own (`refs\TFTV-src\TFTV\TFTVIncidents\GeoscapeEvents.cs:767`); a clone
  taken BEFORE the swap keeps the shipped body, a clone taken after inherits the new one. Neither is
  a defect and neither is under this mod's control.
- **It is not verified in game.** The seam, the writes and the refusals are read off the decompile
  and checked offline (`tests\ObjCodecTests\StartRoster.cs`); the demo has no row in
  `docs\VERIFIED-DEMOS.md` because no measured in-game run exists for it yet.

For def cloning, events and DLL-based injection, return to the [creature recipe](creature.md). For
everything about converting the model itself, use the [humanoid soldier guide](humanoid-soldier.md).
