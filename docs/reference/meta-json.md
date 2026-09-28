# `meta.json` reference

Put `meta.json` at the root of the mod folder. Phoenix Point's mod manager uses it to list and load the mod. `ct_package` reads it before making a publishable package.

The file must be a JSON object. For `meta.json`, comments (`//` and `/* ... */`) and trailing commas before `}` or `]` are accepted. Keep the documented field spelling even though the packager first checks the exact spelling and then accepts another casing.

| Field | Type | Required | Meaning | Example |
|---|---|---|---|---|
| `ID` | non-empty string | Yes | Mod identity used by the mod manager. Use the same value as `ppcontent.json`'s `id`. | `"author.mymod"` |
| `Dependencies` | array of strings | Yes | Must contain the exact value `com.morgott.ContentTool` so the mod manager enables ContentTool with this mod. Other dependencies may also be present. | `["com.morgott.ContentTool"]` |
| `AssemblyName` | string | Only for a mod with its own DLL | Name of the mod's DLL. Use `""` or omit the field for a content-only mod. | `"<mod>.dll"` |

`ct_package` checks that a declared `AssemblyName` exists in the staged package. It **does not compile** the DLL. The name must be a bare file name, without a directory or `*`/`?` wildcard. The packager searches the project for that exact file name, prefers a copy under `bin\Release`, excludes `obj`, and uses `bin\Debug` only after other locations. Build the DLL before packaging.

`ct_package` refuses an empty file, invalid JSON, a non-object root, a missing `ID`, a missing ContentTool dependency, or a declared DLL absent from the package. See [console commands](console-commands.md) for the packaging command and [project files](project-files.md) for the folder layout.
