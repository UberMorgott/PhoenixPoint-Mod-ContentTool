# Build a behaviour DLL

Use a DLL when your mod must decide *when* something happens: post a new sound event, start a new video, edit a def or call a builder. A content-only replacement needs no DLL.

## You need

- A .NET SDK that targets .NET Framework 4.7.2.
- Phoenix Point’s `ModSDK` and an installed ContentTool.
- A project folder whose `meta.json` names the DLL exactly.

## Folder layout

```text
MyCodeMod\
  meta.json
  ppcontent.json
  MyCodeMod.csproj
  src\
    MyCodeModMain.cs
  bin\
    Release\
      MyCodeMod\
        MyCodeMod.dll     <- preferred by ct_package; obj\ is never used
```

`ct_package` picks the DLL named in `AssemblyName`. It prefers `bin\Release`, then other project folders, then `bin\Debug`; the newest copy wins within a rank. It does not compile.

## Steps

1. Create `meta.json`:

   ```json
   {
     "ID": "example.mycodemod",
     "AssemblyName": "MyCodeMod.dll",
     "Version": "1.0.0",
     "Name": [{ "Key": "English", "Value": "My code mod" }],
     "Dependencies": ["com.morgott.ContentTool"]
   }
   ```

2. Create `ppcontent.json`, even if this DLL has no content row:

   ```json
   {
     "id": "example.mycodemod",
     "bundle": "MyCodeMod.bundle"
   }
   ```

3. Create `MyCodeMod.csproj`. Pass your game directory as `PPRoot` when building:

   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <AssemblyName>MyCodeMod</AssemblyName>
       <TargetFramework>net472</TargetFramework>
       <LangVersion>latest</LangVersion>
       <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
       <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
       <OutputPath>bin\$(Configuration)\MyCodeMod\</OutputPath>
       <ModSDK>$(PPRoot)\ModSDK</ModSDK>
     </PropertyGroup>
     <ItemGroup>
       <Compile Include="src\**\*.cs" />
       <Reference Include="ContentTool">
         <HintPath>$(PPRoot)\Mods\ContentTool\ContentTool.dll</HintPath>
         <Private>false</Private>
       </Reference>
       <Reference Include="Assembly-CSharp">
         <HintPath>$(ModSDK)\Assembly-CSharp.dll</HintPath>
         <Private>false</Private>
       </Reference>
     </ItemGroup>
   </Project>
   ```

   Add only the assembly references your source uses. Keep game and ContentTool references at `Private=false` so the release does not carry rival copies.

4. Create `src\MyCodeModMain.cs`. This example calls ContentTool’s public weapon builder when the mod is enabled; replace that call for your own behaviour:

   ```csharp
   using Morgott.ContentTool.Tactical;
   using PhoenixPoint.Modding;

   namespace Example.MyCodeMod
   {
       public sealed class MyCodeModMain : ModMain
       {
           public override bool CanSafelyDisable => true;

           public override void OnModEnabled()
           {
               WeaponBuild.Build(Instance.Entry.Directory, message => Logger.LogInfo(message));
           }
       }
   }
   ```

   A `creature` manifest block needs no explicit builder call: ContentTool calls `CreatureBuild.BuildAll` for enabled content mods after startup.

5. Build from the project folder, substituting the location of your installation:

   ```text
   dotnet build MyCodeMod.csproj -c Release -p:PPRoot="<Phoenix Point>"
   ```

   Check that `bin\Release\MyCodeMod\MyCodeMod.dll` exists. Then package:

   ```text
   ct_package MyCodeMod
   ```

## Check it worked

The build must succeed and produce `MyCodeMod.dll`. The package should finish with:

```text
PACKAGED <n> file(s), <bytes> B into <persistentDataPath>\ContentTool\Packaged\MyCodeMod
```

Check that the packaged folder contains `MyCodeMod.dll` and `meta.json`. Zip the **folder**, so extracting the archive into `Mods\` creates `Mods\MyCodeMod\meta.json`. If this example has valid `weapons` entries, check its `ct_weapon PASS` lines in `Player.log`.

## Common errors

| What you see | Why | Fix |
|---|---|---|
| `REFUSED: meta.json declares "AssemblyName": "MyCodeMod.dll" but the package does not contain that file` | The DLL was not built or found. | Build it, or correct `AssemblyName`. |
| `REFUSED: meta.json does not declare "Dependencies": [ "com.morgott.ContentTool" ]` | The package does not declare its engine dependency. | Add that dependency and package again. |
| `ct_weapon VOID no ppcontent.json in '<dir>'` | The builder received a folder without the manifest. | Keep `ppcontent.json` at the mod root and pass `Instance.Entry.Directory`. |
| `ct_weapon VOID ppcontent.json declares no "weapons" block` | The sample builder call has no entries to build. | Add valid `weapons` entries or use the behaviour your mod needs. |
| `ct_weapon FAIL '<id>' threw <Type>: <message>` | One declared weapon failed while building. | Read its named error in `Player.log`; see [messages](../reference/messages.md). |

A refused package begins `REFUSED - this package is NOT publishable`; do not distribute that output.

## Example

[WeaponAdd](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/WeaponAdd) calls the shared builder. [AddUiSounds](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/AddUiSounds) loads an added bank and posts its events. [QuitCutscene](https://github.com/UberMorgott/PhoenixPoint-Mod-ContentTool/tree/main/demos/QuitCutscene) supplies a video trigger.
