# Videos: replace a catalog row or add one

A Phoenix Point cutscene usually has three independently addressed parts: picture, Wwise audio and
subtitles. A `video` row changes only the picture. Use the sound route for audio and behavior code
or a text-asset change when subtitles must change.

## Discover the row

Ask the live def repository which playback defs and RuntimeKeys the game uses, then find the loose
file:

```text
ct_video defs
ct_list videos intro
ct_extract video PP_Intro
```

`ct_extract video` is a byte-for-byte copy under `ContentTool\Extracted\videos` in AppData. Use it
to confirm duration, frame size and encoding behavior; do not redistribute game media.

## Replace a shipped video

Put your clip under `Content\Videos`:

```text
MyIntro\
  meta.json
  ppcontent.json
  Content\
    Videos\
      myintro.webm
```

Both forms are legal: the whole streaming path or the bare filename. Matching uses exact,
case-insensitive equality after slash normalization. ContentTool refuses zero matches or any value
that matches more than one row, so a bare filename is safe only while it is unique. Prefer the full
path printed by `ct_video defs`, as the shipped demo does:

```json
{
  "id": "yourname.myintro",
  "bundle": "MyIntro.bundle",
  "replace": [
    {
      "video": "myintro",
      "asset": "StreamableCopiedAssets/Videos/Factions/Phoenix/PP_Intro.webm"
    }
  ]
}
```

The presence of `asset` makes this a replacement. `video` is the lowercased stem under
`Content\Videos`; WEBM, MP4 and MOV sources are accepted by the importer. The game still has to
decode the chosen codec, so verify the actual file with ContentTool rather than relying on the
extension.

Serve and test without restarting:

```text
ct_video live MyIntro
ct_video resolve <runtime-key-from-ct_video-defs>
ct_video open <runtime-key-from-ct_video-defs>
ct_video play PP_Intro_Cinematic
```

`resolve` verifies the catalog row; `open` verifies that Unity can prepare the media; `play` exercises
the playback def. The def name above is the known intro example—use the one printed for your target.
A video replacement needs no `ct_project`: this route reads the manifest and loose video directly
and never reads the project's baked bundle.

Package the loose video and manifest:

```text
ct_package MyIntro
```

## Add a video

An added row omits `asset`:

```json
{
  "id": "yourname.outro",
  "bundle": "Outro.bundle",
  "replace": [
    {
      "video": "outro"
    }
  ]
}
```

With `Content\Videos\outro.webm`, ContentTool adds a catalog row whose RuntimeKey is the 32-character
lowercase MD5 of `<mod id>/<video stem>`. `ct_video live` prints that stable key. Paste it into the
`VideoPlaybackSourceDef` your behavior creates or changes.

```text
ct_video live Outro
ct_video resolve <printed-runtime-key>
ct_video open <printed-runtime-key>
```

The row makes the clip resolvable. Enabling the mod already serves every declared clip; the DLL does
not serve or bake the video. It supplies only a trigger because the manifest cannot create a menu
action, campaign event, or playback def.

### Worked trigger: play before quitting

Start with the shared, complete [DLL project and reference list](behavior-dll.md), substitute
`Outro` for `MyMod`, and set `"AssemblyName": "Outro.dll"` in `meta.json`. Paste the key printed by
`ct_video live` into `RuntimeKey` below. For the manifest above, MD5 of
`yourname.outro/outro` is `15d2a9ee51f6c38f21e974d708cd9dd2`.

This complete entry point patches the call used by both quit buttons. At the home screen it creates
a playback def, verifies the catalog resolution, hands the def to the game's own
`HomeScreenView.ToCutsceneState`, and lets its callback perform the real quit. An in-game quit has no
`HomeScreenView`, so it proceeds normally without the clip.

