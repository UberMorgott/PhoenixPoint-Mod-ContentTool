using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Import;

/// <summary>
/// The two things a REAL author's file does that the U8/U9 probe never did, and that broke the bake:
///
///  - <b>an animation the bake has nowhere to put.</b> A clip whose channels are all blend-shape
///    weights, or all drive nodes outside the armature, is read back with ZERO tracks by design
///    (<c>GlbReader</c> counts the loss into <c>LossyReason</c>). <c>ClipFields.Bindings</c> then
///    REFUSES it - correctly, there is nothing to bind - and before this arm that refusal aborted the
///    bake of the whole model, and with it the project, over one clip the file was allowed to carry.
///    So: the plan leaves it out, keeps every other clip, and REPORTS it by name.
///  - <b>two animations with the same name.</b> glTF does not require them to be unique, and the
///    container key is lowercased, so "Walk" and "walk" collapse onto one asset name and the second
///    <c>AddAnimationClip</c> throws "duplicate asset name". So: distinct names, and the first one
///    keeps the readable spelling.
///
/// The fixture is a .glb assembled HERE, byte by byte, the way <see cref="ClipImport"/>'s hand-built
/// file is - three animations in one file, one of them unbakeable, two of them same-named. Our own
/// writer emits no animation at all, so no round trip could ever produce this file.
/// </summary>
internal static class ClipPlan
{
    private const string Model = "u9";

    internal static string Run()
    {
        var clips = new List<SampledClip>();
        BakedSkin skin = ModelBuild.From(GlbReader.Read(Hand(), clips), Model);

        // The file, as READ: three animations, the last one driving nothing of the rig.
        Assert(clips.Count == 3, "the file's three animations came back: " + clips.Count);
        Assert(clips[0].Name == "Walk" && clips[1].Name == "walk" && clips[2].Name == "Morphs",
               "and keep their own names: '" + clips[0].Name + "', '" + clips[1].Name + "', '" +
               clips[2].Name + "'");
        Assert(clips[2].Tracks.Count == 0, "the weights/non-joint animation drives no bone at all: " +
               clips[2].Tracks.Count + " track(s)");
        Assert(clips[2].LossyReason.Contains("1 channel(s) drive something that is not a bone") &&
               clips[2].LossyReason.Contains("1 blend-shape channel(s) were dropped"),
               "and the reader already says why: " + clips[2].LossyReason);

        // WHY it has to be left out rather than baked: the binding writer refuses it, and that refusal
        // is what used to take the whole project's bake down with it.
        string refusal = Refusal(clips[2], skin);
        Assert(refusal.Contains("drives no bone of the rig"),
               "binding it is REFUSED, which is the abort this plan exists to avoid: " + refusal);

        var skipped = new List<string>();
        List<KeyValuePair<string, SampledClip>> plan = ClipFields.Bakeable(Model, clips, skipped);

        // ARM 1 - the unbakeable clip costs its own line and nothing else.
        Assert(plan.Count == 2, "the OTHER two clips still bake: " + plan.Count + " planned");
        foreach (KeyValuePair<string, SampledClip> e in plan)
            Assert(e.Value.Tracks.Count > 0, "no clip in the plan is one nothing can bind: '" + e.Key + "'");
        Assert(skipped.Count == 1, "exactly one clip was left out: " + skipped.Count);
        Assert(skipped[0].Contains("'Morphs'") && skipped[0].Contains("SKIPPED") &&
               skipped[0].Contains(clips[2].LossyReason),
               "and it is REPORTED by name, with the reader's own reason: " + skipped[0]);

        // ARM 2 - two animations of the same name bake under two names, the first one readable.
        Assert(plan[0].Key == Model + "_walk", "the first clip keeps the readable name: '" + plan[0].Key + "'");
        Assert(plan[1].Key != plan[0].Key, "the same-named second clip does NOT collapse onto it: '" +
               plan[1].Key + "' vs '" + plan[0].Key + "'");
        Assert(plan[1].Key == Model + "_walk_1", "it takes its own index: '" + plan[1].Key + "'");
        // The keys are compared the way the CONTAINER compares them - lowercased, forward-slashed,
        // untrimmed of nothing - so two names that differ only in case are still a collision here.
        Assert(Key(plan[0].Key) != Key(plan[1].Key), "and they are still distinct as container keys: '" +
               Key(plan[0].Key) + "' vs '" + Key(plan[1].Key) + "'");
        Assert(plan[0].Value == clips[0] && plan[1].Value == clips[1],
               "the plan keeps the file's own clip order");

        // Anti-vacuity: a plan that indexed EVERY name would pass both arms above while making every
        // asset name unreadable, and a plan of zero clips would pass the "no collision" arm perfectly.
        Assert(plan[0].Key.IndexOf('_') == Model.Length && !plan[0].Key.EndsWith("_0", StringComparison.Ordinal),
               "the common case is not indexed at all: '" + plan[0].Key + "'");

        return "CLIP plan PASS\n  three animations in one hand-built .glb: '" + clips[0].Name + "', '" +
               clips[1].Name + "', '" + clips[2].Name + "' (" + clips[2].Tracks.Count + " track(s))\n  " +
               "planned '" + plan[0].Key + "' + '" + plan[1].Key + "', skipped: " + skipped[0] +
               "\n  " + Declared(clips, plan, skipped) + "\n  " + Playing(plan) + "\n  " + Verdict();
    }

