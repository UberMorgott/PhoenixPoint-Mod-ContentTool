# Manifest core — domain model + safe writer — design

Status: **v1, 2026-09-02**. Owner decisions Q1-Q6 fixed before writing; recorded, not re-opened.
Peer review: Codex memos `3e4f6bee...out.md` (Q1-Q7) and `dcb260f7...out.md` (17 plan findings),
adopted where the owner accepted them. Facts-file claims corrected in §3.
Unblocks: "Replace one mesh" wizard, then the lifecycle dashboard.

## 1. Goal

One UnityEngine-free file that can **read `ppcontent.json` into a typed facade over a real JSON
tree, add one `replace` row, and write it back without touching a single byte the author wrote
anywhere else** — plus a shared atomic-write helper, plus the migration of the `replace` readers off
the regex that cannot see a nested map. The wizard is then three calls and no new file format:
`AliasMap.SaveSidecar` + `Manifest.AddMeshReplacement` + `ManifestFile.Save`.

## 2. Non-goals (this slice ships none of these)

- Any UI — no wizard, no dashboard, no panel.
- Editing or removing an existing `replace` row. **Add only** — that is all the wizard needs.
- Migrating `sounds`, `publish`, `creature` or `weapons` parsing (§6 follow-ups); re-modelling the
  alias sidecar (`AliasMap.LoadSidecar/SaveSidecar` stays the sidecar API).
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

- **`WeaponManifest` DOES have a writer** — `Save:168`, a validated atomic splice, BOM preserved
  `:184-186`, `.ct_tmp` + `File.Replace` `:196-205`; **`CreatureManifest.Scaffold` does not write** —
  it returns text, `ProjectBake.cs:798` writes. **`AliasMap`'s schema cast is `:176`,** not `:174`
  (`:174` is `object schema;`); the defect is real, `(int)declared != Schema` accepts `1.5`.
  **`ContentProject.Field` cannot be deleted this slice** — `ParseSounds:469` and
  `ParsePublish:508-511` still call it; it goes when they go.
- **`Json`/`JsonWriter` DO need extraction** (corrects an earlier reading of the facts file). Only
  `ObjCodecTests.csproj` compiles `GlbReader.cs`/`GlbCodec.cs` (`:190`, `:140`); four projects link
  ContentTool source directly and break otherwise — `tests\TargetPathTests` (`:62`) and `tools\Package`
  (`:14`) link `Package.cs` alone, `tools\ClipEvents` and `tools\SpiderAxisCheck` (`:18-19` each)
  compile both GLB files and would lose the classes. → move `Json` (`GlbReader.cs:2306-2444`) +
  `JsonWriter` (`GlbCodec.cs:1221-1332`) **verbatim** into `src\Import\Json.cs`, linked into all four.
  Measured 2026-09-02: the two GLB tools are ALREADY red (`CS0234` on `Bake`, `CS0246` on `ImportCode`), so M1 measures their error COUNT, not `0 Error(s)`.
- `CreatureManifest.Block` `:401-413` brace-counts without string awareness (`:407-410`), so a `{`
  inside a string mis-terminates the block — do **not** reuse it as the span scanner.

## 4. Design

### 4.1 Types — `src\Project\Manifest.cs` (new, UnityEngine-free)

`ContentProject.cs` imports `UnityEngine` at `:7`, so this cannot live there.

```csharp
internal sealed class ManifestFile            // the FILE: bytes, BOM, newline, fingerprint, spans
{   internal static ManifestFile Load(string path);    // throws InvalidDataException
    internal string   Path     { get; }
    internal Manifest Manifest { get; }
    internal void Save();                              // throws InvalidDataException / IOException
}
internal sealed class Manifest                // typed facade over the Json.Parse tree
{   internal static Manifest Parse(string text);       // tree only, no id/bundle requirement
    internal string Id { get; }                        // + Bundle, Loop, Play (string), Scale (double?)
    internal IReadOnlyList<ReplaceRow> Replace { get; }        // existing rows + pending additions
    internal ReplaceRow AddMeshReplacement(string bundle, string asset, string meshFile);
    internal IDictionary<string, object> Root { get; }         // the raw tree, kept for round-trip
}
internal sealed class ReplaceRow              // facade over one row dictionary
{   internal string Bundle { get; }           // + Asset, Texture, Material, Mesh, Clip, Video
    internal string Kind   { get; }           // texture|material|mesh|clip|video, null if not exactly one
}
```

