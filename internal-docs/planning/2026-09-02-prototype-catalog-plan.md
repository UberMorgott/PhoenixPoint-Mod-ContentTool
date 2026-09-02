# Prototype Catalog + Targeting Implementation Plan (slice 1)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development

**Goal:** Ship slice 1 of `internal-docs\planning\2026-09-02-prototype-picker-design.md` (v2): the
Model Doctor stops verifying a mesh against whatever actor is on the stand and verifies it against a
**picked prototype** — 37 physical rigs collapsed to **36 binding prototypes**, each with
manager/role **variants** and per-variant **slots** — where a Replace target is a `RigTarget`
snapshotted from the **live slot `SkinnedMeshRenderer`** a geoscape squad-bay rebuild really
produced, and an Extend target is the prototype's `BindableBones` set.

**Architecture:** Two halves, split exactly the way the shipped Doctor is split. The **pure half**
(`src\Doctor\PrototypeCatalog.cs`, `src\Doctor\PrototypeTarget.cs`) carries no `UnityEngine` type: it
takes a flat scan (rig name → bones, manager → rig/slots/anim), computes the binding signature,
merges managers into prototypes, and answers search — so the whole catalog is provable offline
against `internal-docs\research\rig-census-2026-09-02.json`, the 2551-transform live census, without
launching the game. The **Unity half** (`src\Dev\PrototypeBaySession.cs`, plus hooks in
`src\Dev\FitBench.cs` and `src\Dev\ModelDoctor.cs`) harvests that scan from `DefRepository` and owns
one transaction against the squad bay's existing `AddonsCharacterBuilder`: snapshot the displayed
unit, `DisplayCharacter` → re-tag → `RebuildCharacter` for the chosen variant, enumerate the real
SMRs on `OnCharacterRebuilded`, and put the original soldier back on close or failure. The verdict
pipeline (`SkinCompatibility.Analyze` → `ReplacementDecision.Decide` → `ReplacementPreflight.Run`)
is unchanged except for one new **Extend** mode that relaxes the bijection.

**Tech Stack:** C# 9 / net472, Mono inside Unity 2019 (Phoenix Point). **No new dependencies** —
JSON in via the existing `Morgott.ContentTool.Import.Json.Parse(text, maxDepth)`
(`src\Import\GlbReader.cs:2311-2313`, returns `Dictionary<string,object>` / `List<object>` /
`string` / `double` / `bool` / `null`), which is already linked into the test project via
`GlbReader.cs`. UI is IMGUI. Build: `dotnet build -c Release` from `E:\DEV\PhoenixPoint\ContentTool`.
Offline tests: the console EXE `tests\ObjCodecTests` (NOT `dotnet test`), run with
`dotnet run --project tests\ObjCodecTests -c Release`; every gate is a
`static class X { internal static string Run() }` that throws on failure and is called from
`Program.Main`. `tests\ObjCodecTests\ObjCodecTests.csproj` sets `EnableDefaultCompileItems=false`, so
**every new file — test or linked src — must be added to its `<Compile Include>` list**;
`ContentTool.csproj` globs `src\**\*.cs` and needs no edit. Test files locate repo assets with
`Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\..\<path>")` (pattern:
`AliasTests.cs:211-213`).

---

## Read before you touch `FitBench.cs` — it is moving under you

`src\Dev\FitBench.cs`, `src\Dev\BenchList.cs`, `src\Dev\FitAnim.cs` and `src\Import\GlbSlim.cs` are
being changed **concurrently** by the viewport-controls plan
(`internal-docs\planning\2026-09-02-viewport-controls-plan.md`, landed so far as `ae50b91`
"one OrbitCamera for the bench viewport", `0faa265` "3D-editor viewport gestures", `6adbf08`
"F frames the model and Home resets the view", and follow-ups after those).

- **Every `FitBench.cs` line number in this plan is a 2026-09-02 reading and WILL drift.** Re-read the
  file fresh before each edit and rebase the anchor mentally — match on the surrounding code, never
  on the number.
- **`OrbitCamera` (`src\Dev\OrbitCamera.cs`, held by `FitBench.view`, `FitBench.cs:328`) is the ONLY
  camera API this plan may use.** Do not add a second camera path, do not write `Camera.main`
  transforms directly, do not reintroduce the pre-`ae50b91` ad-hoc yaw/pitch/zoom fields. Framing is
  `Reframe()`; gesture classification is `OrbitCamera.Classify` / `OrbitCamera.InViewport`.
- Do not edit `GlbSlim.cs` at all — nothing in this plan needs it.

---

## File Structure

**Created**

| Path | Responsibility |
|---|---|
| `src\Doctor\PrototypeCatalog.cs` | `PrototypeBone`, `RigScan`, `ManagerScan`, `PrototypeVariant`, `PrototypeSlot`, `PrototypeRecord` + the pure `PrototypeCatalog.Build` / `Search` / bone-partition helpers. UnityEngine-free. |
| `src\Doctor\PrototypeTarget.cs` | `VerifyMode`, `PrototypeTarget` — what the Doctor verifies against, and the duplicate-name refusal rule. UnityEngine-free. |
| `src\Dev\PrototypeBaySession.cs` | The bay transaction: snapshot, serialized variant rebuilds, slot enumeration, restore. Unity-side. |
| `tests\ObjCodecTests\CatalogTests.cs` | `CATALOG` gate over the live census JSON. |

**Modified**

| Path | Change |
|---|---|
| `src\Import\SkinCompatibility.cs:23` | Two new `BindCode`s: `ExtJointWeighted`, `ExtJointUnused`. |
| `src\Import\SkinCompatibility.cs:91-102`, `:196-236` | `Analyze` gains an `extend` flag: `MissingBone` suppressed, `NotBijective` skipped, `EXT_*` file joints reported. |
| `src\Doctor\ReplacementPreflight.cs:37`, `:111`, `:188` | `Run` takes a `PrototypeTarget`; `Judge` maps the two new codes to Blocking/Warning; `Remedy.For` gains their sentences. |
| `src\Dev\FitBench.cs` (`Open` ~`:357`, subscribe ~`:418`, close ~`:936`, `Posed` ~`:1130`) | Own a `PrototypeBaySession`, tick it from the rebuild callback, dispose it on close. |
| `src\Dev\ModelDoctor.cs:558`, `:616-648` | `Targets()` becomes the full-area prototype browser; `PickTarget` accepts a `PrototypeTarget`. |
| `tests\ObjCodecTests\ObjCodecTests.csproj` | `<Compile Include>` for `CatalogTests.cs`, `PrototypeCatalog.cs`, `PrototypeTarget.cs`. |
| `tests\ObjCodecTests\Program.cs:121` | `Console.WriteLine(CatalogTests.Run());`. |
| `tests\ObjCodecTests\PreflightTests.cs` | Extend-mode cases. |
| `tests\ObjCodecTests\BinderFrozen.cs` | Proof that `extend:false` is byte-identical to today. |

---

### Task 1: `PrototypeCatalog` — 37 rigs, 36 binding prototypes, proven offline

