# Viewport mouse controls to 3D-editor standard — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended)
> or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax
> for tracking.

Implements section 8 of `internal-docs\planning\2026-09-02-prototype-picker-design.md:246-296`
(slice 2, `:378`). No other section of that design is in scope here.

**Goal:** The bench viewport behaves like every 3D editor an author already knows, on every tab: MMB
drags orbit around the focus point, Shift+MMB pans, the wheel zooms **toward the cursor** with a
distance-scaled step, Alt+LMB orbits for a mouse with no middle button, `F` frames, `Home` resets, and
the LEFT button is left alone so it can PICK — the gizmo's handles today, the Doctor's skeleton overlay
in slice 2. The current scheme (LMB orbits, RMB turns the model, MMB pans) is deleted, not deprecated.

**Architecture:** ONE controller, `Morgott.ContentTool.Dev.OrbitCamera` (`src\Dev\OrbitCamera.cs`), owns
the whole view state — yaw, pitch, zoom, their damping targets and the wheel's cursor anchor — and every
bench tab already shares it, because every tab is drawn by `FitBench` and `FitBench.Arm.Mouse()` is the
only method in the mod that reads the mouse (`ModelDoctor.cs` contains no input code at all; `grep -c
"Input\." src\Dev\*.cs` finds it in `FitBench`, `FitGizmo`, `FitAnim`, `GlbFileBrowser`, `DevRunner`
only). The controller is **plain floats end to end** — no `Vector3`, no `Quaternion`, no `Mathf`, no
`UnityEngine` at all — exactly like `src\Dev\BenchList.cs`, which it delegates the tuned gains to
(`BenchList.Orbit`, `Tilt`, `Wheel`, `Clamp`, `WrapYaw`, `StripReserve`, and the constants
`DegreesPerPixel = 0.2`, `PitchMin/PitchMax = -80/80`, `ZoomFactor = 0.12`, `ZoomDefault = 1.35`). Both
files are linked into the offline test EXE, so the gestures, the pitch clamp, the cursor anchor, the
framing distance and the damping are proven without launching a game. The Unity half stays where it is:
`FitBench.Reframe` turns yaw/pitch/zoom into a pose, `FitBench.PanBy` and a new `FitBench.ZoomAnchor`
turn pixel offsets into the world-space `pan` vector, and `FitBench.Arm` is the thin adapter that reads
`UnityEngine.Input` in `Update` and latches IMGUI's `hotControl` in `OnGUI`.

**Why the core is float-only, against the design's own wording.** Section 8's implementation notes say
`Mathf.SmoothDamp` and the task brief allows `Vector3/Quaternion/Bounds/Mathf`. The offline test EXE
(`tests\ObjCodecTests`) references **no UnityEngine assembly at all** — every linked `src\` file in
`ObjCodecTests.csproj:25-153` is UnityEngine-free on purpose, and adding a reference to
`UnityEngine.CoreModule` to test a camera would be the first one. So: exponential damping over
`Math.Exp` instead of `Mathf.SmoothDamp` (same shape, one line, no velocity field to carry), scalar
in/out instead of `Vector3`, and the three lines that genuinely need a rotation stay in `FitBench`,
which is compiled only into the mod.

**Tech Stack:** C# 9 / net472, Mono inside Unity 2019 (Phoenix Point). No new dependencies. UI is IMGUI
(`OnGUI`, per frame) and legacy `UnityEngine.Input`, both already referenced. Build: `dotnet build -c
Release` in `E:\DEV\PhoenixPoint\ContentTool`. Offline tests: the console EXE `tests\ObjCodecTests`
(NOT `dotnet test`), run with `dotnet run --project tests\ObjCodecTests -c Release`; every gate is a
`static class X { internal static string Run() }` that throws on failure and is called from
`Program.Main`. `ObjCodecTests.csproj` sets `EnableDefaultCompileItems=false`, so **every new file —
test or linked src — must be added to its `<Compile Include>` list**; `ContentTool.csproj` globs
`src\**\*.cs` and needs no edit.

---

## File Structure

**Created**

| Path | Responsibility |
|---|---|
| `src\Dev\OrbitCamera.cs` | `ViewGesture` enum + `OrbitCamera`: yaw/pitch/zoom + targets + wheel anchor, `Classify`, `InViewport`, `Damp`/`DampAngle`/`ShortWay`, `ZoomShift`, `FrameZoom`, `OrbitBy`, `WheelAt`, `FrameOn`, `Reset`, `Tick`. UnityEngine-free. |
| `tests\ObjCodecTests\OrbitTests.cs` | The gate: gesture routing, viewport rect, orbit deltas, pitch clamp, zoom-toward-cursor, framing distance, damping convergence. |

**Modified**

| Path | Change |
|---|---|
| `tests\ObjCodecTests\ObjCodecTests.csproj` | Link `src\Dev\BenchList.cs` and `src\Dev\OrbitCamera.cs`; compile `OrbitTests.cs`. |
| `tests\ObjCodecTests\Program.cs:116` | `Console.WriteLine(OrbitTests.Run());` |
| `src\Dev\FitBench.cs:317-321` | `zoom`/`yaw`/`pitch` fields deleted; one `static readonly OrbitCamera view`. `lift` and `pan` stay. |
| `src\Dev\FitBench.cs:530` | `ResetView` resets through `view.Reset()`. |
| `src\Dev\FitBench.cs:755` / `:763` | `Reframe` reads `view.Zoom` / `view.Yaw` / `view.Pitch`. |
| `src\Dev\FitBench.cs:804` | New `ZoomAnchor(before, after)` beside `PanBy` — the cursor-anchored half of the wheel. |
| `src\Dev\FitBench.cs:817-834` | `Fly` gains `F` (frame) and `Home` (reset) ahead of its `framed` guard. |
| `src\Dev\FitBench.cs:1293-1297` | New `guiHot` latch beside `typing`/`dropFocus`. |
| `src\Dev\FitBench.cs:1412-1413` | The `in`/`out` buttons write the zoom TARGET; `Tick` reframes. |
| `src\Dev\FitBench.cs:1473-1480` | The gesture hint line and the Advanced readout tell the new truth. |
| `src\Dev\FitBench.cs:1813-1815` | `Update` ticks the damping and reframes once, after `Mouse`/`Fly`. |
| `src\Dev\FitBench.cs:1827-1911` | `Mouse()` rewritten: the gesture table, the viewport rect, the hotControl gate. RMB model-turn deleted. |
| `src\Dev\FitBench.cs:1978-1981` | `OnGUI` latches `guiHot = GUIUtility.hotControl != 0`. |
| `src\Dev\BenchList.cs:684-689` | `OverScene` deleted — `OrbitCamera.InViewport` is the one viewport test. |
| `src\Dev\BenchList.cs:378-393` | `OverStrip` deleted — same reason; `StripShown`/`StripReserve`/`StripTop` stay. |
| `internal-docs\planning\2026-09-02-prototype-picker-design.md` | Task 4 appends the acceptance run. |

---

### Task 1: `OrbitCamera` — the pure core, and the gate that proves it

**Files:**
- Create: `src\Dev\OrbitCamera.cs`, `tests\ObjCodecTests\OrbitTests.cs`
- Modify: `tests\ObjCodecTests\ObjCodecTests.csproj`, `tests\ObjCodecTests\Program.cs`
- Test: `tests\ObjCodecTests\OrbitTests.cs`

- [ ] **Step 1: Write the gate first.** Create `tests\ObjCodecTests\OrbitTests.cs`:

```csharp
using System;
using Morgott.ContentTool.Dev;

/// <summary>
/// THE VIEWPORT'S RULES, WITHOUT A VIEWPORT. Every gesture the bench answers to is a decision about
/// floats - which button means orbit, how far 100 pixels turn the model, where the pitch stops, where
/// the pivot lands when the wheel is rolled with the cursor off to one side - and every one of them was
/// previously only checkable by dragging a mouse in a running game and judging the result by eye.
///
/// The design's acceptance (section 8) asks for a pitch that never flips, a zoom that keeps the point
/// under the cursor still, and a drag that eases instead of snapping. All three are arithmetic, so all
/// three are asserted here rather than in a screenshot.
/// </summary>
internal static class OrbitTests
{
    /// <summary>A 1920x1080 window with the bench's own panel and its transport strip - the geometry
    /// every rect test below is asked about.</summary>
    private const float W = 1920f, H = 1080f, Panel = 380f;

