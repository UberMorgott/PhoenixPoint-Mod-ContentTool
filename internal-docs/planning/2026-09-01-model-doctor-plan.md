# Model Doctor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the Model Doctor described in `internal-docs\planning\2026-09-01-model-doctor-design.md` (spec v3): pick a `.glb`, pick the `SkinnedMeshRenderer` it should replace, and see within seconds which of four outcomes the bake would produce — `ByName` / `NearestBone` / `NotRigged` / `Refused` — with every reason phrased as a Blender action, with bone-name aliases applied live and persisted to a sidecar so the real bake produces the same outcome.

**Architecture:** One pure decision function, `ReplacementDecision.Decide`, is the single definition of the verdict and is called by BOTH the Doctor's preflight and `BundleBaker.ReplaceMesh`; the binding checks that feed it are extracted out of `SkinBinder.Bind` into `SkinCompatibility.Analyze`, which returns severity-free ordered `BindingIssue`s while `Bind` keeps throwing the first one byte-for-byte as it does today. Everything from bytes to verdict (`GlbSource.ReadReplacement` → `ModelBuild.From` → `Analyze` → `Decide`) is free of UnityEngine types and runs on a worker thread and in the offline test EXE; only mesh construction, IMGUI and the renderer swap touch Unity, on the main thread, behind a generation counter and a target fingerprint.

**Tech Stack:** C# 9 / net472, Mono inside Unity 2019 (Phoenix Point). No new dependencies — JSON in via the existing `Morgott.ContentTool.Import.Json.Parse` (`src\Import\GlbReader.cs:2304`), JSON out hand-written, hashing via `System.Security.Cryptography.SHA256`. UI is IMGUI (`UnityEngine.IMGUIModule`, already referenced). Build: `dotnet build -c Release`. Offline tests: the console EXE `tests\ObjCodecTests` (NOT `dotnet test`), run with `dotnet run --project tests\ObjCodecTests -c Release`; every gate is a `static class X { internal static string Run() }` that throws on failure and is called from `Program.Main`. `tests\ObjCodecTests\ObjCodecTests.csproj` sets `EnableDefaultCompileItems=false`, so **every new file — test or linked src — must be added to its `<Compile Include>` list**; `ContentTool.csproj` globs `src\**\*.cs` and needs no edit.

---

## File Structure

**Created**

| Path | Responsibility |
|---|---|
| `src\Import\SkinCompatibility.cs` | `BindCode`, `BindStage`, `BindSide`, `BindingIssue`, `RigTarget`, `SkinCompatibility.Analyze` — every check `SkinBinder.Bind` performs, as an ordered severity-free list. |
| `src\Import\ReplacementDecision.cs` | `Outcome` enum + the pure `Decide(...)` — the one definition of the verdict, shared by the Doctor and the bake. |
| `src\Import\ImportRefused.cs` | `ImportCode` enum + `ImportRefusedException : FormatException` carrying a stable code. |
| `src\Import\AliasMap.cs` | Immutable file-bone → game-bone map; simultaneous `Apply`; sidecar read/write and its policy. |
| `src\Import\GlbSource.cs` | `ReplacementSource` envelope + `ReadReplacement(bytes, path)` — the single replacement-path read, with alias provenance. |
| `src\Doctor\Diagnostic.cs` | `Severity`, `Diagnostic`, `DiagnosticReport` — plain UI-facing data, UnityEngine-free. |
| `src\Doctor\ReplacementPreflight.cs` | `ReplacementPreflightResult` + `Run(bytes, path, target)` — bytes to verdict, pure, catches everything. |
| `src\Dev\GlbFileBrowser.cs` | IMGUI file browser (drives / up / into / `.glb` filter / 5 recents persisted to disk). |
| `src\Dev\ModelDoctor.cs` | Doctor session: state machine, intent queue, worker job, fingerprint, preview/revert/save, IMGUI panel. |
| `tests\ObjCodecTests\BinderFrozen.cs` | Frozen record of today's `SkinBinder.Bind` behaviour — captured BEFORE any refactor. |
| `tests\ObjCodecTests\DecisionGolden.cs` | Truth table for `ReplacementDecision.Decide` against `BundleBaker.ReplaceMesh`'s branches. |
| `tests\ObjCodecTests\AliasTests.cs` | Alias semantics + sidecar policy. |
| `tests\ObjCodecTests\PreflightTests.cs` | End-to-end `ReplacementPreflight.Run` over the committed `lib\u9_probe.glb`. |

**Modified**

| Path | Change |
|---|---|
| `src\Import\GlbReader.cs:2450-2549` | `SkinBinder.Bind` delegates its checks to `SkinCompatibility.Analyze`; messages and throw order unchanged. |
| `src\Import\GlbReader.cs:2294` / `:2271` | `Bad(message, code = ImportCode.MalformedGlb)` and `Unreadable` → `ImportRefusedException`; six call sites gain an explicit code. |
| `src\Import\ModelBuild.cs:151-155` | The two refusals become `ImportRefusedException` (`NoVertices`, `NoNormals`). |
| `src\Bake\BundleBaker.cs:142-205` | `ReplaceMesh` asks `ReplacementDecision.Decide`; `catch (Exception)` narrowed to `catch (FormatException)`; `how` names applied aliases. |
| `src\Dev\LiveMesh.cs:35-93` | `Load` routes through `GlbSource.ReadReplacement` and logs alias provenance; new public `Build(SkinnedModel, string)` seam. |
| `src\Dev\LiveMesh.cs:114-151` | `Bind` gains `out BindMode mode`; existing 3-arg call site keeps working via an overload. |
| `src\Project\ContentProject.cs:88-97` / `:611-619` | `ImportedMesh` carries `SidecarPath`/`AliasesApplied`; `ImportMesh` reads through `GlbSource.ReadReplacement`. |
| `src\Dev\FitBench.cs:1307-1354` | A `MODEL DOCTOR` tab inside `Draw()`, plus an `Advanced` toggle that hides the numeric readouts. |
| `tests\ObjCodecTests\ObjCodecTests.csproj` | New `<Compile Include>` entries for the four test files and the six linked src files. |
| `tests\ObjCodecTests\Program.cs:105-148` | New gates called from `Main`. |

---

### Task 1: Freeze today's `SkinBinder.Bind` behaviour

The record must predate the refactor, or it proves nothing. No `src\` file changes in this task.

**Files:**
- Create: `tests\ObjCodecTests\BinderFrozen.cs`
- Modify: `tests\ObjCodecTests\ObjCodecTests.csproj`, `tests\ObjCodecTests\Program.cs`
- Test: itself

- [ ] **Step 1: Write the frozen-fixture gate.** Create `tests\ObjCodecTests\BinderFrozen.cs`:

```csharp
using System;
using System.Collections.Generic;
using Morgott.ContentTool.Import;

/// <summary>
/// THE RECORD THAT PREDATES THE REFACTOR. SkinBinder.Bind is about to hand its checks to
/// SkinCompatibility.Analyze (Model Doctor, task 2). Every sentence below was captured from the
/// UNREFACTORED binder, so a delegation that changes which refusal an author reads - or its
/// wording - fails here rather than in a bake log three weeks later.
///
/// The replacement path always calls Bind(file, names, 0, null, ...) - LiveMesh.cs:217 and
/// SkinFields.cs:748 - so that is what every case here uses.
/// </summary>
internal static class BinderFrozen
{
    /// <summary>name -> (file joint names, target bone names, the substring the refusal must carry,
    /// or null when it must BIND).</summary>
    private static readonly List<string[]> Cases = new List<string[]>
    {
        new[] { "binds",              "Root|Neck",  "Root|Neck", null },
        new[] { "binds reversed",     "Neck|Root",  "Root|Neck", null },
        new[] { "no target bones",    "Root",       "",          "the target model lists no bones" },
        new[] { "no armature",        "",           "Root",      "the file carries no armature" },
        new[] { "target bone empty",  "Root|Neck",  "Root|",     "has no name" },
        new[] { "target bone twice",  "Root|Neck",  "Root|Root", "the target model has two bones named 'Root'" },
        new[] { "file bone twice",    "Root|Root",  "Root|Neck", "the file has two bones named 'Root'" },
        new[] { "missing bone",       "Root|Hand",  "Root|Neck", "does not contain the bone 'Neck'" },
        new[] { "extra bone",         "Root|Neck",  "Root",      "the file adds the bone 'Neck'" },
    };

    internal static string Run()
    {
        int checks = 0;
        foreach (string[] c in Cases) checks += One(c[0], Split(c[1]), Split(c[2]), c[3]);

        // The two decorated cases, which are the ones a live rig actually produces.
        checks += One("decorated binds", new[] { "#Root_Addon => D", "#Neck_Addon => D" },
                      new[] { "Root", "Neck" }, null);
        checks += One("decoration collides", new[] { "#Root_Addon => A", "#Root_Addon => B" },
                      new[] { "Root", "Neck" }, "both name the bone 'Root'");

        // The two that need a MALFORMED model rather than a name list.
        SkinnedModel ibm = Model(new[] { "Root" });
        ibm.InverseBindMatrices = new float[0][];
        checks += Refuses("bind pose count", ibm, new[] { "Root" }, "bind poses for");

        SkinnedModel slot = Model(new[] { "Root" });
        slot.Joints = new ushort[] { 7, 0, 0, 0 };
        checks += Refuses("bone index out of range", slot, new[] { "Root" }, "references bone 7");

        SkinnedModel cover = Model(new[] { "Root" });
        cover.Weights = new float[2];
        checks += Refuses("weights do not cover", cover, new[] { "Root" },
                          "bone weights do not cover every vertex");

        // Submeshes(file, 0) is NOT a no-op: it bounds-checks every triangle index, and it runs
        // BEFORE the bone checks. A file that is wrong in both ways must still say THIS first.
        SkinnedModel tri = Model(new[] { "Hand" });
        tri.Submeshes.Clear();
        tri.Submeshes.Add(new[] { 0, 0, 99 });
        checks += Refuses("triangle bound wins over a bone name", tri, new[] { "Root" },
                          "a triangle points at vertex 99");

        // Shapes(file, null) refuses ANY blend shape on the replacement path, also before the
        // bone checks - so a shape-keyed .glb falls back to nearest-bone in the bake.
        SkinnedModel morph = Model(new[] { "Hand" });
        morph.Morphs.Add(new SkinMorph { Name = "smile" });
        checks += Refuses("blend shape wins over a bone name", morph, new[] { "Root" },
                          "the file has 1 blend shapes but this model has 0");

        return "BINDER-FROZEN PASS, " + checks + " check(s) - the pre-refactor record of SkinBinder.Bind";
    }

    private static string[] Split(string joined)
    {
        return joined.Length == 0 ? new string[0] : joined.Split('|');
    }

    private static int One(string what, string[] jointNames, string[] boneNames, string cause)
    {
        SkinnedModel file = jointNames.Length == 0 ? Empty() : Model(jointNames);
        if (cause != null) return Refuses(what, file, boneNames, cause);

        ushort[] joints;
        float[][] bindposes;
        SkinBinder.Bind(file, boneNames, 0, null, out joints, out bindposes);
        return Check(joints.Length == file.Positions.Length * 4 && bindposes.Length == boneNames.Length,
                     what + " - it bound, but produced " + joints.Length + " joint slot(s) and " +
                     bindposes.Length + " bind pose(s)");
    }

    private static int Refuses(string what, SkinnedModel file, string[] boneNames, string cause)
    {
        try
        {
            ushort[] joints;
            float[][] bindposes;
            SkinBinder.Bind(file, boneNames, 0, null, out joints, out bindposes);
        }
        catch (FormatException e)
        {
            return Check(e.Message.IndexOf(cause, StringComparison.Ordinal) >= 0,
                         what + " - refused, but not with '" + cause + "': " + e.Message);
        }
        throw new Exception("BINDER-FROZEN FAILURE: " + what + " - it bound instead of refusing");
    }

    /// <summary>One vertex per joint, one full-weight influence each, one distinguishable bind pose
    /// per joint - the same fixture shape BoneNames.cs uses.</summary>
    private static SkinnedModel Model(string[] jointNames)
    {
        int n = jointNames.Length;
        var m = new SkinnedModel
        {
            Positions = new ObjVector3[n],
            Joints = new ushort[n * 4],
            Weights = new float[n * 4],
            InverseBindMatrices = new float[n][]
        };
        for (int j = 0; j < n; j++)
        {
            m.Positions[j] = new ObjVector3(j, 0f, 0f);
            m.Joints[j * 4] = (ushort)j;
            m.Weights[j * 4] = 1f;
            m.JointNames.Add(jointNames[j]);
            m.InverseBindMatrices[j] = new[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, j + 1f, 0f, 0f, 1f };
        }
        m.Submeshes.Add(new[] { 0, 0, 0 });
        return m;
    }

    private static SkinnedModel Empty()
    {
        var m = new SkinnedModel
        {
            Positions = new[] { new ObjVector3(0f, 0f, 0f) },
            Joints = new ushort[4],
            Weights = new float[4],
            InverseBindMatrices = new float[0][]
        };
        m.Submeshes.Add(new[] { 0, 0, 0 });
        return m;
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("BINDER-FROZEN FAILURE: " + what);
        return 1;
    }
}
```

- [ ] **Step 2: Register the gate.** In `tests\ObjCodecTests\ObjCodecTests.csproj`, after the line `<Compile Include="BoneNames.cs" />`, add:

```xml
    <Compile Include="BinderFrozen.cs" />
```

In `tests\ObjCodecTests\Program.cs`, after the line `Console.WriteLine(BoneNames.Run());` (`:116`), add:

```csharp
        Console.WriteLine(BinderFrozen.Run());
```

- [ ] **Step 3: Run it and expect PASS against the UNREFACTORED binder.** `dotnet run --project tests\ObjCodecTests -c Release`. Expected: a line `BINDER-FROZEN PASS, 15 check(s) - the pre-refactor record of SkinBinder.Bind` and exit 0. This gate is written to pass on today's code — that is the point; it is the oracle for tasks 2-3. If any case fails now, the fixture is wrong, not the binder: fix the fixture until it records what the binder actually does.

- [ ] **Step 4: Commit.**

```
git add tests/ObjCodecTests/BinderFrozen.cs tests/ObjCodecTests/ObjCodecTests.csproj tests/ObjCodecTests/Program.cs
git commit -m "test(import): freeze SkinBinder.Bind behaviour before the Doctor refactor"
```

---

### Task 2: Extract `SkinCompatibility.Analyze`; `Bind` delegates

**Files:**
- Create: `src\Import\SkinCompatibility.cs`
- Modify: `src\Import\GlbReader.cs:2450-2549` (`SkinBinder.Bind`), `tests\ObjCodecTests\ObjCodecTests.csproj`
- Test: `tests\ObjCodecTests\BinderFrozen.cs` (task 1, unchanged — it is the oracle)

- [ ] **Step 1: Add a failing check to the frozen gate.** In `tests\ObjCodecTests\BinderFrozen.cs`, immediately before the `return "BINDER-FROZEN PASS…"` line, add:

```csharp
        // ---- the extraction itself: Analyze must list EVERY reason, in Bind's own throw order,
        // where Bind stops at the first. One file that is wrong three ways over.
        SkinnedModel many = Model(new[] { "Root", "Hand" });
        IList<BindingIssue> issues = SkinCompatibility.Analyze(many, new[] { "Root", "Neck" });
        checks += Check(issues.Count == 2, "Analyze lists every reason, not just the first: " + issues.Count);
        checks += Check(issues[0].Code == BindCode.MissingBone && issues[0].Subject == "Neck",
                        "the missing live bone is reported FIRST and by name: " + issues[0].Code +
                        " '" + issues[0].Subject + "'");
        checks += Check(issues[1].Code == BindCode.ExtraBone && issues[1].Subject == "Hand",
                        "the added file bone comes second: " + issues[1].Code + " '" + issues[1].Subject + "'");
        checks += Check(issues[0].Message.IndexOf("does not contain the bone 'Neck'", StringComparison.Ordinal) >= 0,
                        "an issue carries the BINDER's own sentence, not a new one: " + issues[0].Message);
        checks += Check(SkinCompatibility.Analyze(Model(new[] { "Root" }), new[] { "Root" }).Count == 0,
                        "a file that binds produces no issue at all");
