using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

// Reads SHIPPED AnimationClips and reports what each one actually binds: how many curves per
// Transform attribute, on how many distinct transform paths, and - for position curves - the path
// hash plus the first sampled value. Answers one question and nothing else: does a PP clip write
// localPosition on every bone, or only on the root?
//
//   ClipCensus.exe <classdata.tpk> <bundle> [nameSubstring]   > census.jsonl
//
// Output is one JSON object per clip on stdout; hashes are resolved to bone names by the caller
// (tools\ppskel.py knows the PP rig's paths).
internal static class Program
{
    private static int Width(uint attribute)
    {
        if (attribute == 1) return 3;   // localPosition
        if (attribute == 2) return 4;   // localRotation
        if (attribute == 3) return 3;   // localScale
        return 1;
    }

    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--export") return Export.Run(args);
        if (args.Length > 0 && args[0] == "--fields") return Export.Fields(args);
        if (args.Length > 0 && args[0] == "--selfcheck") return Export.SelfCheck();
        if (args.Length < 2) { Console.Error.WriteLine("usage: ClipCensus <classdata.tpk> <bundle> [filter]"); return 2; }
        string filter = args.Length > 2 ? args[2] : null;

        AssetsManager m = new AssetsManager();
        m.LoadClassPackage(args[0]);
        BundleFileInstance bun = m.LoadBundleFile(args[1], true);
        AssetsFileInstance af = m.LoadAssetsFileFromBundle(bun, 0, false);
        m.LoadClassDatabaseFromPackage(af.file.Metadata.UnityVersion);
        try
        {
            foreach (AssetFileInfo info in af.file.Metadata.GetAssetsOfType(AssetClassID.AnimationClip))
            {
                AssetTypeValueField clip = m.GetBaseField(af, info);
                string name = clip["m_Name"].IsDummy ? "?" : clip["m_Name"].AsString;
                if (filter != null && name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0) continue;

                AssetTypeValueField muscle = clip["m_MuscleClip"];
                AssetTypeValueField data = muscle["m_Clip"]["data"];
                AssetTypeValueField dense = data["m_DenseClip"];
                int frames = dense["m_FrameCount"].AsInt;
                int denseCurves = (int)dense["m_CurveCount"].AsUInt;
                AssetTypeValueField samples = dense["m_SampleArray"]["Array"];
                int streamCurves = (int)data["m_StreamedClip"]["curveCount"].AsUInt;
                AssetTypeValueField constants = data["m_ConstantClip"]["data"]["Array"];

                var perAttribute = new Dictionary<uint, int>();
                var paths = new HashSet<uint>();
                var posPaths = new List<uint>();
                var posValues = new List<string>();
                var scalePaths = new List<uint>();
                var scaleValues = new List<string>();
                int offset = 0, streamedPos = 0;

                foreach (AssetTypeValueField b in clip["m_ClipBindingConstant"]["genericBindings"]["Array"].Children)
                {
                    uint attribute = b["attribute"].AsUInt, path = b["path"].AsUInt;
                    int typeId = b["typeID"].AsInt;
                    int width = Width(attribute);
                    if (typeId == 4)
                    {
                        perAttribute.TryGetValue(attribute, out int had);
                        perAttribute[attribute] = had + 1;
                        paths.Add(path);
                        if (attribute == 3)
                        {
                            scalePaths.Add(path);
                            scaleValues.Add(Sample(offset, width, streamCurves, denseCurves, samples, constants, frames) ?? "null");
                        }
                        if (attribute == 1)
                        {
                            posPaths.Add(path);
                            string v = Sample(offset, width, streamCurves, denseCurves, samples, constants, frames);
                            if (v == null) streamedPos++;
                            posValues.Add(v ?? "null");   // index-parallel to posPaths
                        }
                    }
                    offset += width;
                }

                Console.WriteLine("{\"clip\":\"" + name + "\",\"bindings\":" + offset +
                    ",\"frames\":" + frames + ",\"denseCurves\":" + denseCurves +
                    ",\"streamCurves\":" + streamCurves + ",\"constFloats\":" + constants.Children.Count +
                    ",\"pos\":" + Get(perAttribute, 1) + ",\"rot\":" + Get(perAttribute, 2) +
                    ",\"scale\":" + Get(perAttribute, 3) + ",\"paths\":" + paths.Count +
                    ",\"streamedPos\":" + streamedPos +
                    ",\"posPaths\":[" + string.Join(",", posPaths) + "]" +
                    ",\"scalePaths\":[" + string.Join(",", scalePaths) + "]" +
                    ",\"posFirst\":[" + string.Join(",", posValues) + "]" +
                    ",\"scaleFirst\":[" + string.Join(",", scaleValues) + "]}");
            }
        }
        finally { m.UnloadAll(); }
        return 0;
    }

    private static int Get(Dictionary<uint, int> d, uint k) { d.TryGetValue(k, out int v); return v; }

    /// <summary>The binding's first sampled value, or null when it lives in the streamed bank.</summary>
    private static string Sample(int offset, int width, int streamCurves, int denseCurves,
                                 AssetTypeValueField samples, AssetTypeValueField constants, int frames)
    {
        if (offset < streamCurves) return null;
        var parts = new List<string>();
        for (int i = 0; i < width; i++)
        {
            int at = offset + i - streamCurves;
            float v;
            if (at < denseCurves)
            {
                if (frames <= 0 || at >= samples.Children.Count) return null;
                v = samples.Children[at].AsFloat;              // frame 0 = the first denseCurves floats
            }
            else
            {
                int c = at - denseCurves;
                if (c >= constants.Children.Count) return null;
                v = constants.Children[c].AsFloat;
            }
            parts.Add(v.ToString("0.#####", CultureInfo.InvariantCulture));
        }
        return "[" + string.Join(",", parts) + "]";
    }
}