    internal static string Run()
    {
        int checks = 0;
        bool ix = BenchList.InvertX, iy = BenchList.InvertY;
        try
        {
            BenchList.InvertX = true; BenchList.InvertY = true;
            checks += Gestures();
            checks += Viewport();
            checks += Deltas();
            checks += ClampBand();
            checks += Damping();
            checks += ZoomCursor();
            checks += Framing();
            checks += Resetting();
        }
        finally { BenchList.InvertX = ix; BenchList.InvertY = iy; }

        return "ORBIT PASS, " + checks + " check(s) - gestures, the pitch clamp, zoom toward the " +
               "cursor, framing and the damping";
    }

    // ---------------------------------------------------------------- which button means what

    private static int Gestures()
    {
        int n = 0;
        n += Check(OrbitCamera.Classify(true, false, true, false, false, false) == ViewGesture.Orbit,
                   "MIDDLE-drag orbits");
        n += Check(OrbitCamera.Classify(true, false, true, false, true, false) == ViewGesture.Pan,
                   "SHIFT+MIDDLE pans");
        n += Check(OrbitCamera.Classify(true, true, false, true, false, false) == ViewGesture.Orbit,
                   "ALT+LEFT orbits, for a mouse with no middle button");
        // THE ONE THAT IS A FEATURE RATHER THAN A GAP: a bare left drag is NOT the camera's. It is what
        // picks - the gizmo's handles today, the Doctor's skeleton overlay in slice 2 - and a camera
        // that also answered to it would swing the model out from under the click.
        n += Check(OrbitCamera.Classify(true, true, false, false, false, false) == ViewGesture.None,
                   "a bare LEFT drag is left alone, so it can pick");
        n += Check(OrbitCamera.Classify(true, true, false, false, false, true) == ViewGesture.None,
                   "the gizmo gets first refusal on LEFT");
        n += Check(OrbitCamera.Classify(true, true, false, true, false, true) == ViewGesture.None,
                   "... and it beats ALT too, or a press on an arrow would drag AND orbit");
        n += Check(OrbitCamera.Classify(false, false, true, false, false, false) == ViewGesture.None,
                   "nothing at all happens off the viewport");
        return n;
    }

    // ---------------------------------------------------------------- where the viewport is

    private static int Viewport()
    {
        int n = 0;
        n += Check(OrbitCamera.InViewport(1000f, 500f, W, H, Panel), "the middle of the scene is in");
        n += Check(!OrbitCamera.InViewport(100f, 500f, W, H, Panel), "the panel's own column is out");
        // The strip is a band along the BOTTOM of the free region, in the mouse's convention (y from the
        // bottom), and a drag that starts on the scrub slider must never become an orbit.
        n += Check(!OrbitCamera.InViewport(1000f, 40f, W, H, Panel), "the transport strip is out");
        // ... but a window too small for a strip does not reserve one, so those pixels come back.
        n += Check(OrbitCamera.InViewport(1000f, 40f, W, 200f, Panel),
                   "with no room for a strip there is no strip to avoid");
        n += Check(!OrbitCamera.InViewport(float.NaN, 500f, W, H, Panel), "NaN is out");
        return n;
    }

    // ---------------------------------------------------------------- how far a drag turns it

    private static int Deltas()
    {
        int n = 0;
        var c = new OrbitCamera();
        c.OrbitBy(100f, 0f);
        n += Check(Near(c.YawTarget, 20f), "100 px of drag is 20 degrees of yaw: " + c.YawTarget);
        c.OrbitBy(0f, 100f);
        n += Check(Near(c.PitchTarget, -20f), "and 20 of pitch, inverted: " + c.PitchTarget);
        // The INVERT toggles are session knobs on the panel and they must reach the MIDDLE-drag orbit,
        // which is the only orbit there is now.
        BenchList.InvertX = false;
        var flipped = new OrbitCamera();
        flipped.OrbitBy(100f, 0f);
        n += Check(Near(flipped.YawTarget, 340f), "InvertX flips the yaw: " + flipped.YawTarget);
        BenchList.InvertX = true;

        var far = new OrbitCamera();
        far.OrbitBy(10000f, 0f);
        n += Check(far.YawTarget >= 0f && far.YawTarget < 360f,
                   "a long drag wraps rather than growing forever: " + far.YawTarget);
        return n;
    }

    // ---------------------------------------------------------------- and where it stops

    private static int ClampBand()
    {
        int n = 0;
        var up = new OrbitCamera();
        bool inBand = true;
        for (int i = 0; i < 40; i++)
        {
            up.OrbitBy(0f, -1000f);
            if (up.PitchTarget < BenchList.PitchMin || up.PitchTarget > BenchList.PitchMax) inBand = false;
        }
        n += Check(inBand, "continuous vertical drag never leaves the band");
        n += Check(Near(up.PitchTarget, BenchList.PitchMax), "it stops AT the top: " + up.PitchTarget);

        var down = new OrbitCamera();
        for (int i = 0; i < 80; i++) down.OrbitBy(0f, 1000f);
        n += Check(Near(down.PitchTarget, BenchList.PitchMin),
                   "and at the bottom, with no flip through it: " + down.PitchTarget);
        return n;
    }

    // ---------------------------------------------------------------- the easing

    private static int Damping()
    {
        int n = 0;
        var c = new OrbitCamera();
        c.OrbitBy(225f, 0f);                                  // 45 degrees of yaw
        n += Check(Near(c.Yaw, 0f) && Near(c.YawTarget, 45f),
                   "a gesture writes the TARGET and moves nothing yet: " + c.Yaw);

        c.Tick(0f);
        n += Check(Near(c.Yaw, 0f), "no time passing moves nothing: " + c.Yaw);

        float previous = c.Yaw;
        bool monotone = true;
        for (int i = 0; i < 60; i++)
        {
            c.Tick(1f / 60f);
            if (c.Yaw < previous - 1e-4f || c.Yaw > 45f + 1e-4f) monotone = false;
            previous = c.Yaw;
        }
        n += Check(monotone, "it approaches from one side and never overshoots");
        n += Check(Near(c.Yaw, 45f), "one second is long past arrival: " + c.Yaw);
        n += Check(!c.Tick(1f / 60f), "a settled camera reports NO movement, so nothing reframes");

        // The yaw is wrapped, so damping it as a plain number would sweep 340 degrees the wrong way
        // round the moment a drag crosses zero.
        n += Check(Near(OrbitCamera.ShortWay(350f, 10f), 20f),
                   "350 -> 10 is twenty degrees forward: " + OrbitCamera.ShortWay(350f, 10f));
        float stepped = OrbitCamera.DampAngle(350f, 10f, 1f / 60f, OrbitCamera.Tau);
        n += Check(stepped > 350f && stepped < 360f,
                   "and one step of it goes UP through 360, not down through 180: " + stepped);
        return n;
    }

    // ---------------------------------------------------------------- the wheel, and what it aims at

    private static int ZoomCursor()
    {
        int n = 0;
        const float distance = 5f, fov = 50f, anchorX = 200f, anchorY = -120f;
        float before = BenchList.ZoomDefault;
        float after = BenchList.Wheel(before, 1f);
        n += Check(after < before, "a notch up is closer: " + after);

        float right, up;
        OrbitCamera.ZoomShift(before, after, anchorX, anchorY, distance, fov, H, out right, out up);

        // THE WHOLE POINT, stated as the picture the author sees: the bit of model that was under the
        // cursor is still under the cursor. A pixel at this depth is worth mpp metres; the pivot moved
        // by (right, up), so what was anchorX*mpp to the right of the pivot is now that minus right -
        // and a pixel is now worth mpp * after/before, because the distance shrank in step with the
        // margin (BenchList.Frame is linear in it).
        float mpp = 2f * distance * (float)Math.Tan(fov * 0.5 * Math.PI / 180.0) / H;
        float mppAfter = mpp * after / before;
        n += Check(Near((anchorX * mpp - right) / mppAfter, anchorX),
                   "the point under the cursor keeps its x: " + (anchorX * mpp - right) / mppAfter);
        n += Check(Near((anchorY * mpp - up) / mppAfter, anchorY),
                   "... and its y: " + (anchorY * mpp - up) / mppAfter);

        float cx, cy;
        OrbitCamera.ZoomShift(before, after, 0f, 0f, distance, fov, H, out cx, out cy);
        n += Check(Near(cx, 0f) && Near(cy, 0f), "a wheel dead centre moves the pivot not at all");

        float ox, oy;
        OrbitCamera.ZoomShift(before, BenchList.Wheel(before, -1f), anchorX, anchorY,
                              distance, fov, H, out ox, out oy);
        n += Check(ox < 0f, "zooming OUT walks the pivot away from the cursor: " + ox);
        return n;
    }

    // ---------------------------------------------------------------- F, in metres

