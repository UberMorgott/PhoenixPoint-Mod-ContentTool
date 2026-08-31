using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Morgott.ContentTool.Import;

/// <summary>
/// THE NODES ABOVE THE ARMATURE ARE PART OF THE RIG. glTF 2.0 §3.7.4.3 states the skinning matrix as
/// <c>jointMatrix = globalTransformOfJointNode * inverseBindMatrix</c>, and "global" is the whole
/// parent chain up to the SCENE root. <c>GlbReader</c> used to derive every bone rest from the inverse
/// bind matrices alone, which roots the rig at the skin's own root joint and silently drops whatever
/// sits above it - and what sits above it is exactly where an exporter parks the file's axis
/// conversion and its unit scale.
///
/// Quaternius' CC0 spider (<c>lib\u8_probe.glb</c>, the file the demo shipped as <c>spider.glb</c>
/// until the creature line replaced the model) is that file, and its numbers are the oracle here:
///   node 0 'RootNode'        - no transform
///   node 1 'SpiderArmature'  - rotation [-0.7071,0,0,0.7071] = -90 deg about X, scale 100
///   node 2 'Root'            - skins[0].skeleton, joint 0 of 39
/// Read WITHOUT that node the model arrives in the armature's own space: half-extents
/// X 1.0000, Y 0.8877, Z 0.3283, the eight *Foot bones coplanar on Z, i.e. Z-UP at 1/100 scale. Read
/// WITH it - the spec's reading - the -90 about X carries the model's Z onto Unity's Y and the scale
/// multiplies by 100, so the SAME file must come out Y-UP with half-extents X 100.00, Y 32.83,
/// Z 88.77 and its feet at the BOTTOM of Y.
///
/// Every arm below is stated against those derived numbers, so re-dropping the parent transform turns
/// this whole module red rather than shifting one tolerance. Falsified by doing exactly that.
/// </summary>
internal static class SkinAbove
{
    private static readonly string[] AxisName = { "X", "Y", "Z" };

    /// <summary>Node 1's own scale, and the axis its -90 about X carries the model's up onto.</summary>
    private const float AuthoredScale = 100f;
    private const int Up = 1;                      // Unity +Y

    /// <summary>The armature-local half-extents, measured off this file before the fix - what the old
    /// importer produced, and what node 1's transform is applied TO below.</summary>
    private static readonly float[] Local = { 1.0000f, 0.8877f, 0.3283f };

    /// <summary>The scale the file's OWN root bone rests at, in the armature's space - the number the
    /// old importer reported for it, so node 1's scale below is a factor on top of this and not a
    /// replacement for it.</summary>
    private const float LocalScale = 33.68827f;

    internal static string Run()
    {
        // The fixture is COMMITTED (lib\u8_probe.glb), so it is absent only in a broken checkout -
        // and a missing one is a FAILURE, never a VOID that reads green. This arm spent the creature
        // line switched off exactly that way: it named the demo's `spider.glb`, the creature line
        // replaced that model with a DIFFERENT one (`cyborg_spider.glb`, Sketchfab's Cyborg Spider),
        // and the arm printed "VOID - no ...spider.glb" while the suite stayed green. The oracle
        // above is Quaternius' CC0 spider, and the demo's old spider.glb was a copy of it - so the
        // fixture is that same file where it still lives, not whatever the demo ships today.
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            @"..\..\..\..\..\lib\u8_probe.glb");
        if (!File.Exists(path))
            throw new Exception("SKIN-ABOVE FAILURE: the fixture is gone - no " +
                                Path.GetFullPath(path));

        SkinnedModel model = GlbReader.Read(File.ReadAllBytes(path));
        int checks = 0;

        // ---- the vertices: where the model actually is, in Unity space
        var min = new[] { float.MaxValue, float.MaxValue, float.MaxValue };
        var max = new[] { float.MinValue, float.MinValue, float.MinValue };
        foreach (ObjVector3 p in model.Positions)
        {
            float[] v = { p.X, p.Y, p.Z };
            for (int a = 0; a < 3; a++) { if (v[a] < min[a]) min[a] = v[a]; if (v[a] > max[a]) max[a] = v[a]; }
        }
        var half = new float[3];
        for (int a = 0; a < 3; a++) half[a] = (max[a] - min[a]) * 0.5f;