    /// <summary>
    /// ppcontent.json's clip-name declaration, the thing that says WHICH clips loop (glTF carries no
    /// loop flag, so there is nothing in the file to infer from). Two failures this closes, both of
    /// which would otherwise be silent - the clip simply would not cycle and no line would say why:
    ///
    ///  - the author writes the name in the case their own file uses ("Walk"), or in any other case,
    ///    with whatever spacing; the match must not care.
    ///  - the author MISTYPES it. A name that matches no clip has to be REFUSED by name, beside the
    ///    names the project does carry.
    ///  - the author names a clip that IS in their file and reads back fine, but BAKES TO NOTHING
    ///    ('Morphs' here). The project arm compares against the BAKEABLE names, so this is refused -
    ///    but "no clip carries it" would be a lie about their own file and would send them hunting a
    ///    typo that is not there, so the refusal has to carry the reader's own reason instead.
    ///
    /// The names are matched against the .glb's OWN clip names, never the lowercased container key the
    /// plan derives - so the arm feeds it the names the reader really returned, out of the same file.
    /// </summary>
    private static string Declared(List<SampledClip> clips, List<KeyValuePair<string, SampledClip>> plan,
                                   List<string> skipped)
    {
        List<string> have = new List<string>();
        foreach (SampledClip c in clips) have.Add(c.Name);

        HashSet<string> names = ClipFields.Names(" walk ,MORPHS; ");
        Assert(names.Count == 2, "'  walk ,MORPHS; ' declares two names, trimmed of space and of the " +
               "empty tail: " + names.Count);
        Assert(names.Contains("Walk") && names.Contains("Morphs"),
               "and they match the file's own spelling whatever case they were typed in");
        // Anti-vacuity: a set that answered YES to everything would satisfy the line above perfectly.
        Assert(!names.Contains("Run") && !names.Contains("u9_walk"),
               "a name nobody declared is NOT in the set - including the container KEY the plan derives " +
               "('u9_walk'), which is not what an author sees or types");
        Assert(ClipFields.Names(null).Count == 0 && ClipFields.Names("").Count == 0 &&
               ClipFields.Names(" , ; ").Count == 0, "declaring nothing declares no clip");

        Assert(ClipFields.Unknown("loop", names, have) == null,
               "every declared name is carried by the file, so nothing is refused");
        string typo = ClipFields.Unknown("loop", ClipFields.Names("Walk, Wlak"), have);
        Assert(typo != null && typo.Contains("Wlak") && !typo.Contains("names Walk,"),
               "the MISTYPED name alone is refused: " + (typo ?? "(nothing was refused!)"));
        Assert(typo.Contains("Walk, walk, Morphs"),
               "and the refusal names the clips the project DOES carry, so the fix is in the message: " + typo);
        Assert(ClipFields.Unknown("loop", ClipFields.Names("Walk"), new List<string>()) != null,
               "a project with no clip at all cannot honour a declaration either");

        // What the PROJECT arm really compares against: the BAKEABLE names, not every name the file
        // carries. 'Morphs' is in the author's file and reads back fine - it simply has nothing to bake,
        // so an asset that could loop is never written and the declaration would do NOTHING.
        List<string> bakes = new List<string>();
        foreach (KeyValuePair<string, SampledClip> e in plan) bakes.Add(e.Value.Name);
        string unbakeable = ClipFields.Unknown("loop", ClipFields.Names("Morphs"), bakes, skipped);
        Assert(unbakeable != null && unbakeable.Contains("Morphs"),
               "declaring a loop on a clip that BAKES TO NOTHING is refused by name: " +
               (unbakeable ?? "(nothing was refused - the loop would silently never happen)"));
        Assert(unbakeable.Contains("IS in the file") && unbakeable.Contains(clips[2].LossyReason),
               "and the refusal carries the READER's own reason, so the author is not sent hunting a " +
               "typo that is not there: " + unbakeable);
        // Anti-vacuity 1: a name that is in NO clip must stay a plain typo. A refusal that pasted the
        // skipped reasons onto everything would satisfy the two lines above perfectly.
        string plain = ClipFields.Unknown("loop", ClipFields.Names("Wlak"), bakes, skipped);
        Assert(plain != null && !plain.Contains("IS in the file"),
               "a real typo gets no reason invented for it: " + plain);
        // Anti-vacuity 2: and the bakeable list still HONOURS a declaration, or "refuse everything"
        // would satisfy every line above.
        Assert(ClipFields.Unknown("loop", ClipFields.Names("walk"), bakes, skipped) == null,
               "a clip that really bakes is still accepted, case-blind");

        return "declaration: ' walk ,MORPHS; ' -> " + names.Count + " name(s) matched case-blind, " +
               "'Wlak' refused with the file's own names beside it, 'Morphs' refused with the reason " +
               "it bakes to nothing";
    }

