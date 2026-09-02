# Manifest core — domain model + safe writer — design

Status: **v1, 2026-09-02**. Owner decisions Q1-Q6 fixed before writing; recorded, not re-opened.
Peer review: Codex memo `C:\Temp\cx\3e4f6bee82894e01b577b029c15bbe34.out.md` (Q1-Q7), adopted
except where a decision below overrides it. Facts-file claims corrected in §3.
Unblocks: "Replace one mesh" wizard, then the lifecycle dashboard.

## 1. Goal

One UnityEngine-free file that can **read `ppcontent.json` into a typed facade over a real JSON
tree, add one `replace` row, and write it back without touching a single byte the author wrote
anywhere else in the file** — plus a shared atomic-write helper, plus the migration of the `replace`
readers off the regex that cannot see a nested map. The wizard is then three calls and no new file
format: `AliasMap.SaveSidecar` + `Manifest.AddMeshReplacement` + `ManifestFile.Save`.

## 2. Non-goals (this slice ships none of these)

- Any UI — no wizard, no dashboard, no panel.
- Editing or removing an existing `replace` row. **Add only** — that is all the wizard needs.
- Migrating `sounds`, `publish`, `creature` or `weapons` parsing (§6 follow-ups).
- Re-modelling the alias sidecar; `AliasMap.LoadSidecar/SaveSidecar` stays the sidecar API.
- `SlimJob`'s three atomic writes, and the in-game `JsonUtility` read path (`ContentProject.cs:287`, `:303`).

## 3. Current state

| What | Where | Problem |
|---|---|---|
| `replace` array read | `ContentProject.ParseReplace` `:385-435` | array regex `"replace"\s*:\s*\[(.*?)\]`, row regex `\{[^{}]*\}` — a nested map inside a row breaks the row; a `]` inside a string ends the array early |
| field read | `ContentProject.Field` `:539-542` | `"name"\s*:\s*"([^"]*)"` — string values only, escapes verbatim |
| row validation / "declared but empty" | `ContentProject.cs:404-425`, `:428-433` | exactly one of texture/material/mesh/clip/video, `bundle`+`asset` unless `video`; then `ppcontent.json declares "replace" but no complete entry was read from it` |
| shipped replace targets, root bundle | `Package.ReplaceTargets` `:444-451`, `OwnBundle` `:435-440` (+ `Bundles` `:453`, `Depth` `:467`) | every `"bundle"` at depth > 1 is a target — a `"bundle"` key inside any other nested block would be counted |
| payload presence / sounds | `Package.Ships` `:286-294`, `Package.DeclaredSounds` `:403-415` | presence-only regex over five rung names; same flat-row regex, deliberately silent |
| JSON reader | `Json` `GlbReader.cs:2311`, `Parse` `:2313` | recursive descent → `Dictionary`/`List`/`string`/`double`/`bool`/`null`; `Json.Fail` `:2440` throws `ImportRefusedException` (a `FormatException`) worded for GLB re-export |
| JSON writer | `JsonWriter` `GlbCodec.cs:1225`, `Val(object)` `:1245` | re-serializes a parsed tree, integral doubles as integers |
| atomic writes today | `AliasMap.SaveSidecar` `:227-258`, `SlimJob` x3, `WeaponManifest.Save` `:168-214` | five hand-rolled copies of tmp + `File.Replace`/`File.Move` |
| non-atomic writes | `ProjectBake.cs:798`, `ModelDoctor.cs:516`, `ReplacementFile.Save` | plain `File.WriteAllText` |

**Corrections to the inputs, each verified against source:**

- **`WeaponManifest` DOES have a writer** — `WeaponManifest.Save` `:168`, a validated atomic splice,
  BOM preserved `:184-186`, `.ct_tmp` + `File.Replace` `:196-205`. The facts file is wrong.
