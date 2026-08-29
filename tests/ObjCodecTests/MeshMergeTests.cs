using System;
using System.Collections.Generic;
using System.Text;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Import;

/// <summary>
/// Gate <b>U11</b>: a STATIC model that arrived as many meshes is merged into one, with one submesh
/// per distinct material - the case every gun downloaded off the internet actually is.
///
/// The numbers below are the two real downloads' shapes, so the gate fails if the merge ever stops
/// producing what those files need: <c>ar-181.glb</c> is 14 pieces over 3 materials, and
/// <c>tau_pulse_pistol.glb</c> is 9 pieces over 1. Both must end up as ONE mesh.
///
/// Every refusal arm asserts the CAUSE in the message, so a merge that refused for the wrong reason
/// would not pass - and the SKINNED arm is the creature line's guarantee: this path must never take
/// a rigged mesh, because merging one needs a shared bone list and a vertex rebase it does not do.
/// </summary>
internal static class MeshMergeTests
{
    private static int checks;

    internal static string Run()
    {
        StringBuilder log = new StringBuilder();
        checks = 0;
        try
        {
            Merges(log);
            Groups(log);
            RefusesSkinned(log);
            RefusesMixedUv(log);
            CarriesImages(log);
            CarriesEmissive(log);
            Fits(log);
            Normalises(log);
        }
        catch (Exception ex) { return "MESHMERGE FAIL " + ex; }
        return log.ToString() + "MESHMERGE: ALL PASS, " + checks + " check(s)";
    }

    private static void Ok(bool cond, string what)
    {
        checks++;
        if (!cond) throw new Exception("MESHMERGE FAIL " + what);
    }

    /// <summary>A quad-ish part: 3 verts, 1 triangle, its own material name.</summary>
    private static SkinnedModel Part(string name, string material, float x, bool uv = true)
    {
        SkinnedModel m = new SkinnedModel { Name = name };
        m.Positions = new[]
        {
            new ObjVector3(x, 0f, 0f), new ObjVector3(x + 1f, 0f, 0f), new ObjVector3(x, 1f, 0f)
        };
        m.Normals = new[]
        {
            new ObjVector3(0f, 0f, 1f), new ObjVector3(0f, 0f, 1f), new ObjVector3(0f, 0f, 1f)
        };
        if (uv)
            m.Uv0 = new[] { new ObjVector2(0f, 0f), new ObjVector2(1f, 0f), new ObjVector2(0f, 1f) };
        m.Submeshes.Add(new[] { 0, 1, 2 });
        if (material != null) m.Materials.Add(material);
        return m;
    }

