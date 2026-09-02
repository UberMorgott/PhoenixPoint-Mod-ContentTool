using System;
using System.Collections.Generic;
using Morgott.ContentTool.Doctor;

/// <summary>
/// THE CLIP CATALOGUE (design §7), decided offline. Slice 0(d) measured the trap it exists for:
/// <c>Crabman_AnimActionsDef</c> ships <c>AnimActions.Count == 0</c> with no default action or
/// reaction clip, while <c>Soldier_Utka_AnimActionsDef</c> carries 177 - so a preview that reads only
/// the anim-actions def shows an EMPTY clip list for a Crabman. And a controller lists the same clip
/// more than once: <c>HumanoidAnimatorLOC</c> 73 entries / 69 distinct, <c>MidMonsterAnimator</c>
/// 60 / 45. The whole rule is string work, so it is proven here rather than by standing a prototype
/// on the bench and counting rows.
/// </summary>
internal static class OverlayTests
{
    internal static string Run()
    {
        int checks = 0;
        PrototypeCatalog.ClipSource source;

        // ---- 1. A def that HAS actions answers, and answers once per name.
        IList<string> actions = PrototypeCatalog.ResolveClips(
            new[] { "idle", "walk", "idle" }, new[] { "c1" }, out source);
        checks += Check(actions.Count == 2 && actions[0] == "idle" && actions[1] == "walk" &&
                        source == PrototypeCatalog.ClipSource.AnimActions,
                        "a non-empty anim-actions list wins and is deduplicated");

        // ---- 2. THE CRABMAN CASE: no actions at all, so the rig's own controller answers - at the
        // two shapes slice 0 measured, duplicates and all.
        IList<string> human = PrototypeCatalog.ResolveClips(new string[0], Controller(69, 4), out source);
        bool ordered = human.Count == 69 && human[0] == "Clip00" && human[68] == "Clip68";
        PrototypeCatalog.ClipSource humanSource = source;
        IList<string> mutog = PrototypeCatalog.ResolveClips(new string[0], Controller(45, 15), out source);
        checks += Check(ordered && mutog.Count == 45 &&
                        humanSource == PrototypeCatalog.ClipSource.Controller &&
                        source == PrototypeCatalog.ClipSource.Controller,
                        "an empty def falls back to the controller: 73 -> 69 and 60 -> 45, first-seen order kept");

        // ---- 3. Neither list has anything. A rig-less variant is a normal state, not a fault.
        IList<string> none = PrototypeCatalog.ResolveClips(new string[0], new string[0], out source);
        checks += Check(none.Count == 0 && source == PrototypeCatalog.ClipSource.None,
                        "no clips anywhere is an empty list and ClipSource.None, never a throw");

        // ---- 4. Called off LIVE game data, where either side is routinely null.
        IList<string> nulls = PrototypeCatalog.ResolveClips(null, null, out source);
        bool bothNull = nulls.Count == 0 && source == PrototypeCatalog.ClipSource.None;
        IList<string> nullActions = PrototypeCatalog.ResolveClips(null, new[] { "a", null, "a" }, out source);
        checks += Check(bothNull && nullActions.Count == 1 && nullActions[0] == "a" &&
                        source == PrototypeCatalog.ClipSource.Controller,
                        "null behaves exactly as empty, on either side and inside the list");

        // ---- 5. ORDINAL dedup. Two Unity clips may differ only in case, and the transport looks
        // them up by name - folding them together would hide one of them for good.
        IList<string> cased = PrototypeCatalog.ResolveClips(new[] { "Idle", "idle", "Idle" }, null, out source);
        checks += Check(cased.Count == 2 && cased[0] == "Idle" && cased[1] == "idle" &&
                        source == PrototypeCatalog.ClipSource.AnimActions,
                        "'Idle' and 'idle' are two clips - dedup is ordinal");

        return "OVERLAY PASS, " + checks + " check(s)";
    }

    /// <summary>A controller listing <paramref name="distinct"/> clips of which
    /// <paramref name="repeats"/> are listed a second time, the repeat always of a name already seen -
    /// so a correct dedup leaves the distinct names in plain order.</summary>
    private static IList<string> Controller(int distinct, int repeats)
    {
        var names = new List<string>();
        for (int i = 0; i < distinct; i++)
        {
            names.Add("Clip" + i.ToString("00"));
            if (names.Count - i - 1 < repeats) names.Add("Clip00");
        }
        return names;
    }

    private static int Check(bool ok, string what)
    {
        if (!ok) throw new Exception("OVERLAY FAILURE: " + what);
        return 1;
    }
}