```

and add `using System.Collections.Generic;` to the file's usings (already present).

- [ ] **Step 2: Run it and watch it fail to compile.** `dotnet run --project tests\ObjCodecTests -c Release`. Expected: `error CS0103: The name 'SkinCompatibility' does not exist in the current context` (and `BindingIssue`, `BindCode`). That is the failing test.

- [ ] **Step 3: Write `SkinCompatibility`.** Create `src\Import\SkinCompatibility.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Morgott.ContentTool.Import
{
    /// <summary>Which of Bind's three phases an issue belongs to. Bind runs Submeshes/Shapes BETWEEN
    /// the skin guards and the bone checks (GlbReader.cs:2465-2466), and Submeshes(file, 0) is not a
    /// no-op - it bounds-checks every triangle index - so the phases must be kept apart or the
    /// extraction silently changes which sentence an author reads.</summary>
    internal enum BindStage { Skin, Mesh, Bones }

    /// <summary>Whose asset the issue is about. The Doctor draws Target rows separately, because
    /// "this is the game's model, not your file" is the difference between a fix and a dead end.</summary>
    internal enum BindSide { File, Target }

    /// <summary>Stable identity of one binding disagreement. The catalogue is spec v3 §7.</summary>
    internal enum BindCode
    {
        TargetBonesUnavailable, NoArmature, JointsWeightsMismatch,
        TriangleOutOfRange, BlendShapeCount,
        TargetBoneEmpty, TargetBoneDuplicate, DuplicateFileBone, PlainCollision,
        MissingBone, ExtraBone, NotBijective, InverseBindCount, BoneIndexOutOfRange
    }

    /// <summary>
    /// ONE reason a file and a rig do not correspond, WITHOUT a severity. Severity is a UI decision -
    /// the Doctor calls these Downgrade because the bake imports anyway and merely loses the author's
    /// weights, while SkinBinder.Bind treats the very first one as fatal. A severity carried here
    /// would have to be both at once.
    /// </summary>
    internal sealed class BindingIssue
    {
        internal BindCode Code;
        internal BindStage Stage;
        internal BindSide Side;
        /// <summary>The binder's OWN sentence, verbatim - never a new wording.</summary>
        internal string Message;
        /// <summary>The bone the row is about, or null. This is what the bone-map table keys on.</summary>
        internal string Subject;
    }

    /// <summary>
    /// A plain snapshot of the live target, taken on the main thread so the worker never touches a
    /// UnityEngine object. The last five fields are the FINGERPRINT: a SkinnedMeshRenderer keeps its
    /// instance id while its mesh, its bind poses and its bones are replaced under it.
    /// </summary>
    internal sealed class RigTarget
    {
        /// <summary>smr.bones[b].name, in the live rig's order. NULL when the renderer lists no bones
        /// (LiveMesh.cs:116-117) - which is the nearest-bone branch, not an error.</summary>
        internal string[] BoneNames;
        /// <summary>From BIND POSES, the same fact SkinFields.Rigged keys on (SkinFields.cs:623-626).</summary>
        internal bool Rigged;
        internal int RendererInstanceId;
        internal int MeshInstanceId;
        internal int BindPoseCount;
        internal string TransformPath = "";
        internal string MeshName = "";

        internal bool SameAs(RigTarget other)
        {
            if (other == null) return false;
            if (RendererInstanceId != other.RendererInstanceId || MeshInstanceId != other.MeshInstanceId ||
                BindPoseCount != other.BindPoseCount || Rigged != other.Rigged ||
                !string.Equals(TransformPath, other.TransformPath, StringComparison.Ordinal) ||
                !string.Equals(MeshName, other.MeshName, StringComparison.Ordinal)) return false;
            if (BoneNames == null || other.BoneNames == null) return BoneNames == other.BoneNames;
            if (BoneNames.Length != other.BoneNames.Length) return false;
            for (int i = 0; i < BoneNames.Length; i++)
                if (!string.Equals(BoneNames[i], other.BoneNames[i], StringComparison.Ordinal)) return false;
            return true;
        }
    }

    /// <summary>
    /// Every check <see cref="SkinBinder.Bind"/> performs, in Bind's own order, as a LIST instead of a
    /// first-failure throw. Bind still throws the first one and its sentences are unchanged, so the
    /// bake cannot drift from what the Doctor predicts; the Doctor gets all of them at once, which is
    /// the whole feature (an author fixes three bone names in one pass, not one per game launch).
    ///
    /// The replacement path is what this describes: Bind(file, boneNames, 0, null, ...) -
    /// LiveMesh.cs:217, SkinFields.cs:748. Material-slot and blend-shape-name checks against a
    /// non-empty list stay in Bind, because no replacement caller passes one.
    /// </summary>
    internal static class SkinCompatibility
    {
        internal static IList<BindingIssue> Analyze(SkinnedModel file, IList<string> boneNames)
        {
            int[] liveOf, fileOf;
            return Analyze(file, boneNames, out liveOf, out fileOf);
        }

        /// <param name="liveOf">file joint -&gt; live bone index, or null when the file cannot be bound.</param>
        /// <param name="fileOf">live bone -&gt; file joint index, or null when the file cannot be bound.</param>
        internal static IList<BindingIssue> Analyze(SkinnedModel file, IList<string> boneNames,
                                                    out int[] liveOf, out int[] fileOf)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));
            liveOf = null;
            fileOf = null;
            var issues = new List<BindingIssue>();

            // ---- Stage.Skin: GlbReader.cs:2454-2463, before Submeshes/Shapes.
            if (boneNames == null || boneNames.Count == 0)
                Add(issues, BindCode.TargetBonesUnavailable, BindStage.Skin, BindSide.Target, null,
                    "the target model lists no bones, so there is no skeleton to bind onto; " +
                    "reload the scene and try again");
            if (file.JointNames.Count == 0)
                Add(issues, BindCode.NoArmature, BindStage.Skin, BindSide.File, null,
                    "the file carries no armature, so it cannot replace a rigged model; " +
                    "in Blender export the mesh together with its armature, or put the file on a static object instead");
            if (file.Joints == null || file.Weights == null || file.Positions == null ||
                file.Joints.Length != (file.Positions == null ? -1 : file.Positions.Length) * 4 ||
                file.Weights.Length != file.Joints.Length)
                Add(issues, BindCode.JointsWeightsMismatch, BindStage.Skin, BindSide.File, null,
                    "the file's bone weights do not cover every vertex; " +
                    "in Blender give the whole mesh an Armature modifier with vertex groups and re-export");
            if (issues.Count > 0) return issues;

            // ---- Stage.Mesh: what Submeshes(file, 0) and Shapes(file, null) refuse.
            int vertices = file.Positions == null ? 0 : file.Positions.Length;
            foreach (int[] triangles in file.Submeshes)
            {
                foreach (int index in triangles)
                    if (index < 0 || index >= vertices)
                    {
                        Add(issues, BindCode.TriangleOutOfRange, BindStage.Mesh, BindSide.File, null,
                            "a triangle points at vertex " + index.ToString(CultureInfo.InvariantCulture) +
                            " of " + vertices.ToString(CultureInfo.InvariantCulture) +
                            "; the file is corrupt, so re-export it");
                        break;
                    }
                if (issues.Count > 0) break;
            }
            if (file.Morphs.Count != 0)
                Add(issues, BindCode.BlendShapeCount, BindStage.Mesh, BindSide.File, null,
                    "the file has " + file.Morphs.Count.ToString(CultureInfo.InvariantCulture) +
                    " blend shapes but this model has 0, and the game drives them by position; " +
                    "in Blender keep every shape key that came with the model, in the same order, and re-export");
            if (issues.Count > 0) return issues;

            // ---- Stage.Bones: GlbReader.cs:2468-2544.
            var live = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < boneNames.Count; i++)
            {
                if (string.IsNullOrEmpty(boneNames[i]))
                    Add(issues, BindCode.TargetBoneEmpty, BindStage.Bones, BindSide.Target, null,
                        "the target model's bone " + i.ToString(CultureInfo.InvariantCulture) +
                        " has no name, so nothing in the file can be matched to it; reload the scene and try again");
                else if (live.ContainsKey(boneNames[i]))
                    Add(issues, BindCode.TargetBoneDuplicate, BindStage.Bones, BindSide.Target, boneNames[i],
                        "the target model has two bones named '" + boneNames[i] +
                        "', so a bone in the file cannot be matched to one of them; this model cannot be replaced by name");
                else live[boneNames[i]] = i;
            }
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int j = 0; j < file.JointNames.Count; j++)
            {
                if (seen.ContainsKey(file.JointNames[j]))
                    Add(issues, BindCode.DuplicateFileBone, BindStage.Bones, BindSide.File, file.JointNames[j],
                        "the file has two bones named '" + file.JointNames[j] +
                        "'; rename one of them in Blender so every bone name is unique, then re-export");
                else seen[file.JointNames[j]] = j;
            }
            var plain = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int j = 0; j < file.JointNames.Count; j++)
            {
                string bare = SkinBinder.Plain(file.JointNames[j]);
                if (bare == file.JointNames[j] || seen.ContainsKey(bare)) continue;
                if (plain.ContainsKey(bare))
                    Add(issues, BindCode.PlainCollision, BindStage.Bones, BindSide.File, bare,
                        "the file's bones '" + file.JointNames[plain[bare]] + "' and '" +
                        file.JointNames[j] + "' both name the bone '" + bare + "' once the game's own " +
                        "'#<bone>_Addon => <part>' decoration is removed, so neither can be matched to it; " +
                        "keep the one that belongs to this model and re-export");
                else plain[bare] = j;
            }
            if (issues.Count > 0) return issues;

            int[] toLive = new int[file.JointNames.Count];
            int[] toFile = new int[boneNames.Count];
            for (int i = 0; i < toLive.Length; i++) toLive[i] = -1;
            for (int i = 0; i < toFile.Length; i++) toFile[i] = -1;

            for (int i = 0; i < boneNames.Count; i++)
            {
                int j;
                if (!seen.TryGetValue(boneNames[i], out j) && !plain.TryGetValue(boneNames[i], out j))
                {
                    Add(issues, BindCode.MissingBone, BindStage.Bones, BindSide.File, boneNames[i],
                        "the file does not contain the bone '" + boneNames[i] +
                        "', which this model's skeleton has; the skeleton is never replaced, so in Blender keep the imported " +
                        "armature exactly as it came, with every bone and its name unchanged, and re-export");
                    continue;
                }
                toFile[i] = j;
                toLive[j] = i;
            }
            for (int j = 0; j < file.JointNames.Count; j++)
                if (toLive[j] < 0)
                    Add(issues, BindCode.ExtraBone, BindStage.Bones, BindSide.File, file.JointNames[j],
                        "the file adds the bone '" + file.JointNames[j] +
                        "', which this model's skeleton does not have; the skeleton is never replaced, so delete the added bone " +
                        "in Blender and re-export");
            // The bijection, the bind-pose count and the vertex bone indices are only ASKABLE once
            // every bone has a partner - toFile still holds -1 otherwise, and indexing with it is
            // the crash the binder avoids by throwing at the first failure.
            if (issues.Count > 0) return issues;

            for (int i = 0; i < toFile.Length; i++)
                if (toLive[toFile[i]] != i)
                {
                    Add(issues, BindCode.NotBijective, BindStage.Bones, BindSide.File, boneNames[i],
                        "the file's bones could not be matched one to one onto this model's skeleton; " +
                        "re-export from the model this mod dumped, without adding, removing or renaming bones");
                    break;
                }
            if (file.InverseBindMatrices == null || file.InverseBindMatrices.Length != file.JointNames.Count)
                Add(issues, BindCode.InverseBindCount, BindStage.Bones, BindSide.File, null,
                    "the file has " +
                    (file.InverseBindMatrices == null ? 0 : file.InverseBindMatrices.Length).ToString(CultureInfo.InvariantCulture) +
                    " bind poses for " + file.JointNames.Count.ToString(CultureInfo.InvariantCulture) +
                    " bones; re-export from Blender rather than editing the file by hand");
            if (issues.Count > 0) return issues;

            for (int i = 0; i < file.Joints.Length; i++)
            {
                int slot = file.Joints[i];
                if (slot < toLive.Length) continue;
                Add(issues, BindCode.BoneIndexOutOfRange, BindStage.Bones, BindSide.File, null,
                    "vertex " + (i / 4).ToString(CultureInfo.InvariantCulture) + " references bone " +
                    slot.ToString(CultureInfo.InvariantCulture) + " but the file has " +
                    toLive.Length.ToString(CultureInfo.InvariantCulture) + "; the file is corrupt, so re-export it");
                break;
            }
            if (issues.Count > 0) return issues;

            liveOf = toLive;
            fileOf = toFile;
            return issues;
        }

        /// <summary>The first issue of a stage, or null. Bind throws these one stage at a time.</summary>
        internal static BindingIssue First(IList<BindingIssue> issues, BindStage stage)
        {
            for (int i = 0; i < issues.Count; i++) if (issues[i].Stage == stage) return issues[i];
            return null;
        }

        private static void Add(List<BindingIssue> into, BindCode code, BindStage stage, BindSide side,
                                string subject, string message)
        {
            into.Add(new BindingIssue { Code = code, Stage = stage, Side = side, Subject = subject, Message = message });
        }
    }
}
```

- [ ] **Step 4: Make `Bind` delegate.** In `src\Import\GlbReader.cs`, replace the whole body of `SkinBinder.Bind` from line 2453 (`if (file == null) …`) down to and including line 2536 (the closing brace of the `InverseBindMatrices` throw) with:

```csharp
            if (file == null) throw new ArgumentNullException(nameof(file));

            // Every check now lives in SkinCompatibility.Analyze so the Doctor can LIST what this
            // throws at. The order is preserved exactly: the skin guards, then Submeshes/Shapes
            // (which the replacement path calls with 0/null but a MATERIAL replacement does not),
            // then the bone checks.
            int[] liveOf, fileOf;
            IList<BindingIssue> issues = SkinCompatibility.Analyze(file, boneNames, out liveOf, out fileOf);
            BindingIssue first = SkinCompatibility.First(issues, BindStage.Skin);
            if (first != null) throw new FormatException(first.Message);

            Submeshes(file, materialSlots);
            Shapes(file, blendShapeNames);

            if (issues.Count > 0) throw new FormatException(issues[0].Message);
```

Leave lines 2537-2548 (the `joints`/`bindposes` construction) exactly as they are — they read `liveOf`/`fileOf`, which the call above now supplies. Delete the now-unreachable per-vertex bound check inside that loop (`if (slot >= liveOf.Length) throw …`, `:2541-2544`) and replace the loop body with `joints[i] = (ushort)liveOf[file.Joints[i]];`, because `Analyze` has already refused every out-of-range slot with the same sentence.

Add `using System.Collections.Generic;` to `src\Import\GlbReader.cs` if it is not already among its usings (it is, `:2` region — verify before editing).

- [ ] **Step 5: Link the new file into the offline test project.** In `tests\ObjCodecTests\ObjCodecTests.csproj`, immediately before `<Compile Include="..\..\src\Import\GlbReader.cs" Link="GlbReader.cs" />`, add:

```xml
    <!-- The binding CHECKS, lifted out of SkinBinder.Bind so the Doctor can list them instead of
         reading one thrown sentence. UnityEngine-free like the binder it came from. -->
    <Compile Include="..\..\src\Import\SkinCompatibility.cs" Link="SkinCompatibility.cs" />
```

- [ ] **Step 6: Run and expect PASS.** `dotnet run --project tests\ObjCodecTests -c Release`. Expected: `BINDER-FROZEN PASS, 20 check(s) - the pre-refactor record of SkinBinder.Bind`, plus the pre-existing `BONE-NAMES PASS, 6 check(s)` line unchanged (that gate asserts the same sentences through `Bind` and is the second oracle), and exit 0.

- [ ] **Step 7: Build the mod to prove the delegation compiles inside the real assembly.** `dotnet build -c Release`. Expected: `Build succeeded`, 0 errors.

- [ ] **Step 8: Commit.**

```
git add src/Import/SkinCompatibility.cs src/Import/GlbReader.cs tests/ObjCodecTests/BinderFrozen.cs tests/ObjCodecTests/ObjCodecTests.csproj
git commit -m "refactor(import): list binding issues via SkinCompatibility.Analyze, Bind still throws the first"
```

---

### Task 3: `ReplacementDecision.Decide`, and the bake asks it

**Files:**
- Create: `src\Import\ReplacementDecision.cs`, `tests\ObjCodecTests\DecisionGolden.cs`
- Modify: `src\Bake\BundleBaker.cs:142-205`, `tests\ObjCodecTests\ObjCodecTests.csproj`, `tests\ObjCodecTests\Program.cs`
- Test: `tests\ObjCodecTests\DecisionGolden.cs`

- [ ] **Step 1: Write the truth-table gate.** Create `tests\ObjCodecTests\DecisionGolden.cs`:

```csharp
using System;
using Morgott.ContentTool.Import;

/// <summary>
/// THE VERDICT, ONCE. BundleBaker.ReplaceMesh (src\Bake\BundleBaker.cs:153-202) chooses between four
/// endings, and the Model Doctor has to predict the same one. The prediction is not a copy of those
/// branches - both call ReplacementDecision.Decide - and this table is the record of what those
/// branches DO, quoted line by line, so a change to the bake that forgets the Doctor fails here.
///
/// The branches, in the bake's own order:
///   :153-156  model==null || JointNames.Count==0, and SkinFields.Rigged(mesh)  -> refusal, writes NOTHING
///   :176-177  names = null when the source carries no armature
///   :180-184  Rebind returns false on a mesh with no bind poses                 -> "not rigged"
///   :180-183  Rebind returns true                                              -> nearest-bone
///   :190-195  RebindByName returned                                            -> BY NAME
///   :197-201  RebindByName threw                                               -> nearest-bone
/// </summary>
internal static class DecisionGolden
{
    internal static string Run()
    {
        var issue = new BindingIssue { Code = BindCode.MissingBone, Message = "x" };
        int checks = 0;

        // A skinless source onto a RIGGED target is the one case that writes nothing at all.
        checks += Is(Outcome.Refused, false, true, false, null, "skinless onto rigged is REFUSED (:153-156)");
        checks += Is(Outcome.Refused, false, true, true, null, "and stays refused however the target names its bones");

        // A skinless source onto an unrigged target: the guard is skipped, Rebind finds no bind poses.
        checks += Is(Outcome.NotRigged, false, false, false, null, "skinless onto unrigged is NOT RIGGED (:184)");

        // A rigged source onto an unrigged target: same sentence, same reason.
        checks += Is(Outcome.NotRigged, true, false, false, null, "rigged source onto unrigged target is NOT RIGGED");
        checks += Is(Outcome.NotRigged, true, false, true, null, "even when the bundle does name bones");

        // A rigged source, a rigged target, but nothing in the bundle names the target's bones.
        checks += Is(Outcome.NearestBone, true, true, false, null, "no bone names available is NEAREST-BONE (:178-183)");
        checks += Is(Outcome.NearestBone, true, true, false, issue, "and an issue cannot make that worse");

        // The two that decide whether the author's weights survive.
        checks += Is(Outcome.ByName, true, true, true, null, "a clean binding is BY NAME (:190-195)");
        checks += Is(Outcome.NearestBone, true, true, true, issue, "one issue is enough to fall back (:197-201)");

        return "DECISION PASS, " + checks + " check(s) - one Decide, four outcomes, the bake's own branches";
    }

    private static int Is(Outcome want, bool armature, bool rigged, bool names, BindingIssue first, string what)
    {
        Outcome got = ReplacementDecision.Decide(armature, rigged, names, first);
        if (got != want) throw new Exception("DECISION FAILURE: " + what + " - wanted " + want + ", got " + got);
        return 1;
    }
}
```

- [ ] **Step 2: Run it and watch it fail to compile.** `dotnet run --project tests\ObjCodecTests -c Release`. Expected: `error CS0103: The name 'ReplacementDecision' does not exist in the current context`.

- [ ] **Step 3: Write `ReplacementDecision`.** Create `src\Import\ReplacementDecision.cs`:

```csharp
namespace Morgott.ContentTool.Import
{
    /// <summary>What a mesh replacement WILL do, in the bake's own words. Four, not three: an
    /// unrigged target is a different sentence from a lost skin, and a skinless source onto a rigged
    /// target is refused outright rather than downgraded.</summary>
    internal enum Outcome
    {
        /// <summary>The file's own weights land on the target's own bones, matched by name.</summary>
        ByName,
        /// <summary>The file imports, but every vertex is welded whole to its nearest bone.</summary>
        NearestBone,
        /// <summary>The target carries no bind poses, so there is nothing to skin to.</summary>
        NotRigged,
        /// <summary>Nothing is written at all.</summary>
        Refused
    }

