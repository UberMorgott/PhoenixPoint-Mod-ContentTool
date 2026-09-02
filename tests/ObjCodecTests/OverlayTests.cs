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

        // ---- 6. EXT_ is a skip the GAME itself makes (Addon.cs:1208), so an attachment point is never
        // a defect and never green - whatever an alias, a file joint or the missing list says about it.
        var loud = new Dictionary<string, string>(StringComparer.Ordinal) { { "j", "EXT_VoiceContext" } };
        checks += Check(BoneOverlay.Classify("EXT_VoiceContext", new[] { "EXT_VoiceContext" }, loud,
                                             new HashSet<string>(StringComparer.Ordinal) { "EXT_VoiceContext" },
                                             true) == BoneStatus.Attachment,
                        "an EXT_ attachment point is grey whatever else claims it");

        // ---- 7. The author's explicit mapping outranks a coincidence of names.
        var mapped = new Dictionary<string, string>(StringComparer.Ordinal) { { "Bip_Head", "Head" } };
        checks += Check(BoneOverlay.Classify("Head", new[] { "Head" }, mapped, null, false) == BoneStatus.Alias,
                        "a bone an alias points at is yellow even when a file joint carries its name");

        // ---- 8. DECORATION-INSENSITIVE: '#Root_Addon => Def' and 'Root' are ONE bone to the binder,
        // which is the same rule ModelDoctor.Suggest matches on.
        checks += Check(BoneOverlay.Classify("Root", new[] { "#Root_Addon => PX_Heavy_Torso_BodyPartDef" },
                                             null, null, false) == BoneStatus.ByName &&
                        BoneOverlay.Classify("Root", new[] { "Pelvis" }, null, null, false)
                            == BoneStatus.Unmatched,
                        "a file joint matches through SkinBinder.Plain, and a different name does not");

        // ---- 9. The report's own MissingBone subjects, coloured by which bind the verdict took.
        var missing = new HashSet<string>(StringComparer.Ordinal) { "L_Hand" };
        checks += Check(BoneOverlay.Classify("L_Hand", null, null, missing, true) == BoneStatus.Nearest &&
                        BoneOverlay.Classify("L_Hand", null, null, missing, false) == BoneStatus.Unmatched,
                        "an unmatched target bone is blue under a nearest-bone bind and red otherwise");

        // ---- 10. THE PICK: the closest joint inside the radius, nothing outside it, and a tie that
        // does not flicker - a pick that alternates between two overlapping joints is one nobody can make.
        int hit;
        bool near = BoneOverlay.Nearest(10f, 10f, new[] { 40f, 12f }, new[] { 10f, 10f },
                                        new[] { true, true }, 12f, out hit) && hit == 1;
        bool far = !BoneOverlay.Nearest(10f, 10f, new[] { 40f }, new[] { 10f }, new[] { true }, 12f, out hit)
                   && hit == -1;
        bool tie = BoneOverlay.Nearest(10f, 10f, new[] { 14f, 6f }, new[] { 10f, 10f },
                                       new[] { true, true }, 12f, out hit) && hit == 0;
        checks += Check(near && far && tie,
                        "the pick takes the closest joint inside the radius, and a tie takes the lowest index");

        // ---- 11. Every one of these arrives from a LIVE camera, so none of them may throw.
        bool empty = !BoneOverlay.Nearest(10f, 10f, new float[0], new float[0], new bool[0], 12f, out hit);
        bool nanCursor = !BoneOverlay.Nearest(float.NaN, 10f, new[] { 10f }, new[] { 10f },
                                              new[] { true }, 12f, out hit);
        bool nanPoint = !BoneOverlay.Nearest(10f, 10f, new[] { float.NaN }, new[] { 10f },
                                             new[] { true }, 12f, out hit);
        checks += Check(empty && nanCursor && nanPoint && hit == -1,
                        "an empty array, a NaN cursor and a NaN joint are each a miss, never a throw");

        // ---- 12. CLICK-TO-ALIAS ELIGIBILITY, read off the SAME status the bone was coloured by: a
        // joint nothing claims takes the armed row, and so does one only the nearest-bone fallback
        // reached - those two ARE the report's unanswered target bones, which is what the bone map's
        // own dropdown offers.
        checks += Check(BoneOverlay.CanAlias("L_Hand", BoneStatus.Unmatched, null, "hand_L") == AliasRefusal.Ok &&
                        BoneOverlay.CanAlias("L_Hand", BoneStatus.Nearest, null, "hand_L") == AliasRefusal.Ok,
                        "an unmatched or nearest-bound target bone takes the armed alias row");

        // ---- 13. An EXT_ attachment point is skipped by the game itself (Addon.cs:1208), so binding
        // weights to one is a mapping that can never do anything.
        checks += Check(BoneOverlay.CanAlias("EXT_VoiceContext", BoneStatus.Attachment, null, "hand_L")
                        == AliasRefusal.Attachment,
                        "an EXT_ attachment point is refused, not silently accepted");

        // ---- 14. A bone a file joint already reaches BY NAME is the PlainCollision the binder refuses -
        // two file bones on one game bone - so it is never assignable.
        checks += Check(BoneOverlay.CanAlias("Root", BoneStatus.ByName, null, "hand_L") == AliasRefusal.BoundByName,
                        "a bone already bound by name to a file joint is refused");

        // ---- 15. ALIAS OVER ALIAS: another row's target is claimed, but the armed row's OWN target is
        // just a re-pick of what it already says - which must not be refused, or a row cannot be
        // confirmed from the model at all.
        var map = new Dictionary<string, string>(StringComparer.Ordinal) { { "hand_R", "R_Hand" } };
        checks += Check(BoneOverlay.CanAlias("R_Hand", BoneStatus.Alias, map, "hand_L") == AliasRefusal.Claimed &&
                        BoneOverlay.CanAlias("R_Hand", BoneStatus.Alias, map, "hand_R") == AliasRefusal.Ok,
                        "another row's alias target is claimed, and the armed row's own target is not");

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
