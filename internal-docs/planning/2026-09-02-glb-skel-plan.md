# GLB Skeleton Implementation Plan (`ppskel.py` -> in-game C#)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Port `tools\ppskel.py` (378 lines) into the mod as `src\Import\GlbSkel.cs` + a third mode of
the existing slim panel, per design §9 order 3 ("ppskel 5-7 days: validation + explicit
rename/insert/collapse plans against selected prototype; drops the Tiffany-specific paths and the
hard-coded map (`ppskel.py:36`) - do NOT generalise them into automatic guesses").

Rewrite a foreign `.glb`'s SKELETON so its bone names and its transform paths are the ones a chosen
prototype spells, with three geometry-preserving edits and nothing else. What that buys is stated
twice in this repo and measured both times:

- **BY NAME binding.** `Addon.GetEquivalentBones`
  (`decompiled\AssemblyCSharp\Assembly-CSharp\src\PhoenixPoint.Common.Entities.Addons\Addon.cs:1203-1232`)
  skips every transform whose name starts with `EXT_` (`:1209`) and matches everything else with
  `ownBoneName == bone.name` inside a `FirstOrDefault` over `manager.GetAllRigOnlyBones()` (`:1217`).
  Exact, ordinal, case-sensitive string equality against the LITERAL `Transform.name`. That single
  line is what a rewrite has to achieve, and it is why a rename is the whole feature: nothing in the
  game does fuzzy matching, aliasing or retargeting on that path.
- **Clip binding by PATH.** A PP rig is Unity GENERIC, and `GenericBinding.path` is CRC-32 of the
  transform path RELATIVE to the Animator's GameObject (`src\Bake\ClipFields.cs:34-41`, measured on
  `Fireworm_unfurl` = `crc32("Fireworm_root/Fireworm_base")`, not remembered). So a clip authored on
  a PP prototype drives a foreign model if and only if that model spells the same paths.

The two questions are different and this port answers both separately: names for the Doctor's
verdict, paths for the clips. `GlbSkel.Verify` returns one list for each.

**The three edits, each geometry-preserving** (`ppskel.py:10-22`):

| Edit | What it does | Why it moves no vertex |
|---|---|---|
| RENAME | `nodes[i].name = to` | a string is not a transform |
| INSERT | a new node slipped between a parent and one of its children | its local TRS is identity, so the child's world matrix is unchanged; when the plan gives it a non-identity local, the child's own local is compensated (below) |
| COLLAPSE | a node re-parented onto its GRANDparent with the skipped node's local composed in | `L_kept' = L_kept * L_dropped`, so the world matrix is unchanged; the skipped node stays as a childless leaf |

**Architecture:** four moving parts, and only the first is new code of any size.

1. `src\Import\GlbSkel.cs` - the node graph, the 4x4 helpers, the plan model, `Validate`, `Apply`,
   `Verify`. Pure, no Unity, works on `GlbDocument`'s parsed JSON exactly as `GlbSlim` does, so it
   handles files `GlbReader` refuses.
2. **The container is NOT re-solved.** `ppskel.glb_read` / `glb_write` (`ppskel.py:93,109`) have a
   shipped, lossless counterpart: `GlbDocument.Load` / `.Write` (`src\Import\GlbDocument.cs:37,89`),
   which writes the ORIGINAL JSON chunk bytes verbatim while `!Dirty`. That is what makes the
   "empty plan is byte-identical" gate a one-liner instead of a JSON differ.
3. **BIN is never touched, and that is a hard invariant, not an accident.** Skinning in glTF is
   INDEX-based: `skin.joints[]` is parallel to `inverseBindMatrices`, and a vertex names a joint by
   its slot in that array. Nothing here deletes a node, reorders one, or removes one from
   `skin.joints` - inserted nodes are APPENDED past the end of `nodes[]` - so no index in the file
   ever changes meaning and `doc.Bin` comes out reference-identical. `ppskel.py:20-22` states the
   rule and `ppskel.check:249-256` asserts it; this port asserts it too, per byte.
4. The job and the UI are EXTENSIONS OF THE SLIM ONES, not siblings, exactly as the ZIP plan does
   it (`2026-09-02-glb-zip-plan.md:24-33`): `SlimJob` gets a `Skel` / `StartSkel` pair beside
   `Execute` / `Start`, reusing `SlimProgress`, `At`, `Publish` and the ONE copy of the `.ct_tmp` +
   `File.Replace` swap; `SlimPanel` gets a third mode.

---

## `ppskel.py` pass -> C# counterpart, one to one

| `ppskel.py` | C# | Note |
|---|---|---|
| `glb_read` `:93` / `glb_write` `:109` | `GlbDocument.Load` / `.Write` | already ships, lossless, **do not port** |
| `trs` `:122` | `GlbSkel.Trs(node)` -> `double[16]` | row-vector, translation in row 3, **plus** the `matrix` key ppskel silently ignores |
| `mul` `:132` | `GlbSkel.Mul(a, b)` | `M_world(n) = L(n) * M_world(parent)` |
| `decompose` `:136` | `GlbSkel.Decompose(m, out t, out r, out s)` | same four-branch quaternion extraction; refuses a negative determinant, which ppskel would silently mangle |
| `_paths` `:348` | `GlbSkel.Paths(nodes, parents)` | node index -> `'/'`-joined path |
| `resolver` `:220` | `GlbSkel.Resolve(nodes, root, path, out parent, out missing)` | walk by child NAME; returns the deepest resolved parent and the first missing part |
| `RENAME` `:44-78`, `_SIDE` `:51-60`, `INSERT_ABOVE` `:81-88`, `COLLAPSE` `:89`, `ANIM_ROOT` `:41`, `SRC`/`DST`/`MAP`/`PP_PREFAB` `:37-40` | **DROPPED** | the Tiffany map and every hard-coded path. Replaced by an explicit `SkelPlan` the author supplies. Codex §9: "do NOT generalise them into automatic guesses" |
| `pp_paths` `:156` | `PrototypeRecord.Bones` (`src\Doctor\PrototypeCatalog.cs:69`) + `PrototypeBone.Path` (`:11`) | the TARGET skeleton comes from the LIVE prototype census, never from a prefab JSON dump on disk |
| `pp_rest` `:181` / `rest_tsv` `:212` | **NO COUNTERPART** | a rest pose is `ppretarget`'s input, and `ppretarget` is DO-NOT-PORT (design §9 row 4). Nothing in this port reads a rest offset |
| `check` `:237` | split in two: `GlbSkel.Validate` (BEFORE, refusals) + `GlbSkel.Verify` (AFTER, the gate) | ppskel checks after the fact and `assert`s; inside `OnGUI` an assert tears the bench down |
| `convert` `:268` | `GlbSkel.Apply(doc, plan)` | the same four phases in the same order: rename `:281`, collapse `:285`, insert `:301`, create-missing `:316` |
| `createdEmptyPpNodes` `:316-328` (auto-creates EVERY unresolved PP path) | `SkelPlan.Create[]`, **explicit paths only** | the automatic sweep is the "automatic guess" §9 forbids |
| the MAP json dump `:331-339` | the plan file itself, plus the alias sidecar (see below) | |

### Two things `ppskel.py` gets away with and this port must not

- **A node with a `matrix` key.** `trs` `:122` reads `translation`/`rotation`/`scale` and never looks
  at `matrix`, which glTF allows instead. `convert` pops `matrix` off the KEPT node after writing TRS
  (`:295`) but composes from a matrix it never read - so a collapse under a matrix-form node writes a
  wrong local silently. `GlbSkel.Trs` reads `matrix` first when present.
- **A clip that animates a node whose local was rewritten.** A collapse changes `L_kept`, so a
  channel that writes `L_kept` every frame overwrites the composition on frame 1 and the geometry
  jumps. ppskel does not care - the whole point of the exercise is that the foreign file's own clips
  are thrown away and PP's clips drive it. This port cannot assume that, so `Validate` REFUSES a
  collapse (or a non-identity insert) whose affected node is an animation channel target, and names
  the clip.

### The animation channel pass, stated exactly

A glTF `channel.target.node` is a node INDEX. Renames change no index, inserts only append, collapses
delete nothing - so **no channel is ever remapped, and a pass that remapped them would be a bug.**
The port's animation work is therefore entirely the refusal above plus the invariant gate:
after `Apply`, every animation's channel count, every `target.node` and every `target.path` is
unchanged, byte for byte (`ppskel.check` has no equivalent assertion; this port adds it).

What DOES change for the file's own clips is what Unity makes of them on import: a generic clip binds
by path (`ClipFields.cs:34-41`), so after a rename the file's clips bind to the NEW paths - which is
the intended effect and is why `Verify` reports paths as well as names.

### Name decoration vs `SkinBinder.Plain`

A `.glb` ripped out of a live scene names its joints `#Root_Addon => PX_Heavy_Torso_BodyPartDef`
where the shipped asset says `Root` - the engine's own `Addon.MovedBoneNameFormat`
(`Addon.cs:143`, written at `:1250`), undecorated by `SkinBinder.Plain`
(`src\Import\GlbReader.cs:2499-2504`). The rule here has two halves and they are deliberately
asymmetric:

