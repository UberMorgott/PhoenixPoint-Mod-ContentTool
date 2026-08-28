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
               ", root bone scale " + F(column);
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("SKIN-ABOVE FAILURE: " + what);
        return 1;
    }

    private static string F(float v) { return v.ToString("0.####", CultureInfo.InvariantCulture); }
}
