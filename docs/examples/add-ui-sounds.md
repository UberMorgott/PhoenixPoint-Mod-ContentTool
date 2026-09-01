# AddUiSounds

This demo adds two sounds Phoenix Point does not ship. Press **Alt+B** in game and its DLL chooses
one of the two blips and posts the new Wwise event.

**Corresponds to:** [Add or replace sounds](../recipes/sounds.md) and
[Build a behaviour DLL](../recipes/behavior-dll.md).

## Features and how they work

- **Two new media and events are baked on the Add route.** `blip_rise.mp3` and `blip_fall.mp3` sit
  directly under `Content\Audio`; `ct_project` puts their bank inside `AddUiSounds.bundle`.
- **Names produce stable IDs.** The DLL uses lowercase FNV-1 names
  `morgott_demo_adduisounds_blip_rise` and `morgott_demo_adduisounds_blip_fall`; the measured event
  IDs are `1781464403` and `2693404503`.
- **The bank is loaded from the mod bundle.** The DLL reads
  `assets/morgott.demo.adduisounds/audio/banks/morgott_demo_adduisounds.bnk` as a `TextAsset` and
  passes a pinned memory copy to Wwise.
- **The hotkey is behaviour.** A `DontDestroyOnLoad` object polls Alt+B and posts an event. Content
  alone cannot invent that input path.

## Project on disk

```text
AddUiSounds\
  meta.json                       <- AssemblyName is AddUiSounds.dll
  ppcontent.json                  <- ID and own bundle name
  AddUiSounds.csproj
  Content\Audio\                  <- Add sources; not the Replace folder
    blip_rise.mp3
    blip_fall.mp3
  Dist\AddUiSounds.bundle         <- committed bundle containing the bank
  bin\Release\
    AddUiSounds.dll
    AddUiSounds.pdb
  src\AddUiSoundsMain.cs          <- loads the bank and handles Alt+B
  README.md
  SOURCES.md
```

## Rebuild and run it

Replace the `PPRoot` value with your game folder:

```text
dotnet build demos\AddUiSounds\AddUiSounds.csproj -c Release -p:PPRoot="D:\Steam\steamapps\common\Phoenix Point"
ct_project AddUiSounds
ct_package AddUiSounds
```

Restart with the demo enabled. Press Alt+B, then measure either event:

```text
ct_sound probe event 1781464403
ct_sound probe event 2693404503
```

## What a good run prints

```text
A6 PASS <source path> decoded <details> vs the source's own header: <details>
BANK PASS assets/morgott.demo.adduisounds/audio/banks/morgott_demo_adduisounds.bnk -> <LoadBankMemoryCopy: AK_Success details>
ct_project: ALL PASS - <project>\Dist\AddUiSounds.bundle
```

On enable, `Player.log` contains this line with measured byte and bank values:

```text
AddUiSounds: bank <bytes> B loaded as <bankId>, 2 clip(s) on Alt+B [blip_rise=1781464403, blip_fall=2693404503]
```

## Verification status

**Verified in-game on 2026-08-28.** `blip_rise` produced media `3338666241`, 392 ms with its end
callback at 405 ms. `blip_fall` produced media `3338666240`, 496 ms with its end callback at 507 ms.
Both were served from memory.
