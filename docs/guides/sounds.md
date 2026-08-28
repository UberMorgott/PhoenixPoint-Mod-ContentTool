# Sounds: replace shipped media or add your own

Phoenix Point asks Wwise to post an event; that event resolves to media. Replacement targets the
shipped media ID. Addition creates a new event/media identity, but something still has to post the
new event.

## Find a sound you heard

Arm the watcher, then cause the sound before the timer expires:

```text
ct_voices watch 20
```

A replaceable result has this shape:

```text
event 784388130 x1 'GUI_StatsPlusClick' in UI -> media 18839791 'GUI_StatsPlusClick' - replaceable
```

Confirm its metadata and play it back:

```text
ct_sound status 18839791
ct_sound probe 18839791
ct_extract audio 18839791
```

Extraction writes the original WEM and a decoded WAV under the ContentTool `Extracted\audio`
folder in AppData. Edit from the WAV, but create and redistribute your own recording.

Three dead ends are reported rather than guessed:

- `STOP event`: find the corresponding Start event;
- `no STREAMED media`: the media is embedded in a shipped bank and this route cannot replace it;
- no shipped bank text names the event: the installed listings cannot resolve that event-to-media
  relationship.

## Replace shipped media

The shortest declaration uses the media ID as the filename:

```text
MySound\
  meta.json
  ppcontent.json
  Content\
    Audio\
      Replace\
        18839791.mp3
```

```json
{
  "id": "yourname.mysound",
  "bundle": "MySound.bundle"
}
```

If you want a descriptive filename, use a `sounds` row instead:

```text
Content\Audio\Replace\my_click.mp3
```

```json
{
  "id": "yourname.mysound",
  "bundle": "MySound.bundle",
  "sounds": [
    {
      "media": 18839791,
      "file": "my_click.mp3"
    }
  ]
}
```

WAV, OGG and MP3 sources are accepted. Two files aimed at one media ID are refused. The target must
be a Phoenix Point media ID that ContentTool knows is replaceable.

Bake the replacement bank:

```text
ct_sound bake MySound
```

This replacement route does **not** use `ct_project`. `ct_sound bake` followed by `ct_package` is
the complete content path.

The output is `Dist\Sounds\18839791.bnk`. The player loads that bank; the source under
`Content\Audio\Replace` is not the runtime form and is left out by `ct_package` when the bank exists.
An unbaked replacement is refused during packaging.

Test the real game action, not only `ct_sound probe`. Wwise does not fall back to the shipped media
after a replacement bank is unloaded, so restart the game when testing the disabled state.

Package after the last bake:

```text
ct_package MySound
```

## Add a sound

Put a source directly under `Content\Audio`:

```text
AddSound\
  meta.json
  ppcontent.json
  Content\
    Audio\
      scanner_ping.wav
```

```json
{
  "id": "yourname.addsound",
  "bundle": "AddSound.bundle"
}
```

No `sounds` row is needed; that array is only for replacements. Unlike a replacement, an added
sound **does** need `ct_project`: it discovers added audio, allocates collision-checked identities,
and writes the mod's own bank into its bundle. Use
`scanner_ping.stream.wav` when the media should be streamed; `.stream` is removed from its content
name. Otherwise it is embedded.

```text
ct_project AddSound
```

Read the printed event/media IDs and use them in the behavior that should post the sound. A DLL is
needed only for that trigger. A content-only project can bake and load the sound, but silence is the
correct result until some code posts its event. If you add that trigger, start with the shared DLL
page and read its [profile-wide `Managed\` module warning](behavior-dll.md#managed-module-load-failure).

## Combined replacement and addition

The two routes may share a project. Run both bakes:

```text
ct_project SoundPack
ct_sound bake SoundPack
ct_package SoundPack
```

`ct_project` handles added audio. `ct_sound bake` handles shipped-media replacements. Neither
invokes the other.

## Limits

- Only loose/streamed shipped media is replaceable; bank-embedded media is not.
- Human-readable Wwise names exist only where shipped bank text exposes the relationship.
- Adding media does not attach it to a Phoenix Point event, ability or UI action.
- Source duration and loudness are authoring decisions. Verify them in the context where the game
  mixes and interrupts the sound.