    /// <summary>
    /// Three pieces, two materials. The whole contract in one arm: one mesh out, submeshes equal
    /// DISTINCT MATERIALS in first-seen order, vertices concatenated, and - the part that actually
    /// breaks in a naive merge - every triangle index REBASED by the running vertex count.
    /// </summary>
    private static void Merges(StringBuilder log)
    {
        List<SkinnedModel> parts = new List<SkinnedModel>
        {
            Part("body", "Steel", 0f), Part("scope", "Glass", 10f), Part("grip", "Steel", 20f)
        };
        string refusal, note;
        SkinnedModel m = MeshMerge.Static(parts, "gun", out refusal, out note);

        Ok(refusal == null, "a well-formed static model was refused: " + refusal);
        Ok(m != null, "merge returned nothing");
        Ok(m.Submeshes.Count == 2, "expected 2 submeshes (2 distinct materials), got " + m.Submeshes.Count);
        Ok(m.Materials.Count == 2, "expected 2 materials, got " + m.Materials.Count);
        Ok(m.Materials[0] == "Steel" && m.Materials[1] == "Glass",
           "materials lost first-seen order: " + string.Join(",", m.Materials.ToArray()));
        Ok(m.Positions.Length == 9, "expected 9 verts, got " + m.Positions.Length);
        Ok(m.Normals.Length == 9, "expected 9 normals, got " + m.Normals.Length);
        Ok(m.Uv0 != null && m.Uv0.Length == 9, "expected 9 UVs");

        // Steel = body (verts 0..2) THEN grip, which lands at 3..5 because it is appended inside the
        // same group; Glass = scope, appended after both, so its base is 6. An un-rebased merge
        // would give {0,1,2} here and draw the scope on top of the body.
        int[] steel = m.Submeshes[0], glass = m.Submeshes[1];
        Ok(steel.Length == 6, "Steel should hold 2 triangles, got " + steel.Length / 3);
        Ok(steel[0] == 0 && steel[1] == 1 && steel[2] == 2, "first triangle moved");
        Ok(steel[3] == 3 && steel[4] == 4 && steel[5] == 5,
           "the SECOND Steel piece was not rebased: got " + string.Join(",", Array.ConvertAll(steel, i => i.ToString())));
        Ok(glass.Length == 3 && glass[0] == 6 && glass[1] == 7 && glass[2] == 8,
           "the Glass piece was not rebased: got " + string.Join(",", Array.ConvertAll(glass, i => i.ToString())));

        // The vertex the rebased index points at must still be the vertex that part supplied.
        // Asserted as ORDER, not as absolute coordinates: the merge normalises the result to one
        // unit on its longest axis, so the numbers are scaled - but a uniform positive scale cannot
        // reorder them. Checking 20f literally made this arm fail the moment normalising landed,
        // which is the arm coupling itself to a value it never actually cared about.
        Ok(m.Positions[3].X > m.Positions[6].X,
           "the grip (source x=20) must still sit beyond the scope (x=10) after the rebase: " +
           m.Positions[3].X + " vs " + m.Positions[6].X);
        Ok(m.Positions[6].X > m.Positions[0].X,
           "and the scope beyond the body (x=0): " + m.Positions[6].X + " vs " + m.Positions[0].X);
        Ok(note != null && note.Contains("3 piece(s)") && note.Contains("2 submesh(es)"),
           "the note must say what was done, got: " + note);
        log.AppendLine("  merge   " + note);
    }

    /// <summary>The two real downloads' shapes: 14 pieces/3 materials and 9 pieces/1.</summary>
    private static void Groups(StringBuilder log)
    {
        List<SkinnedModel> ar = new List<SkinnedModel>();
        string[] three = { "Material", "material", "Scope" };
        for (int i = 0; i < 14; i++) ar.Add(Part("piece" + i, three[i % 3], i * 5f));
        string refusal, note;
        SkinnedModel m = MeshMerge.Static(ar, "ar-181", out refusal, out note);
        Ok(refusal == null, "the AR-181 shape was refused: " + refusal);
        Ok(m.Submeshes.Count == 3, "14 pieces over 3 materials must give 3 submeshes, got " + m.Submeshes.Count);
        Ok(m.Positions.Length == 42, "expected 42 verts, got " + m.Positions.Length);
        int tris = 0;
        foreach (int[] s in m.Submeshes) tris += s.Length / 3;
        Ok(tris == 14, "no triangle may be lost: expected 14, got " + tris);
        log.AppendLine("  ar-181  " + note);

        List<SkinnedModel> tau = new List<SkinnedModel>();
        for (int i = 0; i < 9; i++) tau.Add(Part("defaultMaterial", "MAT_PulseRifle", i * 5f));
        m = MeshMerge.Static(tau, "tau", out refusal, out note);
        Ok(refusal == null, "the Tau shape was refused: " + refusal);
        Ok(m.Submeshes.Count == 1, "9 pieces over 1 material must give 1 submesh, got " + m.Submeshes.Count);
        Ok(m.Submeshes[0].Length == 27, "expected 9 triangles in one submesh, got " + m.Submeshes[0].Length / 3);
        log.AppendLine("  tau     " + note);
    }

