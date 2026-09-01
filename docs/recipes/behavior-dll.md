# Build a behaviour DLL

A DLL supplies decisions that content cannot express: a hotkey, a definition edit or an explicit
builder call such as `WeaponBuild.Build`. Do not add one to a content-only replacement.

## What you need before you start

- The .NET SDK capable of targeting .NET Framework 4.7.2.
- Phoenix Point's `ModSDK` folder and an installed ContentTool DLL.
- A project folder whose `meta.json` names the DLL exactly.
- Only the assembly references your source actually uses. Set game and ContentTool references to
  `Private=false`; the release must not carry rival copies.

## Folder tree

```text
MyCodeMod\
  meta.json                    <- AssemblyName must be MyCodeMod.dll
  ppcontent.json
  MyCodeMod.csproj             <- net472 build
  src\
    MyCodeModMain.cs           <- ModMain entry point
  bin\
    Release\
      MyCodeMod\
        MyCodeMod.dll          <- newest matching DLL is picked up by ct_package
```

`ct_package` searches the project recursively for the newest file with the declared assembly name.
It does not compile it.

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

2. Create a minimal `ppcontent.json` even if the behaviour has no content row:

   ```json
   {
     "id": "example.mycodemod",
     "bundle": "MyCodeMod.bundle"
   }
   ```

3. Create `MyCodeMod.csproj`. Change the default `PPRoot` if your game is elsewhere:

   ```xml
   <Project Sdk="Microsoft.NET.Sdk">
     <PropertyGroup>
       <AssemblyName>MyCodeMod</AssemblyName>
       <RootNamespace>Example.MyCodeMod</RootNamespace>
       <TargetFramework>net472</TargetFramework>
       <LangVersion>latest</LangVersion>
       <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
       <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
       <OutputPath>bin\$(Configuration)\MyCodeMod\</OutputPath>
       <PPRoot Condition="'$(PPRoot)' == ''">D:\Steam\steamapps\common\Phoenix Point</PPRoot>
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

4. Create `src\MyCodeModMain.cs`. This complete example calls the public weapon builder; replace
   that one call only when your route needs different behaviour:

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

   Creature mods with a `creature` block do **not** need this call in 1.1.2. ContentTool scans enabled
   content mods and calls `CreatureBuild.BuildAll` one frame after startup. `startingRoster: true`
   also handles their new-campaign placement.

5. Build from the project folder. Quote the property because the path contains a space:

   ```text
   dotnet build MyCodeMod.csproj -c Release -p:PPRoot="D:\Steam\steamapps\common\Phoenix Point"
   ```

6. Confirm `bin\Release\MyCodeMod\MyCodeMod.dll` exists. Do not copy ContentTool.dll or game DLLs
   beside it.

7. Package:

   ```text
   ct_package MyCodeMod
   ```

## What success looks like

The compiler ends with:

```text
Build succeeded.
    0 Error(s)
```

The packager then includes `MyCodeMod.dll` and ends with:

```text
PACKAGED <n> file(s), <bytes> B into <persistentDataPath>\ContentTool\Packaged\MyCodeMod
Zip the FOLDER itself, so the archive holds MyCodeMod\meta.json, and upload it. The player unzips it into Mods\ (ending up with Mods\<YourMod>\meta.json) or subscribes on the Workshop; the mod manager enables ContentTool for them because meta.json declares it.
```

If you used the sample builder call with a valid `weapons` entry, `Player.log` also contains one
`ct_weapon PASS` line for that entry.

## When it fails

| Exact output | Meaning | Fix |
|---|---|---|
| `REFUSED: meta.json declares "AssemblyName": "MyCodeMod.dll" but the package does not contain that file - the game refuses to load the mod. Build it, or set "AssemblyName": "" for a content-only mod.` | The named DLL was not found under the project. | Run the build, correct `AssemblyName`, or remove it for a content-only mod. |
| `REFUSED: meta.json does not declare "Dependencies": [ "com.morgott.ContentTool" ] - without it the player can install this mod with the engine switched off and it will silently do nothing. With it, Phoenix Point enables ContentTool for them.` | The dependency is missing. | Add the exact dependency string and package again. |
| `ct_weapon VOID no ppcontent.json in '<dir>'` | The builder was called with a folder that has no manifest. | Pass `Instance.Entry.Directory` and keep `ppcontent.json` at the mod root. |
| `ct_weapon VOID ppcontent.json declares no "weapons" block` | The sample builder call has no work. | Add a valid `weapons` array, or remove the call and implement the behaviour your mod needs. |

Read [the status glossary](../troubleshooting/bake-errors.md). A compiler error is neither a bake
failure nor a package refusal; fix it before running `ct_package`.