The census is the fixture. `internal-docs\research\rig-census-2026-09-02.json` (578 KB) is a live
measurement: `_meta` carries `managersTotal:46`, `managersWithRig:42`, `distinctRigs:37`,
`transformsTotal:2551`, and each rig maps to `{managers[], instanceId, count, bones:[{name,parent,path}]}`.
Nothing in this task touches Unity, so the whole merge rule is decided before a game is ever launched.

**Files:**
- Create: `src\Doctor\PrototypeCatalog.cs`, `tests\ObjCodecTests\CatalogTests.cs`
- Modify: `tests\ObjCodecTests\ObjCodecTests.csproj`, `tests\ObjCodecTests\Program.cs:121`
- Test: `tests\ObjCodecTests\CatalogTests.cs`

- [ ] **Step 1: Write the failing gate.** Create `tests\ObjCodecTests\CatalogTests.cs`. It reads the
  census with `Json.Parse`, turns each rig into a `RigScan`, and asserts the facts the design rests on.
  Required checks, each throwing `new Exception("CATALOG FAILURE: " + what)` on failure:

  1. the census parses and `_meta.distinctRigs == 37`, `_meta.transformsTotal == 2551`;
  2. `PrototypeCatalog.Build(rigs, managers).Count == 36` — the merge of Fireworm+Acidworm is the
     ONLY merge across prefabs;
  3. `Signature(ALN_Fireworm_Rig_Ready) == Signature(ALN_Acidworm_Rig_Ready)` — 13 identical
     bone/`EXT_` names, the 14th difference being the root GameObject's own name;
  4. `Signature(ALN_Crabman_Rig_Ready) != Signature(ALN_Oilcrab_Protean_Rig_Ready)` **while**
     `Bindable(Crabman) ∩ Bindable(Oilcrab)` is large (assert `>= 25`; the raw overlap incl. `EXT_*`
     is 34) — different prototypes that share most of a naming scheme is the whole reason this
     picker exists;
  5. `AttachmentPoints(rig)` is non-empty for every one of the 37 rigs and `EXT_VoiceContext` is the
     only name present in ALL 37 — so a comparison that counted `EXT_*` would relate every rig to
     every other;
  6. `Bindable(CHR_Human_Rig_Ready).Count` + `AttachmentPoints(...).Count` == the census `count` 124,
     and the same identity for `ALN_Crabman_Rig_Ready` (58) — the partition loses nothing;
  7. `Ambiguous(ALN_Fishman_Rig_Ready)` is **empty** — `Fishman_upWrist_l` and `Fishman_upWrist_L` are
     case-variants (two distinct transforms; `GetEquivalentBones` compares case-sensitively, both reachable);
     `Ambiguous` of `VEH_NJ_Armadillo_Rig_Ready`, `VEH_PX_Scarab_V01_Rig_Ready` and
     `VEH_SYN_Sanator_Rig_Ready` each contains `light`; `Ambiguous(ALN_Crabman_Rig_Ready)` is empty;
  8. the four rig-less managers (`DefaultTacCharacter`, `Dropped`, `FallDown`,
     `YuggothianDropped_ItemContainer`) produce **no** record;
  9. the worm record has **1** id, **2** `RigPrefabNames` and **3** variants named `Fireworm`,
     `Acidworm`, `Poisonworm`; the tech-turret record has **1** `RigPrefabNames` and **3** variants;
  10. `Search(all, "mutoid")` returns the Human record (token-AND over `SearchTerms`), and
      `Search(all, "crab man")` returns Crabman while `Search(all, "crab zzz")` returns nothing.

  Return `"CATALOG PASS, " + checks + " check(s) - 37 rigs, 36 binding prototypes, off the live census"`.

  Fixture path, verbatim:

```csharp
        string census = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            @"..\..\..\..\..\internal-docs\research\rig-census-2026-09-02.json"));
        if (!File.Exists(census)) throw new Exception("CATALOG FAILURE: the rig census is missing at " + census);
        var root = (Dictionary<string, object>)Json.Parse(File.ReadAllText(census), 64);
```

- [ ] **Step 2: Register the gate and run it; watch it fail to compile.** In
  `tests\ObjCodecTests\ObjCodecTests.csproj`, after `<Compile Include="PreflightTests.cs" />`, add:

```xml
    <!-- The PROTOTYPE CATALOG carries no UnityEngine type on purpose, so which rigs are one binding
         prototype - and that Crabman and Oilcrab are NOT, despite 34 shared names - is proven against
         the live 2551-transform census here instead of by baking a mesh onto the wrong creature. -->
    <Compile Include="..\..\src\Doctor\PrototypeCatalog.cs" Link="PrototypeCatalog.cs" />
    <Compile Include="CatalogTests.cs" />
```

  In `tests\ObjCodecTests\Program.cs`, after `Console.WriteLine(PreflightTests.Run());` (`:121`), add:

```csharp
        Console.WriteLine(CatalogTests.Run());
```

  Run `dotnet run --project tests\ObjCodecTests -c Release`. Expected:
  `error CS0103: The name 'PrototypeCatalog' does not exist in the current context`.

- [ ] **Step 3: Write `PrototypeCatalog`.** Create `src\Doctor\PrototypeCatalog.cs` in namespace
  `Morgott.ContentTool.Doctor`, with **no `using UnityEngine`**. Signatures and contracts
  (implementers write the bodies):