    /// <summary>
    /// The embedded texture of each material survives the merge, index-parallel to the submeshes.
    /// This is what stops an imported model rendering pure white, so the interesting case is the one
    /// that used to lose it: a group whose FIRST piece names the material but carries no image, and
    /// a later piece in the same group that does. A last-writer-wins carry would erase it.
    /// </summary>
    private static void CarriesImages(StringBuilder log)
    {
        SkinnedModel bare = Part("bare", "Steel", 0f);
        bare.MaterialImages.Add(null);
        SkinnedModel painted = Part("painted", "Steel", 10f);
        painted.MaterialImages.Add(new byte[] { 1, 2, 3 });
        SkinnedModel glass = Part("glass", "Glass", 20f);
        glass.MaterialImages.Add(new byte[] { 9 });

        string refusal, note;
        // ORDER MATTERS TO THIS ARM: the piece that HAS the image comes first and the one without it
        // second, so a carry that simply overwrites per piece would end on null and lose the texture.
        // With the other order the buggy version passes by luck, which is how this test first went
        // green against a deliberately broken merge.
        SkinnedModel m = MeshMerge.Static(new List<SkinnedModel> { painted, bare, glass }, "gun",
                                          out refusal, out note);
        Ok(refusal == null, "refused: " + refusal);
        Ok(m.MaterialImages.Count == m.Submeshes.Count,
           "one image slot per submesh: " + m.MaterialImages.Count + " vs " + m.Submeshes.Count);
        Ok(m.MaterialImages[0] != null && m.MaterialImages[0].Length == 3,
           "the Steel image was lost - a later piece with no image must not erase it");
        Ok(m.MaterialImages[1] != null && m.MaterialImages[1][0] == 9, "the Glass image is wrong");
        log.AppendLine("  images  " + m.MaterialImages.Count + " slot(s), first-non-null per material kept");
    }

    /// <summary>
    /// The emissive colour survives the merge the same way the texture does, and lands on the
    /// submesh that actually glows rather than on all of them. Ordered so a last-writer-wins carry
    /// would lose it, exactly as the image arm is.
    /// </summary>
    private static void CarriesEmissive(StringBuilder log)
    {
        SkinnedModel lit = Part("emitter", "Neon", 0f);
        lit.MaterialEmissive.Add(new[] { 0f, 0.8f, 1f, 1f });
        SkinnedModel dark = Part("housing", "Neon", 10f);
        dark.MaterialEmissive.Add(null);
        SkinnedModel steel = Part("body", "Steel", 20f);
        steel.MaterialEmissive.Add(null);

        string refusal, note;
        SkinnedModel m = MeshMerge.Static(new List<SkinnedModel> { lit, dark, steel }, "gun",
                                          out refusal, out note);
        Ok(refusal == null, "refused: " + refusal);
        Ok(m.MaterialEmissive.Count == m.Submeshes.Count,
           "one emissive slot per submesh: " + m.MaterialEmissive.Count + " vs " + m.Submeshes.Count);
        Ok(m.MaterialEmissive[0] != null && Math.Abs(m.MaterialEmissive[0][2] - 1f) < 1e-6f,
           "the Neon glow was lost - a later piece with none must not erase it");
        Ok(m.MaterialEmissive[1] == null,
           "Steel does not glow and must not inherit Neon's emission");
        log.AppendLine("  glow    submesh 0 emits " + m.MaterialEmissive[0][1] + "," + m.MaterialEmissive[0][2] +
                       "; submesh 1 dark");
    }

