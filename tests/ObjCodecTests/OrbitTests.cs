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