```csharp
    /// <summary>One transform under a rig prefab, exactly as the census recorded it.</summary>
    internal sealed class PrototypeBone
    {
        internal string Name;      // Transform.name, case-sensitive - the game matches on it exactly
        internal string Parent;    // parent's Name, or null for the prefab root
        internal string Path;      // '/'-joined path from the root, the ONLY way to tell duplicates apart
    }

    /// <summary>A rig prefab and everything under it. The unit the picker is really built on.</summary>
    internal sealed class RigScan
    {
        internal string RigName;
        internal List<PrototypeBone> Bones = new List<PrototypeBone>();
        internal List<string> Managers = new List<string>();   // AddonsManagerDef names using this prefab
    }

    /// <summary>One AddonsManagerDef, flattened. HasRig false => not a picker entry at all.</summary>
    internal sealed class ManagerScan
    {
        internal string ManagerName, RigName, RootMotionNode, ResourcePath;
        internal string RepresentativeCharacter;      // a TacCharacterDef name - what the bay rebuild needs
        internal string BodyStateDef, AnimActionsDef, ControllerName;
        internal List<string> SlotNames = new List<string>();
        internal List<string> ClipNames = new List<string>();  // already deduplicated by the harvester
        internal bool HasRig;
    }

    internal sealed class PrototypeSlot
    {
        internal string SlotDefName, AttachmentPointName;
        internal List<string> RepresentativeAddons = new List<string>();
    }

    internal sealed class PrototypeVariant
    {
        internal string Name, ManagerName, RepresentativeCharacter, BodyStateDef;
        internal string AnimActionsDef, ControllerName;
        internal List<PrototypeSlot> Slots = new List<PrototypeSlot>();
        /// <summary>Resolved clip catalogue: the anim-actions def's clips, or - when that is EMPTY,
        /// which is the shipped state of Crabman_AnimActionsDef - the controller's own
        /// animationClips, deduplicated by name. Slice 0(d).</summary>
        internal List<string> Clips = new List<string>();
    }

    internal sealed class PrototypeRecord
    {
        internal string Id, DisplayName, Category;
        internal List<string> SearchTerms = new List<string>();
        internal List<string> RigPrefabNames = new List<string>();   // 2 only for the worm prototype
        internal List<PrototypeBone> Bones = new List<PrototypeBone>();
        internal List<string> BindableBones = new List<string>();
        internal List<string> AttachmentPoints = new List<string>();
        internal List<string> AmbiguousNames = new List<string>();
        internal List<PrototypeVariant> Variants = new List<PrototypeVariant>();
        internal string Warning;    // set when AmbiguousNames is non-empty; never blocks by itself
    }

    internal static class PrototypeCatalog
    {
        /// <summary>Addon.GetEquivalentBones skips every transform whose name starts with this
        /// (Addon.cs:1208), so it is the line between what can bind and what cannot.</summary>
        internal const string AttachmentPrefix = "EXT_";

        internal static bool IsAttachmentPoint(string boneName);

        /// <summary>Bone names that can actually take a skin weight, ORDINAL-sorted and deduplicated.</summary>
        internal static IList<string> Bindable(IList<PrototypeBone> bones);

        /// <summary>The EXT_* names. Informational on the Extend path, REQUIRED on the Replace path
        /// when the live SMR references one - see the design's section 3.</summary>
        internal static IList<string> AttachmentPoints(IList<PrototypeBone> bones);

        /// <summary>Names appearing more than once anywhere under the rig. The game resolves by name
        /// plus FirstOrDefault, so the second one is unreachable and must never be index-matched.</summary>
        internal static IList<string> Ambiguous(IList<PrototypeBone> bones);

        /// <summary>THE MERGE KEY: the ordinal-sorted Bindable() set, '\n'-joined. Two rigs are one
        /// prototype if and only if this string is equal. Not the prefab, not the slots, not the
        /// animation - slice 0 measured all three lying in both directions.</summary>
        internal static string Signature(IList<PrototypeBone> bones);

        /// <summary>Group rigs by Signature, attach every manager that uses one of the grouped
        /// prefabs as a VARIANT, and drop managers with HasRig == false.</summary>
        internal static IList<PrototypeRecord> Build(IList<RigScan> rigs, IList<ManagerScan> managers);

        /// <summary>Case-insensitive token-AND over SearchTerms: every whitespace-delimited token must
        /// match somewhere. Empty query returns everything.</summary>
        internal static IList<PrototypeRecord> Search(IList<PrototypeRecord> all, string query);

        /// <summary>The 8 navigation groups, keyed off ResourcePath's faction folder plus the manager
        /// name. Navigation only - it never merges or splits anything.</summary>
        internal static string CategoryOf(string resourcePath, string managerName);
    }
```

- [ ] **Step 4: Run and expect PASS.** `dotnet run --project tests\ObjCodecTests -c Release`.
  Expected: `CATALOG PASS, 24 check(s) - 37 rigs, 36 binding prototypes, off the live census`, exit 0,
  and every pre-existing gate (`OBJ: ALL PASS`, `BINDER-FROZEN PASS`, `DECISION PASS`, `ALIAS PASS`,
  `PREFLIGHT PASS`, `GLB-SLIM`, …) unchanged. If the count differs from 36, the merge rule is wrong —
  print the offending signatures and fix `Signature`, never the assertion.

- [ ] **Step 5: Build.** `dotnet build -c Release`. Expected: `Build succeeded`, 0 errors.

- [ ] **Step 6: Commit.**

```
git add src/Doctor/PrototypeCatalog.cs tests/ObjCodecTests/CatalogTests.cs tests/ObjCodecTests/ObjCodecTests.csproj tests/ObjCodecTests/Program.cs
git commit -m "feat(doctor): one prototype per bone-binding signature, 37 rigs into 36"
```

---

### Task 2: `PrototypeTarget` and the Extend bone policy

**Files:**
- Create: `src\Doctor\PrototypeTarget.cs`
- Modify: `src\Import\SkinCompatibility.cs:23` (`BindCode`), `:91-102` (`Analyze` overloads),
  `:196-236` (the bone loops), `tests\ObjCodecTests\ObjCodecTests.csproj`,
  `tests\ObjCodecTests\BinderFrozen.cs`
- Test: `tests\ObjCodecTests\BinderFrozen.cs` (extended — it is the oracle that Replace did not move)

- [ ] **Step 1: Add the failing checks.** In `tests\ObjCodecTests\BinderFrozen.cs`, immediately before
  the `return "BINDER-FROZEN PASS…"` line, add:

```csharp
        // ---- EXTEND: a partial body part is legitimate, so a rig bone the file does not use is not
        // a defect. Nothing else moves - an added bone is still an added bone.
        IList<BindingIssue> ext = SkinCompatibility.Analyze(Model(new[] { "Root" }),
                                                            new[] { "Root", "Neck" }, 0, true);
        checks += Check(ext.Count == 0, "Extend accepts a strict subset of the rig: " + Codes(ext));
        IList<BindingIssue> extAdds = SkinCompatibility.Analyze(Model(new[] { "Root", "Hand" }),
                                                                new[] { "Root" }, 0, true);
        checks += Check(extAdds.Count == 1 && extAdds[0].Code == BindCode.ExtraBone,
                        "Extend still refuses a bone the rig does not have: " + Codes(extAdds));
        // ---- and REPLACE is byte-identical to what it was before this task.
        IList<BindingIssue> rep = SkinCompatibility.Analyze(Model(new[] { "Root" }),
                                                            new[] { "Root", "Neck" }, 0, false);
        checks += Check(rep.Count == 1 && rep[0].Code == BindCode.MissingBone,
                        "Replace still requires every live bone: " + Codes(rep));

        // ---- EXT_* joints in the FILE. The game skips them (Addon.cs:1208), so a weighted one
        // loses its weights silently and an unweighted one is only noise.
        SkinnedModel weighted = Model(new[] { "Root", "EXT_Grip" });
        IList<BindingIssue> hot = SkinCompatibility.Analyze(weighted, new[] { "Root" }, 0, true);
        checks += Check(Has(hot, BindCode.ExtJointWeighted),
                        "a WEIGHTED EXT_ joint is reported: " + Codes(hot));
        SkinnedModel cold = Model(new[] { "Root", "EXT_Grip" });
        for (int i = 0; i < cold.Weights.Length; i++) if (cold.Joints[i] == 1) cold.Weights[i] = 0f;
        IList<BindingIssue> mild = SkinCompatibility.Analyze(cold, new[] { "Root" }, 0, true);
        checks += Check(Has(mild, BindCode.ExtJointUnused) && !Has(mild, BindCode.ExtJointWeighted),
                        "an UNWEIGHTED EXT_ joint is only noted: " + Codes(mild));
```

  and add the two helpers next to `Check`:

```csharp
    private static bool Has(IList<BindingIssue> issues, BindCode code)
    {
        for (int i = 0; i < issues.Count; i++) if (issues[i].Code == code) return true;
        return false;
    }

    private static string Codes(IList<BindingIssue> issues)
    {
        var parts = new List<string>();
        foreach (BindingIssue i in issues) parts.Add(i.Code.ToString());
        return parts.Count == 0 ? "(none)" : string.Join(",", parts.ToArray());
    }
```

- [ ] **Step 2: Run and watch it fail to compile.** `dotnet run --project tests\ObjCodecTests -c Release`.
  Expected: `error CS1501: No overload for method 'Analyze' takes 4 arguments` and
  `error CS0117: 'BindCode' does not contain a definition for 'ExtJointWeighted'`.

- [ ] **Step 3: Teach `Analyze` the Extend mode.** In `src\Import\SkinCompatibility.cs`:

  a. Append two members to the `BindCode` enum (`:23`), after `BoneIndexOutOfRange`:
     `ExtJointWeighted, ExtJointUnused`. **Append only** — the enum is persisted in reports.

  b. Add a 4-argument overload beside the existing ones (`:91-102`) and thread the flag into the full
     signature, keeping the existing 2- and 3-argument entry points binding to `extend: false` so the
     bake path cannot change:

```csharp
        internal static IList<BindingIssue> Analyze(SkinnedModel file, IList<string> boneNames,
                                                    int expectedShapes, bool extend);
        internal static IList<BindingIssue> Analyze(SkinnedModel file, IList<string> boneNames,
                                                    int expectedShapes, bool extend,
                                                    out int[] liveOf, out int[] fileOf);
```

  c. In the live-bone loop (`:196-219`), when `extend` is true, take the `continue` **without** adding
     the `MissingBone` issue. The `ExtraBone` loop (`:220-225`) is untouched in both modes.

  d. Skip the `NotBijective` check (`:231-236`) when `extend` is true — with `MissingBone` suppressed,
     `toFile` legitimately holds `-1`, and indexing with it is the crash the binder avoids.
     `InverseBindCount` and `BoneIndexOutOfRange` still run in both modes.

  e. Add one loop over `file.JointNames` that fires only when `extend` is true: for a joint whose name
     starts with `PrototypeCatalog.AttachmentPrefix`, emit `ExtJointWeighted` when any
     `file.Weights[i] > 0f` at a slot where `file.Joints[i]` is that joint, otherwise `ExtJointUnused`.
     Sentences, verbatim:

```
ExtJointWeighted: "the file weights vertices to '<name>', and the game SKIPS every bone whose name
starts with EXT_ (it is an attachment point, not a skin joint) - so those weights would be lost
silently; in Blender move that influence onto a real bone and re-export"

ExtJointUnused:   "the file carries the attachment point '<name>', which the game does not bind to;
nothing is lost, but it will not follow the rig either"
```

  f. `src\Import\SkinCompatibility.cs` must NOT gain a `using UnityEngine` — reference
     `Morgott.ContentTool.Doctor.PrototypeCatalog.AttachmentPrefix` (both files are UnityEngine-free
     and both are linked into the test project).

- [ ] **Step 4: Write `PrototypeTarget`.** Create `src\Doctor\PrototypeTarget.cs`, namespace
  `Morgott.ContentTool.Doctor`, no `using UnityEngine`:

```csharp
    internal enum VerifyMode
    {
        /// <summary>Exact: the file must reproduce one LIVE slot SkinnedMeshRenderer's bone list.</summary>
        Replace,
        /// <summary>Subset: the file's joints must map uniquely onto the prototype's BindableBones.</summary>
        Extend
    }

    internal sealed class PrototypeTarget
    {
        internal PrototypeRecord Record;
        internal PrototypeVariant Variant;
        internal string SlotDefName;
        internal VerifyMode Mode;

        /// <summary>Replace ONLY: the snapshot of the live slot renderer the bay rebuild produced.
        /// Null on the Extend path, and null when the slot has no renderer.</summary>
        internal RigTarget Live;

        /// <summary>Non-null when this slot produced no renderer - the row reads
        /// "slot visual unavailable" and Replace is refused for it. Extend still works.</summary>
        internal string Unavailable;

        /// <summary>The names Analyze is run against: Live.BoneNames on Replace,
        /// Record.BindableBones on Extend.</summary>
        internal IList<string> BoneNames();

        /// <summary>THE DUPLICATE RULE. Returns the ambiguous names that the given referenced set
        /// actually touches - and ONLY those block a verdict. An ambiguous name nothing references is
        /// a Record.Warning, so Fishman's two wrist pairs and the vehicles' 'light' nodes never make
        /// unrelated slots unusable. NEVER disambiguate by index: the game resolves by name plus
        /// FirstOrDefault (Addon.cs:1202-1231), so the second one is unreachable.</summary>
        internal IList<string> BlockingAmbiguous(IList<string> referenced);
    }
```

- [ ] **Step 5: Register and run.** In `tests\ObjCodecTests\ObjCodecTests.csproj`, immediately after
  the `PrototypeCatalog.cs` line added in task 1, add:

```xml
    <Compile Include="..\..\src\Doctor\PrototypeTarget.cs" Link="PrototypeTarget.cs" />
```

  `dotnet run --project tests\ObjCodecTests -c Release`. Expected: `BINDER-FROZEN PASS, 29 check(s)`,
  `CATALOG PASS` unchanged, `PREFLIGHT PASS` unchanged, `BONE-NAMES PASS, 6 check(s)` unchanged
  (that gate asserts the same sentences through `Bind`, which still calls `extend: false`), exit 0.

- [ ] **Step 6: Build.** `dotnet build -c Release`. Expected: `Build succeeded`, 0 errors.

- [ ] **Step 7: Commit.**

```
git add src/Doctor/PrototypeTarget.cs src/Import/SkinCompatibility.cs tests/ObjCodecTests/BinderFrozen.cs tests/ObjCodecTests/ObjCodecTests.csproj
git commit -m "feat(doctor): Extend matches bindable bones only, and EXT_ joints are reported"
```

---

### Task 3: `PrototypeBaySession` — the bay transaction

The one place that mutates the game. It uses the **existing** squad-bay `AddonsCharacterBuilder`;
a hidden builder buys nothing (slice 0(b): a rig outside a loaded level yields **0** renderers) and
duplicates the coroutine lifecycle `FitBench` already handles.

**Files:**
- Create: `src\Dev\PrototypeBaySession.cs`
- Modify: `src\Dev\FitBench.cs` (`Open` ~`:357-420`, the close step list ~`:936`, `Posed` ~`:1130`)
- Test: build only (Unity-side); behaviour is accepted in task 7