- Reuses `Json.Parse` and `JsonWriter` unchanged (moved to `src\Import\Json.cs`, §3); depth cap 64,
  root must be a `Dictionary<string, object>`. `Json.Fail` throws `ImportRefusedException` with GLB
  "re-export it" wording, so both entry points **catch `FormatException` and rethrow
  `InvalidDataException`** carrying the path (E1).
- **Decode with `new UTF8Encoding(false, true)`** (V1): a byte that is not UTF-8 THROWS (wrapped into
  E1) rather than becoming U+FFFD, which `Save` would then write back over the author's bytes.
  Re-encoding stays `UTF8Encoding(false)` — every character came from a strict decode, so lossless.
- `Manifest.Parse(text)` is the tolerant entry (`Package` holds text, not a path, and may be handed
  a manifest with no `id`); `ManifestFile.Load(path)` is the strict file boundary.

### 4.2 `src\IO\AtomicFile.cs` (new)

`Write(path, byte[] bytes, backupPath = null)` and `WriteText(path, text, Encoding, backupPath)`
(encodes, then calls `Write`; the encoding's PREAMBLE is NOT written — a BOM belongs in the bytes
overload). tmp = `path + "." + Guid("N") + ".tmp"`, `FileMode.CreateNew`, written, `Flush(true)`;
then existing destination → `File.Replace(tmp, path, backupPath)`, new file → `File.Move(tmp, path)`
and **no** `.bak`. All in ONE `try/finally` whose `finally` best-effort `File.Delete(tmp)`s: a no-op
after a successful swap, the cleanup otherwise, original exception untouched. The UNIQUE name is the
point — a fixed `.tmp` (`AliasMap:245`) lets writers collide and a crash-left file linger. Manifest
saves pass `path + ".bak"`.

### 4.3 The splice — Save keeps every byte outside the `replace` value span

Whole-tree reserialization is refused: it loses BOM, CRLF, indent, key order, number spelling and
unknown keys (`Dictionary` order is not contractual, `GlbDocument.cs:22`).

1. `Load`: read **bytes** once; BOM = `EF BB BF` prefix; decode the rest strict UTF-8
   (`UTF8Encoding(false, true)`, §4.1).
2. SHA-256 the raw bytes → the load fingerprint.
3. `Json.Parse(text, 64)`; root must be an object, else refuse (E1).
4. **Span scan of the ROOT object only.** One forward pass with `inString`/`escape` flags and a
   `{}`/`[]` depth counter: at depth 1 record each key and the `[start, end)` of its value, deeper
   only keep the counter honest; string contents never move it (the `CreatureManifest.Block:407`
   weakness, fixed, not reused). Record the root's closing `}` too. Each key LITERAL is decoded by
   handing it, quotes included, to `Json.Parse`, so scanner and tree agree on what a key spells (a key
   whose `r` is a `u0072` escape IS `replace`); two root keys decoding to one name refuse (V9/E8).