- **`Rename.From` is matched EXACT first, then through `SkinBinder.Plain`** - so a plan written
  against plain names still finds a decorated node, the same two-call pattern
  `SkinCompatibility.Match` uses (`src\Import\SkinCompatibility.cs:334-338`).
- **`Rename.To` is written VERBATIM and is never decorated**, and a `To` that is ITSELF decorated is
  refused. `Addon.cs:1217` compares literal `Transform.name`s, so writing a decorated name would
  produce a node that binds to nothing while looking right in every panel.

---

## The alias sidecar vs a rename plan - the decision

A rename plan and `<name>.glb.aliases.json` describe the same fact (file bone -> game bone) from two
sides, so they must not both be live for one file. The decision, and it is one-directional:

**SKEL BAKES THE RENAME INTO THE FILE, AND THE SIDECAR BECOMES UNNECESSARY FOR EVERY BONE IT
RENAMED.** The flow is `Doctor aliases -> skel plan -> baked .glb`, never the reverse.

Three grounded reasons:

1. **The sidecar is sha256-guarded** (`src\Import\AliasMap.cs:189-195`). Rewriting the `.glb` changes
   its hash, so an existing sidecar goes `SidecarProblem.Stale` and stops applying - silently as far
   as the bake is concerned, and with a "re-export it" warning in the Doctor that would now be
   pointing at the wrong cause. An in-place SKEL run must therefore delete the sidecar itself and say
   so, which is Task 6 step 3.
2. **The sidecar only reaches the REPLACEMENT read** (`AliasMap.cs:20-23`: `GlbSource.ReadReplacement`
   applies it; `ContentProject.ImportModel` ignores it on purpose, because published bone-path hashes
   must not depend on a file sitting next to the `.glb`). A file whose bones are actually renamed
   works on BOTH routes. That is the whole reason to bake.
3. **A sidecar cannot express an insert or a collapse at all.** `AliasMap.Apply`
   (`AliasMap.cs:67-87`) moves strings and states outright that parents, joints, weights and IBMs are
   untouched. Half of ppskel's job is hierarchy, which no alias map can carry.

The sidecar keeps exactly one job it does better: a source `.glb` the author must NOT rewrite (a
shared or vendored file). Nothing in this plan removes it, deprecates it, or changes its format.

---

**Tech Stack:** C# 9 / net472, Mono inside Unity 2019. No new dependencies. Build
`dotnet build -c Release`. Offline gates are `tests\ObjCodecTests` (NOT `dotnet test`), run with
`dotnet run --project tests\ObjCodecTests -c Release`; each gate is a
`static class X { internal static string Run() }` that throws on failure and is called from
`Program.Main`. `tests\ObjCodecTests\ObjCodecTests.csproj` sets `EnableDefaultCompileItems=false`, so
**every new file - test or linked src - must be added to its `<Compile Include>` list**;
`ContentTool.csproj` globs `src\**\*.cs` and needs no edit. Fixtures are reached by the existing
relative locator `GlbSlimTests.Fixture` (`tests\ObjCodecTests\GlbSlimTests.cs:390-392`).

**Fixtures - structure MEASURED 2026-09-02, not estimated:**

| Fixture | Measured shape | What it proves |
|---|---|---|
| `lib\u9_probe.glb` (2,888 B) | 5 nodes `rig, hip, head, body, prop`; **1 skin**, joints `[hip, head]`, `inverseBindMatrices` accessor 5, no `skeleton`; 4 clips; 3 scene roots (`rig`, `body`, `prop`) | the COLLAPSE case: `rig/hip/head` is exactly ppskel's `neck_01/neck_02` shape, and both joints are in the chain, so a collapse that moved a weight would be visible immediately |
| `lib\u8_probe.glb` (349,468 B) | 42 nodes, **skin joints 39**, `skeleton` node 2 (`Root`), IBM accessor 277, 5 clips, root `RootNode` -> `SpiderArmature` -> `Root` -> `Body` -> 39-bone tree | the INSERT case at scale, and the index-safety case: 39 joints and 277 accessors that must all come out identical |
| plan JSON | written by the tests into the scratchpad, never checked in | `SkelPlan.Parse` round trip |

---

## File Structure

**Created**

| Path | Responsibility |
|---|---|
| `src\Import\GlbSkel.cs` | node graph, 4x4 helpers, `SkelPlan`, `Validate`, `Apply`, `Verify`, `Stats`. |
| `tests\ObjCodecTests\GlbSkelTests.cs` | the ported `ppskel.check`, the refusal catalogue, world-matrix preservation, idempotence. |

**Modified**

| Path | Change |
|---|---|
| `src\Import\GlbSlim.cs:332,346,375-391` | widen `AccessorViews`, `ElementSize`, `Get`, `Obj`, `Arr`, `Str`, `Int`, `Long` from `private` to `internal`. **No-op if the ZIP plan already did it.** |
| `src\Import\SlimJob.cs:43,57,140` | `Stages` becomes a parameter of `At`; new `Skel` + `StartSkel` pair. **The `At` change is the same one the ZIP plan makes - apply once.** |
| `src\Dev\SlimPanel.cs:34-232` | a three-way mode (SLIM \| ZIP \| SKEL); `SkelOptions(width)` beside `Clips(width)`; `Run()` branches. |
| `src\Dev\ModelDoctor.cs:679` | one button, "Write skel plan", turning the live alias map into `<glb>.skel.json`. |
| `tests\ObjCodecTests\ObjCodecTests.csproj:150` | `<Compile Include>` for `..\..\src\Import\GlbSkel.cs` and `GlbSkelTests.cs`. |
| `tests\ObjCodecTests\Program.cs:140` | `Console.WriteLine(GlbSkelTests.Run());` after the `GlbSlimTests` line. |