    /// <summary>
    /// WHICH clip the Animator plays. Before <c>"play"</c> the AOC always got plan[0], so a model with
    /// Attack/Death/Idle/Jump/Walk animated by whichever name sorts first - the author had no say.
    ///
    /// The fixture is deliberately the awkward one: the plan's two clips are 'Walk' and 'walk', so
    /// "the first case-blind match wins" is a rule with a visible consequence rather than a detail,
    /// and the third animation ('Morphs') is one the plan LEFT OUT - naming it must not silently
    /// select clip 0.
    /// </summary>
    private static string Playing(List<KeyValuePair<string, SampledClip>> plan)
    {
        Assert(ClipFields.Chosen(plan, null) == 0 && ClipFields.Chosen(plan, "") == 0,
               "declaring nothing keeps the old behaviour exactly - the first bakeable clip");
        Assert(ClipFields.Chosen(plan, " Walk ") == 0, "a declared name picks its clip, spaces and all");
        // Anti-vacuity: an implementation that returned 0 for everything would pass both lines above.
        // The second clip is the same name in another case, so this asserts the FIRST match wins AND
        // that a non-zero index is reachable at all - through the plan's own second key.
        Assert(ClipFields.Chosen(plan, plan[1].Key) == -1,
               "the container KEY is not what an author names ('" + plan[1].Key + "')");
        var three = new List<KeyValuePair<string, SampledClip>>(plan);
        three.Add(new KeyValuePair<string, SampledClip>("u9_run", new SampledClip { Name = "Run" }));
        Assert(ClipFields.Chosen(three, "run") == 2, "and a later clip is really reachable: " +
               ClipFields.Chosen(three, "run"));
        Assert(ClipFields.Chosen(plan, "Morphs") == -1,
               "a clip the plan LEFT OUT is refused, never silently swapped for clip 0");
        Assert(ClipFields.Chosen(plan, "Wlak") == -1, "and so is a typo");

        // THE TWO SIDES OF ONE STRING, ASSERTED TOGETHER - the divergence IS the bug. "play" was parsed
        // one way by the project gate (split on , and ;) and another by the resolver (the whole raw
        // string compared as one name), so "Run, Walk" PASSED the gate - both names are carried - then
        // matched nothing and the caller fell back to clip 0. An arm that only tries single names cannot
        // see that: on those two the parsers agree by accident.
        List<string> bakes3 = new List<string>();
        foreach (KeyValuePair<string, SampledClip> e in three) bakes3.Add(e.Value.Name);

        string many = ClipFields.TooMany(ClipFields.Names("Run, Walk"));
        Assert(many != null && many.Contains("Run") && many.Contains("Walk"),
               "a \"play\" naming TWO clips the project really carries is REFUSED, by name: " +
               (many ?? "(accepted - and clip 0 would play with nothing said)"));
        Assert(ClipFields.Chosen(three, "Run, Walk") < 0,
               "and the resolver refuses to guess between them rather than falling back to clip 0: " +
               ClipFields.Chosen(three, "Run, Walk"));
        // Anti-vacuity, and the invariant itself: whatever the gate ACCEPTS, the resolver must land on a
        // real clip for. A TooMany that refused everything, or a Chosen that answered -1 to everything,
        // would satisfy the two lines above perfectly.
        foreach (string decl in new[] { null, "", " Walk ", "run", "RUN;", " Run , " })
        {
            HashSet<string> parsed = ClipFields.Names(decl);
            Assert(ClipFields.TooMany(parsed) == null &&
                   ClipFields.Unknown("play", parsed, bakes3) == null,
                   "the gate accepts '" + decl + "'");
            Assert(ClipFields.Chosen(three, decl) >= 0,
                   "so the resolver has to land on a real clip for '" + decl + "', never fall back: " +
                   ClipFields.Chosen(three, decl));
        }

        return "play: nothing -> clip 0, ' Walk ' -> clip 0, 'run' -> clip 2, 'Morphs' (unbakeable) " +
               "and 'Wlak' (typo) -> refused, 'Run, Walk' -> refused as TWO clips by both sides";
    }