    private static int Framing()
    {
        int n = 0;
        n += Check(Near(OrbitCamera.FrameZoom(2f, 2f), BenchList.ZoomDefault),
                   "framing the whole model is the default framing");
        n += Check(Near(OrbitCamera.FrameZoom(1f, 2f), BenchList.ZoomDefault * 0.5f),
                   "half the radius is half the margin: " + OrbitCamera.FrameZoom(1f, 2f));
        n += Check(Near(OrbitCamera.FrameZoom(0.0001f, 2f), BenchList.ZoomMin),
                   "a speck stops at the near clamp rather than at the camera's own eye");
        n += Check(Near(OrbitCamera.FrameZoom(500f, 2f), BenchList.ZoomMax), "and a huge one at the far");

        // The margin is only ever fed to BenchList.Frame with the WHOLE model's radius, so the claim
        // that has to hold is this one: framing a part at radius r produces the distance the whole
        // model would have been framed at if IT were r.
        float part, whole, ignored;
        BenchList.Frame(2f, 50f, W, H, Panel, 96f, OrbitCamera.FrameZoom(0.5f, 2f),
                        out part, out ignored, out ignored);
        BenchList.Frame(0.5f, 50f, W, H, Panel, 96f, BenchList.ZoomDefault,
                        out whole, out ignored, out ignored);
        n += Check(Math.Abs(part - whole) < 1e-3f,
                   "F stands the camera where a model that size would have stood: " + part + " vs " + whole);
        return n;
    }

    // ---------------------------------------------------------------- Home

    private static int Resetting()
    {
        int n = 0;
        var c = new OrbitCamera();
        c.OrbitBy(300f, -100f);
        c.WheelAt(3f, 400f, 400f);
        c.Tick(1f / 60f);
        c.Reset();
        n += Check(Near(c.Yaw, 0f) && Near(c.Pitch, 0f) && Near(c.Zoom, BenchList.ZoomDefault),
                   "Home puts the LIVE view back");
        n += Check(Near(c.YawTarget, 0f) && Near(c.PitchTarget, 0f) &&
                   Near(c.ZoomTarget, BenchList.ZoomDefault) && Near(c.AnchorX, 0f) && Near(c.AnchorY, 0f),
                   "... and the targets with it, or the damping would drag it straight back out");
        return n;
    }

    private static bool Near(float a, float b) { return Math.Abs(a - b) < 1e-3f; }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("ORBIT FAILURE: " + what);
        return 1;
    }
}
```

- [ ] **Step 2: Register it and watch it fail to compile.** In `tests\ObjCodecTests\ObjCodecTests.csproj`,
after the line `<Compile Include="BoneNames.cs" />`, add:

```xml
    <Compile Include="OrbitTests.cs" />
```

and immediately before `<Compile Include="..\..\src\Import\ImportRefused.cs" Link="ImportRefused.cs" />`,
add:

```xml
    <!-- The bench's own arithmetic. BenchList has carried no UnityEngine type since it was written (its
         Frame remark says so out loud), and OrbitCamera is held to the same rule, so which button means
         orbit, where the pitch stops and where the wheel puts the pivot are proven here instead of by
         dragging a mouse in a running game. -->
    <Compile Include="..\..\src\Dev\BenchList.cs" Link="BenchList.cs" />
    <Compile Include="..\..\src\Dev\OrbitCamera.cs" Link="OrbitCamera.cs" />
```

In `tests\ObjCodecTests\Program.cs`, after the line `Console.WriteLine(BoneNames.Run());` (`:116`), add:

```csharp
        Console.WriteLine(OrbitTests.Run());
```

Run `dotnet run --project tests\ObjCodecTests -c Release`. Expected: it does not build, with
`error CS0234: The type or namespace name 'OrbitCamera' does not exist in the namespace
'Morgott.ContentTool.Dev'` (and the same for `ViewGesture`). That is the failing test.

- [ ] **Step 3: Write the controller.** Create `src\Dev\OrbitCamera.cs`:

```csharp
using System;

namespace Morgott.ContentTool.Dev
{
    /// <summary>What one mouse gesture over the viewport means. There is deliberately no Pick member:
    /// the LEFT button is not the camera's to interpret, and the whole of this type's answer about it is
    /// None. Picking is done by whoever owns the pixels - FitGizmo's handles today, the Doctor's
    /// skeleton overlay in slice 2 - through IMGUI, where the click actually lands.</summary>
    internal enum ViewGesture { None, Orbit, Pan }

    /// <summary>
    /// ============ THE VIEWPORT CAMERA, AND WHY IT IS ONE CLASS WITH NO UNITY IN IT ============
    ///
    /// One controller for every bench tab. It is not a per-tab convenience: FitBench.Arm.Mouse() is the
    /// ONLY method in the mod that reads the mouse, every tab is drawn inside that one panel, and the
    /// only per-tab difference in the scheme is what a LEFT click picks - which this type answers by
    /// refusing to claim the left button at all.
    ///
    /// THE STATE IS A TARGET AND A LIVE VALUE. A gesture writes the target; <see cref="Tick"/> walks the
    /// live value toward it once per frame, so a drag eases in instead of landing in one frame. Only the
    /// live values are ever read back by FitBench.Reframe.
    ///
    /// NOT ONE UnityEngine TYPE, not even Mathf. Same rule, same reason, as the BenchList it delegates
    /// to: tests\ObjCodecTests references no Unity assembly, and a camera whose arithmetic can only be
    /// judged by eye in a running game is a camera whose pitch flips in a build nobody notices. The
    /// three lines that genuinely need a rotation (turning a screen-plane offset into a world vector)
    /// stay in FitBench, which is compiled only into the mod.
    ///
    /// THE GAINS ARE BenchList'S, unchanged: DegreesPerPixel = 0.2, PitchMin/PitchMax = -80/80,
    /// ZoomFactor = 0.12 (distance-proportional), ZoomDefault = 1.35, and the InvertX/InvertY session
    /// toggles, which now reach the MIDDLE-drag orbit because that is the only orbit there is.
    /// </summary>
    internal sealed class OrbitCamera
    {
        /// <summary>The damping time constant, in seconds: one Tau covers 63% of the distance left. 0.08
        /// is the design's own figure (section 8) - slow enough to be seen, fast enough that a drag does
        /// not feel like it is on a rope.</summary>
        internal const float Tau = 0.08f;

        /// <summary>Close enough to be there. Below this the live value SNAPS onto the target, which is
        /// what lets <see cref="Tick"/> report false and stops a settled camera reframing forever.</summary>
        internal const float Settled = 0.001f;

        /// <summary>The live view: what FitBench.Reframe reads. Yaw is wrapped to [0, 360), pitch is
        /// clamped to BenchList's band, zoom is the framing MARGIN (not a distance in metres - see
        /// BenchList.Frame).</summary>
        internal float Yaw, Pitch;
        internal float Zoom = BenchList.ZoomDefault;

        /// <summary>Where the gestures put it. The live values above are chasing these.</summary>
        internal float YawTarget, PitchTarget;
        internal float ZoomTarget = BenchList.ZoomDefault;

        /// <summary>The cursor's offset from the centre of the free region, in pixels, at the last wheel
        /// notch. It is remembered rather than consumed because the zoom is DAMPED: the pivot has to
        /// walk toward that point over the same frames the distance shrinks over, or the point under the
        /// cursor drifts while the easing plays.</summary>
        internal float AnchorX, AnchorY;

        // ---------------------------------------------------------------- what the gestures do

        /// <summary>One frame of orbit drag, in pixels, onto the TARGETS.</summary>
        internal void OrbitBy(float dxPixels, float dyPixels)
        {
            YawTarget = BenchList.Orbit(YawTarget, dxPixels);
            PitchTarget = BenchList.Tilt(PitchTarget, dyPixels);
        }

        /// <summary>One wheel notch, aimed at a point <paramref name="anchorX"/>/<paramref name="anchorY"/>
        /// pixels from the centre of the free region (the point Reframe aims the camera at). Pass 0,0 for
        /// the panel's own in/out buttons, which have no cursor to aim at.</summary>
        internal void WheelAt(float notches, float anchorX, float anchorY)
        {
            ZoomTarget = BenchList.Wheel(ZoomTarget, notches);
            AnchorX = anchorX;
            AnchorY = anchorY;
        }

        /// <summary>F: frame something of <paramref name="partRadius"/> inside a model measured at
        /// <paramref name="modelRadius"/>. The caller moves the pivot onto the part; this is the
        /// distance half of it, and framing the whole model is the ordinary default framing.</summary>
        internal void FrameOn(float partRadius, float modelRadius)
        {
            ZoomTarget = FrameZoom(partRadius, modelRadius);
            AnchorX = AnchorY = 0f;
        }