---

### Task 1: the node graph, the 4x4 helpers and the plan model

Everything `ppskel.py` needs before it decides anything: who is whose parent, what a node's local
matrix is, and what a plan says. No file is rewritten in this task.

**Files:**
- Create: `src\Import\GlbSkel.cs`
- Modify: `src\Import\GlbSlim.cs:332,346,375-391`
- Create: `tests\ObjCodecTests\GlbSkelTests.cs`
- Modify: `tests\ObjCodecTests\ObjCodecTests.csproj:150`, `tests\ObjCodecTests\Program.cs:140`

- [ ] **Step 1: Write the gate stub.** Create `tests\ObjCodecTests\GlbSkelTests.cs` with
  `internal static class GlbSkelTests { internal static string Run() }` returning
  `"GLB-SKEL FAIL: not implemented"`, plus a private `Fixture(string)` copied verbatim from
  `GlbSlimTests.cs:390-392` and a private `Check(bool, string)` copied from `:394-398` with the
  prefix `GLB-SKEL FAIL: `. Register it:
  - In `tests\ObjCodecTests\ObjCodecTests.csproj`, after the `GlbSlimTests.cs` line (`:150`):
    ```xml
    <Compile Include="..\..\src\Import\GlbSkel.cs" Link="GlbSkel.cs" />
    <Compile Include="GlbSkelTests.cs" />
    ```
  - In `tests\ObjCodecTests\Program.cs`, after `Console.WriteLine(GlbSlimTests.Run());` (`:140`):
    ```csharp
    Console.WriteLine(GlbSkelTests.Run());
    ```
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: every existing gate PASSes, the new line reads `GLB-SKEL FAIL: not implemented`.

- [ ] **Step 2: Widen the `GlbSlim` readers.** In `src\Import\GlbSlim.cs`, change `private static` to
  `internal static` on `AccessorViews` (`:332`), `ElementSize` (`:346`), `Get` (`:375`), `Obj`
  (`:378`), `Arr` (`:380`), `Str` (`:382`), `Int` (`:385`) and `Long` (`:390`). Nothing else moves;
  the comment block at `:370-372` already states the contract these keep (a wrong type reads as
  absent, never as a throw) and `GlbSkel` inherits it. **If the ZIP plan has already landed this,
  verify and skip.**
  - Run: `dotnet build -c Release`
  - Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Implement the graph, the matrices and the plan.** Create `src\Import\GlbSkel.cs` in
  namespace `Morgott.ContentTool.Import`:
  ```csharp
  /// <summary>One rename. The whole reason this tool exists: Addon.GetEquivalentBones matches a
  /// bone with ownBoneName == bone.name (Addon.cs:1217) - ordinal, case-sensitive, literal - so the
  /// only way a foreign mesh binds is for its node to BE called what the rig calls it.</summary>
  internal sealed class SkelRename { internal string From, To; }

  /// <summary>A new node slipped between Parent and one of its children. PP carries roll bones
  /// INSIDE the chain (L.UpLeg/L.UpLeg_Roll_1/L.UpLeg_Roll_2/L.Leg) where a foreign rig has its
  /// twist bones as SIBLINGS, so the chain has to grow the missing links (ppskel.py:13-15).</summary>
  internal sealed class SkelInsert
  {
      internal string Parent;      // an existing node, named after any rename in the same plan
      internal string Name;        // the new node's name; must not already exist
      internal string Child;       // the existing child of Parent that moves under the new node
      /// <summary>Local TRS of the new node. Null = identity, which is the world-preserving case
      /// and the only one ppskel emits (ppskel.py:307). A non-identity local is honoured by
      /// COMPENSATING the child: L_child' = L_child * inverse(L_new), so Child's world matrix is
      /// still exactly what it was. Refused when Child is animated - see Validate.</summary>
      internal double[] Translation, Rotation, Scale;   // 3, 4 (xyzw), 3
  }

  /// <summary>Node is re-parented onto Into, which must be its GRANDparent, with the skipped
  /// parent's local composed in. ppskel.py:89 needs it once: the source rig has neck_01/neck_02
  /// where PP has a single Neck.</summary>
  internal sealed class SkelCollapse { internal string Node, Into; }

  /// <summary>An explicit path to create as an identity leaf under its deepest existing ancestor.
  /// ppskel.convert:316-328 sweeps EVERY unresolved PP path into one of these automatically; design
  /// §9 forbids that, so this list is written by hand and nothing is ever invented.</summary>
  internal sealed class SkelPlan
  {
      /// <summary>The node the Animator sits on in the converted model - ppskel's ANIM_ROOT
      /// (ppskel.py:41). PP paths start BELOW it, and the root itself is the empty path that root
      /// motion binds to (crc32("") - ClipFields.cs:38).</summary>
      internal string Root;
      internal List<SkelRename> Renames = new List<SkelRename>();
      internal List<SkelCollapse> Collapses = new List<SkelCollapse>();
      internal List<SkelInsert> Inserts = new List<SkelInsert>();
      internal List<string> Create = new List<string>();

      /// <summary>Read a plan file. Returns null and fills <paramref name="why"/> for anything a
      /// plan cannot be - not an object, an unknown schema, a step missing a name. Never throws:
      /// this is reached from OnGUI, where a throw tears the bench panel down mid-frame.</summary>
      internal static SkelPlan Parse(string json, out string why) { ... }

      /// <summary>The plan as JSON, through the writer GlbDocument already uses
      /// (GlbDocument.cs:91). Round-trips Parse exactly.</summary>
      internal string ToJson() { ... }
      internal const int Schema = 1;
  }

  internal static class GlbSkel
  {
      /// <summary>What one run did, for the sentence the panel shows.</summary>
      internal sealed class Stats { internal int Renamed, Inserted, Collapsed, Created; }

      /// <summary>The document's nodes array, or an empty list. Never null.</summary>
      internal static List<object> Nodes(GlbDocument doc) { ... }

      /// <summary>parent[i] = the index whose "children" holds i, or -1. Returns null and fills
      /// <paramref name="why"/> when a node has TWO parents - ppskel.check:257-261 asserts exactly
      /// this, because an insert that forgot to unlink the child produces it.</summary>
      internal static int[] Parents(List<object> nodes, out string why) { ... }

      /// <summary>Every node's '/'-joined path from its root (ppskel._paths:348). Index-parallel to
      /// nodes. A node with no name reads as "node&lt;i&gt;", exactly as ppskel spells it.</summary>
      internal static string[] Paths(List<object> nodes, int[] parents) { ... }

      /// <summary>Walk a '/'-joined path down from <paramref name="root"/> by child NAME
      /// (ppskel.resolver:220). Returns the node index, or -1 with <paramref name="deepest"/> set to
      /// the last node that DID resolve and <paramref name="missing"/> to the first part that did
      /// not - which is what Create needs to know where to hang a leaf.</summary>
      internal static int Resolve(List<object> nodes, int root, string path,
                                  out int deepest, out string missing) { ... }

      /// <summary>A node's local 4x4, ROW-VECTOR: translation occupies row 3 and world composes as
      /// M(n) = L(n) * M(parent). Reads "matrix" when the node carries one - the key ppskel.trs:122
      /// never looks at, which is why its collapse is silently wrong under a matrix-form node - and
      /// falls back to translation/rotation/scale with glTF's own defaults.</summary>
      internal static double[] Trs(Dictionary<string, object> node) { ... }

      /// <summary>Row-vector 4x4 product (ppskel.mul:132).</summary>
      internal static double[] Mul(double[] a, double[] b) { ... }

      /// <summary>Inverse of an affine row-vector 4x4. Null when the matrix is singular.</summary>
      internal static double[] Inverse(double[] m) { ... }

      /// <summary>Split a 4x4 back into TRS (ppskel.decompose:136), four-branch quaternion
      /// extraction included. Returns false - rather than producing a mirrored quaternion nothing
      /// can represent - when the upper 3x3 has a negative determinant.</summary>
      internal static bool Decompose(double[] m, out double[] t, out double[] r, out double[] s) { ... }
  }
  ```
  `Trs`, `Mul` and `Decompose` are `double`, not `float`: a collapse composes two matrices and
  decomposes the product, and the gate in Task 4 compares world matrices to 1e-9. Values are written
  back into the JSON as `double` because `Json.Parse` hands back `double` and `GlbDocument` writes
  what it is given.