    /// <summary>
    /// The verdict every U7/U9 drive arm now ends in - the ONE place that decides whether such an arm
    /// proved anything. Two holes it exists to close, both of which a real in-game run walked into:
    ///
    ///  - a missing piece of the SHIPPING SHAPE used to be logged VOID and counted ZERO failures, so
    ///    a bundle whose baked Animator or whose external base controller never resolved could report
    ///    ALL PASS while the path the gate exists to prove was never exercised once.
    ///  - the anti-vacuity control printed "18 of them off their rest pose (furthest 'BackLeg3.R' by
    ///    0)" - it was printing the worst ERROR, which is zero on a green run, beside a count of
    ///    MOVERS. A control whose number is always zero measures nothing, so the verdict now refuses
    ///    a count of movers that no distance backs.
    ///
    /// The last case is the run LEAD actually observed in-game, and it must still PASS - a verdict
    /// that refused everything would satisfy every arm above it.
    /// </summary>
    private static string Verdict()
    {
        string nullController = "the Animator's runtimeAnimatorController is null - the external base " +
                                "controller did not resolve, so the shipping shape was never exercised";
        string missing = ClipFields.DriveVerdict(nullController, 0, 0, 0, 0f);
        Assert(missing == nullController, "a shipping shape that did not assemble is a FAILURE with " +
               "its own reason, never a skip: " + (missing ?? "(it passed!)"));

        string blind = ClipFields.DriveVerdict(null, 34, 0, 18, 0f);
        Assert(blind != null && blind.Contains("measures nothing"),
               "18 bones 'moved' but nothing travelled is a contradiction, not a pass: " +
               (blind ?? "(it passed!)"));

        string frozen = ClipFields.DriveVerdict(null, 34, 0, 0, 0f);
        Assert(frozen != null && frozen.Contains("rest pose"),
               "a rig frozen at its bind pose is refused: " + (frozen ?? "(it passed!)"));
        string bad = ClipFields.DriveVerdict(null, 34, 2, 18, 0.35f);
        Assert(bad != null && bad.Contains("2 of 34"), "bones off where the file says are refused: " +
               (bad ?? "(it passed!)"));
        Assert(ClipFields.DriveVerdict(null, 0, 0, 0, 0f) != null, "a clip driving no bone is refused");

        // Anti-vacuity: the reading the in-game gate really produced has to come back CLEAN, or the
        // four refusals above are satisfied by a verdict that simply never passes.
        string real = ClipFields.DriveVerdict(null, 34, 0, 18, 0.35f);
        Assert(real == null, "the real in-game reading (34 checked, 0 wrong, 18 moved, travel 0.35) " +
               "still passes: " + (real ?? "null"));
        // And the threshold is ONE number, shared with the count of movers, so a bone that travelled
        // exactly the epsilon cannot count as a mover and fail the distance rule at the same time.
        Assert(ClipFields.DriveVerdict(null, 34, 0, 18, ClipFields.RestTravel) != null &&
               ClipFields.DriveVerdict(null, 34, 0, 18, ClipFields.RestTravel * 2f) == null,
               "the travel rule turns over at the same epsilon the movers are counted by");

        return "drive verdict: null controller -> FAIL, 18 movers travelling 0 -> FAIL, frozen rig -> " +
               "FAIL, 2 of 34 wrong -> FAIL, the real reading -> PASS";
    }