- **`CreatureManifest.Scaffold` does not write** — it returns text; `ProjectBake.cs:798` writes.
- **`AliasMap` schema cast is `:176`,** not `:174` (`:174` is `object schema;`); the defect is real,
  `(int)declared != Schema` accepts `1.5`.
- **`ContentProject.Field` cannot be deleted this slice** (correction to the brief) — still called
  by `ParseSounds:469` and `ParsePublish:508-511`, both left. It goes when they go.
- **`Json`/`JsonWriter` need no extraction** — `ObjCodecTests.csproj` already compiles
  `src\Import\GlbReader.cs` (line 190) and `src\Import\GlbCodec.cs` (line 140), so Codex's "move
  both into `src\Import\Json.cs`" is **not done**: a rename for nothing.
- `CreatureManifest.Block` `:401-413` brace-counts without string awareness (`:407-410`), so a `{`
  inside a string mis-terminates the block — do **not** reuse it as the span scanner.

## 4. Design

### 4.1 Types — `src\Project\Manifest.cs` (new, UnityEngine-free)

`ContentProject.cs` imports `UnityEngine` at `:7`, so this cannot live there.

```csharp
internal sealed class ManifestFile            // the FILE: bytes, BOM, newline, fingerprint, spans
{
    internal static ManifestFile Load(string path);    // throws InvalidDataException
    internal string   Path     { get; }
    internal Manifest Manifest { get; }
    internal void Save();                              // throws InvalidDataException / IOException
}
internal sealed class Manifest                // typed facade over the Json.Parse tree
{
    internal static Manifest Parse(string text);       // tree only, no id/bundle requirement
    internal string Id { get; }                        // + Bundle, Loop, Play (string), Scale (double?)
    internal IReadOnlyList<ReplaceRow> Replace { get; }        // existing rows + pending additions
    internal ReplaceRow AddMeshReplacement(string bundle, string asset, string meshFile);
    internal IDictionary<string, object> Root { get; }         // the raw tree, kept for round-trip
}
internal sealed class ReplaceRow              // facade over one row dictionary
{
    internal string Bundle { get; }           // + Asset, Texture, Material, Mesh, Clip, Video
    internal string Kind   { get; }           // texture|material|mesh|clip|video, null if not exactly one
}
```

- Reuses `Json.Parse` (`GlbReader.cs:2313`) and `JsonWriter` (`GlbCodec.cs:1225`) unchanged; depth
  cap 64, root must be a `Dictionary<string, object>`.
- `Json.Fail` throws `ImportRefusedException` with GLB "re-export it" wording, so both entry points
  **catch `FormatException` and rethrow `InvalidDataException`** carrying the path (E1).
- `Manifest.Parse(text)` is the tolerant entry (`Package` holds text, not a path, and may be handed
  a manifest with no `id`); `ManifestFile.Load(path)` is the strict file boundary.

### 4.2 `src\IO\AtomicFile.cs` (new)

`internal static void Write(string path, byte[] bytes, string backupPath = null)` and
`WriteText(string path, string text, Encoding enc, string backupPath = null)`.
tmp = `path + ".tmp"`; existing destination → `File.Replace(tmp, path, backupPath)`; new file →
`File.Move(tmp, path)` and **no** `.bak`. Best-effort `File.Delete(tmp)` on failure, then rethrow:
`AliasMap.SaveSidecar:246-257` verbatim, moved once. Manifest saves pass `path + ".bak"`.

### 4.3 The splice — Save keeps every byte outside the `replace` value span

Whole-tree reserialization is refused: it would lose BOM, CRLF, indentation, key order, number
spelling and unknown keys, and `Dictionary` insertion order is not contractual (`GlbDocument.cs:22`).

1. `Load`: read **bytes** once; BOM = `EF BB BF` prefix; decode the rest UTF-8 (`UTF8Encoding(false)`).
2. SHA-256 the raw bytes → the load fingerprint.
3. `Json.Parse(text, 64)`; root must be an object, else refuse (E1).
4. **Span scan of the ROOT object only.** One forward pass with `inString`/`escape` flags and a
   `{}`/`[]` depth counter: at depth 1 record each key and the `[start, end)` of its value; deeper,
   only maintain the counter. String contents never move it — the `CreatureManifest.Block:407`
   weakness, fixed, not reused. Record the root's closing `}` too.