    /// <summary>
    /// The measured fit: a downloaded model's box into the box the cloned weapon already occupies.
    ///
    /// The target here is the REAL one - the shipped Phoenix sniper's own m_LocalAABB, measured off
    /// px_equipment_assets_all.bundle: centre (0.00435, 0.02574, 0.30869), extent (0.03774, 0.11355,
    /// 0.46011). The source is a metre-scale gun modelled along X at the origin, which is what an
    /// internet download actually looks like.
    /// </summary>
    private static void Fits(StringBuilder log)
    {
        float[] tc = { 0.00435f, 0.02574f, 0.30869f };
        float[] te = { 0.03774f, 0.11355f, 0.46011f };
        float[] sc = { 0f, 0f, 0f };
        float[] se = { 0.5f, 0.06f, 0.04f };            // 1 m long down X, modelled at the origin

        Ok(FitBox.LongAxis(se) == 0, "the barrel is the longest axis, X here");
        float[] euler = FitBox.RotationToZ(FitBox.LongAxis(se), false);
        Ok(Math.Abs(euler[1] - 90f) < 1e-4f, "an X-aligned gun yaws 90 degrees onto +Z, got " + euler[1]);
        Ok(Math.Abs(FitBox.RotationToZ(0, true)[1] - 270f) < 1e-4f, "flip adds a half turn");
        Ok(FitBox.RotationToZ(2, false)[1] == 0f, "a gun already on +Z is not turned");

        float scale; float[] offset; string why;
        // null rotation = the identity case, unchanged by the turn-aware solve.
        Ok(FitBox.Solve(sc, se, tc, te, null, out scale, out offset, out why), "a normal fit solves: " + why);

        // SMALLEST ratio wins, so the result fits INSIDE the reserved box on every axis. Here that is
        // Y: 0.11355/0.06 = 1.89, against X 0.0755 and Z 11.5 - so X, the tightest, actually governs.
        float expected = Math.Min(te[0] / se[0], Math.Min(te[1] / se[1], te[2] / se[2]));
        Ok(Math.Abs(scale - expected) < 1e-6f, "uniform scale is the SMALLEST ratio: " + scale + " vs " + expected);
        for (int i = 0; i < 3; i++)
            Ok(scale * se[i] <= te[i] + 1e-5f,
               "axis " + i + " must end up INSIDE the reserved box, or the gun clips the soldier's arm");

        // The scaled source centre must land exactly on the target centre.
        for (int i = 0; i < 3; i++)
            Ok(Math.Abs((scale * sc[i] + offset[i]) - tc[i]) < 1e-6f,
               "axis " + i + " centre lands on the donor's own centre");

        // Logged BEFORE the refusal case below, which necessarily leaves scale at its default and
        // would otherwise report 1.0000 for a fit that actually solved.
        log.AppendLine("  fit     scale " + scale.ToString("0.0000") + " -> offset " +
                       offset[0].ToString("0.000") + "," + offset[1].ToString("0.000") + "," +
                       offset[2].ToString("0.000") + ", rotate " + euler[1] + " deg");

        // A flat or empty mesh has no meaningful fit and must be refused rather than divided by.
        Ok(!FitBox.Solve(sc, new[] { 0.5f, 0f, 0.04f }, tc, te, null, out scale, out offset, out why),
           "a mesh with no thickness is refused");
        Ok(why != null && why.Contains("thickness"), "and the refusal says why: " + why);
    }

