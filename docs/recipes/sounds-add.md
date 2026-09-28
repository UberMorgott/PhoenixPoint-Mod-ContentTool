# Add a new sound

Add an event that Phoenix Point does not already have, then make your mod post it when you want the player to hear it.

!!! warning "Known limitation"
    `ct_project` bakes added sounds into your mod bundle and self-checks the bank, but ContentTool does **not** load that bank when players run the mod. Your own DLL must load it; otherwise the new sounds are silent. Files named `<name>.stream.wav`, `<name>.stream.ogg` or `<name>.stream.mp3` are treated as streamed sounds during baking, but their extracted `.wem` files are **not packaged** for players. Use embedded sounds for this recipe.

!!! tip "Finding a sound to match"
    The bench’s [Sounds](../bench/sounds.md) screen replaces existing sounds only, so it cannot add an event. Its search and `Play` buttons are still the quickest way to find and hear a shipped sound before you make your own.

## You need

- Mono or stereo WAV, OGG or MP3 files that you may distribute.
- A behaviour DLL that loads your bank and posts your new event. See [Build a behaviour DLL](behavior-dll.md).

## Folder layout

```text
MySoundMod\
  meta.json
  ppcontent.json
  Content\
    Audio\
      blip_rise.mp3       <- new, embedded sound
  src\
    MySoundModMain.cs     <- loads the bank and posts its event
  Dist\
    MySoundMod.bundle     <- ct_project writes the bank here
```

## Steps

1. Put `blip_rise.mp3` directly in `Content\Audio`. Its filename stem becomes part of the event name. Do not put it in `Content\Audio\Replace`.

2. Create `meta.json` with your compiled DLL as `AssemblyName`:

   ```json
   {
     "ID": "example.mysoundmod",
     "AssemblyName": "MySoundMod.dll",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "My sound mod" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

3. Create `ppcontent.json`. Added files need no `sounds` row:

   ```json
   {
     "id": "example.mysoundmod",
     "bundle": "MySoundMod.bundle"
   }
   ```

4. Bake the bundle:

   ```text
   ct_project MySoundMod
   ```

   The bank asset is `assets/example.mysoundmod/audio/banks/example_mysoundmod.bnk`. The event for this file is `example_mysoundmod_blip_rise`; its numeric event ID is the tool’s lower-case FNV-1 name hash. Added media IDs are allocated from the project ID. A `.stream` suffix is removed from an event name, but streamed files have the packaging limitation above.

5. Load the bank in your DLL and post its event. This is the relevant pattern from [AddUiSounds](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/AddUiSounds), with its own ID and bundle name:

   ```csharp
   string dir = Instance?.Entry?.Directory;
   string bundlePath = Path.Combine(dir ?? "", Path.Combine("Dist", "AddUiSounds.bundle"));
   AssetBundle bundle = AssetBundle.LoadFromFile(bundlePath);

   string underscored = ModId.Replace('.', '_');
   TextAsset bank = bundle.LoadAsset<TextAsset>(
       "assets/" + ModId + "/audio/banks/" + underscored + ".bnk");

   byte[] bytes = bank.bytes;
   GCHandle pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
   AKRESULT r;
   uint bankId;
   try { r = AkSoundEngine.LoadBankMemoryCopy(pin.AddrOfPinnedObject(), (uint)bytes.Length, out bankId); }
   finally { pin.Free(); }
   ```

   Adapt `ModId`, the bundle filename, null/error handling and the trigger to your mod. The demo hashes `underscored + "_" + Clips[i].ToLowerInvariant()` with the same FNV-1 function as the bake, registers an emitter and calls `AkSoundEngine.PostEvent` when the player presses Alt+B. Copy the complete source for those parts.

6. Build the DLL, then package:

   ```text
   ct_package MySoundMod
   ```

## Check it worked

The bake should report `A6 PASS` for an OGG or MP3 whose header can be checked, `BANK PASS` for the bank asset, and finish with:

```text
ct_project: ALL PASS - <project>\Dist\MySoundMod.bundle
```

A WAV may report `A6 VOID` because its decoder cannot provide an independent header check. Confirm that `MySoundMod.dll` is in the package. In game, use your DLL’s trigger and hear the sound; a bake pass alone does not prove runtime playback.

## Common errors

| What you see | Why | Fix |
|---|---|---|
| `SOURCE SKIPPED: <file> <reason> - SKIPPED, the project's other sources are unaffected` | One audio file could not be imported, including a source with more than two channels. | Export mono or stereo WAV, OGG or MP3 and bake again. |
| `ct_project: <n> FAILURE(S)` | A skipped source or another counted check failed. | Read the preceding named failure; see [messages](../reference/messages.md). |
| `BANK PASS` but no sound in play | The bake self-check loaded and then unloaded the bank. | Make your DLL load the bank with `LoadBankMemoryCopy` and post the matching event. |
| `REFUSED: meta.json declares "AssemblyName": "MySoundMod.dll" but the package does not contain that file` | The DLL was not built or found. | Build it before `ct_package MySoundMod`. |

## Example

[AddUiSounds](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/AddUiSounds) contains the complete bank loader, event hash, emitter and Alt+B trigger.