5. Newline style = first `\r\n` in the text → CRLF, else LF.
6. `Save`: serialize the row once with `JsonWriter.Val(rowTree)`, then place it by case —
   **(a)** `replace` a non-empty array → `"," + newline + indent + row` before its closing `]`,
   `indent` copied from the last existing row's leading whitespace; **(b)** array holds only
   whitespace → `newline + indent + row + newline + closeIndent` between `[` and `]`, `indent` =
   the array line's indentation + 2 spaces; **(c)** `replace` absent → `"," + newline + "  \"replace\":
   [" + newline + "    " + row + newline + "  ]"` before the root `}`, as the last root member.
7. Re-`Json.Parse` the produced text and re-run §4.4 validation on it; refuse if either fails (E6).
8. Re-read the destination bytes and SHA-256; mismatch against the load fingerprint → refuse (E5).
9. `AtomicFile.Write(path, BOM ? bomBytes + utf8(text) : utf8(text), path + ".bak")`.

Everything outside the `replace` value span — a nested map inside an existing row included — is
byte-identical by construction. The written row is a flat object of string members, so the in-game
`JsonUtility` read of the root scalars is unaffected.

### 4.4 Validation — manifest, plus the one alias-sidecar fix

| # | Rule | When | On break |
|---|---|---|---|
| V1 | text parses as JSON, root is an object | Load, and again on the spliced text | `InvalidDataException`, nothing written |
| V2 | root `id` and `bundle` present, non-empty strings | Load only (not `Manifest.Parse`) | `InvalidDataException` (E2) |
| V3 | `replace`, if present, is an array of objects | Load + before write | `InvalidDataException` |
| V4 | every row selects **exactly one** of `texture`/`material`/`mesh`/`clip`/`video` | before write | `InvalidDataException` (E3) |
| V5 | `bundle` + `asset` non-empty unless the row is `video` | before write | `InvalidDataException` (E3) |
| V6 | every known field's value is a **string** (a number or map in `mesh` is not a row) | before write | `InvalidDataException` (E3) |
| V7 | no two rows share (`bundle` OrdinalIgnoreCase, `asset` Ordinal, kind) | before write | `InvalidDataException` (E4) |
| V8 | destination SHA-256 still equals the load fingerprint | immediately before commit | `IOException` (E5) |

V4/V5 are today's rule at `ContentProject.cs:404-416`, unchanged. Unknown fields and nested values
are accepted and retained. `asset` is never lowercased — shipped names go on verbatim (bundles are
matched `OrdinalIgnoreCase` at `ProjectBake.cs:1534`; assets are folded nowhere).

`AliasMap.cs:176` accepts a non-integral `schema` (`1.5` casts to `1`). Refuse a `schema` that is
not `Math.Floor(schema)`, with the existing sentence and `SidecarProblem.Invalid`. `SaveSidecar`
keeps its API and its hand-built text; only its commit moves to `AtomicFile`.

## 5. Migration