        /// <summary>Home: BOTH halves. Resetting only the live values would let the damping drag the view
        /// straight back to where the author could not see anything.</summary>
        internal void Reset()
        {
            Yaw = YawTarget = 0f;
            Pitch = PitchTarget = 0f;
            Zoom = ZoomTarget = BenchList.ZoomDefault;
            AnchorX = AnchorY = 0f;
        }

        /// <summary>One frame of easing. Returns whether anything actually moved, which is the caller's
        /// signal to re-compute the pose - a settled camera must not reframe every frame forever.</summary>
        internal bool Tick(float dt)
        {
            float yaw = DampAngle(Yaw, YawTarget, dt, Tau);
            float pitch = Damp(Pitch, PitchTarget, dt, Tau);
            float zoom = Damp(Zoom, ZoomTarget, dt, Tau);
            bool moved = Math.Abs(ShortWay(Yaw, yaw)) > 1e-4f ||
                         Math.Abs(pitch - Pitch) > 1e-4f ||
                         Math.Abs(zoom - Zoom) > 1e-6f;
            Yaw = yaw; Pitch = pitch; Zoom = zoom;
            return moved;
        }

        // ---------------------------------------------------------------- the arithmetic, all of it

        /// <summary>
        /// WHICH GESTURE A PRESS IS, decided once at the press and held for the drag - the same latch the
        /// old code kept, and for the same reason: a drag that changes its mind half way through because
        /// a modifier was released is a camera that jumps.
        ///
        /// The order is the whole table from the design's section 8: middle wins outright, the gizmo
        /// refuses left before any modifier is looked at, and a bare left press is nobody's.
        /// </summary>
        /// <param name="overViewport"><see cref="InViewport"/>, plus whatever else the adapter knows is
        /// covering those pixels (a floating list, an IMGUI control holding hotControl).</param>
        /// <param name="gizmoWouldGrab">FitGizmo.WouldGrab - asked DIRECTLY rather than through
        /// FitGizmo.Owns, because Update runs before OnGUI and on the frame of the press the handles have
        /// not claimed hotControl yet.</param>
        internal static ViewGesture Classify(bool overViewport, bool leftDown, bool middleDown,
                                             bool alt, bool shift, bool gizmoWouldGrab)
        {
            if (!overViewport) return ViewGesture.None;
            if (middleDown) return shift ? ViewGesture.Pan : ViewGesture.Orbit;
            if (!leftDown || gizmoWouldGrab) return ViewGesture.None;
            return alt ? ViewGesture.Orbit : ViewGesture.None;
        }

        /// <summary>
        /// THE VIEWPORT RECT, in the mouse's own convention (y from the BOTTOM, which is what
        /// Input.mousePosition hands over): right of the panel, above the transport strip, inside the
        /// window. This is the one place that band is defined - it replaced BenchList.OverScene and
        /// BenchList.OverStrip, whose only caller it is, and it keeps their exact semantics including
        /// the one that matters: a window with no room for a strip reserves no height for one
        /// (BenchList.StripReserve), so those pixels belong to the scene again.
        ///
        /// In IMGUI's own convention the same rect is
        /// <c>new Rect(panelW, 0, screenW - panelW, screenH - StripReserve(...))</c> and the test is
        /// <c>rect.Contains(Event.current.mousePosition)</c>; the adapter does not use that form because
        /// Event.current only exists inside OnGUI, while the drag deltas are accumulated in Update.
        /// </summary>
        internal static bool InViewport(float mouseX, float mouseY, float screenW, float screenH,
                                        float panelW)
        {
            if (float.IsNaN(mouseX) || float.IsNaN(mouseY)) return false;
            return mouseX > panelW && mouseX <= screenW &&
                   mouseY > BenchList.StripReserve(screenW, screenH, panelW) && mouseY <= screenH;
        }

        /// <summary>Exponential easing: after <paramref name="dt"/> seconds the remaining distance has
        /// shrunk by <c>1 - e^(-dt/tau)</c>. This is Mathf.SmoothDamp's shape without Mathf, without a
        /// velocity field to carry and without a spring that can overshoot - and it is frame-rate
        /// independent, which a plain <c>current += (target - current) * 0.2f</c> is not.</summary>
        internal static float Damp(float current, float target, float dt, float tau)
        {
            if (float.IsNaN(current) || float.IsNaN(target)) return target;
            if (dt <= 0f) return current;
            if (tau <= 0f) return target;
            float next = current + (target - current) * (float)(1.0 - Math.Exp(-dt / tau));
            return Math.Abs(target - next) <= Settled ? target : next;
        }

        /// <summary>The same easing on a WRAPPED angle. Damping 350 toward 10 as plain numbers sweeps the
        /// model 340 degrees the wrong way round every time a drag crosses zero.</summary>
        internal static float DampAngle(float current, float target, float dt, float tau)
        {
            return BenchList.WrapYaw(current + Damp(0f, ShortWay(current, target), dt, tau));
        }

        /// <summary>Signed degrees from one heading to another, the short way: (-180, 180].</summary>
        internal static float ShortWay(float from, float to)
        {
            if (float.IsNaN(from) || float.IsNaN(to)) return 0f;
            float d = (to - from) % 360f;
            if (d > 180f) d -= 360f;
            if (d < -180f) d += 360f;
            return d;
        }

        /// <summary>
        /// ZOOM TOWARD THE CURSOR, as the offset the PIVOT has to take so the bit of model under the
        /// cursor stays under the cursor.
        ///
        /// At the pivot's depth a pixel is worth <c>2 * distance * tan(fov/2) / screenHeight</c> metres -
        /// the same algebra BenchList.Frame and FitBench.PanBy are built on. The point under the cursor
        /// therefore sits <c>anchor * mpp</c> from the pivot. BenchList.Frame is LINEAR in the margin, so
        /// after the notch the distance is <c>after/before</c> of what it was and a pixel is worth that
        /// much less; for the point to keep its pixel the pivot must close the difference, which is
        /// <c>anchor * mpp * (1 - after/before)</c>. Positive = toward the cursor, which is what zooming
        /// IN does.
        ///
        /// The sign is the opposite of FitBench.PanBy's on purpose: a pan GRABS the world and drags it
        /// with the hand, a zoom FOLLOWS the cursor.
        /// </summary>
        /// <param name="right">Metres to add to the pivot along the camera's own right.</param>
        /// <param name="up">Metres to add along the camera's own up.</param>
        internal static void ZoomShift(float before, float after, float anchorX, float anchorY,
                                       float distance, float fovDeg, float screenH,
                                       out float right, out float up)
        {
            right = up = 0f;
            if (before <= 0f || after <= 0f || float.IsNaN(before) || float.IsNaN(after) ||
                float.IsNaN(anchorX) || float.IsNaN(anchorY)) return;
            // The same guards Frame applies, for the same reason: these arrive from a live game.
            if (screenH < 1f) screenH = 1f;
            if (fovDeg < 1f) fovDeg = 1f;
            if (fovDeg > 175f) fovDeg = 175f;
            if (distance < 0.01f) distance = 0.01f;

            float mpp = (float)(2.0 * distance * Math.Tan(fovDeg * 0.5 * Math.PI / 180.0) / screenH);
            float k = 1f - after / before;
            right = anchorX * mpp * k;
            up = anchorY * mpp * k;
        }