    /// <summary>
    /// A merged model is normalised to one unit on its longest axis, so an exporter's arbitrary root
    /// scale cannot survive into the game.
    ///
    /// THE NUMBERS ARE THE REAL DEFECT. tau_pulse_pistol baked to m_LocalAABB extent
    /// (17.686, 8.486, 3.036) - a 35-unit slab that occluded the whole soldier in the equipment
    /// screen - and ar-181 baked 7 units long. This arm builds a part at that scale and asserts it
    /// comes out at 1.0, so the giant cannot come back unnoticed.
    /// </summary>
    private static void Normalises(StringBuilder log)
    {
        // One part spanning 35.4 units on X, which is what the Tau actually measured.
        SkinnedModel huge = new SkinnedModel { Name = "slab" };
        huge.Positions = new[]
        {
            new ObjVector3(0f, 0f, 0f), new ObjVector3(35.372f, 0f, 0f), new ObjVector3(0f, 16.972f, 0f)
        };
        huge.Normals = new[]
        {
            new ObjVector3(0f, 0f, 1f), new ObjVector3(0f, 0f, 1f), new ObjVector3(0f, 0f, 1f)
        };
        huge.Uv0 = new[] { new ObjVector2(0f, 0f), new ObjVector2(1f, 0f), new ObjVector2(0f, 1f) };
        huge.Submeshes.Add(new[] { 0, 1, 2 });
        huge.Materials.Add("MAT_PulseRifle");

        string refusal, note;
        SkinnedModel m = MeshMerge.Static(new List<SkinnedModel> { huge }, "tau", out refusal, out note);
        Ok(refusal == null, "refused: " + refusal);

        float lo = float.MaxValue, hi = float.MinValue;
        foreach (ObjVector3 p in m.Positions) { if (p.X < lo) lo = p.X; if (p.X > hi) hi = p.X; }
        Ok(Math.Abs((hi - lo) - 1f) < 1e-4f,
           "the longest axis must come out at 1.0 unit, got " + (hi - lo) +
           " - an exporter's scale reached the game and became a slab that occluded the soldier");

        // The SHAPE must survive: normalising is a uniform scale, not a fit-to-cube.
        float yLo = float.MaxValue, yHi = float.MinValue;
        foreach (ObjVector3 p in m.Positions) { if (p.Y < yLo) yLo = p.Y; if (p.Y > yHi) yHi = p.Y; }
        Ok(Math.Abs((yHi - yLo) - (16.972f / 35.372f)) < 1e-3f,
           "Y keeps its proportion to X - a non-uniform normalise would stretch the gun: got " + (yHi - yLo));
        Ok(note != null && note.Contains("NORMALISED"), "and the bake log says so: " + note);
        log.AppendLine("  scale   " + note.Substring(note.IndexOf("NORMALISED")));
    }

    /// <summary>THE CREATURE LINE'S GUARANTEE. A rigged mesh must never come through here.</summary>
    private static void RefusesSkinned(StringBuilder log)
    {
        List<SkinnedModel> parts = new List<SkinnedModel> { Part("a", "M", 0f), Part("spider", "M", 5f) };
        parts[1].Weights = new float[12];
        parts[1].JointNames.Add("root");
        string refusal, note;
        SkinnedModel m = MeshMerge.Static(parts, "x", out refusal, out note);
        Ok(m == null, "a SKINNED model was merged - the creature path must own that case");
        Ok(refusal != null && refusal.Contains("skin"), "refusal must name the skin, got: " + refusal);
        Ok(refusal.Contains("'spider'"), "refusal must name the offending piece, got: " + refusal);
        log.AppendLine("  skinned REFUSED " + refusal.Substring(0, Math.Min(74, refusal.Length)));
    }

    /// <summary>Half a model with UVs and half without cannot be painted by one material set.</summary>
    private static void RefusesMixedUv(StringBuilder log)
    {
        List<SkinnedModel> parts = new List<SkinnedModel>
        {
            Part("body", "M", 0f), Part("backdrop", "M", 5f, uv: false)
        };
        string refusal, note;
        SkinnedModel m = MeshMerge.Static(parts, "x", out refusal, out note);
        Ok(m == null, "a model with mixed UV coverage was merged");
        Ok(refusal != null && refusal.Contains("'backdrop'"),
           "refusal must name the piece with no UVs, got: " + refusal);
        log.AppendLine("  mixedUV REFUSED " + refusal.Substring(0, Math.Min(74, refusal.Length)));

        // ...but a model where NOBODY has UVs is uniform, and merges.
        List<SkinnedModel> bare = new List<SkinnedModel>
        {
            Part("a", "M", 0f, uv: false), Part("b", "M", 5f, uv: false)
        };
        m = MeshMerge.Static(bare, "x", out refusal, out note);
        Ok(m != null && refusal == null, "a uniformly UV-less model must still merge: " + refusal);
        Ok(m.Uv0 == null, "no UVs in, no UVs out");
    }
}