- [ ] **Step 1: Re-read `FitBench.cs` fresh.** `git -C E:\DEV\PhoenixPoint\ContentTool log --oneline -5`
  then read `src\Dev\FitBench.cs`. Confirm by eye, and write the CURRENT line numbers into your notes:
  the `OnCharacterRebuilded += Posed` subscription (was `:418`), its `-= Posed` restore step (was
  `:936`), `Show()` (was `:1038`) with `DisplayCharacter` (was `:1056`) and `RebuildCharacter` (was
  `:1070`), and `Posed()` (was `:1130`) with `doctor.Root = bay.CharacterBuilder.transform` (was
  `:1140`). The viewport-controls plan is editing this file concurrently.

- [ ] **Step 2: Write the session.** Create `src\Dev\PrototypeBaySession.cs`, namespace
  `Morgott.ContentTool.Dev`:

```csharp
    /// <summary>
    /// ONE TRANSACTION AGAINST THE SQUAD BAY. Showing a prototype means rebuilding the bay's own
    /// AddonsCharacterBuilder as somebody else - so the soldier the player was looking at has to come
    /// back, exactly, whatever happens in between.
    ///
    /// The native sequence this mirrors is UIModuleActorCycle.DisplaySoldier:602-615 -
    /// CommonCharacterUtils.DisplayCharacter (CommonCharacterUtils.cs:25-52), then autorefresh off,
    /// then GameTags.Clear + AddRange, then RebuildCharacter (:54-64). FitBench.Show already performs
    /// it for a TacCharacterDef; this class adds the SNAPSHOT and the RESTORE around it.
    ///
    /// The rebuild is a COROUTINE (AddonsCharacterBuilder.StartRebuildCharacter:176, event raised at
    /// :293), so nothing here may read renderers on return - Rebuilt() is the only place they exist.
    /// </summary>
    internal sealed class PrototypeBaySession : IDisposable
    {
        internal PrototypeBaySession(AddonsCharacterBuilder builder, UIModuleActorCycle cycle,
                                     SharedData shared);

        /// <summary>A rebuild is in flight. Show() refuses while true - two overlapping rebuilds leave
        /// the bay showing a mix of two prototypes and neither slot list is trustworthy.</summary>
        internal bool Busy { get; }

        /// <summary>True once the ORIGINAL unit has been captured; a session that never captured must
        /// never restore, because it would DisplaySoldier a null.</summary>
        internal bool Captured { get; }

        /// <summary>Snapshot the bay on first use, then show this variant. Returns null on success or
        /// a one-line reason. Captures, in this order and BEFORE the first mutation:
        ///   UIModuleActorCycle.CurrentUnit  (UIModuleActorCycle.cs:174, a UnitDisplayData),
        ///   builder.AddonsManagerDef, a copy of builder.Addons, a copy of AddonsManager.GameTags.
        /// NEVER calls GeoCharacter.SetItems, SaveLoadout, or touches a template: presentation only.</summary>
        internal string Show(TacCharacterDef representative, System.Collections.Generic.List<ItemDef> bodyparts,
                             ItemDef weapon);

        /// <summary>Called from FitBench.Posed, i.e. on OnCharacterRebuilded. Clears Busy and makes
        /// Slots() answerable.</summary>
        internal void Rebuilt();

        /// <summary>The real slot renderers this rebuild produced - builder.GetComponentsInChildren
        /// &lt;SkinnedMeshRenderer&gt;(true). Empty until Rebuilt() has run at least once.</summary>
        internal SkinnedMeshRenderer[] Slots();

        /// <summary>Put the captured unit back: DisplaySoldier(captured, resetAnimation: false,
        /// addWeapon: true) - UIModuleActorCycle.cs:602. Waits out or invalidates an in-flight rebuild
        /// first. Returns null on success or a reason. Safe to call twice.</summary>
        internal string Restore();

        /// <summary>Restore() unless the level is already gone, then drop every reference. The bench's
        /// StillThere() is the test for "gone": restoring into a dead bay is a fistful of references
        /// to things about to die.</summary>
        public void Dispose();
    }
```

- [ ] **Step 3: Hook it into the bench.** In `src\Dev\FitBench.cs`:
  - add `private static PrototypeBaySession proto;` beside the other static UI fields;
  - in `Open`, after the existing `bay.CharacterBuilder.OnCharacterRebuilded += Posed;`, construct it
    from `bay.CharacterBuilder`, the bay's `UIModuleActorCycle` and
    `GameUtl.GameComponent<SharedData>()` — the same component `Show` already passes to
    `UnitDisplayData` (was `FitBench.cs:1049`);
  - in `Posed`, immediately **after** `doctor.Root = bay.CharacterBuilder.transform;`, call
    `if (proto != null) proto.Rebuilt();` — before `Handed`/`FitAnim.Bind`, so the Doctor sees the
    slots in the same frame the rest of the pose is applied;
  - in the close path, add a `Step(failed, "the squad bay's own soldier", () => { … })` that calls
    `proto.Dispose()` and nulls the field, placed **before** the existing
    `bay.CharacterBuilder.OnCharacterRebuilded -= Posed;` step so the restore's rebuild still gets
    its callback.

  Do not touch the camera: framing stays `Reframe()` inside `Posed`, driven by `OrbitCamera`.

- [ ] **Step 4: Build.** `dotnet build -c Release`. Expected: `Build succeeded`, 0 errors.

- [ ] **Step 5: Run the offline suite to prove nothing regressed.**
  `dotnet run --project tests\ObjCodecTests -c Release` — expected exit 0, every gate as in task 2.
  `PrototypeBaySession` is Unity-side and contributes no gate.

- [ ] **Step 6: Commit.**

```
git add src/Dev/PrototypeBaySession.cs src/Dev/FitBench.cs
git commit -m "feat(dev): one bay transaction for prototype previews, and the soldier comes back"
```

---

### Task 4: Representative instantiation — every shipped rig can be shown

**Files:**
- Modify: `src\Dev\FitBench.cs` (harvest + drive), `src\Dev\PrototypeBaySession.cs` (if the harvest
  needs a seam)
- Test: build + offline suite; the matrix itself is task 7

- [ ] **Step 1: Harvest the scan from `DefRepository`.** Add a private static method to `FitBench` (or
  a `Harvest` helper beside it) that builds `IList<RigScan>` + `IList<ManagerScan>` and calls
  `PrototypeCatalog.Build`. Ground every call:
  - `GameUtl.GameComponent<DefRepository>().GetAllDefs<TacCharacterDef>()` — the pattern already in
    `FitBench.cs:580-593`;
  - `TacCharacterDef.GetAddonsMangerDef()` (`TacCharacterDef.cs:172-175`) → the manager;
  - `AddonsManagerDef.Rig` (`AddonsManagerDef.cs:12`) — a DIRECT `GameObject`, not an Addressable;
    a null `Rig` means `HasRig = false` and the manager is dropped;
  - bones: `Rig.GetComponentsInChildren<Transform>(true)` — **DFS preorder**, which is how the census
    reconstructed parents from `childCount` (taxonomy "How it was measured"); use `Transform.parent`
    directly here, it is free in-process;
  - slots: `TacCharacterDef.GetTemplateBodyparts` (`TacCharacterDef.cs:192-204`) and
    `CharacterBodyStateDef.BodyPartsDefs` (`CharacterBodyStateDef.cs:10-12`);
  - clips: the variant's `TacActorAnimActionsDef` (`TacCharacterDef.GetAnimActionDef()`,
    `TacCharacterDef.cs:187-190`); **when its `AnimActions` is empty**, fall back to
    `AddonsManagerDef.Rig.GetComponent<Animator>().runtimeAnimatorController.animationClips`
    (the very controller `CommonCharacterUtils.cs:42-43` copies onto the live rig) and **deduplicate
    by name** — `HumanoidAnimatorLOC` lists 73 entries for 69 distinct names.

