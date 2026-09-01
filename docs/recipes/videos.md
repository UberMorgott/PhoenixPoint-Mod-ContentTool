# Add or replace a video

This serves a WEBM, MP4 or MOV from your mod folder through Phoenix Point's live streamable catalog.
Name `asset` to replace a shipped clip; omit it to add a new runtime key. A DLL is needed only for
the behaviour that starts the new clip or changes subtitles, sound or flow.

## What you need before you start

- A WEBM, MP4 or MOV directly under `Content\Videos`.
- For Replace: the shipped streaming path or filename printed by `ct_list videos`.
- For Add: code that uses the runtime key printed by `ct_video live`.
- Separate audio/subtitle work if the cutscene needs it. A video row changes only the video file.

## Folder tree

```text
MyVideoMod\
  meta.json                    <- AssemblyName only when a trigger/def edit needs code
  ppcontent.json               <- video source stem and optional shipped asset path
  Content\
    Videos\
      campaign_intro.webm      <- direct child; source "campaign_intro"
  Dist\
    Sounds\                    <- optional separate sound replacement banks
```

## Steps

1. For Replace, find the shipped clip:

   ```text
   ct_list videos PP_Intro
   ```

2. Optionally extract it:

   ```text
   ct_extract video PP_Intro
   ```

   The command prints the output path under `ContentTool\Extracted\videos`.

3. Put your edited file directly in `Content\Videos` and rename it `campaign_intro.webm`.

4. Create `meta.json`:

   ```json
   {
     "ID": "example.myvideomod",
     "AssemblyName": "",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "My video mod" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

5. For Replace, create `ppcontent.json` with the shipped catalog path in `asset`:

   ```json
   {
     "id": "example.myvideomod",
     "bundle": "MyVideoMod.bundle",
     "replace": [
       {
         "video": "campaign_intro",
         "asset": "StreamableCopiedAssets/Videos/Factions/Phoenix/PP_Intro.webm"
       }
     ]
   }
   ```

   For Add, omit `asset`:

   ```json
   {
     "id": "example.myvideomod",
     "bundle": "MyVideoMod.bundle",
     "replace": [
       { "video": "campaign_intro" }
     ]
   }
   ```

6. Run `ct_project` to validate the declaration, then serve it live:

   ```text
   ct_project MyVideoMod
   ct_video live MyVideoMod
   ```

7. For Add, copy the runtime key from `ct_video live` into the code that starts the clip. Follow
   [Build a behaviour DLL](behavior-dll.md). For Replace, enabling the mod serves the row
   automatically; `ct_video live` is also the author's refresh command.

8. Package after `ct_project` passes:

   ```text
   ct_package MyVideoMod
   ```

## What success looks like

Replace validation ends with:

```text
video 'StreamableCopiedAssets/Videos/Factions/Phoenix/PP_Intro.webm' <- campaign_intro - serve it with: ct_video live MyVideoMod
ct_project: ALL PASS - nothing needed patching: none of this project's 1 replacement(s) names a shipped bundle, so no copy was written - the video row(s) above are served live by ct_video
```

The live command then prints:

```text
  MyVideoMod: 1 clip(s) served in memory from <project path>; nothing in the install was written
  <runtime key>
    before: <shipped path or current value>
    after:  <your source path>
    <registration result>
```

An Add row starts with `video ADD 'campaign_intro' (its RuntimeKey is printed by the command)` and
uses the same final summary.

## When it fails

| Exact output | Meaning | Fix |
|---|---|---|
| `SKIP 'campaign_intro' is not a .webm/.mp4/.mov under Content\Videos\` | The source stem was not imported. | Move the file directly into `Content\Videos`, use a supported extension, or correct `video`. |
| `SKIP no catalog row names '<asset>'` | A Replace row names no shipped path or filename. | Run `ct_list videos <filter>` and copy the returned path or unique filename. |
| `SKIP '<asset>' is ambiguous, <n> rows match:` | A filename matches several catalog rows. | Replace `asset` with the full streaming path printed below the refusal. |
| `REFUSED: no ppcontent.json in <root>` | `ct_video live` resolved a folder without a manifest. | Put `ppcontent.json` at the project root and remove a stale fallback copy. |

Read [the status glossary](../troubleshooting/bake-errors.md). Video live output uses `SKIP`, not a
P-number, because no shipped Unity bundle is patched.

## Quit-cutscene status

The main-menu exit path in `demos\QuitCutscene` has been measured: on 2026-09-01 it closed in 3.0
seconds against a 13.0-second deadline. The ESC-keypress skip path has not been run. That is the only
remaining unmeasured path; do not treat the normal exit as pending.

## Worked demos

- [IntroVideo](../examples/intro-video.md) replaces an existing streamed catalog row.
- [QuitCutscene](../examples/quit-cutscene.md) adds a new row and supplies its own trigger.