- [ ] **Step 4: Port `ppskel`'s value-level facts as 6 checks.** In `GlbSkelTests.Run()`:
  1. `Parents` on `lib\u8_probe.glb`: 42 entries, exactly one -1 (`RootNode` at index 0), `why` null.
  2. `Parents` on `lib\u9_probe.glb`: 5 entries, THREE roots (`rig`, `body`, `prop`) - the scene lists
     `[0, 3, 4]`, so a port that assumed one root would be wrong on the very first fixture.
  3. `Paths` on `u9_probe`: `head` is `"rig/hip/head"`, `prop` is `"prop"`.
  4. `Resolve(nodes, rigIndex, "hip/head")` returns `head`'s index;
     `Resolve(nodes, rigIndex, "hip/neck")` returns -1 with `deepest == hip` and `missing == "neck"`.
  5. `Trs` of a node carrying `matrix` returns those 16 numbers verbatim; `Trs` of a node with no
     TRS keys returns identity; `Mul(Trs(a), Inverse(Trs(a)))` is identity within 1e-12.
  6. `Decompose(Mul(A, B))` round-trips: rebuild from the returned TRS and compare to `Mul(A, B)`
     within 1e-9, for a non-trivial A and B; `Decompose` of a matrix with scale `[-1,1,1]` returns
     false.
  - Gate prints: `GLB-SKEL PASS, 6 check(s)`.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `GLB-SKEL PASS, 6 check(s)` among all-green output.

- [ ] **Step 5: Build the mod DLL.**
  - Run: `dotnet build -c Release`
  - Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add -A && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(glb): GlbSkel node graph, row-vector 4x4 helpers and the explicit skeleton plan"`

---

### Task 2: `GlbSkel.Validate` - every refusal, before a single byte moves

`ppskel` asserts (`convert:277`, `:286`; `check:241-261`) and dies. This runs inside `OnGUI` and
inside a worker, so it returns SENTENCES and mutates nothing. Nothing in Task 3 re-checks: a plan
that reaches `Apply` has already been proven applicable.

**Files:**
- Modify: `src\Import\GlbSkel.cs` (add `Validate`)
- Modify: `tests\ObjCodecTests\GlbSkelTests.cs`

- [ ] **Step 1: Write the failing refusal gate.** Add 12 checks (total 18). Each builds a plan against
  a fixture and asserts `Validate` returns exactly one refusal whose text contains the quoted words,
  and that `doc.Dirty` is still false afterwards:
  7. `Root` names no node -> `"names no node"`. (ppskel.check:241-242 asserts exactly one ANIM_ROOT.)
  8. `Root` names two nodes -> `"names 2 nodes"`.
  9. `Rename.From` names no node -> `"has no bone called"`. (ppskel.convert:277-278.)
  10. `Rename.From` names two nodes -> `"has two bones called"`; the plan cannot say which.
  11. `Rename.To` is already a node name in the file -> `"already has a bone called"`.
  12. Two renames share a `To` -> `"two of the file's bones onto"` - the same collision
      `AliasMap.Of` refuses (`AliasMap.cs:52-53`), refused here for the same reason.
  13. `Rename.To` is decorated (`#X_Addon => Y`) -> `"the game's own decoration"`; `Addon.cs:1217`
      compares literal names, so it would bind to nothing.
  14. `Collapse.Node` has no parent (it is a scene root) -> `"is a root"`.
  15. `Collapse.Into` is not `Node`'s grandparent -> `"is not the grandparent of"`.
      (ppskel.convert:287 `assert parent[ki] == di`.)
  16. `Insert.Child` is not a child of `Insert.Parent` -> `"is not a child of"`.
  17. `Insert.Name` already exists -> `"already has a bone called"`.
  18. **The animation refusal:** a collapse whose `Node` is the target of an animation channel in
      `u9_probe.glb` (all four clips animate `hip`/`head`) -> `"is animated by"` plus the clip name.
      Same refusal for a non-identity `Insert` whose `Child` is animated.
  Plus one positive: a plan that renames `hip`->`Spine_1` and `head`->`Neck` on `u9_probe.glb`
  validates with ZERO refusals.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: the gate fails (`GLB-SKEL FAIL: ...`) - nothing is implemented yet.

- [ ] **Step 2: Implement `Validate`.** Add to `src\Import\GlbSkel.cs`:
  ```csharp
  /// <summary>
  /// EVERY reason this plan cannot be applied to this document, all of them at once. Empty list =
  /// go. Mutates nothing, so a caller can show the list and let the author fix the plan.
  ///
  /// Checked against the state each phase actually sees: renames first, so a Collapse or an Insert
  /// names PP's bones rather than the foreign ones - which is the order convert applies them in
  /// (ppskel.py:281, :285, :301) and the only order in which a plan is readable by a human.
  /// </summary>
  /// <param name="target">the prototype the plan is aiming at, for the name checks. Null skips
  /// them: a plan can be validated as a REWRITE without a target selected.</param>
  internal static IList<string> Validate(GlbDocument doc, SkelPlan plan, IList<string> targetBones) { ... }
  ```
  Order of work inside, and every arm is one of the 12 checks above:
  1. `Parents` - a two-parent document is refused before anything else (ppskel.check:257-261).
  2. `Root` resolves to exactly one node.
  3. Build `byName`: name -> list of node indices, ORDINAL. A name appearing twice is only a problem
     when a step NAMES it - the same rule `PrototypeTarget.BlockingAmbiguous`
     (`src\Doctor\PrototypeTarget.cs:58-68`) keeps for the rig side: an ambiguity nothing touches is
     not a refusal.
  4. Renames: `From` resolves to exactly one node (exact spelling, then `SkinBinder.Plain`); `To` is
     non-empty, undecorated, not already a name in the file (unless it is the `From` node itself),
     and unique across the plan.
  5. Collapses, against the post-rename name table: `Node` and `Into` each resolve to one node;
     `Node` has a parent and that parent has a parent; `parents[parents[node]] == into`;
     `Decompose` succeeds on `Mul(Trs(node), Trs(parent))`.
  6. Inserts, against the post-rename-post-collapse table: `Parent` and `Child` resolve; `Child`'s
     parent is `Parent`; `Name` is free; a non-null TRS is 3/4/3 long and `Inverse` is non-null.
  7. `Create` paths: the path's own parent resolves under `Root`, and the leaf does not already exist.
  8. **The animation arm, last:** collect every `animations[*].channels[*].target.node`, and refuse a
     collapse on such a node, or a non-identity insert whose `Child` is one. Name the clip - an
     author cannot act on "some clip".
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `GLB-SKEL PASS, 18 check(s)`.

