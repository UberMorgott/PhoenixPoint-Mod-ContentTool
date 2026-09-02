# Prototype Visual Check Implementation Plan (slice 2)

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development

**Goal:** Ship slice 2 of `internal-docs\planning\2026-09-02-prototype-picker-design.md` — §7's
skeleton overlay and animation preview, and the §6 layout around them. After this slice an author
can stand a prototype on the platform (slice 1), **play its own clips** on the rig the imported mesh
is bound to, **see the skeleton drawn over the model coloured by whether each bone found a partner**,
**click a joint to read it**, and **click a joint to assign the alias** the bone map's armed row is
waiting for — and closing the bench still leaves the squad bay exactly as it was found.

**Architecture:** The same split slice 1 shipped and for the same reason. The **pure half**
(`src\Doctor\PrototypeCatalog.cs`, one new `src\Doctor\BoneOverlay.cs`) carries no `UnityEngine`
type: clip-list resolution (anim-actions def first, controller fallback, dedup by name) and
per-bone status classification plus the screen-space hit test are arithmetic and string work, so they
are decided by `tests\ObjCodecTests` before a game is ever launched. The **Unity half**
(`src\Dev\PrototypeHarvest.cs`, `src\Dev\PrototypeBaySession.cs`, `src\Dev\FitAnim.cs`,
`src\Dev\ModelDoctor.cs`, `src\Dev\FitBench.cs`) only fetches the objects those rules are applied to
and draws the result.

**Nothing new is built where something already exists.** Three things this slice would otherwise
invent are already shipped and are REUSED, not replaced:

| §7 / §10 asks for | Already shipped | Where |
|---|---|---|
| play / pause / scrub / speed / loop, clip list | the transport strip, entire | `FitAnim.Clips` (`src\Dev\FitAnim.cs:453`), `FitAnim.Controls` (`:488`), `FitAnim.List` (`:399`), `FitAnim.Tick` (`:329`) |
| a clip sampler that avoids controller-state names | `clip.SampleAnimation(rig, …)` with `animator.speed = 0` | `FitAnim.Tick` (`src\Dev\FitAnim.cs:344`) |
| world→screen projection + a picture that cannot disagree with the hit test | `Camera.WorldToScreenPoint` through one shared routine, unlit vertex-colour material probed once | `FitGizmo.Project` (`src\Dev\FitGizmo.cs:155`), `FitGizmo.Mat` (`:87`), `FitGizmo.Render` (`:278`) |
| §10's "viewport mouse controls standardized per section 8" | **DONE before this slice** — `OrbitCamera` with damped orbit/pan/zoom, `F`, `Home`, and `ViewGesture` deliberately carrying **no Pick member** so the LEFT button is free for this slice's overlay | `src\Dev\OrbitCamera.cs:9`, `:127`, `:149`; wired at `FitBench.cs:2116`, `:2138` |

**Tech Stack:** C# 9 / net472, Mono inside Unity 2019 (Phoenix Point). **No new dependencies.** UI is
IMGUI. Build: `dotnet build -c Release` from `E:\DEV\PhoenixPoint\ContentTool`. Offline tests: the
console EXE `tests\ObjCodecTests` (NOT `dotnet test`), run with
`dotnet run --project tests\ObjCodecTests -c Release`; every gate is a
`static class X { internal static string Run() }` that throws on failure and is called from
`Program.Main` (`tests\ObjCodecTests\Program.cs:106-130`).
`tests\ObjCodecTests\ObjCodecTests.csproj` sets `EnableDefaultCompileItems=false`, so **every new
file — test or linked `src` file — must be added to its `<Compile Include>` list**;
`ContentTool.csproj` globs `src\**\*.cs` and needs no edit. `src\Doctor\PrototypeCatalog.cs` and
`src\Doctor\PrototypeTarget.cs` are already linked into the test project (slice 1).

**Every line number below is a reading at HEAD `475cde8` and WILL drift.** Re-read each file fresh
before editing and match on the surrounding code, never on the number.

---

## Refusals — restated, because this is the slice that would break them

From §1 and §7, unchanged and non-negotiable:

- **No bone dragging.** The overlay's joints are readable and clickable. They are not draggable, and
  no code path in this slice writes a `Transform` position, rotation or scale.
- **No IK/FK controls, no rest-pose editing, no weight painting, no automatic retargeting.**
- The only thing a click may change is (a) which bone the inspector is showing and (b) one entry in
  the **existing** alias map (`ModelDoctor.aliases`, `src\Dev\ModelDoctor.cs:37`, written through
  `SetAlias` `:151` and saved by the existing `AliasMap` sidecar flow). **Click-to-alias must feed
  that map — never a new format, never a second file.**
- The preview drives an animator's **sample**, never a def, a clip asset or an override table. No
  game asset is mutated anywhere in this slice.

---

## Deviations from §7 as written, and why

Two §7 sentences describe a design that slice 0 measured to be impossible and slice 1 already
replaced. They are honoured by their shipped equivalents, not re-implemented:

1. **"Clone the selected rig and bind the imported mesh onto it"** (§7 MVP, and §10's "standalone rig
   instantiation"). A rig outside a loaded level yields **zero** `SkinnedMeshRenderer`s — slice 0(b),
   recorded in §1 — so a cloned rig cannot be previewed on. The shipped answer is
   `PrototypeBaySession` rebuilding the bay's own `AddonsCharacterBuilder` as the variant
   (`src\Dev\PrototypeBaySession.cs:110`) with `ModelDoctor.DoPreview` (`src\Dev\ModelDoctor.cs:400`)
   binding the imported mesh onto that renderer. This slice adds **no** second rig, hidden or
   otherwise.
2. **"Viewport mouse controls standardized per section 8"** (§10 slice-2 bullet). Landed already —
   `src\Dev\OrbitCamera.cs`. This plan changes no gesture; it only consumes the left button that
   `ViewGesture` was written to leave alone (`src\Dev\OrbitCamera.cs:5-9`).

---

## File Structure

**Created**

| Path | Responsibility |
|---|---|
| `src\Doctor\BoneOverlay.cs` | `BoneStatus`, `BoneOverlay.Classify`, `BoneOverlay.Nearest` — per-bone colour status and the screen-space pick. UnityEngine-free. |
| `tests\ObjCodecTests\OverlayTests.cs` | `OVERLAY` gate: clip resolution (task 1) + status classification and hit test (task 3). |

**Modified**

| Path | Change |
|---|---|
| `src\Doctor\PrototypeCatalog.cs:45-54` | `PrototypeVariant` gains `ClipSource`, `PreviewPoseClip`; new pure `PrototypeCatalog.ResolveClips`. |
| `src\Dev\PrototypeHarvest.cs:190` | `ReadClips` routes through `ResolveClips`; keeps the live `AnimationClip[]` per manager, exposed by a new `Clips(string managerName)`. |
| `src\Dev\PrototypeBaySession.cs:110-149` | Keep the `TacActorAnimActions` `DisplayCharacter` returns (today it is **discarded** at `:124`), exposed as `Actions`. |
| `src\Dev\FitAnim.cs:120`, `:163` | `Bind` takes a `fallback` clip array used when the anim-actions def yields nothing; `Catalogue` says which source answered. |
| `src\Dev\FitBench.cs:585`, `:1277`, `:1305-1335`, `:2223-2242` | Feed the prototype's actions + fallback clips to `FitAnim`; call the Doctor's overlay from `OnGUI`. |
| `src\Dev\ModelDoctor.cs:538-560`, `:606`, `:917` | `[Skeleton]` toggle, overlay draw + pick, selected-bone inspector, bone map auto-open, click-to-alias through `SetAlias`. |
| `src\Dev\FitGizmo.cs:87` | `Mat()` → `internal static Material Colored()`, so the overlay reuses the one probed material instead of probing a second. |
| `tests\ObjCodecTests\ObjCodecTests.csproj` | `<Compile Include>` for `BoneOverlay.cs` and `OverlayTests.cs`. |
| `tests\ObjCodecTests\Program.cs:122` | `Console.WriteLine(OverlayTests.Run());`. |

---

### Task 1: The clip catalogue per variant — anim-actions def, then the controller

Slice 0(d) measured the trap: `Crabman_AnimActionsDef` ships `AnimActions.Count == 0` with no
default action or reaction clip, while `Soldier_Utka_AnimActionsDef` has 177 — a preview reading only
`TacActorAnimActionsDef` shows an EMPTY list for Crabman. And controllers list duplicates:
`HumanoidAnimatorLOC` 73 entries / **69** distinct, `MidMonsterAnimator` 60 / **45**. The rule is
pure string work, so it is decided here, offline.

**Files:**
- Create: `tests\ObjCodecTests\OverlayTests.cs`
- Modify: `src\Doctor\PrototypeCatalog.cs:45-54` (`PrototypeVariant`), `src\Doctor\PrototypeCatalog.cs:189-200`
  (variant fill in `Build`), `src\Dev\PrototypeHarvest.cs:43-55`, `:123`, `:190-227`,
  `tests\ObjCodecTests\ObjCodecTests.csproj`, `tests\ObjCodecTests\Program.cs:122`
- Test: `tests\ObjCodecTests\OverlayTests.cs`

- [ ] **Step 1: Write the failing gate.** Create `tests\ObjCodecTests\OverlayTests.cs` in the style of
  `CatalogTests.cs` — `internal static class OverlayTests { internal static string Run() }`, every
  failure `throw new Exception("OVERLAY FAILURE: " + what)`. The clip checks in this task:

  1. `ResolveClips(actions: {"idle","walk","idle"}, controller: {"c1"}, out source)` → `{"idle","walk"}`
     and `source == ClipSource.AnimActions` — a non-empty def list wins and is deduplicated;
  2. `ResolveClips(actions: {}, controller: 73 names of which 69 are distinct, out source)` → **69**
     entries, first-seen order preserved, `source == ClipSource.Controller` — the measured
     `HumanoidAnimatorLOC` shape (build the 73 by repeating 4 of the 69);
  3. `ResolveClips(actions: {}, controller: {}, out source)` → empty, `source == ClipSource.None`,
     **no exception** — a rig-less/clip-less variant is a normal state, not a fault;
  4. nulls for either argument behave exactly as empty (this is called off live game data);
  5. dedup is **ordinal** — `"Idle"` and `"idle"` are two clips, because two Unity clips may differ
     only in case and the transport looks them up by name.

  Return `"OVERLAY PASS, " + checks + " check(s)"` (tasks 3 adds to the same count).

- [ ] **Step 2: Register the gate and watch it fail.** In `tests\ObjCodecTests\ObjCodecTests.csproj`,
  after the slice-1 `<Compile Include="CatalogTests.cs" />` line, add:

```xml
    <!-- The clip rule and the overlay's bone-status rule are string and pixel arithmetic, so which
         clips a variant offers - and which colour a bone gets - is proven here rather than by
         squinting at a screenshot. -->
    <Compile Include="..\..\src\Doctor\BoneOverlay.cs" Link="BoneOverlay.cs" />
    <Compile Include="OverlayTests.cs" />
```

  In `tests\ObjCodecTests\Program.cs`, after `Console.WriteLine(CatalogTests.Run());` (`:122`), add:

```csharp
        Console.WriteLine(OverlayTests.Run());
```

  Run `dotnet run --project tests\ObjCodecTests -c Release`. Expected:
  `error CS0117: 'PrototypeCatalog' does not contain a definition for 'ResolveClips'`.

- [ ] **Step 3: Write the pure rule.** In `src\Doctor\PrototypeCatalog.cs` (no `using UnityEngine`),
  add beside `Search` (`:225`):

```csharp
    /// <summary>Which list answered. Shown in the transport's label, because a clip list does NOT
    /// identify a prototype - Human and Crabman both carry HumanoidAnimatorLOC (slice 0(d)).</summary>
    internal enum ClipSource { None, AnimActions, Controller }

    /// <summary>THE CLIP CATALOGUE, §7: the variant's TacActorAnimActionsDef first; when that yields
    /// NOTHING - the shipped state of Crabman_AnimActionsDef - the controller's own animationClips.
    /// Deduplicated by name, ORDINAL, first-seen order kept. Null is empty.</summary>
    internal static IList<string> ResolveClips(IList<string> fromActions, IList<string> fromController,
                                               out ClipSource source);
```

  and extend `PrototypeVariant` (`:45-54`) with:

```csharp
        /// <summary>The controller or def name the Clips list came from, plus which one answered -
        /// e.g. "HumanoidAnimatorLOC (controller)". Labelled, never used as identity.</summary>
        internal string ClipSource;
        /// <summary>The variant's own preview pose, when the def carries one (design open question 6:
        /// Acheron's is null and 43 are unchecked). Null means "start on clip 0, paused".</summary>
        internal string PreviewPoseClip;
```

  In `Build` (`:189-200`), copy both new fields off `ManagerScan` beside the existing
  `variant.Clips.AddRange(manager.ClipNames)`.

- [ ] **Step 4: Wire the harvest.** In `src\Dev\PrototypeHarvest.cs`:
  - `ReadClips` (`:190`) collects the two candidate name lists separately — the anim-actions arm
    (`anim.DefaultActionClip`, `anim.DefaultReactionClip`, then `action.GetAllClips()` per entry;
    `GetAllClips` is `TacActorAnimActionBaseDef`'s own abstract member,
    `decompiled\AssemblyCSharp\…\TacActorAnimActionBaseDef.cs:12`) and the controller arm
    (`m.Rig.GetComponent<Animator>().runtimeAnimatorController.animationClips` — the very controller
    `CommonCharacterUtils.cs:42-43` copies onto the live rig) — then hands BOTH to
    `PrototypeCatalog.ResolveClips` instead of deciding the fallback inline;
  - `scan.ClipSource = controllerName + (source == ClipSource.Controller ? " (controller)" : " (anim actions)")`,
    and `scan.PreviewPoseClip` off the def's preview-pose field when it is non-null;
  - **keep the live objects**: a `Dictionary<string, AnimationClip[]> clipsByManager` beside the
    existing `representatives` (`:46`), filled in the same pass and read by a new
    `internal AnimationClip[] Clips(string managerName)` shaped exactly like `Representative` (`:51`).
    `PrototypeVariant` stays UnityEngine-free; the Unity objects live on the harvest, as the
    representative `TacCharacterDef`s already do.
  - `ManagerScan` (`src\Doctor\PrototypeCatalog.cs:26-35`) gains `ClipSource`, `PreviewPoseClip`.

- [ ] **Step 5: Run and expect PASS.** `dotnet run --project tests\ObjCodecTests -c Release`.
  Expected: `OVERLAY PASS, 5 check(s)`, `CATALOG PASS` and every other gate unchanged, exit 0.

- [ ] **Step 6: Build.** `dotnet build -c Release`. Expected: `Build succeeded`, 0 errors.

- [ ] **Step 7: Commit.**

```
git add src/Doctor/PrototypeCatalog.cs src/Dev/PrototypeHarvest.cs tests/ObjCodecTests/OverlayTests.cs tests/ObjCodecTests/ObjCodecTests.csproj tests/ObjCodecTests/Program.cs
git commit -m "feat(doctor): a variant's clip catalogue falls back to its controller, deduplicated"
```

---

### Task 2: The preview transport plays the PROTOTYPE's own clips

The transport already exists and already binds on every rebuild — including a prototype's, because
`FitBench.Posed` runs for all of them (`src\Dev\FitBench.cs:1305`, `FitAnim.Bind` at `:1334`). What it
binds is wrong: `FitBench.animActions` is assigned only in `FitBench.Show` (`:1230`, off
`CommonCharacterUtils.DisplayCharacter`'s return), and `PrototypeBaySession.Show` **discards that
same return value** (`src\Dev\PrototypeBaySession.cs:124`). So after a prototype rebuild the strip
catalogues the PREVIOUS unit's clip set against the new rig, and for Crabman it catalogues nothing at
all (`AnimActions.Count == 0`, slice 0(d)).

**Files:**
- Modify: `src\Dev\PrototypeBaySession.cs:110-149` (`Show`), `:167-208` (`Restore`),
  `src\Dev\FitAnim.cs:120` (`Bind`), `:163` (`Catalogue`), `src\Dev\FitBench.cs:585`, `:1277`,
  `:1319-1335`
- Test: build + offline suite; behaviour is proven live in task 7

- [ ] **Step 1: Keep the actions the rebuild produced.** In `src\Dev\PrototypeBaySession.cs`, `Show`
  (`:110`) assigns the return of `CommonCharacterUtils.DisplayCharacter` to a field and exposes it:

```csharp
        /// <summary>The TacActorAnimActions the last prototype DisplayCharacter produced - the clip
        /// set that belongs to the rig now standing there. Null before the first Show, and cleared by
        /// Restore, so a stale set can never be catalogued against a new rig.</summary>
        internal TacActorAnimActions Actions { get; private set; }
```

  `Restore` (`:167`) and `Dispose` (`:214`) null it, exactly where they null `slots`.

- [ ] **Step 2: Give `FitAnim` a fallback.** In `src\Dev\FitAnim.cs`, `Bind` (`:120`) gains a final
  parameter and `Catalogue` (`:163`) uses it:

```csharp
        /// <param name="fallback">The variant's own clips (PrototypeHarvest.Clips), used ONLY when the
        /// anim-actions def catalogued nothing - the shipped state of Crabman_AnimActionsDef. Null for
        /// an ordinary bench unit, which keeps today's behaviour byte for byte.</param>
        internal static void Bind(AddonsCharacterBuilder charBuilder, TacActorAnimActions actions,
                                  Equipment held, List<ItemDef> worn, AnimationClip[] modClips,
                                  AnimationClip[] fallback);
```

  In `Catalogue`, after the existing `Add(ActiveIdleClips) / Add(Shoot(...)) / Add(ActiveNavigation)`
  sequence: when `clips.Count == 0` and `fallback != null`, add each non-null fallback clip through
  the same dedup `Add` already applies (`:272-284`), and set `note` to name the source
  (`"clips from the rig's own controller - this variant's anim actions are empty"`). A `null`
  `actions` with a non-null `fallback` must also reach this arm — today `Catalogue` returns early at
  `:165`, which is exactly the Crabman case.

- [ ] **Step 3: Feed it from the bench.** In `src\Dev\FitBench.cs`:
  - `Posed` (`:1319-1335`): when `pendingVariant != null` (a prototype's rebuild, the flag `Retarget`
    already keys off at `:1325`), set `animActions = proto.Actions` **before** `Handed(manager)`
    (`:1329`), so `SetActiveNumberOfHands` (`TacActorAnimActions.cs:66`) runs on the prototype's own
    actions, and pass `harvest.Clips(shownVariant.ManagerName)` as `Bind`'s `fallback` (`:1334`);
  - the other two `FitAnim.Bind` call sites (`:585` in `ResetView`, `:1277` in the failure arm) pass
    `null` for `fallback` — an ordinary unit keeps today's behaviour exactly;
  - **preview pose first:** after `Bind`, when `shownVariant.PreviewPoseClip` is non-null, select that
    clip by name; otherwise `Bind`'s own "clip 0, paused at frame 0" already IS the required first
    state (`FitAnim.Bind:151-154`, `FitAnim.Select:483`). Do not auto-play: a bench model that starts
    moving on its own has to be caught before it can be read.

- [ ] **Step 4: Prove the pose comes back.** No new machinery — assert the shipped ordering instead
  and fix it only if it is wrong. `FitAnim.Bind` restores the OUTGOING animator's speed
  (`FitAnim.cs:128`) so a prototype swap cannot leave a frozen rig; `FitAnim.Release` (`:292` →
  `Stop` `:305`) runs `CommonCharacterUtils.ResetCharacterAnimation` and puts the speed back, and
  `FitBench.Close` calls it at `:1107` **after** the bay restore. Read those three sites and confirm
  the order still holds after step 3; the live proof is task 7 case 6.

- [ ] **Step 5: Build.** `dotnet build -c Release`. Expected: `Build succeeded`, 0 errors.

- [ ] **Step 6: Run the offline suite.** `dotnet run --project tests\ObjCodecTests -c Release` — exit
  0, every gate's count unchanged.

- [ ] **Step 7: Commit.**

```
git add src/Dev/PrototypeBaySession.cs src/Dev/FitAnim.cs src/Dev/FitBench.cs
git commit -m "feat(dev): the transport plays the standing prototype's own clips"
```

---

### Task 3: The skeleton overlay — lines, dots, and one colour per status

Drawn over the viewport in `OnGUI`, from the same projection the gizmo picks with, so what is drawn
and what is clickable can never disagree (`FitGizmo.Project`, `src\Dev\FitGizmo.cs:155`, and the
remark at `:152-154` that says why).

**Files:**
- Create: `src\Doctor\BoneOverlay.cs`
- Modify: `tests\ObjCodecTests\OverlayTests.cs`, `src\Dev\FitGizmo.cs:87` (`Mat` → `Colored`),
  `src\Dev\ModelDoctor.cs:538-560` (state), `:606` (`Draw`), `src\Dev\FitBench.cs:2223-2242` (`OnGUI`)
- Test: `tests\ObjCodecTests\OverlayTests.cs`

- [ ] **Step 1: Add the failing checks** to `OverlayTests.cs`, over `BoneOverlay`:

  1. `Classify("EXT_VoiceContext", …)` → `BoneStatus.Attachment` whatever else is passed — the game
     skips every `EXT_` transform (`Addon.cs:1208`), so it is never a defect and never green;
  2. a bone that is the VALUE of an alias entry → `BoneStatus.Alias`, even when its name also appears
     in the file's joints (the author's explicit mapping outranks a coincidence);
  3. a bone whose name equals `SkinBinder.Plain(fileJoint)` for some file joint → `BoneStatus.ByName`
     — decoration-insensitive, because `#X_Addon => Def` and `X` are the same bone to the binder
     (`ModelDoctor.Suggest`, `src\Dev\ModelDoctor.cs:996-1000` uses the same rule);
  4. a bone in the `missing` set (the report's `MissingBone` subjects, the same list `BoneMap` builds
     at `src\Dev\ModelDoctor.cs:925-926`) → `BoneStatus.Nearest` when `nearestBind` is true and
     `BoneStatus.Unmatched` when it is false;
  5. `Nearest(x, y, px, py, radius, out index)` returns the CLOSEST joint within the radius, `false`
     with `index == -1` when the closest is outside it, and is stable when two joints tie (lowest
     index wins) — a pick that flickers between two overlapping joints is a pick nobody can make;
  6. `Nearest` with a zero-length array, a NaN cursor, or a NaN projected point returns false rather
     than throwing — every one of those arrives from a live camera.

  Bump the return count to `"OVERLAY PASS, 11 check(s)"`.

- [ ] **Step 2: Run and watch it fail.** `dotnet run --project tests\ObjCodecTests -c Release`.
  Expected: `error CS0246: The type or namespace name 'BoneOverlay' could not be found`.

- [ ] **Step 3: Write the pure half.** Create `src\Doctor\BoneOverlay.cs`, namespace
  `Morgott.ContentTool.Doctor`, **no `using UnityEngine`**:

```csharp
    /// <summary>What one rig bone's colour means. Three distinct colours for the three states §7's
    /// legend names, plus the two the legend does not: a bone nothing claims, and an EXT_ attachment
    /// point the game itself skips.</summary>
    internal enum BoneStatus { Unmatched, ByName, Alias, Nearest, Attachment }

    internal static class BoneOverlay
    {
        /// <summary>Within this many pixels of a joint dot, a press is that joint's.</summary>
        internal const float PickRadiusPixels = 12f;

        /// <summary>ONE bone's status. Order is the whole rule: EXT_ first (the game skips it, so it
        /// is never a defect), then an explicit alias (the author outranks a coincidence), then a
        /// by-name match under SkinBinder.Plain, then whether the bind fell back to nearest-bone.</summary>
        internal static BoneStatus Classify(string boneName, ICollection<string> fileJoints,
                                            IDictionary<string, string> aliases,
                                            ICollection<string> missing, bool nearestBind);

        /// <summary>The joint nearest a cursor, or false. Ties go to the lowest index so a pick over
        /// two overlapping joints is repeatable; NaN and an empty array are false, never a throw.</summary>
        internal static bool Nearest(float x, float y, float[] px, float[] py, bool[] visible,
                                     float radiusPixels, out int index);
    }
```

- [ ] **Step 4: Share the gizmo's material.** In `src\Dev\FitGizmo.cs`, rename `private static Material
  Mat()` (`:87`) to `internal static Material Colored()` and update its three call sites (`Live`
  `:66`, `Render` `:281`). Body unchanged — the probe, the `Last` diagnostic sentence and the null
  arm stay exactly as they are. **Do not probe `Hidden/Internal-Colored` a second time**: a build that
  stripped the shader must disable both drawings through one message, not two.

- [ ] **Step 5: Draw it.** In `src\Dev\ModelDoctor.cs`, beside the browser state (`:538-560`):

```csharp
        /// <summary>§6's [Skeleton] toggle. Session-only, like every other view preference.</summary>
        private bool skeleton = true;
        /// <summary>The bone the inspector is showing, by NAME (a rebuild replaces the Transform).</summary>
        private string picked;
```

  and a new method, called from the bench's `OnGUI` and nowhere else:

```csharp
        /// <summary>
        /// The skeleton over the viewport: one line per parent-child pair, one dot per joint,
        /// coloured by BoneOverlay.Classify. Drawn in OnGUI - in the Repaint pass, in PIXEL space
        /// (GL.LoadPixelMatrix) with FitGizmo's own material - so the picture is projected by exactly
        /// the arithmetic the pick uses one branch below it.
        ///
        /// The BONES ARE THE TARGET'S, not the rig's: on Replace they are Renderer.bones (slice 0
        /// measured a Human head slot at 21 against the rig's 124), and on Extend they are the
        /// transforms under Root whose names are in Record.BindableBones. Never the full hierarchy
        /// dressed up as a slot.
        /// </summary>
        /// <param name="stripTopGui">The transport strip's top edge in IMGUI coordinates - the strip
        /// and the panel own their pixels, exactly as FitGizmo.Gui:405 documents.</param>
        internal void Overlay(Camera cam, float panelWidth, float stripTopGui);
```

  Requirements, each one load-bearing:
  - **draws nothing** unless `skeleton`, the viewport is actually visible (`cam != null`,
    `BenchList.StripShown`-style room test) and there is a `Target`; a `Prototype` with
    `Unavailable != null` draws nothing rather than a rig it does not have;
  - one `Vector3 cam.WorldToScreenPoint` per joint, cached for the pass — never one per line;
  - a joint behind the camera (`z <= cam.nearClipPlane`) is `visible == false`: not drawn, not
    pickable. `FitGizmo.AxisVisible`'s test at `src\Dev\FitGizmo.cs:165-166` is the precedent;
  - a line is drawn only when BOTH ends are visible and both are in the drawn set;
  - colours: `ByName` green `(0.35, 0.9, 0.35)`, `Unmatched` red `(0.92, 0.25, 0.25)`, `Alias` yellow
    `(1, 0.92, 0.3)` — the three §7 names, taken from `FitGizmo.AxisColour` / `Hot`
    (`src\Dev\FitGizmo.cs:267-270`) so the bench has one palette; `Nearest` blue
    `(0.35, 0.55, 1)`, `Attachment` the dim grey `(0.45, 0.45, 0.45, 0.5)`;
  - a one-line legend drawn in the strip, naming only the statuses actually present;
  - the whole body inside `try/catch` that swallows: this runs from `OnGUI`, and `FitBench.OnGUI`
    closes the entire bench on an exception (`src\Dev\FitBench.cs:2244-2250`).

- [ ] **Step 6: Call it.** In `src\Dev\FitBench.cs`, `Arm.OnGUI` (`:2223`), after `FitAnim.Draw`
  (`:2240`) and before `guiHot` is latched (`:2242`):

```csharp
                    // AFTER the strip, so the strip's own pixels are already the strip's; BEFORE the
                    // hotControl latch, so a joint pick counts as a control taking the mouse and the
                    // orbit stands down for it.
                    if (doctorTab) doctor.Overlay(cam, PanelWidth,
                                                  BenchList.StripTop(Screen.width, Screen.height, PanelWidth));
```

- [ ] **Step 7: Run and expect PASS.** `dotnet run --project tests\ObjCodecTests -c Release`.
  Expected: `OVERLAY PASS, 11 check(s)` and every other gate unchanged, exit 0.

- [ ] **Step 8: Build.** `dotnet build -c Release`. Expected: `Build succeeded`, 0 errors.

- [ ] **Step 9: Commit.**

```
git add src/Doctor/BoneOverlay.cs src/Dev/FitGizmo.cs src/Dev/ModelDoctor.cs src/Dev/FitBench.cs tests/ObjCodecTests/OverlayTests.cs
git commit -m "feat(doctor): the prototype's skeleton is drawn over the viewport, coloured by status"
```

---

### Task 4: Click a joint, read the joint

The left button is free by construction: `ViewGesture` has no `Pick` member and
`OrbitCamera.Classify` answers `None` for a bare left press (`src\Dev\OrbitCamera.cs:5-9`, `:127-134`).
The precedence is the one the bench already documents: **panel, then transport strip, then
`FitGizmo`, then this overlay, then the orbit** (`FitGizmo.Gui`'s remark, `src\Dev\FitGizmo.cs:382-404`).

**Files:**
- Modify: `src\Dev\ModelDoctor.cs` (`Overlay`, plus a new inspector draw), `src\Dev\FitAnim.cs` only
  if the inspector needs room beside the strip
- Test: build + offline suite (the hit test itself is gated in task 3); live proof in task 7

- [ ] **Step 1: Take the press.** Inside `Overlay`, mirroring `FitGizmo.Gui` (`src\Dev\FitGizmo.cs:405`)
  exactly:
  - allocate the control id **unconditionally, first thing**, with
    `GUIUtility.GetControlID(hint, FocusType.Passive)` — an id allocated only sometimes is a different
    id every frame;
  - on `EventType.MouseDown` with `e.button == 0`: refuse when `e.mousePosition.x <= panelWidth`,
    when `e.mousePosition.y >= stripTopGui`, and when
    `FitGizmo.WouldGrab(e.mousePosition.x, Flip(e.mousePosition.y))` (`src\Dev\FitGizmo.cs:259`) —
    the gizmo gets first refusal, as it does from the orbit at `src\Dev\FitBench.cs:2134-2140`;
  - `BoneOverlay.Nearest(...)` over this pass's projected points with
    `BoneOverlay.PickRadiusPixels`; a miss returns WITHOUT `e.Use()`, so the orbit still gets it.
    A hit enqueues the selection on `edits` (`src\Dev\ModelDoctor.cs:35` — the draw pass must not
    change what the next layout pass lays out) and calls `e.Use()`.

- [ ] **Step 2: Draw the inspector.** A `▸ Selected bone inspector` foldout, per §6 in the RIGHT
  column: drawn as its own IMGUI area above the transport strip, the way `FitAnim.List` draws the open
  clip list (`src\Dev\FitAnim.cs:399-427`, and the remark at `:84-95` explaining why it cannot live
  inside the strip). It shows, for the picked bone:
  - `name`, full `path` (from `PrototypeBone.Path`, `src\Doctor\PrototypeCatalog.cs:11` — the only
    thing that tells duplicates apart), `parent` name;
  - `rest` and `current` TRS: `localPosition` / `localRotation.eulerAngles` / `localScale` of the live
    `Transform`, and the same off the prototype record's censused bone for rest;
  - its `BoneStatus`, and for `Alias` which file joint maps onto it;
  - which file joint binds to it, or `-`;
  - **read-only** — no editable field, no drag, no handle. §1's refusal list is the reason.

- [ ] **Step 3: Survive a rebuild.** `picked` is a NAME, and `ModelDoctor.Root`'s setter
  (`src\Dev\ModelDoctor.cs:573-599`) already clears everything downstream of a dead renderer; clear
  `picked` there too. A name that no longer resolves draws "that bone is no longer on the rig"
  rather than throwing.

- [ ] **Step 4: Build.** `dotnet build -c Release`. Expected: `Build succeeded`, 0 errors.

- [ ] **Step 5: Run the offline suite.** `dotnet run --project tests\ObjCodecTests -c Release` — exit
  0, counts unchanged.

- [ ] **Step 6: Commit.**

```
git add src/Dev/ModelDoctor.cs
git commit -m "feat(doctor): clicking a joint opens the selected-bone inspector"
```

---

### Task 5: Click-to-alias, into the map that already exists

`ModelDoctor.boneOpen` (`src\Dev\ModelDoctor.cs:539`) is ALREADY the "armed row" — the bone map sets
it when a row's right-hand button is pressed (`:949`) and clears it when a bone is chosen (`:968`).
Click-to-alias is therefore not a new mode: it is a second way to answer the question the armed row is
already asking, and it must go through the SAME `SetAlias` (`:151`) → `Rethink` → `Restart` path, so
the sidecar format, the bijection rule and the re-run preflight all come for free.

**Files:**
- Modify: `src\Dev\ModelDoctor.cs:917-981` (`BoneMap`), the `Overlay` pick branch from task 4
- Test: build + offline suite; live proof is task 7 case 5

- [ ] **Step 1: Expose the armed row to the overlay.** No new state. Inside `Overlay`, read `boneOpen`
  and the same eligibility the dropdown enforces: a target bone is assignable when it is in the
  `MissingBone` subject list (`:925-926`) and `!Claimed(bone, boneOpen)` (`:985` — two file bones on
  one game bone is the collision the binder refuses, so it is never offered).

- [ ] **Step 2: Arm the picture.** While `boneOpen != null`, eligible joints are drawn with a ring (a
  second, larger dot behind the joint) and ineligible ones dimmed, and the strip's legend line reads
  `aliasing '<fileBone>' - click a target bone, Esc to cancel`. An author must be able to see that the
  next click means something different from the last one.

- [ ] **Step 3: Assign on click.** In the pick branch: when armed and the hit joint is eligible,
  `edits.Enqueue(delegate { SetAlias(armedFileBone, boneName); })` and clear `boneOpen` — capturing
  both strings in locals first, exactly as `BoneMap` does at `:966-967`. `SetAlias` already re-runs the
  preflight through `Restart` (`:156`), which is §7's "re-runs preflight" in full.

- [ ] **Step 4: Disarm.** `boneOpen = null` on `EventType.KeyDown` with `KeyCode.Escape` (with
  `e.Use()`, mirroring `FitGizmo.Gui:441-444`), and on a click that hits the SAME joint twice or an
  ineligible joint. An armed state with no way out is the trap `BoneMap`'s own `x` button
  (`:952-957`) exists to avoid.

- [ ] **Step 5: Build.** `dotnet build -c Release`. Expected: `Build succeeded`, 0 errors.

- [ ] **Step 6: Run the offline suite.** `dotnet run --project tests\ObjCodecTests -c Release` — exit
  0, counts unchanged. `ALIAS PASS` in particular must be unchanged: the sidecar format is untouched.

- [ ] **Step 7: Commit.**

```
git add src/Dev/ModelDoctor.cs
git commit -m "feat(doctor): clicking a bone assigns the armed alias row"
```

---

### Task 6: The §6 layout — only what is not already there

Read §6's diagram before starting, then read `ModelDoctor.Draw` (`src\Dev\ModelDoctor.cs:606`),
`ModelDoctor.Header` (`:691`) and `FitAnim.Draw` (`src\Dev\FitAnim.cs:363`) and write down what is
already drawn. Most of it is: the one-line source/prototype/mode/role/slot header (`:691-721`), the
verdict + counts, the scrolling diagnostics (`:654-659`), the bone map foldout (`:917`), the button
row (`:661-683`), and the whole right-column transport — `◀ ▶`, the clip stepper/list, `Loop`, speed,
the scrubber and the time readout (`FitAnim.Clips:453`, `FitAnim.Controls:488`).

**Files:**
- Modify: `src\Dev\ModelDoctor.cs:606` (`Draw`), `:917` (`BoneMap`), `src\Dev\FitAnim.cs:453` (`Clips`)
- Test: build + offline suite; the picture is task 7

- [ ] **Step 1: `[Skeleton]` into the right column's header row.** Add the toggle as the FIRST control
  of `FitAnim.Clips`'s row (`src\Dev\FitAnim.cs:456-472`), giving §6's
  `[Skeleton] Clip ▼ ◀ ▶ Loop` line, reading and writing the Doctor's `skeleton` field through one
  accessor. It is drawn only on the Doctor tab — on the Fit tab the row keeps exactly its current
  shape.

- [ ] **Step 2: Bone map auto-opens on a NAME MISMATCH, once.** `mapOpen` (`src\Dev\ModelDoctor.cs:538`)
  is a manual toggle today. Per §6 ("collapsed when BY NAME, auto-opened for name mismatch"), open it
  automatically when a NEW report arrives whose `Outcome` is `NearestBone` or that carries any
  `ExtraBone` row — once per report generation, remembered by the generation counter, so an author who
  closes it does not have it reopened under them every frame.

- [ ] **Step 3: The inspector foldout header.** The task-4 area draws `▸ / ▾ Selected bone inspector`
  and collapses to the header alone. Collapsed is the default: the viewport is what the author came
  for.

- [ ] **Step 4: Leave the rest alone.** The 42/58 proportion is the bench's existing panel/scene split
  (`FitBench.PanelWidth`, `BenchList.ContentWidth`), and Advanced already holds the file utilities and
  the catalogue rescan (`src\Dev\FitBench.cs:1615-1616`). **Do not restructure the panel** — this task
  adds three controls and nothing else.

- [ ] **Step 5: Build.** `dotnet build -c Release`. Expected: `Build succeeded`, 0 errors.

- [ ] **Step 6: Run the offline suite.** `dotnet run --project tests\ObjCodecTests -c Release` — exit
  0, counts unchanged.

- [ ] **Step 7: Commit.**

```
git add src/Dev/ModelDoctor.cs src/Dev/FitAnim.cs
git commit -m "feat(doctor): the skeleton toggle, the bone inspector and an auto-opened bone map"
```

---

### Task 7: Live acceptance on `D:\PP-Instance3`

Evidence from a real run only. **Use `D:\PP-Instance3`.** `D:\PP-Instance2` is owned by another
session for the duration — do not deploy to it, do not connect to it, do not launch or kill anything
there. **Never** target `D:\Steam\steamapps\common\Phoenix Point`: that is the user's own game.

**Files:**
- Modify: `internal-docs\planning\2026-09-02-prototype-picker-design.md` (a
  `## 13. Slice 2 acceptance run` section at the end)
- Test: the running game

- [ ] **Step 1: Read the playbook first.** Read `E:\DEV\PhoenixPoint\PPCLI\PLAYBOOK.md` and take the
  exact command lines for `plan`, `connect console`, `connect call` and `connect screenshot`. Do not
  dig PPCLI source, do not invent a command line. Every PPCLI invocation in this task carries
  `-PPRoot 'D:\PP-Instance3' -ProfileId 76561197996210593`. If PPCLI misbehaves, append an entry to
  `E:\DEV\PhoenixPoint\PPCLI\ISSUES.md` (attempted → happened → expected → evidence → severity) and
  work around it — never edit PPCLI source, never commit into that repo.

- [ ] **Step 2: Deploy to Instance3.** `dotnet build -c Release`, then the repo's own
  `.\deploy.ps1` with an explicit Instance3 root (read the script first for its parameter names).
  Expected: the copied `ContentTool.dll` timestamp matches the build just made. Confirm the mod is in
  that profile's `MOD_ACTIVATED` before launching.

- [ ] **Step 3: Reach a geoscape and open the bench.** The bench refuses outside a playing geoscape
  level with a squad bay (`src\Dev\FitBench.cs:364-379`):

```powershell
cd E:\DEV\PhoenixPoint\PPCLI
.\ppcli.ps1 -PPRoot 'D:\PP-Instance3' -ProfileId 76561197996210593 connect state    # gate FIRST
.\ppcli.ps1 -PPRoot 'D:\PP-Instance3' -ProfileId 76561197996210593 plan .\plans\start-campaign.json '{"difficultyIndex":1}'
.\ppcli.ps1 -PPRoot 'D:\PP-Instance3' -ProfileId 76561197996210593 connect state    # gate again
.\ppcli.ps1 -PPRoot 'D:\PP-Instance3' -ProfileId 76561197996210593 connect console '{"command":"ct_bench","args":["open"]}'
```

  Record the baseline the close has to restore: the bay's `AddonsManagerDef` name, `Addons.Count` and
  `SkinnedMeshRenderer` count, read by `connect call` `op:"get"` — the triple slice 1's case 9 used.

- [ ] **Step 4: Drive it by reflection, three prototypes.** IMGUI cannot be clicked through PPCLI, so
  drive the seams the rows call, exactly as slice 1 did: `FitBench.Prototypes` /
  `PrototypeCatalog.Search` / `FitBench.ShowPrototype` / `FitBench.SlotTargets` /
  `FitBench.PrototypeBusy`, and the Doctor through `AccessTools.Field(typeof(FitBench),"doctor")`.
  Write each step as a `connect plan` with an inline plan file under the scratchpad (never inside the
  PPCLI repo). Assert `PrototypeBusy == false` before reading anything after a `ShowPrototype`.

  | # | Case | Expected |
  |---|---|---|
  | 1 | Human → clip list | `Clips.Count` and `ClipSource` off the live harvest; the count is what a `HumanoidAnimatorLOC` variant really resolves to — record it verbatim, and compare against the slice-0 fact **73 entries / 69 distinct** |
  | 2 | Crabman → clip list | **non-empty**, `ClipSource` ends `(controller)` — `Crabman_AnimActionsDef.AnimActions.Count == 0`, so this is the fallback under test |
  | 3 | Mutog → clip list | non-empty off `MidMonsterAnimator` (60 entries / **45** distinct), clip names `Chiron_*` — a clip list does not identify a prototype |
  | 4 | play + scrub | with a Crabman standing, select a clip, set `playing` true, wait, `connect screenshot`; then scrub to 0.5 paused and screenshot again — two visibly DIFFERENT poses |
  | 5 | overlay + a broken name | `PickFile` a copy of `lib\u9_probe.glb` (copied into the scratchpad) with **one joint deliberately renamed** so the report carries an `ExtraBone`/`MissingBone` pair; screenshot the overlay showing the red/green split, then assign the alias by invoking the click path's own `SetAlias` and screenshot the verdict going NEAREST-BONE → BY NAME |
  | 6 | close | `ct_bench close`, then the step-3 triple read again — **identical** — plus `FitAnim` not holding the animator (`speed` back to what it was) |

- [ ] **Step 5: Check `Player.log`.** Grep the Instance3 profile's
  `Player.log` for `Exception` and for `Getting control … in a group with only …` (the IMGUI
  layout-imbalance signature this slice's new controls could introduce). Expected: no new ContentTool
  entry across the whole run.

- [ ] **Step 6: Record the run.** Append `## 13. Slice 2 acceptance run` to
  `internal-docs\planning\2026-09-02-prototype-picker-design.md`: date, install, build stamp, the
  screenshot paths, the observed clip counts and sources per case, the case-5 verdict transition, the
  step-6 restore comparison and the `Player.log` result. **Observed values only — no predicted ones.**

- [ ] **Step 7: Commit.**

```
git add internal-docs/planning/2026-09-02-prototype-picker-design.md
git commit -m "docs(planning): slice 2 visual-check acceptance run on Instance3"
```

- [ ] **Step 8: Hand off to the owner.** Slice 2 acceptance ends with a visual check only the owner can
  make. Report the screenshot paths and ask him to confirm, in game:
  1. the skeleton overlay sits ON the model at every orbit angle and zoom, not beside it or behind it;
  2. green / red / yellow are distinguishable at a glance, on a dark model and on a light one;
  3. the joint dots are big enough to hit and small enough not to hide the mesh;
  4. an idle clip plays smoothly and the scrubber lands where the thumb says it does;
  5. the inspector reads the bone he expected when he clicks a shoulder, a hand and a fingertip;
  6. closing the bench leaves his squad member exactly as it was.

  **Do not cut a release** — the owner verifies first (slice 1 is still awaiting his check).