        // -90 deg about X maps the model's (x,y,z) onto (x,z,-y), so the half-extents permute the same
        // way, and the whole thing is then multiplied by node 1's scale.
        float[] want = { Local[0] * AuthoredScale, Local[2] * AuthoredScale, Local[1] * AuthoredScale };
        for (int a = 0; a < 3; a++)
            checks += Check(Math.Abs(half[a] - want[a]) <= want[a] * 0.001f,
                "half-extent " + AxisName[a] + " is " + F(half[a]) + ", and the file's own node 1 (-90 about X, " +
                "scale " + F(AuthoredScale) + ") over the armature-local " + F(Local[a == 0 ? 0 : a == 1 ? 2 : 1]) +
                " states " + F(want[a]) + " - the transform above the skin's root joint is being dropped");

        // ---- the skeleton: bone world rests are inverse(bindPose), which is what the engine skins with
        var rest = new Dictionary<string, float[]>();
        for (int b = 0; b < model.JointNames.Count; b++)
        {
            float[] world = ModelBuild.Invert(model.InverseBindMatrices[b], model.JointNames[b]);
            rest[model.JointNames[b]] = world;
        }

        // The ground is the plane the FEET are coplanar on - the skeleton's own statement of which way
        // is up, and it does not care what the bounding box looks like.
        var feet = new List<float[]>();
        var body = new List<float[]>();
        foreach (KeyValuePair<string, float[]> bone in rest)
        {
            if (bone.Key.IndexOf("Foot", StringComparison.Ordinal) >= 0) feet.Add(bone.Value);
            else if (bone.Key == "Body" || bone.Key == "Thorax" || bone.Key == "Abdomen" || bone.Key == "Head")
                body.Add(bone.Value);
        }
        if (feet.Count < 4 || body.Count < 2)
            throw new Exception("SKIN-ABOVE FAILURE: the file no longer has the *Foot and Body bones this " +
                                "check reads the ground plane off (" + feet.Count + " feet, " + body.Count + " body)");

        var spread = new float[3];
        for (int a = 0; a < 3; a++)
        {
            float lo = float.MaxValue, hi = float.MinValue;
            foreach (float[] m in feet) { float v = m[12 + a]; if (v < lo) lo = v; if (v > hi) hi = v; }
            spread[a] = hi - lo;
        }
        int flattest = spread[0] <= spread[1] ? (spread[0] <= spread[2] ? 0 : 2) : (spread[1] <= spread[2] ? 1 : 2);
        float next = float.MaxValue;
        for (int a = 0; a < 3; a++) if (a != flattest && spread[a] < next) next = spread[a];
        checks += Check(next > spread[flattest] * 10f,
            "the feet are coplanar on " + AxisName[flattest] + " by only " + F(next / Math.Max(1e-6f, spread[flattest])) +
            "x, which is not enough to call it the ground plane at all");
        checks += Check(flattest == Up,
            "the eight feet rest coplanar on " + AxisName[flattest] + " (spread " + F(spread[flattest]) +
            "), so the imported model stands on " + AxisName[flattest] + " and not on Unity's +" + AxisName[Up]);

        float footMean = 0f, bodyMean = 0f;
        foreach (float[] m in feet) footMean += m[12 + Up];
        foreach (float[] m in body) bodyMean += m[12 + Up];
        footMean /= feet.Count; bodyMean /= body.Count;
        checks += Check(footMean < bodyMean,
            "the feet rest ABOVE the body on +" + AxisName[Up] + " (feet " + F(footMean) + ", body " + F(bodyMean) +
            "), so the model is upside down - the up axis is -" + AxisName[Up]);

        // ---- the scale, read straight off the root bone rather than off the bounding box.
        // The file's own root bone carries a scale of its own (LocalScale, which is precisely what the
        // OLD importer produced and what b667381 measured in game), so what node 1 contributes is a
        // FACTOR on top of it. Asserting the product keeps the arm falsifiable in both directions: drop
        // node 1 and this reads 33.688, apply it twice and it reads 336882.7.
        float[] root = rest["Root"];
        float column = (float)Math.Sqrt((double)root[0] * root[0] + (double)root[1] * root[1] + (double)root[2] * root[2]);
        checks += Check(Math.Abs(column - LocalScale * AuthoredScale) <= LocalScale * AuthoredScale * 0.001f,
            "the root bone rests at scale " + F(column) + ", and the file's own root bone scale " + F(LocalScale) +
            " under node 1's authored " + F(AuthoredScale) + " states " + F(LocalScale * AuthoredScale) +
            " - the scale above the skin's root joint is being dropped");

