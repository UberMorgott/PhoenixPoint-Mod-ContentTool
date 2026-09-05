using System;
using UnityEngine;
using Morgott.ContentTool.Tactical;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// ============ THE ARROWS ON THE GUN ============
    ///
    /// Editor-style translation handles drawn on the selected weapon and dragged with the mouse. The
    /// axis buttons in <see cref="FitBench"/>'s dial block are untouched and remain the exact, keyboard
    /// -only way to work; this is the coarse one - "put it roughly THERE" in one gesture instead of
    /// forty presses - and it is the gesture an author reaches for first.
    ///
    /// MOVE AND TURN. Three arrows along the parent's axes, and three ROTATION RINGS about them
    /// ("ещё бы сферу сделать а не тока стрелки чтобы по осям можно было вертеть"). The rings are an
    /// EXTENSION of the arrows and not a system beside them: same geometry, same constant screen size,
    /// same projected-polyline hit test, same <c>hotControl</c> latch, same Escape/right-click cancel,
    /// same single commit path through <see cref="WeaponBuild.Set"/>. Handle numbering is 0..2 for the
    /// arrows and 3..5 for the rings, and that is the whole of the difference in the plumbing.
    ///
    /// Scale handles are still deferred: the scale +/- buttons work, a scale box is a third pick space,
    /// and nobody has asked. ponytail: scale boxes when a session actually wants one.
    ///
    /// ============ FOUR THINGS IT DOES NOT DO, EACH FOR A REASON ============
    ///
    /// NO COLLIDERS, NO RAYCAST. The handles are ephemeral geometry with no GameObject at all, so there
    /// is nothing for Physics to hit - and adding colliders to a soldier's hand in a live campaign is
    /// exactly the kind of thing that outlives the panel that made it. The hit test is done against the
    /// SAME projected segments that are drawn, in screen pixels, which also means what is clickable is
    /// by construction what is visible.
    ///
    /// NO WRITE TO THE LIVE TRANSFORM. Every drag frame commits through
    /// <see cref="WeaponBuild.Set"/>, which re-solves the auto fit, derives the manifest offset, moves
    /// the four EXT_ sockets and copies the result onto every other live instance of the same mesh.
    /// Writing the transform directly would look identical on screen for one frame and then desync the
    /// muzzle, the other instances and - worst - the numbers SAVE writes.
    ///
    /// NO GUESS WHEN THE MATHS RUNS OUT. An axis pointing nearly at the camera has no usable drag
    /// plane; it is dimmed, unpickable, and a drag that reaches that state stops rather than inventing
    /// a fallback. A gizmo that jumps ten metres from a one-pixel drag is worse than one that refuses.
    ///
    /// NO SHADER IT DID NOT CHECK FOR. <c>Hidden/Internal-Colored</c> is a name-only lookup and
    /// name-only shaders can be stripped from a shipped player build. It is probed ONCE; if it is not
    /// there the gizmo disables itself and says so, and the panel's buttons carry on. Drawing with a
    /// null shader is the pink-magenta failure, which on top of a soldier's hand is worse than nothing.
    /// </summary>
    internal static class FitGizmo
    {
        /// <summary>The last thing a drag had to say - a refusal, or WeaponBuild's own answer line.
        /// <see cref="FitBench"/> drains it into its message box; it is not shown from here, because
        /// this file never draws IMGUI text.</summary>
        internal static string Last;

        // ---- what the gizmo is currently attached to, refreshed every frame by FitBench ----
        private static Camera cam;
        private static Transform mesh;      // the live mesh child - the thing the fit's numbers move
        private static string key;          // the WeaponBuild fit key, or null when nothing is tunable

        /// <summary>Is a drag in progress? The orbit must consult this, not the pointer position: a
        /// drag that started on a handle keeps the mouse even when the pointer wanders off it.</summary>
        internal static bool Owns { get { return active >= 0; } }

        /// <summary>Whether the handles are being drawn at all - the panel says so in words, because an
        /// author who cannot see them needs to know whether they are missing or merely off screen.</summary>
        internal static bool Live { get { return Ready() && Colored() != null; } }

        internal static void Aim(Camera camera, Transform liveMesh, string fitKey)
        {
            cam = camera; mesh = liveMesh; key = fitKey;
            if (!Ready()) Cancel();
        }

        private static bool Ready()
        {
            return cam != null && mesh != null && mesh.parent != null && !string.IsNullOrEmpty(key);
        }

        // ---------------------------------------------------------------- the material, probed once

        private static Material mat;
        private static bool probed;

        /// <summary>The one unlit vertex-colour material, or null FOREVER if the shader is not in this
        /// build. Depth is off in both directions so the handles are visible through the gun they are
        /// attached to - a handle hidden inside a barrel is a handle that cannot be grabbed.
        ///
        /// SHARED, not copied: the Doctor's skeleton overlay draws with this same material. A build that
        /// stripped the shader must disable both drawings through ONE message, and probing a name-only
        /// shader a second time is a second way for them to disagree about whether they can draw.</summary>
        internal static Material Colored()
        {
            if (probed) return mat;
            probed = true;
            Shader shader = null;
            try { shader = Shader.Find("Hidden/Internal-Colored"); } catch (Exception) { }
            if (shader == null)
            {
                Last = "ct_bench: the drag handles are OFF - this build of the game has no " +
                       "'Hidden/Internal-Colored' shader (name-only shaders can be stripped from a " +
                       "player build). Everything else works; use the move/turn/scale buttons.";
                return null;
            }
            try
            {
                mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                mat.SetInt("_ZWrite", 0);
                mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            }
            catch (Exception ex)
            {
                mat = null;
                Last = "ct_bench: the drag handles are OFF - " + ex.GetType().Name + ": " + ex.Message;
            }
            return mat;
        }

        // ---------------------------------------------------------------- where the handles are

        /// <summary>
        /// ============ THE AXES ARE THE PARENT'S, NOT THE WORLD'S AND NOT THE MESH'S ============
        ///
        /// This is the single most load-bearing decision in the file. The fit's <c>offset</c> - the
        /// number the manifest carries and SAVE writes - is the mesh child's position IN ITS PARENT'S
        /// FRAME (WeaponBuild's node is a child of the prefab root, and Follow copies exactly that
        /// localPosition onto every live instance). So the handles have to point along the PARENT's x,
        /// y and z, or dragging the "x" arrow would change two manifest numbers at once and the readout
        /// would not match the gesture.
        ///
        /// The pivot, though, is the MESH's own world position - that is where the gun actually is.
        /// </summary>
        private static bool Geometry(out Vector3 pivot, out Vector3[] axes, out float size)
        {
            pivot = Vector3.zero; axes = null; size = 0f;
            if (!Ready()) return false;
            Transform parent = mesh.parent;
            Matrix4x4 m = parent.localToWorldMatrix;
            Vector3 x = new Vector3(m.m00, m.m10, m.m20);
            Vector3 y = new Vector3(m.m01, m.m11, m.m21);
            Vector3 z = new Vector3(m.m02, m.m12, m.m22);
            if (x.sqrMagnitude < 1e-12f || y.sqrMagnitude < 1e-12f || z.sqrMagnitude < 1e-12f) return false;
            axes = new[] { x.normalized, y.normalized, z.normalized };
            pivot = mesh.position;
            // CONSTANT ON SCREEN. The camera pulls back a long way to frame a vehicle and comes right
            // in on a pistol; a handle sized in metres would be a speck in one case and fill the screen
            // in the other. z is the DEPTH along the view axis, not the distance - the same number the
            // projection divides by.
            float depth = Vector3.Dot(pivot - cam.transform.position, cam.transform.forward);
            // Screen.height, not cam.pixelHeight: the arrows are hit-tested and clipped in the pixels
            // WorldToScreenPoint reports, which are the BACKBUFFER's. An upscaler (Renderforge/DLSS)
            // makes the camera's own target smaller than the window, and sizing by it would shrink the
            // handle by the upscale ratio while the picture on screen stayed the same size.
            size = BenchList.WorldSize(BenchList.GizmoPixels, depth, cam.fieldOfView, Screen.height);
            return size > 1e-6f;
        }

        /// <summary>The three arrows as the screen segments they are DRAWN as, plus which of them are
        /// worth drawing at all. One routine, so the hit test can never disagree with the picture -
        /// that disagreement is the classic gizmo bug and it is invisible until someone clicks.</summary>
        private static bool Project(out float pivotX, out float pivotY, out float[] tipX, out float[] tipY,
                                    out bool[] valid, out Vector3 pivot, out Vector3[] axes, out float size)
        {
            pivotX = pivotY = 0f; tipX = tipY = null; valid = null;
            if (!Geometry(out pivot, out axes, out size)) return false;
            Vector3 p = cam.WorldToScreenPoint(pivot);
            pivotX = p.x; pivotY = p.y;
            tipX = new float[3]; tipY = new float[3]; valid = new bool[3];
            for (int i = 0; i < 3; i++)
            {
                Vector3 t = cam.WorldToScreenPoint(pivot + axes[i] * size);
                tipX[i] = t.x; tipY[i] = t.y;
                valid[i] = BenchList.AxisVisible(p.z, t.z, cam.nearClipPlane, p.x, p.y, t.x, t.y,
                                                 BenchList.MinAxisPixels);
            }
            return true;
        }

        /// <summary>
        /// The three rings as the closed screen polylines they are DRAWN as - the same one routine for
        /// the picture and for the hit test, exactly as <see cref="Project"/> is for the arrows.
        ///
        /// Ring <c>i</c> lies in the plane spanned by the OTHER TWO parent axes, so turning it is
        /// turning about parent axis <c>i</c> - the same frame the manifest's euler is written in.
        ///
        /// A ring is marked unusable, and then dimmed and unpickable, when:
        ///   - the parent's frame is not a similarity (<see cref="BenchList.RingsUsable"/>) - then ALL
        ///     three go, because the refusal is about the frame and not about one axis;
        ///   - it is edge-on to the camera (|view . axis| below <see cref="BenchList.MinRingDot"/>),
        ///     where it draws as a line and drags to infinity;
        ///   - the camera is inside it, or the pivot is behind the near clip plane, so part of the ring
        ///     is behind the viewer and its projection is nonsense.
        /// </summary>
        private static bool ProjectRings(out float[][] ringX, out float[][] ringY, out bool[] valid,
                                         out Vector3 pivot, out Vector3[] axes, out float radius,
                                         out string why)
        {
            ringX = null; ringY = null; valid = null; radius = 0f; why = null;
            float size;
            if (!Geometry(out pivot, out axes, out size)) return false;
            radius = size * BenchList.RingFraction;

            Matrix4x4 m = mesh.parent.localToWorldMatrix;
            bool frameOk = BenchList.RingsUsable(
                new[] { m.m00, m.m10, m.m20, m.m01, m.m11, m.m21, m.m02, m.m12, m.m22 },
                BenchList.ScaleTolerance, out why);

            Vector3 eye = cam.transform.position;
            float depth = Vector3.Dot(pivot - eye, cam.transform.forward);
            bool reachable = depth > cam.nearClipPlane + radius;

            ringX = new float[3][]; ringY = new float[3][]; valid = new bool[3];
            for (int i = 0; i < 3; i++)
            {
                Vector3 u = axes[(i + 1) % 3], v = axes[(i + 2) % 3];
                float[] xs = new float[BenchList.RingSegments], ys = new float[BenchList.RingSegments];
                bool ahead = true;
                for (int s = 0; s < BenchList.RingSegments; s++)
                {
                    float a = s * 2f * Mathf.PI / BenchList.RingSegments;
                    Vector3 p = cam.WorldToScreenPoint(
                        pivot + u * (Mathf.Cos(a) * radius) + v * (Mathf.Sin(a) * radius));
                    xs[s] = p.x; ys[s] = p.y;
                    if (p.z <= cam.nearClipPlane) ahead = false;
                }
                ringX[i] = xs; ringY[i] = ys;
                Vector3 toPivot = (pivot - eye).normalized;
                valid[i] = frameOk && reachable && ahead &&
                           Mathf.Abs(Vector3.Dot(toPivot, axes[i])) >= BenchList.MinRingDot;
            }
            return true;
        }

        /// <summary>Which HANDLE is under this point, in UNITY screen coordinates (origin bottom-left,
        /// the convention <c>Input.mousePosition</c> and <c>WorldToScreenPoint</c> both use): 0..2 for
        /// the arrows, 3..5 for the rings, or -1.
        ///
        /// The ARROWS ARE ASKED FIRST and win a tie. They are the older gesture, they are what an author
        /// reaches for, and each of them crosses each ring exactly once - so at those four pixels
        /// somebody has to be given priority and it may as well be the one that was there first.</summary>
        internal static int Pick(float x, float y)
        {
            try
            {
                float px, py; float[] tipX, tipY; bool[] valid;
                Vector3 pivot; Vector3[] axes; float size;
                if (Project(out px, out py, out tipX, out tipY, out valid, out pivot, out axes, out size))
                {
                    int arrow = BenchList.NearestAxis(px, py, tipX, tipY, valid, x, y, BenchList.PickRadius);
                    if (arrow >= 0) return arrow;
                }

                float[][] rx, ry; bool[] rvalid; float radius; string why;
                if (!ProjectRings(out rx, out ry, out rvalid, out pivot, out axes, out radius, out why))
                    return -1;
                int ring = BenchList.NearestRing(rx, ry, rvalid, x, y, BenchList.RingPickRadius);
                return ring < 0 ? -1 : 3 + ring;
            }
            catch (Exception) { return -1; }
        }

        /// <summary>The question <see cref="FitBench"/>'s Update has to ask BEFORE it starts an orbit.
        /// Unity runs Update before OnGUI, so on the frame of a press the gizmo has not claimed the
        /// mouse yet and <see cref="Owns"/> is still false - asking the pick directly is the only thing
        /// that is true at that moment.</summary>
        internal static bool WouldGrab(float x, float y)
        {
            return Live && Pick(x, y) >= 0;
        }

        // ---------------------------------------------------------------- drawing

        private static readonly Color[] AxisColour =
            { new Color(0.92f, 0.25f, 0.25f), new Color(0.35f, 0.9f, 0.35f), new Color(0.35f, 0.55f, 1f) };
        private static readonly Color Hot = new Color(1f, 0.92f, 0.3f);
        private static readonly Color Dim = new Color(0.45f, 0.45f, 0.45f, 0.5f);

        /// <summary>
        /// Called from <c>OnRenderObject</c>, which runs ONCE PER CAMERA. Without the
        /// <c>Camera.current</c> test the handles would be drawn again by every reflection probe,
        /// UI camera and render-texture capture in the scene - each with its own projection, so what
        /// the player sees is several overlapping ghost gizmos and only one of them clickable.
        /// </summary>
        internal static void Render()
        {
            if (!Ready() || Camera.current != cam) return;
            Material m = Colored();
            if (m == null) return;
            try
            {
                float px, py; float[] tipX, tipY; bool[] valid;
                Vector3 pivot; Vector3[] axes; float size;
                if (!Project(out px, out py, out tipX, out tipY, out valid, out pivot, out axes, out size))
                    return;

                m.SetPass(0);
                GL.PushMatrix();
                // No matrix of our own: inside OnRenderObject the model-view is already the current
                // camera's, so plain world coordinates are what the vertices want.
                GL.Begin(GL.LINES);
                for (int i = 0; i < 3; i++)
                {
                    GL.Color(Shade(i, valid[i]));
                    GL.Vertex(pivot);
                    GL.Vertex(pivot + axes[i] * (size * ShaftFraction));
                }
                GL.End();

                GL.Begin(GL.TRIANGLES);
                for (int i = 0; i < 3; i++) Head(pivot, axes[i], size, Shade(i, valid[i]));
                GL.End();

                // THE RINGS, as the same sampled polyline the pick walks - in WORLD space here, in
                // screen space there, but the same RingSegments points either way, so what is grabbable
                // is what is drawn.
                float[][] rx, ry; bool[] rvalid; float radius; string why;
                Vector3 rp; Vector3[] ra;
                if (ProjectRings(out rx, out ry, out rvalid, out rp, out ra, out radius, out why))
                {
                    GL.Begin(GL.LINES);
                    for (int i = 0; i < 3; i++)
                    {
                        GL.Color(Shade(3 + i, rvalid[i]));
                        Vector3 u = ra[(i + 1) % 3], v = ra[(i + 2) % 3];
                        for (int s = 0; s < BenchList.RingSegments; s++)
                        {
                            float a0 = s * 2f * Mathf.PI / BenchList.RingSegments;
                            float a1 = (s + 1) * 2f * Mathf.PI / BenchList.RingSegments;
                            GL.Vertex(rp + u * (Mathf.Cos(a0) * radius) + v * (Mathf.Sin(a0) * radius));
                            GL.Vertex(rp + u * (Mathf.Cos(a1) * radius) + v * (Mathf.Sin(a1) * radius));
                        }
                    }
                    GL.End();
                }
                GL.PopMatrix();
            }
            catch (Exception) { /* a render callback that throws throws every frame; one gizmo is not worth that */ }
        }

        private const float ShaftFraction = 0.82f;
        private const float HeadRadius = 0.055f;
        private const int HeadSegments = 6;

        /// <param name="handle">0..2 an arrow, 3..5 the ring about that same axis - one numbering, so
        /// the highlight cannot say "X arrow" while the drag is turning the X ring.</param>
        private static Color Shade(int handle, bool usable)
        {
            if (!usable) return Dim;
            if (handle == active || (active < 0 && handle == hover)) return Hot;
            return AxisColour[handle % 3];
        }

        /// <summary>A little cone at the tip, so an arrow reads as an arrow rather than as three lines
        /// crossing. Six segments: enough to look round at 90 px, cheap enough not to think about.</summary>
        private static void Head(Vector3 pivot, Vector3 axis, float size, Color colour)
        {
            Vector3 baseCentre = pivot + axis * (size * ShaftFraction);
            Vector3 apex = pivot + axis * size;
            Vector3 u = Vector3.Cross(axis, cam.transform.forward);
            if (u.sqrMagnitude < 1e-8f) u = Vector3.Cross(axis, Vector3.up);
            if (u.sqrMagnitude < 1e-8f) u = Vector3.Cross(axis, Vector3.right);
            u = u.normalized * (size * HeadRadius);
            Vector3 v = Vector3.Cross(axis, u).normalized * (size * HeadRadius);

            GL.Color(colour);
            for (int s = 0; s < HeadSegments; s++)
            {
                float a0 = s * 2f * Mathf.PI / HeadSegments, a1 = (s + 1) * 2f * Mathf.PI / HeadSegments;
                Vector3 p0 = baseCentre + u * Mathf.Cos(a0) + v * Mathf.Sin(a0);
                Vector3 p1 = baseCentre + u * Mathf.Cos(a1) + v * Mathf.Sin(a1);
                GL.Vertex(apex); GL.Vertex(p0); GL.Vertex(p1);
                GL.Vertex(baseCentre); GL.Vertex(p1); GL.Vertex(p0);   // the cap, so it is solid edge-on
            }
        }

        // ---------------------------------------------------------------- the mouse

        private static readonly int Hint = "Morgott.ContentTool.FitGizmo".GetHashCode();
        private static int active = -1, hover = -1, owner;
        private static string dragKey;
        private static Vector3 startPos, startEuler;
        private static float startScale;
        private static float[] pressOrigin, pressDir, pressPivot, pressAxis, pressView, pressBasis;
        /// <summary>The dragged ring's world radius, frozen at the press. It is what the "landed on the
        /// pivot" refusal is measured against, and it must not follow the gizmo's live size.</summary>
        private static float ringRadius;

        /// <summary>
        /// ============ WHO OWNS THE MOUSE, AND WHEN ============
        ///
        /// Three things want the left button - the panel's own controls, these handles, and the orbit -
        /// and the arbitration has to be decided in ONE place or two of them act on the same press. The
        /// order is: the panel first (it is on top and it is what the pointer is literally over), then
        /// the handles, then the orbit.
        ///
        /// The claim is IMGUI's own <c>hotControl</c> rather than a bool of ours, because that is the
        /// mechanism the panel's buttons and scrollbars are already using: taking hotControl is what
        /// makes a scroll view let go. And the claim is LATCHED for the whole gesture - a drag that
        /// began on the Y arrow keeps the mouse when the pointer crosses onto the panel, which is
        /// exactly what a hand does when it drags something a long way.
        ///
        /// Must be called on EVERY OnGUI pass, before anything else draws: a control id is allocated
        /// from a per-pass counter, so an id fetched conditionally is a different id from frame to
        /// frame and hotControl would never match it again.
        /// </summary>
        /// <param name="stripTopGui">The top edge of the transport strip in IMGUI's coordinates
        /// (y from the TOP), or <c>float.MaxValue</c> when no strip is drawn. The strip is the FOURTH
        /// region in the same arbitration: panel, then transport, then the handles, then the orbit -
        /// it is drawn on top of the scene exactly as the panel is, so a press inside it belongs to
        /// its own controls and to nothing else.</param>
        internal static void Gui(float panelWidth, float stripTopGui)
        {
            Event e = Event.current;
            int id = GUIUtility.GetControlID(Hint, FocusType.Passive);
            if (e == null) return;
            if (!Live) { Cancel(); return; }

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 1 && active >= 0) { Cancel(); e.Use(); return; }
                    if (e.button != 0 || active >= 0) return;
                    if (e.mousePosition.x <= panelWidth) return;      // the panel wins, always
                    if (e.mousePosition.y >= stripTopGui) return;     // ... and so does the transport
                    int handle = Pick(e.mousePosition.x, Flip(e.mousePosition.y));
                    if (handle < 0) return;                           // nothing here: the orbit may have it
                    if (handle < 3 ? !Grab(handle, e.mousePosition)
                                   : !GrabRing(handle - 3, e.mousePosition)) return;
                    owner = id;
                    GUIUtility.hotControl = id;
                    e.Use();
                    return;

                case EventType.MouseDrag:
                    if (active < 0 || owner != id || GUIUtility.hotControl != id) return;
                    if (active < 3) Apply(e.mousePosition); else ApplyRing(e.mousePosition);
                    e.Use();
                    return;

                case EventType.MouseUp:
                    if (owner != id || GUIUtility.hotControl != id) return;
                    active = -1; owner = 0;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    return;

                case EventType.KeyDown:
                    if (e.keyCode == KeyCode.Escape && active >= 0) { Cancel(); e.Use(); }
                    return;

                case EventType.Repaint:
                    // Only for the highlight; the picking that matters happens on the press.
                    hover = active >= 0 ? active : Pick(e.mousePosition.x, Flip(e.mousePosition.y));
                    return;
            }
        }

        /// <summary>IMGUI measures y from the TOP of the window, the camera and Input from the BOTTOM.
        /// Every pick in this file is done in the camera's convention, so this is where the one
        /// conversion lives - a second copy of it somewhere would be a gizmo that is clickable in a
        /// mirror image of where it is drawn.</summary>
        private static float Flip(float guiY)
        {
            return Screen.height - guiY;
        }

        /// <summary>Everything the drag will be measured against, frozen at the press. Frozen because
        /// the gun MOVES as it is dragged: measuring against its live position would feed the answer
        /// back into its own input and the handle would run away from the pointer.</summary>
        private static bool Grab(int axis, Vector2 guiPoint)
        {
            Vector3 pivot; Vector3[] axes; float size;
            if (!Geometry(out pivot, out axes, out size)) return false;

            Vector3 pos, euler, offset; float scale;
            if (!WeaponBuild.State(key, out pos, out euler, out scale, out offset))
            {
                Last = "ct_bench: '" + key + "' has no live fit to drag.";
                return false;
            }
            startPos = pos; startEuler = euler; startScale = scale; dragKey = key;

            Ray r = cam.ScreenPointToRay(new Vector3(guiPoint.x, Flip(guiPoint.y), 0f));
            pressOrigin = F(r.origin); pressDir = F(r.direction);
            pressPivot = F(pivot); pressAxis = F(axes[axis]);
            pressView = F(cam.transform.forward);
            Matrix4x4 m = mesh.parent.localToWorldMatrix;
            // COLUMNS, and unnormalised - see BenchList.LocalFromWorld. The parent's scale is in these
            // lengths and it has to stay there.
            pressBasis = new[] { m.m00, m.m10, m.m20, m.m01, m.m11, m.m21, m.m02, m.m12, m.m22 };

            // A last refusal before the gesture starts rather than half way through it: an axis whose
            // drag plane is degenerate NOW will be degenerate for the whole drag.
            float probe;
            if (!BenchList.PlaneDelta(pressPivot, pressAxis, pressView, pressOrigin, pressDir,
                                      pressOrigin, pressDir, BenchList.MinPlaneDenom, out probe))
            {
                Last = "ct_bench: the " + "XYZ"[axis] + " handle is pointing almost straight at the " +
                       "camera - there is no accurate way to drag it from here. Orbit a little, or use " +
                       "the " + "XYZ"[axis] + "+/- buttons.";
                return false;
            }
            active = axis;
            return true;
        }

        private static void Apply(Vector2 guiPoint)
        {
            if (dragKey != key) { Cancel(); return; }
            Ray r = cam.ScreenPointToRay(new Vector3(guiPoint.x, Flip(guiPoint.y), 0f));
            float along;
            if (!BenchList.PlaneDelta(pressPivot, pressAxis, pressView, pressOrigin, pressDir,
                                      F(r.origin), F(r.direction), BenchList.MinPlaneDenom, out along))
            {
                Last = "ct_bench: this drag has run out of accuracy (the pointer is sliding along the " +
                       "drag plane). Let go, orbit a little, and take it again.";
                return;
            }
            float[] world = { pressAxis[0] * along, pressAxis[1] * along, pressAxis[2] * along };
            float[] local;
            if (!BenchList.LocalFromWorld(pressBasis, world, out local))
            {
                Last = "ct_bench: the hand this weapon hangs on has a degenerate scale, so a screen " +
                       "distance cannot be turned into a fit offset. Use the axis buttons.";
                return;
            }
            // THE ONE COMMIT PATH. Set re-solves the auto fit, derives the manifest offset, moves the
            // four EXT_ sockets and copies the result onto every other live instance - none of which a
            // direct write to mesh.localPosition would do.
            Last = WeaponBuild.Set(dragKey, startPos + new Vector3(local[0], local[1], local[2]),
                                   startEuler, startScale);
        }

        /// <summary>
        /// The ring's press, the arrows' <see cref="Grab"/> told with a different measure. It freezes
        /// the SAME three numbers - position, euler, scale, as they stand at the press - because the
        /// commit composes onto them every frame rather than adding to the live ones.
        ///
        /// The frame check comes FIRST and applies to all three rings at once: a parent that is not a
        /// similarity has no honest ring at all (<see cref="BenchList.RingsUsable"/>), and finding that
        /// out half way through a gesture is finding it out too late.
        /// </summary>
        private static bool GrabRing(int ring, Vector2 guiPoint)
        {
            Vector3 pivot; Vector3[] axes; float size;
            if (!Geometry(out pivot, out axes, out size)) return false;

            Matrix4x4 m = mesh.parent.localToWorldMatrix;
            string why;
            if (!BenchList.RingsUsable(
                    new[] { m.m00, m.m10, m.m20, m.m01, m.m11, m.m21, m.m02, m.m12, m.m22 },
                    BenchList.ScaleTolerance, out why))
            {
                Last = "ct_bench: the rotation rings are OFF here - " + why + ". Use the turn X/Y/Z " +
                       "buttons: they write the local frame directly and are always exact.";
                return false;
            }

            Vector3 pos, euler, offset; float scale;
            if (!WeaponBuild.State(key, out pos, out euler, out scale, out offset))
            {
                Last = "ct_bench: '" + key + "' has no live fit to turn.";
                return false;
            }
            startPos = pos; startEuler = euler; startScale = scale; dragKey = key;

            Ray r = cam.ScreenPointToRay(new Vector3(guiPoint.x, Flip(guiPoint.y), 0f));
            pressOrigin = F(r.origin); pressDir = F(r.direction);
            pressPivot = F(pivot); pressAxis = F(axes[ring]);
            ringRadius = size * BenchList.RingFraction;

            // The same "refuse now rather than half way through" probe the arrows do: a ring that is
            // edge-on at the press is edge-on for the whole gesture.
            float probe;
            if (!BenchList.RingAngle(pressPivot, pressAxis, pressOrigin, pressDir, pressOrigin, pressDir,
                                     BenchList.MinRingDot, ringRadius * BenchList.MinRingRadius, out probe))
            {
                Last = "ct_bench: the " + "XYZ"[ring] + " ring is edge-on from here - there is no " +
                       "accurate angle to read off a drag. Orbit a little, or use the turn " +
                       "XYZ"[ring] + "+/- buttons.";
                return false;
            }
            active = 3 + ring;
            return true;
        }

        /// <summary>
        /// ============ ACCUMULATE FROM THE PRESS, NEVER FROM THE LAST FRAME ============
        ///
        /// One angle, measured from the PRESS ray to the current one, composed ONCE onto the press-time
        /// orientation: <c>q1 = AngleAxis(angle, e_i) * q0</c>. Pre-multiplication is what makes the
        /// turn happen about the PARENT's axis, which is the frame the manifest's euler lives in - and
        /// the world ring the mouse is dragging is that same axis, which is why the sign comes out right
        /// (and why <see cref="BenchList.RingsUsable"/> refuses a mirrored parent, where it would not).
        ///
        /// Adding a per-frame delta to <c>euler</c> instead would look identical for most of a drag and
        /// then come apart near a gimbal singularity: <c>eulerAngles</c> is free to hand back an
        /// EQUIVALENT representation, which round-trips as a rotation and does not as a sum.
        ///
        /// The position passed in is the press-time one, unchanged, so <see cref="WeaponBuild.Set"/>
        /// re-derives the offset about a pivot that has not moved - the gun turns in place.
        /// </summary>
        private static void ApplyRing(Vector2 guiPoint)
        {
            if (dragKey != key) { Cancel(); return; }
            int ring = active - 3;
            Ray r = cam.ScreenPointToRay(new Vector3(guiPoint.x, Flip(guiPoint.y), 0f));
            float degrees;
            if (!BenchList.RingAngle(pressPivot, pressAxis, pressOrigin, pressDir,
                                     F(r.origin), F(r.direction),
                                     BenchList.MinRingDot, ringRadius * BenchList.MinRingRadius,
                                     out degrees))
            {
                Last = "ct_bench: this turn has run out of accuracy (the pointer is sliding along the " +
                       "ring's own plane). Let go, orbit a little, and take it again.";
                return;
            }
            Vector3 local = ring == 0 ? Vector3.right : ring == 1 ? Vector3.up : Vector3.forward;
            Quaternion turned = Quaternion.AngleAxis(degrees, local) * Quaternion.Euler(startEuler);
            Last = WeaponBuild.Set(dragKey, startPos, turned.eulerAngles, startScale);
        }

        /// <summary>Escape, right-click, or anything that pulls the gizmo's footing out from under it:
        /// the gun goes back to the numbers it had when the drag began. Nothing has touched disk, so
        /// this really is the whole of the undo.</summary>
        private static void Cancel()
        {
            if (active >= 0 && dragKey != null)
                try { Last = WeaponBuild.Set(dragKey, startPos, startEuler, startScale); }
                catch (Exception) { }
            if (owner != 0 && GUIUtility.hotControl == owner) GUIUtility.hotControl = 0;
            active = -1; hover = -1; owner = 0; dragKey = null;
        }

        private static float[] F(Vector3 v) { return new[] { v.x, v.y, v.z }; }
    }
}
