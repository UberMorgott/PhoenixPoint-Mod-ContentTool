using System;
using System.Collections.Generic;
using System.Globalization;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// The engine-free half of the guided "Add a weapon" flow: the weapon CLASSES, which clips belong to a
    /// class, which step the author is on and what the one main button says. Everything here is asserted
    /// offline (tests\TargetPathTests, WeaponFlowArm); the screen itself is <see cref="TaskScreens"/>.
    ///
    /// ============ WHERE THE CLASSES COME FROM ============
    ///
    /// The game has no weapon-kind enum. It sorts weapons by GameTagDef on the def (<c>ItemDef</c> ->
    /// <c>AddonDef.Tags</c>, AddonDef.cs:83), the same tags proficiencies and perks key on - the names
    /// below are the ones TFTV looks up (refs\TFTV-src\TFTV\TFTVDrills\DrillsDefs.cs:522-523, 732-738,
    /// :1548; TFTVMeleeDamage.cs:536; SkillModifications\HeavySkills.cs:461; FactionPerks.cs:343). A class
    /// is only SHOWN when the running game has a weapon wearing its tag, so a tag this list names but the
    /// game lacks costs nothing, and a weapon with none of them lands in "Other".
    ///
    /// How it is held and which clips it plays is NOT stored here: the animation actions pick clips by
    /// <c>ItemDef.HandsToUse</c> (ItemDef.cs:40) against each action's <c>NumberOfHands</c>, or by an
    /// explicit equipment list (TacActorAnimActionEquipmentFilteredDef.cs:15-21). A new weapon is a CLONE
    /// of its template, so it inherits the template's hands, and WeaponBuild (WeaponBuild.cs:278-292) adds
    /// the clone to every list that names the template. The class picks the template; the template is the
    /// hand preset and the animation set.
    /// </summary>
    internal static class WeaponFlow
    {
        internal sealed class WeaponClass
        {
            internal readonly string Id, Label, Tag;
            /// <summary>Shipped defs to start from, best first - the first one the game has is the default.</summary>
            internal readonly string[] Templates;
            /// <summary>Words a clip name carries when it is one of this class's moves.</summary>
            internal readonly string[] Moves;

            internal WeaponClass(string id, string label, string tag, string[] templates, string[] moves)
            { Id = id; Label = label; Tag = tag; Templates = templates; Moves = moves; }
        }

        private static readonly string[] GunMoves =
            { "idle", "aim", "shoot", "shot", "fire", "reload", "run", "walk", "sprint", "overwatch", "draw", "holster",
              "ready", "crouch" };
        private static readonly string[] MeleeMoves =
            { "idle", "attack", "strike", "swing", "melee", "bash", "slash", "shot", "whip", "buttstroke", "run", "walk", "sprint", "draw",
              "holster", "ready" };

        /// <summary>
        /// PRECEDENCE ORDER - a weapon is the FIRST class whose tag it wears. The narrow kinds come first
        /// because a launcher or a flamer can also wear the broad Heavy tag, and Heavy is the one class
        /// whose clips are "big two-handed gun" rather than one weapon's; the broad one comes last.
        /// </summary>
        internal static readonly WeaponClass[] All =
        {
            new WeaponClass("melee", "Melee", "MeleeWeapon_TagDef",
                            new[] { "PX_StunRod_WeaponDef", "SY_LaserBlade_WeaponDef", "AN_Blade_WeaponDef" }, MeleeMoves),
            new WeaponClass("grenadelauncher", "Grenade launcher", "GrenadeLauncherItem_TagDef",
                            new[] { "PX_GrenadeLauncher_WeaponDef" }, GunMoves),
            new WeaponClass("dronelauncher", "Drone launcher", "DroneLauncherItem_TagDef",
                            new[] { "SY_SpiderDroneLauncher_WeaponDef" }, GunMoves),
            new WeaponClass("flamethrower", "Flamethrower", "FlamethrowerItem_TagDef", new string[0], GunMoves),
            new WeaponClass("viral", "Viral weapon", "ViralItem_TagDef", new[] { "AN_Redemptor_WeaponDef" }, GunMoves),
            new WeaponClass("crossbow", "Crossbow", "CrossbowItem_TagDef", new[] { "SY_Crossbow_WeaponDef" }, GunMoves),
            new WeaponClass("pistol", "Pistol", "HandgunItem_TagDef",
                            new[] { "PX_Pistol_WeaponDef", "AN_HandCannon_WeaponDef" }, GunMoves),
            new WeaponClass("pdw", "PDW / SMG", "PDWItem_TagDef", new[] { "PX_LaserPDW_WeaponDef" }, GunMoves),
            new WeaponClass("shotgun", "Shotgun", "ShotgunItem_TagDef",
                            new[] { "PX_ShotgunRifle_WeaponDef", "AN_Shotgun_WeaponDef" }, GunMoves),
            new WeaponClass("sniper", "Sniper rifle", "SniperRifleItem_TagDef",
                            new[] { "PX_SniperRifle_WeaponDef" }, GunMoves),
            new WeaponClass("rifle", "Assault rifle", "AssaultRifleItem_TagDef",
                            new[] { "PX_AssaultRifle_WeaponDef", "NJ_Gauss_AssaultRifle_WeaponDef" }, GunMoves),
            new WeaponClass("heavy", "Heavy weapon", "HeavyItem_TagDef",
                            new[] { "PX_HeavyCannon_WeaponDef", "NJ_Gauss_MachineGun_WeaponDef",
                                    "NJ_HeavyRocketLauncher_WeaponDef" }, GunMoves),
        };

        /// <summary>A weapon that wears none of the tags above.</summary>
        internal static readonly WeaponClass Other = new WeaponClass("other", "Other", null, new string[0], GunMoves);

        private static readonly string[] AssaultSoldier = { "PX_AssaultStarting_TacCharacterDef", "PX_Assault_TacCharacterDef" };
        private static readonly string[] HeavySoldier = { "PX_HeavyStarting_TacCharacterDef", "PX_Heavy_TacCharacterDef" };
        private static readonly string[] SniperSoldier = { "PX_SniperStarting_TacCharacterDef", "PX_Sniper_TacCharacterDef" };

        /// <summary>
        /// WHO STANDS ON THE PLATFORM for a class, best first. The bench's default unit is simply the first
        /// template by name - a Hoplite (a shield-bearing guardian) whose clips are HumanGuard_* and whose
        /// hands hold nothing a soldier holds - so the flow's "moves of this class" strip would list a
        /// creature's moves. A Phoenix soldier of the class that carries the weapon plays the soldier clips
        /// the weapon is really seen with. A list, because a mod may remove a template.
        /// </summary>
        internal static string[] SoldiersFor(WeaponClass c)
        {
            if (c == null) return AssaultSoldier;
            switch (c.Id)
            {
                case "heavy": case "grenadelauncher": case "dronelauncher": case "flamethrower": return HeavySoldier;
                case "sniper": case "crossbow": return SniperSoldier;
                default: return AssaultSoldier;
            }
        }

        /// <summary>The class of a weapon with these tag names - the first in <see cref="All"/> it wears.</summary>
        internal static WeaponClass Classify(IEnumerable<string> tagNames)
        {
            var have = new HashSet<string>(StringComparer.Ordinal);
            if (tagNames != null) foreach (string t in tagNames) if (t != null) have.Add(t);
            foreach (WeaponClass c in All) if (have.Contains(c.Tag)) return c;
            return Other;
        }

        internal static WeaponClass ById(string id)
        {
            foreach (WeaponClass c in All) if (c.Id == id) return c;
            return id == Other.Id ? Other : null;
        }

        /// <summary>The template a class starts from: its best shipped def the game has, else its first.</summary>
        internal static string DefaultTemplate(WeaponClass c, IList<string> donorsOfClass)
        {
            if (c == null || donorsOfClass == null || donorsOfClass.Count == 0) return null;
            foreach (string t in c.Templates)
                foreach (string d in donorsOfClass) if (string.Equals(d, t, StringComparison.Ordinal)) return d;
            return donorsOfClass[0];
        }

        /// <summary>Is this clip one of the class's moves? A word match, case-blind.</summary>
        internal static bool Relevant(WeaponClass c, string clipName)
        {
            if (c == null || string.IsNullOrEmpty(clipName)) return false;
            foreach (string m in c.Moves)
                if (clipName.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        /// <summary>
        /// The clip rows to list: those that are the class's moves, or every row when <paramref name="c"/>
        /// is null, <paramref name="all"/> is set, or NONE match - a filter that empties the transport is a
        /// transport whose buttons go dead, which is the one thing it must never do.
        /// </summary>
        internal static List<int> Visible(WeaponClass c, IList<string> names, bool all)
        {
            var keep = new List<int>();
            if (names == null) return keep;
            if (c != null && !all)
                for (int i = 0; i < names.Count; i++) if (Relevant(c, names[i])) keep.Add(i);
            if (keep.Count == 0) for (int i = 0; i < names.Count; i++) keep.Add(i);
            return keep;
        }

        // ---------------------------------------------------------------- the steps

        internal static readonly string[] Steps = { "Class", "Model & name", "Fit in hand", "Check moves" };

        /// <summary>Everything the flow's one main button depends on.</summary>
        internal struct State
        {
            internal bool ClassPicked, TemplatePicked, HaveModel, HaveName, Created, Live, InHand, Fitted, Dirty;
            internal string ModRefusal;
        }

        internal enum Act { None, Create, Build, Hold, Save }

        /// <summary>The step the bar marks: 0 class, 1 model &amp; name, 2 fit, 3 moves.</summary>
        internal static int Step(State s)
        {
            if (!s.ClassPicked || !s.TemplatePicked) return 0;
            if (!s.Created) return 1;
            if (!s.Fitted || s.Dirty) return 2;
            return 3;
        }

        /// <summary>
        /// The ONE main button: its label, its reason for being grey (null = pressable) and what it does.
        /// The reason is the next thing the author has to do, in plain words.
        /// </summary>
        internal static Act MainButton(State s, out string label, out string refusal)
        {
            label = "Create weapon"; refusal = null;
            if (!s.ClassPicked) { refusal = "pick what kind of weapon it is (step 1)"; return Act.None; }
            if (!s.TemplatePicked) { refusal = "pick the game weapon it copies (step 1)"; return Act.None; }
            if (s.ModRefusal != null) { refusal = s.ModRefusal; return Act.None; }
            if (!s.Created)
            {
                if (!s.HaveModel) refusal = "pick your .glb model (step 2)";
                else if (!s.HaveName) refusal = "type the weapon's name (step 2)";
                return refusal == null ? Act.Create : Act.None;
            }
            if (!s.Live)
            {
                label = "Next: Build & share";
                return Act.Build;
            }
            if (!s.InHand) { label = "Put it in the hand"; return Act.Hold; }
            label = s.Dirty ? "Save fit *" : "Save fit";
            if (!s.Fitted) { refusal = "wait - the weapon has not loaded in the hand yet (re-pick it if this stays)"; return Act.None; }
            return Act.Save;
        }

        // ---------------------------------------------------------------- the fit dials

        /// <summary>Slider ranges: the offset in metres, the turn in degrees, the size as a multiplier.</summary>
        internal const float OffsetRange = 0.5f, TurnRange = 180f, ScaleMin = 0.05f, ScaleMax = 5f;

        /// <summary>A typed number, invariant culture; false (value untouched) on anything else.</summary>
        internal static bool Parse(string text, out float value)
        {
            return float.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                   && !float.IsNaN(value) && !float.IsInfinity(value);
        }

        internal static string Show(float v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

        /// <summary>A turn folded into (-180, 180] so the slider can always show it.</summary>
        internal static float Wrap(float degrees)
        {
            float d = degrees % 360f;
            if (d > 180f) d -= 360f;
            if (d <= -180f) d += 360f;
            return d;
        }
    }
}