| Path | This slice | Why |
|---|---|---|
| `ContentProject.ParseReplace` `:385-435` | **migrate**, delete the regex body | the defect this slice exists for; signature `(string json, List<string> refusals = null)` kept, `ReplaceRow → ShippedReplacement` mapping lives in `ContentProject` |
| `Package.ReplaceTargets` `:444` (+ `Bundles` `:453-456`, **deleted**) | **migrate** to `Manifest.Parse(text).Replace` bundles | it reads the `replace` array; the depth heuristic counts any nested `"bundle"` |
| `Package.OwnBundle` `:435` | **migrate** to `Manifest.Parse(text).Bundle` | shares `Bundles`/`Depth` with the above and is *defined* by contrast with it; the pair must stay consistent (`S14-ownbundle`, `S14-order-blind`). Both return null/empty on a parse failure, preserving today's tolerance |
| `AliasMap.SaveSidecar` `:227`, `ModelDoctor` skel-plan write `:515-516` | **refactor** onto `AtomicFile` | one is the pattern's origin, the other a bare `File.WriteAllText` |
| `AliasMap.LoadSidecar` `:176` | **fix** integral schema | one line, real data-loss-adjacent bug |
| `ContentProject.Field` `:539-542`, `Package.Depth` `:467` | **leave** | `Field` still serves `ParseSounds`/`ParsePublish`; `Depth` still serves the balanced-brace refusal at `Package.cs:87` |
| `Package.Ships` `:286`, `Package.DeclaredSounds` `:403` | **leave** | a five-rung presence test and a `sounds` reader — neither reads a `replace` row |
| `ContentProject` `JsonUtility` reads `:287`, `:303` | **untouched** | the in-game read path must keep working |
| `SlimJob` x3, `WeaponManifest.Save`, `ProjectBake.cs:798`, `ReplacementFile.Save`, `ParseSounds`, `ParsePublish`, `CreatureManifest.*`, `WeaponBuild.Parse`, `BenchList.MirrorSave`, `PatchCache.Key`, `ContentToolMain:468`, `deploy.ps1` | **leave** | §6 |

Refusal semantics preserved exactly: an incomplete row goes to `refusals` (or throws when
`refusals == null`), and the "declares `replace` but no complete entry" sentence
(`ContentProject.cs:430`) is kept **verbatim**.

## 6. Follow-ups (`ponytail:` ledger)

- `ponytail:` onto `AtomicFile` — `SlimJob` x3 (in-game verified today; needs the validate-temp
  callback Codex sketched), `WeaponManifest.Save`'s commit, `ProjectBake.cs:798`, `ReplacementFile.Save`.
- `ponytail:` onto the tree — `ParseSounds`/`ParsePublish` (`ContentProject.Field` dies with them),
  `CreatureManifest.Block/Pairs/Flat/Field/Number/StripKey` (`Block:407` and `StripKey:531-537` are
  both string-unaware), `WeaponBuild.Parse:1618`.
- `ponytail:` `BenchList.MirrorSave`'s `File.Copy(..., true)` can overwrite a concurrent repo edit.
- `ponytail:` `Package.cs:87`'s brace check is a second parse of the same text; fold into `Parse`.
- Leave forever: `PatchCache.Key` (hashes opaque bytes), `ContentToolMain:468` (substring stamp),
  `deploy.ps1` (outside `src`).

## 7. Error messages (exact strings)

| id | Thrown | Text |
|---|---|---|
| E1 | `InvalidDataException`, `ManifestFile.Load` / `Manifest.Parse` | `"'" + path + "' is not valid JSON: " + inner.Message` (inner = the `FormatException` from `Json.Parse`; `Manifest.Parse` has no path, so its prefix is `ppcontent.json is not valid JSON: `) |
| E2 | `InvalidDataException`, `ManifestFile.Load` | `ppcontent.json needs both "id" and "bundle"` — identical to `ContentProject.cs:289`/`:305` |
| E3 | `InvalidDataException`, before write | `"replace" row REFUSED: every entry needs exactly one of "texture", "material", "mesh", "clip" or "video", plus "bundle" and "asset" for everything but "video" (a "video" entry with no "asset" ADDS a new clip); got ` + row + ` - SKIPPED, this project's other rows still bake` — verbatim from `ContentProject.cs:419-422` |
| E4 | `InvalidDataException`, before write | `ppcontent.json already replaces "<asset>" in "<bundle>" with a <kind>, so a second row for the same target was NOT written - edit the existing row instead` |
| E5 | `IOException`, before commit | `'<path>' changed on disk since it was loaded, so nothing was written - reload it and add the row again` |
| E6 | `InvalidDataException`, `ManifestFile.Save` | `the edited ppcontent.json did not re-read as valid JSON, so the file on disk was NOT touched` |
| E7 | reused, `AliasMap.LoadSidecar` | the existing schema sentence at `AliasMap.cs:178-180`, now also for a non-integral `schema` |

