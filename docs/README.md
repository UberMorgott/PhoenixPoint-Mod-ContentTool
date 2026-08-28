# ContentTool — docs index

In-game Phoenix Point content authoring + bake tool. Mod folder `ContentTool`, namespace
`Morgott.ContentTool`, assembly `ContentTool.dll`, console commands `ct_*`.

Read in this order:

1. **`PROVEN-FOUNDATIONS.md`** — READ FIRST. What is already closed by in-game measurement, with
   each evidence line. Never re-investigate anything in it.
2. **`FINAL-PLAN.md`** — the frozen architecture and the implementation task sequence.
   §39 is the two-mode replacement amendment (asset identity, developer-mode live swap,
   shipping-mode baked bindings, Tasks 22-28).
3. **`RECIPES.md`** — copy-ready exact API sequences (AssetsTools.NET, bundle registration,
   Wwise bank shapes, streaming) and the gotcha behind each.
4. **`METHODOLOGY.md`** — the test-discipline rules; obey them in every spike and regression.
5. **`research-zero-runtime-replacement.md`** — research, not a decision: §39's "visual replacement
   must be a runtime binding" is a policy line, not a format limit. Routes to true zero-runtime
   native replacement (Addressables catalog redirect, in-place bundle patch, forged external PPtr),
   with verdicts, price, and the three experiments that settle them.

Handoffs (dated, historical — read for the OPEN list, not for current instructions):
`HANDOFF-2026-08-12.md` (§"Open, in priority order" is the live open-item tracker),
`HANDOFF-sound-redesign.md`.

## Demo mods — `demos\`

One capability per mod, each with its own `README.md`. They are teaching artefacts: an author is
meant to open one, see the two or three things it consists of, and copy them.

| Demo | Shows | Our DLL? |
|---|---|---|
| `MenuMusic` | the vanilla main-menu music replaced, both edition tracks (route S1) | no |
| `ReplaceUiSounds` | three shipped geoscape UI sounds replaced — the CONTENT half, no code at all | no |
| `AddUiSounds` | two sounds the game never had, plus an Alt+B hotkey — the CODE half of the same line | yes |
| `IntroVideo` | the new-campaign video served from the MOD's own folder, no game file written (V1-open) | no |
| `QuitCutscene` | a video on quit — the trigger is the half that costs a DLL (Q1, VERIFIED) | yes, ~40 lines |
| `CustomCreature` | an UNMODIFIED CC0 download with its own 39-bone rig and its own 5 clips, played by Mecanim (U11, VERIFIED) | no |
| `WeaponMesh` | a shipped WEAPON's mesh AND its whole texture set replaced together (P4d, VERIFIED) | no |
| `WeaponAdd` | a NEW weapon — cloned defs plus a model served out of the mod's own bundle (route iii, VERIFIED) | yes, one call |

## Upstream ground truth — NOT in this repository

Two research notes preceded this tool and are cited throughout `FINAL-PLAN.md`. They live in the
author's Phoenix Point monorepo (`docs\research\`) and are **not part of this repository** — the
links below will not resolve for anyone who cloned it. Nothing here depends on reading them: every
finding this tool actually rests on was re-measured and is recorded in `PROVEN-FOUNDATIONS.md`.

- `pp-content-tool-findings-2026-08-12.md` — the capability handoff that fixed the product shape:
  the tool IS the mod DLL running inside Phoenix Point (no external builder), doing live replacement
  while an author iterates and a bake that emits ONE shippable AssetBundle plus a Wwise bank.
- `pp-audio-architecture-FROZEN.md` — the frozen audio decision: both new and replacement sounds are
  one `AkSoundEngine.LoadBankMemoryCopy` on a tool-generated `.bnk`. Wwise 2021.1.0 build 7575,
  BKHD version 140 (loader accepts 118..140); Wwise is up at t=0.000 and mods load at t=0.255, so
  every Wwise API is safe from `OnModEnabled()`; Unity audio is dead in PP (`m_DisableAudio = true`),
  so no `AudioSource` path exists. `RECIPES.md` carries the bank shapes this implies.