5. Newline style = first `\r\n` in the text → CRLF, else LF.
6. `Save`: serialize the row once with `JsonWriter.Val(rowTree)`, then place it by case —
   **(a)** non-empty array → `"," + newline + indent + row` inserted immediately AFTER the last
   existing row's LAST BYTE, so the author's whitespace between that row and the closing `]` is
   copied, not regenerated; `indent` = that row's own leading whitespace. **(b)** whitespace-only or
   inline `[]` → `newline + indent + row + newline + closeIndent` between the brackets, `indent` =
   the array line's indentation + 2 spaces. **(c)** absent → `"," + newline + "  \"replace\": [" +
   newline + "    " + row + newline + "  ]"` before the root `}`, as the last root member — so a file
   with no final newline ends `...]}`, **accepted as written**: this tool inserts, never reformats.
7. Re-`Json.Parse` the produced text and re-run §4.4 validation on it; refuse if either fails (E6).
8. Re-read the destination bytes and SHA-256; mismatch against the load fingerprint → refuse (E5).
9. `AtomicFile.Write(path, BOM ? bomBytes + utf8(text) : utf8(text), path + ".bak")`.

Everything outside the span — a nested map in an existing row included — is byte-identical by
construction, and the written row is flat strings, so the in-game `JsonUtility` read is unaffected.

### 4.4 Validation — manifest, plus the one alias-sidecar fix

| # | Rule | When | On break |
|---|---|---|---|
| V1 | bytes decode as **strict** UTF-8 (`UTF8Encoding(false, true)`), text parses as JSON, root is an object | Load, and again on the spliced text | `InvalidDataException` (E1), nothing written |
| V2 | root `id` and `bundle` present, non-empty strings | Load only (not `Manifest.Parse`) | `InvalidDataException` (E2) |
| V3 | `replace`, if present, is an array of objects | Load + before write | `InvalidDataException`, text `Manifest.NotAnArray` (§7) |
| V4 | every row selects **exactly one** of `texture`/`material`/`mesh`/`clip`/`video` | before write | `InvalidDataException` (E3) |
| V5 | `bundle` + `asset` non-empty unless the row is `video` | before write | `InvalidDataException` (E3) |
| V6 | every known field that is PRESENT is a **string** — a number, map or JSON `null` in `mesh` is not a row | before write | `InvalidDataException` (E3) |
| V7 | no two rows share (`bundle` OrdinalIgnoreCase, `asset` Ordinal, kind) | before write | `InvalidDataException` (E4) |
| V8 | destination SHA-256 still equals the load fingerprint | immediately before commit | `IOException` (E5) |
| V9 | no two ROOT keys DECODE to the same name (an escaped spelling of `replace` is `replace`) | Load, during the span scan | `InvalidDataException` (E8) |

V4/V5 are today's rule at `ContentProject.cs:404-416`, unchanged. Unknown fields and nested values are
retained. `asset` is never lowercased — shipped names go on verbatim (bundles fold
`OrdinalIgnoreCase` at `ProjectBake.cs:1534`; assets fold nowhere). `AliasMap.cs:176` accepts a
non-integral `schema` (`1.5` casts to `1`): refuse a `schema` that is not `Math.Floor(schema)` **or
not equal to `Schema`**, comparing the parsed double directly with no integer cast, keeping the
existing sentence and `SidecarProblem.Invalid`. `SaveSidecar` keeps its API and hand-built text; only
its commit moves to `AtomicFile`.

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
(`ContentProject.cs:430`) is kept **verbatim**. Same for the new structural refusals — an unparseable
manifest, or a `replace` holding a primitive (V3): collected when there is a list, **rethrown** when
there is not, or a broken manifest reads as "this project replaces nothing". `ParseReplace`'s **name
and `(string, List<string>)` signature are load-bearing** — `RefusalCount.cs:39` `Assembly.LoadFrom`s
the built `ContentTool.dll` and invokes it by reflection (`:57-60`, `:110-117`); the migrated body
touches no UnityEngine type, so that gate still runs it, and is where this migration is proved.

## 6. Follow-ups (`ponytail:` ledger)

- `ponytail:` onto `AtomicFile` — `SlimJob` x3 (needs a validate-temp callback), `WeaponManifest.Save`'s commit, `ProjectBake.cs:798`, `ReplacementFile.Save`.
- `ponytail:` onto the tree — `ParseSounds`/`ParsePublish` (`ContentProject.Field` dies with them),
  `CreatureManifest.Block/Pairs/Flat/Field/Number/StripKey` (`Block:407`, `StripKey:531-537`: both
  string-unaware), `WeaponBuild.Parse:1618`.
- `ponytail:` `BenchList.MirrorSave`'s `File.Copy(..., true)` overwrites a concurrent repo edit; `Package.cs:87`'s brace check is a second parse of the same text, fold it into `Parse`; `tools\ClipEvents` and `tools\SpiderAxisCheck` need `src\Bake\` on their compile list to build at all.
- `ponytail:` no manifest-only LEXICAL validator. `Json.Parse` is shared with the GLB reader and
  accepts duplicate NESTED keys (last wins), raw control chars in strings and number spellings JSON
  lacks (`1.`, `+1`, `1e`); a stricter manifest-only pre-pass — never for GLB — is the upgrade if a
  real file trips. V9 already covers the only case that silently CHANGES a file: duplicate ROOT keys.
- Leave forever: `PatchCache.Key` (opaque bytes), `ContentToolMain:468` (stamp), `deploy.ps1`.

## 7. Error messages (exact strings)

| id | Thrown | Text |
|---|---|---|
| E1 | `InvalidDataException`, `ManifestFile.Load` / `Manifest.Parse` | `"'" + path + "' is not valid JSON: " + inner.Message` (inner = the `FormatException` from `Json.Parse`; `Manifest.Parse` has no path, so its prefix is `ppcontent.json is not valid JSON: `) |
| E2 | `InvalidDataException`, `ManifestFile.Load` | `ppcontent.json needs both "id" and "bundle"` — identical to `ContentProject.cs:289`/`:305` |
| E3 | `InvalidDataException`, before write | `"replace" row REFUSED: every entry needs exactly one of "texture", "material", "mesh", "clip" or "video", plus "bundle" and "asset" for everything but "video" (a "video" entry with no "asset" ADDS a new clip); got ` + row + ` - SKIPPED, this project's other rows still bake` — verbatim from `ContentProject.cs:419-422` |
| E4 | `InvalidDataException`, before write | `ppcontent.json already replaces "<asset>" in "<bundle>" with a <kind>, so a second row for the same target was NOT written - edit the existing row instead` |
| E5 | `IOException`, before commit | `'<path>' changed on disk since it was loaded, so nothing was written - reload it and add the row again` |
| E6 | `InvalidDataException`, `ManifestFile.Save` | `the edited ppcontent.json did not re-read as valid JSON, so the file on disk was NOT touched` |
| E7 | reused, `AliasMap.LoadSidecar` | the existing schema sentence at `AliasMap.cs:178-180`, now also for a non-integral `schema`, with the value spelled `ToString("R", InvariantCulture)` and no integer cast anywhere |
| E8 | `InvalidDataException`, `ManifestFile.Load` (V9) | `'<path>' declares the root key "<key>" twice, so it cannot be edited safely - delete one of them` |
| — | `InvalidDataException`, `Manifest` ctor (V3) | `Manifest.NotAnArray`: `ppcontent.json's "replace" must be an ARRAY OF ROWS - a value of any other shape declares nothing this tool can read or write` |

