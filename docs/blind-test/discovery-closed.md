# Source-blind findings — DISCOVERY, closed (2026-08-28)

> **Not published.** `mkdocs.yml` excludes `blind-test/` from the build.

Two source-blind rounds — one building a sound mod, one building a weapon — hit the **same wall from
two directions**, and neither was a missing fact. The site could hand a reader a target and could not
teach them to **find** one.

> *(sound round)* "**You cannot choose a sound.** The wem's name is its media ID, so the listing is
> bare integers with no names. The only named sounds on the whole site are the 5 IDs the demos
> already own, and the page says *do not overlap* — so the only targets I was given are ones I was
> told not to take."
>
> *(weapon round)* "**No way to choose or discover a donor.** The whole donor catalogue a blind reader
> has is 4 def names. I abandoned a shotgun design rather than invent `PX_Shotgun_WeaponDef` — **the
> docs dictated my weapon's class.**"

Rig for everything below: `D:\PP-Instance2`, driven through `PPCLI\ppcli.ps1 connect`. **Seven
launches.**

## 1. Finding a def — `ct_list defs <nameFilter> [typeFilter]`

A `defs` mode on the existing verb, not a new one. It walks the live def repository through the same
lookup every builder already does (`GetAllDefs` + compare `name`), so a name it prints is a name a
manifest accepts by construction. The name filter is required — 23013 defs.

The weapon round's guess was **wrong in exactly the way the tool now prevents**:

```text
> ct_list defs Shotgun WeaponDef
4 def(s) match name 'Shotgun' and type 'WeaponDef' out of 23013 in the repository
  AN_Shotgun_WeaponDef   [WeaponDef]
  AN_ShreddingShotgun_WeaponDef   [WeaponDef]
  FS_SlamstrikeShotgun_WeaponDef   [WeaponDef]
  PX_ShotgunRifle_WeaponDef   [WeaponDef]
```

`PX_ShotgunRifle_WeaponDef`, not the `PX_Shotgun_WeaponDef` every other Phoenix weapon's naming
implies.

**The type filter had to walk the base chain, and finding that out was the real work.** First
implementation compared the concrete class name, and the fire search answered:

```text
> ct_list defs Fire DamageTypeBaseEffectDef
0 def(s) match name 'Fire' and type 'DamageTypeBaseEffectDef' out of 23013 in the repository
```

Which reads as *"this game has no fire damage"*. The manifest key is typed by the **base**
(`DamageTypeBaseEffectDef`) and the only fire damage type in the game is a
`StandardDamageTypeEffectDef`. After the fix, on the same install:

```text
> ct_list defs Fire DamageTypeBaseEffectDef
1 def(s) match ... Fire_StandardDamageTypeEffectDef   [StandardDamageTypeEffectDef]
> ct_list defs Burning DamageKeywordDef
1 def(s) match ... Burning_DamageKeywordEffectorDef   [DamageKeywordDef]
```

## 2. Finding a sound — `ct_voices`, and what it was missing

`ct_voices` already counted what the game posts. It reported **bare event IDs**, and every
replacement is keyed on a **media ID** — so the instrument that answers *"which sound did that make"*
handed back a number no other command took. Verified live and closed by resolving each event through
the shipped `<bank>.txt` listings this code already reads.

```text
ct_voices what those events PLAY (event -> media, the id 'ct_sound bake' takes):
  event 799408924 x1  'MainMenuMusicStart' in MainMenuMusic, TacticalMusic -> media 208540756 'MainMenuMusic' - replaceable
  event 3086540886 x1  'StopAll' in UI -> no STREAMED media named 'StopAll', so its sound is embedded in a bank and cannot be replaced by a media bank
```

208540756 is exactly the media the `MenuMusic` demo replaces — reached from nothing but "arm it and
let the menu load".

Three things measured while getting there, all now in the code and on the page:

- **Cross-bank.** `StatXPBangupStop` is an event in `UIGeoscape.txt` and its media is listed in
  `UI.txt`. Searching only the event's own bank finds nothing.
- **Prefix, and more than one.** `MissionWinShow` fans out to `317726851 / 445739832 / 539878758`.
  Replacing one changes the sound about a third of the time.