    /// <summary>
    /// THE ONE DEFINITION OF THE VERDICT. BundleBaker.ReplaceMesh asks it, and so does the Model
    /// Doctor's preflight - not "the same conditions in the same order", the same function, because
    /// two implementations of one rule drift and the author is the one who finds out.
    ///
    /// Pure: no UnityEngine type, no AssetTypeValueField, so the whole table is provable offline
    /// (tests\ObjCodecTests\DecisionGolden.cs).
    /// </summary>
    internal static class ReplacementDecision
    {
        /// <param name="sourceHasArmature">model != null &amp;&amp; model.JointNames.Count &gt; 0 (BundleBaker.cs:153/176).</param>
        /// <param name="targetRigged">the target has bind poses: SkinFields.Rigged (BundleBaker.cs:154) live-side, smr.sharedMesh.bindposes.Length &gt; 0.</param>
        /// <param name="targetBoneNamesAvailable">SkinFields.BoneNames(...) != null (BundleBaker.cs:177) live-side, smr.bones is non-empty.</param>
        /// <param name="firstIssue">
        /// the first thing SkinCompatibility.Analyze found, or null. The Doctor has this up front; the
        /// bake only learns it when RebindByName throws, and re-asks with it from the catch. It is the
        /// ONE input this function cannot compute for itself.
        /// </param>
        internal static Outcome Decide(bool sourceHasArmature, bool targetRigged,
                                       bool targetBoneNamesAvailable, BindingIssue firstIssue)
        {
            if (!sourceHasArmature) return targetRigged ? Outcome.Refused : Outcome.NotRigged;
            if (!targetRigged) return Outcome.NotRigged;
            if (!targetBoneNamesAvailable) return Outcome.NearestBone;
            return firstIssue == null ? Outcome.ByName : Outcome.NearestBone;
        }
    }
}
```

- [ ] **Step 4: Register the gate.** In `tests\ObjCodecTests\ObjCodecTests.csproj`, after the `SkinCompatibility.cs` line added in task 2, add:

```xml
    <Compile Include="..\..\src\Import\ReplacementDecision.cs" Link="ReplacementDecision.cs" />
```

and after `<Compile Include="BinderFrozen.cs" />` add:

```xml
    <Compile Include="DecisionGolden.cs" />
```

In `tests\ObjCodecTests\Program.cs`, after `Console.WriteLine(BinderFrozen.Run());`, add:

```csharp
        Console.WriteLine(DecisionGolden.Run());
```

- [ ] **Step 5: Run and expect PASS.** `dotnet run --project tests\ObjCodecTests -c Release`. Expected: `DECISION PASS, 9 check(s) - one Decide, four outcomes, the bake's own branches`, exit 0.

- [ ] **Step 6: Make the bake ask it, and narrow the catch.** In `src\Bake\BundleBaker.cs`, replace lines 149-156 (the refusal guard) with:

```csharp
            // A SKINLESS SOURCE CANNOT SKIN A RIGGED TARGET, and saying so beats welding it. Checked
            // before MeshFields.Fill and long before SetNewData, so a refusal writes nothing at all and
            // the player keeps the model they had. WHICH ending this is comes from
            // ReplacementDecision.Decide, the same function the Model Doctor predicts with.
            bool armature = model != null && model.JointNames.Count > 0;
            bool rigged = SkinFields.Rigged(mesh);
            refusal = ReplacementDecision.Decide(armature, rigged, true, null) == Outcome.Refused
                ? SkinFields.Skinless(assetName) : null;
            if (refusal != null) return null;
```

and replace lines 175-202 (from `string how;` to the closing brace of the `else` block) with:

```csharp
            string how;
            string[] names = armature ? SkinFields.BoneNames(man, afileInst, info.PathId) : null;
            Outcome outcome = ReplacementDecision.Decide(armature, rigged, names != null, null);
            if (outcome != Outcome.ByName)
            {
                how = SkinFields.Rebind(mesh, baked, influences)
                    ? "nearest-bone, one full-weight influence per vertex (" +
                      (!armature ? "the source carries no armature"
                                 : "no SkinnedMeshRenderer in this bundle names the target's bones") + ")"
                    : "not rigged - the target carries no bind poses";
            }
            else
            {
                // RebindByName throws before writing anything, so a refusal costs the mesh nothing and
                // the fallback below binds the very same geometry the strict path was handed.
                //
                // FormatException, not Exception: that is the ONLY way the binding path refuses -
                // SkinBinder.Bind throws it at every site (GlbReader.cs:2455-2544) and RebindByName's
                // own width check does too (SkinFields.cs:739). A NullReference or an index error out
                // of that code is a BUG, and quietly downgrading a bug to nearest-bone is how one
                // ships.
                try
                {
                    SkinFields.RebindByName(mesh, baked, model, names, influences);
                    how = "BY NAME onto the target's own " + names.Length +
                          " bones, carrying " + Math.Max(influences, 1) +
                          " of the file's own influences per vertex";
                }
                catch (FormatException ex)
                {
                    SkinFields.Rebind(mesh, baked, influences);
                    how = "nearest-bone - the file's own weights were NOT used: " + ex.Message;
                }
            }
```

Add `using Morgott.ContentTool.Import;` to `src\Bake\BundleBaker.cs` if the file does not already have it (check its using block first — `SkinnedModel` is already a parameter type, so it may be fully qualified instead; in that case qualify the new names as `Import.ReplacementDecision` / `Import.Outcome` rather than adding a using that shadows something).

- [ ] **Step 7: Build and re-run.** `dotnet build -c Release` — expected `Build succeeded`, 0 errors. Then `dotnet run --project tests\ObjCodecTests -c Release` — expected exit 0 with `DECISION PASS` and every pre-existing gate (`SKIN`, `MODEL`, `BONE-NAMES`, `REFUSAL-COUNT`) still passing.

- [ ] **Step 8: Commit.**

```
git add src/Import/ReplacementDecision.cs src/Bake/BundleBaker.cs tests/ObjCodecTests/DecisionGolden.cs tests/ObjCodecTests/ObjCodecTests.csproj tests/ObjCodecTests/Program.cs
git commit -m "refactor(bake): one ReplacementDecision for the verdict, and only a FormatException falls back"
```

---

### Task 4: `ImportRefusedException` — a stable code on every refusal

**Files:**
- Create: `src\Import\ImportRefused.cs`
- Modify: `src\Import\GlbReader.cs:2271-2294` and six throw sites, `src\Import\ModelBuild.cs:151-155`, `tests\ObjCodecTests\ObjCodecTests.csproj`, `tests\ObjCodecTests\BinderFrozen.cs`
- Test: `tests\ObjCodecTests\BinderFrozen.cs` (extended)

- [ ] **Step 1: Add the failing checks.** In `tests\ObjCodecTests\BinderFrozen.cs`, immediately before the `return "BINDER-FROZEN PASS…"` line, add:

```csharp
        // ---- every refusal now carries a CODE, and the ones nobody catalogued still carry one.
        checks += Code(ImportCode.MalformedGlb, new byte[] { 1, 2, 3 }, "a stub is malformed");
        byte[] notGlb = new byte[16];
        checks += Code(ImportCode.MalformedGlb, notGlb, "the wrong magic is malformed");
        checks += Check(new ImportRefusedException(ImportCode.NoNormals, "x") is FormatException,
                        "a refusal is still a FormatException, so every existing catch keeps working");
```

and add this helper next to `Check`:

```csharp
    private static int Code(ImportCode want, byte[] bytes, string what)
    {
        try { GlbReader.Read(bytes); }
        catch (ImportRefusedException e)
        {
            return Check(e.Code == want, what + " - got code " + e.Code + ": " + e.Message);
        }
        throw new Exception("BINDER-FROZEN FAILURE: " + what + " - it did not refuse at all");
    }
```

- [ ] **Step 2: Run and watch it fail to compile.** `dotnet run --project tests\ObjCodecTests -c Release`. Expected: `error CS0246: The type or namespace name 'ImportRefusedException' could not be found`.

- [ ] **Step 3: Write the exception.** Create `src\Import\ImportRefused.cs`:

```csharp
using System;

namespace Morgott.ContentTool.Import
{
    /// <summary>
    /// Stable identity of an import refusal. The eight named ones are the cases the Model Doctor
    /// gives an author a Blender action for; everything else the reader refuses - roughly eighty-five
    /// further Bad(...) exits, each with its own good sentence - is MalformedGlb, which is honest:
    /// the message already names the cause, and the code only decides how the UI groups the row.
    /// </summary>
    internal enum ImportCode
    {
        MalformedGlb, UnsupportedGlb,
        Oversize, ExternalBuffer, NoMesh, NonTriangle, NotIndexed, TooManyInfluences,
        NoVertices, NoNormals
    }

    /// <summary>
    /// What GlbReader and ModelBuild already threw, plus the code. Deriving from FormatException is
    /// deliberate and load-bearing: LiveMesh.cs:52, BundleBaker.cs:197 and
    /// tests\ObjCodecTests\BoneNames.cs:100 all catch FormatException today, and adding a code must
    /// not quietly change which of them stops catching.
    /// </summary>
    internal sealed class ImportRefusedException : FormatException
    {
        internal ImportRefusedException(ImportCode code, string message) : base(message) { Code = code; }

        internal ImportCode Code { get; }
    }
}
```

- [ ] **Step 4: Route every refusal through it.** In `src\Import\GlbReader.cs`:

Replace line 2294 with:

```csharp
        private static FormatException Bad(string message)
        {
            return new ImportRefusedException(ImportCode.MalformedGlb, message);
        }

        /// <summary>The catalogued refusals - the ones the Model Doctor turns into a Blender action.</summary>
        private static FormatException Bad(ImportCode code, string message)
        {
            return new ImportRefusedException(code, message);
        }
```

In `Unreadable` (`:2271-2292`), change all three `return Bad(` to `return Bad(ImportCode.UnsupportedGlb, ` — including the Draco branch, which is unreachable today (both call sites, `:231` and `:607`, exclude `Draco.Extension`) but must not be the one place a code is missing if it ever becomes reachable again.

Then add the code as the first argument at exactly these six sites, changing nothing else about them:

| Line | Change |
|---|---|
| `:156` | `throw Bad(ImportCode.Oversize, "the file is " + …` |
| `:240` | `throw Bad(ImportCode.ExternalBuffer, "the file's geometry lives in a separate file '" + …` |
| `:249` | `throw Bad(ImportCode.NoMesh, "the file contains no mesh; …` |
| `:612` | `throw Bad(ImportCode.NonTriangle, what + " is not made of triangles …` |
| `:617` | `throw Bad(ImportCode.TooManyInfluences, what + " weights some vertices to more than four bones, …` |
| `:671` | `throw Bad(ImportCode.NotIndexed, what + " has no index buffer, …` |

In `src\Import\ModelBuild.cs`, replace the two throws at `:151-155` with:

```csharp
            if (model.Positions == null || model.Positions.Length == 0)
                throw new ImportRefusedException(ImportCode.NoVertices,
                    name + ".glb carries no vertices; export a mesh with geometry");
            if (model.Normals == null || model.Normals.Length != model.Positions.Length)
                throw new ImportRefusedException(ImportCode.NoNormals,
                    name + ".glb carries no per-vertex normals; in Blender's " +
                    "glTF export panel leave Geometry > Normals on and re-export");
```

`ModelBuild.cs` keeps its `using System.IO;` for the rest of the file; nothing else catches `InvalidDataException` from `From` (grep: `SourceImport.Each` catches `Exception`, `LiveMesh.Load` catches `Exception`), so widening the type is safe.

- [ ] **Step 5: Link and run.** In `tests\ObjCodecTests\ObjCodecTests.csproj`, before the `SkinCompatibility.cs` line, add:

```xml
    <Compile Include="..\..\src\Import\ImportRefused.cs" Link="ImportRefused.cs" />
```

`dotnet run --project tests\ObjCodecTests -c Release`. Expected: `BINDER-FROZEN PASS, 23 check(s)` and exit 0, with `MODEL round trip`, `Compressed`, `DRACO` and every other importer gate unchanged — they catch `FormatException`, which `ImportRefusedException` still is.

- [ ] **Step 6: Build.** `dotnet build -c Release` — expected `Build succeeded`, 0 errors.

- [ ] **Step 7: Commit.**

```
git add src/Import/ImportRefused.cs src/Import/GlbReader.cs src/Import/ModelBuild.cs tests/ObjCodecTests/BinderFrozen.cs tests/ObjCodecTests/ObjCodecTests.csproj
git commit -m "feat(import): every refusal carries a stable ImportCode"
```

---

### Task 5: `AliasMap` and its sidecar

**Files:**
- Create: `src\Import\AliasMap.cs`, `tests\ObjCodecTests\AliasTests.cs`
- Modify: `tests\ObjCodecTests\ObjCodecTests.csproj`, `tests\ObjCodecTests\Program.cs`
- Test: `tests\ObjCodecTests\AliasTests.cs`

- [ ] **Step 1: Write the gate.** Create `tests\ObjCodecTests\AliasTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Import;

/// <summary>
/// THE ONE FIX THAT IS HONEST TO DO IN-GAME: renaming a bone IN THE FILE so it names the bone the
/// game's skeleton already has. The rename is SIMULTANEOUS - every new name is read off the ORIGINAL
/// names - or a map that swaps two bones would apply one half and then the other onto its own result.
/// Nothing else moves: the joint order, the weights, the inverse bind matrices and the node parents
/// are the file's own, byte for byte, which is what the last check measures.
/// </summary>
internal static class AliasTests
{
    internal static string Run()
    {
        int checks = 0;

        // ---- a swap, which is the case a sequential rename gets wrong.
        SkinnedModel m = Model("A", "B");
        byte[] beforeIbm = Bytes(m.InverseBindMatrices);
        ushort[] beforeJoints = (ushort[])m.Joints.Clone();
        var map = new Dictionary<string, string> { { "A", "B" }, { "B", "A" } };
        IList<string> unused;
        AliasMap.Of(map).Apply(m, out unused);
        checks += Check(m.JointNames[0] == "B" && m.JointNames[1] == "A",
                        "A<->B swapped simultaneously: " + m.JointNames[0] + "," + m.JointNames[1]);
        checks += Check(m.Nodes[m.JointNodes[0]].Name == "B" && m.Nodes[m.JointNodes[1]].Name == "A",
                        "the node names followed the joint names");
        checks += Check(unused.Count == 0, "nothing was reported unused: " + unused.Count);

        // ---- the index tables are untouched. This is the whole safety argument for renaming in place.
        checks += Check(Same(beforeIbm, Bytes(m.InverseBindMatrices)), "the inverse bind matrices are byte-identical");
        checks += Check(Same(beforeJoints, m.Joints), "the per-vertex joint slots are unchanged");

        // ---- a key the file does not have is IGNORED and reported, and the rest still applies.
        SkinnedModel partial = Model("A", "B");
        AliasMap.Of(new Dictionary<string, string> { { "A", "Root" }, { "Q", "Neck" } })
                .Apply(partial, out unused);
        checks += Check(partial.JointNames[0] == "Root" && partial.JointNames[1] == "B",
                        "the valid entry applied while the absent key did not: " + partial.JointNames[0] +
                        "," + partial.JointNames[1]);
        checks += Check(unused.Count == 1 && unused[0] == "Q", "the absent key is named back: " + unused.Count);

        // ---- an output used twice is refused whole: it would make two file bones one, which is
        // exactly the PlainCollision the binder already refuses, only silently.
        checks += Check(AliasMap.Of(new Dictionary<string, string> { { "A", "R" }, { "B", "R" } }) == null,
                        "a colliding output is refused");
        checks += Check(AliasMap.Of(new Dictionary<string, string> { { "A", "" } }) == null,
                        "an empty output is refused");
        checks += Check(AliasMap.Of(null) == null, "a null map is refused");

        // ---- the sidecar: what LOADS, what does not, and why.
        string dir = Path.Combine(Path.GetTempPath(), "ct_alias_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string glb = Path.Combine(dir, "x.glb");
            byte[] bytes = new byte[] { 1, 2, 3, 4 };
            File.WriteAllBytes(glb, bytes);
            string sha = AliasMap.Sha256(bytes);

            string why;
            checks += Check(AliasMap.LoadSidecar(glb, sha, out why) == null && why == null,
                            "no sidecar is not a problem, and says nothing: " + why);

            AliasMap.SaveSidecar(glb, sha, bytes.Length,
                                 new Dictionary<string, string> { { "A", "Root" } });
            checks += Check(File.Exists(AliasMap.SidecarPathOf(glb)), "the sidecar was created");
            AliasMap loaded = AliasMap.LoadSidecar(glb, sha, out why);
            checks += Check(loaded != null && loaded.Count == 1 && why == null,
                            "a matching sidecar loads clean: " + why);

            // updating an existing sidecar goes down File.Replace, not File.Move
            AliasMap.SaveSidecar(glb, sha, bytes.Length,
                                 new Dictionary<string, string> { { "A", "Root" }, { "B", "Neck" } });
            checks += Check(AliasMap.LoadSidecar(glb, sha, out why).Count == 2, "the sidecar was updated in place");

            checks += Check(AliasMap.LoadSidecar(glb, "deadbeef", out why) == null &&
                            why != null && why.IndexOf("re-exported", StringComparison.Ordinal) >= 0,
                            "a stale hash is NOT applied and says so: " + why);

            File.WriteAllText(AliasMap.SidecarPathOf(glb), "{ not json ");
            checks += Check(AliasMap.LoadSidecar(glb, sha, out why) == null && why != null,
                            "malformed JSON is not applied and says so: " + why);

            File.WriteAllText(AliasMap.SidecarPathOf(glb),
                              "{\"schema\":99,\"source\":{\"sha256\":\"" + sha + "\"},\"bones\":{}}");
            checks += Check(AliasMap.LoadSidecar(glb, sha, out why) == null &&
                            why != null && why.IndexOf("99", StringComparison.Ordinal) >= 0,
                            "an unknown schema is not applied and names the number: " + why);
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }

        return "ALIAS PASS, " + checks + " check(s) - simultaneous rename, untouched index tables, sidecar policy";
    }

    private static SkinnedModel Model(params string[] names)
    {
        var m = new SkinnedModel { Joints = new ushort[] { 0, 1, 0, 0 }, Weights = new[] { 0.5f, 0.5f, 0f, 0f } };
        m.Nodes.Add(new SkinNode { Name = "rig", Parent = -1 });
        m.JointNodes = new int[names.Length];
        m.InverseBindMatrices = new float[names.Length][];
        for (int j = 0; j < names.Length; j++)
        {
            m.JointNames.Add(names[j]);
            m.Nodes.Add(new SkinNode { Name = names[j], Parent = 0 });
            m.JointNodes[j] = j + 1;
            m.InverseBindMatrices[j] = new float[16];
            m.InverseBindMatrices[j][12] = j + 1f;
        }
        return m;
    }

    private static byte[] Bytes(float[][] rows)
    {
        var all = new List<byte>();
        foreach (float[] r in rows) foreach (float f in r) all.AddRange(BitConverter.GetBytes(f));
        return all.ToArray();
    }

    private static bool Same(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static bool Same(ushort[] a, ushort[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("ALIAS FAILURE: " + what);
        return 1;
    }
}
```