Read side, verbatim: `ppcontent.json declares "replace" but no complete entry was read from it` (`:430`).

**What "verbatim" covers in E3:** the SENTENCE, byte-identical to `ContentProject.cs:419-422`. The row
after `got ` is `JsonWriter`'s canonical spelling of the PARSED row — spacing, escapes and key order may differ from the file's, deliberately: it shows nested members `\{[^{}]*\}` never saw.

## 8. Tests — new `tests\ObjCodecTests\ManifestTests.cs`

House pattern (`AliasTests.cs`): `internal static string Run()`, `checks += Check(cond, msg)`, a temp
dir deleted in `finally`, final line `MANIFEST PASS, N check(s) - ...`, wired into `Program.cs`.
`ObjCodecTests.csproj` gains `Json.cs`, `AtomicFile.cs`, `Manifest.cs`, `ManifestTests.cs`.

| Arm | What it proves |
|---|---|
| `Manifest_LoadsKnownAndUnknownTree` | root scalars typed; a nested map inside a `replace` row, an unknown root key and a `creature` block all survive Load — the case `\{[^{}]*\}` cannot read |
| `Manifest_AppendsMeshWithoutCollateralRewrite` | CRLF + BOM fixture: `[` and `]` located INDEPENDENTLY in the before and after bytes, everything outside them identical, the old row still a contiguous byte run, and the author's whitespace before `]` copied not regenerated |
| `Manifest_InsertsMissingReplaceArray` | no `replace` key → one added as the last root member; also an inline `[]`, and a file with no final newline whose accepted output ends `]}` |
| `Manifest_RefusesMalformedWithoutWriting` | truncated JSON, a byte that is not UTF-8 (V1), and two root keys that decode alike (V9/E8) → `InvalidDataException`, original bytes unchanged, no temp and no `.bak` left |
| `Manifest_RefusesInvalidReplaceRows` | V4/V5/V6: no kind, two kinds, missing `asset`, a map in `mesh`, a JSON `null` in `clip` — each refused with E3 |
| `Manifest_RefusesDuplicateMeshTarget` | V7: same asset, bundle differing only in case → refused with E4 |
| `Manifest_RefusesConcurrentEdit` | file mutated between Load and Save → E5, and the external bytes are what remains on disk |
| `AtomicFile_WriteLeavesBakAndNoTmp` | overwrite leaves `.bak` with the pre-write bytes, a first write leaves none, no temp survives either; a stale `.tmp` from an old crash blocks nothing, and a write that cannot commit leaves no temp of its own |
| `AliasSidecar_SchemaMustBeIntegral` | `"schema": 1.5` is refused (today it loads as 1) |