- [ ] **Step 3: Build.**
  - Run: `dotnet build -c Release`
  - Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add -A && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(glb): GlbSkel.Validate refuses every plan ppskel would have asserted on"`

---

### Task 3: `GlbSkel.Apply` - the four passes, and the index invariant they must not break

`ppskel.convert:268-345` without the Tiffany map. Four phases, in ppskel's order.

**Files:**
- Modify: `src\Import\GlbSkel.cs` (add `Apply`)
- Modify: `tests\ObjCodecTests\GlbSkelTests.cs`

- [ ] **Step 1: Write the failing apply gate.** Add 10 checks (total 28):
  19. **The no-op round trip:** an EMPTY plan on `lib\u9_probe.glb` and on `lib\u8_probe.glb` leaves
      `doc.Dirty == false`, `Stats` all zero, and `doc.Write()` **byte-identical** to
      `File.ReadAllBytes(fixture)`. This is the one that keeps `GlbDocument`'s verbatim-JSON promise
      honest (`GlbDocument.cs:91-92`).
  20. **Rename touches nothing but names:** rename `hip`->`Spine_1`, `head`->`Neck` on `u9_probe`.
      `nodes[1].name == "Spine_1"`, `nodes[2].name == "Neck"`; and for EVERY node, `children`,
      `translation`, `rotation`, `scale`, `matrix`, `mesh`, `skin`, `camera` compare equal to the
      original by value; `skins`, `meshes`, `animations`, `accessors`, `bufferViews`, `buffers`,
      `samplers`, `materials`, `scenes` compare equal whole; and `doc.Bin` is byte-identical.
  21. **Rename is index-blind:** `animations[*].channels[*].target.node` and `target.path` are
      unchanged by the rename, and `skins[0].joints` is still `[1, 2]`.
  22. **Insert, identity:** insert `Spine_Roll_1` between `Root` and `Body` on `u8_probe`. Node count
      42 -> 43; the new node is at index 42 (APPENDED, ppskel.py:306); `Body` appears exactly once in
      exactly one `children` array; `skins[0].joints` is unchanged element for element;
      `skins[0].inverseBindMatrices` is still accessor 277; `skins[0].skeleton` is still 2.
  23. **Insert, the matrix check, Unity-free:** compute every joint's world matrix by walking parents
      with `Mul(Trs(node), parentWorld)` before and after check 22 - all 39 agree to 1e-9. Then repeat
      with a NON-identity insert (translation `[0.1, -0.2, 0.3]`, a 30-degree rotation about Y):
      the compensation `L_child' = L_child * Inverse(L_new)` must keep all 39 world matrices equal to
      1e-9 as well, while the new node's own local is exactly the one the plan gave.
  24. **Collapse:** on `u9_probe`, collapse `head` past `hip` onto `rig`. `head`'s world matrix is
      unchanged to 1e-9; `head`'s parent is now `rig`; `hip` has no `children` key at all
      (ppskel.py:290-291 deletes the key rather than leaving `[]`); `hip` is renamed `hip_unused`
      (ppskel.py:297); node count is still 5.
  25. **Collapse moves no weight, and does not need to:** after check 24, `skins[0].joints` is still
      `[1, 2]`, `inverseBindMatrices` is still accessor 5, and `doc.Bin` is byte-identical to the
      original. The dropped node keeps its own local under the same grandparent, so its world matrix
      - and therefore its bind pose - is untouched, and every vertex weighted to it still deforms
      exactly as before.
  26. **Create:** an explicit `Create` of `"rig/hip/Neck_Tip"` appends one node named `Neck_Tip`
      with no TRS keys, as a child of `hip`, and touches nothing else. A `Create` of a path whose
      parent does not resolve was already refused in Task 2.
  27. **Every animation invariant, over both fixtures, over a plan using all four phases:** the
      animation count, each clip's name, each clip's channel count, and every channel's
      `target.node` + `target.path` are unchanged. There is no channel remap and there must not be.
  28. **`SkelPlan` round trip:** write a plan with one of each step to a scratchpad `.json` with
      `ToJson()`, `Parse` it back, and compare field for field; a malformed file returns null with a
      non-empty `why` rather than throwing.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: the gate fails - `Apply` does not exist yet.

- [ ] **Step 2: Implement `Apply`.** Add to `src\Import\GlbSkel.cs`:
  ```csharp
  /// <summary>
  /// Apply a VALIDATED plan. Four phases in ppskel's own order (ppskel.py:281, :285, :301, :316),
  /// each one geometry-preserving, and between them they never delete a node, reorder one, or take
  /// one out of skin.joints. That is not tidiness - glTF skinning is INDEX-based (skin.joints[] is
  /// parallel to inverseBindMatrices and a vertex names a joint by its slot), so any of those three
  /// would silently re-bind every vertex in the file. Because none of them happens, doc.Bin comes
  /// out reference-identical and the inverse bind matrices need no recompute at all.
  ///
  /// Call Validate first. This method assumes what Validate proved and will throw
  /// InvalidOperationException rather than write a broken document if that assumption is false.
  /// </summary>
  internal static Stats Apply(GlbDocument doc, SkelPlan plan) { ... }
  ```
  Phase by phase, one to one with `convert`:
  1. **RENAME** (`ppskel.py:281-283`) - resolve every `From` against the ORIGINAL name table first,
     then assign, so a plan that swaps two names does not apply one half onto the other's result.
     That is the same simultaneity rule `AliasMap.Apply` keeps (`AliasMap.cs:60-63`). Rebuild the
     name table afterwards, as ppskel does at `:283`.
  2. **COLLAPSE** (`ppskel.py:285-297`) - unlink `Node` from its parent's `children`, delete the key
     when the list empties, append `Node` to the grandparent's `children`, set
     `L_Node = Mul(Trs(Node), Trs(parent))` via `Decompose` (removing any `matrix` key,
     `ppskel.py:295`), and rename the skipped parent to `<name>_unused` (`:297`).
  3. **INSERT** (`ppskel.py:301-313`) - unlink `Child`, append `{ "name": Name }` plus its TRS if the
     plan gave one, link it under `Parent`, link `Child` under it. When the TRS is non-identity, set
     `L_Child = Mul(Trs(Child), Inverse(L_new))` so `Child`'s world matrix is unchanged.
  4. **CREATE** (`ppskel.py:322-328`, but only the explicit paths) - append `{ "name": leaf }` under
     the resolved parent. Sorted by depth so a two-level create works in one pass, exactly as
     `ppskel.py:322` sorts by `p.count("/")`.
  Then: `doc.Dirty = true` **only if any phase did something** - an empty plan must leave the
  document writing its original JSON bytes verbatim, which is check 19.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `GLB-SKEL PASS, 28 check(s)`.

- [ ] **Step 3: Build.**
  - Run: `dotnet build -c Release`
  - Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add -A && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(glb): GlbSkel.Apply renames, collapses and inserts without moving a skin index"`