- [ ] **Step 2: Run and watch it fail to compile.** `dotnet run --project tests\ObjCodecTests -c Release`. Expected: `error CS0103: The name 'AliasMap' does not exist in the current context`.

- [ ] **Step 3: Write `AliasMap`.** Create `src\Import\AliasMap.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Morgott.ContentTool.Import
{
    /// <summary>
    /// FILE BONE -&gt; GAME BONE, and nothing else. The game's skeleton is never renamed and never
    /// replaced; what an author can honestly fix from inside the game is which of THEIR bones stands
    /// for which of the game's, so that is the only thing this carries.
    ///
    /// Applied only on the REPLACEMENT read (GlbSource.ReadReplacement). The add-model route
    /// (ContentProject.ImportModel) ignores sidecars on purpose: its published bone-path hashes must
    /// not depend on a file sitting next to the .glb.
    /// </summary>
    internal sealed class AliasMap
    {
        internal const int Schema = 1;

        private readonly Dictionary<string, string> bones;

        private AliasMap(Dictionary<string, string> map) { bones = map; }

        internal int Count => bones.Count;

        internal IEnumerable<KeyValuePair<string, string>> Entries => bones;

        /// <summary>
        /// A map, or NULL when it could never be applied: no entries, an empty output, or two file
        /// bones renamed onto one game bone (which is the PlainCollision the binder already refuses,
        /// and doing it silently here would be worse).
        /// </summary>
        internal static AliasMap Of(IDictionary<string, string> map)
        {
            if (map == null || map.Count == 0) return null;
            var copy = new Dictionary<string, string>(StringComparer.Ordinal);
            var outputs = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> e in map)
            {
                if (string.IsNullOrEmpty(e.Key) || string.IsNullOrEmpty(e.Value)) return null;
                if (!outputs.Add(e.Value)) return null;
                copy[e.Key] = e.Value;
            }
            return new AliasMap(copy);
        }

        /// <summary>
        /// Renames the file's joints SIMULTANEOUSLY - every new name is read from the ORIGINAL names,
        /// so a map that swaps two bones does not apply one half onto the result of the other. The
        /// joint order, the weights, the inverse bind matrices, the node parents and every animation
        /// track's node index are untouched: only the strings move.
        /// </summary>
        /// <param name="unusedKeys">keys the file has no bone for. Applied partially and reported,
        /// never refused whole - an author who fixed two of three names should keep the two.</param>
        internal void Apply(SkinnedModel model, out IList<string> unusedKeys)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            var used = new HashSet<string>(StringComparer.Ordinal);
            string[] original = model.JointNames.ToArray();
            for (int j = 0; j < original.Length; j++)
            {
                string to;
                if (!bones.TryGetValue(original[j], out to)) continue;
                used.Add(original[j]);
                model.JointNames[j] = to;
                if (model.JointNodes != null && j < model.JointNodes.Length)
                {
                    int node = model.JointNodes[j];
                    if (node >= 0 && node < model.Nodes.Count) model.Nodes[node].Name = to;
                }
            }
            var unused = new List<string>();
            foreach (KeyValuePair<string, string> e in bones) if (!used.Contains(e.Key)) unused.Add(e.Key);
            unusedKeys = unused;
        }

        /// <summary>Which outputs do NOT name a bone the target has. Asked here and only here - neither
        /// Apply nor the loader ever sees a target, which is why spec v2's "output not a target bone"
        /// check had nowhere to live.</summary>
        internal IList<string> OutputsNotIn(string[] targetBoneNames)
        {
            var bad = new List<string>();
            if (targetBoneNames == null) return bad;
            var have = new HashSet<string>(targetBoneNames, StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> e in bones) if (!have.Contains(e.Value)) bad.Add(e.Key);
            return bad;
        }

        internal string Describe(string sidecarPath)
        {
            var sb = new StringBuilder();
            sb.Append(bones.Count.ToString(CultureInfo.InvariantCulture)).Append(" alias(es) from ").Append(sidecarPath);
            foreach (KeyValuePair<string, string> e in bones)
                sb.Append("\n    '").Append(e.Key).Append("' -> '").Append(e.Value).Append('\'');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ the sidecar

        internal static string SidecarPathOf(string glbPath) { return glbPath + ".aliases.json"; }

        internal static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>
        /// The sidecar next to the .glb, or null. <paramref name="why"/> is null when there is simply
        /// no sidecar and a SENTENCE whenever one exists but was not applied - stale, malformed,
        /// unknown schema, colliding outputs. Never silent, and never fatal: a sidecar that does not
        /// apply leaves a file that may still bind by name on its own, so the caller carries this as a
        /// WARNING and computes the outcome from the unaliased model.
        /// </summary>
        internal static AliasMap LoadSidecar(string glbPath, string sha256, out string why)
        {
            why = null;
            string path = SidecarPathOf(glbPath);
            if (!File.Exists(path)) return null;
            try
            {
                var root = Json.Parse(File.ReadAllText(path), 16) as Dictionary<string, object>;
                if (root == null) { why = "'" + path + "' is not a JSON object, so its aliases were NOT applied"; return null; }

                object schema;
                double declared = root.TryGetValue("schema", out schema) && schema is double d ? d : 0;
                if ((int)declared != Schema)
                {
                    why = "'" + path + "' declares schema " + ((int)declared).ToString(CultureInfo.InvariantCulture) +
                          " but this mod reads " + Schema.ToString(CultureInfo.InvariantCulture) +
                          ", so its aliases were NOT applied";
                    return null;
                }

                object src;
                var source = root.TryGetValue("source", out src) ? src as Dictionary<string, object> : null;
                object stated;
                string was = source != null && source.TryGetValue("sha256", out stated) ? stated as string : null;
                if (!string.Equals(was, sha256, StringComparison.OrdinalIgnoreCase))
                {
                    why = "'" + path + "' was written for a different version of this .glb (the file has been " +
                          "re-exported since), so its aliases were NOT applied - open the Doctor and save them again";
                    return null;
                }

                object raw;
                var bones = root.TryGetValue("bones", out raw) ? raw as Dictionary<string, object> : null;
                if (bones == null) { why = "'" + path + "' carries no \"bones\" object, so nothing was applied"; return null; }
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, object> e in bones) map[e.Key] = e.Value as string;
                AliasMap loaded = Of(map);
                if (loaded == null)
                    why = "'" + path + "' maps two of the file's bones onto one of the game's, or leaves a name " +
                          "empty, so NONE of its aliases were applied";
                return loaded;
            }
            catch (Exception ex)
            {
                why = "'" + path + "' could not be read (" + ex.Message + "), so its aliases were NOT applied";
                return null;
            }
        }

        /// <summary>
        /// Writes the sidecar through a temporary file: File.Move when creating, File.Replace when
        /// updating (File.Replace fails outright if the destination does not exist), so a crash
        /// mid-write cannot leave half a map beside the model.
        /// </summary>
        internal static void SaveSidecar(string glbPath, string sha256, long bytes, IDictionary<string, string> map)
        {
            string path = SidecarPathOf(glbPath);
            string tmp = path + ".tmp";
            var sb = new StringBuilder();
            sb.Append("{\n  \"schema\": ").Append(Schema.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\n  \"source\": { \"sha256\": \"").Append(sha256).Append("\", \"bytes\": ")
              .Append(bytes.ToString(CultureInfo.InvariantCulture)).Append(" }");
            sb.Append(",\n  \"bones\": {");
            bool first = true;
            foreach (KeyValuePair<string, string> e in map)
            {
                sb.Append(first ? "\n    " : ",\n    ");
                sb.Append('"').Append(Escape(e.Key)).Append("\": \"").Append(Escape(e.Value)).Append('"');
                first = false;
            }
            sb.Append(first ? "}" : "\n  }").Append("\n}\n");

            File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        private static string Escape(string s)
        {
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
```

- [ ] **Step 4: Register.** In `tests\ObjCodecTests\ObjCodecTests.csproj`, after the `ImportRefused.cs` line, add:

```xml
    <Compile Include="..\..\src\Import\AliasMap.cs" Link="AliasMap.cs" />
```

and after `<Compile Include="DecisionGolden.cs" />` add:

```xml
    <Compile Include="AliasTests.cs" />
```

In `tests\ObjCodecTests\Program.cs`, after `Console.WriteLine(DecisionGolden.Run());`, add:

```csharp
        Console.WriteLine(AliasTests.Run());
```

- [ ] **Step 5: Run and expect PASS.** `dotnet run --project tests\ObjCodecTests -c Release`. Expected: `ALIAS PASS, 18 check(s) - simultaneous rename, untouched index tables, sidecar policy`, exit 0.

- [ ] **Step 6: Build.** `dotnet build -c Release` — expected `Build succeeded`.

- [ ] **Step 7: Commit.**

```
git add src/Import/AliasMap.cs tests/ObjCodecTests/AliasTests.cs tests/ObjCodecTests/ObjCodecTests.csproj tests/ObjCodecTests/Program.cs
git commit -m "feat(import): bone-name aliases with a sidecar that is never applied silently"
```

---

### Task 6: `GlbSource.ReadReplacement` — one read, with provenance

**Files:**
- Create: `src\Import\GlbSource.cs`
- Modify: `src\Dev\LiveMesh.cs:35-59`, `src\Project\ContentProject.cs:88-97` and `:611-619`, `src\Bake\BundleBaker.cs` (`how` string), `tests\ObjCodecTests\ObjCodecTests.csproj`, `tests\ObjCodecTests\AliasTests.cs`
- Test: `tests\ObjCodecTests\AliasTests.cs` (extended)

- [ ] **Step 1: Add the failing checks.** In `tests\ObjCodecTests\AliasTests.cs`, inside the `try` block of the sidecar section, immediately before its closing brace, add:

```csharp
            // ---- the READ that every replacement path goes through, with its provenance intact.
            byte[] real = File.ReadAllBytes(Probe());
            string realGlb = Path.Combine(dir, "probe.glb");
            File.WriteAllBytes(realGlb, real);
            ReplacementSource plain = GlbSource.ReadReplacement(real, realGlb);
            checks += Check(plain.Model != null && plain.AliasesApplied == 0 && plain.SidecarPath == null,
                            "a .glb with no sidecar reads clean and claims no aliases");
            checks += Check(plain.Sha256 == AliasMap.Sha256(real) && plain.Bytes == real.Length,
                            "the envelope carries the bytes' own hash and length");

            string firstBone = plain.Model.JointNames[0];
            AliasMap.SaveSidecar(realGlb, plain.Sha256, real.Length,
                                 new Dictionary<string, string> { { firstBone, "CT_RENAMED" } });
            ReplacementSource aliased = GlbSource.ReadReplacement(real, realGlb);
            checks += Check(aliased.Model.JointNames[0] == "CT_RENAMED" && aliased.AliasesApplied == 1,
                            "the sidecar renamed the file's first bone: " + aliased.Model.JointNames[0]);
            checks += Check(aliased.AliasLog != null &&
                            aliased.AliasLog.IndexOf("CT_RENAMED", StringComparison.Ordinal) >= 0,
                            "and the log NAMES the mapping, so nothing is silent: " + aliased.AliasLog);
            checks += Check(aliased.Original.JointNames[0] == firstBone,
                            "the pristine names survive for re-aliasing without a re-parse");
```

and add this helper next to `Check`:

```csharp
    /// <summary>The committed rigged fixture. Copied beside the build output by ContentTool.csproj,
    /// and read here from the repo so this gate does not depend on a deploy.</summary>
    internal static string Probe()
    {
        string here = AppDomain.CurrentDomain.BaseDirectory;
        string path = Path.GetFullPath(Path.Combine(here, "..\\..\\..\\..\\..\\lib\\u9_probe.glb"));
        if (!File.Exists(path)) throw new Exception("ALIAS FAILURE: lib\\u9_probe.glb is missing at " + path);
        return path;
    }
```

- [ ] **Step 2: Run and watch it fail to compile.** `dotnet run --project tests\ObjCodecTests -c Release`. Expected: `error CS0103: The name 'GlbSource' does not exist in the current context`.

- [ ] **Step 3: Write `GlbSource`.** Create `src\Import\GlbSource.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Morgott.ContentTool.Import
{
    /// <summary>
    /// A .glb read for REPLACEMENT, with everything the callers have to be able to say about it. A
    /// bare SkinnedModel is not enough: neither LiveMesh.Load nor the bake could then name the
    /// sidecar it applied, and "never silent" (design §5) would be a promise nothing could keep.
    /// </summary>
    internal sealed class ReplacementSource
    {
        /// <summary>The model with aliases APPLIED - what a preview and a bake must both use.</summary>
        internal SkinnedModel Model;
        /// <summary>The same file read again with NO aliases. The outcome is computed from this one
        /// whenever the sidecar did not apply, and the Doctor re-derives every alias edit from its
        /// joint names so edits are order-independent.</summary>
        internal SkinnedModel Original;
        internal string Path;
        internal string Sha256;
        internal long Bytes;
        /// <summary>The sidecar that WAS applied, or null.</summary>
        internal string SidecarPath;
        internal int AliasesApplied;
        internal AliasMap Aliases;
        /// <summary>Sidecar keys the file has no bone for - one AliasUnused warning each.</summary>
        internal IList<string> UnusedAliasKeys = new List<string>();
        /// <summary>Why a sidecar that EXISTS was not applied, or null. Always a warning, never an
        /// outcome: ignoring a sidecar leaves a file that may still bind by name on its own.</summary>
        internal string SidecarRefusal;
        /// <summary>One block naming the sidecar and every mapping, ready for the log. Null when no
        /// sidecar applied.</summary>
        internal string AliasLog;
    }

    /// <summary>
    /// THE single "read a .glb for a replacement" helper. LiveMesh.Load, ContentProject.ImportMesh and
    /// ReplacementPreflight all come through here, so a sidecar cannot apply on one path and not on
    /// another - which is the way a preview and a bake start disagreeing.
    /// </summary>
    internal static class GlbSource
    {
        internal static ReplacementSource ReadReplacement(byte[] bytes, string path)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            var source = new ReplacementSource
            {
                Path = path,
                Bytes = bytes.Length,
                Sha256 = AliasMap.Sha256(bytes),
                Original = GlbReader.Read(bytes)
            };
            source.Model = source.Original;

            string why;
            AliasMap map = AliasMap.LoadSidecar(path, source.Sha256, out why);
            source.SidecarRefusal = why;
            if (map == null) return source;

            // The aliased model is a SECOND read, not a mutation of Original: the Doctor re-applies a
            // changed map onto the pristine names every keystroke, and a model renamed in place has
            // already lost them.
            source.Model = GlbReader.Read(bytes);
            IList<string> unused;
            map.Apply(source.Model, out unused);
            source.Aliases = map;
            source.AliasesApplied = map.Count - unused.Count;
            source.UnusedAliasKeys = unused;
            source.SidecarPath = AliasMap.SidecarPathOf(path);
            source.AliasLog = map.Describe(source.SidecarPath);
            return source;
        }
    }
}
```

- [ ] **Step 4: Route `LiveMesh.Load` through it.** In `src\Dev\LiveMesh.cs`, replace lines 43-50 (the body of the `try`) with:

```csharp
                BakedMesh baked;
                if (string.Equals(ext, ".glb", StringComparison.OrdinalIgnoreCase))
                {
                    ReplacementSource source = GlbSource.ReadReplacement(File.ReadAllBytes(file), file);
                    model = source.Model;
                    // NEVER SILENT: a bone the game renamed for the author is exactly the kind of help
                    // that becomes a mystery when it is not said out loud.
                    if (source.AliasLog != null)
                        ContentToolMain.Say("ct_replace: applied " + source.AliasLog);
                    else if (source.SidecarRefusal != null)
                        ContentToolMain.Say("ct_replace: " + source.SidecarRefusal);
                    baked = ModelBuild.From(model, Path.GetFileNameWithoutExtension(file)).Mesh;
                }
                else baked = MeshBuild.From(ObjCodec.Parse(File.ReadAllText(file)));
                return ToMesh(baked, Path.GetFileName(file));
```

- [ ] **Step 5: Route `ContentProject.ImportMesh` through it, and let the bake name the aliases.** In `src\Project\ContentProject.cs`, add two fields to `ImportedMesh` (after `:96`):

```csharp
        /// <summary>The alias sidecar that was applied to <see cref="Model"/>, or null. Carried so the
        /// bake log can name it - an author must never discover a rename by its effect.</summary>
        internal string SidecarPath;
        internal int AliasesApplied;
```

and replace `:617-618` with:

```csharp
            ReplacementSource source = GlbSource.ReadReplacement(File.ReadAllBytes(path), path);
            return new ImportedMesh
            {
                Name = name,
                Baked = ModelBuild.From(source.Model, name).Mesh,
                Model = source.Model,
                SidecarPath = source.SidecarPath,
                AliasesApplied = source.AliasesApplied
            };
```

In `src\Bake\BundleBaker.cs`, widen `ReplaceMesh`'s signature by one optional argument and append it to `how`. Change the signature to:

```csharp
        internal string ReplaceMesh(string assetName, string sourceName, BakedMesh baked, SkinnedModel model,
                                    out string refusal, out string mapping, out bool suspect,
                                    int aliases = 0, string sidecar = null)
```

and immediately before `info.SetNewData(mesh);` (`:203`) insert:

```csharp
            if (aliases > 0 && sidecar != null)
                how += " with " + aliases + " alias(es) from " + sidecar;
```

Then find every caller of `ReplaceMesh` (`grep -rn "ReplaceMesh(" src\Bake src\Project`) and, at the one that passes an `ImportedMesh`, add `, mesh.AliasesApplied, mesh.SidecarPath` as the last two arguments. The optional parameters mean every other caller compiles unchanged.

- [ ] **Step 6: Register and run.** In `tests\ObjCodecTests\ObjCodecTests.csproj`, after the `AliasMap.cs` line, add:

```xml
    <Compile Include="..\..\src\Import\GlbSource.cs" Link="GlbSource.cs" />
```

`dotnet run --project tests\ObjCodecTests -c Release`. Expected: `ALIAS PASS, 23 check(s) - …`, exit 0.

- [ ] **Step 7: Build.** `dotnet build -c Release` — expected `Build succeeded`. (`LiveMesh.cs` and `ContentProject.cs` are Unity-side and are only proven to COMPILE here; their behaviour is covered by the in-game acceptance in task 13.)

- [ ] **Step 8: Commit.**

```
git add src/Import/GlbSource.cs src/Dev/LiveMesh.cs src/Project/ContentProject.cs src/Bake/BundleBaker.cs tests/ObjCodecTests/AliasTests.cs tests/ObjCodecTests/ObjCodecTests.csproj
git commit -m "feat(import): one replacement read that carries its alias provenance"
```

---

### Task 7: `ReplacementPreflight` — bytes to verdict

**Files:**
- Create: `src\Doctor\Diagnostic.cs`, `src\Doctor\ReplacementPreflight.cs`, `tests\ObjCodecTests\PreflightTests.cs`
- Modify: `tests\ObjCodecTests\ObjCodecTests.csproj`, `tests\ObjCodecTests\Program.cs`
- Test: `tests\ObjCodecTests\PreflightTests.cs`

- [ ] **Step 1: Write the end-to-end gate.** Create `tests\ObjCodecTests\PreflightTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Doctor;
using Morgott.ContentTool.Import;

/// <summary>
/// THE WHOLE PIPELINE, on a REAL committed .glb (lib\u9_probe.glb - rigged, 2 888 B, the same file
/// gate U9 reads). Model-and-name-list fixtures cannot reach this: they never exercise the byte
/// path, the sidecar, the skinless guard or the not-rigged branch, which are four of the places the
/// Doctor and the bake could disagree.
///
/// Every target below is built FROM the file's own joint names, so a passing run is not a constant
/// that happens to match - change the fixture and the expectations move with it.
/// </summary>
internal static class PreflightTests
{
    internal static string Run()
    {
        byte[] bytes = File.ReadAllBytes(AliasTests.Probe());
        SkinnedModel probe = GlbReader.Read(bytes);
        string[] own = probe.JointNames.ToArray();
        int checks = 0;

        string dir = Path.Combine(Path.GetTempPath(), "ct_preflight_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string glb = Path.Combine(dir, "probe.glb");
            File.WriteAllBytes(glb, bytes);

            // ---- the file against its own skeleton: the one outcome that keeps the author's weights.
            ReplacementPreflightResult ok = ReplacementPreflight.Run(bytes, glb, Rig(own));
            checks += Check(ok.Outcome == Outcome.ByName, "the file binds onto its own bone names: " + ok.Outcome);
            checks += Check(ok.Baked != null && ok.Baked.Mesh.VertexCount > 0,
                            "the bake's own mesh build ran, so a preview has something to show");
            checks += Check(ok.Sha256 == AliasMap.Sha256(bytes), "the result carries the bytes it was computed from");

            // ---- one renamed bone: the case the whole feature exists for.
            string[] renamed = (string[])own.Clone();
            string was = renamed[0];
            renamed[0] = "CT_NOT_IN_FILE";
            ReplacementPreflightResult bad = ReplacementPreflight.Run(bytes, glb, Rig(renamed));
            checks += Check(bad.Outcome == Outcome.NearestBone,
                            "one wrong bone name costs the author's weights: " + bad.Outcome);
            checks += Check(Has(bad, "MissingBone") && Has(bad, "ExtraBone"),
                            "and BOTH halves of that are listed, not just the first: " + Codes(bad));

            // ---- the alias fixes it, through the sidecar, exactly as the bake would read it.
            AliasMap.SaveSidecar(glb, ok.Sha256, bytes.Length,
                                 new Dictionary<string, string> { { was, "CT_NOT_IN_FILE" } });
            ReplacementPreflightResult fixedUp = ReplacementPreflight.Run(bytes, glb, Rig(renamed));
            checks += Check(fixedUp.Outcome == Outcome.ByName,
                            "the sidecar alias turns it back into BY NAME: " + fixedUp.Outcome + " " + Codes(fixedUp));

            // ---- a STALE sidecar is a warning, and the outcome comes from the UNALIASED model.
            AliasMap.SaveSidecar(glb, "deadbeef", bytes.Length,
                                 new Dictionary<string, string> { { was, "CT_NOT_IN_FILE" } });
            ReplacementPreflightResult stale = ReplacementPreflight.Run(bytes, glb, Rig(renamed));
            checks += Check(stale.Outcome == Outcome.NearestBone,
                            "a stale sidecar does not silently fix anything: " + stale.Outcome);
            checks += Check(Has(stale, "SidecarStale") && Severity(stale, "SidecarStale") == Severity.Warning,
                            "and it is a WARNING, not the reason for the outcome: " + Codes(stale));
            File.Delete(AliasMap.SidecarPathOf(glb));

            // ---- an alias that names a bone the TARGET does not have. Only the preflight can know.
            AliasMap.SaveSidecar(glb, ok.Sha256, bytes.Length,
                                 new Dictionary<string, string> { { was, "CT_NO_SUCH_TARGET_BONE" } });
            ReplacementPreflightResult wrongOut = ReplacementPreflight.Run(bytes, glb, Rig(own));
            checks += Check(Has(wrongOut, "AliasNotATargetBone"),
                            "an alias output that is not a target bone is named: " + Codes(wrongOut));
            File.Delete(AliasMap.SidecarPathOf(glb));

            // ---- the target the game gave us has no bone list at all.
            RigTarget noNames = Rig(own);
            noNames.BoneNames = null;
            checks += Check(ReplacementPreflight.Run(bytes, glb, noNames).Outcome == Outcome.NearestBone,
                            "no target bone names is NEAREST-BONE, not a crash");

            // ---- the target is not rigged.
            RigTarget flat = Rig(own);
            flat.Rigged = false;
            flat.BindPoseCount = 0;
            checks += Check(ReplacementPreflight.Run(bytes, glb, flat).Outcome == Outcome.NotRigged,
                            "a target with no bind poses is NOT RIGGED");

            // ---- a skinless source onto a rigged target: the one case that writes nothing.
            SkinnedModel skinless = GlbReader.Read(bytes);
            skinless.JointNames.Clear();
            byte[] skinlessBytes = GlbCodec.Write(skinless);
            string skinlessPath = Path.Combine(dir, "skinless.glb");
            File.WriteAllBytes(skinlessPath, skinlessBytes);
            ReplacementPreflightResult refused = ReplacementPreflight.Run(skinlessBytes, skinlessPath, Rig(own));
            checks += Check(refused.Outcome == Outcome.Refused && Has(refused, "SkinlessOntoRigged"),
                            "a skinless source onto a rigged target is REFUSED: " + refused.Outcome + " " + Codes(refused));

            // ---- garbage in. The worker must never throw; it must report.
            ReplacementPreflightResult junk = ReplacementPreflight.Run(new byte[] { 7, 7, 7, 7 }, glb, Rig(own));
            checks += Check(junk.Outcome == Outcome.Refused && Has(junk, "MalformedGlb"),
                            "four bytes of nonsense come back as a REPORT, not an exception: " + Codes(junk));
        }
        finally { try { Directory.Delete(dir, true); } catch (Exception) { } }

        return "PREFLIGHT PASS, " + checks + " check(s) - lib\\u9_probe.glb through the real pipeline";
    }

    /// <summary>A target that says yes to everything except what the caller changes.</summary>
    private static RigTarget Rig(string[] boneNames)
    {
        return new RigTarget
        {
            BoneNames = boneNames,
            Rigged = true,
            RendererInstanceId = 1,
            MeshInstanceId = 2,
            BindPoseCount = boneNames.Length,
            TransformPath = "Root/Body",
            MeshName = "CHR_TEST"
        };
    }

    private static bool Has(ReplacementPreflightResult r, string code)
    {
        foreach (Diagnostic d in r.Report.Rows) if (d.Code == code) return true;
        return false;
    }

    private static Severity Severity(ReplacementPreflightResult r, string code)
    {
        foreach (Diagnostic d in r.Report.Rows) if (d.Code == code) return d.Severity;
        throw new Exception("PREFLIGHT FAILURE: no row '" + code + "'");
    }

    private static string Codes(ReplacementPreflightResult r)
    {
        var names = new List<string>();
        foreach (Diagnostic d in r.Report.Rows) names.Add(d.Code);
        return "[" + string.Join(", ", names.ToArray()) + "]";
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("PREFLIGHT FAILURE: " + what);
        return 1;
    }
}
```

- [ ] **Step 2: Run and watch it fail to compile.** `dotnet run --project tests\ObjCodecTests -c Release`. Expected: `error CS0246: The type or namespace name 'Doctor' does not exist in the namespace 'Morgott.ContentTool'`.

- [ ] **Step 3: Write the plain data.** Create `src\Doctor\Diagnostic.cs`:

```csharp
using System.Collections.Generic;
using Morgott.ContentTool.Import;

namespace Morgott.ContentTool.Doctor
{
    /// <summary>How much a row costs the author. Assigned by the DOCTOR - SkinCompatibility keeps its
    /// issues severity-free, because the same fact is fatal to SkinBinder.Bind and merely expensive
    /// to a bake that falls back.</summary>
    internal enum Severity
    {
        /// <summary>Nothing will be written, or nothing can be previewed.</summary>
        Blocking,
        /// <summary>It imports, and it loses the author's weights.</summary>
        Downgrade,
        /// <summary>Something was ignored. The model is unaffected.</summary>
        Warning,
        /// <summary>Said out loud so it is not a surprise later.</summary>
        Info
    }

    /// <summary>Whose asset a row is about. Target rows are drawn apart: "this is the game's model,
    /// not your file" is the difference between a fix and a dead end.</summary>
    internal enum DiagnosticSide { File, Target, Sidecar }

    /// <summary>One row of the report. Code is a stable string (spec v3 §7) so the UI, the log and a
    /// future manifest all name the same thing.</summary>
    internal sealed class Diagnostic
    {
        internal string Code;
        internal Severity Severity;
        internal DiagnosticSide Side;
        /// <summary>The engine's own sentence, verbatim.</summary>
        internal string Message;
        /// <summary>What to do in Blender. Empty when there is nothing the author can do.</summary>
        internal string Remedy = "";
        /// <summary>The bone the row is about, or null.</summary>
        internal string Subject;
    }

    /// <summary>The rows plus the verdict they add up to.</summary>
    internal sealed class DiagnosticReport
    {
        internal readonly List<Diagnostic> Rows = new List<Diagnostic>();
        internal Outcome Outcome;

        internal void Add(string code, Severity severity, DiagnosticSide side, string message,
                          string remedy = "", string subject = null)
        {
            Rows.Add(new Diagnostic
            {
                Code = code, Severity = severity, Side = side,
                Message = message, Remedy = remedy, Subject = subject
            });
        }

        internal int Count(Severity severity)
        {
            int n = 0;
            foreach (Diagnostic d in Rows) if (d.Severity == severity) n++;
            return n;
        }

        /// <summary>The one line at the top of the panel, and the one line worth pasting when asking
        /// for help.</summary>
        internal string Header()
        {
            switch (Outcome)
            {
                case Outcome.ByName: return "BY NAME - your weights will be used";
                case Outcome.NearestBone:
                    return "NEAREST-BONE - the bake would import this but NOT use your weights (" +
                           Count(Severity.Downgrade) + " reason(s))";
                case Outcome.NotRigged: return "NOT RIGGED - the target carries no bind poses";
                default: return "IMPORT REFUSED (" + Count(Severity.Blocking) + " reason(s))";
            }
        }
    }
}
```

- [ ] **Step 4: Write the preflight.** Create `src\Doctor\ReplacementPreflight.cs`:

```csharp
using System;
using System.Collections.Generic;
using Morgott.ContentTool.Import;

namespace Morgott.ContentTool.Doctor
{
    /// <summary>Everything one run produced. The report is what the author reads; the rest is what a
    /// preview and a save need, so neither has to parse the file again.</summary>
    internal sealed class ReplacementPreflightResult
    {
        internal DiagnosticReport Report = new DiagnosticReport();
        internal Outcome Outcome;
        /// <summary>The ALIASED model - what LiveMesh.Build must be handed.</summary>
        internal SkinnedModel Model;
        /// <summary>The same file with its own names, so an alias edit re-derives from originals.</summary>
        internal SkinnedModel Original;
        /// <summary>ModelBuild's own output. Null when the import was refused.</summary>
        internal BakedSkin Baked;
        internal string Sha256;
        internal ReplacementSource Source;
    }

    /// <summary>
    /// BYTES TO VERDICT, and the only thing that runs off the main thread. No UnityEngine type enters
    /// or leaves, so the whole pipeline is provable in tests\ObjCodecTests - and so a worker thread
    /// cannot construct a Unity object by accident, which would be a crash rather than a bug.
    ///
    /// The outcome is NOT decided here. It is asked of ReplacementDecision.Decide, the same function
    /// BundleBaker.ReplaceMesh asks.
    /// </summary>
    internal static class ReplacementPreflight
    {
        internal static ReplacementPreflightResult Run(byte[] bytes, string path, RigTarget target)
        {
            var result = new ReplacementPreflightResult();
            if (target == null) target = new RigTarget();
            try
            {
                ReplacementSource source = GlbSource.ReadReplacement(bytes, path);
                result.Source = source;
                result.Sha256 = source.Sha256;
                result.Model = source.Model;
                result.Original = source.Original;
                result.Baked = ModelBuild.From(source.Model, "preflight");
                Sidecar(result, source, target);
                return Verdict(result, source, target);
            }
            catch (ImportRefusedException refused)
            {
                result.Sha256 = result.Sha256 ?? AliasMap.Sha256(bytes ?? new byte[0]);
                result.Report.Add(refused.Code.ToString(), Severity.Blocking, DiagnosticSide.File,
                                  refused.Message, Remedy.For(refused.Code));
                result.Outcome = Outcome.Refused;
                result.Report.Outcome = Outcome.Refused;
                return result;
            }
            catch (Exception ex)
            {
                // THE WORKER BOUNDARY. An I/O error, a bug, anything at all: it becomes a row rather
                // than an unhandled exception on a background thread, which in Unity is a hard stop.
                result.Report.Add("ImportFailed", Severity.Blocking, DiagnosticSide.File,
                                  "'" + path + "' could not be read: " + ex.GetType().Name + " - " + ex.Message,
                                  "This is not an export setting - the log carries the details. " +
                                  "Check the file is not open in another program and try again.");
                result.Outcome = Outcome.Refused;
                result.Report.Outcome = Outcome.Refused;
                return result;
            }
        }

        /// <summary>Everything the sidecar has to say, all of it a WARNING: ignoring a sidecar leaves a
        /// file that may still bind by name on its own, so a sidecar never decides the outcome.</summary>
        private static void Sidecar(ReplacementPreflightResult result, ReplacementSource source, RigTarget target)
        {
            if (source.SidecarRefusal != null)
                result.Report.Add(source.SidecarRefusal.IndexOf("re-exported", StringComparison.Ordinal) >= 0
                                      ? "SidecarStale" : "SidecarInvalid",
                                  Severity.Warning, DiagnosticSide.Sidecar, source.SidecarRefusal,
                                  "Open the bone map, set the names again and press Save aliases.");
            foreach (string key in source.UnusedAliasKeys)
                result.Report.Add("AliasUnused", Severity.Warning, DiagnosticSide.Sidecar,
                                  "the alias for '" + key + "' was ignored: this file has no bone of that name",
                                  "Delete the row, or rename the bone in Blender to '" + key + "'.", key);
            if (source.Aliases == null) return;
            foreach (string key in source.Aliases.OutputsNotIn(target.BoneNames))
                result.Report.Add("AliasNotATargetBone", Severity.Warning, DiagnosticSide.Sidecar,
                                  "the alias for '" + key + "' names a bone this model's skeleton does not have",
                                  "Pick the target bone from the list instead of typing it.", key);
        }

        private static ReplacementPreflightResult Verdict(ReplacementPreflightResult result,
                                                          ReplacementSource source, RigTarget target)
        {
            // The OUTCOME is computed from the model the BAKE would see. When a sidecar did not apply,
            // that is the unaliased one - which is exactly what the bake will read from the same
            // sidecar a moment later.
            SkinnedModel effective = source.Aliases == null ? source.Original : source.Model;
            bool armature = effective.JointNames.Count > 0;
            bool names = target.BoneNames != null && target.BoneNames.Length > 0;

            IList<BindingIssue> issues = names
                ? SkinCompatibility.Analyze(effective, target.BoneNames)
                : new List<BindingIssue>();
            BindingIssue first = issues.Count == 0 ? null : issues[0];
            Outcome outcome = ReplacementDecision.Decide(armature, target.Rigged, names, first);

            if (outcome == Outcome.Refused)
                result.Report.Add("SkinlessOntoRigged", Severity.Blocking, DiagnosticSide.File,
                                  Bake.SkinFields.Skinless(target.MeshName ?? "this model"),
                                  "In Blender give the mesh an Armature modifier with vertex groups, " +
                                  "weight it to the bones the target already has, and export as .glb.");
            else if (outcome == Outcome.NotRigged)
                result.Report.Add("TargetNotRigged", Severity.Info, DiagnosticSide.Target,
                                  "not rigged - the target carries no bind poses", "");
            else
            {
                if (!names)
                    result.Report.Add("TargetBonesUnavailable", Severity.Downgrade, DiagnosticSide.Target,
                                      "the target model lists no bones, so there is no skeleton to bind onto",
                                      "Re-pick the target, or reload the scene.");
                foreach (BindingIssue issue in issues)
                    result.Report.Add(issue.Code.ToString(),
                                      issue.Code == BindCode.NoArmature ? Severity.Blocking : Severity.Downgrade,
                                      issue.Side == BindSide.Target ? DiagnosticSide.Target : DiagnosticSide.File,
                                      issue.Message, Remedy.For(issue.Code), issue.Subject);
            }

            result.Outcome = outcome;
            result.Report.Outcome = outcome;
            return result;
        }
    }

    /// <summary>
    /// The one sentence that turns a refusal into an action. Deliberately separate from the engine's
    /// own message, which stays verbatim: the message says what happened, the remedy says which box
    /// to tick in Blender 4.x, and only the second one goes stale when Blender moves a menu.
    /// </summary>
    internal static class Remedy
    {
        internal static string For(ImportCode code)
        {
            switch (code)
            {
                case ImportCode.Oversize: return "Reduce texture resolution, or remove unused meshes and animations, and re-export.";
                case ImportCode.ExternalBuffer: return "In the export dialog set Format to 'glTF Binary (.glb)', not 'glTF Separate'.";
                case ImportCode.NoMesh: return "Check the mesh object is selected and visible when you export.";
                case ImportCode.NonTriangle: return "Tick 'Apply Modifiers' and triangulate the faces (Triangulate modifier, or Ctrl+T in Edit mode).";
                case ImportCode.NotIndexed: return "Re-export with Blender's own glTF exporter; indexed geometry is its default.";
                case ImportCode.TooManyInfluences: return "Weight Paint > Weights > Limit Total, set it to 4, then re-export.";
                case ImportCode.NoVertices: return "Export the mesh itself, not an empty or an armature-only selection.";
                case ImportCode.NoNormals: return "In the export dialog, under Mesh, leave 'Normals' ticked.";
                case ImportCode.UnsupportedGlb: return "Import the file into Blender and export it again with compression and extension add-ons off.";
                default: return "Re-export the file from Blender rather than editing it by hand.";
            }
        }

        internal static string For(BindCode code)
        {
            switch (code)
            {
                case BindCode.TargetBonesUnavailable: return "Re-pick the target, or reload the scene.";
                case BindCode.NoArmature: return "Parent the mesh to the armature (Ctrl+P > Armature Deform) and export with Skinning on.";
                case BindCode.JointsWeightsMismatch: return "Give the WHOLE mesh an Armature modifier with vertex groups, then re-export.";
                case BindCode.TriangleOutOfRange: return "Re-export; do not edit the .glb by hand.";
                case BindCode.BlendShapeCount: return "Remove the shape keys, or replace a model that has them - a replacement cannot add shapes.";
                case BindCode.TargetBoneEmpty: return "This is the game's own model. Re-pick the target.";
                case BindCode.TargetBoneDuplicate: return "This is the game's own model; it cannot be replaced by name.";
                case BindCode.DuplicateFileBone: return "Two of your bones share a name - rename one in Blender and re-export.";
                case BindCode.PlainCollision: return "Keep the one bone that belongs to this model, delete the other, and re-export.";
                case BindCode.MissingBone: return "Rename your bone to this name - or map it in the table above.";
                case BindCode.ExtraBone: return "Map this bone to the one it stands for, or transfer its weights to its parent and delete it.";
                case BindCode.NotBijective: return "Check the table above for a target bone chosen twice.";
                case BindCode.InverseBindCount: return "Broken export - re-export from Blender rather than editing the file.";
                default: return "Broken export - re-export from Blender rather than editing the file.";
            }
        }
    }
}
```