## 9. Acceptance

| id | Check | Command / evidence |
|---|---|---|
| M1 | Release build clean, **and no project that links ContentTool source directly gains an error** | `dotnet build -c Release` → 0 errors (1 known CS0649 allowed); `dotnet build tools\Package\Package.csproj -c Release` → `0 Error(s)`; `tools\ClipEvents` and `tools\SpiderAxisCheck` → exactly ONE error, the pre-existing `GlbReader.cs(6,27) CS0234` on `Morgott.ContentTool.Bake` (they carried TWO before this slice, measured 2026-09-02), and nothing about `Json`/`JsonWriter`/`ImportCode` |
| M2 | Offline gate green | `dotnet run --project tests\ObjCodecTests -c Release` → every line PASS, exit 0 |
| M3 | Path gate green after the `Package` migration | `dotnet run --project tests\TargetPathTests -c Release` → `S14-ownbundle`, `S14-order-blind`, `S14-order-packages` pass, exit 0 |
| M4 | Byte preservation, nested map included | `Manifest_AppendsMeshWithoutCollateralRewrite` + `Manifest_LoadsKnownAndUnknownTree` on the BOM + CRLF fixture; prefix and suffix byte-compared |
| M5 | No user edit can be lost | `Manifest_RefusesConcurrentEdit` (E5) + `AtomicFile_WriteLeavesBakAndNoTmp` (`.bak` holds the pre-write bytes) |
| M6 | Refusal wording unchanged | E3 and the `declares "replace"` sentence string-compared against `ContentProject.cs:419-422` / `:430` |
| M7 | In-game read path intact | `demos\CustomCreature` bakes and the mod activates after a tool-written row — PPCLI on `D:\PP-Instance3`, `Player.log` gains no new exception. **Closed by the plan's final in-game acceptance task, not left open** |
| M8 | Owner visual check | owner confirms a hand-edited `ppcontent.json` is unchanged apart from the added row (the diff is one hunk) — recorded in the same final task |