- **Deduped, and every declaring bank named.** `MainMenuMusicStart` is in two `.txt` files; the first
  version reported whichever the scan reached last, which named the wrong bank for the menu music.

**Coverage, counted over the whole shipped set:** 843 events, **389 resolve** to a streamed media,
454 are embedded. A tactical mission load posts only embedded ones (music, ambience, turn stingers) —
that is the honest ceiling, and the instrument now says so per event instead of leaving the reader to
find out after baking.

**Timing is the technique.** A watch armed after the menu is already up catches nothing, because the
music was posted while it loaded. That case prints
`nothing was posted, so there is nothing to name. Arm the watch, then do the thing you want to hear.`

## 3. The sounds-with-no-bake verdict — the packager now refuses

The reference said a project with `Content\` and no `Dist\` packages successfully and *"that is not a
hole, because such a mod still works"*. **True for the bundle routes, false for sounds**, and the two
were being stated as one rule:

| Source | Read on the player's machine? |
|---|---|
| `Content\Textures\`, `Meshes\`, `Models\`, `Videos\` | **yes** — the first-tick bake reads them |
| `Content\Audio\Replace\` | **no** — only `Dist\Sounds\<mediaId>.bnk` is ever loaded |

So an unbaked sound mod installed, enabled, printed nothing wrong and played the shipped sound. That
is the silent-dead-package class this project keeps closing, so the packager refuses it by name and
deletes the staged folder.

Falsified both ways on **one project, one bank file apart** (`S19` arms, offline):

- `S19-refuse-unbaked` — two of three sounds baked, the third not: `!ok`, the message names
  `18839791.mp3`, staged folder gone.
- `S19-baked-packages` — bake the third bank, change nothing else: `ok`, and now all three sources
  are left behind because all three have banks.
- `S19-no-bank-refuses-all` — delete `Dist\Sounds\`: all three named.
- `S19-control-no-sounds` — a texture project with `Content\` and no `Dist\` still packages, so the
  rule did not spill onto the routes where the old text was right.
- `S19-refuse-is-not-about-redistribution` — the refusal does not print the "never redistribute
  Phoenix Point's data" preamble, which would have been a lie in front of it.

## 4. "Post the event by ID instead" — the command now exists

Both the sounds and videos pages told a reader to post an event by ID when a probe by media ID
refused, and **no command did that**. Rather than delete the advice — the video page's own 6034 ms
measurement was taken that way — the branch is now real:

```text
> ct_sound probe event 1015492702
probe event 1015492702: 'PP_Intro' in Cinematics -> no STREAMED media named 'PP_Intro', so its sound is embedded in a bank and cannot be replaced by a media bank
LoadBank(Cinematics): AK_Success
POST event/1015492702: playingID=1 dur=6034ms estDur=6034ms mediaID=908611677 streaming=false(MEMORY) endOfEvent=TIMEOUT

> ct_sound probe event 4000000001
probe event 4000000001: no shipped bank .txt names this event (a mod's own event, or a bank that ships no listing)
POST event/4000000001: playingID=0 POST FAILED (the event did not start; no callback can arrive and nothing below was measured)
```

Exactly the reading the videos page quotes, and a bogus event ID in the same session fails — which is
what makes the positive mean anything.

**One measurement worth keeping:** without loading the declaring banks first, posting the very event
the game had itself posted seconds earlier returned `playingID=0`. A bank the game is finished with
is not resident, and an unresident event does not start. `ct_sound probe event` therefore loads every
shipped bank whose `.txt` declares the event before posting.

## 5. Console output the site quoted but never showed

Captured live and placed on the pages: `ct_version`, `ct_catalog status`, `ct_catalog verify`,
`ct_sound bake`, `ct_sound status` (with and without a media ID), `ct_list audio`, `ct_list defs`, and
`ct_project` run on a project whose `Content\` exists and is **empty** — the undocumented case a blind
agent met:

```text
> ct_project EmptyContent
project 'morgott.demo.emptycontent' at ...\Mods\ContentTool\EmptyContent: 0 texture(s), 0 mesh(es), 0 model(s), 0 video(s), 0 sound(s), 0 replacement(s)
nothing to bake - put .png/.jpg under Content\Textures\, .glb under Content\Models\ or .wav under Content\Audio\
```

It is not an error, it does not write an empty bundle, and it does not claim success.