        return "SKIN-ABOVE PASS, " + checks + " check(s) - u8_probe.glb imports Y-UP at the authored scale: " +
               "half-extents X " + F(half[0]) + " Y " + F(half[1]) + " Z " + F(half[2]) +
               ", feet coplanar on " + AxisName[flattest] + " at " + F(footMean) + " under the body at " + F(bodyMean) +
               ", root bone scale " + F(column) + "\n  " + RootCurve() + "\n  " + Hard();
    }

    /// <summary>
    /// THE HARD CASE AS A REAL FILE ON DISK, read through <see cref="GlbReader.Read(byte[],List{SampledClip})"/>
    /// end to end: <c>lib\u8_rootfold.glb</c> is Blender's own shape - an armature OBJECT carrying
    /// -90 deg about X AND a scale of 100, a ROOT bone driven by translation AND rotation curves, one
    /// child bone, and a skinned mesh weighted to both. <see cref="RootCurve"/> proves the fold on a
    /// scale-only rig with one translation channel; nothing proved it where the transform ROTATES and
    /// the curve turns the bone as well, which is exactly the file a user sends.
    ///
    /// Every number below is derived by hand from the file's own JSON, not measured off a run:
    ///   the fold      -90 about X maps glTF (x,y,z) -> (x,z,-y), and Unity's S = diag(-1,1,1)
    ///                  leaves that untouched (X is the mirror axis), so <c>over</c> is the
    ///                  SAME matrix in both spaces: Unity (x,y,z) -> 100*(x,z,-y).
    ///   vertices      glTF (1,0,0) -> Unity (-1,0,0) -> folded (-100,0,0)
    ///                 glTF (0,2,0) -> Unity  (0,2,0) -> folded (0,0,-200)
    ///   Root rest     inverse(bindPose) = over: scale 100, translation 0
    ///   Child rest    bind(parent)*inverse(bind(child)) cancels the fold -> translate(0,1,0), untouched
    ///   frame 0       t glTF (1,0,0) -> (-100,0,0), r rest -> (-0.7071,0,0,0.7071), s (100,100,100)
    ///   frame 1       t glTF (5,0,0) -> (-500,0,0), r over*(0,-0.7071,0,0.7071) -> (-0.5,-0.5,0.5,0.5)
    ///
    /// The first key is the vertex's OWN glTF position, so "the samples land in the same space as the
    /// baked vertices" is asserted as an identity between two numbers the importer produced down two
    /// different paths rather than against a constant. Falsified by folding the frame the other way
    /// round (<c>Trs * over</c> instead of <c>over * Trs</c>): the translation reads -1 and -5.
    /// </summary>
    private static string Hard()
    {
        // Unity-space transform of the armature object, hand-derived above: columns are the images of
        // X, Y and Z, each 100 long.
        float[] over = { 100, 0, 0, 0,  0, 0, -100, 0,  0, 100, 0, 0,  0, 0, 0, 1 };

        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            @"..\..\..\..\..\lib\u8_rootfold.glb");
        if (!File.Exists(path))
            throw new Exception("ROOT-FOLD FAILURE: the fixture is gone - no " + Path.GetFullPath(path));

        var clips = new List<SampledClip>();
        SkinnedModel model = GlbReader.Read(File.ReadAllBytes(path), clips);
        int checks = Check(model.JointNames.Count == 2 && model.JointNames[0] == "Root" &&
                           model.JointNames[1] == "Child" && clips.Count == 1,
            "the file's two bones and one clip came back: " + model.JointNames.Count + " bone(s), " +
            clips.Count + " clip(s)");

        // ---- the vertices, folded by the armature object.
        checks += Check(Vec(model.Positions[1], -100f, 0f, 0f) && Vec(model.Positions[2], 0f, 0f, -200f),
            "the baked vertices carry the armature's -90 about X and its scale 100: v1 " +
            V(model.Positions[1]) + " (want -100,0,0), v2 " + V(model.Positions[2]) + " (want 0,0,-200)");

        // ---- the bind poses, and the relation the whole importer rests on.
        for (int j = 0; j < 2; j++)
        {
            float[] world = ModelBuild.Invert(model.InverseBindMatrices[j], model.JointNames[j]);
            float[] round = ModelBuild.Multiply(world, model.InverseBindMatrices[j]);
            for (int i = 0; i < 16; i++)
                checks += Check(Math.Abs(round[i] - (i % 5 == 0 ? 1f : 0f)) < 1e-3f,
                    "bone '" + model.JointNames[j] + "': boneWorld * bindPose is not the identity at [" +
                    i + "] = " + F(round[i]));
            if (j != 0) continue;
            for (int i = 0; i < 16; i++)
                checks += Check(Math.Abs(world[i] - over[i]) < 1e-2f,
                    "the ROOT bone's world rest is the armature object's own transform: [" + i + "] = " +
                    F(world[i]) + ", want " + F(over[i]));
        }

        // ---- the CHILD's rest, which the fold must leave completely alone: bind(parent) *
        // inverse(bind(child)) cancels it, so a fold applied one level too low shows up here first.
        float[] child = model.Nodes[1].Local;
        for (int i = 0; i < 16; i++)
            checks += Check(Math.Abs(child[i] - (i == 13 ? 1f : i % 5 == 0 ? 1f : 0f)) < 1e-4f,
                "the child bone's rest is translate(0,1,0), untouched by the armature's transform: [" +
                i + "] = " + F(child[i]));

        // ---- the clip: the root bone's own curve, in the vertices' space.
        SampledTrack root = clips[0].Tracks[0];
        checks += Check(clips[0].Tracks.Count == 1 && root.Node == 0 && clips[0].Times.Length == 2,
            "the clip drives the root bone alone, on the 1 Hz grid its keys state: " +
            clips[0].Tracks.Count + " track(s) x " + clips[0].Times.Length + " frame(s)");
        checks += Check(Vec(root.Translations[0], model.Positions[1].X, model.Positions[1].Y, model.Positions[1].Z),
            "the first key is the file's own vertex 1, so the sample must land ON it: sample " +
            V(root.Translations[0]) + " vs vertex " + V(model.Positions[1]) +
            " - the armature's transform is not reaching the samples");
        checks += Check(Vec(root.Translations[1], -500f, 0f, 0f),
            "and glTF x = 5 under the same fold is -500: " + V(root.Translations[1]));
        for (int f = 0; f < 2; f++)
            checks += Check(Vec(root.Scales[f], 100f, 100f, 100f),
                "a channel the file leaves out keeps the bone's OWN rest under the fold, so the scale " +
                "is the armature's 100 at frame " + f + ": " + V(root.Scales[f]));
        checks += Check(Quat(root.Rotations[0], -0.7071068f, 0f, 0f, 0.7071068f),
            "frame 0 has no rotation of its own, so it is the armature's -90 about X: " + Q(root.Rotations[0]));
        checks += Check(Quat(root.Rotations[1], -0.5f, -0.5f, 0.5f, 0.5f),
            "and frame 1 is that composed with the curve's own +90 about glTF +Y: " + Q(root.Rotations[1]));

        return "ROOT-FOLD PASS, " + checks + " check(s) - u8_rootfold.glb (armature -90 about X, scale " +
               "100; root bone driven in translation AND rotation): verts " + V(model.Positions[1]) + " " +
               V(model.Positions[2]) + " | root samples " + V(root.Translations[0]) + " .. " +
               V(root.Translations[1]) + " scale " + V(root.Scales[0]) + " rot " + Q(root.Rotations[1]) +
               " | child rest untouched at (0,1,0)";
    }

    private static bool Vec(ObjVector3 v, float x, float y, float z) =>
        Math.Abs(v.X - x) < 1e-2f && Math.Abs(v.Y - y) < 1e-2f && Math.Abs(v.Z - z) < 1e-2f;

    /// <summary>A quaternion and its negation are the SAME rotation, so the sign the decomposer
    /// happens to pick must not decide whether this arm is green.</summary>
    private static bool Quat(ObjQuaternion q, float x, float y, float z, float w) =>
        Math.Abs(q.X * x + q.Y * y + q.Z * z + q.W * w) > 0.9999f;

    private static string V(ObjVector3 v) => "(" + F(v.X) + "," + F(v.Y) + "," + F(v.Z) + ")";

    private static string Q(ObjQuaternion q) =>
        "(" + F(q.X) + "," + F(q.Y) + "," + F(q.Z) + "," + F(q.W) + ")";

    /// <summary>
    /// AND THE SAME TRANSFORM REACHES THE ANIMATION. A clip that drives the armature's ROOT bone
    /// replaces the very rest transform <c>Carry</c> folded node 1 into, so the fold has to be applied
    /// to every SAMPLE as well - it used to be REFUSED instead, which sent the author to Blender to
    /// apply all transforms and broke the rig they applied them to.
    ///
    /// The fixture is hand-built glTF bytes: a rig node scaled 2, one root joint 'hip' rest 1 up, and
    /// ONE channel driving the hip's translation from glTF x = 1 to x = 5. So the three numbers are:
    ///   translation  glTF x 1..5 -> Unity x -1..-5, times node 0's scale 2 -> -2 .. -10
    ///   scale        no channel  -> the hip's own rest scale 1, times 2    -> (2, 2, 2)
    ///   rotation     no channel  -> the hip's own rest rotation           -> identity
    /// Dropping the fold reads -1 and 1; applying it twice reads -4 and 4.
    /// </summary>
    private static string RootCurve()
    {
        var clips = new List<SampledClip>();
        GlbReader.Read(Fixture(), clips);
        SampledClip clip = clips[0];
        int checks = Check(clip.Tracks.Count == 1 && clip.Times.Length == 2,
            "the clip drives the one bone the file names, on the grid its keys state: " +
            clip.Tracks.Count + " track(s) x " + clip.Times.Length + " frame(s)");

        SampledTrack hip = clip.Tracks[0];
        checks += Check(Math.Abs(hip.Translations[0].X + 2f) < 1e-4f && Math.Abs(hip.Translations[1].X + 10f) < 1e-4f,
            "the root bone's own curve comes back x " + F(hip.Translations[0].X) + " .. " + F(hip.Translations[1].X) +
            ", and the file's glTF x 1..5 under node 0's scale 2 states -2 .. -10 - the transform above the " +
            "armature is not reaching the samples");
        checks += Check(hip.Scales != null && Math.Abs(hip.Scales[1].X - 2f) < 1e-4f &&
                        Math.Abs(hip.Scales[1].Y - 2f) < 1e-4f && Math.Abs(hip.Scales[1].Z - 2f) < 1e-4f,
            "a channel the file leaves out keeps the bone's OWN rest under the same fold, so the scale is " +
            "(2,2,2): " + (hip.Scales == null ? "no scale at all" :
                "(" + F(hip.Scales[1].X) + "," + F(hip.Scales[1].Y) + "," + F(hip.Scales[1].Z) + ")"));
        checks += Check(hip.Rotations != null && Math.Abs(hip.Rotations[1].W - 1f) < 1e-4f,
            "and the rotation it leaves out is the rest one, w = " +
            (hip.Rotations == null ? "no rotation at all" : F(hip.Rotations[1].W)));

        return "ROOT-CURVE PASS, " + checks + " check(s) - a curve on the armature's root bone carries the " +
               "object above it: x " + F(hip.Translations[0].X) + " .. " + F(hip.Translations[1].X) +
               ", scale (" + F(hip.Scales[1].X) + "," + F(hip.Scales[1].Y) + "," + F(hip.Scales[1].Z) + ")";
    }

    /// <summary>
    /// The fixture, assembled out of <see cref="ClipImport"/>'s own container and accessor writers -
    /// glTF-space bytes with no line of our writer in the loop, same as the arms there.
    /// </summary>
    private static byte[] Fixture()
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
        int move = b.Vec(2, "VEC3", 1f, 0f, 0f, 5f, 0f, 0f);

        string json =
            "{\"asset\":{\"version\":\"2.0\"}," +
            "\"scenes\":[{\"nodes\":[0,3]}],\"scene\":0," +
            "\"nodes\":[" +
              "{\"name\":\"rig\",\"children\":[1],\"scale\":[2,2,2]}," +
              "{\"name\":\"hip\",\"children\":[2],\"translation\":[0,1,0]}," +
              "{\"name\":\"head\",\"translation\":[0,3,0]}," +
              "{\"name\":\"body\",\"mesh\":0,\"skin\":0}]," +
            "\"skins\":[{\"joints\":[1,2],\"inverseBindMatrices\":" + bind + "}]," +
            "\"meshes\":[{\"name\":\"rootmesh\",\"primitives\":[{\"attributes\":{\"POSITION\":" + position +
              ",\"NORMAL\":" + normal + ",\"JOINTS_0\":" + joints + ",\"WEIGHTS_0\":" + weights +
              "},\"indices\":" + indices + "}]}]," +
            "\"animations\":[{\"name\":\"root\",\"samplers\":[" +
              ClipImport.Sampler(times, move, "LINEAR") + "]," +
            "\"channels\":[{\"sampler\":0,\"target\":{\"node\":1,\"path\":\"translation\"}}]}]," +
            b.Json() + "}";
        return ClipImport.Container(json, b.Bytes());
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("SKIN-ABOVE FAILURE: " + what);
        return 1;
    }

    private static string F(float v) { return v.ToString("0.####", CultureInfo.InvariantCulture); }
}