---

### Task 4: `GlbSkel.Verify` and the idempotence gate

`ppskel.check:237-264` as a return value, answering the two DIFFERENT questions the two binding
mechanisms ask, plus the property the brief calls idempotence.

**Files:**
- Modify: `src\Import\GlbSkel.cs` (add `Verify`)
- Modify: `tests\ObjCodecTests\GlbSkelTests.cs`

- [ ] **Step 1: Write the failing verify gate.** Add 8 checks (total 36):
  29. **BY NAME, the Doctor's question:** build a synthetic `PrototypeRecord` whose `BindableBones`
      are `["Spine_1", "Neck"]`. Against unmodified `u9_probe.glb`, `Verify` reports both as missing;
      after the Task 3 rename plan, `MissingNames` is empty.
  30. **BY PATH, the clip question:** with target paths `["Spine_1", "Spine_1/Neck"]`,
      `MissingPaths` is empty after the SAME plan and non-empty before it. The two lists are computed
      separately and a check that only ran one of them would pass on a rig where names collide across
      branches.
  31. **`EXT_` is not a path failure:** a target carrying `EXT_VoiceContext` that the file lacks
      produces no `MissingNames` entry, because `Addon.GetEquivalentBones` skips it (`Addon.cs:1209`)
      and `PrototypeCatalog.IsAttachmentPoint` (`src\Doctor\PrototypeCatalog.cs:91`) is the shipped
      predicate for it. It is reported in a third list, `AttachmentsAbsent`, as information.
  32. **The skin-untouched assertion, ported whole** (`ppskel.check:249-256`): after any plan, the
      skin count, every `joints` array, every `inverseBindMatrices` index and every `skeleton` index
      equal the source's, and every node's `mesh` and `skin` are unchanged.
  33. **The single-parent assertion** (`ppskel.check:257-261`): `Parents` returns a non-null array
      with `why == null` after every plan in this gate.
  34. **Idempotence, honestly:** apply the Task 3 rename plan, write, reload, and `Validate` the SAME
      plan again. It must be REFUSED with the "has no bone called 'hip'" refusal - the plan is a
      one-shot instruction, not a fixed point, and reporting it as a silent no-op would let a panel
      run it twice and claim success both times. The reloaded file is byte-identical to the first
      output.
  35. **A second, DIFFERENT plan composes:** rename `Spine_1`->`Root` on the output of check 34,
      write, reload - `Verify` against `["Root", "Neck"]` is clean, and `doc.Bin` is still
      byte-identical to the original fixture's BIN chunk after two full rewrites.
  36. **A plan that is fully applied is verifiable from the file alone:** load the written file with
      no plan and `Verify` against the target - the pass/fail does not depend on the plan object,
      which is what makes the in-game acceptance in Task 7 a real test.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: the gate fails - `Verify` does not exist yet.

- [ ] **Step 2: Implement `Verify`.** Add to `src\Import\GlbSkel.cs`:
  ```csharp
  /// <summary>What a converted file still does not answer for. THREE lists, because there are three
  /// different questions and merging them has cost this repo a wrong diagnosis before:
  ///
  ///  - MissingNames: prototype BindableBones with no node of that literal name anywhere under Root.
  ///    This is the Doctor's verdict question - Addon.cs:1217 matches names, not paths.
  ///  - MissingPaths: prototype bone PATHS that do not resolve by walking child names down from
  ///    Root. This is the CLIP question - ClipFields.cs:34-41, a generic binding is CRC-32 of a path.
  ///    A file can be perfect by name and useless by path, which is exactly the state ppskel exists
  ///    to leave behind (ppskel.check:244-246 checks only this one).
  ///  - AttachmentsAbsent: EXT_ names the file lacks. Never a defect - the game skips them.
  /// </summary>
  internal sealed class SkelVerdict
  {
      internal List<string> MissingNames = new List<string>();
      internal List<string> MissingPaths = new List<string>();
      internal List<string> AttachmentsAbsent = new List<string>();
      internal int NamesResolved, PathsResolved, Nodes, SkinJoints;
      internal bool Ok => MissingNames.Count == 0 && MissingPaths.Count == 0;
      /// <summary>ppskel's own closing line, in this repo's words.</summary>
      internal string Sentence() { ... }
  }

  /// <param name="targetBones">the prototype's Bones, name and relative path both -
  /// PrototypeBone (src\Doctor\PrototypeCatalog.cs:7-12). Paths may be null when only the name
  /// question is being asked.</param>
  internal static SkelVerdict Verify(GlbDocument doc, string rootName,
                                     IList<string> targetNames, IList<string> targetPaths) { ... }
  ```
  Name resolution uses `SkinBinder.Plain` on the FILE side only, the same asymmetry
  `SkinCompatibility` keeps (`src\Import\SkinCompatibility.cs:203-215`): a decorated node still
  answers for the plain bone it names, but the plan never writes a decorated name.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `GLB-SKEL PASS, 36 check(s)`.