- [ ] **Step 5: Register.** In `tests\ObjCodecTests\ObjCodecTests.csproj`, after the `GlbSource.cs` line, add:

```xml
    <!-- The Doctor's pure half: plain report rows and the bytes-to-verdict pipeline. UnityEngine-free
         on purpose (it runs on a worker thread), so the whole verdict is proven offline. -->
    <Compile Include="..\..\src\Doctor\Diagnostic.cs" Link="Diagnostic.cs" />
    <Compile Include="..\..\src\Doctor\ReplacementPreflight.cs" Link="ReplacementPreflight.cs" />
```

and after `<Compile Include="AliasTests.cs" />` add:

```xml
    <Compile Include="PreflightTests.cs" />
```

In `tests\ObjCodecTests\Program.cs`, after `Console.WriteLine(AliasTests.Run());`, add:

```csharp
        Console.WriteLine(PreflightTests.Run());
```

- [ ] **Step 6: Run and expect PASS.** `dotnet run --project tests\ObjCodecTests -c Release`. Expected: `PREFLIGHT PASS, 14 check(s) - lib\u9_probe.glb through the real pipeline`, exit 0. If `lib\u9_probe.glb` turns out not to be rigged (`ok.Outcome` comes back `NotRigged`), swap the fixture to `demos\CustomCreature\Content\Models\cyborg_spider.glb` and adjust `AliasTests.Probe()` — the gate is written to read whatever the fixture says, so only the path changes.

- [ ] **Step 7: Build.** `dotnet build -c Release` — expected `Build succeeded`.

- [ ] **Step 8: Commit.**

```
git add src/Doctor/Diagnostic.cs src/Doctor/ReplacementPreflight.cs tests/ObjCodecTests/PreflightTests.cs tests/ObjCodecTests/ObjCodecTests.csproj tests/ObjCodecTests/Program.cs
git commit -m "feat(doctor): ReplacementPreflight turns bytes and a rig into the bake's own verdict"
```

---

### Task 8: The Unity seams — `LiveMesh.Build` and a binding mode

Unity-side: `UnityEngine.Mesh` cannot be constructed outside the player, so nothing in this task runs under `dotnet run --project tests\ObjCodecTests`. It is proven by `dotnet build -c Release` here and by the in-game acceptance in task 13.

**Files:**
- Modify: `src\Dev\LiveMesh.cs:35-151`
- Test: `dotnet build -c Release` (compile), task 13 (behaviour)

- [ ] **Step 1: Add the `Build` seam.** In `src\Dev\LiveMesh.cs`, immediately after the closing brace of `Load` (`:59`), insert:

```csharp
        /// <summary>
        /// A model that is ALREADY parsed to a live Mesh. The Doctor parses on a worker thread and
        /// must build on the main one, so the two halves of Load are separated here rather than
        /// duplicated - a preview built by a second path would be a different mesh that merely looks
        /// similar, which is the exact bug Load's own remark exists to prevent.
        ///
        /// Main thread only: `new Mesh` is a UnityEngine.Object.
        /// </summary>
        internal static Mesh Build(SkinnedModel model, string name)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            return ToMesh(ModelBuild.From(model, Path.GetFileNameWithoutExtension(name)).Mesh, name);
        }
```

- [ ] **Step 2: Report the binding mode.** In `src\Dev\LiveMesh.cs`, immediately before the `Bind` method (`:114`), insert:

```csharp
        /// <summary>Which of the three things Bind actually did. Bind reports in ENGLISH, and a
        /// preview that has to check its own prediction cannot read English.</summary>
        internal enum BindMode { NotRigged, NearestBone, ByName }
```

Change `Bind`'s signature (`:114`) to:

```csharp
        internal static string Bind(Mesh ours, SkinnedMeshRenderer smr, SkinnedModel file = null)
        {
            BindMode mode;
            return Bind(ours, smr, file, out mode);
        }

        /// <summary>The same bind, saying WHICH path it took. The Doctor refuses to swap a preview in
        /// when the mode is not the one the preflight predicted: that mismatch means the target
        /// changed under us, and showing the wrong skinning is worse than showing none.</summary>
        internal static string Bind(Mesh ours, SkinnedMeshRenderer smr, SkinnedModel file, out BindMode mode)
```

Then inside the body: replace `if (bones == null || bones.Length == 0) return null;` (`:117`) with:

```csharp
            mode = BindMode.NotRigged;
            if (bones == null || bones.Length == 0) return null;
```

replace `if (byName != null) return byName;` (`:126`) with:

```csharp
                if (byName != null) { mode = BindMode.ByName; return byName; }
```

and immediately before `Vector3[] verts = ours.vertices;` (`:129`) insert:

```csharp
            mode = BindMode.NearestBone;
```

- [ ] **Step 3: Build.** `dotnet build -c Release`. Expected: `Build succeeded`, 0 errors — `SeamSwap.cs:248` still compiles because the three-argument overload is kept.

- [ ] **Step 4: Run the offline suite to prove nothing regressed.** `dotnet run --project tests\ObjCodecTests -c Release` — expected exit 0, every gate as before.

- [ ] **Step 5: Commit.**

```
git add src/Dev/LiveMesh.cs
git commit -m "feat(dev): a build seam for an already-parsed model, and a binding mode a caller can check"
```

---

### Task 9: `GlbFileBrowser`

Unity-side (IMGUI); compile-checked here, exercised in task 13.

**Files:**
- Create: `src\Dev\GlbFileBrowser.cs`
- Test: `dotnet build -c Release`

- [ ] **Step 1: Write it.** Create `src\Dev\GlbFileBrowser.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// Pick a .glb without typing a path. A native file dialog is not reachable from inside the
    /// player without dragging in a Windows interop dependency, and this needs three things a
    /// dependency would not buy: drives, a filter, and the five files the author actually works on.
    ///
    /// ponytail: no thumbnails, no sorting, no search box. Add them when picking a file is what an
    /// author complains about.
    /// </summary>
    internal sealed class GlbFileBrowser
    {
        private const int Recents = 5;

        private string dir;
        private Vector2 scroll;
        private readonly List<string> recent = new List<string>();

        internal bool Open { get; private set; }

        internal void Show(string startDir)
        {
            dir = Directory.Exists(startDir) ? startDir : FirstDrive();
            LoadRecent();
            Open = true;
        }

        internal void Hide() { Open = false; }

        /// <summary>Draws the browser and returns the picked path, or null. Call once per OnGUI while
        /// <see cref="Open"/>.</summary>
        internal string Draw(float height)
        {
            string picked = null;
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("in: " + Elide(dir, 46));
            if (GUILayout.Button("up", GUILayout.Width(40f)))
            {
                DirectoryInfo parent = Directory.GetParent(dir);
                dir = parent == null ? dir : parent.FullName;
            }
            if (GUILayout.Button("x", GUILayout.Width(24f))) Open = false;
            GUILayout.EndHorizontal();

            if (recent.Count > 0)
            {
                GUILayout.Label("recent");
                foreach (string r in recent.ToArray())
                    if (GUILayout.Button(Elide(Path.GetFileName(r), 44), GUILayout.Height(18f)))
                        picked = r;
            }

            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(height));
            foreach (string drive in Drives())
                if (GUILayout.Button("[" + drive + "]", GUILayout.Height(18f))) dir = drive;
            foreach (string sub in Sorted(Subdirectories()))
                if (GUILayout.Button("> " + Elide(Path.GetFileName(sub), 42), GUILayout.Height(18f))) dir = sub;
            foreach (string file in Sorted(Files()))
                if (GUILayout.Button(Elide(Path.GetFileName(file), 44), GUILayout.Height(18f))) picked = file;
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            if (picked != null) { Remember(picked); Open = false; }
            return picked;
        }

        // ------------------------------------------------------------------ disk, defensively

        private string[] Subdirectories()
        {
            try { return Directory.GetDirectories(dir); } catch (Exception) { return new string[0]; }
        }

        private string[] Files()
        {
            try { return Directory.GetFiles(dir, "*.glb"); } catch (Exception) { return new string[0]; }
        }

        private static string[] Drives()
        {
            try { return Directory.GetLogicalDrives(); } catch (Exception) { return new string[0]; }
        }

        private static string FirstDrive()
        {
            string[] drives = Drives();
            return drives.Length > 0 ? drives[0] : ".";
        }

        private static string[] Sorted(string[] paths)
        {
            Array.Sort(paths, StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        // ------------------------------------------------------------------ the five that matter

        /// <summary>The mod has no settings store, so this is a plain text file beside everything else
        /// ContentTool writes (ContentToolMain.cs:65). One path per line, newest first.</summary>
        private static string RecentFile()
        {
            return Path.Combine(Path.Combine(Application.persistentDataPath, "ContentTool"), "doctor-recent.txt");
        }

        private void LoadRecent()
        {
            recent.Clear();
            try
            {
                if (!File.Exists(RecentFile())) return;
                foreach (string line in File.ReadAllLines(RecentFile()))
                    if (line.Length > 0 && File.Exists(line) && recent.Count < Recents) recent.Add(line);
            }
            catch (Exception) { }
        }

        private void Remember(string path)
        {
            recent.Remove(path);
            recent.Insert(0, path);
            while (recent.Count > Recents) recent.RemoveAt(recent.Count - 1);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RecentFile()));
                File.WriteAllLines(RecentFile(), recent.ToArray());
            }
            catch (Exception) { }
            dir = Path.GetDirectoryName(path) ?? dir;
        }

        private static string Elide(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            return "..." + s.Substring(s.Length - (max - 3));
        }
    }
}
```

- [ ] **Step 2: Build.** `dotnet build -c Release`. Expected: `Build succeeded`, 0 errors.

- [ ] **Step 3: Commit.**

```
git add src/Dev/GlbFileBrowser.cs
git commit -m "feat(dev): an IMGUI .glb browser with drives, a filter and five recents"
```

---

### Task 10: `ModelDoctor` — session, worker and fingerprint

Unity-side; compile-checked here, exercised in task 13. This task builds the non-drawing half: the state, the snapshot, the intent queue and the worker job. The panel comes in task 11 and the tab in task 12, so `ModelDoctor` is unreferenced until then — that is intentional and does not break the build.

**Files:**
- Create: `src\Dev\ModelDoctor.cs`
- Test: `dotnet build -c Release`

