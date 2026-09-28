using System;
using System.Collections.Generic;
using PhoenixPoint.Common.Entities.GameTags;
using PhoenixPoint.Tactical.Entities.Weapons;
using Morgott.ContentTool.Tactical;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// What the guided Add-a-weapon flow (<see cref="TaskScreens.Weapon"/>) asks of the bench: the weapon
    /// classes the RUNNING game has, the templates in each, putting a weapon in the soldier's hand, and the
    /// fit dials. The flow owns the words and the steps; the bench owns the soldier, the catalogue and the
    /// fit - so there is still one Show() and one save path.
    /// </summary>
    internal static partial class FitBench
    {
        private static Dictionary<string, WeaponFlow.WeaponClass> classOf;
        private static List<WeaponDef> classOfFrom;

        /// <summary>The class of every catalogued weapon, off its own tags; rebuilt with the catalogue.</summary>
        private static Dictionary<string, WeaponFlow.WeaponClass> ClassTable()
        {
            if (classOf != null && classOfFrom == weapons) return classOf;
            classOf = new Dictionary<string, WeaponFlow.WeaponClass>(StringComparer.Ordinal);
            foreach (WeaponDef d in weapons)
            {
                var tags = new List<string>();
                try
                {
                    if (d.Tags != null) foreach (GameTagDef t in d.Tags) if (t != null) tags.Add(t.name);
                }
                catch (Exception) { }
                classOf[d.name] = WeaponFlow.Classify(tags);
            }
            classOfFrom = weapons;
            return classOf;
        }

        internal static WeaponFlow.WeaponClass ClassOf(string defName)
        {
            WeaponFlow.WeaponClass c;
            return defName != null && ClassTable().TryGetValue(defName, out c) ? c : null;
        }

        /// <summary>The shipped weapons a new one of class <paramref name="c"/> may copy.</summary>
        internal static List<string> TemplatesOf(WeaponFlow.WeaponClass c)
        {
            var list = new List<string>();
            if (c == null) return list;
            foreach (string d in Donors()) if (ClassOf(d) == c) list.Add(d);
            return list;
        }

        /// <summary>The classes the game really has templates for, in precedence order, with their counts.</summary>
        internal static List<KeyValuePair<WeaponFlow.WeaponClass, int>> Classes()
        {
            var count = new Dictionary<WeaponFlow.WeaponClass, int>();
            foreach (string d in Donors())
            {
                WeaponFlow.WeaponClass c = ClassOf(d);
                if (c == null) continue;
                int n; count.TryGetValue(c, out n); count[c] = n + 1;
            }
            var list = new List<KeyValuePair<WeaponFlow.WeaponClass, int>>();
            foreach (WeaponFlow.WeaponClass c in WeaponFlow.All)
            { int n; if (count.TryGetValue(c, out n)) list.Add(new KeyValuePair<WeaponFlow.WeaponClass, int>(c, n)); }
            { int n; if (count.TryGetValue(WeaponFlow.Other, out n)) list.Add(new KeyValuePair<WeaponFlow.WeaponClass, int>(WeaponFlow.Other, n)); }
            return list;
        }

        internal static string WordOf(string defName) { return Word(defName); }

        /// <summary>One or two hands, off the def - the grip the class's clips are picked by (ItemDef.cs:40).</summary>
        internal static string Hands(string defName)
        {
            WeaponDef d = defName == null ? null : weapons.Find(x => x.name == defName);
            if (d == null) return null;
            return d.HandsToUse >= 2 ? "two-handed" : d.HandsToUse == 1 ? "one-handed" : "no hands (mounted)";
        }

        /// <summary>Is this def loaded in the game right now - true for a mod weapon once the game was
        /// started with its mod switched on.</summary>
        internal static bool Loaded(string defName)
        {
            return defName != null && weapons.Exists(x => x.name == defName);
        }

        /// <summary>Put the def in the soldier's hand - the same Show() the Fit screen uses. A no-op when
        /// the game has no such weapon.</summary>
        internal static void Hold(string defName) { ShowDonor(defName); }

        internal static bool Holding(string defName) { return weapon != null && weapon.name == defName; }

        /// <summary>The live fit key of the weapon in the hand when it is <paramref name="defName"/>.</summary>
        internal static string HeldFitKey(string defName)
        {
            return Holding(defName) ? BenchList.KeyFor(weapon.name, WeaponBuild.FittedKeys) : null;
        }

        internal static string SaveFit(string fitKey) { return SaveAndMirror(fitKey); }

        /// <summary>Names every flow text field starts with, so the bench's fly keys stand down while typing.</summary>
        internal const string TypingPrefix = "type/";

        private static readonly string[] tuneText = new string[7];
        private static string tunedKey;
        private static readonly string[] TuneRow = { "move X", "move Y", "move Z", "turn X", "turn Y", "turn Z", "size" };

        /// <summary>
        /// The fit dials: per channel a slider, a typed number and -/+ nudges, each a DELTA through
        /// <see cref="WeaponBuild.Adjust"/> so the auto-solve and the sockets follow exactly as the Fit
        /// screen's buttons make them. Move = the offset the manifest carries (metres), turn = degrees,
        /// size = the scale. Returns the last answer line, or null when nothing was pressed.
        /// </summary>
        internal static string Tune(string fitKey)
        {
            Vector3 pos, euler, offset; float scale;
            if (!WeaponBuild.State(fitKey, out pos, out euler, out scale, out offset)) return null;
            string said = null;
            // A typed buffer belongs to the weapon it was typed for: another key drops focus, so every
            // field re-reads the new weapon's numbers instead of applying the old one's to it.
            if (fitKey != tunedKey) { tunedKey = fitKey; GUI.FocusControl(null); }
            string focused = GUI.GetNameOfFocusedControl();

            GUILayout.BeginHorizontal();
            GUILayout.Label("steps", GUILayout.Width(46f));
            if (GUILayout.Button("move " + WeaponFlow.Show(step), GUILayout.Width(96f))) step = BenchList.NextStep(step);
            if (GUILayout.Button("turn " + WeaponFlow.Show(turn) + "d", GUILayout.Width(96f))) turn = BenchList.NextTurn(turn);
            if (GUILayout.Button("size " + WeaponFlow.Show(scaleStep), GUILayout.Width(96f))) scaleStep = BenchList.NextStep(scaleStep);
            GUILayout.EndHorizontal();

            for (int i = 0; i < 7; i++)
            {
                bool move = i < 3, size = i == 6;
                float now = move ? offset[i] : size ? scale : WeaponFlow.Wrap(euler[i - 3]);
                float lo = move ? -WeaponFlow.OffsetRange : size ? WeaponFlow.ScaleMin : -WeaponFlow.TurnRange;
                float hi = move ? WeaponFlow.OffsetRange : size ? WeaponFlow.ScaleMax : WeaponFlow.TurnRange;
                float nudge = move ? step : size ? scaleStep : turn;
                string name = TypingPrefix + "tune" + i;
                if (focused != name) tuneText[i] = WeaponFlow.Show(now);

                float want = float.NaN;
                GUILayout.BeginHorizontal();
                GUILayout.Label(TuneRow[i], GUILayout.Width(46f));
                if (GUILayout.Button("-", GUILayout.Width(24f))) want = now - nudge;
                float shown = Mathf.Clamp(now, lo, hi);
                float slid = GUILayout.HorizontalSlider(shown, lo, hi);
                if (Mathf.Abs(slid - shown) > 1e-5f) want = slid;
                if (GUILayout.Button("+", GUILayout.Width(24f))) want = now + nudge;
                GUI.SetNextControlName(name);
                string typed = GUILayout.TextField(tuneText[i] ?? "", GUILayout.Width(60f));
                GUILayout.EndHorizontal();
                float exact;
                if (typed != tuneText[i]) { tuneText[i] = typed; if (WeaponFlow.Parse(typed, out exact)) want = exact; }

                if (float.IsNaN(want) || Mathf.Abs(want - now) < 1e-6f) continue;
                float d = want - now;
                Vector3 dv = Vector3.zero;
                if (!size) dv[move ? i : i - 3] = d;
                said = move ? WeaponBuild.Adjust(fitKey, dv, Vector3.zero, 0f)
                     : size ? WeaponBuild.Adjust(fitKey, Vector3.zero, Vector3.zero, d)
                     : WeaponBuild.Adjust(fitKey, Vector3.zero, dv, 0f);
            }
            return said;
        }
    }
}