- [ ] **Step 2: Build lazily, once, on first bench open.** The harvest runs the first time the
  prototype browser is opened, not from `Open` and never at menu time. Keep a `Rescan` entry point for
  the Advanced row. Cache the result in a static; invalidate it on `Rescan` only.

- [ ] **Step 3: Drive the session from a selection.** Selecting a variant calls
  `proto.Show(variant.RepresentativeCharacter's def, bodyparts, weapon: null)`; when `Rebuilt()` lands,
  match `proto.Slots()` to `variant.Slots` by renderer transform path and fill each
  `PrototypeTarget.Live` via the existing `ModelDoctor.Snapshot(smr, transformPath)`
  (`src\Dev\ModelDoctor.cs:196`). A slot with no matching renderer gets
  `Unavailable = "slot visual unavailable"` and no `Live`.

- [ ] **Step 4: Build.** `dotnet build -c Release`. Expected: `Build succeeded`, 0 errors.

- [ ] **Step 5: Run the offline suite.** `dotnet run --project tests\ObjCodecTests -c Release` — exit 0,
  unchanged.

- [ ] **Step 6: Commit.**

```
git add src/Dev/FitBench.cs src/Dev/PrototypeBaySession.cs
git commit -m "feat(dev): harvest the prototype catalog from DefRepository on first bench open"
```

---

### Task 5: The picker UI

**Files:**
- Modify: `src\Dev\ModelDoctor.cs:558` (the `Targets()` call site), `:616-648` (`Targets` itself),
  `:101-110` (`PickTarget`)
- Test: build + offline suite; visual acceptance is task 7

- [ ] **Step 1: Replace the renderer list with the browser.** `Targets()` today is
  `candidates = targetsOpen && Root != null ? Root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
  : new SkinnedMeshRenderer[0];` (`ModelDoctor.cs:622-623`) — the list tied to whatever actor the bench
  posed. Replace it with a full-area browser (it temporarily replaces the whole content area, not an
  inline expansion) drawing: collapsible **category** groups → **prototype** rows (display name,
  bindable-bone count, variant count, ambiguity warning badge) → **variant** rows → **slot** rows with
  a Replace/Extend toggle.

- [ ] **Step 2: Search.** One `GUILayout.TextField`, reactive on every keystroke, feeding
  `PrototypeCatalog.Search`. Matching groups auto-expand; clearing the box restores the previous
  collapse state. Recompute the filtered rows only when the text changed.

- [ ] **Step 3: `PickTarget` takes a `PrototypeTarget`.** Keep the existing
  `PickTarget(SkinnedMeshRenderer, string)` (`:101`) as the seam the bay session calls, and add
  `PickTarget(PrototypeTarget)` that stores the target and bumps `gen`. **The renderer list must no
  longer be derived from `Root`**: `Root`'s setter (`:498-525`) still invalidates a preview when the
  benched actor dies, which stays correct, but a prototype selection is no longer a function of who is
  on the stand.

- [ ] **Step 4: Build.** `dotnet build -c Release`. Expected: `Build succeeded`, 0 errors.

- [ ] **Step 5: Run the offline suite.** `dotnet run --project tests\ObjCodecTests -c Release` — exit 0,
  unchanged.

- [ ] **Step 6: Commit.**

```
git add src/Dev/ModelDoctor.cs
git commit -m "feat(doctor): a prototype browser replaces the benched actor's renderer list"
```

---

### Task 6: Replace / Extend wiring through the preflight

**Files:**
- Modify: `src\Doctor\ReplacementPreflight.cs:37` (`Run`), `:111` (`Judge`), `:188` (`Remedy.For`),
  `src\Dev\ModelDoctor.cs` (call site), `tests\ObjCodecTests\PreflightTests.cs`
- Test: `tests\ObjCodecTests\PreflightTests.cs`

- [ ] **Step 1: Add the failing checks.** In `tests\ObjCodecTests\PreflightTests.cs`, inside the `try`
  block immediately before its closing brace, add cases over the committed `lib\u9_probe.glb`:

  1. `ReplacementPreflight.Run(bytes, path, ExtendTarget(probeJointNames))` — a `PrototypeTarget` whose
     `Mode = VerifyMode.Extend` and whose `BindableBones` is the probe's own joint set **plus** two
     extra names — comes back with **no** `MissingBone` row;
  2. the same target in `VerifyMode.Replace` **does** produce `MissingBone` rows for those two;
  3. a target whose `BindableBones` contains a name the probe does not have and whose `Live` is null
     still returns a REPORT, never an exception;
  4. `ExtJointWeighted` maps to `Severity.Blocking` and `ExtJointUnused` to `Severity.Warning` in the
     rendered report;
  5. a `PrototypeTarget` with `Unavailable != null` and `Mode = Replace` returns
     `Outcome.Refused` with a row naming "slot visual unavailable", and the SAME target in
     `Mode = Extend` still produces a normal verdict.

  Bump the return string's expected count.

- [ ] **Step 2: Run and watch it fail.** `dotnet run --project tests\ObjCodecTests -c Release`.
  Expected: a compile error on the new `Run` overload, or a `PREFLIGHT FAILURE` naming the missing
  suppression.

- [ ] **Step 3: Wire it.** In `src\Doctor\ReplacementPreflight.cs`:
  - add `internal static ReplacementPreflightResult Run(byte[] bytes, string path, PrototypeTarget target)`
    beside the existing `Run(byte[], string, RigTarget)` (`:37`), which stays for the bake-side and the
    existing gates. It resolves `target.BoneNames()` and passes
    `extend: target.Mode == VerifyMode.Extend` into `SkinCompatibility.Analyze`;
  - `Unavailable != null` on the Replace path short-circuits to `Outcome.Refused` with one row —
    never fabricate a target from the full hierarchy;
  - `Judge` (`:111`) maps `ExtJointWeighted` → `Severity.Blocking`, `ExtJointUnused` →
    `Severity.Warning`;
  - `Remedy.For(BindCode)` (`:188`) gains a sentence for each of the two new codes, in the existing
    "in Blender, do X" voice.

- [ ] **Step 4: Point the Doctor at it.** In `src\Dev\ModelDoctor.cs`, the worker job calls the
  `PrototypeTarget` overload when a prototype is selected and the `RigTarget` overload otherwise, so a
  session that never opened the browser behaves exactly as it does today.

