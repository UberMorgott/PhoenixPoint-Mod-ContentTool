using System;
using System.Collections.Generic;
using Morgott.ContentTool.Import;

/// <summary>
/// THE GAME RENAMES THE BONES IT ATTACHES TO, AND A RIPPED RIG CARRIES THOSE NAMES. Phoenix Point
/// decorates an attachment point's transform at runtime with its own format -
/// <c>Addon.MovedBoneNameFormat = "#{0}_Addon =&gt; {1}"</c>
/// (decompiled\AssemblyCSharp\Assembly-CSharp\src\PhoenixPoint.Common.Entities.Addons\Addon.cs:143,
/// written at :1250) - so a body part exported out of a live scene names its joints
/// '#Root_Addon =&gt; PX_Heavy_Torso_BodyPartDef' where the shipped mesh CHR_PX_HVY_TS_M_V01 says
/// 'Root'. The Siren shows the same shape ('#LowerArm_Slasher_R_Addon =&gt; ...WeaponDef'), so this
/// is a whole CLASS of files, not one author's mistake.
///
/// The intersection with the shipped skeleton was 0 of 10, SkinBinder refused, and the bake fell back
/// to nearest-bone with one full-weight influence per vertex - the "lost weights and positions" the
/// files came in with. Measured from the bake log: "the file's own weights were NOT used: the file
/// does not contain the bone 'Root', which this model's skeleton has".
/// </summary>
internal static class BoneNames
{
    private const string Def = "PX_Heavy_Torso_BodyPartDef";

    internal static string Run()
    {
        // ---- the decorated file binds BY NAME, and lands on the right bones. The live rig lists the
        // two bones in the OPPOSITE order, so a pass here cannot come from the slots happening to line
        // up - the same trap SkinRoundTrip's fixture is built around.
        SkinnedModel file = Model("#Root_Addon => " + Def, "#Neck_Addon => " + Def);
        ushort[] joints;
        float[][] bindposes;
        SkinBinder.Bind(file, new[] { "Neck", "Root" }, 0, null, out joints, out bindposes);
        int checks = Check(joints[0] == 1 && joints[4] == 0,
            "vertex 0 is weighted to the file's joint 0 ('" + file.JointNames[0] + "') and vertex 1 to " +
            "joint 1, which are live bones 1 and 0: got " + joints[0] + " and " + joints[4] +
            " - the decoration is not being read off the name");
        checks += Check(bindposes[0][12] == 2f && bindposes[1][12] == 1f,
            "each live bone got ITS OWN bind pose back, not the other one's: " +
            bindposes[0][12] + " and " + bindposes[1][12]);

        // ---- an EXACT name still wins, so a plainly-named file is untouched by any of this. Here the
        // file carries both 'Root' and a decorated '#Root_Addon => ...', and the plain one must take
        // the bone - which leaves the decorated one unmatched, and unmatched is refused as it always was.
        checks += Refuses(Model("Root", "#Root_Addon => " + Def), new[] { "Root" }, "the file adds the bone",
            "an exact bone name wins over a decorated one");

        // ---- two joints that decorate the SAME bone are a genuine ambiguity: refused by name rather
        // than resolved by which one happens to come first.
        checks += Refuses(Model("#Root_Addon => A", "#Root_Addon => B"), new[] { "Root", "Neck" },
            "both name the bone 'Root'", "two joints decorating one bone are refused");

        // ---- and a name that only LOOKS decorated is left alone, or a real bone called '#weird' would
        // silently become something else.
        checks += Check(SkinBinder.Plain("#Root") == "#Root" && SkinBinder.Plain("Root") == "Root" &&
                        SkinBinder.Plain("#_Addon => X") == "#_Addon => X" &&
                        SkinBinder.Plain("#R.Shoulder_Addon => " + Def) == "R.Shoulder",
            "only the engine's own format is undecorated: '" + SkinBinder.Plain("#Root") + "', '" +
            SkinBinder.Plain("#_Addon => X") + "', '" + SkinBinder.Plain("#R.Shoulder_Addon => " + Def) + "'");

        return "BONE-NAMES PASS, " + checks + " check(s) - '#Root_Addon => " + Def +
               "' binds onto the shipped 'Root' by name; exact names win, a doubled decoration is refused";
    }

    /// <summary>Two vertices, one full-weight influence each, one bind pose per joint distinguishable
    /// by its translation (1 and 2 on X) so a transposed map cannot read as a correct one.</summary>
    private static SkinnedModel Model(params string[] jointNames)
    {
        var m = new SkinnedModel
        {
            Positions = new[] { new ObjVector3(0f, 0f, 0f), new ObjVector3(1f, 0f, 0f) },
            Joints = new ushort[] { 0, 0, 0, 0, 1, 0, 0, 0 },
            Weights = new[] { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f },
            InverseBindMatrices = new float[jointNames.Length][]
        };
        for (int j = 0; j < jointNames.Length; j++)
        {
            m.JointNames.Add(jointNames[j]);
            m.InverseBindMatrices[j] = new[] { 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, j + 1f, 0f, 0f, 1f };
        }
        m.Submeshes.Add(new[] { 0, 1, 0 });
        return m;
    }

    private static int Refuses(SkinnedModel file, string[] bones, string cause, string what)
    {
        try
        {
            ushort[] joints;
            float[][] bindposes;
            SkinBinder.Bind(file, bones, 0, null, out joints, out bindposes);
        }
        catch (FormatException e)
        {
            return Check(e.Message.IndexOf(cause, StringComparison.Ordinal) >= 0,
                what + " - refused, but for the wrong reason: " + e.Message);
        }
        throw new Exception("BONE-NAMES FAILURE: " + what + " - it bound instead of refusing");
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("BONE-NAMES FAILURE: " + what);
        return 1;
    }
}
