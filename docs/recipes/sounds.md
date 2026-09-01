# Add or replace sounds

Use **Replace** when the game already posts the event you need: you swap its shipped Wwise media ID.
Use **Add** for a new sound. An added sound also needs code that loads the baked bank and posts its
new event; content alone cannot decide when to play.

## What you need before you start

- WAV, OGG or MP3 sources. FLAC, M4A/AAC, WMA and Opus are not accepted.
- For Replace: the numeric media ID Phoenix Point owns. `ct_extract audio` accepts a shipped WEM
  name and writes both the WEM and a decoded WAV for comparison.
- For Add: a unique file stem and a [behaviour DLL](behavior-dll.md) that loads/posts the event.
- Short replacement audio that suits the event. Replacement banks preserve the target's loop
  policy; an accidental long loop is still an accidental long loop.

## Folder tree

```text
MySoundMod\
  meta.json
  ppcontent.json               <- sounds[] is only for shipped-media replacement
  Content\
    Audio\                     <- ADD sources: .wav, .ogg, .mp3
      blip_rise.wav
      Replace\                 <- REPLACE sources; not scanned by the Add bake
        sting_confirm.mp3
  Dist\
    MySoundMod.bundle          <- Add bank, written by ct_project
    Sounds\
      633458426.bnk            <- Replace bank, written by ct_sound bake
```

Keep Add and Replace files in separate projects unless you deliberately need both. The tree above
shows both locations so you can see the boundary.

## Steps

1. For a replacement, identify and optionally extract the shipped media. `ct_list audio` filters the
   shipped audio index; use the numeric ID it prints:

   ```text
   ct_list audio confirm
   ct_extract audio 633458426
   ```

2. Name your replacement file `sting_confirm.mp3` and put it directly in
   `Content\Audio\Replace`. For an added sound, name it `blip_rise.wav` and put it directly in
   `Content\Audio`.

3. Create `meta.json`. Leave `AssemblyName` empty for a replacement-only mod. Set it to your built
   DLL for an Add mod with a trigger:

   ```json
   {
     "ID": "example.mysoundmod",
     "AssemblyName": "MySoundMod.dll",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "My sound mod" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

4. Create `ppcontent.json`. For Replace, add a `sounds` row. `file` includes its extension and must
   match a file in `Content\Audio\Replace`:

   ```json
   {
     "id": "example.mysoundmod",
     "bundle": "MySoundMod.bundle",
     "sounds": [
       { "media": 633458426, "file": "sting_confirm.mp3" }
     ]
   }
   ```

   For Add, there is no sound row. The file is the declaration:

   ```json
   {
     "id": "example.mysoundmod",
     "bundle": "MySoundMod.bundle"
   }
   ```

5. For Add, implement and build the trigger. Follow [Build a behaviour DLL](behavior-dll.md). Use
   `demos\AddUiSounds\src\AddUiSoundsMain.cs` as the checked-in example of loading the bank asset,
   deriving the FNV-1 event ID and calling `AkSoundEngine.PostEvent`.

6. Run the command for your route, then package:

   ```text
   ct_sound bake MySoundMod
   ct_package MySoundMod
   ```

   Or, for Add:

   ```text
   ct_project MySoundMod
   ct_package MySoundMod
   ```

## What success looks like

Replace prints one `baked` line per row and then the summary:

```text
baked <project>\Dist\Sounds\633458426.bnk: <bytes> B, bankId=<id>, media 633458426 = <ms>ms <channels>ch <rate>Hz, <loop report> from sting_confirm.mp3
ct_sound bake: 1 bank(s) in <project>\Dist\Sounds - NO game file was opened for writing. ContentTool loads these at init.
```

Add prints its decode check, writes the bundle, loads its bank back and ends:

```text
A6 PASS <source path> decoded <details> vs the source's own header: <details>
WROTE <project>\Dist\MySoundMod.bundle <bytes> B as example_mysoundmod
BANK PASS assets/example.mysoundmod/audio/banks/example_mysoundmod.bnk -> <LoadBankMemoryCopy: AK_Success details>
ct_project: ALL PASS - <project>\Dist\MySoundMod.bundle
```

A WAV has no independent header oracle in this check and prints `A6 VOID ...`; that is not a
failure. The `BANK PASS` and final `ALL PASS` remain the bake result.

## When it fails

| Exact output | Meaning | Fix |
|---|---|---|
| `"sounds" names 'sting_confirm.mp3' for media 633458426, and there is no such file in <dir>` | The replacement source is missing from `Content\Audio\Replace`. | Move that exact file there or correct `file`. |
| `two files aim at media 633458426: '<a>' and '<b>'` | Two declarations target the same shipped media. | Delete the unwanted row/file or correct its media ID. |
| `bake REFUSED 633458426 is not one of the <count> media IDs Phoenix Point owns - nothing would ever play it` | The number is not a shipped media ID. | Repeat discovery and use a real owned ID. Use Add for a new event. |
| `bake REFUSED sting_confirm.mp3 <reason>` | The decoder rejected the source. | Re-export it as WAV, OGG or MP3; delete the rejected copy. |
| `Content\Audio\Replace\ holds two files for the same media: <a> and <b> - one of them has to go` | Two source filenames collapse to the same declared media mapping. | Remove one duplicate. |
| `SOURCE SKIPPED: Content\Audio\ holds <n> file(s) this tool does not import: <files> - the accepted set is .wav, .ogg and .mp3, which this tool decodes itself at bake time. Export the rest to .ogg (small) or .wav (lossless); no decoder for .flac, .m4a/.aac, .wma or .opus is going into this tool (docs\research-format-coverage.md 2.1).` | An Add source has an unsupported extension. | Export it to OGG or WAV and delete the unsupported file. |

Read [the status glossary](../troubleshooting/bake-errors.md). `ct_sound bake` builds replacement
banks; `ct_project` builds added sounds. `ct_package` does neither.