        /// <summary>The framing margin that shows something of <paramref name="partRadius"/> the way the
        /// default margin shows a whole model of <paramref name="modelRadius"/>. Clamped, because F on a
        /// fingertip must not put the camera inside the geometry with nothing on screen to say why.</summary>
        internal static float FrameZoom(float partRadius, float modelRadius)
        {
            if (float.IsNaN(partRadius) || float.IsNaN(modelRadius) ||
                partRadius <= 0f || modelRadius < 1e-4f) return BenchList.ZoomDefault;
            return BenchList.Clamp(BenchList.ZoomDefault * (partRadius / modelRadius),
                                   BenchList.ZoomMin, BenchList.ZoomMax);
        }
    }
}
```

- [ ] **Step 4: Run and expect PASS.** `dotnet run --project tests\ObjCodecTests -c Release`. Expected:
a line

```
ORBIT PASS, 38 check(s) - gestures, the pitch clamp, zoom toward the cursor, framing and the damping
```

and exit 0, with every pre-existing gate (`OBJ: ALL PASS`, `BONE-NAMES PASS`, `BINDER-FROZEN PASS`,
`DECISION PASS`, `ALIAS PASS`, `PREFLIGHT PASS`, …) unchanged. Nothing in `src\` has been rewired yet, so
a red line here is this task's own arithmetic, not a regression.

- [ ] **Step 5: Build the mod, so the new file is proven to compile inside the real assembly too.**
`dotnet build -c Release`. Expected: `Build succeeded`, 0 errors, 0 warnings about `OrbitCamera` being
unused (it is `internal` and referenced by nothing yet — that is legal and the next task fixes it).

- [ ] **Step 6: Commit.**

```
git add src/Dev/OrbitCamera.cs tests/ObjCodecTests/OrbitTests.cs tests/ObjCodecTests/ObjCodecTests.csproj tests/ObjCodecTests/Program.cs
git commit -m "feat(dev): one OrbitCamera for the bench viewport, with its arithmetic proven offline"
```

---

### Task 2: The bench drives through it — MMB orbit, Shift+MMB pan, wheel toward the cursor

Behaviour parity for what already worked is the bar: the wheel still zooms proportionally, the pan is
still the same screen-plane algebra, the panel's own in/out/up/down/RECENTRE/RESET VIEW buttons still do
what they did, the gizmo still gets first refusal on a left press, and the InvertX/InvertY toggles still
flip the orbit. What CHANGES is which button starts which gesture, that the wheel now aims at the cursor,
and that the values ease. What GOES is the right-drag model turn.

**Files:**
- Modify: `src\Dev\FitBench.cs`, `src\Dev\BenchList.cs`
- Test: `tests\ObjCodecTests\OrbitTests.cs` (task 1, unchanged — it is the oracle for the arithmetic;
  the wiring is proven by `dotnet build` and by task 4 in game)

- [ ] **Step 1: Replace the three view fields with the controller.** In `src\Dev\FitBench.cs`, replace
lines 314-321 (the `zoom`/`lift` declaration, its remark, and the `yaw`/`pitch` declaration and its
remark) with:

```csharp
        /// <summary>The lift knob, remembered for the session and nowhere else. Framing is an eyeball
        /// judgement - the algebra puts the unit inside the free region, it cannot know how much air
        /// around him reads as "well framed" - so it is nudged live, in RADII.</summary>
        private static float lift = 0f;
        /// <summary>
        /// THE VIEW ITSELF: yaw about world up, pitch about the camera's own right, and zoom as the
        /// framing margin, all as offsets ON TOP of the bay's own authored look direction. One instance,
        /// shared by every tab, because there is one camera and one viewport however many tabs are drawn
        /// into the panel beside it.
        ///
        /// Its values are DAMPED (OrbitCamera.Tick, driven from Arm.Update), so every gesture writes a
        /// target and the pose eases toward it. Nothing outside Arm may write them: RESET VIEW goes
        /// through Reset, the panel's buttons through WheelAt, the mouse through OrbitBy.
        /// </summary>
        private static readonly OrbitCamera view = new OrbitCamera();
```

- [ ] **Step 2: Point `Reframe` at it.** In the same file, in `Reframe`, replace `zoom` on line 755 with
`view.Zoom`, so the call reads:

```csharp
                                view.Zoom, out distance, out lateral, out vertical);
```

and replace line 763 with:

```csharp
                Quaternion rot = Quaternion.Euler(0f, view.Yaw, 0f) * look *
                                 Quaternion.Euler(view.Pitch, 0f, 0f);
```

- [ ] **Step 3: Reset through it.** Replace line 530 with:

```csharp
            view.Reset(); lift = 0f; pan = Vector3.zero;
```

- [ ] **Step 4: Add the cursor-anchored half of the wheel.** In the same file, immediately after `PanBy`
(after line 804, the closing brace), add:

```csharp
        /// <summary>
        /// The other half of a wheel notch: the PIVOT's own step toward the point under the cursor, so
        /// that point does not slide out from under it as the camera comes in. The metres come from
        /// <see cref="OrbitCamera.ZoomShift"/>; the only thing done here is turning them into a world
        /// offset with the camera's own right and up, exactly as <see cref="PanBy"/> does.
        ///
        /// It is called ONCE PER FRAME with the zoom step that frame actually took, not once per notch,
        /// because the zoom is damped: the pivot has to travel over the same frames the distance shrinks
        /// over. <c>frameDist</c> is the distance the LAST Reframe computed, which is why this runs
        /// before the Reframe that follows it and not after.
        /// </summary>
        private static void ZoomAnchor(float before, float after)
        {
            if (cam == null || !framed || Math.Abs(after - before) < 1e-6f) return;
            float right, up;
            OrbitCamera.ZoomShift(before, after, view.AnchorX, view.AnchorY,
                                  frameDist, cam.fieldOfView, Screen.height, out right, out up);
            pan += (frameRot * Vector3.right) * right + (frameRot * Vector3.up) * up;
        }
```

If `src\Dev\FitBench.cs` does not already have `using System;` among its usings, add it (it does — the
file catches `Exception` throughout; verify before editing).

- [ ] **Step 5: The panel's zoom buttons write the target.** Replace lines 1412-1413 with:

```csharp
                if (GUILayout.Button("in"))      view.WheelAt(1f, 0f, 0f);
                if (GUILayout.Button("out"))     view.WheelAt(-1f, 0f, 0f);
```

They no longer call `Reframe` themselves: `Arm.Update` reframes for the whole damping, and a second
caller would draw one frame at a value the damping has not reached. The 0,0 anchor is the centre of the
free region, which is where these buttons have always zoomed.

- [ ] **Step 6: Latch IMGUI's hot control.** In `src\Dev\FitBench.cs`, after the `typing` field
(`:1297`), add:

```csharp
        /// <summary>Whether any IMGUI control held <c>hotControl</c> during the LAST OnGUI pass, i.e.
        /// "some control has grabbed the mouse and this drag is its". It is the gate that lets a control
        /// DRAWN OVER the viewport - a gizmo handle, the Doctor's future overlay buttons - keep a drag
        /// the rect test would otherwise hand to the camera.
        ///
        /// One frame old on purpose, and that is safe here but not everywhere: Unity runs every Update
        /// before any OnGUI, so on the frame of a PRESS nothing has claimed hotControl yet. That is
        /// exactly why FitGizmo is also asked directly (WouldGrab) at the press - see Mouse().</summary>
        private static bool guiHot;
```

and in `Arm.OnGUI`, immediately after `FitAnim.Draw(PanelWidth);` (`:1990`), add:

```csharp
                    // AFTER everything has drawn: whoever took the mouse this pass has taken it by now.
                    guiHot = GUIUtility.hotControl != 0;