- [ ] **Step 5: Run and expect PASS.** `dotnet run --project tests\ObjCodecTests -c Release`. Expected:
  `PREFLIGHT PASS, 19 check(s) - lib\u9_probe.glb through the real pipeline`, `CATALOG PASS`,
  `BINDER-FROZEN PASS, 29 check(s)`, `DECISION PASS, 9 check(s)` and every other gate unchanged, exit 0.

- [ ] **Step 6: Build.** `dotnet build -c Release`. Expected: `Build succeeded`, 0 errors.

- [ ] **Step 7: Commit.**

```
git add src/Doctor/ReplacementPreflight.cs src/Dev/ModelDoctor.cs tests/ObjCodecTests/PreflightTests.cs
git commit -m "feat(doctor): the preflight verifies against a prototype target, Replace or Extend"
```

---

### Task 7: Live matrix on `D:\PP-Instance2` through PPCLI

Evidence from a real run only. **Never target `D:\Steam\steamapps\common\Phoenix Point`** — that is
the user's own game; do not cold-launch it, do not kill a process there.

**Files:**
- Modify: `internal-docs\planning\2026-09-02-prototype-picker-design.md` (a short
  `## 12. Acceptance run` section at the end)
- Test: the running game

- [ ] **Step 1: Read the playbook first.** Read `E:\DEV\PhoenixPoint\PPCLI\PLAYBOOK.md` and take the
  exact command lines for `plan`, `connect console`, `connect call` and `connect screenshot`. Do not
  dig PPCLI source, do not invent a command line. If PPCLI itself misbehaves, append an entry to
  `E:\DEV\PhoenixPoint\PPCLI\ISSUES.md` (attempted → happened → expected → evidence → severity) and
  work around it — never edit PPCLI source, never commit into that repo.

- [ ] **Step 2: Deploy this build to the automation install.** `dotnet build -c Release`, then copy
  `bin\Release\ContentTool\` into `D:\PP-Instance2`'s `Mods\ContentTool\` with the repo's own
  `deploy.ps1` (read it first for its parameters). Expected: the copied `ContentTool.dll` timestamp
  matches the build you just made.

- [ ] **Step 3: Reach a geoscape.** The bench refuses outside a playing geoscape level with a squad bay
  (`FitBench.cs:364-379`), so a HomeScreen is not enough:

```powershell
cd E:\DEV\PhoenixPoint\PPCLI
.\ppcli.ps1 connect state                       # gate FIRST - wait until it actually answers
.\ppcli.ps1 plan .\plans\start-campaign.json    # a NEW playable campaign from the main menu, ~15 s
.\ppcli.ps1 connect state                       # gate again before anything else
```

- [ ] **Step 4: Open the bench and record the baseline.** `.\ppcli.ps1 connect console
  '{"command":"ct_bench","args":[]}'`, then `.\ppcli.ps1 connect screenshot`. Expected: a PNG whose
  path the reply names, showing the bench with the `FIT` / `MODEL DOCTOR` toggles. **Write down which
  squad member and which loadout the bay is showing** — that is the thing step 6 has to bring back.

- [ ] **Step 5: Drive the picker by reflection and screenshot each case.** The IMGUI panel has no
  scriptable surface, so drive it the way `2026-09-01-model-doctor-plan.md` task 13 drove the Doctor:
  `connect call` with `op:"invoke"` / `op:"set"` against the static `ModelDoctor` / `FitBench` members,
  reading the panel's state back with `op:"get"`. Six cases, `connect screenshot` after each, and after
  each one assert `proto.Busy == false` and the slot list is non-empty (or explicitly
  "slot visual unavailable"):

  | # | Prototype → variant → slot | Expected |
  |---|---|---|
  | 1 | Human → Soldier → Head | a `RigTarget` whose bone names are a SUBSET of the 124-transform rig (the in-mission control measured a head at 21) |
  | 2 | Crabman → Gunner → Torso | a DIFFERENT `RigTarget`; no name overlap with case 1 beyond the three `EXT_*` |
  | 3 | Fishman → any → a wrist-adjacent slot | case-variant wrists (`_l`/`_L`) both bind — no ambiguity; ambiguity example = vehicle `light` (case 5) |
  | 4 | the worm prototype → all three variants | one prototype, two `RigPrefabNames`, three variants; all three rebuild |
  | 5 | a vehicle (`NJ_Armadillo`) → Turret | rebuilds; duplicate `light` is a warning, not a block |
  | 6 | a static structure (`EggFacehugger`) | rebuilds or reports "slot visual unavailable" — never a fabricated target |

- [ ] **Step 6: Close and prove the bay came back.** `.\ppcli.ps1 connect console
  '{"command":"ct_bench","args":["close"]}'`, then `connect screenshot`. **Expected: the SAME squad
  member with the SAME loadout as the step-4 baseline is on the platform.** Then grep
  `%USERPROFILE%\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\Player.log` for `Exception` and
  confirm no new entries appeared across the whole run.

- [ ] **Step 7: Record the run.** Append a `## 12. Acceptance run` section to
  `internal-docs\planning\2026-09-02-prototype-picker-design.md`: date, install, build stamp, the six
  screenshot paths, the observed bone counts per case, the step-6 baseline comparison, and the
  `Player.log` result. Evidence from the real run only — no predicted values.

- [ ] **Step 8: Commit.**

```
git add internal-docs/planning/2026-09-02-prototype-picker-design.md
git commit -m "docs(planning): prototype picker acceptance run on Instance2"
```

- [ ] **Step 9: Hand off to the owner for the visual check.** Report to the user: the six screenshot
  paths, which prototypes rendered, the step-6 restore result, and that slice 1 is awaiting his visual
  in-game check before a release is cut. Do not cut a release.

---

## Task 7 acceptance run - 2026-09-02, D:\PP-Instance2

Real run only. Install `D:\PP-Instance2` (never the user's own game), ContentTool `1.1.3.0`
`build=c0869416`, PPBridge `build=2d9f4a41`, deployed with the repo's own `.\deploy.ps1` (its
`-PPRoot` already defaults to `D:\PP-Instance2`). Geoscape reached with
`.\ppcli.ps1 plan .\plans\start-campaign.json '{"difficultyIndex":1}'`, bench opened with
`.\ppcli.ps1 connect console '{"command":"ct_bench","args":["open"]}'`. IMGUI cannot be clicked
through PPCLI, so the browser was driven by reflection on the very seams its rows call -
`FitBench.Prototypes` / `PrototypeCatalog.Search` / `FitBench.ShowPrototype` / `FitBench.SlotTargets`
/ `FitBench.PrototypeBusy`, and `ModelDoctor.PickTarget(PrototypeTarget)` / `PickFile` through
`AccessTools.Field(typeof(FitBench),"doctor")`. Every step ran as a `connect plan` with an inline
plan file under the scratchpad (never inside the PPCLI repo).

**The census line, verbatim, on its first harvest:**

```
ct_bench prototypes: 46 manager(s) [census 46], 42 with a rig [42], 37 distinct rig(s) [37], 2551 transform(s) [2551], 36 binding prototype(s) [36] - MATCHES rig-census-2026-09-02.json.
```

`FitBench.Prototypes()` returned `List<PrototypeRecord>` with `count: 36`.