- [ ] **Step 1: Write the session half.** Create `src\Dev\ModelDoctor.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Morgott.ContentTool.Doctor;
using Morgott.ContentTool.Import;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// THE MODEL DOCTOR. Pick a .glb, pick the skinned mesh it should replace, and read what the BAKE
    /// would do with them - before writing a manifest, before a bake, before a restart.
    ///
    /// The verdict is not computed here. ReplacementPreflight.Run computes it, on a worker thread,
    /// out of bytes and a plain snapshot of the rig, using the same ReplacementDecision.Decide the
    /// bake uses. This class is the part that cannot be pure: it owns the Unity objects, the
    /// generation counter that makes a stale answer harmless, and the fingerprint that makes a
    /// preview refuse to land on a target that changed under it.
    ///
    /// Threading, stated once: OnGUI only reads and enqueues intents; Update only drains and mutates;
    /// the worker only touches bytes. Nothing else is allowed to move between them.
    /// </summary>
    internal sealed class ModelDoctor
    {
        private enum Intent { Preview, Revert, Save }

        private readonly ConcurrentQueue<Intent> intents = new ConcurrentQueue<Intent>();
        private readonly ConcurrentQueue<Job> done = new ConcurrentQueue<Job>();
        private readonly Dictionary<string, string> aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly List<int> ourMeshes = new List<int>();

        private sealed class Job
        {
            internal int Gen;
            internal ReplacementPreflightResult Result;
        }

        private int gen;
        private bool running;

        internal string Path;
        internal SkinnedMeshRenderer Renderer;
        internal RigTarget Target;
        internal ReplacementPreflightResult Ready;
        internal string Message = "";

        /// <summary>The mesh this Doctor put on the renderer, and the one it took off, so Revert puts
        /// back the OBJECT rather than something that looks like it.</summary>
        private Mesh preview;
        private Mesh origin;
        private Bounds originBounds;

        internal bool Busy => running;
        internal bool HasPreview => preview != null;
        internal IDictionary<string, string> Aliases => aliases;

        // ------------------------------------------------------------------ picking

        internal void PickFile(string path)
        {
            Path = path;
            aliases.Clear();
            Restart();
        }

        internal void PickTarget(SkinnedMeshRenderer smr, string transformPath)
        {
            Renderer = smr;
            Target = Snapshot(smr, transformPath);
            Restart();
        }

        internal void SetAlias(string fileBone, string targetBone)
        {
            if (string.IsNullOrEmpty(targetBone)) aliases.Remove(fileBone);
            else aliases[fileBone] = targetBone;
            Restart();
        }

        internal void Enqueue(string what)
        {
            if (what == "preview") intents.Enqueue(Intent.Preview);
            else if (what == "revert") intents.Enqueue(Intent.Revert);
            else if (what == "save") intents.Enqueue(Intent.Save);
        }

        /// <summary>
        /// A plain copy of everything about the target that a preview depends on. Taken on the main
        /// thread, and compared again immediately before every swap: a SkinnedMeshRenderer keeps its
        /// instance id while another mod, an addon or the bench's own rebuild replaces its mesh, its
        /// bind poses and its bones underneath it.
        /// </summary>
        internal static RigTarget Snapshot(SkinnedMeshRenderer smr, string transformPath)
        {
            var t = new RigTarget { TransformPath = transformPath ?? "" };
            if (smr == null) return t;
            t.RendererInstanceId = smr.GetInstanceID();
            Transform[] bones = smr.bones;
            if (bones != null && bones.Length > 0)
            {
                t.BoneNames = new string[bones.Length];
                for (int b = 0; b < bones.Length; b++) t.BoneNames[b] = bones[b] == null ? "" : bones[b].name;
            }
            Mesh mesh = smr.sharedMesh;
            if (mesh == null) return t;
            t.MeshInstanceId = mesh.GetInstanceID();
            t.MeshName = mesh.name ?? "";
            Matrix4x4[] poses = mesh.bindposes;
            t.BindPoseCount = poses == null ? 0 : poses.Length;
            t.Rigged = t.BindPoseCount > 0;
            return t;
        }

        // ------------------------------------------------------------------ the job

        /// <summary>Every change bumps the generation, so an answer that was already in flight is
        /// dropped when it lands rather than cancelled - the worker has nothing to roll back.</summary>
        private void Restart()
        {
            gen++;
            Ready = null;
            if (Path == null || Target == null) return;
            Start(gen);
        }

        private void Start(int forGen)
        {
            if (running) return;                       // Update starts the next one when this returns
            running = true;
            string path = Path;
            RigTarget target = Target;
            var map = new Dictionary<string, string>(aliases, StringComparer.Ordinal);
            ThreadPool.QueueUserWorkItem(delegate
            {
                var job = new Job { Gen = forGen };
                try
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    job.Result = ReplacementPreflight.Run(bytes, path, target);
                    ApplyLiveAliases(job.Result, map, target);
                }
                catch (Exception ex)
                {
                    job.Result = new ReplacementPreflightResult { Outcome = Outcome.Refused };
                    job.Result.Report.Outcome = Outcome.Refused;
                    job.Result.Report.Add("ImportFailed", Severity.Blocking, DiagnosticSide.File,
                                          "'" + path + "' could not be read: " + ex.GetType().Name + " - " + ex.Message,
                                          "Check the file is not open in another program and try again.");
                }
                done.Enqueue(job);
            });
        }

        /// <summary>
        /// The aliases the author is editing RIGHT NOW, which are not in the sidecar yet. Applied to
        /// the PRISTINE names every time, so the edits are order-independent and a swap of two names
        /// works - the same rule AliasMap.Apply keeps for the saved ones.
        /// </summary>
        private static void ApplyLiveAliases(ReplacementPreflightResult result,
                                             Dictionary<string, string> map, RigTarget target)
        {
            if (result.Original == null || map.Count == 0) return;
            AliasMap live = AliasMap.Of(map);
            if (live == null) return;
            SkinnedModel model = result.Original;
            var rebuilt = new SkinnedModel();
            foreach (string n in model.JointNames) rebuilt.JointNames.Add(n);
            IList<string> unused;
            live.Apply(model, out unused);
            IList<BindingIssue> issues = target.BoneNames == null
                ? new List<BindingIssue>() : SkinCompatibility.Analyze(model, target.BoneNames);
            Outcome outcome = ReplacementDecision.Decide(model.JointNames.Count > 0, target.Rigged,
                                                         target.BoneNames != null && target.BoneNames.Length > 0,
                                                         issues.Count == 0 ? null : issues[0]);
            result.Model = model;
            result.Outcome = outcome;
            var report = new DiagnosticReport { Outcome = outcome };
            foreach (BindingIssue issue in issues)
                report.Add(issue.Code.ToString(),
                           issue.Code == BindCode.NoArmature ? Severity.Blocking : Severity.Downgrade,
                           issue.Side == BindSide.Target ? DiagnosticSide.Target : DiagnosticSide.File,
                           issue.Message, Remedy.For(issue.Code), issue.Subject);
            foreach (string key in unused)
                report.Add("AliasUnused", Severity.Warning, DiagnosticSide.Sidecar,
                           "the alias for '" + key + "' was ignored: this file has no bone of that name",
                           "Delete the row, or rename the bone in Blender to '" + key + "'.", key);
            foreach (string key in live.OutputsNotIn(target.BoneNames))
                report.Add("AliasNotATargetBone", Severity.Warning, DiagnosticSide.Sidecar,
                           "the alias for '" + key + "' names a bone this model's skeleton does not have",
                           "Pick the target bone from the list instead of typing it.", key);
            result.Report = report;
        }

        // ------------------------------------------------------------------ the main thread

        /// <summary>Called every frame from the bench's Update. Drains results, then intents.</summary>
        internal void Tick()
        {
            Job job;
            while (done.TryDequeue(out job))
            {
                running = false;
                if (job.Gen != gen) continue;                          // stale: the author moved on
                RigTarget now = Snapshot(Renderer, Target.TransformPath);
                if (!now.SameAs(Target))
                {
                    job.Result.Report.Add("TargetChanged", Severity.Blocking, DiagnosticSide.Target,
                                          "the model this report was made for has changed since it was picked",
                                          "Press Change and pick the target again.");
                    Target = now;
                }
                Ready = job.Result;
            }
            if (!running && Ready == null && Path != null && Target != null) Start(gen);

            Intent intent;
            while (intents.TryDequeue(out intent))
            {
                if (intent == Intent.Preview) Message = DoPreview();
                else if (intent == Intent.Revert) Message = Revert();
                else Message = DoSave();
            }
        }

        private string DoPreview()
        {
            if (Ready == null) return "nothing to preview yet";
            if (Ready.Outcome == Outcome.Refused || Ready.Outcome == Outcome.NotRigged)
                return "this file would not be written at all, so there is nothing to preview";

            string stale = Stale();
            if (stale != null) { Restart(); return stale; }

            RigTarget now = Snapshot(Renderer, Target.TransformPath);
            if (!now.SameAs(Target)) { Target = now; Restart(); return "the target changed - reading it again"; }

            Mesh candidate = LiveMesh.Build(Ready.Model, System.IO.Path.GetFileName(Path));
            ourMeshes.Add(candidate.GetInstanceID());
            LiveMesh.BindMode mode;
            string how = LiveMesh.Bind(candidate, Renderer, Ready.Model, out mode);
            Outcome got = mode == LiveMesh.BindMode.ByName ? Outcome.ByName
                        : mode == LiveMesh.BindMode.NearestBone ? Outcome.NearestBone : Outcome.NotRigged;
            if (got != Ready.Outcome)
            {
                // CANDIDATE-THEN-SWAP: the preview that is already on screen is untouched, and the one
                // that disagreed with the prediction is destroyed rather than shown. A wrong skinning
                // shown confidently is worse than none.
                UnityEngine.Object.Destroy(candidate);
                Ready.Report.Add("PreviewDisagreed", Severity.Blocking, DiagnosticSide.Target,
                                 "the live bind came out " + got + " where the report predicted " + Ready.Outcome +
                                 ", so the preview was not applied",
                                 "The model changed under the report. Press Change and pick the target again.");
                return "preview REFUSED: the live bind disagreed with the report (" + got + " vs " + Ready.Outcome + ")";
            }

            if (preview == null)
            {
                origin = Renderer.sharedMesh;
                originBounds = Renderer.localBounds;
            }
            else UnityEngine.Object.Destroy(preview);
            preview = candidate;
            Renderer.sharedMesh = candidate;
            Renderer.localBounds = candidate.bounds;
            return "preview: " + how;
        }

        internal string Revert()
        {
            if (preview == null) return "no preview is live";
            if (Renderer != null)
            {
                Renderer.sharedMesh = origin;
                Renderer.localBounds = originBounds;
            }
            UnityEngine.Object.Destroy(preview);
            preview = null;
            return "preview reverted - the game's own mesh is back, by reference";
        }

        private string DoSave()
        {
            if (Path == null || Ready == null) return "nothing to save";
            if (aliases.Count == 0) return "no aliases to save";
            string stale = Stale();
            if (stale != null) { Restart(); return stale; }
            try
            {
                byte[] bytes = File.ReadAllBytes(Path);
                AliasMap.SaveSidecar(Path, AliasMap.Sha256(bytes), bytes.Length, aliases);
                Ready.Report.Add("AliasesSaved", Severity.Info, DiagnosticSide.Sidecar,
                                 aliases.Count + " alias(es) saved to " + AliasMap.SidecarPathOf(Path), "");
                return "saved " + aliases.Count + " alias(es) to " + AliasMap.SidecarPathOf(Path);
            }
            catch (Exception ex) { return "could not save: " + ex.Message; }
        }

        /// <summary>
        /// Has the .glb changed since the report was made? An author re-exports from Blender while
        /// this panel is open, and saving then would bind names authored against the OLD joints to the
        /// NEW file's hash - a sidecar that is wrong and looks right.
        /// </summary>
        private string Stale()
        {
            try
            {
                if (AliasMap.Sha256(File.ReadAllBytes(Path)) == Ready.Sha256) return null;
                return "the .glb has changed on disk since this report - reading it again";
            }
            catch (Exception ex) { return "the .glb could not be re-read: " + ex.Message; }
        }

        /// <summary>Everything this Doctor owns, given back. Called when the bench closes.</summary>
        internal void Dispose()
        {
            Revert();
            Path = null;
            Renderer = null;
            Target = null;
            Ready = null;
            aliases.Clear();
        }
    }
}
```

- [ ] **Step 2: Build.** `dotnet build -c Release`. Expected: `Build succeeded`, 0 errors. (The unused local `rebuilt` in `ApplyLiveAliases` is a leftover of the pristine-copy sketch — delete that line before building; the pristine names are already preserved because `result.Original` is a separate `GlbReader.Read`, per `GlbSource.ReadReplacement`.)

- [ ] **Step 3: Run the offline suite.** `dotnet run --project tests\ObjCodecTests -c Release` — expected exit 0, unchanged. `ModelDoctor` is Unity-side and contributes no gate.

- [ ] **Step 4: Commit.**

```
git add src/Dev/ModelDoctor.cs
git commit -m "feat(dev): Model Doctor session - generation, target fingerprint, worker and preview swap"
```

---

### Task 11: `ModelDoctor` — the panel

Unity-side; compile-checked here, exercised in task 13.

**Files:**
- Modify: `src\Dev\ModelDoctor.cs`
- Test: `dotnet build -c Release`

- [ ] **Step 1: Add the drawing half.** In `src\Dev\ModelDoctor.cs`, immediately before the `Dispose` method, insert:

```csharp
        // ------------------------------------------------------------------ the panel

        private readonly GlbFileBrowser browser = new GlbFileBrowser();
        private Vector2 rowScroll;
        private string mapOpenFor;

        /// <summary>
        /// Draws the whole Doctor. READS ONLY: every button enqueues an intent that Tick performs on
        /// the next frame, because mutating Unity objects inside OnGUI is how an IMGUI layout ends up
        /// unbalanced and the panel throws every frame afterwards.
        /// </summary>
        internal void Draw(float width)
        {
            if (browser.Open)
            {
                string picked = browser.Draw(260f);
                if (picked != null) PickFile(picked);
                return;
            }

            GUILayout.Label("source: " + (Path == null ? "-" : Elide(System.IO.Path.GetFileName(Path), 40)));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Browse...", GUILayout.Width(90f)))
                browser.Show(Path == null ? "" : System.IO.Path.GetDirectoryName(Path));
            if (Ready != null && Ready.Source != null && Ready.Source.AliasesApplied > 0)
                GUILayout.Label("ALIASES ACTIVE (" + Ready.Source.AliasesApplied + ")");
            GUILayout.EndHorizontal();

            GUILayout.Label("target: " + (Target == null ? "-" : Elide(Target.TransformPath, 34)) +
                            (Target == null ? "" : "  (" + (Target.BoneNames == null ? 0 : Target.BoneNames.Length) +
                                                   " bones, mesh '" + Elide(Target.MeshName, 20) + "')"));

            if (Path == null || Target == null)
            {
                GUILayout.Label("pick a .glb and a skinned mesh to see what the bake would do with them");
                return;
            }
            if (Ready == null) { GUILayout.Label(Busy ? "reading..." : "queued..."); return; }

            GUILayout.Space(4f);
            GUILayout.Label(Ready.Report.Header());
            GUILayout.Space(2f);

            if (Ready.Outcome == Outcome.NearestBone && Ready.Model != null && Target.BoneNames != null)
                BoneMap();

            rowScroll = GUILayout.BeginScrollView(rowScroll, GUILayout.Height(200f));
            Rows(Severity.Blocking, "REFUSED");
            Rows(Severity.Downgrade, "LOSES YOUR WEIGHTS");
            Rows(Severity.Warning, "IGNORED");
            Rows(Severity.Info, "NOTE");
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            bool canPreview = Ready.Outcome == Outcome.ByName || Ready.Outcome == Outcome.NearestBone;
            GUI.enabled = canPreview;
            if (GUILayout.Button("Preview", GUILayout.Width(80f))) Enqueue("preview");
            GUI.enabled = HasPreview;
            if (GUILayout.Button("Revert", GUILayout.Width(70f))) Enqueue("revert");
            GUI.enabled = aliases.Count > 0;
            if (GUILayout.Button("Save aliases", GUILayout.Width(110f))) Enqueue("save");
            GUI.enabled = true;
            if (GUILayout.Button("Copy report", GUILayout.Width(100f)))
                GUIUtility.systemCopyBuffer = PlainText();
            GUILayout.EndHorizontal();
            if (Message.Length > 0) GUILayout.Label(Message);
        }

        /// <summary>
        /// ONE table, file bones on the left and the target bone each one will land on - or a dash -
        /// on the right. Editing a cell IS an alias; nothing is ever applied on the author's behalf,
        /// because a wrong bone quietly chosen for them is the exact failure this whole panel exists
        /// to end.
        /// </summary>
        private void BoneMap()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button((mapOpenFor == null ? "> " : "v ") + "bone map", GUILayout.Width(110f)))
                mapOpenFor = mapOpenFor == null ? Path : null;
            GUILayout.EndHorizontal();
            if (mapOpenFor == null) return;

            var have = new HashSet<string>(Target.BoneNames, StringComparer.Ordinal);
            foreach (Diagnostic d in Ready.Report.Rows)
            {
                if (d.Code != "MissingBone" && d.Code != "ExtraBone") continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label(d.Code == "ExtraBone" ? Elide(d.Subject, 24) : "-", GUILayout.Width(160f));
                GUILayout.Label("->", GUILayout.Width(20f));
                if (d.Code == "ExtraBone")
                {
                    string current;
                    aliases.TryGetValue(d.Subject, out current);
                    string typed = GUILayout.TextField(current ?? "", GUILayout.Width(160f));
                    if (typed != (current ?? "") && (typed.Length == 0 || have.Contains(typed)))
                        SetAlias(d.Subject, typed);
                }
                else GUILayout.Label(Elide(d.Subject, 24), GUILayout.Width(160f));
                GUILayout.EndHorizontal();
            }
        }

        private void Rows(Severity severity, string heading)
        {
            bool any = false;
            foreach (Diagnostic d in Ready.Report.Rows)
            {
                if (d.Severity != severity) continue;
                if (!any) { GUILayout.Label(heading); any = true; }
                GUILayout.Label((d.Side == DiagnosticSide.Target ? "[the game's model] " :
                                 d.Side == DiagnosticSide.Sidecar ? "[aliases] " : "") + d.Message);
                if (d.Remedy.Length > 0) GUILayout.Label("    " + d.Remedy);
            }
        }

        /// <summary>What a non-programmer pastes when they ask for help.</summary>
        internal string PlainText()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("ContentTool Model Doctor\n");
            sb.Append("file:   ").Append(Path).Append('\n');
            sb.Append("target: ").Append(Target == null ? "-" : Target.TransformPath).Append('\n');
            if (Ready == null) return sb.Append("(no report yet)\n").ToString();
            sb.Append("verdict: ").Append(Ready.Report.Header()).Append('\n');
            foreach (Diagnostic d in Ready.Report.Rows)
                sb.Append("  [").Append(d.Severity).Append("] ").Append(d.Code).Append(": ")
                  .Append(d.Message).Append('\n');
            return sb.ToString();
        }

        private static string Elide(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "-";
            return s.Length <= max ? s : "..." + s.Substring(s.Length - (max - 3));
        }
```

- [ ] **Step 2: Build.** `dotnet build -c Release`. Expected: `Build succeeded`, 0 errors.

- [ ] **Step 3: Commit.**

```
git add src/Dev/ModelDoctor.cs
git commit -m "feat(dev): the Doctor's panel - verdict, bone map, rows and a report worth pasting"
```

---

### Task 12: The bench tab and the Advanced toggle

Unity-side; compile-checked here, exercised in task 13.

**Files:**
- Modify: `src\Dev\FitBench.cs:1307-1354` (`Draw`), `:1366-1428` (`View`), `:1717` (`Update`), `:852` (`Close`)
- Test: `dotnet build -c Release`

- [ ] **Step 1: Add the state.** In `src\Dev\FitBench.cs`, next to the other static UI fields (near `:226`, beside `units`/`weapons`), add:

```csharp
        /// <summary>The Model Doctor tab. One session per bench visit; Close gives its meshes back.</summary>
        private static readonly ModelDoctor doctor = new ModelDoctor();
        private static bool doctorTab;
        /// <summary>The numeric readouts are for the ten minutes an author spends dialling a weapon,
        /// not for the hour they spend looking at a model. Off by default.</summary>
        private static bool advanced;
```

- [ ] **Step 2: Draw the tab.** In `FitBench.Draw` (`:1307`), immediately after the `GUILayout.EndHorizontal();` that closes the CLOSE/RESET VIEW row (`:1331`), insert:

```csharp
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(!doctorTab, " FIT", GUILayout.Width(70f))) doctorTab = false;
            if (GUILayout.Toggle(doctorTab, " MODEL DOCTOR", GUILayout.Width(130f))) doctorTab = true;
            GUILayout.EndHorizontal();
            if (doctorTab)
            {
                doctor.Draw(BenchList.ContentWidth(w));
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                if (leaving) message = Close();
                else if (resetting) message = ResetView();
                return;
            }
```

- [ ] **Step 3: Hide the numbers behind Advanced.** In `FitBench.View` (`:1366`), immediately after `GUILayout.Space(4f);` (`:1368`), insert:

```csharp
            advanced = GUILayout.Toggle(advanced, " Advanced (numeric readouts and per-axis nudges)");
```

and wrap the two `GUILayout.BeginHorizontal()` blocks that draw the `view` step buttons (`:1369-1378`) and the `drag` toggles (`:1381-1388`) in `if (advanced) { … }`. Leave the model-scale slider (`:1395-1412`) and the framing label (`:1417-1427`) always visible — the slider is the one knob a model author actually turns, and the label is how they learn the mouse gestures.