- [ ] **Step 3: Build.**
  - Run: `dotnet build -c Release`
  - Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add -A && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "test(glb): GlbSkel.Verify answers the name question and the path question apart"`

---

### Task 5: `SlimJob.Skel` - stages, cancel, atomic save, read-back

Wrap the passes in the design §9 contract, on the ONE copy of the swap that already ships.

**Files:**
- Modify: `src\Import\SlimJob.cs:43,57,113,140`
- Modify: `tests\ObjCodecTests\GlbSkelTests.cs`

- [ ] **Step 1: Write the failing job gate.** Add 6 checks (total 42):
  37. `Skel` on `lib\u9_probe.glb` copied into a temp dir with the rename plan: the destination exists
      and `Verify`s clean; the SOURCE is byte-identical to what it was.
  38. `Skel` with a token cancelled before the call: `OperationCanceledException`, the destination
      does not exist, the source is untouched.
  39. After any run - completed, cancelled or refused - no `*.ct_tmp` is left in the directory.
  40. Progress fires at least 6 times with `Done <= Total` and the last snapshot's `Stage == "Done"`.
  41. `Skel` with a plan `Validate` refuses: `InvalidOperationException` carrying the refusals
      verbatim, one per line; nothing written.
  42. `Skel` on a plan that changes nothing (empty): returns a sentence containing
      `"changed nothing"` and the destination is **not** created - a rewrite that rewrites nothing is
      not a save, the same rule the ZIP job keeps for a file that would grow.
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: compile error / `GLB-SKEL FAIL` - not implemented.

- [ ] **Step 2: Implement `Skel` and `StartSkel`.** In `src\Import\SlimJob.cs`:
  - Change `At` (`:140`) to take the stage table:
    `private static void At(CancellationToken cancel, Action<SlimProgress> publish, string[] stages, int stage, string message)`,
    and update the five call sites in `Execute` (`:66,69,72,76,79`) to pass `Stages`.
    **If the ZIP plan already made this change, verify and skip.**
  - Add beside `Stages` (`:43`):
    ```csharp
    /// <summary>The skel run's six checkpoints. Verify is last and asks the file itself, with no
    /// plan in hand, whether the prototype's bones are now there - because that is the only form of
    /// the question the game will ask.</summary>
    private static readonly string[] SkelStages = { "Load", "Plan", "Validate", "Rewrite", "Write", "Verify" };
    ```
  - Add after `Execute` (`:106`):
    ```csharp
    /// <summary>
    /// The skeleton run: load, read the plan, validate, rewrite, save, verify. Same shape and same
    /// guarantees as Execute - pure, no thread affinity, and the destination is only ever touched by
    /// the swap of a finished .ct_tmp.
    /// </summary>
    /// <param name="planPath">the .skel.json to apply.</param>
    /// <param name="targetNames">the prototype's BindableBones, for the closing Verify. May be null,
    /// in which case the sentence says the rewrite happened and claims nothing about binding.</param>
    /// <exception cref="OperationCanceledException">Cancelled before the swap; nothing was written.</exception>
    /// <exception cref="InvalidOperationException">The plan would not parse, or Validate refused;
    /// its refusals are the message, one per line.</exception>
    internal static string Skel(string src, string dst, string planPath,
                               IList<string> targetNames, IList<string> targetPaths,
                               CancellationToken cancel, Action<SlimProgress> publish) { ... }

    /// <summary>Skel on the pool, exactly as Start runs Execute. Both callbacks land on the WORKER
    /// thread.</summary>
    internal static void StartSkel(string src, string dst, string planPath,
                                   IList<string> targetNames, IList<string> targetPaths,
                                   CancellationTokenSource cts, Action<SlimProgress> onProgress,
                                   Action<string> onComplete) { ... }
    ```
    `Skel` algorithm:
    1. Stage 0 -> `GlbDocument.Load(src)`.
    2. Stage 1 -> `SkelPlan.Parse(File.ReadAllText(planPath), out why)`; a null plan is an
       `InvalidOperationException` carrying `why`.
    3. Stage 2 -> `GlbSkel.Validate(doc, plan, targetNames)`; a non-empty list is an
       `InvalidOperationException` carrying `string.Join("\n", refusals)`.
    4. Stage 3 -> `GlbSkel.Apply(doc, plan)`. When `doc.Dirty` is still false, return
       `"the plan changed nothing, so nothing was written"` WITHOUT swapping.
    5. Stage 4 -> `doc.Write(tmp)`, then the swap exactly as `Execute` does (`SlimJob.cs:83-84`):
       `File.Replace` when `dst` exists, else `File.Move`.
    6. Stage 5 -> reload the WRITTEN file from disk and `GlbSkel.Verify` it, so the sentence is about
       the artifact and not about the in-memory document. A verify failure is REPORTED, never thrown
       after a successful swap.
    7. Sentence: `"renamed R, collapsed C, inserted I, created N; <verdict sentence>"`.
       `StartSkel` copies the caller's lists before queueing, the way `Start` copies the drop set
       (`SlimJob.cs:118`).
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `GLB-SKEL PASS, 42 check(s)`.

- [ ] **Step 3: Build.**
  - Run: `dotnet build -c Release`
  - Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add -A && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(glb): SlimJob.Skel with the same cancel and atomic swap, and a verify off the written file"`

---

### Task 6: the SKEL mode of `SlimPanel`, and the alias -> plan bridge

The two UI seams, both small, both inside files that already exist.

**Files:**
- Modify: `src\Dev\SlimPanel.cs:34-232`
- Modify: `src\Dev\ModelDoctor.cs:679`

- [ ] **Step 1: Make the panel's mode a three-way.** In `src\Dev\SlimPanel.cs`:
  - Replace the ZIP plan's `zipMode` bool (`SlimPanel.cs:46-47` area) with
    ```csharp
    /// <summary>Which run this panel is set up for. One panel rather than three because everything
    /// above the middle block - the browser, the intent queue, the progress trio, the writes line -
    /// is the same panel whichever run is chosen.</summary>
    private enum Mode { Slim, Zip, Skel }
    private Mode mode;
    private string planPath;
    ```
    **If the ZIP plan has not landed yet, define the enum with `Slim` and `Skel` only and leave the
    `Zip` arm to it.**
  - The mode row goes where the ZIP plan puts its toggle, after the Browse row (`:94`), as three
    `GUILayout.Toggle`s assigned through `intents` - never mid-layout (rule 1 of the class remark,
    `SlimPanel.cs:17-24`).
  - `Clips(width)` (`:96`) becomes a switch on `mode`. `SkelOptions()` draws:
    a second "Plan..." browse button that seeds `planPath` from `<source>.skel.json` when that file
    exists; a label naming the plan file and its step counts once parsed
    (`"12 rename, 1 collapse, 9 insert, 0 create"`), or `SkelPlan.Parse`'s `why` when it will not
    parse; and a label naming the prototype the Doctor currently has selected, or
    `"no prototype selected - the run will rewrite but claim nothing about binding"`.
    Both arms must emit the SAME NUMBER OF CONTROLS across the Layout and repaint passes of one
    frame, which they do because `mode` only ever changes in the intent drain.
  - The title label (`:86`) becomes mode-dependent:
    `"GLB SKEL - rename this model's bones onto the prototype's"`.
  - The `force` toggle (`:100`) is drawn in SLIM mode only; `inPlace` stays in all three.
  - The writes line (`:114`) gives `foo.skel.glb` in SKEL mode.
  - `Run()` (`:194`) branches to
    `SlimJob.StartSkel(sourcePath, inPlace ? sourcePath : Beside(sourcePath, "skel"), planPath, targetNames, targetPaths, cts, ...)`
    with the same two callbacks, seeding
    `progress = new SlimProgress("Queued", 0, 6, "waiting for a worker")`.
  - The RUN button's enable condition (`:102`) in SKEL mode is `sourcePath != null && planPath != null`,
    not `census.Length > 0`: a file with no clips is a perfectly good skeleton to rewrite.

- [ ] **Step 2: The alias -> plan bridge, one button.** In `src\Dev\ModelDoctor.cs`, beside
  "Save aliases" (`:679`), add `"Write skel plan"`, enabled under the same condition
  (`:670`) and enqueued through `edits` like every other press. It writes
  `<Path>.skel.json` containing one `SkelRename { From = fileBone, To = targetBone }` per live alias,
  with `Root` set to the file's single scene root when there is exactly one and left null otherwise,
  and no collapses, inserts or creates - **the Doctor knows which bones are misnamed and knows
  nothing about hierarchy, so it writes only what it knows.** The alias map is already bijective by
  construction (`ModelDoctor.Claimed`, `:985-990`), which is precisely `Validate`'s duplicate-target
  rule, so a plan written this way validates by construction.
  Result sentence: `"wrote N rename(s) to <path> - open Advanced > SKEL to apply it"`.

- [ ] **Step 3: An in-place SKEL run drops the stale sidecar.** In `SlimPanel.Run()`'s SKEL arm, when
  `inPlace` is true and `AliasMap.SidecarPathOf(sourcePath)` exists, delete it in the completion
  callback's intent and append `"; removed the now-stale <sidecar>"` to the result sentence. The
  reason is `AliasMap.cs:189-195`: the sidecar is sha256-guarded, so after an in-place rewrite it can
  never apply again - and every mapping it carried is now baked into the node names. A non-in-place
  run touches nothing: the source and its sidecar stay valid for the source.

