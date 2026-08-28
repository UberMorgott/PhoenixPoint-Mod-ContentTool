using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Base.Core;
using Base.Serialization;
using Base.Utils;
using PhoenixPoint.Common.Game;
using PhoenixPoint.Tactical.Entities;
using PhoenixPoint.Tactical.Levels;
using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// Gate M1 - the dev workbench's replacement seam MEASURED AT MISSION SCALE.
    ///
    /// Every seam number this project has was taken on the roster (main menu / character screen): a
    /// handful of rigged renderers, no scene transition, no actor spawn. A tactical mission loads a
    /// different scene through a different path, so nothing about the seam there was known - row 21 of
    /// PROVEN-FOUNDATIONS says so in as many words.
    ///
    /// Reaching a mission without a human is the whole difficulty, and the game already ships the
    /// lever: <c>load_game</c> (Base.Serialization\SerializationCommands.cs:41) hands a savegame's
    /// metadata to <see cref="PhoenixGame.FinishLevelAndLoadGame"/>, and
    /// <see cref="PPSavegameMetaData.IsTacticalSave"/> says - in the save's own metadata, not in a
    /// guess of ours - whether that save is inside a mission. So the gate names a save, refuses
    /// anything that does not POSITIVELY declare itself tactical, loads it, and only then measures.
    ///
    /// Dev-only by construction: this drives the dev seam and nothing here can reach a baked mod
    /// (<c>ReplacementSet.Bakeable</c> refuses scan/dev records, and route vii has no runtime at all).
    /// </summary>
    internal static class MissionGate
    {
        private const float LoadBudgetSeconds = 420f;

        internal static string Run(string[] args)
        {
            string cmd = args != null && args.Length > 0 ? args[0].ToLowerInvariant() : "list";
            switch (cmd)
            {
                case "list": return Arm(null);
                case "gate":
                    if (args.Length < 2) return "usage: ct_mission gate <savename>   (ct_mission list prints the names)";
                    // Names, never paths - and a name may contain spaces, so the rest of the line is it.
                    return Arm(string.Join(" ", args.Skip(1).ToArray()));
                default: return "usage: ct_mission [list | gate <savename>]";
            }
        }

        private static string Arm(string saveName)
        {
            if (saveName != null && !SeamSwap.Active)
                return "ct_mission REFUSED: the seam is OFF - run 'ct_seamprobe on' BEFORE the mission " +
                       "loads, or the postfix misses every resolve the load makes and the run measures nothing";

            GameObject go = new GameObject("ct_mission");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<Runner>().Begin(saveName);
            return saveName == null
                ? "ct_mission list armed - savegame enumeration is a game coroutine; the rows print from the runner"
                : "M1 armed on save '" + saveName + "' - the arms print from the runner once the mission is live";
        }

        /// <summary>
        /// Asynchronous by necessity, twice over: enumerating savegames is a game-Timing coroutine, and
        /// loading a mission takes minutes of player loop. <see cref="AsyncGate"/> holds ct_autorun's
        /// DONE until the arms have printed, or autogate would kill the game mid-load and a run that
        /// measured nothing would read like a run that measured everything.
        /// </summary>
        private sealed class Runner : MonoBehaviour
        {
            private string saveName;
            private int rosterInScene;
            private string refusal;
            private bool listed;

            internal void Begin(string name)
            {
                saveName = name;
                AsyncGate.Pending++;
                StartCoroutine(name == null ? (IEnumerator)List() : Gate());
            }

            // ------------------------------------------------------------------ ct_mission list

            private IEnumerator List()
            {
                StringBuilder log = new StringBuilder();
                GameUtl.GameComponent<TimeSource>().Timing.Start(Enumerate(log));
                float start = Time.realtimeSinceStartup;
                while (!listed && Time.realtimeSinceStartup - start < 60f) yield return null;
                if (!listed) log.AppendLine("ct_mission list VOID - the savegame enumeration never returned");
                ContentToolMain.Say(log.ToString().TrimEnd());
                AsyncGate.Pending--;
                Destroy(gameObject);
            }

            private IEnumerator<NextUpdate> Enumerate(StringBuilder log)
            {
                ByRef<List<SavegameMetaData>> all = new ByRef<List<SavegameMetaData>>();
                yield return Timing.Current.Call(
                    GameUtl.GameComponent<SerializationComponent>().GetSavegames(all, true));
                log.AppendLine("ct_mission list: " + (all.Value == null ? 0 : all.Value.Count) + " savegame(s)");
                foreach (SavegameMetaData m in all.Value ?? new List<SavegameMetaData>())
                {
                    PPSavegameMetaData pp = m as PPSavegameMetaData;
                    log.AppendLine("  '" + m.Name + "' v." + m.Version +
                                   " tactical=" + (pp == null ? "(not a PP save)" : pp.IsTacticalSave.ToString()) +
                                   (pp == null ? "" : " saveType=" + pp.SaveType + " loadable=" + pp.IsLoadable()));
                }
                listed = true;
            }

            // ------------------------------------------------------------------ ct_mission gate

            private IEnumerator Gate()
            {
                StringBuilder log = new StringBuilder();
                int fail = 0;
                bool measured = false;
                try
                {
                    // The same-run baseline. Whatever scale this is, it is the scale every previous seam
                    // number was taken at, and the gate has to prove it LEFT it.
                    rosterInScene = InSceneRigged();
                    log.AppendLine("M1-baseline roster scale before the load: inSceneRiggedRenderers=" + rosterInScene);

                    GameUtl.GameComponent<TimeSource>().Timing.Start(Load(saveName));

                    float start = Time.realtimeSinceStartup;
                    TacticalLevelController tac = null;
                    int actors = 0;
                    while (Time.realtimeSinceStartup - start < LoadBudgetSeconds)
                    {
                        if (refusal != null) break;
                        tac = CurrentTactical();
                        actors = tac == null ? 0 : CountActors(tac);
                        if (tac != null && tac.TacMission != null && actors > 0) break;
                        yield return new WaitForSeconds(1f);
                    }

                    // Everything below is VOID, never PASS, when the mission never came up: a gate that
                    // could not answer must not read like one that answered.
                    if (refusal != null)
                    {
                        log.AppendLine("M1 VOID " + refusal);
                        yield break;
                    }
                    if (tac == null || tac.TacMission == null || actors == 0)
                    {
                        log.AppendLine("M1 VOID no tactical mission became live within " + LoadBudgetSeconds +
                                       "s of loading '" + saveName + "' (level=" +
                                       (GameUtl.CurrentLevel() == null ? "(none)" : GameUtl.CurrentLevel().name) +
                                       " actors=" + actors + ") - nothing was measured at mission scale");
                        yield break;
                    }

                    // Settle: actors keep arriving for a few seconds after the first one exists, and the
                    // coverage ratio is taken against whatever is in the scene at report time.
                    yield return new WaitForSeconds(5f);
                    measured = true;
                    int missionInScene = InSceneRigged();
                    actors = CountActors(tac);

                    // ARM 1 - positive identity, of the mission itself. Not "we are not on the roster":
                    // a named live TacMission, a named map, and actors that exist.
                    fail += Check(log, "M1-in-mission",
                        tac.TacMission != null && actors > 0,
                        "GameUtl.CurrentLevel() is TacticalLevelController, mission=" + tac.TacMission +
                        " turn=" + tac.TurnNumber + " factions=" + tac.Factions.Count + " actors=" + actors);

                    // ARM 2 - the scale actually changed, measured in the same run against the baseline
                    // this runner recorded before the load. This is the arm that makes the whole run
                    // mission-scale rather than a claim that it is.
                    fail += Check(log, "M1-scale", missionInScene > rosterInScene,
                        "inSceneRiggedRenderers roster=" + rosterInScene + " -> mission=" + missionInScene);

                    // ARM 3 - the seam, at that scale. The report is the instrument that settled rows
                    // 18-21; the only thing new here is WHERE it is taken.
                    log.AppendLine("M1-seam report at mission scale:");
                    log.AppendLine(SeamSwap.Run(new[] { "report" }));

                    // ARM 4/5 - the seam does not merely see, it still WRITES and REVERTS here. R2/R3
                    // pick their own subject out of this scene and carry their own controls.
                    log.AppendLine("M1-texswap (gate R2, mission scale):");
                    log.AppendLine(SeamSwap.TexSwapGate());
                    log.AppendLine("M1-meshswap (gate R3, mission scale):");
                    log.AppendLine(SeamSwap.MeshSwapGate());
                }
                finally
                {
                    // Zero failures because zero arms ran is NOT a pass. The falsification run found this
                    // the hard way: with the load disarmed the gate printed M1 VOID and then signed off
                    // "arms PASS", which is exactly the shape this project bans.
                    log.Append(!measured ? "ct_mission: M1 VOID - no arm ran, nothing was measured at mission scale"
                               : fail == 0 ? "ct_mission: M1 arms PASS" : "ct_mission: M1 " + fail + " FAILURE(S)");
                    ContentToolMain.Say(log.ToString());
                    AsyncGate.Pending--;
                    Destroy(gameObject);
                }
            }

            /// <summary>
            /// What <c>load_game</c> does, minus the console: find the save BY NAME, refuse anything that
            /// does not positively declare itself tactical, then hand it to the game's own loader.
            /// </summary>
            private IEnumerator<NextUpdate> Load(string name)
            {
                ByRef<List<SavegameMetaData>> all = new ByRef<List<SavegameMetaData>>();
                yield return Timing.Current.Call(
                    GameUtl.GameComponent<SerializationComponent>().GetSavegames(all));

                List<SavegameMetaData> hits = (all.Value ?? new List<SavegameMetaData>())
                    .Where(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)).ToList();
                if (hits.Count == 0)
                {
                    refusal = "REFUSED: no savegame named '" + name + "' (run 'ct_mission list')";
                    yield break;
                }
                if (hits.Count > 1)
                {
                    // Ambiguity is refused and the offenders are printed - BundleBaker.FindUnique's rule.
                    refusal = "REFUSED: " + hits.Count + " savegames answer to '" + name + "': " +
                              string.Join(", ", hits.Select(h => h.Path).ToArray());
                    yield break;
                }

                PPSavegameMetaData pp = hits[0] as PPSavegameMetaData;
                if (pp == null)
                {
                    refusal = "REFUSED: '" + name + "' carries no PPSavegameMetaData, so nothing says it is tactical";
                    yield break;
                }
                if (!pp.IsTacticalSave)
                {
                    refusal = "REFUSED: '" + name + "' declares IsTacticalSave=False (saveType=" + pp.SaveType +
                              ") - loading it lands on the geoscape and the run would measure the wrong scale";
                    yield break;
                }
                if (!pp.IsLoadable())
                {
                    refusal = "REFUSED: '" + name + "' declares saveType=" + pp.SaveType + " (not loadable)";
                    yield break;
                }

                ContentToolMain.Say("ct_mission: loading tactical save '" + pp.Name + "' v." + pp.Version +
                                    " (IsTacticalSave=True, saveType=" + pp.SaveType + ")");
                GameUtl.GameComponent<PhoenixGame>().FinishLevelAndLoadGame(pp);
            }

            /// <summary>
            /// The live tactical brain, or null. `Level` is the scene wrapper (Base.Levels\Level.cs:17)
            /// and the controller is a component ON it, so this is a GetComponent, not a cast.
            /// </summary>
            private static TacticalLevelController CurrentTactical()
            {
                Base.Levels.Level lvl = GameUtl.CurrentLevel();
                return lvl == null ? null : lvl.GetComponent<TacticalLevelController>();
            }

            private static int CountActors(TacticalLevelController tac)
            {
                int n = 0;
                foreach (TacticalFaction f in tac.Factions)
                {
                    IEnumerable<TacticalActorBase> actors = null;
                    try { actors = f.Actors; } catch { }        // the map is not queryable mid-load
                    if (actors == null) continue;
                    foreach (TacticalActorBase a in actors) if (a != null) n++;
                }
                return n;
            }

            /// <summary>The scan control's own population - the denominator of R1 COVERAGE.</summary>
            private static int InSceneRigged()
            {
                int n = 0;
                foreach (SkinnedMeshRenderer r in Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>())
                    if (r.gameObject.scene.IsValid()) n++;
                return n;
            }

            private static int Check(StringBuilder log, string arm, bool ok, string detail)
            {
                log.AppendLine(arm + (ok ? " PASS " : " FAIL ") + detail);
                return ok ? 0 : 1;
            }
        }
    }
}