```

- [ ] **Step 7: Rewrite `Mouse()`.** Replace the whole of `src\Dev\FitBench.cs` lines 1827-1911 (the
XML remark and the entire `Mouse` method) with:

```csharp
            /// <summary>
            /// ============ THE MOUSE, AND THE ONE PLACE IT IS ALLOWED TO ACT ============
            ///
            /// The 3D-editor scheme, identical on every tab: MIDDLE-drag orbits about the focus point,
            /// SHIFT+MIDDLE pans, the wheel zooms TOWARD THE CURSOR, ALT+LEFT orbits for a mouse with no
            /// middle button, and the LEFT button is left alone so it can PICK - FitGizmo's handles
            /// today, the Doctor's skeleton overlay in slice 2. Right-drag no longer turns the model:
            /// orbiting answers the same question ("how does the far side look") without a second piece
            /// of state for RESET VIEW to put back, and the bay's own SceneRoot rotation is now touched
            /// by nothing but Close and RESET VIEW.
            ///
            /// THREE GATES, and all three are load-bearing:
            ///   - the pointer is inside the VIEWPORT RECT (OrbitCamera.InViewport: right of the panel,
            ///     above the transport strip), or dragging the panel's own scrollbar would swing the
            ///     camera and a wheel over the weapon list would scroll the list AND zoom;
            ///   - no floating list is under it (FitAnim.OverList) - the clip list opens UPWARD out of
            ///     the strip and over the scene, so its pixels are not the viewport's;
            ///   - no IMGUI control holds hotControl (guiHot, latched at the end of OnGUI), which is how
            ///     a control drawn OVER the viewport says "this drag is mine".
            ///
            /// WHICH GESTURE is decided once, at the press (OrbitCamera.Classify), and held for the
            /// whole drag: a drag that changed its mind because Shift was let go half way through is a
            /// camera that jumps. That latch is also what keeps a drag begun on the panel from grabbing
            /// the camera when the pointer wanders over the model.
            ///
            /// Nothing here moves the view. Every gesture writes a TARGET and Arm.Update's one
            /// OrbitCamera.Tick walks the live values toward it, which is the whole of the easing.
            /// </summary>
            private void Mouse()
            {
                float mx = Input.mousePosition.x, my = Input.mousePosition.y;
                bool over = OrbitCamera.InViewport(mx, my, Screen.width, Screen.height, PanelWidth) &&
                            !FitAnim.OverList(mx, my) && !guiHot;

                float wheel = Input.mouseScrollDelta.y;
                if (over && Mathf.Abs(wheel) > 0.01f)
                {
                    // The anchor is the cursor's offset from the centre of the FREE REGION, because that
                    // is the point Reframe aims at - the lateral and vertical offsets it computes are
                    // exactly what puts the aim point in the middle of the part of the screen the panel
                    // and the strip do not cover.
                    view.WheelAt(wheel,
                                 mx - (PanelWidth + Screen.width) * 0.5f,
                                 my - (BenchList.StripReserve(Screen.width, Screen.height, PanelWidth) +
                                       Screen.height) * 0.5f);
                }

                if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(2))
                {
                    // THE GIZMO GETS FIRST REFUSAL ON A LEFT PRESS, and it has to be ASKED rather than
                    // consulted through FitGizmo.Owns: Unity runs every Update before any OnGUI, so on
                    // the frame of the press the handles have not claimed hotControl yet and Owns - like
                    // guiHot - is still false.
                    gesture = OrbitCamera.Classify(
                        over, Input.GetMouseButton(0), Input.GetMouseButton(2),
                        Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt),
                        Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift),
                        FitGizmo.WouldGrab(mx, my));
                    // Clicking the model is how a user says "I am done typing" - IMGUI will not work
                    // that out on its own, and a filter that keeps the keyboard keeps the fly keys.
                    if (over) dropFocus = true;
                    lastX = mx; lastY = my;
                }
                // And for every frame AFTER the press, the latched claim is the answer.
                if (FitGizmo.Owns) { gesture = ViewGesture.None; return; }
                if (!Input.GetMouseButton(0) && !Input.GetMouseButton(2))
                { gesture = ViewGesture.None; return; }
                if (gesture == ViewGesture.None) return;

                float dx = mx - lastX, dy = my - lastY;
                lastX = mx; lastY = my;
                if (Mathf.Abs(dx) < 0.01f && Mathf.Abs(dy) < 0.01f) return;

                if (gesture == ViewGesture.Pan) { PanBy(dx, dy); return; }
                view.OrbitBy(dx, dy);
            }
```

- [ ] **Step 8: Replace the drag latch's field.** In `Arm`, replace lines 1766-1767 with:

```csharp
            /// <summary>Which gesture the drag in progress is, latched at the press. See <see cref="Mouse"/>.</summary>
            private ViewGesture gesture;
```

- [ ] **Step 9: Tick the damping.** In `Arm.Update`, replace lines 1813-1815 with:

```csharp
                    if (!open || bay == null || bay.SceneRoot == null) return;
                    Mouse();
                    Fly();
                    // THE ONE PLACE THE VIEW IS RECOMPUTED for the mouse: everything above only wrote
                    // targets. ZoomAnchor runs first because it needs the distance the LAST Reframe
                    // computed, and Reframe is about to replace it; and both run only when something
                    // actually moved, so a settled camera costs one subtraction a frame.
                    float wasZoom = view.Zoom;
                    if (view.Tick(Time.deltaTime)) { ZoomAnchor(wasZoom, view.Zoom); Reframe(); }
```

- [ ] **Step 10: Tell the panel the truth.** Replace lines 1470-1481 (the `else` branch's `GUILayout.Label`
call) with:

```csharp
                GUILayout.Label(
                    (FitGizmo.Live ? "ARROWS on the gun = move it, RINGS = turn it about that axis (Esc " +
                                     "cancels; a dimmed handle is edge-on to the camera). " : "handles OFF. ") +
                    "MIDDLE-drag = orbit (Alt+left too), SHIFT+middle = pan, wheel = zoom at the " +
                    "cursor, F = frame, Home = reset, WASD/QE (Shift = faster) = fly." +
                    (advanced
                        ? "  x" + view.Zoom.ToString("0.00", CultureInfo.InvariantCulture) +
                          " lift " + lift.ToString("0.00", CultureInfo.InvariantCulture) +
                          " yaw " + view.Yaw.ToString("0", CultureInfo.InvariantCulture) +
                          " pitch " + view.Pitch.ToString("0", CultureInfo.InvariantCulture) +
                          " r " + frameRadius.ToString("0.00", CultureInfo.InvariantCulture) + "m"
                        : ""));
```

The row count is unchanged — still exactly one label, which is what `BenchList.Rows`' height budget is
counted against (`FitBench.cs:1455-1466`).

- [ ] **Step 11: Delete the two dead rect helpers.** In `src\Dev\BenchList.cs`, delete lines 684-689
(`OverScene` and its remark) and lines 378-393 (`OverStrip` and its remark). `OrbitCamera.InViewport` is
their only surviving caller's answer and keeps their semantics; `StripShown`, `StripReserve` and
`StripTop` all stay, because `Reframe`, `FitGizmo.Gui` and `FitAnim` still ask them. Verify with:

```
Select-String -Path E:\DEV\PhoenixPoint\ContentTool\src -Pattern "OverScene|OverStrip" -Recurse
```

Expected: no matches at all (the two remaining `<see cref="BenchList.OverScene"/>`-style mentions were
in the `Mouse()` remark that step 7 replaced and in `FitAnim.cs:501`, which must be re-pointed — change
`(BenchList.OverStrip)` there to `(OrbitCamera.InViewport)` and re-run the search until it is clean).

- [ ] **Step 12: Build and re-run the suite.** `dotnet build -c Release` — expected `Build succeeded`,
0 errors. Then `dotnet run --project tests\ObjCodecTests -c Release` — expected exit 0, `ORBIT PASS, 38
check(s) …` still printed, and every other gate unchanged.

- [ ] **Step 13: Commit.**

```
git add src/Dev/FitBench.cs src/Dev/BenchList.cs src/Dev/FitAnim.cs
git commit -m "feat(dev): 3D-editor viewport gestures - middle orbits, shift+middle pans, wheel zooms at the cursor"
```

---

### Task 3: `F` frames and `Home` resets

**Files:**
- Modify: `src\Dev\FitBench.cs:806-834` (`Fly`)
- Test: `tests\ObjCodecTests\OrbitTests.cs` (the arithmetic both keys use — `FrameZoom` and `Reset` —
  is already asserted there); the wiring by `dotnet build` and by task 4 in game

- [ ] **Step 1: Give the keyboard its two hotkeys.** In `src\Dev\FitBench.cs`, replace the remark and the
first four lines of `Fly` (lines 806-825, from `/// <summary>` down to and including `if (typing) return;`)
with:

```csharp
        /// <summary>
        /// THE KEYBOARD: F frames, Home resets, WASD/QE fly. Read raw from <c>UnityEngine.Input</c> like
        /// every other gesture here - the game itself cannot hear any of it, because the bench holds
        /// <c>InputController.IncDisableHandlersCalling</c> for as long as it is open (see
        /// <see cref="input"/>).
        ///
        /// The one thing it MUST stand aside for is IMGUI's own keyboard: the panel has two text filters
        /// and a scale field, and typing "assault" into one of them would otherwise fly the camera on
        /// every 'a' and 's' - and frame the view on every 'f'.
        ///
        /// HOME IS ASKED BEFORE THE framed GUARD, on purpose. It is the panic button, and the state it
        /// exists to rescue - nothing on screen, no way back - is exactly the state in which there is no
        /// frame to have. F is not: framing something that was never measured has no answer.
        /// </summary>
        private static void Fly()
        {
            // BY NAME, not by "is anything focused". IMGUI keeps keyboardControl on the text field long
            // after the pointer has left it - clicking the scene does not clear it - so a blanket
            // "keyboardControl != 0" guard switched flying off for the rest of the session the first
            // time a filter was typed in. Only the three fields are allowed to eat these keys, and a
            // press on the scene drops their focus (see dropFocus).
            if (typing) return;

            if (Input.GetKeyDown(KeyCode.Home))
            {
                message = ResetView();
                ContentToolMain.Say(message);
                return;
            }
            // F FRAMES. There is no selection to frame yet - the Doctor's skeleton overlay lands in
            // slice 2 - so today it frames the whole measured model: the default margin back, the pan
            // back to the measured centre, the orbit left exactly as it was dialled in. When the overlay
            // ships, the selected bone's radius and centre are what FrameOn and pan are given instead;
            // OrbitCamera.FrameZoom already takes both radii for that reason.
            if (Input.GetKeyDown(KeyCode.F) && framed)
            {
                view.FrameOn(frameRadius, frameRadius);
                Recentre();
            }

            if (cam == null || !framed) return;
```