    /// <summary>The container spelling <c>BundleBaker.Normalize</c> gives a key.</summary>
    private static string Key(string name) =>
        ("clips/" + name).Replace('\\', '/').Trim('/').ToLowerInvariant();

    private static string Refusal(SampledClip clip, BakedSkin skin)
    {
        try
        {
            ClipFields.Bindings(clip, skin);
            return "(no refusal at all)";
        }
        catch (InvalidDataException exception) { return exception.Message; }
    }

    /// <summary>
    /// The same rig <see cref="ClipImport"/> hand-builds - two joints, one skinned triangle, one node
    /// ('prop') the armature does not list - carrying THREE animations:
    ///
    ///   "Walk"   head translation      -> bakes
    ///   "walk"   hip  translation      -> bakes, same name once lowercased
    ///   "Morphs" head weights + prop translation -> zero tracks, nothing to bind
    /// </summary>
    private static byte[] Hand()
    {
        var b = new ClipImport.Bin();
        int position = b.Vec(3, "VEC3", 0f, 0f, 0f, 1f, 0f, 0f, 0f, 2f, 0f);
        int normal = b.Vec(3, "VEC3", 0f, 0f, -1f, 0f, 0f, -1f, 0f, 0f, -1f);
        int joints = b.Joints(0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0);
        int weights = b.Vec(3, "VEC4", 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f);
        int indices = b.Indices(0, 1, 2);
        int bind = b.Vec(2, "MAT4",
            1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -1, 0, 1,
            1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -4, 0, 1);
        int times = b.Vec(2, "SCALAR", 0f, 1f);
        int move = b.Vec(2, "VEC3", 0f, 0f, 0f, 1f, 0f, 0f);
        int morph = b.Vec(2, "SCALAR", 0f, 1f);

        string json =
            "{\"asset\":{\"version\":\"2.0\"}," +
            "\"scenes\":[{\"nodes\":[0,3,4]}],\"scene\":0," +
            "\"nodes\":[" +
              "{\"name\":\"rig\",\"children\":[1]}," +
              "{\"name\":\"hip\",\"children\":[2],\"translation\":[0,1,0]}," +
              "{\"name\":\"head\",\"translation\":[0,3,0]}," +
              "{\"name\":\"body\",\"mesh\":0,\"skin\":0}," +
              "{\"name\":\"prop\"}]," +
            "\"skins\":[{\"joints\":[1,2],\"inverseBindMatrices\":" + bind + "}]," +
            "\"meshes\":[{\"name\":\"u9mesh\",\"primitives\":[{\"attributes\":{\"POSITION\":" + position +
              ",\"NORMAL\":" + normal + ",\"JOINTS_0\":" + joints + ",\"WEIGHTS_0\":" + weights +
              "},\"indices\":" + indices + "}]}]," +
            "\"animations\":[" +
              "{\"name\":\"Walk\",\"samplers\":[" + ClipImport.Sampler(times, move, "LINEAR") + "]," +
                "\"channels\":[{\"sampler\":0,\"target\":{\"node\":2,\"path\":\"translation\"}}]}," +
              "{\"name\":\"walk\",\"samplers\":[" + ClipImport.Sampler(times, move, "LINEAR") + "]," +
                "\"channels\":[{\"sampler\":0,\"target\":{\"node\":1,\"path\":\"translation\"}}]}," +
              "{\"name\":\"Morphs\",\"samplers\":[" + ClipImport.Sampler(times, morph, "LINEAR") + "," +
                ClipImport.Sampler(times, move, "LINEAR") + "]," +
                "\"channels\":[{\"sampler\":0,\"target\":{\"node\":2,\"path\":\"weights\"}}," +
                             "{\"sampler\":1,\"target\":{\"node\":4,\"path\":\"translation\"}}]}]," +
            b.Json() + "}";
        return ClipImport.Container(json, b.Bytes());
    }

