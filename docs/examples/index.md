# Worked examples

There are **11 public demos**. Pick the one closest to the mod you want to make, then keep its
manifest open beside the matching recipe. `NoDepTexture` is a test fixture, not a public example.

```text
demos\
  MaterialTweak\             <- smallest Replace project: manifest only
  WeaponMesh\                <- Replace a shipped weapon's art
  ReplaceUiSounds\           <- replace shipped Wwise media
  MenuMusic\                 <- filename-based sound replacement
  AddUiSounds\               <- add a bank and call it from a DLL
  IntroVideo\                <- picture, sound and subtitles use three mechanisms
  QuitCutscene\              <- add a video and trigger it from a DLL
  CustomCreature\            <- add a non-humanoid with its own rig and clips
  HumanoidSoldier\           <- add a retargeted playable soldier
  ReplaceCharacterBody\      <- experimental in-place body swap
  WeaponAdd\                 <- add three weapon defs and three model keys
  NoDepTexture\              <- internal dependency fixture; not published here
```

| Demo | What it shows | Recipe |
|---|---|---|
| [MaterialTweak](material-tweak.md) | Change one float on one shipped material. | [Material properties](../recipes/material-properties.md) |
| [WeaponMesh](weapon-mesh.md) | Replace the Ares rifle mesh, five textures and its inventory icon. | [Weapons](../recipes/weapon.md), [meshes](../recipes/meshes.md), [textures](../recipes/textures.md) |
| [ReplaceUiSounds](replace-ui-sounds.md) | Replace three shipped UI media IDs without a DLL. | [Sounds](../recipes/sounds.md) |
| [MenuMusic](menu-music.md) | Replace both editions' menu music by numeric filenames. | [Sounds](../recipes/sounds.md) |
| [AddUiSounds](add-ui-sounds.md) | Bake two new events into a mod bundle and play them on a hotkey. | [Sounds](../recipes/sounds.md), [behaviour DLL](../recipes/behavior-dll.md) |
| [IntroVideo](intro-video.md) | Replace a campaign intro's picture, Wwise media and subtitle field. | [Videos](../recipes/videos.md), [sounds](../recipes/sounds.md), [behaviour DLL](../recipes/behavior-dll.md) |
| [QuitCutscene](quit-cutscene.md) | Add a video key and intercept main-menu quit to play it. | [Videos](../recipes/videos.md), [behaviour DLL](../recipes/behavior-dll.md) |
| [CustomCreature](custom-creature.md) | Add a non-humanoid with its own rig and seven clips. | [Creatures](../recipes/creature.md), [animation contract](../recipes/animation-contract.md) |
| [HumanoidSoldier](humanoid-soldier.md) | Add a playable foreign humanoid with 300 retargeted game clips. | [Humanoid soldiers](../recipes/humanoid-soldier.md) |
| [ReplaceCharacterBody](replace-character-body.md) | Experimentally put that body on a DLC character without changing her identity. | [Replace a character body](../recipes/replace-character-body.md) |
| [WeaponAdd](weapon-add.md) | Publish three models, clone three weapon defs and seed new-campaign storage. | [Weapons](../recipes/weapon.md) |

“Verified” on these pages means a measured in-game run exists in the project's verification ledger.
A clean bake proves the files and declarations passed the bake gates. It does not prove what a player
saw in a campaign.
