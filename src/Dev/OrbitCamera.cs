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