    /// <summary>
    /// The SAME three animations as <see cref="Hand"/> plus the two grids no shipped file reaches,
    /// written to disk as <c>lib\u9_probe.glb</c> so the IN-GAME arm can read the identical shapes
    /// (<c>Program --u9probe</c>). It is deliberately not <see cref="Hand"/> itself: the plan arms
    /// above count clips, and a fixture that grew under them would move their numbers.
    ///
    ///   "Walk"   head translation, LINEAR, keys 0 / 0.1 / 0.511 s -> no whole rate up to 120 Hz
    ///            serves both 0.1 and 0.511, so the clip falls back to 30 Hz and the grid has to
    ///            CEIL to reach the last key: 17 frames, last at 0.5333 s   (U8-grid)
    ///   "walk"   the same curve on the hip - same name once lowercased     (U9-plan, dedup)
    ///   "Morphs" head weights + a node the armature does not list          (U9-plan, skip)
    ///   "Hold"   head translation, STEP, keys 0 / 0.5 / 1 s -> 2 Hz as LINEAR, raised to the
    ///            highest whole multiple the mod allows: 120 Hz x 121 frames (U8-step)
    /// </summary>
    internal static byte[] Probe()
    {
        var b = new ClipImport.Bin();
        int position = b.Vec(3, "VEC3", 0f, 0f, 0f, 1f, 0f, 0f, 0f, 2f, 0f);
        int normal = b.Vec(3, "VEC3", 0f, 0f, -1f, 0f, 0f, -1f, 0f, 0f, -1f);
        int joints = b.Joints(0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0);
        int weights = b.Vec(3, "VEC4", 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f);
        int indices = b.Indices(0, 1, 2);
        int bind = b.Vec(2, "MAT4",
            1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -1, 0, 1,
            1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, -4, 0, 1);
        int gridTimes = b.Vec(3, "SCALAR", 0f, 0.1f, 0.511f);
        int stepTimes = b.Vec(3, "SCALAR", 0f, 0.5f, 1f);
        int ramp = b.Vec(3, "VEC3", 1f, 0f, 0f, 3f, 0f, 0f, 5f, 0f, 0f);
        int morph = b.Vec(2, "SCALAR", 0f, 1f);
        int propTimes = b.Vec(2, "SCALAR", 0f, 1f);
        int propMove = b.Vec(2, "VEC3", 0f, 0f, 0f, 1f, 0f, 0f);

        string json =
            "{\"asset\":{\"version\":\"2.0\"}," +
            "\"scenes\":[{\"nodes\":[0,3,4]}],\"scene\":0," +
            "\"nodes\":[" +
              "{\"name\":\"rig\",\"children\":[1]}," +
              "{\"name\":\"hip\",\"children\":[2],\"translation\":[0,1,0]}," +
              "{\"name\":\"head\",\"translation\":[0,3,0]}," +
              "{\"name\":\"body\",\"mesh\":0,\"skin\":0}," +
              "{\"name\":\"prop\"}]," +
            "\"skins\":[{\"joints\":[1,2],\"inverseBindMatrices\":" + bind + "}]," +
            "\"meshes\":[{\"name\":\"u9mesh\",\"primitives\":[{\"attributes\":{\"POSITION\":" + position +
              ",\"NORMAL\":" + normal + ",\"JOINTS_0\":" + joints + ",\"WEIGHTS_0\":" + weights +
              "},\"indices\":" + indices + "}]}]," +
            "\"animations\":[" +
              "{\"name\":\"Walk\",\"samplers\":[" + ClipImport.Sampler(gridTimes, ramp, "LINEAR") + "]," +
                "\"channels\":[{\"sampler\":0,\"target\":{\"node\":2,\"path\":\"translation\"}}]}," +
              "{\"name\":\"walk\",\"samplers\":[" + ClipImport.Sampler(gridTimes, ramp, "LINEAR") + "]," +
                "\"channels\":[{\"sampler\":0,\"target\":{\"node\":1,\"path\":\"translation\"}}]}," +
              "{\"name\":\"Morphs\",\"samplers\":[" + ClipImport.Sampler(propTimes, morph, "LINEAR") + "," +
                ClipImport.Sampler(propTimes, propMove, "LINEAR") + "]," +
                "\"channels\":[{\"sampler\":0,\"target\":{\"node\":2,\"path\":\"weights\"}}," +
                             "{\"sampler\":1,\"target\":{\"node\":4,\"path\":\"translation\"}}]}," +
              "{\"name\":\"Hold\",\"samplers\":[" + ClipImport.Sampler(stepTimes, ramp, "STEP") + "]," +
                "\"channels\":[{\"sampler\":0,\"target\":{\"node\":2,\"path\":\"translation\"}}]}]," +
            b.Json() + "}";
        return ClipImport.Container(json, b.Bytes());
    }

    private static void Assert(bool ok, string what)
    {
        if (!ok) throw new Exception("CLIP plan FAILED: " + what);
    }
}