Read side, verbatim: `ppcontent.json declares "replace" but no complete entry was read from it` (`:430`).

## 8. Tests — new `tests\ObjCodecTests\ManifestTests.cs`

House pattern (`AliasTests.cs`): `internal static string Run()`, `checks += Check(cond, msg)` with
`Check` throwing on failure, temp dir under `Path.GetTempPath()` deleted in `finally`, final line
`MANIFEST PASS, N check(s) - ...`, wired into `Program.cs`. `ObjCodecTests.csproj` gains `Manifest.cs`,
`AtomicFile.cs`, `ManifestTests.cs`; fixtures are inline strings in the test file.

| Arm | What it proves |
|---|---|
| `Manifest_LoadsKnownAndUnknownTree` | root scalars typed; a nested map inside a `replace` row, an unknown root key and a `creature` block all survive Load — the case `\{[^{}]*\}` cannot read |
| `Manifest_AppendsMeshWithoutCollateralRewrite` | CRLF + BOM fixture: after adding one mesh row every byte outside the `replace` value span is identical, BOM and CRLF included |
| `Manifest_InsertsMissingReplaceArray` | a manifest with no `replace` key gets one as the last root member; reload reads exactly one valid row |
| `Manifest_RefusesMalformedWithoutWriting` | truncated JSON → `InvalidDataException`, original bytes unchanged, no `.tmp` and no `.bak` left |
| `Manifest_RefusesInvalidReplaceRows` | V4/V5/V6: no kind, two kinds, missing `asset`, a non-string `mesh` — each refused with E3 |
| `Manifest_RefusesDuplicateMeshTarget` | V7: same asset, bundle differing only in case → refused with E4 |
| `Manifest_RefusesConcurrentEdit` | file mutated between Load and Save → E5, and the external bytes are what remains on disk |
| `AtomicFile_WriteLeavesBakAndNoTmp` | overwrite leaves `.bak` holding the pre-write bytes and no `.tmp`; a first write leaves no `.bak` |
| `AliasSidecar_SchemaMustBeIntegral` | `"schema": 1.5` is refused (today it loads as 1) |

## 9. Acceptance

| id | Check | Command / evidence |
|---|---|---|
| M1 | Release build clean | `dotnet build -c Release` → 0 errors (1 known CS0649 allowed) |
| M2 | Offline gate green | `dotnet run --project tests\ObjCodecTests -c Release` → every line PASS, exit 0 |
| M3 | Path gate green after the `Package` migration | `dotnet run --project tests\TargetPathTests -c Release` → `S14-ownbundle`, `S14-order-blind`, `S14-order-packages` pass, exit 0 |
| M4 | Byte preservation, nested map included | `Manifest_AppendsMeshWithoutCollateralRewrite` + `Manifest_LoadsKnownAndUnknownTree` on the BOM + CRLF fixture; prefix and suffix byte-compared |
| M5 | No user edit can be lost | `Manifest_RefusesConcurrentEdit` (E5) + `AtomicFile_WriteLeavesBakAndNoTmp` (`.bak` holds the pre-write bytes) |
| M6 | Refusal wording unchanged | E3 and the `declares "replace"` sentence string-compared against `ContentProject.cs:419-422` / `:430` |
| M7 | In-game read path intact | `demos\CustomCreature` bakes and the mod activates after a tool-written row — PPCLI `connect state` on `D:\PP-Instance3`, `Player.log` gains no new exception |
| M8 | Owner visual check | owner confirms a hand-edited `ppcontent.json` is unchanged apart from the added row (the diff is one hunk) |
