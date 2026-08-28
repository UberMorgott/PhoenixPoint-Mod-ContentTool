# The behaviour DLL

Most ContentTool projects are code-less. Exactly three kinds of work need a DLL: weapons, creatures,
and anything that needs a trigger or def edit. Pure replacements, publishing, and serving an added
video need none. This page is the complete minimum for the DLL routes.

## Project layout and references

Keep the C# project directly in `<PP install>\Mods\MyMod\`. Phoenix Point scans only the immediate
directories under `Mods\`. For each one it tries `<modDir>\<AssemblyName>` verbatim, then
`<modDir>\<folderName>.dll` as a fallback. It does not append `.dll` to `AssemblyName` and does not
search recursively. The built DLL must therefore sit directly in the mod folder; a DLL left under
`bin\` is never loaded.

```text
MyMod\
  MyMod.csproj
  MyMod.dll          written here by the build
  meta.json
  ppcontent.json
  src\
    MyModMain.cs
```

This project targets the framework shipped by Phoenix Point and references the player's own
installation. Replace the placeholder `PPRoot` with that installation's root in your IDE project.
ContentTool `1.0.0.0` is supported and tested against Phoenix Point **1.30.2.75117**
(`ReleaseCandidate2025`), whose ModSDK targets .NET Framework 4.7.2 (`net472`).

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>MyMod</AssemblyName>
    <RootNamespace>YourName.MyMod</RootNamespace>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>latest</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <OutputPath>$(MSBuildProjectDirectory)\</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <PPRoot>C:\Path\To\Phoenix Point</PPRoot>
    <ModSDK>$(PPRoot)\ModSDK</ModSDK>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="src\**\*.cs" />
    <Reference Include="Assembly-CSharp">
      <HintPath>$(ModSDK)\Assembly-CSharp.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="ContentTool">
      <HintPath>$(PPRoot)\Mods\ContentTool\ContentTool.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="0Harmony">
      <HintPath>$(ModSDK)\0Harmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>$(ModSDK)\UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

The `OutputPath` and `AppendTargetFrameworkToOutputPath` pair above makes an ordinary IDE build put
`MyMod.dll` where the loader can see it. If you prefer a normal `bin\` output, leave `OutputPath`
alone and add this target inside the project instead:

```xml
<Target Name="ToMods" AfterTargets="Build">
  <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(PPRoot)\Mods\MyMod\" />