Leave the rest of `Fly` (the WASD/QE block, lines 826-834) exactly as it is.

- [ ] **Step 2: Build.** `dotnet build -c Release`. Expected: `Build succeeded`, 0 errors. If
`ContentToolMain.Say` is not already reachable from this file, it is — `Reframe`'s neighbour `Arm.Update`
calls it at `:1791` in the same class tree; do not add a using.

- [ ] **Step 3: Re-run the suite.** `dotnet run --project tests\ObjCodecTests -c Release`. Expected: exit
0, `ORBIT PASS, 38 check(s) …` unchanged — no `src\Dev\FitBench.cs` code is linked into it, so this step
is a regression check on the other gates, not on the keys.

- [ ] **Step 4: Commit.**

```
git add src/Dev/FitBench.cs
git commit -m "feat(dev): F frames the model and Home resets the view"
```

---

### Task 4: In-game acceptance through PPCLI, and the owner's visual check

PPCLI is a SEPARATE project (`E:\DEV\PhoenixPoint\PPCLI\`). We are its CONSUMER: never edit it, never
commit to it. If it misbehaves, append an entry to `E:\DEV\PhoenixPoint\PPCLI\ISSUES.md` (attempted →
happened → expected → evidence → severity) and work around it.

**What an agent can and cannot prove here.** The 2026-09-02 Doctor run recorded the constraint plainly:
*IMGUI cannot be clicked through PPCLI* (`2026-09-01-model-doctor-plan.md:2847`). No middle-drag, no
Shift+MMB, no wheel can be delivered to the game from a command line. So the split is: the agent proves
the WIRING by driving the very same functions the mouse drives, through reflection on the live instance,
and screenshotting the result; the OWNER proves the FEEL, with a hand on a real mouse. Both are named
below; do not report the second as done.

**Files:**
- Modify: none in `src\`, unless a defect is found — then a fix, its own commit, and the offline suite
  re-run
- Test: the live game on `D:\PP-Instance2`

- [ ] **Step 1: Read the playbook first.** Read `E:\DEV\PhoenixPoint\PPCLI\PLAYBOOK.md` and take the exact
command lines for: running a console command against a running game (`connect console`), evaluating an
expression on the live instance (`connect call`), and `connect screenshot`. Do not dig PPCLI source; do
not invent a command line.

- [ ] **Step 2: Deploy this build to the automation install.** `dotnet build -c Release`, then copy
`bin\Release\ContentTool\` into `D:\PP-Instance2`'s `Mods\ContentTool\` using the repo's own `deploy.ps1`
(read it first for its parameters). NEVER target `D:\Steam\steamapps\common\Phoenix Point` — that is the
user's own game. Expected: the copied `ContentTool.dll` timestamp matches the build you just made.

- [ ] **Step 3: Open the bench.** With the game running on Instance2 and `connect state` actually
answering (wait for it — querying a still-initialising game hangs for minutes and looks like an engine
bug), run `ct_bench` through `connect console` and pick a unit. Then `connect screenshot`. Expected: a
PNG whose path the reply names, showing the panel and the new hint line reading
`MIDDLE-drag = orbit (Alt+left too), SHIFT+middle = pan, wheel = zoom at the cursor, F = frame, Home =
reset, WASD/QE (Shift = faster) = fly.` — the old line's `drag = orbit … right-drag = turn the model`
must be gone. This is the one thing a screenshot can settle on its own.

- [ ] **Step 4: The orbit, and the proof that it is damped.** Get the controller once —
`AccessTools.Field(typeof(FitBench), "view").GetValue(null)` on the `Morgott.ContentTool.Dev.FitBench`
type from the loaded `ContentTool` assembly — and in ONE call expression: read `Yaw` and `YawTarget`,
call `OrbitBy(225f, 0f)`, read them again. Expected: `Yaw` unchanged and `YawTarget` 45 degrees further
on, in the same frame, because a gesture writes only the target. Then a SECOND call a moment later
reading both again. Expected: `Yaw` has arrived at `YawTarget`. Those two readings are the damping — a
still screenshot cannot show motion, and this can. `connect screenshot` after the second reading, and
confirm the model is seen from 45 degrees round.

- [ ] **Step 5: The pitch clamp, under continuous drag.** Call `OrbitBy(0f, -1000f)` forty times in one
expression, then read `PitchTarget` and `Pitch`. Expected: both exactly `80`, the design's `PitchMax`,
and the screenshot shows the unit from above with the model upright — no flip, no upside-down camera.
Repeat with `+1000f` eighty times and expect `-80`.

- [ ] **Step 6: The wheel at the cursor.** Read `frameDist` and `pan` (both private statics on
`FitBench`), then in one expression call `WheelAt(3f, 400f, 0f)` — three notches with the cursor 400 px
right of the free region's centre — and let a few frames pass. Expected: `view.Zoom` has fallen by
roughly `0.88^3` of its previous value, `frameDist` with it, and `pan` has moved along the camera's own
right rather than staying put. `connect screenshot`: the part of the model that was 400 px right of
centre is still about 400 px right of centre, and larger. That last sentence is the acceptance criterion
and it is judged from the two screenshots side by side.

- [ ] **Step 7: F and Home.** Call `view.OrbitBy(300f, -80f)` and `view.WheelAt(6f, 300f, 200f)` to leave
the view somewhere awkward, screenshot it, then invoke the two keys' own code paths —
`AccessTools.Method(typeof(FitBench), "Recentre")` after `view.FrameOn(frameRadius, frameRadius)` for F,
and `AccessTools.Method(typeof(FitBench), "ResetView")` for Home — screenshotting after each. Expected:
after F the whole model is framed with the orbit angle preserved; after Home the view is the one a fresh
open gives, `view.Yaw`/`view.Pitch` are 0 and `view.Zoom` is `1.35`.

- [ ] **Step 8: No regression on the gizmo, and no exceptions.** With the FIT tab open and handles on,
confirm `FitGizmo.WouldGrab` still answers true over a handle and that `OrbitCamera.Classify(true, true,
false, false, false, true)` is `None` on the live assembly (call it directly — it is `internal static`
and reflection reaches it). Then grep
`%USERPROFILE%\AppData\LocalLow\Snapshot Games Inc\Phoenix Point\Player.log` for `Exception` and for
`Getting control ... position in a group with only ... controls` between the first and last screenshot.
Expected: no new entries of either.

- [ ] **Step 9: Hand the feel to the owner.** Report, in the session, exactly this list as the OWNER
VISUAL CHECK, with the install (`D:\PP-Instance2`), the build id and the screenshot folder, and say
plainly that PPCLI cannot deliver mouse gestures to IMGUI so these six are the ones no agent can sign
off:
  1. MMB-drag orbits, and it eases rather than snapping.
  2. Shift+MMB pans in the screen plane.
  3. Alt+LMB orbits identically to MMB.
  4. The wheel zooms toward the cursor — the thing under the pointer stays under the pointer.
  5. LMB still grabs a gizmo arrow on the FIT tab and does NOT orbit anywhere else.
  6. `F` frames and `Home` resets, and neither fires while a text filter has the keyboard.

- [ ] **Step 10: Record the run.** Append to
`internal-docs\planning\2026-09-02-prototype-picker-design.md` a short `### Viewport controls acceptance
run` subsection under section 8: date, install, build id, the table of steps 3-8 with PASS/FAIL and the
evidence for each, the screenshot paths, and the owner list from step 9 marked as outstanding until the
owner answers. Evidence from the real run only — no step may be recorded as PASS on the strength of the
code reading right.

- [ ] **Step 11: Commit.**

```
git add internal-docs/planning/2026-09-02-prototype-picker-design.md
git commit -m "docs(planning): record the viewport-controls in-game acceptance run"
```

---

## Self-review

**Section 8 coverage — every row of the design's two tables lands in a task**

| Design (section 8) | Task |
|---|---|
| MMB drag = orbit about the focus point, `DegreesPerPixel = 0.2`, clamp `[-80, 80]` | 1 (`OrbitBy`, `Classify`, the clamp gate), 2 (wiring) |
| Shift+MMB = pan, through `FitBench.PanBy` | 1 (`Classify`), 2 (`ViewGesture.Pan` branch) |
| Wheel = zoom toward cursor, distance-scaled (`ZoomFactor = 0.12`) | 1 (`ZoomShift` + its fixed-point gate), 2 (`WheelAt` anchor, `ZoomAnchor`) |
| Alt+LMB = orbit, MMB fallback | 1 (`Classify`), 2 |
| LMB click = pick; `FitGizmo.WouldGrab` priority preserved | 1 (`Classify` returns `None` for LMB), 2 (the press asks the gizmo directly) |
| F = frame selection, else the whole model | 1 (`FrameZoom` + the distance gate), 3 |
| Home = reset, equal to `FitBench.cs:530` | 1 (`Reset`), 3 |
| Smooth damping ~0.08 s on orbit and zoom | 1 (`Damp`/`DampAngle`/`Tick` + convergence gate), 2 (`Tick` in `Update`) |
| No gimbal flip, pitch clamp kept as-is | 1 (`BenchList.Tilt` unchanged, clamp gate) |
| Same scheme on every tab, driven by `FitBench.Arm.Mouse` | 2 (there is only one `Mouse`; `ModelDoctor` has no input code) |
| Migration: LMB-orbit → MMB-orbit, RMB-turn removed, hint text updated, Invert toggles kept | 2 (steps 7, 10) |
| Existing helpers reused (`Orbit`, `Tilt`, `Wheel`, `PanBy`, `WouldGrab`) | 1 (delegation), 2 |
| Acceptance verified in game | 4 |

**Three places this plan does not do what section 8 literally says, and why**

1. **`Mathf.SmoothDamp` → exponential damping over `Math.Exp`.** Same curve, no velocity state, and it
   keeps the core UnityEngine-free so the offline EXE can run it at all. The design's requirement is
   "orbit does not snap to final position in one frame"; the gate asserts exactly that.
2. **No `Vector3`/`Quaternion`/`Bounds` in the controller.** Same reason. The focus point stays in
   `FitBench.pan` (a `Vector3` in world space, as it already is) and the controller speaks in the two
   scalars — metres along the camera's own right and up — that the caller turns into it.
3. **`ViewGesture` has no `Pick` member.** Nothing picks on LMB today except `FitGizmo`, which does its
   own IMGUI hit test; a `Pick` the camera returns and nobody consumes is dead code that would have to be
   re-designed the moment the Doctor's overlay is real. What section 8 actually requires — that the
   camera never claims a bare left drag — is asserted in task 1.

**Placeholder scan.** No "TBD", no "similar to task N", no "add the rest here". Every step names the file,
the exact text to insert or replace, the command to run and what it prints. The two judgements left to
the worker are flagged where they occur: whether `FitBench.cs` already carries `using System;` (task 2
step 4) and the `FitAnim.cs:501` comment that mentions the deleted `OverStrip` (task 2 step 11).

**Build-green ordering.** Task 1 adds a file nothing references (legal, and the suite is green at the end
of it). Task 2 makes it the only view state there is and deletes the two helpers it replaced, in one
commit, so the bench is never half-converted. Task 3 adds two keys onto a controller that already exists.
Task 4 changes no code unless it finds a defect.

---

## Task 4 acceptance run - 2026-09-02, D:\PP-Instance2

Install `D:\PP-Instance2`, ContentTool `1.1.3.0` `build=c0869416`, PPBridge `build=2d9f4a41`,
deployed with the repo's own `.\deploy.ps1`. Geoscape from `plans\start-campaign.json`, bench from
`connect console '{"command":"ct_bench","args":["open"]}'`, FIT tab. PPCLI cannot deliver a mouse
gesture to IMGUI, so the wiring was driven through the very functions the mouse drives -
`AccessTools.Field(typeof(FitBench),"view").GetValue(null)` and then `OrbitBy` / `WheelAt` /
`FrameOn` on the live `OrbitCamera`, plus `AccessTools.Method(typeof(FitBench),"Recentre"/"ResetView")`
- each as one `connect plan` with an inline plan file kept outside the PPCLI repo.

| Step | Action | Expected | Observed | Verdict |
|---|---|---|---|---|
| 3 | read the hint line off the panel | the new scheme, the old `right-drag = turn the model` gone | `handles OFF. MIDDLE-drag = orbit (Alt+left too), SHIFT+middle = pan, wheel = zoom at the cursor, F = frame, Home = reset, WASD/QE (Shift = faster) = fly.` (`view-01-hintline.png`, also visible in `proto-baseline-01.png`) | PASS |
| 4 | `OrbitBy(225, 0)`, read twice | a gesture writes only the TARGET; the live value arrives later | in the same plan: `Yaw` 0.0 with `YawTarget` 45.0; after a 1200 ms wait `Yaw` 45.0 / `YawTarget` 45.0 (`view-02-orbit45.png`) | PASS |
| 5 | 40x `OrbitBy(0, -1000)`, then 80x `OrbitBy(0, +1000)` | stops at the band, no flip | `Pitch` 80.0 / `PitchTarget` 80.0, then `Pitch` -80.0 / `PitchTarget` -80.0 (`view-03-pitch-bottom.png`) | PASS |
| 6 | `WheelAt(3, 400, 0)` | zoom in, distance with it, pivot walks toward the cursor | `Zoom` 1.35 -> 0.864, `frameDist` 2.70912457 -> 1.73383975, `pan` (0,0,0) -> (-0.333569676, -1.34e-08, -0.300347567), `AnchorX` 400 (`view-04-wheel-before.png` / `view-05-wheel-after.png`) | PASS |
| 7 | F, then Home, from an awkward view | F keeps the orbit and re-frames; Home is a fresh open | after `OrbitBy(300,-80)` + `WheelAt(6,300,200)` (`view-06-awkward.png`): F (`view.FrameOn(frameRadius, frameRadius)` + `FitBench.Recentre`) -> `Zoom` back to 1.35 with `Yaw` 105 / `Pitch` -64 preserved (`view-07-after-F.png`); Home (`FitBench.ResetView`) -> `Yaw` 0, `Pitch` 0, `Zoom` 1.35, `YawTarget` 0, `ZoomTarget` 1.35, and it answered *"ct_bench: view RESET - zoom, lift, orbit, the animation transport and the bay's own rotation back to default, scene and lighting re-asserted, camera re-taken and re-measured."* (`view-08-after-Home.png`) | PASS |
| 8a | `OrbitCamera.Classify` on the LIVE assembly | left is nobody's when the gizmo would grab; middle orbits; shift+middle pans | `Classify(true,true,false,false,false,true)` -> `None`; `Classify(true,false,true,false,false,false)` -> `Orbit`; `Classify(true,false,true,false,true,false)` -> `Pan` | PASS |
| 8b | `FitGizmo.WouldGrab` over a handle | true over an arrow | **NOT EXERCISED** - the session had no weapon fitted, so the panel read `handles OFF` and there was no handle to be over. The gizmo-priority arithmetic it feeds is covered by 8a | not verified |
| 8c | `Player.log` | no new exception, no IMGUI group error | **0** occurrences of `Getting control … in a group with only … controls`; no ContentTool exception. The only entries are third-party and pre-date the bench: TFTV's own `TFTVRevenant` NRE / InvalidOperationException in a tactical level, and 13 `ArgumentException: Mesh can not have more than 65000 vertices` from `UnityEngine.UI.Text.UpdateGeometry` (TFTV's error popup outgrowing the UGUI vertex cap), first seen at boot | PASS |

**One number the plan predicted and the code does not produce.** Step 6's prose expects the zoom to
fall by "roughly `0.88^3`". `BenchList.Wheel` is LINEAR in notches, not compounded: three notches at
`ZoomFactor = 0.12` gave `1.35 x (1 - 0.36) = 0.864`, exactly. The gesture is correct and the pivot
anchoring is what the step actually asserts; only the predicted figure was wrong. Nothing was changed.

### OWNER-VISUAL - the six no agent can sign off

PPCLI cannot deliver mouse gestures to IMGUI, so these stay outstanding until the owner drives them
by hand on `D:\PP-Instance2` (ContentTool `1.1.3.0`, `build=c0869416`):

1. MMB-drag orbits, and it eases rather than snapping.
2. Shift+MMB pans in the screen plane.
3. Alt+LMB orbits identically to MMB.
4. The wheel zooms toward the cursor - the thing under the pointer stays under the pointer.
5. LMB still grabs a gizmo arrow on the FIT tab and does NOT orbit anywhere else.
6. `F` frames and `Home` resets, and neither fires while a text filter has the keyboard.

Screenshots: `C:\Temp\claude\E--DEV-PhoenixPoint-ContentTool\e31d205c-b842-452c-8655-3d543056001d\scratchpad\shots\`
(`view-01-hintline.png` … `view-09-closed.png`).

The environment note about Renderforge restarting the game into the D3D12 debug layer - which ended
three sessions of this run - is recorded once, in `2026-09-02-prototype-catalog-plan.md`.