```csharp
using System;
using System.IO;
using System.Reflection;
using Base.Assets.StreamableSystem;
using Base.Core;
using Base.Defs;
using Base.UI.VideoPlayback;
using HarmonyLib;
using PhoenixPoint.Common.Game;
using PhoenixPoint.Home.View;
using PhoenixPoint.Modding;

namespace YourName.Outro
{
    public sealed class OutroMain : ModMain
    {
        internal const string RuntimeKey = "15d2a9ee51f6c38f21e974d708cd9dd2";
        internal static bool AllowRealQuit;
        internal static OutroMain Current;

        public override bool CanSafelyDisable => true;

        public override void OnModEnabled()
        {
            Current = this;
            ((Harmony)HarmonyInstance).PatchAll(Assembly.GetExecutingAssembly());
        }

        internal static VideoPlaybackSourceDef CreateSource()
        {
            DefRepository repo = GameUtl.GameComponent<DefRepository>();
            VideoPlaybackSourceDef def = repo.CreateRuntimeDef<VideoPlaybackSourceDef>();
            def.name = "YourName_Outro_Runtime";
            def.ResourcePath = "YourName/Outro/outro";
            def.VideoClipSource = new StreamableVideoClipReference
            {
                RuntimeKey = RuntimeKey
            };
            def.SkipOnPlayerInput = true;
            return def;
        }

        internal static bool Resolves(VideoPlaybackSourceDef def)
        {
            try
            {
                string path = def.VideoClipSource.GetStreamingPath();
                bool exists = !string.IsNullOrEmpty(path) && File.Exists(path);
                Current.Logger.LogInfo("outro key resolves to '" + path + "', exists=" + exists);
                return exists;
            }
            catch (Exception ex)
            {
                Current.Logger.LogError("outro key did not resolve: " + ex.Message);
                return false;
            }
        }
    }

    [HarmonyPatch(typeof(PhoenixGame), nameof(PhoenixGame.FinishLevelAndQuitGame))]
    internal static class QuitPatch
    {
        private static bool Prefix(PhoenixGame __instance)
        {
            if (OutroMain.AllowRealQuit)
            {
                return true;
            }

            HomeScreenView view = UnityEngine.Object.FindObjectOfType<HomeScreenView>();
            if (view == null)
            {
                return true;
            }

            VideoPlaybackSourceDef def = OutroMain.CreateSource();
            if (!OutroMain.Resolves(def))
            {
                return true;
            }

            view.ToCutsceneState(def, delegate
            {
                OutroMain.AllowRealQuit = true;
                __instance.FinishLevelAndQuitGame();
            });
            return false;
        }
    }
}
```

Build the def with `DefRepository.CreateRuntimeDef<VideoPlaybackSourceDef>()`, never
`ScriptableObject.CreateInstance`; the repository factory stamps its `Guid`. `ResourcePath` is the
def's identity/resource string, not a file path, and nothing loads the video through it. The clip is
loaded through `VideoClipSource.RuntimeKey`. Set `ResourcePath` to any non-null
`<Author>/<Mod>/<clip stem>` string, such as `YourName/Outro/outro`, that does not contain
`Game_Intro_Cutscene`. Other mods inspect this field: TFTV calls `ResourcePath.Contains(...)` and
throws when it is null, or treats a value containing that intro marker as an intro it may skip.
`SkipOnPlayerInput = true` is what lets Escape skip it. `GetStreamingPath()` is the pre-trigger
sanity check.

The working demo additionally finds
`Morgott.ContentTool.Bake.CatalogLive, ContentTool` and calls its public static `Register` by
reflection so an API version mismatch can be logged. That is defensive re-registration, not the
normal serving path: enabling the declared video mod has already registered the clip. Do not add a
compile-time reference to `UnityEngine.VideoModule`; see the
[profile-wide load failure warning](behavior-dll.md#managed-module-load-failure).

`ct_package` picks up the already-built DLL named by `AssemblyName`; it never compiles it. Use the
shared [build-to-mod-folder and restart loop](behavior-dll.md#name-the-real-dll) after code changes.

## Combine picture, audio and subtitles

One mod can contain a video row and sound replacement rows. Use `ct_video live` to test the video
row and `ct_sound bake` for replacement audio. Subtitle handling is behavior-specific and is not a
`ppcontent.json` route.

Keep all three timelines aligned. A video that prepares successfully can still look broken if the
old audio or subtitle timing belongs to a different edit.

## Limits

- ContentTool cannot infer which playback def owns a filename; use `ct_video defs`.
- A dangling def can exist with no shipped file. Replacing a plausible filename will not repair its
  wiring; create or update the def in behavior code.
- Added catalog rows do not play themselves.
- Disabling removes the live catalog mapping, but an already active cutscene should be allowed to
  finish or the screen should be reloaded before judging the disabled state.