</Target>
```

In that layout, run `ct_package` against the mod project folder that actually holds the copied DLL;
the packager searches only under that project. ContentTool's demos deliberately build under `bin\`
because a repository deploy script copies their output afterward. An author has no such script, so
do not copy the demos' output-path pattern.

`ModSDK\` means `<PP install>\ModSDK\`. Check that folder exists before building. The game supplies
it, not ContentTool; this project does not document a way to obtain or restore it if it is absent.
The shipped folder contains `0Harmony.dll`, `0Harmony.xml`, `Assembly-CSharp.dll`,
`UnityEngine.CoreModule.dll`, and `UnityEngine.UI.dll`. `PhoenixPoint.Modding` and `ModMain` are in
`Assembly-CSharp.dll`. That ModSDK assembly is a compile-time stub and lacks
`ApplyDefRepoPatches`, which is why the entry point below uses `OnModEnabled`.

Every game type the samples on this page use — `TacCharacterDef`, `GeoPhoenixFaction`,
`GeoscapeTutorial`, `PhoenixGame`, `HomeScreenView`, `DefRepository`, `VideoPlaybackSourceDef`,
`StreamableVideoClipReference`, `GameUtl`, `ModMain`, `ModInstance` and `ModEntry` — is present and
public in the ModSDK's `Assembly-CSharp.dll`, so the samples compile against the stub as written:
`GeoPhoenixFaction.CreateInitialSquad(GeoSite)`, `PhoenixGame.FinishLevelAndQuitGame()`,
`HomeScreenView.ToCutsceneState(VideoPlaybackSourceDef, Action)`,
`DefRepository.CreateRuntimeDef<T>(…)` and `GameUtl.GameComponent<T>()` are all public,
`ModMain.Instance.Entry.Directory` reaches your mod folder, and `ModMain.Logger` is a `ModLogger`
with `LogInfo` / `LogWarning` / `LogError`. Two things are not callable from the stub:
`GeoscapeTutorial.InitSquad` is private, which is why it is reached by a Harmony patch on the name
rather than a call, and `ApplyDefRepoPatches` is absent from the stub entirely.

The installed ContentTool assembly is `<PP install>\Mods\ContentTool\ContentTool.dll`. Keep all
references non-private: the release must not carry second copies.

## Managed module load failure

!!! danger "Do not reference Unity modules from `Managed\`"
    Do not add a compile-time reference to a Unity module under
    `PhoenixPointWin64_Data\Managed\` when `ModSDK\` does not ship that module. The measured example
    was `UnityEngine.VideoModule.dll`: the mod failed to load, and Phoenix Point responded by
    blanking `MOD_ACTIVATED`, silently disabling **every other mod** in the player's profile.
    Reference only the game and Unity assemblies supplied in `ModSDK\`; use reflection when code
    must reach a type that exists only in a `Managed\` module.

## Minimal `ModMain`

`ModMain` is an abstract class, but none of its members are abstract, so even an empty subclass
compiles. Its engine-populated, get-only properties are `Instance`, `MetaData`, `Config`,
`Dependencies`, `Logger`, `HarmonyInstance`, `ModGO`, `GeoscapeMod`, and `TacticalMod`. `Instance`
is an instance property, not a singleton; `Instance.Entry.Directory` is this mod's folder.
`HarmonyInstance` is typed as `object`, so cast it to `Harmony` before calling Harmony APIs.

`CanSafelyDisable` is virtual and defaults to `true`. The other public surface in the stub is
`GetGame()`, `GetLevel()`, and the virtual methods `OnConfigChanged`, `OnModEnabled`,
`OnModDisabled`, `OnLevelStateChanged`, `OnLevelStart`, and `OnLevelEnd`. Do **not** override
`ApplyDefRepoPatches`: it is missing from the ModSDK stub and that override will not compile.

`WeaponBuild` and `CreatureBuild` are public APIs in the `Morgott.ContentTool.Tactical` namespace.
At the first logging call below, `Logger` is a `ModLogger`: its real methods are `LogInfo(string)`,
`LogWarning(string)`, and `LogError(string, Exception ex = null)`. This complete entry point builds
every declared weapon row:

```csharp
using Morgott.ContentTool.Tactical;
using PhoenixPoint.Modding;

namespace YourName.MyMod
{
    public sealed class MyModMain : ModMain
    {
        public override bool CanSafelyDisable => true;

        public override void OnModEnabled()
        {
            WeaponBuild.Build(
                Instance.Entry.Directory,
                message => Logger.LogInfo(message));
        }
    }
}
```

The weapon builder applies its declared starting-storage quantities itself. A creature builder
returns the new def, but that alone does not place an actor in the game; use the
[complete two-seam creature entry point](creature.md#7-build-the-def-from-your-dll).

`OnModEnabled` runs after the def repository has loaded and before a campaign begins.

`Instance.Entry.Directory` is the installed folder of this mod. The builder reads
`ppcontent.json` there and resolves `Content\` and `Icons\` relative to that folder.

## Name the real DLL

For a code mod, `AssemblyName` is the filename, including `.dll`:

```json
{
  "ID": "yourname.mymod",
  "AssemblyName": "MyMod.dll",
  "Version": "1.0.0",
  "Author": [
    { "Key": "English", "Value": "Your Name" }
  ],
  "Name": [
    { "Key": "English", "Value": "My Mod" }
  ],
  "Description": [
    { "Key": "English", "Value": "Adds game behaviour and content. Requires ContentTool." }
  ],
  "Dependencies": [
    "com.morgott.ContentTool"
  ]
}
```

Build in Visual Studio or Rider before packaging. `ct_package` does not compile; it finds the newest
DLL with that exact name under the project and stages it. After rebuilding, restart Phoenix Point.
The game loads each assembly once and has no unload path. It reads the file without locking it, so
you may overwrite the DLL while the game runs, but the new code takes effect only after relaunch.
