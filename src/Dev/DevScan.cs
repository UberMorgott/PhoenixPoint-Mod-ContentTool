using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// Task 26 - the FALLBACK discovery seam, developer mode only (FINAL-PLAN 39.3, re-scoped by 39.8).
    ///
    /// The seam (<see cref="SeamSwap"/>) can only hand out a `guid:` for something a resolve went
    /// through. Scene props and static geometry never do, so for those the only thing that reaches
    /// them is the scan RR used (`Resource_Replacer.cs:1023`, `MeshReplacer.cs:3156-3172`). It is
    /// OFF by default, one toggle turns it on, and a name it finds twice is a REFUSAL, never a pick -
    /// the same rule as `BundleBaker.FindUnique`.
    ///
    /// It cannot leak into a shipped mod: `ReplacementSet.Bakeable` already fails any `name:` record
    /// by name, and route vii (the shipping mechanism) has no runtime at all to run a scan in.
    ///
    /// ponytail: one-shot scan at apply time, no periodic re-scan and therefore no frame budget.
    /// RR needed the timer because it swept the whole scene every few seconds; here the scan runs
    /// once, on one console command, and writes one live object. Upgrade path if a `name:` swap has
    /// to survive a scene load: re-run <see cref="Find"/> from a coroutine on the budget shape in
    /// `Resource_Replacer.cs:1030-1055`.
    /// </summary>
    internal static class DevScan
    {
        /// <summary>OFF by default - §39.3 makes that a rule, not a default value.</summary>
        internal static bool Enabled;

        internal static string Run(string[] args)
        {
            string cmd = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "status";
            switch (cmd)
            {
                case "on": Enabled = true; return "ct_scan ON - name: targets resolve by scanning the live scene (dev mode only; a scan-found target can never be baked)";
                case "off": Enabled = false; return "ct_scan OFF - name: targets are refused";
                case "status": return "ct_scan is " + (Enabled ? "ON" : "OFF");
                case "gate": return Gate();
                default: return "usage: ct_scan [on|off|status|gate]";
            }
        }

        /// <summary>
        /// The one live GameObject with that name, or null and a sentence saying why. Zero and
        /// more-than-one are both refusals, and the ambiguous one prints the offenders: picking the
        /// first match is exactly the silent guess §39.2 forbids.
        /// </summary>
        internal static GameObject Find(string name, out string why)
        {
            if (!Enabled)
            {
                why = "the dev scan is OFF - run 'ct_scan on' first (name: targets are dev mode only, FINAL-PLAN 39.3)";
                return null;
            }

            GameObject hit = null;
            int n = 0;
            StringBuilder paths = new StringBuilder();
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (!go.scene.IsValid()) continue;               // a loaded asset, not something on screen
                if (!string.Equals(go.name, name, StringComparison.Ordinal)) continue;
                n++;
                if (hit == null) hit = go;
                if (n <= 4) paths.Append(paths.Length == 0 ? "" : "; ").Append(FullPath(go.transform));
            }

            if (n == 0) { why = "the dev scan found no live GameObject named '" + name + "'"; return null; }
            if (n > 1)
            {
                why = "the dev scan found " + n + " live GameObjects named '" + name + "' - ambiguous, nothing was written (" +
                      paths + (n > 4 ? "; ..." : "") + ")";
                return null;
            }
            why = null;
            return hit;
        }

        private static string FullPath(Transform t)
        {
            List<string> parts = new List<string>();
            for (Transform c = t; c != null; c = c.parent) parts.Add(c.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        // ------------------------------------------------------------------------------- gate R4

        /// <summary>
        /// Gate R4 (FINAL-PLAN 39.7, Task 26), one run, through the REAL `ct_replace` command:
        ///   R4-off        a `name:` record with the scan OFF is refused and writes nothing;
        ///   R4-on         the same record with the scan ON is applied and the object wears ours;
        ///   R4-ambiguous  a name two live objects share is refused with both printed, scan ON;
        ///   R4-revert     ct_revert puts the origin OBJECT back, by reference;
        ///   R4-CONTROL    a second live renderer, named by no record, unchanged at all four points.
        /// Anything the live scene cannot supply is VOID, never PASS - a gate that could not run must
        /// not look like one that ran.
        /// </summary>
        private static string Gate()
        {
            try { return RunGate(); }
            finally { Enabled = false; }   // the toggle goes back to its default, always
        }

        private static string RunGate()
        {
            Enabled = false;

            // Population first: uniqueness has to be measured over exactly what Find() searches.
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
                if (go.scene.IsValid())
                {
                    int c;
                    counts[go.name] = counts.TryGetValue(go.name, out c) ? c + 1 : 1;
                }

            Renderer subject = null, control = null;
            int slot = 0;
            Texture2D subjectTex = null, controlTex = null;
            foreach (Renderer r in Resources.FindObjectsOfTypeAll<Renderer>())
            {
                if (!r.gameObject.scene.IsValid()) continue;
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].HasProperty("_MainTex")) continue;
                    Texture2D t = mats[i].GetTexture("_MainTex") as Texture2D;
                    if (t == null) continue;

                    int c;
                    // The subject's name must be UNIQUE (that is the whole mechanism) and typeable:
                    // the console splits arguments on whitespace, so a spaced name is unusable by hand.
                    bool unique = counts.TryGetValue(r.gameObject.name, out c) && c == 1 &&
                                  r.gameObject.name.IndexOf(' ') < 0 && r.gameObject.name.IndexOf('@') < 0;
                    if (subject == null && unique) { subject = r; slot = i; subjectTex = t; }
                    else if (control == null && !ReferenceEquals(r, subject)) { control = r; controlTex = t; }
                }
                if (subject != null && control != null) break;
            }

            string dupName = null;
            foreach (KeyValuePair<string, int> kv in counts)
                if (kv.Value > 1 && kv.Key.IndexOf(' ') < 0 && kv.Key.IndexOf('@') < 0 && kv.Key.Length > 0)
                { dupName = kv.Key; break; }

            if (subject == null) return "R4 VOID - no live scene renderer has a uniquely named GameObject with a _MainTex Texture2D (scanned " + counts.Count + " distinct names)";
            if (control == null) return "R4 VOID - only one live renderer with a _MainTex; the gate REFUSES to run without its control";
            if (dupName == null) return "R4 VOID - no name is shared by two live GameObjects, so the ambiguity refusal cannot be exercised";

            string png = WriteMagentaPng();
            if (png == null) return "R4 VOID - could not write the replacement png";

            string target = "name:" + subject.gameObject.name + "#@Renderer.materials[" + slot + "].tex:_MainTex";
            string ambiguous = "name:" + dupName + "#@Renderer.materials[0].tex:_MainTex";

            StringBuilder log = new StringBuilder();
            int failures = 0;
            log.AppendLine("ct_scan gate subject='" + subject.gameObject.name + "' (unique) materials[" + slot +
                           "] _MainTex='" + subjectTex.name + "' " + subjectTex.width + "x" + subjectTex.height);
            log.AppendLine("  CONTROL renderer='" + control.gameObject.name + "' _MainTex='" + controlTex.name +
                           "'; ambiguity control name='" + dupName + "' shared by " + counts[dupName] + " live objects");

            string off = SeamSwap.Replace(new[] { target, png });
            failures += Check(log, "R4-off", off.StartsWith("ct_replace REFUSED") && off.IndexOf("scan is OFF", StringComparison.Ordinal) >= 0 &&
                                             ReferenceEquals(Bound(subject, slot), subjectTex),
                              "scan OFF -> " + First(off));

            Enabled = true;
            string on = SeamSwap.Replace(new[] { target, png });
            Texture2D now = Bound(subject, slot);
            failures += Check(log, "R4-on", on.StartsWith("ct_replace applied") && now != null &&
                                            !ReferenceEquals(now, subjectTex) && now.name == "ct_replacement",
                              "scan ON -> " + First(on) + "; the renderer now shows '" + (now == null ? "(null)" : now.name) + "'");

            string amb = SeamSwap.Replace(new[] { ambiguous, png });
            failures += Check(log, "R4-ambiguous", amb.StartsWith("ct_replace REFUSED") && amb.IndexOf("ambiguous", StringComparison.Ordinal) >= 0,
                              First(amb));

            failures += Check(log, "R4-CONTROL-during", ReferenceEquals(Bound(control, IndexOf(control, controlTex)), controlTex),
                              "control still shows '" + controlTex.name + "' while the swap is live");

            log.AppendLine(SeamSwap.Revert());
            failures += Check(log, "R4-revert", ReferenceEquals(Bound(subject, slot), subjectTex),
                              "materials[" + slot + "]._MainTex is the origin OBJECT again");
            failures += Check(log, "R4-CONTROL-after", ReferenceEquals(Bound(control, IndexOf(control, controlTex)), controlTex),
                              "control unchanged after revert");

            log.Append(failures == 0 ? "ct_scan: R4 PASS (the scan reaches a target no guid: anchor names, and only while it is ON)"
                                     : "ct_scan: " + failures + " FAILURE(S)");
            return log.ToString();
        }

        private static Texture2D Bound(Renderer r, int i)
        {
            Material[] mats = r == null ? null : r.sharedMaterials;
            if (mats == null || i < 0 || i >= mats.Length || mats[i] == null) return null;
            return mats[i].GetTexture("_MainTex") as Texture2D;
        }

        /// <summary>Which slot of this renderer holds that texture - the control is compared where it lives.</summary>
        private static int IndexOf(Renderer r, Texture2D t)
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                if (mats[i] != null && mats[i].HasProperty("_MainTex") && ReferenceEquals(mats[i].GetTexture("_MainTex"), t)) return i;
            return -1;
        }

        private static string First(string s)
        {
            int nl = s.IndexOf('\n');
            return nl < 0 ? s : s.Substring(0, nl);
        }

        private static int Check(StringBuilder log, string gate, bool ok, string detail)
        {
            log.AppendLine(gate + (ok ? " PASS " : " FAIL ") + detail);
            return ok ? 0 : 1;
        }

        /// <summary>
        /// ct_replace takes a FILE, so the gate hands it one: driving the real command is the point,
        /// a private write path would prove a second implementation instead of the shipped one.
        /// </summary>
        private static string WriteMagentaPng()
        {
            try
            {
                Texture2D t = new Texture2D(8, 8, TextureFormat.RGBA32, false);
                Color32[] px = new Color32[64];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 0, 255, 255);
                t.SetPixels32(px);
                t.Apply();
                byte[] bytes = t.EncodeToPNG();
                UnityEngine.Object.Destroy(t);

                string dir = Path.Combine(Application.persistentDataPath, "ContentTool");
                Directory.CreateDirectory(dir);
                string path = Path.Combine(dir, "ct_scan_gate.png");
                File.WriteAllBytes(path, bytes);
                return path;
            }
            catch (Exception) { return null; }
        }
    }
}
