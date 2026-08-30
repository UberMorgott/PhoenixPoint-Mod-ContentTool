using System;
using System.Collections.Generic;
using System.IO;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// ============ THE WEAPON FIT WORKBENCH'S LISTS AND STEPS, WITHOUT A GAME ============
    ///
    /// The half of <see cref="FitBench"/> that decides things rather than draws them: which of the
    /// several hundred shipped weapons a typed filter leaves on screen, which of them a live fit can
    /// actually be dialled on, and how far one press of an axis button moves the gun.
    ///
    /// It is deliberately free of UnityEngine, for the same reason
    /// <see cref="Morgott.ContentTool.Bake.FitBox"/> is: the classification is the part that can be
    /// WRONG SILENTLY. A vanilla weapon offered with a save button saves nowhere - it has no manifest
    /// row - and the only way to notice in game is to press save and read the refusal. Offline it is
    /// an assert.
    /// </summary>
    internal static class BenchList
    {
        /// <summary>
        /// ============ THE KEYS THE GAME ALREADY OWNS ============
        ///
        /// A dev hotkey is not a free choice: Unity's <c>Input.GetKeyDown</c> and the game's own input
        /// map both see the same press, so a mod that picks a key the game binds does BOTH THINGS AT
        /// ONCE - and the user never pressed two keys, so nothing on screen says why.
        ///
        /// This list is the ones that are documented in the game's own code and cost something real:
        ///   F4  - UIGeoDistanceTool.cs:38, the geoscape distance cheat.
        ///   F5  - "QuickSave",  GeoscapeViewState.cs:171-173 -> GeoscapeView.QuickSaveGame.
        ///   F9  - "QuickLoad",  GeoscapeViewState.cs:175-178 -> HomeScreenView.QuickLoadGame:257-267,
        ///         which switches to UIStateHomeQuickLoad and RELOADS THE CAMPAIGN. F9 was this
        ///         workbench's hotkey until 2026-08-29, and every press of it quick-loaded the game
        ///         behind the panel; when the quicksave failed to deserialize the load dropped the
        ///         player at the main menu with his campaign torn down.
        ///   F10 - ReportIssueInput.cs:39, the issue reporter.
        ///
        /// F12 is deliberately NOT here: no game code binds it, it is src\Dev\DevRunner.cs's own live
        /// -reload key, and the guard exists to keep a mod off the GAME's keys, not off its own.
        /// </summary>
        internal static readonly string[] GameOwnedKeys = { "F4", "F5", "F9", "F10" };

        /// <summary>Whether a KeyCode NAME is one the game already answers to. Taking a name rather
        /// than a KeyCode is what lets this be asserted offline, which is the only place it CAN be
        /// asserted: in game the symptom is the game's own action firing, not an error.</summary>
        internal static bool IsGameOwned(string keyName)
        {
            if (string.IsNullOrEmpty(keyName)) return false;
            foreach (string k in GameOwnedKeys)
                if (string.Equals(k, keyName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>WeaponBuild's key format, and the only place this file knows it: "&lt;id&gt; @
        /// &lt;manifest path&gt;". The id is the WeaponDef's own name (WeaponBuild.One sets
        /// <c>def.name = e.id</c>), which is what makes a def-name lookup possible at all.</summary>
        internal const string Separator = " @ ";

        internal static string Id(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            int at = key.IndexOf(Separator, StringComparison.Ordinal);
            return at < 0 ? key : key.Substring(0, at);
        }

        /// <summary>
        /// The fit key for a weapon def name, or null when that weapon is VANILLA - shipped, with no
        /// manifest row anywhere, hence nothing to tune and nothing to save. Null is the whole answer
        /// the UI needs: it greys the axis buttons out and says so instead of offering a save that
        /// cannot work.
        /// </summary>
        internal static string KeyFor(string defName, IEnumerable<string> fittedKeys)
        {
            if (string.IsNullOrEmpty(defName) || fittedKeys == null) return null;
            foreach (string key in fittedKeys)
                if (Id(key) == defName) return key;
            return null;
        }

        /// <summary>The other half of a fit key: the ppcontent.json it came from.</summary>
        internal static string Manifest(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int at = key.IndexOf(Separator, StringComparison.Ordinal);
            return at < 0 ? null : key.Substring(at + Separator.Length);
        }

        // ================================================================ saving back to the REPO

        /// <summary>
        /// ============ A SAVE THAT SURVIVES THE NEXT DEPLOY ============
        ///
        /// SAVE writes the ppcontent.json the game actually loaded, which is the DEPLOYED copy under
        /// <c>...\Phoenix Point\Mods\&lt;mod&gt;\</c>. The author's source of truth is the repo, and
        /// deploy.ps1 copies repo OVER deployed - so an afternoon of dialling a gun in by eye was one
        /// forgotten hand-copy away from being overwritten by the next deploy, silently, with the file
        /// that was destroyed being the newer one.
        ///
        /// Nothing has to be configured for this, because deploy.ps1 ALREADY knows both paths: it now
        /// drops <see cref="SourceMarker"/> beside every mod it installs, holding the absolute folder it
        /// copied FROM. The bench reads it back after a successful save and mirrors the exact bytes it
        /// just wrote. No re-serialisation - the splice in WeaponManifest.Save is the one thing that
        /// formats that file, and a second formatter would be a second answer.
        ///
        /// EVERY refusal is in words rather than silence, because "my numbers vanished" is precisely
        /// the failure this exists to prevent and a quiet mirror is no better than none.
        /// </summary>
        internal const string SourceMarker = ".contenttool-source";

        /// <summary>Where a just-saved manifest ALSO belongs, or null with <paramref name="why"/>
        /// carrying the reason there is nowhere - which is a normal answer, not an error.</summary>
        internal static string MirrorTarget(string manifestPath, out string why)
        {
            why = null;
            if (string.IsNullOrEmpty(manifestPath))
            {
                why = "no manifest path to mirror.";
                return null;
            }
            string dir;
            try { dir = Path.GetDirectoryName(Path.GetFullPath(manifestPath)); }
            catch (Exception ex) { why = "that manifest path is unusable: " + ex.Message; return null; }
            if (string.IsNullOrEmpty(dir)) { why = "that manifest has no folder."; return null; }

            string marker = Path.Combine(dir, SourceMarker);
            if (!File.Exists(marker))
            {
                why = "DEPLOYED COPY ONLY, no source recorded - this mod was not installed by " +
                      "deploy.ps1 (no " + SourceMarker + " beside it), so there is nothing to mirror " +
                      "back to. Copy it into your repo by hand if you want to keep it.";
                return null;
            }
            string source;
            try { source = First(File.ReadAllLines(marker)); }
            catch (Exception ex) { why = "could not read " + marker + ": " + ex.Message; return null; }
            if (string.IsNullOrEmpty(source))
            {
                why = marker + " is empty, so no source folder is recorded.";
                return null;
            }
            string target;
            try { target = Path.GetFullPath(Path.Combine(source, Path.GetFileName(manifestPath))); }
            catch (Exception ex) { why = "the recorded source path is unusable: " + ex.Message; return null; }
            if (!Directory.Exists(source))
            {
                why = "the recorded source folder is GONE: " + source + " - the deployed file IS saved, " +
                      "nothing was lost, but it could not be mirrored back.";
                return null;
            }
            // Deploying a mod ONTO its own source is legal and means the two are the same file.
            if (string.Equals(target, Path.GetFullPath(manifestPath), StringComparison.OrdinalIgnoreCase))
            {
                why = "the deployed manifest IS the source file, so there is nothing to mirror.";
                return null;
            }
            return target;
        }

        /// <summary>The mirror itself, and the line the panel shows for it. Never throws: this runs
        /// AFTER the real save succeeded, and a failure here must not read as a failure of that.</summary>
        internal static string MirrorSave(string manifestPath)
        {
            string why;
            string target = MirrorTarget(manifestPath, out why);
            if (target == null) return why;
            try
            {
                File.Copy(manifestPath, target, true);
                return "AND mirrored back to the source: " + target;
            }
            catch (Exception ex)
            {
                return "but the SOURCE copy could not be written: " + target + " (" +
                       ex.GetType().Name + ": " + ex.Message + "). The deployed file IS saved - copy " +
                       "it across by hand, or the next deploy.ps1 will overwrite it.";
            }
        }

        private static string First(string[] lines)
        {
            if (lines == null) return null;
            foreach (string l in lines)
                if (!string.IsNullOrEmpty(l) && l.Trim().Length > 0) return l.Trim();
            return null;
        }

        /// <summary>
        /// ============ WHOSE WEAPON IS THIS ============
        ///
        /// The identity a def carries FOREVER, unlike a live fit. <c>WeaponBuild.One</c> stamps every
        /// def it creates with <c>ResourcePath = "Morgott/ContentTool/" + id</c> (WeaponBuild.cs:136),
        /// so this answers "did this mod build it" for a weapon that has never been picked up.
        ///
        /// That distinction is the bug it exists to fix. The workbench used to mark a weapon tunable by
        /// looking it up in <c>WeaponBuild.Fitted()</c> - but that dictionary only gains a row when a
        /// weapon's prefab is actually INSTANTIATED IN A HAND, so before the author equips anything it
        /// is empty and every weapon in the list, his own included, reads as vanilla. "Built by this
        /// mod" and "its fit is live this session" are two different questions and the panel now asks
        /// them separately.
        /// </summary>
        internal const string ResourceRoot = "Morgott/ContentTool/";

        internal static bool IsMine(string resourcePath)
        {
            return resourcePath != null &&
                   resourcePath.StartsWith(ResourceRoot, StringComparison.Ordinal);
        }

        /// <summary>Case-insensitive substring, because a def name is
        /// "PX_LaserAssaultRifle_WeaponDef" and nobody types that. An EMPTY query keeps everything -
        /// a filter that hides the list until you type is a filter that looks broken.</summary>
        internal static bool Matches(string name, string query)
        {
            if (string.IsNullOrEmpty(query)) return true;
            return name != null && name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>One axis button press, as the three numbers WeaponBuild.Adjust wants: the delta on
        /// one axis and two zeroes. An axis outside 0..2 moves NOTHING rather than throwing - the
        /// caller is a GUI loop and a wedged screen is worse than a dead button.</summary>
        internal static float[] Delta(int axis, float sign, float step)
        {
            float[] d = new float[3];
            if (axis >= 0 && axis < 3) d[axis] = sign * step;
            return d;
        }

        /// <summary>
        /// The step ladder the +/- buttons walk. Four orders of magnitude, because the same session
        /// starts by moving a gun a whole hand's width and ends by moving it a millimetre, and a
        /// single step size makes one of those two impossible.
        /// </summary>
        internal static readonly float[] Steps = { 0.001f, 0.005f, 0.01f, 0.05f, 0.1f };

        /// <summary>The next step size, wrapping. Anything not on the ladder lands on the first
        /// rung.</summary>
        internal static float NextStep(float step)
        {
            for (int i = 0; i < Steps.Length; i++)
                if (Math.Abs(Steps[i] - step) < 1e-6f) return Steps[(i + 1) % Steps.Length];
            return Steps[0];
        }

        /// <summary>The rotation ladder, in DEGREES - the same idea, different units: a turn is
        /// judged in whole degrees long before it is judged in tenths.</summary>
        internal static readonly float[] Turns = { 0.5f, 1f, 5f, 15f, 45f, 90f };

        internal static float NextTurn(float turn)
        {
            for (int i = 0; i < Turns.Length; i++)
                if (Math.Abs(Turns[i] - turn) < 1e-6f) return Turns[(i + 1) % Turns.Length];
            return Turns[0];
        }

        /// <summary>
        /// ============ WHERE THE CAMERA HAS TO STAND SO THE UNIT IS ACTUALLY ON SCREEN ============
        ///
        /// The workbench's first version leaned on <c>CameraDirectorHint.GeoscapeSoldierEditCenter</c>,
        /// i.e. on framing authored in PREFAB DATA for the game's OWN equip screen - whose panel is
        /// somewhere else and whose viewport is the whole window. The result was a shoulder at the
        /// right edge and an empty screen. So the framing is COMPUTED here instead, from the unit's
        /// measured size and the panel's actual width, which is the only form of it that works for a
        /// soldier, a four-legged mutoid and a vehicle without three hand-tuned numbers each.
        ///
        /// Two answers, both in world units:
        ///   distance - how far back along the view direction, so a sphere of <paramref name="radius"/>
        ///              fits BOTH the full viewport height and the width LEFT OVER beside the panel.
        ///              A bounding SPHERE, not a box, because the unit is dragged around its own axis
        ///              and a box's footprint changes as it turns - the sphere's does not.
        ///   lateral  - how far LEFT of the aim point the camera stands. Moving the camera left is what
        ///              moves the unit right on screen, and it does so without skewing the projection
        ///              the way an off-centre frustum would. It lands the unit dead centre of the FREE
        ///              region: half the panel's width, as a fraction of the screen, right of centre.
        ///
        /// The whole thing is floats and trigonometry with no UnityEngine in sight, which is the point:
        /// "the unit is off screen" is judged by eye in game, one screenshot at a time, but "the
        /// computed pose puts the bounds inside the free region" is an assert that runs offline.
        /// </summary>
        /// <param name="margin">The zoom knob, clamped to <see cref="ZoomMin"/>..<see cref="ZoomMax"/>.
        /// 1.0 = the unit exactly touches the edges of the free region, which always looks too tight;
        /// the workbench's default leaves room, and below 1 the camera moves INSIDE the bounding sphere
        /// so a small weapon can be read.</param>
        internal static void Frame(float radius, float fovDeg, float screenW, float screenH,
                                   float panelW, float margin,
                                   out float distance, out float lateral)
        {
            float ignored;
            Frame(radius, fovDeg, screenW, screenH, panelW, 0f, margin,
                  out distance, out lateral, out ignored);
        }

        /// <summary>
        /// The same framing with the TRANSPORT STRIP paid for. The strip is a bar along the bottom of
        /// the free region (<see cref="StripHeight"/>), so the region the unit has to fit inside is
        /// shorter by exactly that much and its centre sits <c>stripH/2</c> ABOVE the screen's centre -
        /// the vertical mirror of what the panel does horizontally, and derived from the same one
        /// constant rather than a second hand-tuned number.
        ///
        /// <paramref name="vertical"/> is how far DOWN the camera stands from the aim point: moving the
        /// camera down moves the image up, exactly as moving it left moves the unit right.
        /// </summary>
        internal static void Frame(float radius, float fovDeg, float screenW, float screenH,
                                   float panelW, float stripH, float margin,
                                   out float distance, out float lateral, out float vertical)
        {
            // Everything here arrives from a live game - a minimised window, a silly FOV, a unit whose
            // renderers all failed to load - and a GUI loop is the wrong place to discover any of it.
            if (screenW < 1f) screenW = 1f;
            if (screenH < 1f) screenH = 1f;
            if (panelW < 0f) panelW = 0f;
            if (panelW > screenW * 0.9f) panelW = screenW * 0.9f;
            if (fovDeg < 1f) fovDeg = 1f;
            if (fovDeg > 175f) fovDeg = 175f;
            if (radius < 0.01f) radius = 0.01f;
            // THE FLOOR IS ZoomMin, NOT 1. Clamping to 1 here was the real minimum-distance stop: it
            // meant the camera could never stand closer than "the whole unit fits the free region",
            // which on a pistol is a gun four pixels wide. Below 1 the camera is inside the bounding
            // sphere - which is exactly where a small weapon has to be looked at.
            if (margin < ZoomMin) margin = ZoomMin;
            if (stripH < 0f || float.IsNaN(stripH)) stripH = 0f;
            if (stripH > screenH * 0.5f) stripH = screenH * 0.5f;

            double tanV = Math.Tan(fovDeg * 0.5 * Math.PI / 180.0);   // half the viewport, vertically
            double tanH = tanV * (screenW / screenH);                 // ... and horizontally
            double tanFreeW = tanH * ((screenW - panelW) / screenW);  // ... and of the part not covered
            double tanFreeH = tanV * ((screenH - stripH) / screenH);  // ... above the transport strip

            double d = radius / tanFreeH;
            double byWidth = radius / tanFreeW;
            if (byWidth > d) d = byWidth;
            d *= margin;

            distance = (float)d;
            // The free region's centre sits panelW/2 right of the screen's centre; as a fraction of the
            // half-width that is panelW/screenW, and the half-width at this distance is d*tanH.
            lateral = (float)(d * tanH * (panelW / screenW));
            // ... and stripH/2 ABOVE its centre, which is the same sum with the vertical half-angle.
            vertical = (float)(d * tanV * (stripH / screenH));
        }

        // ================================================================ the transport strip

        /// <summary>
        /// ============ THE STRIP UNDER THE MODEL, AND THE HEIGHT IT COSTS ============
        ///
        /// "можно внизу под моделькой" - the animation transport is a bar along the BOTTOM of the free
        /// region, right of the panel: the clip list, play/pause, loop, the scrub slider and the speed.
        ///
        /// This is THE ONE NUMBER for its height, exactly as <see cref="PanelWidth"/> is the one number
        /// for the panel's width, and for the same reason: it is the strip's height, the mouse-input
        /// boundary AND the input to <see cref="Frame"/>. A second copy of it anywhere would be a unit
        /// standing half behind the transport with nothing on screen to say why.
        /// </summary>
        internal const float StripHeight = 96f;
        /// <summary>The margin the strip's content is inset by, on each side.</summary>
        internal const float StripInset = 8f;
        /// <summary>Narrower than this and the strip is not worth the height it eats - the clip names
        /// would be unreadable and the model would lose a fifth of the window for nothing.</summary>
        internal const float StripMinWidth = 260f;

        /// <summary>Is there room for the strip at all? A window too short or a free region too narrow
        /// gets NO strip - and then it costs no height either, which is why the framing asks this same
        /// question through <see cref="StripReserve"/> rather than assuming.</summary>
        internal static bool StripShown(float screenW, float screenH, float panelW)
        {
            if (float.IsNaN(screenW) || float.IsNaN(screenH) || float.IsNaN(panelW)) return false;
            return screenW - panelW >= StripMinWidth && screenH >= StripHeight * 3f;
        }

        /// <summary>The height the framing has to leave clear at the bottom: the strip's, or nothing at
        /// all when there is no strip.</summary>
        internal static float StripReserve(float screenW, float screenH, float panelW)
        {
            return StripShown(screenW, screenH, panelW) ? StripHeight : 0f;
        }

        /// <summary>
        /// The strip's hit rectangle, in the mouse's own convention (y measured from the BOTTOM of the
        /// screen, which is what <c>Input.mousePosition</c> hands over).
        ///
        /// It is the WHOLE BAND right of the panel and below the strip's top edge, not just the drawn
        /// controls: a drag that begins on the scrub slider and wanders two pixels below it must not
        /// suddenly become an orbit. The panel's own column is deliberately NOT in it - the panel wins
        /// there, as it always has - and neither is anything above the band, which stays the gizmo's
        /// and the orbit's.
        /// </summary>
        internal static bool OverStrip(float mouseX, float mouseY, float screenW, float screenH, float panelW)
        {
            if (!StripShown(screenW, screenH, panelW)) return false;
            if (float.IsNaN(mouseX) || float.IsNaN(mouseY)) return false;
            return mouseX > panelW && mouseY >= 0f && mouseY <= StripHeight;
        }

        /// <summary>The same band in IMGUI's convention (y from the TOP): everything at or below this
        /// line belongs to the transport. <c>float.MaxValue</c> when there is no strip, so a caller can
        /// compare against it unconditionally.</summary>
        internal static float StripTop(float screenW, float screenH, float panelW)
        {
            return StripShown(screenW, screenH, panelW) ? screenH - StripHeight : float.MaxValue;
        }

        // ---- what the transport actually does with a clip ----

        /// <summary>The playback speed ladder. Slow motion is the whole point - a grip that reads as
        /// correct at full speed is judged at a tenth of it - so the ladder runs DOWN from 1 as far as
        /// it runs up.</summary>
        internal static readonly float[] Speeds = { 0.05f, 0.1f, 0.25f, 0.5f, 1f, 2f };
        internal const float SpeedMin = 0.05f, SpeedMax = 4f;

        internal static float NextSpeed(float speed)
        {
            for (int i = 0; i < Speeds.Length; i++)
                if (Math.Abs(Speeds[i] - speed) < 1e-6f) return Speeds[(i + 1) % Speeds.Length];
            return Speeds[0];
        }

        /// <summary>The scrub slider's value as a normalized clip position. The slider hands back
        /// whatever the mouse did to it, including a value a pixel past either end, and a normalized
        /// time outside [0,1] is a state Unity's animator does not have.</summary>
        internal static float Normalized(float slider)
        {
            if (float.IsNaN(slider)) return 0f;
            return slider < 0f ? 0f : slider > 1f ? 1f : slider;
        }

        /// <summary>Where a normalized position lands in SECONDS, for the readout.</summary>
        internal static float Seconds(float t, float length)
        {
            if (float.IsNaN(length) || float.IsInfinity(length) || length <= 0f) return 0f;
            return Normalized(t) * length;
        }

        /// <summary>
        /// ============ ONE FRAME OF PLAYBACK, AND THE WRAP THAT IS A RESTART ============
        ///
        /// The transport owns the clip's POSITION and hands it to the animator each frame
        /// (<c>Animator.Play(state, 0, t)</c> with <c>speed = 0</c>), rather than letting the animator
        /// advance itself: pause, scrub, slow motion and loop are then one number and one rule instead
        /// of four interacting ones.
        ///
        /// The rule that can be wrong silently is the WRAP. A looped clip that reaches its end must come
        /// back to the START - <c>next - floor(next)</c> - not sit at 1 forever, which is what a clamp
        /// would do and which in game looks exactly like "the animation froze on the last frame". With
        /// loop off the clamp IS the answer, and <paramref name="ended"/> says so, so the caller can stop
        /// rather than re-assert the same pose for the rest of the session.
        ///
        /// <paramref name="dt"/> is capped: a hitch, a breakpoint or a level load hands over a whole
        /// second of delta, and a clip that teleports 30 frames is not something a fit can be judged on.
        /// </summary>
        internal static float Advance(float t, float dt, float speed, float length, bool loop,
                                      out bool ended)
        {
            ended = false;
            t = Normalized(t);
            if (float.IsNaN(length) || float.IsInfinity(length) || length <= 1e-4f) length = 1f;
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f) dt = 0f;
            if (dt > MaxDelta) dt = MaxDelta;
            speed = Clamp(speed, SpeedMin, SpeedMax);

            float next = t + dt * speed / length;
            if (next < 1f) return next < 0f ? 0f : next;
            if (loop) return next - (float)Math.Floor(next);
            ended = true;
            return 1f;
        }

        /// <summary>The longest frame the transport will believe. Calibration knob: a stall must not
        /// jump the clip, and 1/4 s is already four times a bad frame.</summary>
        internal const float MaxDelta = 0.25f;

        // ================================================================ the panel's own geometry

        /// <summary>
        /// THE ONE NUMBER that says how much of the screen the panel eats. It is the panel's width, the
        /// mouse-input boundary AND the input to <see cref="Frame"/> - the unit is placed in the screen
        /// this leaves over, so a second copy of it anywhere would be a unit half behind the buttons and
        /// nothing on screen to say why. It lives HERE rather than in the Unity half precisely so the
        /// offline gate can measure whether a row of buttons actually fits inside it.
        /// </summary>
        internal const float PanelWidth = 380f;

        /// <summary>The margin <c>GUILayout.BeginArea</c> is inset by on each side.</summary>
        internal const float PanelInset = 8f;

        internal static float ContentWidth(float panelW) { return panelW - 2f * PanelInset; }

        /// <summary>
        /// Does a row of <paramref name="buttons"/> equal-width buttons fit the panel? IMGUI does NOT
        /// clip an over-wide horizontal group - it draws it past the edge of the area and the last
        /// button is simply unreachable, with no error anywhere. That is defect 2 as an assert: the
        /// step row was three 140 px buttons inside a 324 px content width.
        /// </summary>
        internal static bool RowFits(int buttons, float buttonW, float panelW)
        {
            if (buttons <= 0) return true;
            return buttons * buttonW + (buttons - 1) * 2f <= ContentWidth(panelW);
        }

        /// <summary>A long def name shortened from the MIDDLE, because both ends carry meaning
        /// ("Morgott_VultureAR_WeaponDef" - the author's prefix and the def suffix). Never widens the
        /// panel, which is the whole point: the alternative fix is a wider panel and less unit.</summary>
        internal static string Elide(string name, int max)
        {
            if (name == null) return "";
            if (max < 5) max = 5;
            if (name.Length <= max) return name;
            int head = (max - 1) / 2, tail = max - 1 - head;
            return name.Substring(0, head) + "~" + name.Substring(name.Length - tail);
        }

        /// <summary>Roughly how many characters of the default IMGUI font fit one panel line. A
        /// calibration knob: it is measured by eye against a real screenshot, not derived.</summary>
        internal const int NameChars = 44;

        // ---- the vertical budget ----
        // ponytail: rows are counted, not measured. IMGUI can only measure inside a layout pass, and
        // the decision this makes - what is ABOVE the fold - has to be made before drawing. The row
        // height is the calibration knob; raise it if a future skin draws taller.

        /// <summary>One button/label row including IMGUI's own 2 px spacing.</summary>
        internal const float Row = 22f;
        /// <summary>Title, unit line, weapon line, saved/modified line, the view row, the drag-invert
        /// row, the MODEL SCALE row, and the view readout.</summary>
        internal const float ChromeRows = 8f;
        /// <summary>The dial block: two readouts, the step row, move, turn, scale, the save row, its
        /// caption. THE PRIMARY WORKING SURFACE - everything else is a picker used once.</summary>
        internal const float DialRows = 9f;
        internal const float MessageHeight = 70f;
        internal const float ListMin = 60f, ListMax = 150f;

        /// <summary>
        /// ============ THE DIAL BLOCK IS NEVER BELOW THE FOLD ============
        ///
        /// The in-game defect: with a weapon selected, move/turn/scale and SAVE sat underneath two
        /// 130-and-180 px lists and fell off the bottom of a 803 px window, so the one surface the
        /// author works in was the one he could not reach. The fix is ORDER (dial before the lists) and
        /// this is the assert on it: given only the chrome, the dial, the message line and the two list
        /// HEADERS, is everything down to the message on screen without scrolling?
        /// </summary>
        internal static bool DialReachable(float viewportH)
        {
            return ChromeRows * Row + DialRows * Row + MessageHeight + 2f * Row <= viewportH;
        }

        /// <summary>
        /// How tall each def list may be with the dial block already paid for. Whatever is left over
        /// after the fixed block, split between the lists that are open, capped so a long list cannot
        /// eat the window and floored so a shown list is worth showing. NO room left means ZERO, i.e.
        /// "collapse them" - the lists are pickers and the dial is the work.
        /// </summary>
        internal static void Rows(float viewportH, bool dialing, bool unitsOpen, bool weaponsOpen,
                                  out float unitH, out float weaponH)
        {
            unitH = weaponH = 0f;
            if (viewportH < 0f) viewportH = 0f;
            int open = (unitsOpen ? 1 : 0) + (weaponsOpen ? 1 : 0);
            if (open == 0) return;

            float fixedH = ChromeRows * Row + MessageHeight + (dialing ? DialRows * Row : Row) + 2f * Row;
            float spare = viewportH - fixedH;
            if (spare < ListMin) return;                      // no room: collapsed, never overlapping

            float each = spare / open;
            if (each > ListMax) each = ListMax;
            if (unitsOpen) unitH = each;
            if (weaponsOpen) weaponH = each;
        }

        // ================================================================ driving the view by mouse

        /// <summary>
        /// ============ ORBIT, PORTED FROM THE FreeCamera MOD ============
        ///
        /// The gains and the clamp shape are <c>Morgott.FreeCamera.OrbitInputMath</c>'s, which is this
        /// author's own published free-orbit camera: <c>BaseDegreesPerPixel = 0.2</c>,
        /// <c>ClampPitch</c>, <c>WrapHeading</c> and the DISTANCE-PROPORTIONAL zoom step
        /// (<c>DefaultZoomFactor = 0.12</c>) - far away a notch covers ground, close in it steps gently.
        /// They are re-declared rather than referenced because the two mods are separate assemblies and
        /// one const is not worth an assembly reference; the values are the ones already tuned by hand
        /// against a real mouse.
        ///
        /// The one adaptation: this camera orbits the MEASURED BOUNDS CENTRE of whatever is standing on
        /// the platform, not a tactical target, and its "zoom" is the framing MARGIN
        /// (<see cref="Frame"/>'s <c>margin</c>) rather than a distance in metres - so a proportional
        /// notch multiplies it instead of subtracting from it, which gives the same feel on a pistol
        /// and on a vehicle.
        /// </summary>
        internal const float DegreesPerPixel = 0.2f;

        /// <summary>Pitch band. Tighter than FreeCamera's +-89: past this the camera is overhead or
        /// underfoot and a weapon in a hand is edge-on, which is never the thing being judged.</summary>
        internal const float PitchMin = -80f, PitchMax = 80f;

        /// <summary>The framing margin's band. 1.0 has the unit exactly touching the edges of the free
        /// region and 8 is far enough out to see a vehicle whole - but the floor is well BELOW 1 on
        /// purpose: a pistol measured whole fills a hand's worth of screen and is unreadable, and the
        /// only way to read it is to let the camera inside the unit's own bounding sphere. The floor is
        /// non-zero so the pose can never collapse onto the aim point itself, and
        /// <see cref="NearClip"/> is what keeps the geometry from being clipped away down there.</summary>
        internal const float ZoomMin = 0.05f, ZoomMax = 8f;
        internal const float ZoomDefault = 1.35f;
        internal const float ZoomFactor = 0.12f;

        /// <summary>The near plane the bench holds the camera at while it is open, and it exists for
        /// exactly one reason: the geoscape camera ships a near plane authored for a planet, so zooming
        /// a gun to arm's length clipped the gun away instead of showing it. Restored per camera by
        /// <c>FitBench.ReleaseCamera</c> off the same ledger row as the brain and the pose.</summary>
        internal const float NearClip = 0.02f;

        /// <summary>WASD/QE fly speed, in RADII PER SECOND - the same reasoning as the lift knob: one
        /// press has to mean the same thing on a soldier and on a vehicle three times his size.</summary>
        internal const float FlyPerSecond = 1.5f;
        /// <summary>How far the free-camera pan is allowed to walk from the measured centre, in radii.
        /// Not a feel knob - it is the guard against flying to a place with no way back, and RECENTRE
        /// is the way back from anywhere inside it.</summary>
        internal const float PanMaxRadii = 40f;

        /// <summary>The lift knob, in RADII. BOUNDED, unlike the first version: an unbounded lift is
        /// exactly how a press of 'up' walks the aim point off the model with nothing on screen to say
        /// what happened and no way back - the "everything disappeared" of 2026-08-29.</summary>
        internal const float LiftMin = -2f, LiftMax = 2f;
        internal const float LiftStep = 0.12f;

        /// <summary>The PREVIEW model-scale knob's range. It multiplies the pose the game itself chose
        /// for the displayed character and is written NOWHERE - not into a def, not into a save - so
        /// its only job is to let a foreign model be sized by eye against a vanilla soldier. Bounded
        /// for the same reason the lift is: a scale of zero is a model that has vanished with nothing
        /// on screen to say why.</summary>
        internal const float ModelScaleMin = 0.1f, ModelScaleMax = 5f;

        internal static float Clamp(float v, float min, float max)
        {
            if (float.IsNaN(v)) return min;
            return v < min ? min : v > max ? max : v;
        }

        /// <summary>Heading wrapped to [0, 360), as FreeCamera's <c>WrapHeading</c>.</summary>
        internal static float WrapYaw(float yaw)
        {
            if (float.IsNaN(yaw) || float.IsInfinity(yaw)) return 0f;
            float h = yaw % 360f;
            return h < 0f ? h + 360f : h;
        }

        /// <summary>
        /// Drag direction, per axis, SESSION ONLY - two panel toggles, no persistence. Both default to
        /// TRUE, which is the INVERTED sense relative to the first version: the drag now grabs and
        /// turns the model itself rather than sweeping the camera the opposite way ("мышкой вверх вниз
        /// и влево вправо инвертировать надо", 2026-08-29). Which of the two reads as "natural" is a
        /// matter of the hand holding the mouse, so it is a knob rather than another code round-trip.
        /// Only the LEFT-drag orbit reads these; the right-drag model turn and the wheel keep their own
        /// signs and their own feel.
        /// </summary>
        internal static bool InvertX = true, InvertY = true;

        /// <summary>One frame of horizontal drag, in degrees of orbit. With <see cref="InvertX"/> on
        /// (the default) dragging RIGHT carries the near side of the unit right with the hand.</summary>
        internal static float Orbit(float yaw, float dxPixels)
        {
            return WrapYaw(yaw + (InvertX ? dxPixels : -dxPixels) * DegreesPerPixel);
        }

        /// <summary>One frame of vertical drag, CLAMPED. With <see cref="InvertY"/> on (the default)
        /// dragging UP tips the near side up, i.e. the camera looks from below.</summary>
        internal static float Tilt(float pitch, float dyPixels)
        {
            return Clamp(pitch + (InvertY ? -dyPixels : dyPixels) * DegreesPerPixel, PitchMin, PitchMax);
        }

        /// <summary>One wheel notch, proportional and clamped. Positive = scroll up = closer.</summary>
        internal static float Wheel(float zoom, float notches)
        {
            if (float.IsNaN(notches) || float.IsInfinity(notches)) return Clamp(zoom, ZoomMin, ZoomMax);
            float f = 1f - notches * ZoomFactor;
            if (f < 0.25f) f = 0.25f;                 // a trackpad can hand over a huge notch count
            if (f > 4f) f = 4f;
            return Clamp(zoom * f, ZoomMin, ZoomMax);
        }

        /// <summary>Is the pointer over the 3D half of the screen? Mouse input must NEVER act while the
        /// cursor is on the panel, or dragging a scrollbar would also swing the camera.</summary>
        internal static bool OverScene(float mouseX, float panelW)
        {
            return mouseX > panelW;
        }

        // ================================================================ the drag gizmo's algebra

        /// <summary>
        /// ============ THE HANDLES ON THE GUN, AND WHY THE MATHS IS HERE ============
        ///
        /// The gizmo is an editor-style set of translation arrows drawn on the weapon and dragged with
        /// the mouse. Everything about it that can be WRONG SILENTLY is in this file: which arrow a
        /// click lands on, how far a drag moved the gun along one axis, and how that world distance
        /// becomes the parent-local number the manifest is written in. In game a wrong answer looks
        /// like "the gun jumped" and there is nothing to read; offline it is an assert.
        ///
        /// The drawing, the material and the mouse ownership stay in <see cref="FitGizmo"/> - they
        /// need Unity and they cannot be wrong quietly, they are either on screen or they are not.
        /// </summary>
        internal const float GizmoPixels = 90f;
        /// <summary>How near a click has to land on an arrow's DRAWN line. Calibration knob: measured
        /// by hand against a real mouse, not derived.</summary>
        internal const float PickRadius = 10f;
        /// <summary>An arrow shorter than this on screen is one pointing nearly at the camera: it
        /// cannot be aimed at and its drag would be a division by almost nothing. Refused, not
        /// approximated.</summary>
        internal const float MinAxisPixels = 12f;
        /// <summary>The smallest ray-versus-plane denominator a drag is allowed to divide by. Below it
        /// the pointer is sliding along the plane rather than across it and the answer is a huge jump.</summary>
        internal const float MinPlaneDenom = 0.05f;

        /// <summary>
        /// The world size a gizmo must be to cover <paramref name="desiredPixels"/> of screen at view
        /// depth <paramref name="z"/>. Recomputed every frame: a handle that shrinks with distance is
        /// a handle that cannot be grabbed once the camera pulls back to look at a vehicle.
        /// </summary>
        internal static float WorldSize(float desiredPixels, float z, float fovDeg, float pixelHeight)
        {
            if (pixelHeight < 1f) pixelHeight = 1f;
            if (fovDeg < 1f) fovDeg = 1f;
            if (fovDeg > 175f) fovDeg = 175f;
            if (z < 0.01f) z = 0.01f;
            double tan = Math.Tan(fovDeg * 0.5 * Math.PI / 180.0);
            return (float)(desiredPixels * 2.0 * z * tan / pixelHeight);
        }

        /// <summary>Distance from a point to a SEGMENT (not a line) in screen pixels. A segment,
        /// because the arrows are finite and a click far past the tip is not a click on the arrow.</summary>
        internal static float SegmentDistance(float px, float py, float ax, float ay, float bx, float by)
        {
            float vx = bx - ax, vy = by - ay;
            float len2 = vx * vx + vy * vy;
            float t = len2 <= 1e-9f ? 0f : ((px - ax) * vx + (py - ay) * vy) / len2;
            if (t < 0f) t = 0f; else if (t > 1f) t = 1f;
            float dx = px - (ax + vx * t), dy = py - (ay + vy * t);
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Is this axis worth drawing and aimable at? Two refusals, and both matter:
        ///   BEHIND THE CAMERA - <c>WorldToScreenPoint</c> happily returns a screen position for a point
        ///   with negative depth, MIRRORED through the centre. An arrow whose pivot or tip is behind the
        ///   near plane would be picked where it is not, which is the worst kind of hit test.
        ///   TOO SHORT - an axis pointing nearly at the camera projects to a few pixels; it cannot be
        ///   aimed at, and dragging it divides by a denominator near zero.
        /// </summary>
        internal static bool AxisVisible(float pivotZ, float tipZ, float nearClip,
                                         float pivotX, float pivotY, float tipX, float tipY,
                                         float minPixels)
        {
            if (pivotZ <= nearClip || tipZ <= nearClip) return false;
            float dx = tipX - pivotX, dy = tipY - pivotY;
            return Math.Sqrt(dx * dx + dy * dy) >= minPixels;
        }

        /// <summary>The axis whose DRAWN segment the pointer is nearest to, or -1. All three share the
        /// pivot, so only the tips differ. Ties go to the lower index, which is X - arbitrary and
        /// stable, which is all a tie needs to be.</summary>
        internal static int NearestAxis(float pivotX, float pivotY, float[] tipX, float[] tipY,
                                        bool[] valid, float px, float py, float radius)
        {
            int best = -1;
            float bestD = radius;
            if (tipX == null || tipY == null) return -1;
            for (int i = 0; i < 3 && i < tipX.Length && i < tipY.Length; i++)
            {
                if (valid != null && (i >= valid.Length || !valid[i])) continue;
                float d = SegmentDistance(px, py, pivotX, pivotY, tipX[i], tipY[i]);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>
        /// ============ HOW FAR ALONG ONE AXIS THE MOUSE DRAGGED ============
        ///
        /// The constraint-plane technique, which is what every editor gizmo does and the only one that
        /// stays correct as the camera turns. A mouse gives a RAY, not a point; an axis gives a LINE.
        /// Two skew lines have no intersection, so the drag is resolved on a PLANE instead: the one
        /// through the pivot that CONTAINS the axis and faces the viewer as squarely as it can -
        /// normal <c>n = view - a*(view.a)</c>, i.e. the view direction with its along-axis part
        /// removed. Both the press ray and the current ray are intersected with that plane and only the
        /// along-axis part of the difference is kept.
        ///
        /// It REFUSES rather than guessing in the two degenerate cases: an axis pointing at the camera
        /// (the plane has no normal) and a ray sliding along the plane (the intersection runs to
        /// infinity). Both would answer with a jump of tens of metres from a one-pixel drag.
        /// </summary>
        internal static bool PlaneDelta(float[] pivot, float[] axis, float[] view,
                                        float[] pressOrigin, float[] pressDir,
                                        float[] nowOrigin, float[] nowDir,
                                        float minDenom, out float along)
        {
            along = 0f;
            if (pivot == null || axis == null || view == null) return false;
            float[] a = Unit(axis);
            if (a == null) return false;
            float va = Dot(view, a);
            float[] n = Unit(new[] { view[0] - a[0] * va, view[1] - a[1] * va, view[2] - a[2] * va });
            if (n == null) return false;

            float[] hitA, hitB;
            if (!RayPlane(pressOrigin, pressDir, pivot, n, minDenom, out hitA)) return false;
            if (!RayPlane(nowOrigin, nowDir, pivot, n, minDenom, out hitB)) return false;
            along = (hitB[0] - hitA[0]) * a[0] + (hitB[1] - hitA[1]) * a[1] + (hitB[2] - hitA[2]) * a[2];
            return true;
        }

        private static bool RayPlane(float[] origin, float[] dir, float[] point, float[] normal,
                                     float minDenom, out float[] hit)
        {
            hit = null;
            if (origin == null || dir == null) return false;
            float denom = Dot(dir, normal);
            if (Math.Abs(denom) < minDenom) return false;
            float t = ((point[0] - origin[0]) * normal[0] + (point[1] - origin[1]) * normal[1] +
                       (point[2] - origin[2]) * normal[2]) / denom;
            hit = new[] { origin[0] + dir[0] * t, origin[1] + dir[1] * t, origin[2] + dir[2] * t };
            return true;
        }

        /// <summary>
        /// ============ THE SPACE THE MANIFEST IS WRITTEN IN ============
        ///
        /// A world displacement carried into the PARENT-LOCAL frame the fit's <c>offset</c> lives in.
        /// <paramref name="basis"/> is the parent's local-to-world matrix as its three COLUMNS - the
        /// parent's own x, y and z axes expressed in world, LENGTH INCLUDED.
        ///
        /// The length is the whole point and the reason this is not <c>TransformDirection</c>: that
        /// call normalises away the parent's scale, so on a hand scaled to 0.5 every drag would move
        /// the gun twice as far as the mouse and the saved number would be double what was seen. Here
        /// the columns are solved as they are (Cramer on the 3x3), so scale - and even shear from a
        /// rotated scale higher up the chain - comes out right.
        ///
        /// A degenerate basis (a parent scaled to zero on some axis) is REFUSED. There is no local
        /// answer to give and inventing one moves the gun to infinity.
        /// </summary>
        internal static bool LocalFromWorld(float[] basis, float[] world, out float[] local)
        {
            local = null;
            if (basis == null || basis.Length < 9 || world == null || world.Length < 3) return false;
            float[] c0 = { basis[0], basis[1], basis[2] };
            float[] c1 = { basis[3], basis[4], basis[5] };
            float[] c2 = { basis[6], basis[7], basis[8] };
            float det = Det(c0, c1, c2);
            // Relative to the columns' own size: a hand at 1/100 scale is legitimate and has a tiny
            // determinant, a collapsed one is not. Comparing against an absolute epsilon would refuse
            // the first and accept the second.
            float span = Len(c0) * Len(c1) * Len(c2);
            if (span <= 1e-12f || Math.Abs(det) < span * 1e-4f) return false;
            float[] w = { world[0], world[1], world[2] };
            local = new[] { Det(w, c1, c2) / det, Det(c0, w, c2) / det, Det(c0, c1, w) / det };
            return true;
        }

        // ================================================================ the rotation rings

        /// <summary>The rings' radius as a fraction of the gizmo's on-screen size, i.e. INSIDE the
        /// arrows' tips - a ring drawn at the arrows' own length would sit exactly on top of the three
        /// arrow heads and every press there would be ambiguous.</summary>
        internal const float RingFraction = 0.72f;
        /// <summary>How many segments a ring is sampled into. It is drawn AND picked as this polyline,
        /// so the number is not cosmetic: what is clickable is by construction what is visible.</summary>
        internal const int RingSegments = 48;
        /// <summary>How near a click has to land on a ring's drawn polyline. Slightly tighter than the
        /// arrows' (<see cref="PickRadius"/>) because three rings cross each other in four places and a
        /// generous radius there is a coin toss.</summary>
        internal const float RingPickRadius = 8f;
        /// <summary>|dot(ray, axis)| below which the ring is edge-on: the ray is sliding ALONG the ring's
        /// plane and its intersection runs to infinity. Refused, never approximated.</summary>
        internal const float MinRingDot = 0.12f;
        /// <summary>How far from the pivot a ring hit has to land, as a fraction of the ring's radius. A
        /// hit ON the pivot has no direction, so there is no angle to measure from it.</summary>
        internal const float MinRingRadius = 0.15f;
        /// <summary>How unequal the parent's three scales may be before a world rotation stops being
        /// representable as a child-local one. Calibration knob, judged by eye on the result.</summary>
        internal const float ScaleTolerance = 0.02f;

        /// <summary>
        /// ============ A RING IS ONLY HONEST UNDER A SIMILARITY ============
        ///
        /// A ring drag is a rotation measured in WORLD space and written down as a PARENT-LOCAL euler.
        /// Those two are the same rotation only when the parent's frame is a similarity - uniform scale,
        /// right-handed. Under a non-uniform or mirrored parent a pure world rotation is not
        /// representable as a child-local TRS at all: what comes back is skew, silently, and the fit's
        /// saved numbers are then wrong in a way no readout would show.
        ///
        /// So the rings REFUSE instead, with a word, and the turn buttons - which work in the local
        /// frame directly and are therefore always correct - stay the way through. <paramref name="basis"/>
        /// is the parent's local-to-world as its three COLUMNS, the same nine numbers
        /// <see cref="LocalFromWorld"/> takes.
        ///
        /// PP's own weapon parents were not measured before this was written, so this is a guard rather
        /// than a known case: if every hand in the game turns out uniform it costs three square roots a
        /// press and never fires.
        /// </summary>
        internal static bool RingsUsable(float[] basis, float tolerance, out string why)
        {
            why = null;
            if (basis == null || basis.Length < 9) { why = "no parent frame"; return false; }
            float[] c0 = { basis[0], basis[1], basis[2] };
            float[] c1 = { basis[3], basis[4], basis[5] };
            float[] c2 = { basis[6], basis[7], basis[8] };
            float l0 = Len(c0), l1 = Len(c1), l2 = Len(c2);
            float max = Math.Max(l0, Math.Max(l1, l2)), min = Math.Min(l0, Math.Min(l1, l2));
            if (min <= 1e-6f || float.IsNaN(max)) { why = "the parent is scaled to nothing on an axis"; return false; }
            if (tolerance < 0f) tolerance = 0f;
            if ((max - min) / max > tolerance)
            {
                why = "the hand this weapon hangs on is scaled UNEVENLY (" +
                      l0.ToString("0.###") + "," + l1.ToString("0.###") + "," + l2.ToString("0.###") +
                      "), so a world rotation cannot be written down as a local one without skew";
                return false;
            }
            if (Det(c0, c1, c2) <= 0f)
            {
                why = "the hand this weapon hangs on is MIRRORED (negative scale), so a ring would turn " +
                      "the gun the opposite way to the drag";
                return false;
            }
            return true;
        }

        /// <summary>
        /// ============ HOW FAR THE MOUSE TURNED A RING ============
        ///
        /// The standard signed ring-plane measure. Both rays are intersected with the plane through the
        /// pivot whose NORMAL is the ring's axis; the two hits, taken as directions out of the pivot,
        /// give the angle
        ///
        ///     angle = atan2( a . (v0 x v1), v0 . v1 )
        ///
        /// which is signed about <paramref name="axis"/> and correct through a full turn in either
        /// direction, unlike an acos.
        ///
        /// It measures from the PRESS ray every frame, not from the previous frame: the caller then
        /// composes ONE rotation onto the press-time orientation. Per-frame accumulation of eulers is
        /// what makes a gizmo drift and then snap near a gimbal singularity - the euler numbers there
        /// jump to an equivalent representation, which round-trips as a rotation but not as an addition.
        ///
        /// THREE REFUSALS, none of them approximated:
        ///   - a ray nearly parallel to the ring's plane (|dir.axis| &lt; minDot): the intersection runs
        ///     to infinity and a one-pixel drag would answer with a hundred degrees;
        ///   - a plane behind the camera (t &lt;= 0), which is the ring seen from the wrong side;
        ///   - a hit landing on the pivot (closer than <paramref name="minRadius"/>): no direction, so
        ///     no angle.
        /// </summary>
        internal static bool RingAngle(float[] pivot, float[] axis,
                                       float[] pressOrigin, float[] pressDir,
                                       float[] nowOrigin, float[] nowDir,
                                       float minDot, float minRadius, out float degrees)
        {
            degrees = 0f;
            if (pivot == null || pivot.Length < 3) return false;
            float[] a = Unit(axis);
            if (a == null) return false;
            float[] v0, v1;
            if (!RingHit(pivot, a, pressOrigin, pressDir, minDot, minRadius, out v0)) return false;
            if (!RingHit(pivot, a, nowOrigin, nowDir, minDot, minRadius, out v1)) return false;

            float[] c = { v0[1] * v1[2] - v0[2] * v1[1],
                          v0[2] * v1[0] - v0[0] * v1[2],
                          v0[0] * v1[1] - v0[1] * v1[0] };
            double d = Math.Atan2(Dot(a, c), Dot(v0, v1)) * 180.0 / Math.PI;
            if (double.IsNaN(d)) return false;
            degrees = (float)d;
            return true;
        }

        /// <summary>One ray against the ring's plane, as the UNIT direction out of the pivot.</summary>
        private static bool RingHit(float[] pivot, float[] axis, float[] origin, float[] dir,
                                    float minDot, float minRadius, out float[] v)
        {
            v = null;
            float[] d = Unit(dir);
            if (d == null || origin == null || origin.Length < 3) return false;
            float denom = Dot(d, axis);
            if (Math.Abs(denom) < minDot) return false;
            float t = ((pivot[0] - origin[0]) * axis[0] + (pivot[1] - origin[1]) * axis[1] +
                       (pivot[2] - origin[2]) * axis[2]) / denom;
            if (t <= 0f || float.IsNaN(t) || float.IsInfinity(t)) return false;   // the plane is behind us
            float[] w = { origin[0] + d[0] * t - pivot[0],
                          origin[1] + d[1] * t - pivot[1],
                          origin[2] + d[2] * t - pivot[2] };
            if (Len(w) < minRadius) return false;                                 // landed on the pivot
            v = Unit(w);
            return v != null;
        }

        /// <summary>Distance in pixels from a point to a CLOSED polyline - the ring as it is actually
        /// drawn. The same routine the arrows' <see cref="NearestAxis"/> leans on, walked round a
        /// loop, so a ring is clickable exactly where it is visible.</summary>
        internal static float PolylineDistance(float[] xs, float[] ys, float px, float py)
        {
            if (xs == null || ys == null || xs.Length < 2 || ys.Length < xs.Length) return float.MaxValue;
            float best = float.MaxValue;
            for (int i = 0; i < xs.Length; i++)
            {
                int j = (i + 1) % xs.Length;
                float d = SegmentDistance(px, py, xs[i], ys[i], xs[j], ys[j]);
                if (d < best) best = d;
            }
            return best;
        }

        /// <summary>Which of the three rings a click lands on, or -1. Returns the RING INDEX (0..2); the
        /// caller is what offsets it into the gizmo's own handle numbering.</summary>
        internal static int NearestRing(float[][] ringX, float[][] ringY, bool[] valid,
                                        float px, float py, float radius)
        {
            if (ringX == null || ringY == null || valid == null) return -1;
            int best = -1;
            float bestD = radius;
            for (int i = 0; i < ringX.Length && i < ringY.Length && i < valid.Length; i++)
            {
                if (!valid[i]) continue;
                float d = PolylineDistance(ringX[i], ringY[i], px, py);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private static float Det(float[] a, float[] b, float[] c)
        {
            return a[0] * (b[1] * c[2] - b[2] * c[1])
                 - a[1] * (b[0] * c[2] - b[2] * c[0])
                 + a[2] * (b[0] * c[1] - b[1] * c[0]);
        }

        private static float Dot(float[] a, float[] b) { return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]; }

        private static float Len(float[] a) { return (float)Math.Sqrt(Dot(a, a)); }

        /// <summary>Normalised, or NULL when there is nothing to normalise. Null rather than a zero
        /// vector, because every caller here has to REFUSE on that case rather than carry on with a
        /// direction that does not exist.</summary>
        private static float[] Unit(float[] v)
        {
            if (v == null || v.Length < 3) return null;
            float len = Len(v);
            if (len < 1e-6f || float.IsNaN(len) || float.IsInfinity(len)) return null;
            return new[] { v[0] / len, v[1] / len, v[2] / len };
        }

        // ================================================================ saved, or messed about with

        /// <summary>
        /// ============ HAS THIS FIT BEEN TOUCHED SINCE IT WAS LOADED OR SAVED ============
        ///
        /// The panel says SAVED or MODIFIED, and this is the whole of the decision. Without it there is
        /// no way to tell an experiment from the state on disk - which is the question "как бы я сейчас
        /// там не наколбасил" actually asks. No history, no undo tree: one comparison against the last
        /// value that was read from or written to the manifest is the entire feature.
        ///
        /// The epsilon is not decoration. Every one of these numbers has been through a float multiply
        /// and back, so exact equality would report MODIFIED after a save that changed nothing.
        /// </summary>
        internal static bool Same(float scaleA, float[] eulerA, float[] offsetA,
                                  float scaleB, float[] eulerB, float[] offsetB, float eps)
        {
            if (eps <= 0f) eps = 1e-5f;
            if (Math.Abs(scaleA - scaleB) > eps) return false;
            return Triple(eulerA, eulerB, eps) && Triple(offsetA, offsetB, eps);
        }

        private static bool Triple(float[] a, float[] b, float eps)
        {
            if (a == null || b == null) return a == b;
            if (a.Length < 3 || b.Length < 3) return false;
            for (int i = 0; i < 3; i++) if (Math.Abs(a[i] - b[i]) > eps) return false;
            return true;
        }

        /// <summary>
        /// ============ WHAT THIS UNIT MAY BE OFFERED, AND WHAT DROPS OUT OF ITS HAND ============
        ///
        /// The list a newly picked unit gets, plus the one thing that is easy to forget and impossible
        /// to see: THE SELECTION ITSELF IS PART OF THE ANSWER. A four-legged mutoid that inherits the
        /// rifle the previous soldier was holding is a lie in exactly the way an unfiltered list is a
        /// lie - so a selection the new list does not contain comes back null rather than staying put.
        ///
        /// <paramref name="canHold"/> null keeps EVERYTHING on purpose. A unit whose compatibility
        /// cannot be decided must be shown the catalogue, not an empty panel: "nothing fits" and "we
        /// could not ask" look identical on screen and only one of them is a fact.
        /// </summary>
        internal static List<T> Offer<T>(IEnumerable<T> all, Func<T, bool> canHold, ref T selected)
            where T : class
        {
            int rejected;
            return Offer(all, canHold, null, ref selected, out rejected);
        }

        /// <summary>
        /// ============ A WEAPON THIS MOD BUILT IS NEVER ALLOWED TO VANISH ============
        ///
        /// The same list, plus the one rule the first version got wrong. The catalogue is two hundred
        /// shipped weapons and a handful of the author's own, sorted by name - so the author's land
        /// wherever the alphabet puts them, somewhere below the fold of a scrolling list full of
        /// AC_ and AN_. And they are the ONLY ones the workbench can actually do anything to: a shipped
        /// weapon has no manifest row, so it is there for comparison and nothing else.
        ///
        /// Worse, the game's own slot test can legitimately refuse one of them for the selected unit,
        /// and then it does not appear AT ALL - which reads as "my weapon did not load", the one
        /// conclusion that sends an author hunting through a bake that worked.
        ///
        /// So <paramref name="mine"/> - the weapons this mod built - are kept UNCONDITIONALLY and
        /// listed FIRST, and how many of them the slot test refused comes back in
        /// <paramref name="minesRefused"/> so the panel can say it in words instead of by omission.
        /// </summary>
        internal static List<T> Offer<T>(IEnumerable<T> all, Func<T, bool> canHold, Func<T, bool> mine,
                                         ref T selected, out int minesRefused)
            where T : class
        {
            List<T> ours = new List<T>(), theirs = new List<T>();
            minesRefused = 0;
            if (all != null)
                foreach (T item in all)
                {
                    if (item == null) continue;
                    bool held = canHold == null || canHold(item);
                    if (mine != null && mine(item))
                    {
                        ours.Add(item);
                        if (!held) minesRefused++;
                    }
                    else if (held) theirs.Add(item);
                }
            ours.AddRange(theirs);
            if (selected != null && !ours.Contains(selected)) selected = null;
            return ours;
        }
    }
}