- [ ] **Step 4: Build and re-run the offline gates** (the panel is not covered by them, but the
  linked src files are and this catches a signature drift).
  - Run: `dotnet build -c Release`
  - Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`
  - Run: `dotnet run --project tests\ObjCodecTests -c Release`
  - Expected: `GLB-SKEL PASS, 42 check(s)`, every other gate PASS.

- [ ] **Step 5: Commit.**
  - `git -C E:\DEV\PhoenixPoint\ContentTool add -A && git -C E:\DEV\PhoenixPoint\ContentTool commit -m "feat(glb): a SKEL mode on the slim panel and a skel plan written from the Doctor's aliases"`

---

### Task 7: In-game acceptance on `D:\PP-Instance3`

**`D:\PP-Instance2` belongs to another session - do not deploy to it, connect to it, or kill anything
in it.** Everything here runs against `D:\PP-Instance3` with
`-PPRoot 'D:\PP-Instance3' -ProfileId 76561197996210593`.

The acceptance the design asks for, end to end: a `.glb` whose bones are decorated or misnamed, a
rename plan built from the Doctor's own NEAREST-BONE diff against a chosen prototype, SKEL applied,
the file reloaded, and the verdict read **BY NAME**.

**Files:** no source changes. Record the run as an acceptance section appended to THIS file.

- [ ] **Step 1: Deploy.**
  - Run: `E:\DEV\PhoenixPoint\ContentTool\deploy.ps1 -PPRoot 'D:\PP-Instance3'`
  - Expected: it reports the DLL and `meta.json` written into that install's `Mods` folder.

- [ ] **Step 2: Get a geoscape and open the bench.** Wait until `connect state` actually ANSWERS
  before sending anything else - a still-initialising game hangs for minutes and looks exactly like
  an engine bug:
  ```powershell
  cd E:\DEV\PhoenixPoint\PPCLI
  .\ppcli.ps1 connect state -PPRoot 'D:\PP-Instance3' -ProfileId 76561197996210593
  .\ppcli.ps1 plan .\plans\start-campaign.json -PPRoot 'D:\PP-Instance3' -ProfileId 76561197996210593
  .\ppcli.ps1 connect console '{"command":"ct_bench","args":["open"]}' -PPRoot 'D:\PP-Instance3' -ProfileId 76561197996210593
  ```

- [ ] **Step 3: Produce the misnamed source, and get a NEAREST-BONE verdict for it.** Copy a rigged
  `.glb` into the scratchpad (so nothing in the repo is written) and pick a prototype in the Doctor.
  PPCLI cannot click IMGUI, so the panels are driven through their own fields and methods, the way
  the slim acceptance run did (`internal-docs\planning\2026-09-02-glb-slim-plan.md:441-460`):
  `AccessTools.Field(typeof(FitBench), "doctorTab" / "advanced" / "slim")` (`src\Dev\FitBench.cs:249,275,253`),
  then `AccessTools.Method(typeof(ModelDoctor), "PickFile")` (`src\Dev\ModelDoctor.cs:96`) and the
  prototype browser's own pick. Screenshot the verdict:
  `.\ppcli.ps1 connect screenshot -PPRoot 'D:\PP-Instance3' -ProfileId 76561197996210593`
  - Expected: the header reads **NEAREST-BONE**, and the bone map opens by itself
    (`ModelDoctor.cs:644-651` auto-opens it for a name mismatch) with a `<- closest` suggestion
    against each unmatched file bone.

- [ ] **Step 4: Accept the suggestions and write the plan.** Drive `SetAlias`
  (`src\Dev\ModelDoctor.cs:151`) once per row with the target bone the panel suggested, then press
  the new "Write skel plan" button through its intent. Screenshot.
  - Expected: `result:` reads `wrote N rename(s) to <scratchpad>\<file>.glb.skel.json`, and the file
    exists on disk with N `renames` entries.

- [ ] **Step 5: Apply SKEL and reload.** Set `SlimPanel`'s `mode` to `Skel` and its `planPath` to the
  file from step 4 through `AccessTools.Field(typeof(SlimPanel), "mode" / "planPath")`, then
  `AccessTools.Method(typeof(SlimPanel), "Pick" / "Run")`. Screenshot the result line, then feed the
  OUTPUT `.glb` back to the Doctor with `PickFile` against the SAME prototype and screenshot the
  verdict.
  - Expected: the run's sentence names the rename count and a clean verify; the sibling
    `<file>.skel.glb` exists; the source is byte-identical to what it was; no `.ct_tmp` remains; and
    the reloaded file's verdict is **BY NAME** with **no alias sidecar involved** - which is the
    whole claim of this port, and the reason the sidecar-vs-rename decision above is the way it is.

- [ ] **Step 6: The negative run, which is the point and not a failure.** `Run` the same plan a second
  time against the already-converted file and screenshot.
  - Expected: `result:` carries `Validate`'s refusal - `"has no bone called '<old name>'"` - and
    nothing is written. That is check 34 reproduced in game: the plan is a one-shot instruction, and
    a panel that silently succeeded twice would be lying about one of them.

- [ ] **Step 7: Owner handoff.** Append an acceptance table to this file in the shape the slim plan
  uses (`2026-09-02-glb-slim-plan.md:441-471`): install, build stamps, fixture, one row per action
  with expected / observed / verdict, screenshot paths, and any observation that is not a defect.
  Present the screenshots to the owner for the visual check that closes the slice. A PPCLI defect met
  along the way is APPENDED to `E:\DEV\PhoenixPoint\PPCLI\ISSUES.md` and worked around - never fixed
  from this session.

---

## Deliberate refusals, each with its reason

- **Nothing is deleted, ever.** A collapse leaves the skipped node as a childless leaf named
  `<name>_unused` (`ppskel.py:297`) instead of removing it. Removing it would renumber `nodes[]`,
  and therefore `skin.joints`, `skin.skeleton`, `scenes[].nodes`, every `children` array and every
  `channel.target.node`; and when the removed node is itself a joint, it would additionally
  re-index every vertex's `JOINTS_n` attribute - which lives in BIN, which this whole port does not
  touch (`ppskel.py:27-28`). The leaf costs one node and no bytes of geometry. **This is a divergence
  from the brief's "collapse removes the node": it is refused, and check 25 asserts the leaf and the
  byte-identical BIN instead.** Upgrade path, should a file ever need it: a node-index remap through
  `GlbSlim.Remap` (`GlbSlim.cs:227`) for the non-joint case only.
- **No weights are re-parented on collapse**, for the same reason and one more: the dropped node keeps
  its own local under the same grandparent, so its WORLD matrix is unchanged and its bind pose stays
  correct. Every vertex weighted to it deforms exactly as it did. There is nothing to move.
- **No inverse bind matrix is recomputed.** An identity insert preserves every descendant's world
  matrix by construction; a non-identity insert is compensated on the child so it does too; a collapse
  composes so it does too. IBMs are inverses of world bind matrices, so an unchanged world matrix is
  an unchanged IBM. Check 23 proves it numerically rather than by argument.
- **No automatic name guessing anywhere in `GlbSkel`.** The only suggestion machinery in this repo is
  `ModelDoctor.Suggest` (`src\Dev\ModelDoctor.cs:996-1011`), it is drawn with a `?`, and it applies to
  nothing until the author clicks. `GlbSkel` receives names, never invents them.
- **No rest-pose work, no re-binding, no unit rescaling.** That is `ppskel.py:360-370`'s own NEXT
  LEVER and it is `ppretarget`, which design §9 marks DO NOT PORT. A model converted by SKEL will
  BIND; whether it LOOKS right is the separate problem those lines describe.
