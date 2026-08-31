using System;
using System.Collections.Generic;
using System.Globalization;
using Base.Cameras;
using Base.Core;
using Base.Defs;
// Base.Input holds no type called 'Input', so the bare 'Input' the Arm below reads is still
// UnityEngine's - which is the whole point: the bench keeps its own mouse while the game loses its.
using Base.Input;
using Base.Levels;
using Base.Lighting;
using PhoenixPoint.Common.Core;
using PhoenixPoint.Common.Entities.Addons;
using PhoenixPoint.Common.Entities.Items;
using PhoenixPoint.Common.UI;
using PhoenixPoint.Common.Utils;
using PhoenixPoint.Geoscape.Levels;
using PhoenixPoint.Geoscape.View.DataObjects;
using PhoenixPoint.Tactical.Entities;
using PhoenixPoint.Tactical.Entities.Animations;
using PhoenixPoint.Tactical.Entities.Equipments;
using PhoenixPoint.Tactical.Entities.Weapons;
using Morgott.ContentTool.Tactical;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// ============ THE WEAPON FIT WORKBENCH ============
    ///
    /// A full-screen developer view: a unit standing in the squad bay on the RIGHT, and on the LEFT
    /// the three lists that make a fit a thing you LOOK at rather than a number you guess - pick the
    /// unit, pick the weapon, nudge it per axis, save. The panel is on the left because
    /// GeoscapeSoldierEditCenter stands the unit right of screen centre, which is where the panel
    /// used to be and why it covered exactly the thing it exists to show.
    ///
    /// WHY IT IS NOT A UI STATE. Every screen in this game is a state on a StateStack, and the one
    /// genuinely dangerous thing a dev tool can do here is leave that stack wedged - the project's own
    /// law is "never Enter a popped UI state", and the cheapest way to keep it is to never push one.
    /// So the workbench touches NOTHING the stack owns: it swings the squad bay scene on, hints the
    /// camera, disables the canvases that are already open, and draws itself in IMGUI on top. Closing
    /// undoes exactly those four, in reverse, each in its own try - and whatever screen the user came
    /// from is still the top of the stack, untouched, because it was never left.
    ///
    /// IMGUI is the right rendering for this and not a shortcut: the game itself ships
    /// GameConsoleWindow as a full-screen OnGUI, so this is the surface a dev tool is expected to use,
    /// and it costs no prefab, no canvas and no third-party UI.
    ///
    /// IT NEEDS A CAMPAIGN. The squad bay is part of the geoscape level; outside a loaded campaign
    /// there is no bay, no character builder and nothing to stand a soldier on. That is a clean
    /// refusal with a reason, not a silent black screen.
    ///
    /// VANILLA WEAPONS ARE VIEWABLE, NEVER TUNABLE. A shipped weapon has no manifest row, so there is
    /// nowhere for a save to go; it is listed and can be put in the hand for COMPARISON - which is the
    /// whole reason to look at one - and its axis buttons are simply not drawn. Offering a save that
    /// cannot work is worse than offering none.
    ///
    /// ============ THE HOTKEY IS NOT A FREE CHOICE (2026-08-29) ============
    /// This opened on F9, and F9 is the game's own QUICKLOAD (GeoscapeViewState.cs:175-178 ->
    /// HomeScreenView.QuickLoadGame:257-267). Unity's Input and the game's input map both see the same
    /// press, so every F9 opened the workbench AND reloaded the campaign behind it; on a quicksave the
    /// game could not deserialize, that load ended at the MAIN MENU with the campaign gone. The
    /// workbench never touched the level - but it chose the key that did. Hence two rules, both
    /// enforced rather than remembered: the hotkey is a CHORD off the function row, and
    /// <see cref="BenchList.IsGameOwned"/> refuses to arm it at all if someone puts it back on one of
    /// the game's own keys.
    ///
    /// AND IT LETS GO. Whatever the reason, if the level stops playing while the panel is up - a load,
    /// a mission, a return to the menu - the workbench closes itself rather than holding references
    /// into a level that no longer exists. Open likewise REFUSES unless the level is actually playing.
    /// </summary>
    internal static class FitBench
    {
        /// <summary>Ctrl+Alt+B. Off the function row on purpose - the game's destructive pair
        /// (QuickSave/QuickLoad) lives there - and a chord because the game's own dev surface uses one
        /// (GameConsoleInput.cs:53-66), so a stray single press can never open it.</summary>
        internal const KeyCode Hotkey = KeyCode.B;
        internal const string HotkeyLabel = "Ctrl+Alt+B";

        private static Arm arm;

        internal static void Install()
        {
            if (arm != null) return;
            GameObject go = new GameObject("ContentTool.FitBench");
            UnityEngine.Object.DontDestroyOnLoad(go);
            arm = go.AddComponent<Arm>();
            if (BenchList.IsGameOwned(Hotkey.ToString()))
                ContentToolMain.Say("ct_bench: hotkey " + HotkeyLabel + " is a key THE GAME ITSELF " +
                                    "binds (" + Hotkey + "), so no hotkey is armed - it would fire the " +
                                    "game's action too. Use the 'ct_bench' console command.");
        }

        internal static void Uninstall()
        {
            Close();
            if (arm != null) UnityEngine.Object.Destroy(arm.gameObject);
            arm = null;
        }

        /// <summary>ct_bench - open, close, or toggle. Same entry the hotkey uses, so there is one
        /// enter path and one exit path and they cannot drift apart.</summary>
        /// <summary>
        /// The RESCUE PATH, and it is gated on <see cref="entered"/> rather than <see cref="open"/> on
        /// purpose. Those two are not the same thing: <c>open</c> means the panel is being drawn,
        /// <c>entered</c> means the game has been changed and something has to put it back. An Open
        /// that threw half way through - or a panel that closed itself out of OnGUI - used to leave
        /// <c>open</c> false with the canvases still hidden and the camera still held, and then
        /// 'ct_bench close' answered "not open" while the screen stayed gone. That is the shape of "I
        /// pressed something and I cannot get it back", so close now works whenever there is anything
        /// at all to undo.
        /// </summary>
        internal static string Run(string[] args)
        {
            string verb = args != null && args.Length > 0 ? args[0] : (entered ? "close" : "open");
            switch (verb)
            {
                case "open":  return open ? "ct_bench: already open" : Open();
                case "close": return entered ? Close() : "ct_bench: not open";
                case "reset": return entered ? ResetView() : "ct_bench: not open";
                case "unit":  return open ? Choose(args) : "ct_bench: not open";
                default:      return "ct_bench: args are [open|close|reset|unit <name>], or none to " +
                                     "toggle. Hotkey " + HotkeyLabel;
            }
        }

        /// <summary>ct_bench unit &lt;name&gt; - the unit picker's own answer typed instead of clicked, for
        /// a script or a hand already on the console. Exact def name wins; otherwise a SINGLE substring
        /// match, and an ambiguous name is refused WITH its candidates rather than guessed at. It ends
        /// in <see cref="Pick"/> like the list row does, so there is one selection path and it cannot
        /// drift from what the mouse does.</summary>
        private static string Choose(string[] args)
        {
            string q = args != null && args.Length > 1
                     ? string.Join(" ", args, 1, args.Length - 1).Trim() : "";
            if (q.Length == 0)
                return "ct_bench: 'unit <name>' needs a name - " + units.Count + " templates to match.";
            List<TacCharacterDef> hits = new List<TacCharacterDef>();
            foreach (TacCharacterDef d in units)
            {
                if (string.Equals(d.name, q, StringComparison.OrdinalIgnoreCase))
                { hits.Clear(); hits.Add(d); break; }
                if (BenchList.Matches(d.name, q)) hits.Add(d);
            }
            if (hits.Count == 0) return "ct_bench: no unit template matches '" + q + "'.";
            if (hits.Count > 1)
            {
                List<string> names = new List<string>();
                for (int i = 0; i < hits.Count && i < 8; i++) names.Add(hits[i].name);
                return "ct_bench: '" + q + "' matches " + hits.Count + " templates, so nothing was " +
                       "changed - name one of: " + string.Join(", ", names.ToArray()) +
                       (hits.Count > names.Count ? ", ..." : "");
            }
            unit = hits[0];
            // Picked, so out of the way - the same thing a click on the row does.
            unitsOpen = false;
            Pick();
            return message;
        }

        // ---------------------------------------------------------------- what is on screen

        private static bool open;
        /// <summary>Anything at all has been changed in the game and is waiting to be put back. Set
        /// before the FIRST mutation, cleared only by <see cref="Close"/>. See <see cref="Run"/>.</summary>
        private static bool entered;
        private static GeoLevelController level;
        private static GeoSquadBayReference bay;
        private static CameraDirector director;
        private static LightingManager lighting;
        private static Level standingIn;
        /// <summary>The scene that was switched on, and the lighting that was applied, BEFORE the
        /// workbench swung the squad bay in. See the snapshot comment in <see cref="Open"/>: "default"
        /// is not a snapshot, and restoring to it is a change of its own.</summary>
        private static GeoSceneReferences.ActiveSceneReference priorScene;
        private static LightingSettingsDef priorLighting;
        private static bool lightingTaken;
        private static Quaternion sceneRotation;
        private static Vector3 scenePosition, sceneScale, platformScale;
        private static readonly List<Canvas> hidden = new List<Canvas>();
        /// <summary>
        /// ============ THE RIGHT-CLICK THAT GREYED THE MODEL AND MADE IT HUGE (2026-08-29) ============
        ///
        /// The workbench hides the canvases, but the STATE STACK underneath them is still listening: RMB
        /// is the game's own 'Cancel' action (GameData\input\inputmap.json), and the screen the user came
        /// from answers it by POPPING itself (UIStateEditSoldier.cs:248-260, UIStateGeoRoster.cs:233-245).
        /// Its exit path then re-applies DefaultLightingSettings over the bench's edit lighting - that is
        /// the "grey" - and the module that rebuilds behind it writes its own localScale onto the char
        /// root and the platform (UIModuleActorCycle.cs:435-468) - that is the "huge". Neither is a bug
        /// in anything this file does; both are the game correctly answering an input the bench never
        /// meant to forward.
        ///
        /// So the bench does what the game's OWN dev surface does while it is up: it holds
        /// <c>InputController.IncDisableHandlersCalling</c> (GameConsoleInput.cs:242-253), which is a
        /// REFERENCE COUNT (InputController.cs:637-645) gating the whole dispatch (:1099-1108). No
        /// Harmony patch, no state pushed, and raw <c>UnityEngine.Input</c> - which is all the bench
        /// itself reads - is untouched.
        /// </summary>
        private static InputController input;
        private static bool inputHeld;
        /// <summary>
        /// ============ THE 13,128 EXCEPTIONS THAT ATE THE SCREEN (2026-08-29) ============
        ///
        /// The user's Player.log, one line after "ct_bench open", then thirteen thousand more:
        ///
        ///   NullReferenceException
        ///     at SoftMasking.SoftMask.WorldToMask ()
        ///     at SoftMasking.SoftMask.FillCommonParameters ()
        ///     at SoftMasking.SoftMask.OnWillRenderCanvases ()
        ///     at UnityEngine.Canvas.SendWillRenderCanvases ()
        ///
        /// SoftMask is the third-party masking component the game's own UI is built with. Disabling a
        /// root <c>Canvas</c> stops it DRAWING but does not stop its children's components running -
        /// SoftMask stays subscribed to <c>Canvas.willRenderCanvases</c> and keeps resolving a canvas
        /// it can no longer resolve, once per frame per masked element, forever. Unity logs every one
        /// with a full managed stack trace: 18 MB of log, the frame rate on the floor, and a UI that
        /// never comes back. Nothing in this mod threw and nothing said a word about it, which is why
        /// it read as "everything disappeared and I cannot get it back".
        ///
        /// So the components are switched off with the canvas that owns them, by TYPE NAME - the
        /// SoftMasking assembly is not referenced by this mod and does not need to be for one bool -
        /// and switched back on by Close in the same breath as the canvases.
        /// </summary>
        private static readonly List<Behaviour> masks = new List<Behaviour>();

        private static List<TacCharacterDef> units = new List<TacCharacterDef>();
        /// <summary>Which of <see cref="units"/> a content mod BUILT, by identity. Computed once per
        /// catalogue read so the picker, the sort and the clip markers all answer from one set rather
        /// than each asking CreatureBuild again inside a GUI loop.</summary>
        private static HashSet<TacCharacterDef> ourUnits = new HashSet<TacCharacterDef>();
        private static List<WeaponDef> weapons = new List<WeaponDef>();
        private static List<WeaponDef> offered = new List<WeaponDef>();
        private static bool offerAll;
        private static int mine, refused;      // weapons this mod built, and how many this unit refuses
        private static TacCharacterDef unit;
        private static WeaponDef weapon;
        /// <summary>What UNEQUIP took out of the hand, so RE-EQUIP has something to put back.</summary>
        private static WeaponDef lastWeapon;
        private static string unitFilter = "", weaponFilter = "";
        private static Vector2 unitScroll, weaponScroll, messageScroll;
        private static float step = 0.01f, turn = 5f, scaleStep = 0.01f;
        /// <summary>
        /// The PREVIEW model scale: a multiplier on the pose the game itself chose for the displayed
        /// character, and nothing else. It is a view knob exactly like <see cref="zoom"/> and
        /// <see cref="lift"/> - it is written into no def, no manifest and no save, and Close puts
        /// <c>SceneRoot.localScale</c> back off the open-time snapshot regardless of it. Its one job is
        /// the judgement no number can make: is this foreign model the right SIZE next to a soldier.
        /// </summary>
        private static float viewScale = 1f;
        private static string scaleText = "1.00";
        private static string message = "";
        private static Texture2D backdrop;

        // ---- the camera, and the pose we put it in ----
        private static Camera cam;
        /// <summary>
        /// ============ ONE LEDGER ROW PER CAMERA WE EVER TOUCHED ============
        ///
        /// This used to be a single <c>brain</c> reference and a single remembered pose, which is only
        /// correct while there is exactly ONE camera for the whole session. There is not: a scene swap,
        /// another mod, or the game's own director can hand back a REPLACEMENT camera, and
        /// <see cref="TakeCamera"/> runs again on every RESET VIEW. With one slot the second take
        /// overwrote the first - so the old camera's CinemachineBrain stayed disabled forever with
        /// nothing left pointing at it, and Close then wrote the OLD camera's pose onto the NEW one.
        ///
        /// A row per camera fixes both: a camera already held is never re-recorded, and Close walks
        /// every surviving row and puts each one back the way it found it.
        /// </summary>
        private sealed class Held
        {
            internal Camera camera;
            internal Vector3 position;
            internal Quaternion rotation;
            internal Behaviour brain;          // its CinemachineBrain, if it had one that was on
            /// <summary>
            /// ============ WHY THE HANDLES WENT SOFT OFF THE BODY (2026-08-29) ============
            ///
            /// The arrows and rings are drawn from <c>OnRenderObject</c>, i.e. straight into the
            /// camera's colour target - and that target is then handed to POST-PROCESSING before
            /// anyone sees it. The lighting the bench installs (level.View.EditSolderLightingSettings)
            /// brings its own PostProcessVolume: LightingManager.ApplyPostProcessOptions:168-178 reads
            /// the volume off the lights root and switches AmbientOcclusion / Bloom / DEPTH OF FIELD /
            /// ... on it. DOF blurs by circle of confusion, and the CoC is taken from the DEPTH
            /// BUFFER - which the gizmo's GL lines do not write. So over the character the gizmo
            /// inherits the character's in-focus CoC and stays crisp, and over the background it
            /// inherits the background's, which is exactly the "soft and washed out off the body" the
            /// user sees. Bloom then adds the wash.
            ///
            /// The fix is the game's OWN lever: PostProcessLayer.enabled, which is what
            /// ForcedPostProcessLayerSettings' 'dbg_toggle_post_processing' console command toggles
            /// (:20-31). One Behaviour flag on the bench's camera, remembered here and put back by
            /// <see cref="ReleaseCamera"/> next to the brain - and a bench with no bloom and no DOF is
            /// the right picture anyway, because a fit is judged on an edge. Fetched BY NAME for the
            /// same reason the brain is: the post-processing assembly is not referenced by this mod and
            /// does not need to be for one bool.
            /// </summary>
            internal Behaviour post;
            /// <summary>Its near plane before <see cref="BenchList.NearClip"/> was written over it.
            /// The geoscape's own near plane is authored for a planet, and at the close range the zoom
            /// clamp now allows it clipped the weapon away.</summary>
            internal float near;
        }
        private static readonly List<Held> cameras = new List<Held>();
        private static Vector3 framePos; private static Quaternion frameRot;
        private static bool framed;
        /// <summary>The two calibration knobs, remembered for the session and nowhere else. Framing is
        /// an eyeball judgement - the algebra puts the unit inside the free region, it cannot know how
        /// much air around him reads as "well framed" - so zoom and lift are nudged live.</summary>
        private static float zoom = BenchList.ZoomDefault, lift = 0f;
        /// <summary>The orbit the mouse drives, as offsets ON TOP of the bay's own authored look
        /// direction: yaw about world up, pitch about the camera's own right. Both are knobs like
        /// zoom and lift, and RESET VIEW puts all four back.</summary>
        private static float yaw, pitch;
        private static float frameRadius;
        /// <summary>
        /// ============ THE FREE CAMERA, AND WHY IT IS ONE VECTOR ============
        ///
        /// The orbit is always about the MEASURED CENTRE of what is standing there, and that is right
        /// until the zoom goes in close: the pivot is then inside the body, the weapon is off screen,
        /// and no amount of orbiting brings it back. So the aim point itself moves - a world-space
        /// offset added to the measured centre in <see cref="Reframe"/>, which shifts the pivot AND the
        /// camera together, i.e. exactly the "pan" of every DCC viewport. Middle-drag moves it in the
        /// camera's own screen plane, WASD/QE fly it along the view axes, and RECENTRE puts it back to
        /// zero - which is the whole of the restore path, because the offset is the only state.
        /// </summary>
        private static Vector3 pan;
        /// <summary>The distance the last <see cref="Reframe"/> computed, kept because the pan has to
        /// convert PIXELS into metres and the conversion is a function of that distance.</summary>
        private static float frameDist;

        /// <summary>The rig's own anim actions, as DisplayCharacter hands them back - the thing that
        /// swaps a soldier's idle for the number of hands his weapon needs.</summary>
        private static TacActorAnimActions animActions;

        /// <summary>The LIVE Equipment the last rebuild produced for <see cref="weapon"/> - what
        /// <see cref="Handed"/> found, kept because the transport needs the same object to ask for the
        /// clip set that matches what is actually in the hand. Null when the hand is empty.</summary>
        private static Equipment held;

        // ---------------------------------------------------------------- enter

        private static string Open()
        {
            // FIRST, before anything is touched: is there a level, and is it PLAYING? A level that is
            // Loading or Unloading (Level.State, Base\Levels\Level.cs) is a level whose scene objects
            // are being built or destroyed underneath us - posing a soldier in it is at best pointless
            // and at worst a fistful of references to things about to die. Refusing costs the user one
            // line; the alternative costs him whatever the level was.
            standingIn = GameUtl.CurrentLevel();
            if (standingIn == null || !standingIn.IsPlaying)
            {
                standingIn = null;
                return "ct_bench REFUSED: the level is not playing right now (it is loading, unloading " +
                       "or already gone). Nothing was touched. Try again once the geoscape is up.";
            }

            level = UnityEngine.Object.FindObjectOfType<GeoLevelController>();
            bay = level == null || level.SceneReferences == null ? null : level.SceneReferences.SquadBay;
            if (bay == null || bay.CharacterBuilder == null)
            {
                level = null; standingIn = null;
                return "ct_bench REFUSED: the workbench stands a unit in the SQUAD BAY, and the squad " +
                       "bay is part of a loaded geoscape campaign. Load or start a campaign first.";
            }

            director = UnityEngine.Object.FindObjectOfType<CameraDirector>();
            lighting = UnityEngine.Object.FindObjectOfType<LightingManager>();

            // ---- FROM HERE ON THE GAME IS BEING CHANGED ----
            // The flag goes up BEFORE the first mutation and the whole remainder runs inside one try,
            // so that a throw anywhere below is undone rather than left half-applied. That is defect 1:
            // the first version let an exception out of Open with `open` still false, which stranded the
            // canvases, the camera and the lighting with no path back - 'ct_bench close' answered "not
            // open" and the hotkey toggled a panel that was never drawn.
            entered = true;
            hidden.Clear();
            masks.Clear();
            cameras.Clear();
            try
            {
                // FIRST of all the mutations: while the bench is up, the game must not answer input at
                // all. See the comment on <see cref="input"/> - a stray RMB is 'Cancel', and Cancel pops
                // the screen the bench is standing on top of.
                SuspendInput();
                // WHAT WAS THERE, not what the default is. Close used to force the Geoscape scene and
                // the DEFAULT lighting settings unconditionally, which is only the right answer if the
                // workbench was opened from the geoscape with untouched lighting - open it from the
                // vehicle bay, or with a screen that had set its own lighting, and leaving the bench
                // "restored" the game to a state it was never in. Both ARE readable, so both are
                // snapshotted: the active scene from which of the four scene roots is switched on
                // (GeoSceneReferences.ActivateScene:77-98 is the only thing that moves them), the
                // lighting from LightingManager.CurrentLightingSettingsDef, which is public.
                priorScene = ActiveScene(level.SceneReferences);
                priorLighting = lighting == null ? null : lighting.CurrentLightingSettingsDef;
                lightingTaken = lighting != null;
                // The bay's own pose, remembered whole: the drag turns SceneRoot and Posed() re-poses
                // and re-SCALES it per unit, so all four numbers have to come back, not the rotation
                // alone.
                sceneRotation = bay.SceneRoot != null ? bay.SceneRoot.rotation : Quaternion.identity;
                scenePosition = bay.SceneRoot != null ? bay.SceneRoot.localPosition : Vector3.zero;
                sceneScale = bay.SceneRoot != null ? bay.SceneRoot.localScale : Vector3.one;
                platformScale = bay.CharBuilderPlatform != null ? bay.CharBuilderPlatform.localScale : Vector3.one;
                bay.CharacterBuilder.OnCharacterRebuilded += Posed;

                level.SceneReferences.ActivateScene(GeoSceneReferences.ActiveSceneReference.SquadBay);
                if (lighting != null && level.View != null)
                    lighting.SetLighting(level.View.EditSolderLightingSettings, null);
                if (director != null)
                    director.Hint(CameraDirectorHint.GeoscapeSoldierEditCenter,
                                  new CameraDirectorParams { Origin = bay.CameraLookFrom });
                TakeCamera();
                Hide();

                Catalog();
                open = true;
                unitsOpen = weaponsOpen = true;
                panelScroll = Vector2.zero;
                if (unit == null && units.Count > 0) unit = units[0];
                Pick();
                Reframe();
            }
            catch (Exception ex)
            {
                string undone = Close();
                return "ct_bench REFUSED: opening threw " + ex.GetType().Name + ": " + ex.Message +
                       " - everything it had already changed was put back. " + undone;
            }
            return "ct_bench open (" + units.Count + " unit template(s), " + ourUnits.Count +
                   " of them built by a content mod and listed FIRST, " + weapons.Count +
                   " weapon(s), " + mine + " of them built by this mod and listed FIRST). " +
                   HotkeyLabel + ", the RESET VIEW button, or 'ct_bench close' to leave.";
        }

        /// <summary>Take the input lock, at most once - the count is the game's and an unbalanced
        /// increment leaves the game deaf for the rest of the session.</summary>
        private static void SuspendInput()
        {
            if (inputHeld) return;
            input = GameUtl.GameComponent<InputController>();
            if (input == null) return;
            input.IncDisableHandlersCalling();
            inputHeld = true;
        }

        /// <summary>Give it back, exactly once, and only if the increment actually happened. Throwing is
        /// deliberate: <see cref="Close"/> runs this as a named Step, so a failure is reported and
        /// retried rather than silently leaving the game unable to hear a click.</summary>
        private static void ResumeInput()
        {
            if (!inputHeld) { input = null; return; }
            if (input != null) input.DecDisableHandlersCalling();
            inputHeld = false;
            input = null;
        }

        /// <summary>
        /// WHICH of the four scene roots is switched on right now. There is no getter for it -
        /// <c>GeoSceneReferences</c> exposes only <c>ActivateScene</c> (:77-98) - but the state IS
        /// readable, because that method is the only thing that moves them and it leaves exactly one
        /// of SquadBay / VehicleBay / Interception active with the other two off. The Geoscape root is
        /// never deactivated (:86 guards it), so "none of the three" IS the geoscape.
        /// </summary>
        private static GeoSceneReferences.ActiveSceneReference ActiveScene(GeoSceneReferences refs)
        {
            try
            {
                if (refs != null)
                {
                    if (refs.SquadBay != null && refs.SquadBay.gameObject.activeSelf)
                        return GeoSceneReferences.ActiveSceneReference.SquadBay;
                    if (refs.VehicleBay != null && refs.VehicleBay.gameObject.activeSelf)
                        return GeoSceneReferences.ActiveSceneReference.VehicleBay;
                    if (refs.Interception != null && refs.Interception.gameObject.activeSelf)
                        return GeoSceneReferences.ActiveSceneReference.Interception;
                }
            }
            catch (Exception) { }
            return GeoSceneReferences.ActiveSceneReference.Geoscape;
        }

        /// <summary>
        /// Every root canvas that is currently drawing, switched off and remembered - plus the SoftMask
        /// components under it (see <see cref="masks"/>, and the 13,128 exceptions that made this
        /// necessary). Disabling a canvas hides it and stops it eating clicks without telling the state
        /// stack anything happened, which is the point: the state is still there, still on top, and
        /// comes back exactly as it was.
        ///
        /// Re-runnable on purpose. RESET VIEW calls it again, because a canvas that came back on since
        /// the open - a load, another mod, a popup the game raised itself - is a canvas drawing over
        /// the model, and "hide whatever is showing NOW" is the recovery. Anything already on the list
        /// is skipped, so the list never gains a duplicate and Close still restores each exactly once.
        /// </summary>
        private static void Hide()
        {
            foreach (Canvas c in UnityEngine.Object.FindObjectsOfType<Canvas>())
            {
                if (c == null || !c.enabled || !c.isRootCanvas || hidden.Contains(c)) continue;
                foreach (Behaviour b in c.GetComponentsInChildren<Behaviour>(true))
                {
                    if (b == null || !b.enabled || masks.Contains(b)) continue;
                    if (b.GetType().Name != "SoftMask") continue;
                    b.enabled = false;
                    masks.Add(b);
                }
                c.enabled = false;
                hidden.Add(c);
            }
        }

        /// <summary>
        /// ============ THE ONE BUTTON THAT ALWAYS GETS THE PICTURE BACK ============
        ///
        /// Not "reframe" - that only re-MEASURED, and re-measuring through a zoom of 8 and a lift the
        /// old code let run to any value at all just re-computes the same empty screen. This puts every
        /// knob the panel owns back to the value a fresh open would have used, re-asserts the scene,
        /// the lighting and the canvases in case something else changed them, re-takes a camera a scene
        /// swap may have handed back, and only then measures. Each step is its own try: a panic button
        /// that can itself fail half way is not a panic button.
        /// </summary>
        private static string ResetView()
        {
            zoom = BenchList.ZoomDefault; lift = 0f; yaw = 0f; pitch = 0f; pan = Vector3.zero;
            // The preview scale is a view knob like the four above, so RESET VIEW puts it back with
            // them. The transform it multiplies is re-asserted by the next Posed, and by Close either
            // way.
            viewScale = 1f; scaleText = "1.00";
            try { if (bay != null && bay.SceneRoot != null) bay.SceneRoot.rotation = sceneRotation; }
            catch (Exception) { }
            try { if (level != null && level.SceneReferences != null)
                      level.SceneReferences.ActivateScene(GeoSceneReferences.ActiveSceneReference.SquadBay); }
            catch (Exception) { }
            try { if (lighting != null && level != null && level.View != null)
                      lighting.SetLighting(level.View.EditSolderLightingSettings, null); }
            catch (Exception) { }
            try { Hide(); } catch (Exception) { }
            // The transport is a knob like the others: RESET VIEW puts the animator's speed back and
            // stands the unit in the weapon's own idle again, then re-binds against the live rig.
            try { FitAnim.Release(); } catch (Exception) { }
            try { if (bay != null && bay.CharacterBuilder != null)
                      FitAnim.Bind(bay.CharacterBuilder, animActions, held, Bodyparts(), ModClips()); }
            catch (Exception) { }
            // The pose re-asserted through the ordinary path, so the preview scale just put back to 1
            // is actually ON SCREEN rather than waiting for the next rebuild.
            try { Posed(); } catch (Exception) { }
            try { TakeCamera(); } catch (Exception) { }
            try { Reframe(); } catch (Exception) { }
            return "ct_bench: view RESET - zoom, lift, orbit, the animation transport and the bay's " +
                   "own rotation back to default, scene and lighting re-asserted, camera re-taken " +
                   "and re-measured." +
                   (framed ? "" : " Still NOT FRAMED: nothing with a renderer is standing there.");
        }

        /// <summary>
        /// The two def lists. GetAllDefs walks the whole repository, which is ALREADY the full runtime
        /// set - a creature a content mod built is a def CreatureBuild.Clone registered into that same
        /// repository, so it is in here beside the shipped soldiers and Pandorans with nothing extra to
        /// do. A unit that cannot be built is filtered out here rather than throwing inside the GUI
        /// loop: a TacCharacterDef with no addons manager has no rig to hang anything on, and one with
        /// no view element cannot even be named.
        ///
        /// TWO deliberate exceptions to that filter, both for the mod author:
        /// - a def a content mod BUILT is listed even when it is missing one of those two parts. Opening
        ///   his own half-built creature and being told WHY is the whole reason he came; silently
        ///   dropping it out of the list looks exactly like the mod not having loaded at all. Show()
        ///   already reports a build that will not finish instead of throwing.
        /// - those defs sort FIRST, like this mod's weapons do, because a repository of several hundred
        ///   shipped templates is where one new creature goes to hide.
        ///
        /// Re-runnable: it is read at open and by the picker's own 'rescan', because a content mod
        /// enabled after the bench was opened is otherwise invisible until the next open.
        /// </summary>
        private static void Catalog()
        {
            DefRepository repo = GameUtl.GameComponent<DefRepository>();
            units = new List<TacCharacterDef>();
            ourUnits = new HashSet<TacCharacterDef>();
            foreach (TacCharacterDef d in repo.GetAllDefs<TacCharacterDef>())
            {
                if (d == null) continue;
                bool ours = Ours(d);
                if (ours) ourUnits.Add(d);
                try { if (ours || (d.GetAddonsMangerDef() != null && d.GetViewElementDef() != null))
                          units.Add(d); }
                catch (Exception) { }
            }
            weapons = new List<WeaponDef>();
            foreach (WeaponDef d in repo.GetAllDefs<WeaponDef>()) if (d != null) weapons.Add(d);
            units.Sort((a, b) =>
            {
                int m = (ourUnits.Contains(b) ? 1 : 0) - (ourUnits.Contains(a) ? 1 : 0);
                return m != 0 ? m : string.CompareOrdinal(a.name, b.name);
            });
            weapons.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        /// <summary>Did a content mod build this character? The engine's own registry, by identity -
        /// CreatureBuild keeps the cloned addons manager on the Creature it built, and that manager def
        /// is what the character def points at. Never a name prefix: two mods may not collide.</summary>
        private static bool Ours(TacCharacterDef d)
        {
            try { return d != null && CreatureBuild.ByManager(d.GetAddonsMangerDef()) != null; }
            catch (Exception) { return false; }
        }

        /// <summary>Every clip the content mod that built the DISPLAYED character shipped, or null for a
        /// vanilla one - what <see cref="FitAnim"/> needs to tell a mod's clip from one the game swapped
        /// in by itself.</summary>
        private static AnimationClip[] ModClips()
        {
            try
            {
                Creature c = unit == null ? null : CreatureBuild.ByManager(unit.GetAddonsMangerDef());
                return c == null ? null : c.Clips;
            }
            catch (Exception) { return null; }
        }

        // ---------------------------------------------------------------- the camera

        /// <summary>
        /// ============ TAKING THE CAMERA, AND WHY IT IS NOT A HINT ============
        ///
        /// The camera hint above asks the CameraDirector for framing that lives in PREFAB DATA, authored
        /// for the game's own equip screen - a different panel in a different place across the whole
        /// window. Against this panel it put the unit off the right edge. No hint can be right here,
        /// because no authored hint knows how wide OUR panel is; the pose has to be computed.
        ///
        /// So control is taken the way the engine itself offers: the camera's pose is written by
        /// <c>CinemachineBrain</c> (CameraManager.cs:112, :308 - the brain lives on
        /// <c>Manager.Camera</c>), and a disabled brain writes nothing. That is one component's
        /// <c>enabled</c> flag, remembered and put back on Close, next to the lighting and the canvases.
        /// It is fetched BY NAME rather than by type on purpose: the Cinemachine assembly is not
        /// referenced by this mod and does not need to be for one bool.
        ///
        /// The pose itself is then re-asserted every LateUpdate (<see cref="Arm.LateUpdate"/>). The
        /// brain is the only thing that SHOULD move the camera, but the geoscape's camera behaviours are
        /// live objects we did not audit one by one, and one write from any of them would put the unit
        /// back off screen with nothing on screen to say why. The original position and rotation are
        /// remembered here and restored verbatim, so the write is as reversible as the flag.
        /// </summary>
        private static void TakeCamera()
        {
            cam = null; framed = false;
            try { if (director != null) cam = director.Camera; } catch (Exception) { }
            if (cam == null) cam = Camera.main;
            if (cam == null) { message = "ct_bench: no camera to frame with."; return; }

            // Already on the ledger? Then it is already standing where WE put it, and recording it a
            // second time would make Close restore our own pose instead of the game's.
            foreach (Held h in cameras) if (h.camera == cam) return;

            Held row = new Held
            {
                camera = cam,
                position = cam.transform.position,
                rotation = cam.transform.rotation,
                near = cam.nearClipPlane
            };
            try
            {
                Behaviour b = cam.GetComponent("CinemachineBrain") as Behaviour;
                if (b != null && b.enabled) { b.enabled = false; row.brain = b; }
            }
            catch (Exception) { }
            // See Held.post: post-processing is what blurs the handles wherever they fall on the
            // background, because they are drawn into the colour target before it runs.
            try
            {
                Behaviour p = cam.GetComponent("PostProcessLayer") as Behaviour;
                if (p != null && p.enabled) { p.enabled = false; row.post = p; }
            }
            catch (Exception) { }
            try { if (cam.nearClipPlane > BenchList.NearClip) cam.nearClipPlane = BenchList.NearClip; }
            catch (Exception) { }
            cameras.Add(row);
        }

        /// <summary>Every camera on the ledger put back the way it was found - brain first, then the
        /// pose - and each row dropped only once it has actually been restored, so a failure leaves
        /// something for the retry to find. A camera Unity has since destroyed is nothing to put back
        /// and its row simply goes.</summary>
        private static void ReleaseCamera(List<string> failed)
        {
            List<Held> left = new List<Held>();
            foreach (Held h in cameras)
            {
                bool ok = true;
                try { if (h.brain != null) h.brain.enabled = true; }
                catch (Exception) { ok = false; }
                try { if (h.post != null) h.post.enabled = true; }
                catch (Exception) { ok = false; }
                try { if (h.camera != null) { h.camera.transform.position = h.position;
                                              h.camera.transform.rotation = h.rotation;
                                              h.camera.nearClipPlane = h.near; } }
                catch (Exception) { ok = false; }
                if (ok) continue;
                left.Add(h);
                if (failed != null)
                    failed.Add("camera '" + (h.camera == null ? "?" : h.camera.name) + "' (pose/brain/post-processing/near plane)");
            }
            cameras.Clear();
            cameras.AddRange(left);
            cam = null; framed = false;
        }

        /// <summary>The rotation this camera had BEFORE the workbench took it, off the ledger - never
        /// its live rotation, which is our own computed pose and would drift a little further every
        /// frame if it were fed back in as the "authored" direction.</summary>
        private static Quaternion Original(Camera c)
        {
            foreach (Held h in cameras) if (h.camera == c) return h.rotation;
            return c == null ? Quaternion.identity : c.transform.rotation;
        }

        /// <summary>
        /// Measure what is actually standing there, then stand the camera where the whole of it fits the
        /// part of the screen the panel does NOT cover. Re-run after every rebuild, because a new unit
        /// is a new size - a Crabman is not a soldier and a vehicle is neither.
        ///
        /// The direction is the bay's own authored one (<c>CameraLookFrom</c>, the anchor the game hands
        /// the director for this very scene), so the unit is seen from the angle an artist chose; only
        /// the DISTANCE and the sideways offset are ours, because only those depend on our panel.
        /// </summary>
        private static void Reframe()
        {
            if (!open || cam == null || bay == null || bay.CharacterBuilder == null) { framed = false; return; }
            try
            {
                Bounds b = new Bounds(); bool any = false;
                foreach (Renderer r in bay.CharacterBuilder.GetComponentsInChildren<Renderer>())
                {
                    // Particles are muzzle flashes and dust: they are huge, they move, and they are not
                    // the thing being looked at.
                    if (r == null || !r.enabled || r is ParticleSystemRenderer) continue;
                    if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
                }
                if (!any) { framed = false; return; }

                frameRadius = b.extents.magnitude;
                float distance, lateral, vertical;
                // The transport strip eats the bottom of the free region exactly as the panel eats its
                // left, and BOTH come out of BenchList's own constants - a second idea of how tall the
                // strip is would be a unit standing behind it with nothing on screen to say why.
                BenchList.Frame(frameRadius, cam.fieldOfView, Screen.width, Screen.height,
                                PanelWidth,
                                BenchList.StripReserve(Screen.width, Screen.height, PanelWidth),
                                zoom, out distance, out lateral, out vertical);

                // THE ORBIT. The bay's authored look direction is still the starting point - the angle
                // an artist chose for this very scene - and the mouse adds a yaw about world up and a
                // pitch about the camera's own right ON TOP of it. Because the pose below is built as
                // "aim point, minus forward times distance", turning the rotation IS orbiting the
                // camera around the unit: the aim point never moves, so the model stays the centre.
                Quaternion look = bay.CameraLookFrom != null ? bay.CameraLookFrom.rotation : Original(cam);
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f) * look * Quaternion.Euler(pitch, 0f, 0f);
                Vector3 fwd = rot * Vector3.forward, right = rot * Vector3.right, up = rot * Vector3.up;
                // lift is in RADII, not metres - the same press has to mean the same thing on a soldier
                // and on something three times his size. The pan is already in metres and in world
                // space, and it moves the AIM POINT: the camera follows it because the pose below is
                // built off the aim, so the pivot travels with the camera instead of staying buried in
                // the model. See <see cref="pan"/>.
                pan = Vector3.ClampMagnitude(pan, frameRadius * BenchList.PanMaxRadii);
                Vector3 aim = b.center + up * (lift * frameRadius) + pan;
                frameDist = distance;
                // ... and DOWN by the strip's share, for the same reason the camera stands LEFT by the
                // panel's: moving the camera down moves the image up, clear of the transport.
                framePos = aim - fwd * distance - right * lateral - up * vertical;
                frameRot = rot;
                framed = true;
            }
            catch (Exception ex) { framed = false; message = "ct_bench: frame - " + ex.GetType().Name + ": " + ex.Message; }
        }

        /// <summary>RECENTRE: the pan back to zero and nothing else. Deliberately NOT RESET VIEW - the
        /// zoom, the lift and the orbit a session has been dialled in are worth keeping when all that
        /// went wrong is that the camera was flown somewhere the model is not.</summary>
        private static void Recentre()
        {
            pan = Vector3.zero;
            Reframe();
        }

        /// <summary>One middle-drag, in PIXELS, turned into a world offset in the camera's own screen
        /// plane: a pixel is worth <c>2 * distance * tan(fov/2) / screenHeight</c> metres at the aim
        /// point's depth, which is the same algebra <see cref="BenchList.Frame"/> is built on. The sign
        /// is grab-the-world - drag right and the model goes right, because the camera goes left.</summary>
        private static void PanBy(float dxPixels, float dyPixels)
        {
            if (cam == null || !framed) return;
            float mpp = 2f * Mathf.Max(0.01f, frameDist) *
                        Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) /
                        Mathf.Max(1f, Screen.height);
            pan -= (frameRot * Vector3.right) * (dxPixels * mpp) +
                   (frameRot * Vector3.up) * (dyPixels * mpp);
            Reframe();
        }

        /// <summary>
        /// WASD/QE fly, read raw from <c>UnityEngine.Input</c> like every other gesture here - the game
        /// itself cannot hear any of it, because the bench holds
        /// <c>InputController.IncDisableHandlersCalling</c> for as long as it is open (see
        /// <see cref="input"/>).
        ///
        /// The one thing it MUST stand aside for is IMGUI's own keyboard: the panel has two text
        /// filters, and typing "assault" into one of them would otherwise fly the camera on every 'a'
        /// and 's'. <c>GUIUtility.keyboardControl</c> is non-zero exactly while a control has the
        /// keyboard, which is the cheapest true answer to "is he typing".
        /// </summary>
        private static void Fly()
        {
            if (cam == null || !framed) return;
            // BY NAME, not by "is anything focused". IMGUI keeps keyboardControl on the text field long
            // after the pointer has left it - clicking the scene does not clear it - so a blanket
            // "keyboardControl != 0" guard switched flying off for the rest of the session the first
            // time a filter was typed in. Only the two filters are allowed to eat these keys, and a
            // press on the scene drops their focus (see dropFocus).
            if (typing) return;
            float strafe = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            float rise = (Input.GetKey(KeyCode.E) ? 1f : 0f) - (Input.GetKey(KeyCode.Q) ? 1f : 0f);
            float fwd = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            if (strafe == 0f && rise == 0f && fwd == 0f) return;
            float metres = Mathf.Max(0.05f, frameRadius) * BenchList.FlyPerSecond * Time.deltaTime *
                           (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift) ? 4f : 1f);
            pan += (frameRot * new Vector3(strafe, rise, fwd)) * metres;
            Reframe();
        }

        // ---------------------------------------------------------------- leave

        /// <summary>One restoration step, named. Its name is what the user is told when it fails, so
        /// it says WHAT did not come back, not that "something" did not.</summary>
        private static void Step(List<string> failed, string what, Action undo)
        {
            try { undo(); }
            catch (Exception ex) { failed.Add(what + " (" + ex.GetType().Name + ": " + ex.Message + ")"); }
        }

        /// <summary>
        /// ============ CLOSE DOES NOT GET TO SAY IT WORKED UNTIL IT HAS ============
        ///
        /// Everything Open did, undone in reverse, EACH IN ITS OWN STEP - one failure must never abort
        /// the steps after it, or a throw restoring the camera would keep every canvas hidden.
        ///
        /// The part that was wrong until now is the BOOKKEEPING, not the order. It cleared
        /// <see cref="entered"/> on the first line and then swallowed every failure below, so one bad
        /// restore left the game altered, the panel gone, and 'ct_bench close' answering "not open" -
        /// the exact shape of "I pressed something and I cannot get it back" this flag exists to
        /// prevent. Now every step runs, the failures are COLLECTED AND NAMED, and <c>entered</c> is
        /// cleared only when there is genuinely nothing left to put back. Anything that failed stays on
        /// its list, the level references are kept, and pressing close again retries exactly those.
        /// </summary>
        private static string Close()
        {
            if (!entered) { open = false; return "ct_bench: nothing to close - nothing was changed."; }
            open = false;
            List<string> failed = new List<string>();

            // The masks BEFORE the canvases, so nothing is ever drawn with a mask still switched off.
            // Each list keeps whatever failed, so a retry retries only that.
            masks.RemoveAll(b => Restored(failed, "SoftMask on '" + Named(b) + "'",
                                           () => { if (b != null) b.enabled = true; }));
            hidden.RemoveAll(c => Restored(failed, "canvas '" + Named(c) + "'",
                                           () => { if (c != null) c.enabled = true; }));

            // The game's ears back on FIRST: everything below it is a scene object that may or may not
            // still exist, and being left deaf is the one failure the user cannot see or work around.
            Step(failed, "the game's own input handling", ResumeInput);
            // The animator BEFORE the rebuild callback goes: it puts the speed back and plays the
            // default state, and both need the builder this callback still points at.
            Step(failed, "the animator's speed and the weapon's idle pose", FitAnim.Release);
            Step(failed, "the rebuild callback", () => {
                if (bay != null && bay.CharacterBuilder != null)
                    bay.CharacterBuilder.OnCharacterRebuilded -= Posed; });
            // Hole 1's other half: an Open that threw before the first rebuild left the addons manager
            // quiesced. Un-quiescing costs nothing when it is already on, and a manager stuck with
            // autorefresh off silently stops re-resolving skins for the rest of the session.
            Step(failed, "the addons manager's autorefresh", () => {
                if (bay != null && bay.CharacterBuilder != null &&
                    bay.CharacterBuilder.AddonsManager != null)
                    bay.CharacterBuilder.AddonsManager.SetAutorefreshOnTagsChanged(true); });
            ReleaseCamera(failed);
            Step(failed, "the camera director hint", () => {
                if (director != null) director.RemoveHint(CameraDirectorHint.GeoscapeSoldierEditCenter); });
            // THE LIGHTING THAT WAS THERE, off the snapshot - not DefaultLightingSettings, which is
            // simply one more state the game may or may not have been in.
            Step(failed, "the lighting settings", () => {
                if (lighting != null && lightingTaken) { lighting.SetLighting(priorLighting, null);
                                                         lightingTaken = false; } });
            Step(failed, "the squad bay's pose", () => {
                if (bay != null && bay.SceneRoot != null)
                {
                    bay.SceneRoot.rotation = sceneRotation;
                    bay.SceneRoot.localPosition = scenePosition;
                    bay.SceneRoot.localScale = sceneScale;
                } });
            Step(failed, "the character platform's scale", () => {
                if (bay != null && bay.CharBuilderPlatform != null)
                    bay.CharBuilderPlatform.localScale = platformScale; });
            // ... and the scene that was switched on, likewise off the snapshot.
            Step(failed, "the active scene", () => {
                if (level != null && level.SceneReferences != null)
                    level.SceneReferences.ActivateScene(priorScene); });

            if (failed.Count > 0)
                return "ct_bench NOT FULLY CLOSED - the panel is gone but " + failed.Count +
                       " thing(s) could NOT be put back: " + string.Join("; ", failed.ToArray()) +
                       ". Nothing was forgotten: run 'ct_bench close' again (or press " + HotkeyLabel +
                       " twice) and it retries exactly those.";

            entered = false;
            level = null; bay = null; director = null; lighting = null; standingIn = null;
            priorLighting = null;
            return "ct_bench closed - the screen you came from was never left, so it is still there.";
        }

        /// <summary>True when the step SUCCEEDED, which is what makes it removable from its list.</summary>
        private static bool Restored(List<string> failed, string what, Action undo)
        {
            int before = failed.Count;
            Step(failed, what, undo);
            return failed.Count == before;
        }

        private static string Named(UnityEngine.Object o)
        {
            try { return o == null ? "?" : o.name; } catch (Exception) { return "?"; }
        }

        /// <summary>Is the level the workbench opened INTO still the current one, and still playing?
        /// Anything else - a quickload, a mission launch, a return to the menu - means the bay, the
        /// builder and every canvas in the hidden list are being destroyed, and the only correct move
        /// is to let go. Unity's fake-null makes a destroyed Level compare equal to null, so this one
        /// expression covers "gone" and "going" alike.</summary>
        private static bool StillThere()
        {
            if (standingIn == null || !standingIn.IsPlaying) return false;
            if (GameUtl.CurrentLevel() != standingIn) return false;
            return level != null && bay != null && bay.CharacterBuilder != null;
        }

        // ---------------------------------------------------------------- the unit and its weapon

        /// <summary>Picking a unit is TWO answers, not one: who stands on the platform, and what may be
        /// put in his hands. Doing them in this order matters - Offer may drop the weapon the previous
        /// unit was holding, and Show must build the body without it rather than with a corpse.</summary>
        private static void Pick()
        {
            Offer();
            Show();
        }

        /// <summary>
        /// The selected unit, wearing its template's own bodyparts and holding the selected weapon.
        /// The bodyparts are the template's, not an empty list: an alien IS its bodyparts, and a
        /// soldier with none is a rig with nothing on it.
        ///
        /// ============ WHY THIS IS FIVE STEPS AND NOT TWO ============
        /// The first version called DisplayCharacter then RebuildCharacter and stopped, which LOOKS
        /// like the whole of the native path and is not. UIModuleActorCycle.DisplaySoldier:612-615 -
        /// the screen the game itself shows a soldier with - quiesces the addon manager and then
        /// RE-TAGS IT with the new character's game tags before rebuilding, because the manager's
        /// GameTags are what pick the skin variant each addon resolves to (FilteredSkinDataDef and
        /// friends). Every human template shares one rig, so UseAddonManager:145 returns false, the
        /// manager SURVIVES the switch - and with it the previous character's tags. The rebuild then
        /// honestly re-attached the addons and honestly resolved them to the variants those stale tags
        /// selected: a new unit that renders as the old one. Nothing threw and nothing was silent about
        /// it either, which is the worst shape a bug can take.
        ///
        /// The animation resetting on every click was the same story told from the other end: the
        /// squad bay's UIModuleActorCycle is STILL subscribed to OnCharacterRebuilded (:248-249, never
        /// unsubscribed while the module lives) and its handler resets the animation (:453-456). That
        /// reset is proof the rebuild ran to completion - so the missing step was never the rebuild,
        /// it was the tags the rebuild was rebuilding AGAINST.
        /// </summary>
        private static void Show()
        {
            if (unit == null || bay == null || bay.CharacterBuilder == null) return;
            // Hoisted out of the try so the finally can put autorefresh back. It is switched OFF three
            // lines into the mutation and back ON by Posed, on the rebuild callback - so ANY throw
            // between those two points (a malformed def, a rig that will not build) used to leave the
            // manager quiesced for the rest of the session, silently: no exception, no message, just
            // addons that stop re-resolving their skins when tags change.
            AddonsManager manager = null;
            try
            {
                UnitDisplayData data = new UnitDisplayData(unit, GameUtl.GameComponent<SharedData>());
                bool rigChanged;
                // THE RETURN VALUE IS NOT DECORATION. DisplayCharacter hands back the rig's
                // TacActorAnimActions, and UIModuleActorCycle keeps it (:612) for exactly one reason:
                // after the rebuild it is what swaps the IDLE CLIPS for the weapon's hand count (:435-451).
                // Throwing it away is why the soldier held every gun in his empty-handed idle - the fit
                // was being judged against a pose no armed soldier ever stands in.
                animActions = CommonCharacterUtils.DisplayCharacter(bay.CharacterBuilder, data, out rigChanged);

                // DisplaySoldier:613-615, verbatim. Autorefresh off across the mutation so a half-built
                // character is never refreshed against half-changed tags; Posed turns it back on where
                // the native path does, in OnCharacterRebuilded:437.
                manager = bay.CharacterBuilder.AddonsManager;
                if (manager != null)
                {
                    manager.SetAutorefreshOnTagsChanged(false);
                    manager.GameTags.Clear();
                    if (data.GameTags != null) manager.GameTags.AddRange(data.GameTags);
                }

                List<ItemDef> armour = Bodyparts();
                CommonCharacterUtils.RebuildCharacter(bay.CharacterBuilder, armour, weapon);
                message = "ct_bench: " + unit.name + " - " + armour.Count + " bodypart(s)" +
                          (rigChanged ? ", NEW rig" : ", same rig") +
                          (weapon == null ? ", empty handed" : ", holding " + weapon.name) + ".";
            }
            catch (Exception ex)
            {
                // ============ THE QUIESCE HAS TO BE UNDONE HERE, AND *ONLY* HERE ============
                // A throw between the SetAutorefreshOnTagsChanged(false) above and the rebuild's own
                // callback used to leave the manager quiesced for the rest of the session - no
                // exception after this one, no message, just addons that silently stop re-resolving
                // their skins whenever tags change.
                //
                // NOT in a finally, and that is deliberate. CommonCharacterUtils.RebuildCharacter:63
                // calls StartRebuildCharacter, which runs the rebuild as a COROUTINE
                // (AddonsCharacterBuilder.cs:100/109, Timing.Start) - so on the SUCCESSFUL path this
                // method returns with the rebuild not yet started and Posed still to come. A finally
                // would switch autorefresh back on inside the very window the game turns it off for,
                // which is the bug it was meant to prevent, told backwards. Close has its own
                // unconditional un-quiesce for the other end: a builder that dies before its callback.
                try { if (manager != null) manager.SetAutorefreshOnTagsChanged(true); }
                catch (Exception) { }

                // ============ A FAILED SHOW MUST NOT LEAVE THE TRANSPORT ON THE OLD UNIT ============
                // DisplayCharacter destroys the previous rig and instantiates the new one BEFORE
                // anything below it can throw (AddonsCharacterBuilder.UseAddonManager:153-158). So on
                // the way out of here the model on screen is ALREADY the new one, while the strip is
                // still bound to the old one's Animator and still lists the old one's clips - which is
                // how a humanoid came to be shown with a spider's clip names, none of which would play,
                // because the Animator they were bound to had just been destroyed. Re-bind against the
                // rig that is really standing there: with no anim actions to catalogue the strip says
                // so, which is the honest answer while the build is broken.
                animActions = null; held = null;
                try { FitAnim.Bind(bay.CharacterBuilder, null, null, null, null); }
                catch (Exception) { }

                message = "ct_bench: could not build '" + unit.name + "' " +
                          (weapon == null ? "" : "holding '" + weapon.name + "' ") +
                          "- " + ex.GetType().Name + ": " + ex.Message;
                // The MESSAGE is what fits the panel; the LOG gets the whole exception. A bare message
                // named the type and nothing else, and "NullReferenceException" with no frame is not a
                // diagnosis - it cost a whole session of reading the engine backwards to guess where it
                // came from. ToString() carries the stack, and the log is where a stack belongs.
                ContentToolMain.Say(message + "\n" + ex);
            }
        }

        /// <summary>
        /// The last word on the bay's pose, run when a rebuild finishes. It exists for two reasons and
        /// both are the game's own code: the manager has to be un-quiesced where
        /// UIModuleActorCycle.OnCharacterRebuilded:437 un-quiesces it, and the SCENE ROOT carries the
        /// per-character position and scale (:465-467) - which is how one platform shows both a soldier
        /// and something three times his size. Without this the bay keeps whatever pose the screen the
        /// user came from left behind, and a big creature is drawn at soldier scale.
        ///
        /// Ours is subscribed at Open, so it runs AFTER the squad bay module's own stale handler and
        /// overwrites it. A def whose params were never authored (the fields default to
        /// Vector3.negativeInfinity) is left alone rather than sending the bay to infinity - the native
        /// path has a DefaultBuilderViewParams asset to fall back on and a mod does not.
        /// </summary>
        private static void Posed()
        {
            try
            {
                if (!open || unit == null || bay == null || bay.CharacterBuilder == null) return;
                AddonsManager manager = bay.CharacterBuilder.AddonsManager;
                if (manager != null) manager.SetAutorefreshOnTagsChanged(true);
                Handed(manager);
                // AFTER Handed, and with what Handed found: the clip set the transport lists is the one
                // SetActiveNumberOfHands has just re-resolved for this weapon, so a rifle gets the rifle
                // idle and an empty hand gets the empty-handed one. Bind keeps the selection when the
                // same clip is still there - a rebuild is what a nudge causes, and being thrown back to
                // frame 0 after every nudge is what a fit cannot be judged through.
                FitAnim.Bind(bay.CharacterBuilder, animActions, held, Bodyparts(), ModClips());

                ViewElementDef view = unit.GetViewElementDef();
                CharacterBuilderViewParametersDef p = view == null ? null : view.BuilderViewParamDef;
                // The scale the GAME chose for this character, or - for a def that authored none - the
                // one the bay was standing at when the bench opened. Taken FRESH every pose rather than
                // read back off the transform, so the preview multiplier below can never compound.
                Vector3 posed = sceneScale;
                if (p != null && !Unset(p.ObjectWorldPosition) && !Unset(p.ObjectScale))
                {
                    posed = p.ObjectScale;
                    if (bay.SceneRoot != null) bay.SceneRoot.localPosition = p.ObjectWorldPosition;
                    if (bay.CharBuilderPlatform != null && !Unset(p.PlatformScale))
                        bay.CharBuilderPlatform.localScale = p.PlatformScale;
                }
                // ... and then the PREVIEW knob, last. See <see cref="viewScale"/>: this is a look, not
                // a value - Close restores localScale from the same snapshot whatever it is left at.
                if (bay.SceneRoot != null) bay.SceneRoot.localScale = posed * viewScale;
                // LAST, and only here: the bounds we frame against are the bounds AFTER the scene root
                // has been posed and scaled, and a unit three times a soldier's size is only three times
                // his size once the two lines above have run.
                Reframe();
            }
            catch (Exception ex) { message = "ct_bench: pose - " + ex.GetType().Name + ": " + ex.Message; }
        }

        /// <summary>
        /// ============ THE IDLE THE WEAPON ASKS FOR ============
        ///
        /// UIModuleActorCycle.OnCharacterRebuilded:435-451, mirrored: find the just-rebuilt addon that
        /// IS the equipped weapon, and hand it to <c>TacActorAnimActions.SetActiveNumberOfHands</c>
        /// (TacActorAnimActions.cs:66-86), which re-resolves the idle and navigation clips against the
        /// weapon's hand count and applies them as animator overrides. Null when nothing is held - the
        /// native path passes null too, and that is what puts the empty-handed idle back.
        ///
        /// It runs on EVERY rebuild, not only when the unit changes, because the weapon changes far
        /// more often than the unit does and the pose is the whole reason to look.
        /// </summary>
        private static void Handed(AddonsManager manager)
        {
            held = null;
            if (animActions == null) return;
            try
            {
                if (weapon != null && manager != null && manager.RootAddon != null)
                    foreach (Addon a in manager.RootAddon)
                        if (a != null && a.AddonDef == weapon) { held = a as Equipment; break; }
                animActions.SetActiveNumberOfHands(held);
            }
            catch (Exception ex) { message = "ct_bench: idle - " + ex.GetType().Name + ": " + ex.Message; }
        }

        /// <summary>
        /// ============ THE MESH THE DRAG HANDLES HANG ON ============
        ///
        /// The SAME matching seam <see cref="Handed"/> uses two methods up - walk the rebuilt addons,
        /// find the one that IS the selected weapon - and then its <c>VisualRoot</c>'s mesh child. That
        /// child is what carries the fit: WeaponBuild writes its node's localPosition/rotation/scale and
        /// <c>Follow</c> copies exactly those three onto every live instance of the same shared mesh,
        /// this one included. So its parent's frame IS the frame the manifest's "offset" is written in,
        /// which is the whole reason the gizmo can be dragged in world space and saved in local.
        ///
        /// The ROOT is skipped deliberately: the root is left at identity by the fit (see FitNode) and
        /// is the frame, not the thing that moves.
        /// </summary>
        private static Transform LiveMesh()
        {
            if (weapon == null || bay == null || bay.CharacterBuilder == null) return null;
            try
            {
                AddonsManager manager = bay.CharacterBuilder.AddonsManager;
                if (manager == null || manager.RootAddon == null) return null;
                foreach (Addon a in manager.RootAddon)
                {
                    if (a == null || a.AddonDef != weapon) continue;
                    Transform root = a.VisualRoot;
                    if (root == null) continue;
                    foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>())
                        if (mf != null && mf.sharedMesh != null && mf.transform != root)
                            return mf.transform;
                }
            }
            catch (Exception) { }
            return null;
        }

        private static bool Unset(Vector3 v)
        {
            return float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z) ||
                   float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z);
        }

        /// <summary>The template's own bodyparts, never throwing: this feeds both the rebuild and the
        /// slot test, and a GUI loop is the wrong place to discover a malformed def.</summary>
        private static List<ItemDef> Bodyparts()
        {
            List<ItemDef> parts = new List<ItemDef>();
            if (unit == null) return parts;
            try { foreach (TacticalItemDef p in unit.GetTemplateBodyparts()) if (p != null) parts.Add(p); }
            catch (Exception) { }
            return parts;
        }

        // ---------------------------------------------------------------- what this unit may be offered

        /// <summary>
        /// ============ A MUTOID IS NOT OFFERED A RIFLE ============
        ///
        /// The offer list, rebuilt every time the unit changes. The test is NOT a heuristic of ours -
        /// it is <see cref="CommonCharacterUtils.CanSwapItem"/>, the same public call the equip screen
        /// uses to decide whether an item may be added: with a null InventoryComponentDef it takes the
        /// SLOT branch (CommonCharacterUtils.cs:152-189), walks the slots the unit's chassis and its own
        /// bodyparts provide, and answers null when the item's RequiredSlotBinds match none of them.
        /// A four-legged creature provides no hand, so no rifle survives - which is the whole point, and
        /// it falls out of the game's own data rather than a list of ours that would rot.
        ///
        /// The null geoCharacter is safe and deliberate: the parameter is dereferenced only inside the
        /// inventory branch (:137, :144), which a null inventoryDef never enters. That is also the right
        /// question for a workbench - "is there anywhere on this body to put it", not "does this
        /// particular recruit's inventory allow it".
        /// </summary>
        private static void Offer()
        {
            AddonsManagerDef manager = null;
            try { manager = unit == null ? null : unit.GetAddonsMangerDef(); } catch (Exception) { }
            List<ItemDef> worn = Bodyparts();

            // No manager def is "we could not ask", not "nothing fits" - BenchList.Offer keeps the whole
            // catalogue for a null test rather than showing an empty panel that looks like an answer.
            Func<WeaponDef, bool> test = offerAll || manager == null
                                       ? (Func<WeaponDef, bool>)null
                                       : w => Fits(manager, worn, w);
            offered = BenchList.Offer(weapons, test, Mine, ref weapon, out refused);
            // The RE-EQUIP memory is per-unit, and this is where the unit's answer is computed: a gun
            // the previous soldier could hold may have nowhere to go on this one, and putting it back
            // would rebuild the new unit with a weapon the slot test has just refused. Against the
            // PREDICATE, not against 'offered' - BenchList.Offer keeps every weapon this mod built in
            // the list even when the test refuses it, so a custom gun would survive a membership check.
            // A null test is "we could not ask" or "the user asked for all", and neither refuses anything.
            if (lastWeapon != null && test != null && !test(lastWeapon)) lastWeapon = null;
            mine = 0;
            foreach (WeaponDef w in weapons) if (Mine(w)) mine++;

            if (offered.Count == 0 && unit != null)
                message = "ct_bench: '" + unit.name + "' has nowhere to put ANY of the " + weapons.Count +
                          " weapon(s) - the game's own slot test (CommonCharacterUtils.CanSwapItem) " +
                          "admitted none of them. Tick 'all' to list them anyway.";
            else if (mine == 0)
                message = "ct_bench: NONE of the " + weapons.Count + " weapons in the repository was " +
                          "built by this mod (no def carries ResourcePath '" + BenchList.ResourceRoot +
                          "*'). Enable a content mod that declares weapons, or run 'ct_project <mod>', " +
                          "and re-open - the catalogue is read once per open.";
            else if (refused > 0)
                message = "ct_bench: " + refused + " of this mod's " + mine + " weapon(s) do NOT fit '" +
                          (unit == null ? "-" : unit.name) + "' by the game's own slot test - they are " +
                          "still listed first so they can be looked at, but the hand will be empty.";
        }

        /// <summary>Did this mod build it? See <see cref="BenchList.IsMine"/> - the def's own
        /// ResourcePath, which is true from the moment WeaponBuild creates it, unlike a live fit.</summary>
        private static bool Mine(WeaponDef d)
        {
            return d != null && BenchList.IsMine(d.ResourcePath);
        }

        private static bool Fits(AddonsManagerDef manager, List<ItemDef> worn, ItemDef item)
        {
            if (item == null) return false;
            try { return CommonCharacterUtils.CanSwapItem(manager, item, worn, null, null) != null; }
            catch (Exception) { return false; }
        }

        // ---------------------------------------------------------------- the panel

        /// <summary>The ONE number that says how much of the screen the panel eats - it is the panel's
        /// width, the mouse-input boundary AND the input to the framing. It lives in
        /// <see cref="BenchList.PanelWidth"/> so the offline gate can measure whether a row of buttons
        /// actually fits in it; this is the alias the drawing code reads.</summary>
        private const float PanelWidth = BenchList.PanelWidth;

        /// <summary>The whole panel scrolls as one. The lists scroll INSIDE it, but at a small enough
        /// window even the fixed part does not fit, and a control that is off the bottom of a dev panel
        /// may as well not exist - that was half of defect 2.</summary>
        private static Vector2 panelScroll;
        /// <summary>The two filter fields' IMGUI control names - the only two controls in the bench that
        /// are allowed to eat the fly keys. See <see cref="Fly"/>.</summary>
        private const string UnitFilterName = "ct_bench_unit_filter";
        private const string WeaponFilterName = "ct_bench_weapon_filter";
        /// <summary>The model-scale field. Listed with the two filters for one reason only: it eats the
        /// keyboard too, and a digit typed into it must not also fly the camera.</summary>
        private const string ScaleFieldName = "ct_bench_model_scale";
        /// <summary>A press landed on the 3D half, so whichever filter still holds the keyboard should
        /// let go of it. Acted on in OnGUI, because that is where IMGUI's focus lives.</summary>
        private static bool dropFocus;
        /// <summary>Does one of the two filters hold the keyboard? Read in OnGUI and CACHED here,
        /// because <see cref="Fly"/> runs from Update and the IMGUI focus API may only be called inside
        /// OnGUI - out of context it throws, the Update guard swallows it, and flying never runs.</summary>
        private static bool typing;
        /// <summary>The two def lists are pickers: used once, then in the way. Each folds itself shut
        /// the moment something is picked from it, and its header re-opens it.</summary>
        private static bool unitsOpen = true, weaponsOpen = true;

        /// <summary>
        /// ============ THE ORDER IS THE FIX ============
        ///
        /// Once a weapon is picked, the DIAL BLOCK is where the whole session is spent - nudge, look at
        /// the model, nudge again - and it used to be drawn last, underneath a 130 px unit list and a
        /// 180 px weapon list, so on a 803 px window it fell off the bottom of the screen. Nothing was
        /// broken; it was simply unreachable, which for a button is the same thing.
        ///
        /// So the panel is now ordered by how often a thing is touched: escape hatch, what is selected,
        /// THE PICKERS, the view, THE DIAL, the answer line. <see cref="BenchList.DialReachable"/>
        /// asserts offline that everything down to the answer line fits above the fold, and the outer
        /// scroll view is the backstop for a window smaller than that.
        ///
        /// ============ AND THE PICKERS ARE NOT LAST ============
        /// They used to be, underneath the dial, and that is the defect the author hit: "one model, the
        /// same guy all the time". <see cref="BenchList.Rows"/> budgets the panel by COUNTING ROWS, and
        /// three of those rows are Labels that WRAP - the view readout, the dial's SAVE caption and the
        /// answer block. Measured in a 1302x776 window: everything above the pickers really occupies
        /// 671 px where the budget says 444, so the two lists were pushed clean off the bottom with
        /// panelScroll still at zero. The panel looked complete and the unit list was simply not on
        /// screen. Counting rows can never predict wrapped text, so the fix is not better arithmetic:
        /// the pickers are drawn BEFORE the blocks that wrap, where nothing above them can grow.
        /// Closed - which is how they sit the moment something has been picked - they cost two rows,
        /// so the dial block is exactly where S29 left it.
        /// </summary>
        private static void Draw()
        {
            // LEFT edge. GeoscapeSoldierEditCenter stands the unit right of screen centre, so a panel
            // on the right sat on top of him; the character is the instrument here, not the buttons.
            float w = PanelWidth, x = 0f;
            if (backdrop == null)
            {
                backdrop = new Texture2D(1, 1);
                backdrop.SetPixel(0, 0, new Color(0.04f, 0.05f, 0.07f, 0.94f));
                backdrop.Apply();
            }
            GUI.DrawTexture(new Rect(x, 0f, w, Screen.height), backdrop);

            float viewportH = Screen.height - 2f * BenchList.PanelInset;
            GUILayout.BeginArea(new Rect(x + BenchList.PanelInset, BenchList.PanelInset,
                                         BenchList.ContentWidth(w), viewportH));
            panelScroll = GUILayout.BeginScrollView(panelScroll);

            GUILayout.BeginHorizontal();
            // The close is deferred to AFTER EndArea on purpose: returning out of the middle of a
            // GUILayout block leaves the layout stack unbalanced, and IMGUI answers that with an
            // exception every frame - i.e. exactly the wedged screen this button exists to escape.
            bool leaving = GUILayout.Button("CLOSE (" + HotkeyLabel + ")", GUILayout.Width(150f));
            bool resetting = GUILayout.Button("RESET VIEW", GUILayout.Width(110f));
            GUILayout.EndHorizontal();

            string fitKey = weapon == null ? null : BenchList.KeyFor(weapon.name, WeaponBuild.Fitted());
            GUILayout.Label("unit:   " + BenchList.Elide(unit == null ? "-" : unit.name, BenchList.NameChars));
            GUILayout.Label("weapon: " + BenchList.Elide(weapon == null ? "-" : weapon.name, BenchList.NameChars) +
                            (weapon == null ? "" : fitKey != null ? "  [tunable]" : "  [vanilla]"));

            // THE PICKERS COME BEFORE THE WRAPPING BLOCKS. See the note on Draw: closed they are two
            // rows, so the dial stays where S29 put it; open they push the dial down, which is right,
            // because while a unit is being chosen the dial is not what is being looked at.
            float unitH, weaponH;
            BenchList.Rows(viewportH, fitKey != null, unitsOpen, weaponsOpen, out unitH, out weaponH);
            Units(unitH);
            Weapons(fitKey, weaponH);

            View();
            Dial(fitKey);
            Message();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
            if (leaving) message = Close();
            else if (resetting) message = ResetView();
        }

        /// <summary>
        /// The framing knobs, and the note that says the mouse does all of this better. The algebra
        /// guarantees the unit is INSIDE the free region; it cannot know how much air around him reads
        /// as well framed, and that is a judgement made by looking - so zoom, lift and the orbit are
        /// nudged live and remembered for the session.
        ///
        /// 'reframe' re-MEASURES at the current knobs (the answer a rebuild that finished after a stall
        /// would otherwise have missed). RESET VIEW, up in the header, is the different and stronger
        /// thing: it puts the knobs THEMSELVES back first.
        /// </summary>
        private static void View()
        {
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("view", GUILayout.Width(40f));
            if (GUILayout.Button("in"))      { zoom = BenchList.Wheel(zoom, 1f); Reframe(); }
            if (GUILayout.Button("out"))     { zoom = BenchList.Wheel(zoom, -1f); Reframe(); }
            if (GUILayout.Button("up"))      { lift = BenchList.Clamp(lift - BenchList.LiftStep,
                                                                     BenchList.LiftMin, BenchList.LiftMax); Reframe(); }
            if (GUILayout.Button("down"))    { lift = BenchList.Clamp(lift + BenchList.LiftStep,
                                                                     BenchList.LiftMin, BenchList.LiftMax); Reframe(); }
            if (GUILayout.Button("reframe")) { Reframe(); }
            GUILayout.EndHorizontal();
            // Which drag direction reads as "natural" is a matter of the hand, not of the code, so it
            // is a toggle here rather than a sign the author has to come back and change. Session only.
            GUILayout.BeginHorizontal();
            GUILayout.Label("drag", GUILayout.Width(40f));
            BenchList.InvertX = GUILayout.Toggle(BenchList.InvertX, " invert X", GUILayout.Width(78f));
            BenchList.InvertY = GUILayout.Toggle(BenchList.InvertY, " invert Y", GUILayout.Width(78f));
            // The one button that gets the model back after a fly-about, and it is NOT RESET VIEW: it
            // drops the pan only, so the zoom, the lift and the orbit that were dialled in survive.
            if (GUILayout.Button("RECENTRE", GUILayout.Width(96f))) Recentre();
            GUILayout.EndHorizontal();

            // ---- THE MODEL SCALE, and it is a PREVIEW knob ----
            // A foreign model is either the right size next to a soldier or it is not, and that is an
            // eyeball judgement like the framing: dial it here, then put the vanilla soldier on the
            // platform and dial the same number against him. Nothing is written anywhere - see
            // <see cref="viewScale"/> - so '1x' is not an undo of a save, it is simply the value 1.
            GUILayout.BeginHorizontal();
            GUILayout.Label("model", GUILayout.Width(40f));
            float dialled = GUILayout.HorizontalSlider(viewScale, BenchList.ModelScaleMin,
                                                       BenchList.ModelScaleMax);
            if (Mathf.Abs(dialled - viewScale) > 1e-4f) Rescale(dialled, true);
            GUI.SetNextControlName(ScaleFieldName);
            string typed = GUILayout.TextField(scaleText ?? "", GUILayout.Width(52f));
            if (typed != scaleText)
            {
                // The typed text is NOT rewritten from the clamped value while it is being typed: doing
                // that turns "0." into "0.10" under the cursor and the field cannot be used at all.
                scaleText = typed;
                float exact;
                if (float.TryParse(typed, NumberStyles.Float, CultureInfo.InvariantCulture, out exact))
                    Rescale(exact, false);
            }
            if (GUILayout.Button("1x", GUILayout.Width(34f))) Rescale(1f, true);
            GUILayout.EndHorizontal();
            // The handles get a CLAUSE in the line that already lists the other two mouse gestures, not
            // a Label of their own: the panel's height is budgeted ROW BY ROW (BenchList.Rows), so one
            // more label anywhere above the fold pushes the SAVE row off the bottom of a small window -
            // which is the defect S29 exists to prevent, and it does not care which row was added.
            GUILayout.Label(framed
                ? (FitGizmo.Live ? "ARROWS on the gun = move it, RINGS = turn it about that axis (Esc " +
                                   "cancels; a dimmed handle is edge-on to the camera). " : "handles OFF. ") +
                  "drag = orbit, wheel = zoom, right-drag = turn the model, MIDDLE-drag = pan, " +
                  "WASD/QE (Shift = faster) = fly.  x" +
                  zoom.ToString("0.00", CultureInfo.InvariantCulture) +
                  " lift " + lift.ToString("0.00", CultureInfo.InvariantCulture) +
                  " yaw " + yaw.ToString("0", CultureInfo.InvariantCulture) +
                  " pitch " + pitch.ToString("0", CultureInfo.InvariantCulture) +
                  " r " + frameRadius.ToString("0.00", CultureInfo.InvariantCulture) + "m"
                : "NOT FRAMED - nothing with a renderer is standing there yet. Try RESET VIEW.");
        }

        /// <summary>Set the preview scale and re-apply the pose through the SAME callback that poses
        /// every rebuild (<see cref="Posed"/>), so the scale, the framing and the transport all land the
        /// way they do for any other change - there is no second posing path to drift.</summary>
        private static void Rescale(float v, bool retext)
        {
            viewScale = BenchList.Clamp(v, BenchList.ModelScaleMin, BenchList.ModelScaleMax);
            if (retext) scaleText = viewScale.ToString("0.00", CultureInfo.InvariantCulture);
            Posed();
        }

        /// <summary>The unit picker, which folds itself away once it has been used. A height of zero
        /// means the window is too short to spend any of it on a list - see
        /// <see cref="BenchList.Rows"/> - and then only the header is drawn.</summary>
        private static void Units(float height)
        {
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button((unitsOpen ? "v " : "> ") + "unit (" + units.Count + ")", GUILayout.Width(110f)))
                unitsOpen = !unitsOpen;
            if (unitsOpen)
            {
                GUI.SetNextControlName(UnitFilterName);
                unitFilter = GUILayout.TextField(unitFilter ?? "");
                // The catalogue is read once per open, so a content mod enabled or re-projected WHILE
                // the bench is up is otherwise invisible until it is closed and re-opened. Re-reading it
                // is one repository walk and touches nothing that is on screen.
                if (GUILayout.Button("rescan", GUILayout.Width(58f)))
                {
                    Catalog();
                    message = "ct_bench: catalogue re-read - " + units.Count + " unit(s), " +
                              ourUnits.Count + " of them built by a content mod and listed FIRST.";
                }
            }
            else GUILayout.Label(BenchList.Elide(unit == null ? "-" : unit.name, 28));
            GUILayout.EndHorizontal();
            if (!unitsOpen || height <= 0f) return;

            unitScroll = GUILayout.BeginScrollView(unitScroll, GUILayout.Height(height));
            foreach (TacCharacterDef d in units)
            {
                if (!BenchList.Matches(d.name, unitFilter)) continue;
                // Same mark, same reason, as the weapon list's: which of these several hundred templates
                // came out of a content mod has to be readable without picking each one.
                bool ours = ourUnits.Contains(d);
                if (!GUILayout.Button(BenchList.Elide(d.name, BenchList.NameChars - (ours ? 4 : 0)) +
                                      (ours ? "  *" : ""))) continue;
                unit = d; Pick();
                // Picked, so out of the way: the model and the dial are what he came for.
                unitsOpen = false;
            }
            GUILayout.EndScrollView();
        }

        private static void Weapons(string fitKey, float height)
        {
            List<string> keys = WeaponBuild.Fitted();
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button((weaponsOpen ? "v " : "> ") + "weapon (" + offered.Count + ")",
                                 GUILayout.Width(110f)))
                weaponsOpen = !weaponsOpen;
            if (weaponsOpen)
            {
                GUI.SetNextControlName(WeaponFilterName);
                weaponFilter = GUILayout.TextField(weaponFilter ?? "");
                // The calibration knob. The slot test is the game's own, but it is being asked a
                // question the game never asks it - about a bare template with no recruit behind it -
                // so there is one switch that says "show me the catalogue anyway" instead of a dead end.
                bool all = GUILayout.Toggle(offerAll, "all", GUILayout.Width(40f));
                if (all != offerAll) { offerAll = all; Offer(); }
            }
            else GUILayout.Label(BenchList.Elide(weapon == null ? "-" : weapon.name, 20));
            // OUT OF THE HAND AND BACK IN, and OUTSIDE the picker's fold so it is reachable with the
            // list shut - which is how the panel is used once a weapon has been chosen. Both directions
            // are the same rebuild the picker itself causes (Show -> RebuildCharacter), so the idle, the
            // clip set and the transport all re-resolve exactly as they do for any other change.
            GUI.enabled = weapon != null || lastWeapon != null;
            if (GUILayout.Button(weapon == null ? "RE-EQUIP" : "UNEQUIP", GUILayout.Width(84f)))
            {
                if (weapon != null) { lastWeapon = weapon; weapon = null; }
                else weapon = lastWeapon;
                Show();
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (!weaponsOpen || height <= 0f) return;

            GUILayout.Label(offered.Count + "/" + weapons.Count + " fit this unit" +
                            (offerAll ? " (filter OFF)" : "") + " | " + mine + " ours" +
                            (refused > 0 ? ", " + refused + " refused here" : ""));
            weaponScroll = GUILayout.BeginScrollView(weaponScroll, GUILayout.Height(height));
            foreach (WeaponDef d in offered)
            {
                if (!BenchList.Matches(d.name, weaponFilter)) continue;
                // The mod's own say so IN THE LIST, not only after you pick one: the whole point of
                // listing the shipped weapons is to stand one beside yours, and which is which has to
                // be readable at a glance. The mark is the DEF's own ResourcePath, not a live-fit
                // lookup - Fitted() is empty until a weapon has actually been in a hand, so the old
                // test called the author's own guns vanilla for as long as it mattered.
                bool ours = Mine(d);
                bool live = ours && BenchList.KeyFor(d.name, keys) != null;
                string label = BenchList.Elide(d.name, BenchList.NameChars - (ours ? 7 : 0)) +
                               (ours ? live ? "  * live" : "  *" : "");
                if (!GUILayout.Button(label)) continue;
                weapon = d; Show();
                weaponsOpen = false;
            }
            GUILayout.EndScrollView();
            GUILayout.Label("*  built by this mod - the only kind with a manifest row to save into.\n" +
                            "*  live  =  its fit is loaded this session, so the axis buttons work.");
        }

        /// <summary>
        /// The nudge panel. Every button here is a call into <see cref="WeaponBuild"/> and the line it
        /// returns is shown verbatim - including the refusals, and including the path a save went to.
        /// There is no second copy of the algebra and no second idea of where a file lives.
        /// </summary>
        private static void Dial(string fitKey)
        {
            GUILayout.Space(6f);
            if (weapon == null) { GUILayout.Label("pick a weapon to fit it."); return; }
            if (!Mine(weapon))
            {
                GUILayout.Label("'" + weapon.name + "' is a SHIPPED weapon. It is in the hand for " +
                                "comparison only: it has no ppcontent.json row, so there is nothing to " +
                                "tune and nowhere to save.");
                return;
            }
            // Built by this mod, but not yet FITTED this session - two different answers, and the old
            // panel gave the first one's message for both.
            if (fitKey == null)
            {
                GUILayout.Label("'" + weapon.name + "' WAS built by this mod, but no live fit for it " +
                                "exists yet this session: WeaponBuild only remembers a fit once the " +
                                "weapon's prefab has been instantiated in a hand. Pick it above (it is " +
                                "already selected) and let the rebuild finish, then the axes appear.");
                return;
            }

            Vector3 pos, euler, offset; float scale;
            if (!WeaponBuild.State(fitKey, out pos, out euler, out scale, out offset))
            {
                GUILayout.Label("'" + fitKey + "' has no live fit yet - equip it once so its prefab loads.");
                return;
            }
            // ============ SAVED, OR MESSED ABOUT WITH ============
            // "как бы я сейчас там не наколбасил" - and until this line there was no way to tell. It is
            // a comparison against the last numbers that were read from or written to the manifest,
            // nothing more: no history, no undo stack. What it buys is that experimenting stops being
            // frightening, because the answer to "have I changed anything" is on screen.
            bool dirty = WeaponBuild.Modified(fitKey);
            GUILayout.Label(dirty
                ? ">> MODIFIED - these numbers are NOT in the file yet. SAVE keeps them, REVERT throws "
                  + "them away. Nothing has been written to disk."
                : ">> SAVED - what is on screen is exactly what the manifest holds.");

            GUILayout.Label("pos " + Xyz(pos) + "   rot " + Xyz(euler) + "   scale " +
                            scale.ToString("0.0000", CultureInfo.InvariantCulture));
            GUILayout.Label("offset " + Xyz(offset) + "   (what the manifest carries)");

            // ponytail: 112 px x 3 is what fits BenchList.ContentWidth(PanelWidth); the offline arm
            // S29-row asserts it, because IMGUI does not clip an over-wide row - it draws the third
            // button past the edge of the panel where nothing can ever click it.
            const float StepW = 112f;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("move " + step.ToString("0.###", CultureInfo.InvariantCulture),
                                 GUILayout.Width(StepW))) step = BenchList.NextStep(step);
            if (GUILayout.Button("turn " + turn.ToString("0.##", CultureInfo.InvariantCulture) + "d",
                                 GUILayout.Width(StepW))) turn = BenchList.NextTurn(turn);
            if (GUILayout.Button("scale " + scaleStep.ToString("0.###", CultureInfo.InvariantCulture),
                                 GUILayout.Width(StepW))) scaleStep = BenchList.NextStep(scaleStep);
            GUILayout.EndHorizontal();

            Axes(fitKey, "move", step, true);
            Axes(fitKey, "turn", turn, false);

            GUILayout.BeginHorizontal();
            GUILayout.Label("scale", GUILayout.Width(40f));
            if (GUILayout.Button("-")) message = WeaponBuild.Adjust(fitKey, Vector3.zero, Vector3.zero, -scaleStep);
            if (GUILayout.Button("+")) message = WeaponBuild.Adjust(fitKey, Vector3.zero, Vector3.zero, scaleStep);
            if (GUILayout.Button("report", GUILayout.Width(70f))) message = WeaponBuild.Report(fitKey);
            GUILayout.EndHorizontal();

            // ============ THE ONE WRITE, AND THE TWO WAYS OUT ============
            // Deliberately on their OWN row, away from the axis buttons and from each other, and named
            // rather than abbreviated: the terse "reload" beside "SAVE" was a save-shaped button next
            // to a discard-shaped one with nothing to say which was which.
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(dirty ? "SAVE TO FILE *" : "SAVE TO FILE", GUILayout.Width(126f)))
                message = SaveAndMirror(fitKey);
            // Level 1 undo: back to what the file currently says. WeaponBuild.Reload re-reads the
            // manifest and re-applies it, which is exactly "throw away what I have been nudging".
            if (GUILayout.Button("REVERT", GUILayout.Width(90f)))
                message = WeaponBuild.Reload(fitKey);
            // Level 2 undo: back to the solve, with the file's own overrides ignored too. This is the
            // one for a bad fit that has already been saved.
            if (GUILayout.Button("RESET AUTO", GUILayout.Width(110f)))
                message = WeaponBuild.Auto(fitKey);
            GUILayout.EndHorizontal();
            GUILayout.Label("SAVE writes " + BenchList.Id(fitKey) + " into its ppcontent.json (the path " +
                            "is printed below).  REVERT = back to that file.  RESET AUTO = back to the " +
                            "measured solve, every override dropped.  Neither undo touches the disk.");
        }

        /// <summary>
        /// SAVE, and then the SAME BYTES back into the repo the mod was deployed from - see
        /// <see cref="BenchList.MirrorSave"/> for why that second half exists and how the source path
        /// is known without anyone configuring anything.
        ///
        /// The mirror is strictly AFTER, and strictly conditional on the real save having worked:
        /// nothing here touches WeaponManifest's validated splice, it copies the file that splice just
        /// produced. Success is read off <c>WeaponBuild.Save</c>'s own answer - it returns a string and
        /// nothing else, and "ct_fit saved" is the only line it emits when a byte was actually written
        /// (every refusal begins "ct_fit save REFUSED" or "ct_fit: ").
        ///
        /// BOTH destinations are always reported, including when there is only one and why.
        /// </summary>
        private static string SaveAndMirror(string fitKey)
        {
            string said = WeaponBuild.Save(fitKey);
            if (said == null || said.IndexOf("ct_fit saved", StringComparison.Ordinal) < 0) return said;
            return said + "\n" + BenchList.MirrorSave(BenchList.Manifest(fitKey));
        }

        private static void Axes(string fitKey, string what, float amount, bool position)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(what, GUILayout.Width(46f));
            for (int axis = 0; axis < 3; axis++)
                for (int s = -1; s <= 1; s += 2)
                {
                    if (!GUILayout.Button("XYZ"[axis] + (s < 0 ? "-" : "+"))) continue;
                    float[] d = BenchList.Delta(axis, s, amount);
                    Vector3 v = new Vector3(d[0], d[1], d[2]);
                    message = position
                        ? WeaponBuild.Adjust(fitKey, v, Vector3.zero, 0f)
                        : WeaponBuild.Adjust(fitKey, Vector3.zero, v, 0f);
                }
            GUILayout.EndHorizontal();
        }

        /// <summary>The answer line - where a save went, and what a refusal said. It has an EXPLICIT
        /// height now: an unbounded scroll view inside the panel's own scroll view has no height to
        /// negotiate against and IMGUI resolves that by giving it everything, which pushed the two
        /// pickers off the bottom of a panel that had just been reordered to keep things on it.</summary>
        private static void Message()
        {
            GUILayout.Space(4f);
            GUILayout.Label("last answer (this is where a save went):");
            messageScroll = GUILayout.BeginScrollView(messageScroll,
                                                      GUILayout.Height(BenchList.MessageHeight));
            GUILayout.Label(message ?? "");
            GUILayout.EndScrollView();
        }

        private static string Xyz(Vector3 v)
        {
            return v.x.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                   v.y.ToString("0.###", CultureInfo.InvariantCulture) + "," +
                   v.z.ToString("0.###", CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- the frame loop

        /// <summary>
        /// The hotkey, the drag-to-turn and the panel. Nothing else in the mod polls input, and this
        /// costs one GetKeyDown per frame - the same price src\Dev\DevRunner.cs already pays.
        /// </summary>
        private sealed class Arm : MonoBehaviour
        {
            private bool inputBroken;
            private float lastX, lastY;
            /// <summary>Whether the drag in progress began on the 3D half. See <see cref="Mouse"/>.</summary>
            private bool dragging;

            /// <summary>The toggle press. Both halves matter: the key must not be one the game already
            /// answers to - or the press does two things and only one of them is ours - and it must be
            /// held with Ctrl+Alt, so nothing a player types can open a dev panel by accident.</summary>
            private static bool Chord()
            {
                if (BenchList.IsGameOwned(Hotkey.ToString())) return false;
                if (!Input.GetKeyDown(Hotkey)) return false;
                return (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
                       (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt));
            }

            private void Update()
            {
                if (inputBroken) return;
                try
                {
                    // Let go BEFORE anything else looks at the bay: if the level went away this frame,
                    // every reference below it is a corpse and the panel is a lie on top of a loading
                    // screen.
                    if (open && !StillThere())
                    {
                        message = Close();
                        ContentToolMain.Say("ct_bench: the level stopped playing under the workbench " +
                                            "(a load, a mission, or a return to the menu), so it let go " +
                                            "without touching it. " + message);
                        return;
                    }

                    if (Chord())
                    {
                        message = open ? Close() : Open();
                        ContentToolMain.Say(message);
                    }
                    if (!open || bay == null || bay.SceneRoot == null) return;
                    Mouse();
                    Fly();
                }
                catch (Exception ex)
                {
                    // A build with legacy Input disabled would otherwise throw once per frame forever -
                    // the same guard, for the same reason, as DevRunner's hotkey.
                    inputBroken = true;
                    ContentToolMain.Say("ct_bench: input is unavailable in this build, use 'ct_bench' " +
                                        "from the console (" + ex.Message + ")");
                }
            }

            /// <summary>
            /// ============ THE MOUSE, AND THE ONE PLACE IT IS ALLOWED TO ACT ============
            ///
            /// Left-drag ORBITS THE CAMERA around the unit, wheel zooms, right-drag still turns the
            /// model itself. The keyboard knobs in the panel are untouched and remain the fallback -
            /// they are also the only way to work if a build has legacy Input disabled.
            ///
            /// EVERY branch is gated on <see cref="BenchList.OverScene"/>, i.e. on the pointer being on
            /// the 3D half of the screen. Without it, dragging the panel's own scrollbar or holding a
            /// repeat button would swing the camera at the same time, and a wheel over the weapon list
            /// would scroll the list AND zoom.
            ///
            /// Both drags accumulate through <see cref="BenchList"/>'s clamped helpers, so neither can
            /// walk the view somewhere there is no way back from - which is the whole lesson of the
            /// unbounded 'lift' this replaces.
            /// </summary>
            private void Mouse()
            {
                // A FOURTH REGION. The order is panel -> transport strip -> gizmo -> orbit, and the
                // strip is subtracted here for the same reason the panel is: a drag on the scrub
                // slider that wanders a pixel below it must move the clip, never the camera.
                bool over = BenchList.OverScene(Input.mousePosition.x, PanelWidth) &&
                            !BenchList.OverStrip(Input.mousePosition.x, Input.mousePosition.y,
                                                 Screen.width, Screen.height, PanelWidth) &&
                            // A FIFTH REGION, and only while it exists: the clip list opens UPWARD out
                            // of the strip and over the scene, so without this a click on a clip would
                            // pick the clip AND start an orbit.
                            !FitAnim.OverList(Input.mousePosition.x, Input.mousePosition.y);

                float wheel = Input.mouseScrollDelta.y;
                if (over && Mathf.Abs(wheel) > 0.01f)
                {
                    zoom = BenchList.Wheel(zoom, wheel);
                    Reframe();
                }

                if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) ||
                    Input.GetMouseButtonDown(2))
                {
                    // THE GIZMO GETS FIRST REFUSAL ON A LEFT PRESS, and it has to be ASKED rather than
                    // consulted through FitGizmo.Owns: Unity runs every Update before any OnGUI, so on
                    // the frame of the press the handles have not claimed hotControl yet and Owns is
                    // still false. Without this, a press on an arrow started an orbit AND a drag, and
                    // the model swung while the gun was being moved.
                    dragging = over && !(Input.GetMouseButtonDown(0) &&
                                         FitGizmo.WouldGrab(Input.mousePosition.x, Input.mousePosition.y));
                    // Clicking the model is how a user says "I am done typing" - IMGUI will not work
                    // that out on its own, and a filter that keeps the keyboard keeps the fly keys.
                    if (over) dropFocus = true;
                    lastX = Input.mousePosition.x;
                    lastY = Input.mousePosition.y;
                }
                // And for every frame AFTER the press, the latched claim is the answer.
                if (FitGizmo.Owns) { dragging = false; return; }
                if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2))
                { dragging = false; return; }
                // A drag that STARTED on the panel keeps its grip there even when the pointer wanders
                // over the model - otherwise letting go of a scrollbar past the panel edge would snap
                // the camera round.
                if (!dragging) return;

                float dx = Input.mousePosition.x - lastX, dy = Input.mousePosition.y - lastY;
                lastX = Input.mousePosition.x;
                lastY = Input.mousePosition.y;
                if (Mathf.Abs(dx) < 0.01f && Mathf.Abs(dy) < 0.01f) return;

                // MIDDLE-DRAG PANS, and it is asked before the other two: it moves the pivot with the
                // camera, which is the only gesture that gets a zoomed-in view off the inside of the
                // body and onto the gun.
                if (Input.GetMouseButton(2)) { PanBy(dx, dy); return; }

                if (Input.GetMouseButton(1))
                {
                    // The bay's own SceneRoot is what the game itself turns (GeoSquadBayReference
                    // remembers SceneDefaultRotation off it), and both Close and RESET VIEW put the
                    // rotation back. Kept because turning the MODEL and orbiting the CAMERA answer
                    // different questions - "how does the far side look" versus "how does it look from
                    // over there" - and they no longer fight: separate buttons, separate state.
                    bay.SceneRoot.Rotate(Vector3.up, -dx * 0.4f, Space.World);
                    return;
                }
                yaw = BenchList.Orbit(yaw, dx);
                pitch = BenchList.Tilt(pitch, dy);
                Reframe();
            }

            /// <summary>The computed pose, re-asserted after everything else has had its turn. LATE
            /// update on purpose: CinemachineBrain writes the camera in its own LateUpdate, and while
            /// it is disabled nothing should - but "should" is not a guarantee about a geoscape full of
            /// live camera behaviours, and one stray write puts the unit back off screen with nothing to
            /// say why. Two assignments a frame, and Close puts the original pose back.</summary>
            private void LateUpdate()
            {
                if (!open) { FitGizmo.Aim(null, null, null); return; }
                // BEFORE the camera: a scrubbed frame changes where the hand is, and the handles are
                // sized and picked against the pose the camera is about to be written to.
                FitAnim.Tick();
                if (framed && cam != null)
                {
                    cam.transform.position = framePos;
                    cam.transform.rotation = frameRot;
                }
                // AFTER the camera has been written, never before: the handles are sized and picked
                // against the camera's pose, and a frame of lag between the two is a frame in which the
                // arrows are drawn somewhere the mouse cannot reach them.
                FitGizmo.Aim(cam, LiveMesh(), Mine(weapon) ? BenchList.KeyFor(
                                 weapon == null ? null : weapon.name, WeaponBuild.Fitted()) : null);
            }

            /// <summary>The handles themselves. OnRenderObject is Unity's only "draw raw geometry into
            /// this camera" callback that does not need a Renderer, a mesh or a GameObject in the
            /// scene - which is exactly right for something that must leave nothing behind.</summary>
            private void OnRenderObject()
            {
                if (open) FitGizmo.Render();
            }

            /// <summary>
            /// ============ THE PANEL CAN DIE WITHOUT ANYONE PRESSING ANYTHING ============
            ///
            /// Until now the ONLY path that put the game back was a button, a hotkey, a console verb or
            /// <see cref="Uninstall"/> - every one of them a deliberate act. But this component can also
            /// simply GO: a mod reload destroys the arm, so does application quit, and disabling it
            /// stops Update, LateUpdate and OnGUI dead. Every one of those left the canvases hidden, the
            /// SoftMasks off and a CinemachineBrain disabled, with no panel on screen and no key that
            /// would reach the close path any more - unrecoverable without restarting the game.
            ///
            /// So the cleanup hangs off the LIFECYCLE too, where nothing has to be pressed. Both are
            /// idempotent: Close returns immediately when there is nothing entered, so the ordinary
            /// Uninstall -> Close -> Destroy -> OnDestroy sequence does the work exactly once.
            /// </summary>
            private void OnDisable() { Rescue("disabled"); }
            private void OnDestroy() { Rescue("destroyed"); }

            private static void Rescue(string how)
            {
                if (!entered) return;
                try
                {
                    string undone = Close();
                    ContentToolMain.Say("ct_bench: the workbench was " + how + " while it was open, so " +
                                        "it put the game back on its own. " + undone);
                }
                catch (Exception) { }
            }

            private void OnGUI()
            {
                if (!open) return;
                try
                {
                    if (dropFocus) { GUI.FocusControl(null); dropFocus = false; }
                    string focused = GUI.GetNameOfFocusedControl();
                    typing = focused == UnitFilterName || focused == WeaponFilterName ||
                             focused == ScaleFieldName;
                    // FIRST, and unconditionally: it allocates a control id from this pass's counter,
                    // and an id that is only sometimes allocated is a different id every frame.
                    FitGizmo.Gui(PanelWidth,
                                 BenchList.StripTop(Screen.width, Screen.height, PanelWidth));
                    if (FitGizmo.Last != null) { message = FitGizmo.Last; FitGizmo.Last = null; }
                    Draw();
                    // AFTER the panel, and outside its area: the strip is its own region and IMGUI
                    // areas do not nest.
                    FitAnim.Draw(PanelWidth);
                }
                catch (Exception ex)
                {
                    // NEVER leave the player looking at a half-drawn panel he cannot dismiss: an
                    // exception out of OnGUI aborts the layout and would take the Close button with it.
                    message = Close();
                    ContentToolMain.Say("ct_bench: the panel threw and closed itself - " + ex);
                }
            }
        }
    }
}