### The matrix

| # | Action | Expected | Observed | Verdict |
|---|---|---|---|---|
| 1 | Human -> Human -> slots | rebuild, real SMRs, bone names a subset of the 124-transform rig | 110 bindable + 14 `EXT_` = **124** (the census `count` for `CHR_Human_Rig_Ready`), 1 rig prefab, 1 variant, `Busy` false, **9** slot targets, 6 with a live SMR: Head 1, LeftArm 26, LeftLeg 11, RightArm 26, RightLeg 11, Torso 25 bone(s); FacialHair / Hair / Legs = `slot visual unavailable` | PASS |
| 2 | Crabman -> Crabman -> Torso | a DIFFERENT RigTarget | 53 bindable + 5 `EXT_`, **8** slots, **all 8 live**: Head 3, LeftArm 5, LeftHand 2, LeftLeg 5, RightArm 3, RightHand 4, RightLeg 5, Torso 7; meshes `Geo_Head*/Geo_Arm0*/Geo_Torso*` | PASS |
| 3 | Fishman -> Fishman -> wrist-adjacent slots | the case-variant wrists bind, no ambiguity | 138 bindable + 11 `EXT_`, 2 variants, **10** slots, 8 live including `Fishman_UpperLeftArms_SlotDef` and `Fishman_UpperRightArm_SlotDef`; `Record.Warning` null (no ambiguous name), matching the offline `CATALOG` check that `Ambiguous(Fishman)` is empty; `Fishman_Legs` / `Fishman_UpperArms` = `slot visual unavailable` | PASS |
| 4 | the worm -> all three variants | ONE record, 2 rig prefabs, 3 variants, all three rebuild | one record `Acidworm`, 11 bindable, **2** `RigPrefabNames`, **3** variants; `ShowPrototype` for indexes 0/1/2 asked `Acidworm` / `Fireworm` / `Poisonworm` and `ShownVariant()` answered the same name each time, 1 slot each; `Poisonworm_Torso_SlotDef` live, 9 bones, mesh `ALN_Fireworm…` (the shared rig), transport strip showing `Fireworm_idle_loop` | PASS |
| 5 | NJ_Armadillo -> Turret | rebuilds; duplicate `light` warns, never blocks | 50 bindable + 6 `EXT_`; `Record.Warning` = *"this rig carries the name(s) light more than once; the game matches by name and takes the first, so the others are unreachable - a slot is only blocked when it really references one of them"*; **12** slots, 6 live (`Vehicle_Top` 9 = the turret, `Vehicle_Front` 13, Back/Left/Right 2 each, `Armadillo_Engine_Upgrade` 1), the four wheels + front lights + hull upgrade `slot visual unavailable` | PASS |
| 6 | EggFacehugger (static) | rebuilds or says "slot visual unavailable" - never a fabricated target | 23 bindable + 4 `EXT_`, **1** slot `Egg_Facehugger_Body_SlotDef`, live, 10 bones | PASS |
| 7 | Replace verdict on a slot | a real verdict off the LIVE slot renderer | `PickTarget(Human_Torso_SlotDef target)` + `PickFile(lib\u9_probe.glb)` -> `Outcome.NearestBone`, **27** diagnostic rows, `Doctor.Target` non-null | PASS |
| 8 | Extend verdict on the rig | `MissingBone` suppressed, an added bone still refused | same target with `Mode = Extend` -> `Outcome.Refused`, **exactly 2** rows, both `ExtraBone` / `Blocking` (`'hip'`, `'head'` - u9_probe's own joints), **zero** `MissingBone` against a 110-bone rig, `Doctor.Target` null | PASS |
| 9 | close leaves the same squad member + loadout | the bay comes back exactly | before any prototype: `AddonsManagerDef` `ct_creature_morgott.demo.customcreature_AddonsManagerDef`, `Addons` 3, 1 `SkinnedMeshRenderer`. After 8 prototype rebuilds and `ct_bench close`: the same three values, unchanged | PASS |
| 10 | rapid select-and-close leaves no mixed rig | restore invalidates the in-flight rebuild | `ShowPrototype(Crabman)` then `ct_bench close` in the same plan, no wait between them: `FitBench.PrototypeBusy` **true** at the moment of the close (the rebuild really was in flight); afterwards manager / addons / renderers back to the captured triple and `PrototypeBusy` false | PASS |

`UIModuleActorCycle` is **not** reachable from a geoscape that never opened the roster screen
(`PrototypeBaySession.FindCycle` returned null on every read), so every restore above went through
the builder-state fallback (`UseAddonManager` + tags + `RebuildCharacter`) - the path the class
remark calls the `ponytail:` arm. It is the path that was exercised; the `DisplaySoldier` arm was not.

### `Player.log`

No ContentTool exception, and **0** occurrences of `Getting control … in a group with only …`.
The only entries in the whole run are third-party and pre-date the bench: TFTV's own
`TFTVRevenant+Resistance.GetPreferredDamageType` NRE / InvalidOperationException in a tactical level,
and 13 `ArgumentException: Mesh can not have more than 65000 vertices` from
`UnityEngine.UI.Text.UpdateGeometry` - TFTV's error popup growing its own text past the UGUI vertex
cap, first seen at boot before `ct_bench` was ever run.

### Environment: Renderforge relaunches the game into the D3D12 DEBUG layer

Not a ContentTool defect, but it cost this run three game sessions and is worth writing down.
`com.morgott.Renderforge` on Instance2 restarts the process with `-force-d3d12 -force-d3d12-debug`
about a minute into a session, whatever `Renderer`/`Upscaler`/`Mode` say in `ModConfig.json`. The
D3D12 debug layer then runs at ~1 FPS and raises `0x0000087d` from `D3D12SDKLayers`, which the crash
handler records as a crash (`C:\Temp\Snapshot Games Inc\Phoenix Point\Crashes\Crash_2026-09-02_105649977`)
- taking the campaign, the bench and every live handle with it. The run was finished by removing
`com.morgott.Renderforge` from `MOD_ACTIVATED` in the Instance2 profile for the duration and putting
it back afterwards. **Renaming its `meta.json` instead does NOT work**: an activated mod whose folder
no longer carries a `meta.json` stops PPModLoader enabling the mods listed after it, PPBridge
included, and the game boots with no endpoint at all.

### Screenshots

`C:\Temp\claude\E--DEV-PhoenixPoint-ContentTool\e31d205c-b842-452c-8655-3d543056001d\scratchpad\shots\`

`proto-baseline-01.png` (bench open, the bay's own unit), `proto-case1-human-02.png`,
`proto-case1-extend-verdict-03.png`, `proto-case2-crabman-04.png`, `proto-case3-fishman-05.png`,
`proto-armadillo.png`, `proto-facehugger.png`, `proto-case4-worm-v0.png` / `-v1.png` / `-v2.png`,
`proto-before-close-06.png` (the Poisonworm on the platform), `proto-after-close-07.png`
(the geoscape the bench came from), `proto-race-close-08.png`.

Slice 1 is awaiting the owner's own visual check in game; no release cut.
