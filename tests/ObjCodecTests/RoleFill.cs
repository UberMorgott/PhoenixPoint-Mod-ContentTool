using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Tactical;

/// <summary>
/// THE SAFETY NET FOR A HALF-MAPPED CREATURE, offline: a mod that maps some roles and forgets others
/// must still get a clip in every role the game BLOCKS on, and must still get NOTHING in the roles the
/// engine degrades on its own.
///
/// Three failures this arm exists to catch, each of which is silent in a bake log and expensive in game:
///  * a blocking role drifts into the "leave it empty" class - the donor's clip stays in the slot, names
///    none of our bones, and the creature FREEZES mid-battle (CreatureBuild.cs:1169-1172);
///  * the substitute comes back null while the model does ship clips - the same freeze, plus a 10 s
///    AnimEventReceiver timeout per blocking event (AnimEventReceiver.cs:100,126);
///  * a traversal role gets "helpfully" filled - a FILLED traversal slot is a promise the engine trusts,
///    and a flat walk can never satisfy a vertical segment, so the creature hangs half way up a wall.
///
/// The last arm is the falsification of the "byte-for-byte unchanged" claim: the shipped CustomCreature
/// demo maps every role in the fill class itself, so the auto-fill has nothing to do for it.
/// </summary>
internal static class RoleFill
{
    private static int checks;

    internal static string Run()
    {
        checks = 0;

        // ---- the two classes are a PARTITION, so a role can never be added to one and forgotten
        var seen = new HashSet<string>();
        foreach (string role in CreatureRoles.All)
            Check(seen.Add(role), "'" + role + "' is named once - a role in both classes would be " +
                  "filled and cleared by the same build");
        Check(CreatureRoles.All.Length == CreatureRoles.Fill.Length + CreatureRoles.Empty.Length,
              "every role is in exactly one class");
        foreach (string role in CreatureRoles.Empty)
            Check(Array.IndexOf(CreatureRoles.Fill, role) < 0,
                  "'" + role + "' is left empty, so it must not also be filled");

        // ---- A ROLE THE GAME BLOCKS ON MAY NEVER BE LEFT EMPTY
        foreach (KeyValuePair<string, CreatureRoles.Event[]> b in CreatureRoles.Blocking)
        {
            Check(Array.IndexOf(CreatureRoles.Fill, b.Key) >= 0, "'" + b.Key + "' waits for " +
                  "blocking animation events, so it belongs to the fill class - an unfilled one is a " +
                  "10s AnimEventReceiver timeout per event (AnimEventReceiver.cs:100,126)");
            Check(b.Value.Length > 0, "'" + b.Key + "' names the events it blocks on");
            float last = -1f;
            foreach (CreatureRoles.Event e in b.Value)
            {
                Check(!string.IsNullOrEmpty(e.Name), "'" + b.Key + "' names every event it waits for");
                // The ORDER is load-bearing: each wait is registered only after the previous one
                // returned, so two events sharing a timestamp are not two events.
                Check(e.At > last && e.At > 0f && e.At <= 1f, "'" + b.Key + "." + e.Name + "' fires at " +
                      e.At + " - a fraction of the clip, strictly after the event before it");
                last = e.At;
            }
        }
        Check(Named(CreatureRoles.BlockingFor("attack")) == "ActionDo,ShootShot,ActionEnd",
              "the Action state waits for ActionDo, ShootShot, ActionEnd in that order " +
              "(TacticalAbility.cs:1206,1214, BashAbility.cs:465)");
        Check(Named(CreatureRoles.BlockingFor("death")) == "Ragdoll",
              "the Die state waits for Ragdoll (RagdollDieAbility.cs:95)");
        Check(CreatureRoles.BlockingFor("walk").Length == 0,
              "locomotion blocks on the CLIP, not on an event, so nothing is stamped on it");

        // ---- A MOD THAT MAPPED ALMOST NOTHING still gets a clip in every blocking role
        var one = new Dictionary<string, string> { { "walk", "Beast_Walk" } };
        string[] known = { "Beast_Walk", "Beast_Something" };
        foreach (string role in CreatureRoles.Fill)
        {
            if (one.ContainsKey(role)) continue;
            string sub = CreatureRoles.Substitute(r => Get(one, r), known);
            Check(sub == "Beast_Walk", "'" + role + "' is unmapped and takes the one clip the mod DID " +
                  "map ('" + sub + "') - a stand-in that looks wrong still leaves a creature that plays");
        }
        // The IDLE is the default the code already fell back to (CreatureBuild.cs:1007,1175).
        var two = new Dictionary<string, string> { { "walk", "Beast_Walk" }, { "idle", "Beast_Idle" } };
        Check(CreatureRoles.Substitute(r => Get(two, r), known) == "Beast_Idle",
              "the mapped idle wins - it is the fallback the wiring already used for a role with no clip");
        // Nothing mapped at all, but the model ships clips: the first one, never null.
        Check(CreatureRoles.Substitute(r => null, known) == "Beast_Walk",
              "a mod that mapped NO role still gets the model's own first clip, not the donor's - the " +
              "donor's names none of our bones and plays as a freeze");
        // A model with no animation whatsoever cannot be helped, and says so instead of pretending.
        Check(CreatureRoles.Substitute(r => null, new string[0]) == null,
              "a model that ships no clip at all is reported UNRESOLVED, not silently filled");

        // ---- THE SHIPPED DEMO IS UNTOUCHED BY ALL OF THIS
        string demo = Path.Combine(Root(), "demos\\CustomCreature\\ppcontent.json");
        string json = File.ReadAllText(demo);
        foreach (string role in CreatureRoles.Fill)
            Check(json.Contains("\": \"" + role + "\""), "the CustomCreature demo maps '" + role +
                  "' itself, so the auto-fill adds nothing to it and its overrides are what they were");

        return "ROLE-FILL: ALL PASS, " + checks + " check(s)";
    }

    private static string Get(Dictionary<string, string> map, string role)
    {
        string clip;
        return map.TryGetValue(role, out clip) ? clip : null;
    }

    private static string Named(CreatureRoles.Event[] events)
    {
        var names = new List<string>();
        foreach (CreatureRoles.Event e in events) names.Add(e.Name);
        return string.Join(",", names.ToArray());
    }

    /// <summary>The repo's ContentTool root, from the test binary's own location.</summary>
    private static string Root()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "demos")))
            dir = Path.GetDirectoryName(dir.TrimEnd('\\'));
        if (dir == null) throw new Exception("ContentTool root not found above " +
                                             AppDomain.CurrentDomain.BaseDirectory);
        return dir;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("Check failed: " + name);
        checks++;
    }
}