In `Dial` (`:1547`), guard its per-axis nudge rows with the same `if (advanced)`; the SAVE row and the fit summary stay visible.

- [ ] **Step 4: Tick and dispose.** In the `Arm.Update` method (`:1717`), immediately after the guard that returns when the bench is not open, insert:

```csharp
                if (doctorTab) doctor.Tick();
```

In `Close` (`:852`), immediately before the method returns its summary string, insert:

```csharp
            doctor.Dispose();
            doctorTab = false;
```

- [ ] **Step 5: Build.** `dotnet build -c Release`. Expected: `Build succeeded`, 0 errors.

- [ ] **Step 6: Run the offline suite one more time.** `dotnet run --project tests\ObjCodecTests -c Release` — expected exit 0.

- [ ] **Step 7: Commit.**

```
git add src/Dev/FitBench.cs
git commit -m "feat(dev): a MODEL DOCTOR tab on the bench, and the numbers behind Advanced"
```

---

### Task 13: In-game acceptance through PPCLI

PPCLI is a SEPARATE project (`E:\DEV\PhoenixPoint\PPCLI\`). We are its CONSUMER: never edit it, never commit to it. If it misbehaves, append an entry to `E:\DEV\PhoenixPoint\PPCLI\ISSUES.md` (attempted → happened → expected → evidence → severity) and work around it.

**Files:**
- Modify: none in `src\`, unless a defect is found — then a fix, its own commit, and the offline suite re-run
- Test: the live game

- [ ] **Step 1: Read the playbook first.** Read `E:\DEV\PhoenixPoint\PPCLI\PLAYBOOK.md` and take the exact command lines for: opening the bench (`ct_bench`), running a console command against a running game (`connect console`), and `connect screenshot`. Do not dig PPCLI source; do not invent a command line.

- [ ] **Step 2: Deploy this build to the automation install.** Build and copy the mod output (`bin\Release\ContentTool\`) into `D:\PP-Instance2`'s `Mods\ContentTool\` using the repo's own `deploy.ps1` (read it first for its parameters). NEVER target `D:\Steam\steamapps\common\Phoenix Point` — that is the user's own game. Expected: the copied `ContentTool.dll` timestamp matches the build you just made.

- [ ] **Step 3: Open the bench and the tab.** With the game running on Instance2 and `connect state` actually answering (wait for it — querying a still-initialising game hangs for minutes and looks like an engine bug), run `ct_bench` through `connect console` and pick a unit. Then `connect screenshot` and confirm the panel shows the `FIT` / `MODEL DOCTOR` toggle row. Expected: a PNG whose path the reply names, showing both toggles.

- [ ] **Step 4: The known-bad file.** In the Doctor tab, Browse to `E:\DEV\PhoenixPoint\ContentTool\lib\u9_probe.glb` (a foreign armature against a Phoenix Point rig — the documented negative fixture, `ContentTool.csproj:53-58`) and pick a `SkinnedMeshRenderer` on the benched soldier. Expected: the verdict header reads `NEAREST-BONE - the bake would import this but NOT use your weights (n reason(s))`, with `MissingBone` rows naming the game's own bones. `connect screenshot` for the record.

- [ ] **Step 5: Preview and revert.** Press Preview: the model on the platform changes and the message line reports a nearest-bone bind. Press Revert: the original model is back. Expected: no exception in `%USERPROFILE%\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\Player.log` between the two screenshots — grep it for `Exception` after the run and confirm no new entries.

- [ ] **Step 6: An alias, saved, and read back by the real bake.** Type a target bone name into one bone-map cell so the verdict flips to `BY NAME - your weights will be used`, press `Save aliases`, and confirm `<file>.glb.aliases.json` exists beside the `.glb` with the mapping and the file's `sha256`. Then run `ct_project` on a project whose `Content\Meshes\` holds that file plus its sidecar. Expected: the bake log line for that mesh reads `BY NAME onto the target's own N bones … with 1 alias(es) from <sidecar path>` — the Doctor's prediction and the bake's own report, the same words.

- [ ] **Step 7: Record the run.** Append the screenshots' paths, the bake log line and the `Player.log` result to `internal-docs\planning\2026-09-01-model-doctor-design.md` as a short `## 12. Acceptance run` section — date, install, verdict observed, bake line observed. Evidence from the real run only.

- [ ] **Step 8: Commit.**

```
git add internal-docs/planning/2026-09-01-model-doctor-design.md
git commit -m "docs(planning): record the Model Doctor in-game acceptance run"
```

### In-game acceptance 2026-09-02

Install `D:\PP-Instance2` (never the user's own game), ContentTool `1.1.3.0`, PPBridge `build=8939f00f`,
geoscape from `plans\start-campaign.json`, bench on `S_SY_Eileen_CharacterTemplateDef`, target
`CHR_SY_SNI_TS_F_V01` (13 bones). IMGUI cannot be clicked through PPCLI, so the Doctor was driven by
reflection on the live instance — `AccessTools.Field(FitBench,"doctor").GetValue(null)`, then
`PickFile` / `PickTarget` / `SetAlias` / `Enqueue` — which is the same code every button reaches.

**Fixtures.** `ct_extract mesh sy_sniper_assets_all.bundle CHR_SY_SNI_TS_F_V01` names its joints by
HASH (`bone_2424243207`), because a serialized Unity Mesh stores `m_BoneNameHashes` and not names, so
the dump was renamed through the repo's own `GlbCodec.Write` to the 13 names `ct_list bones` reports:
`ts_good.glb` (the rig's own names) and `ts_bad.glb` (`L.Arm` -> `L.Arn`, one bone).

| # | Item | Result | Evidence |
|---|---|---|---|
| 1 | tab strip + Advanced at minimum height | PASS | `Screen.SetResolution(640,480)`, both tabs x Advanced on/off/on: **0** `Getting control` errors in `Player.log`, panel intact, FIT scrolls to its last row (`shots\04`, `shots\05`) |
| 2 | browser on an unavailable folder | PASS | `A:\` -> `UNAVAILABLE - DirectoryNotFoundException: Could not find a part of the path 'A:\'.`; `C:\System Volume Information` -> `UNAVAILABLE - no permission to read this folder.`; panel alive (`shots\03`) |
| 3 | recents file | PASS | `…\Phoenix Point\ContentTool\doctor-recent.txt`, newest first, capped at 5 (the 6th drops off) |
| 4 | known-bad vs a rigged target | PASS | `NEAREST-BONE - the bake would import this but NOT use your weights (2 reason(s))`; `MissingBone '#L.Arm_Addon => SY_Sniper_Torso_BodyPartDef'` + `ExtraBone 'L.Arn'`, both Downgrade, both with remedies (`shots\02`, `shots\07`) |
| 5 | preview / revert | PASS | `sharedMesh` `CHR_SY_SNI_TS_F_V01` id `1633376` -> `ts_bad.glb` id `-192190` -> `1633376` again; `OurMeshCount` 1 -> 0 |
| 6 | alias, save, sidecar | PASS | `SetAlias("L.Arn","L.Arm")` -> `BY NAME - your weights will be used`; `ts_bad.glb.aliases.json` with `schema`/`source.sha256`/`bones`; `canSave` true -> false after the save |
| 7 | the bake reads it back | PASS | `ct_project DoctorFix`: `mesh 'CHR_SY_SNI_TS_F_V01' <- ts_bad … skinned BY NAME onto the target's own 13 bones, carrying 4 of the file's own influences per vertex with 1 alias(es) from …\ts_bad.glb.aliases.json` |
| 8 | re-pick during a parse | PASS | 36 MB `body_bad.glb` then `ts_good.glb`: `Busy` true, only the last became Ready, no exception |
| 9 | unit swapped under the Doctor | PASS after fix | `ct_bench unit spider` with a live preview -> `Target`/`Renderer` null, `HasPreview` false, `OurMeshCount` 0 |
| 10 | leak gate | PASS | `Resources.FindObjectsOfTypeAll<Mesh>()` 2855 before, 2855 after 100x(preview,revert); with a preview live 2856, and after `ct_bench close` (Dispose) 2855 with `OurMeshCount` 0 |
| 11 | known-good vs its own rig | PASS after fix | `ts_good.glb` -> `BY NAME - your weights will be used`; the live preview reports `skinned BY NAME onto the target's own 13 bones, carrying the file's own weights` |
| 12 | refusals | PASS | `truncated.glb` and `notglb.glb` -> `IMPORT REFUSED (1 reason(s))`, row `MalformedGlb` / Blocking |

**Two defects found and fixed, both root cause, both offline suites green afterwards.**

- `87a65fb` `fix(binder): undecorate the LIVE rig's bone names too, not just the file's`.
  `SkinCompatibility.Analyze` undecorated only `file.JointNames`, while the Doctor and the live
  preview read a rig off a LIVE renderer, where the addon system has already renamed every attachment
  point to `#<bone>_Addon => <part>` (`Addon.MovedBoneNameFormat`). The bake reads the SHIPPED asset,
  whose names are plain (`ct_list bones` -> `Root, Spine_1, … Head`), so the Doctor contradicted the
  bake for every body part: measured `NEAREST-BONE … (26 reason(s))` = 13 `MissingBone` + 13
  `ExtraBone` for a file whose bones ARE that rig's. BY NAME was unreachable in game. Both sides are
  now looked up exact-first, then undecorated.
- `38acd14` `fix(doctor): let go of a target the bench rebuilt under us`. `FitBench.Posed` assigns
  `bay.CharacterBuilder.transform` on every rebuild — always the SAME Transform, because the rig is
  rebuilt underneath the builder — so `Root`'s `ReferenceEquals` early-out was taken on every unit
  swap and its reset never ran: measured `HasPreview` true and `OurMeshCount` 1 for a body part the
  swap had destroyed. The reset now also fires when the chosen renderer has been destroyed.

**Two observations, neither a defect.**

- `ct_project DoctorFix` ends `1 FAILURE(S)`: `P4-ctl-shipped`. The control arm asserts the shipped
  bundle does not look like the replacement, and this fixture IS the shipped mesh with one bone
  renamed, so the two are indistinguishable by vert/index/centre/extent. `P6` says the same about the
  bone order (`VOID`). A fixture artefact of replacing a mesh with itself, not a bake failure.
- The panel's labels are clipped, not wrapped, at the right edge of the strip (`shots\02`). The whole
  text is reachable through `Copy report`.

Screenshots: `C:\Temp\claude\E--DEV-PhoenixPoint-ContentTool\253e8cfb-94db-49b1-8672-cbe452669de4\scratchpad\shots\`
(`01` tab strip, `02` known-bad report, `03` browser refusal, `04`/`05`/`06` 640x480, `07` final state).

### Round 2 (Codex findings) 2026-09-02

Same install and rig, on `3441a4f` (build `d25fc3a5`) and then on the fix below (build `b917b039`).

| Check | Result | Evidence |
|---|---|---|
| 4 known-bad | PASS | `NEAREST-BONE … (2 reason(s))` |
| 5 preview/revert | PASS | mesh swapped and the original reference restored, `OurMeshCount` 1 -> 0 |
| 6 alias -> save | PASS | `BY NAME`, sidecar written, `canSave` true -> false |
| 9 unit swap with a live preview | PASS | `Target`/`Renderer` null, `HasPreview` false, `OurMeshCount` 0 |
| P1-1 preview follows its renderer | PASS | preview on A, `PickTarget(B)` -> A back to `CHR_SY_SNI_TS_F_V01`, `OurMeshCount` 0, `HasPreview` false; preview on B then reverts B and leaves A alone |
| P1-2 the file is replaced under a seeded sidecar | PASS | overwrote `ts_bad.glb` in place with `ts_good.glb`: row `SidecarStale`/Warning, `aliases` 0, `AliasesApplied` 0, `canSave` false, header follows the NEW content (`BY NAME`) |
| P2-3 the bone map survives BY NAME, and `x` undoes the alias | PASS | the map's fold is drawn under a `BY NAME` header; `SetAlias(k,null)` -> `NEAREST-BONE (2 reason(s))`, `aliases` 0, `canSave` false. With a SAVED sidecar the header stays `BY NAME` and `canSave` goes true instead — correct, because the bake still reads that file until Save removes it |
| P2-4 remove the last alias | PASS | `canSave` true on an empty map, `save` -> `sidecar removed: …ts_bad.glb.aliases.json`, file gone, `canSave` false; `ct_project DoctorFix` then prints `skinned nearest-bone … does not contain the bone 'L.Arm'` with no `with n alias(es)` |
| P2-6 blind target | PASS (driven) | no SMR on this rig has bindposes without bones (12/12 have `bones == bindposes`: 1x1, 4x8, 1x13, 4x25, 1x34, 1x4), so `Target.BoneNames` was set to null on the live target: the button reads `Preview - no live bones to bind onto` and is disabled (`ModelDoctor.cs:595-599`) |

**One more defect, found and fixed:** `2b1cca7` `fix(alias): stop calling a decorated live bone a bone
the model does not have`. `AliasMap.OutputsNotIn` compared alias OUTPUTS against the target's bone
names as raw strings, so an alias onto `L.Arm` against a live rig that spells it
`#L.Arm_Addon => SY_Sniper_Torso_BodyPartDef` produced `AliasNotATargetBone` — an IGNORED row under a
BY NAME verdict telling the author to fix a mapping that had already bound. Both call sites
(`ReplacementPreflight.Sidecar`, `ModelDoctor.ApplyLiveAliases`) go through that one method. After the
fix the same state reports zero rows in game; `AliasTests` covers both directions (28 checks).

Round-2 screenshots: `shots\r2-p1-1.png`, `r2-p1-2-sidecar-stale.png`, `r2-p2-3-bonemap-byname.png`,
`r2-p2-6-no-live-bones.png`, `r2-final-known-bad.png`.

---

## Self-review

**Spec coverage — every section of spec v3 lands in a task**

| Spec section | Task |
|---|---|
| §4.1 `Diagnostic`/`DiagnosticReport`/`Outcome` | 3 (Outcome), 7 (Diagnostic) |
| §4.1 `BindingIssue`, `SkinCompatibility.Analyze` | 2 |
| §4.1 `ImportRefusedException` | 4 |
| §4.1 `RigTarget` | 2 (type), 10 (`Snapshot`) |
| §4.1 `ReplacementDecision.Decide` | 3 |
| §4.1 `AliasMap` | 5 |
| §4.1 `ReplacementSource` / `GlbSource.ReadReplacement` | 6 |
| §4.1 `ReplacementPreflightResult` / `ReplacementPreflight.Run` | 7 |
| §4.1 `LiveMesh.Build`, `Bind(out BindMode)` | 8 |
| §4.1 `GlbFileBrowser` | 9 |
| §4.1 `ModelDoctor` | 10, 11 |
| §4.1 `FitBench` tab | 12 |
| §4.2 one decision, narrowed catch | 3 |
| §4.3 generation, fingerprint, re-hash, candidate-then-swap | 10 |
| §4.4 threading (OnGUI reads, Update mutates, worker boundary) | 7 (boundary catch), 10 (queues), 11 (read-only draw) |
| §5 sidecar format, policy, write, never-silent | 5 (format/policy/write), 6 (logging), 7 (rows) |
| §6 UI, Advanced toggle | 11, 12 |
| §7 catalogue (codes, remedies, severities) | 4 (import codes), 2 (bind codes), 7 (`Remedy`) |
| §8 frozen fixtures / decision goldens / end-to-end / alias / sidecar | 1, 3, 5, 7 |
| §8 race, in-game acceptance, leak gate | 13 |

**Placeholder scan.** No "TBD", no "similar to Task N", no "add validation here", no "write tests for the above". Every step names the file, the exact text to insert or replace, the command to run and what it prints. The only judgement left to the worker is the one Step 6 of task 7 and Step 2 of task 10 flag explicitly: swap the `.glb` fixture if `u9_probe` turns out unrigged, and delete the one dead local the sketch left in `ApplyLiveAliases`.

**Type and signature consistency.** `BindingIssue`, `BindCode`, `BindStage`, `BindSide`, `RigTarget`, `SkinCompatibility` (task 2) are used by `ReplacementDecision` (3), `ReplacementPreflight` (7) and `ModelDoctor` (10) with the same shapes. `Outcome` (3) is used by 6, 7, 10, 11. `ImportCode`/`ImportRefusedException` (4) is caught in 7 and mapped by `Remedy.For(ImportCode)` (7). `AliasMap` (5) — `Of`, `Apply(out IList<string>)`, `OutputsNotIn`, `Describe`, `Sha256`, `SidecarPathOf`, `LoadSidecar(path, sha, out why)`, `SaveSidecar(path, sha, bytes, map)` — is called with exactly those signatures in 6, 7 and 10. `ReplacementSource` (6) fields are read in 7 and 11. `ReplacementPreflightResult` (7) fields are read in 10 and 11. `LiveMesh.Build`/`BindMode` (8) are called in 10. `GlbFileBrowser` (9) — `Show`, `Hide`, `Open`, `Draw(float)` — is used in 11. `ModelDoctor` — `Draw`, `Tick`, `Dispose`, `PickFile`, `PickTarget`, `Enqueue` — is used in 12. Nothing is referenced before the task that defines it, except `ModelDoctor.Draw` (11) inside `ModelDoctor` itself, which is the same file.

**Build-green ordering.** Tasks 1-7 each end with the offline suite green; 8-12 each end with `dotnet build -c Release` green plus the offline suite unchanged. No task leaves a half-wired seam: task 10 adds an unreferenced class (legal), task 11 completes it, task 12 references it. The narrowed catch (3) lands together with the `Decide` call that justifies it, so the bake is never half-converted.

**Offline test reach.** Everything from bytes to verdict is covered by `dotnet run --project tests\ObjCodecTests -c Release`: the binder record (1), `Analyze` (2), `Decide` (3), import codes (4), aliases and sidecar policy (5), the replacement read (6), the whole preflight over a real `.glb` (7). What cannot run there — `new Mesh`, `SkinnedMeshRenderer`, IMGUI, `Application.persistentDataPath` — is confined to `src\Dev\` and `src\Project\ContentProject.cs`, is compile-checked every task, and is the entire subject of task 13.
