using System;

namespace Morgott.ContentTool.Import
{
    /// <summary>
    /// Stable identity of an import refusal. The eight named ones are the cases the Model Doctor
    /// gives an author a Blender action for; everything else the reader refuses - roughly eighty-five
    /// further Bad(...) exits, each with its own good sentence - is MalformedGlb, which is honest:
    /// the message already names the cause, and the code only decides how the UI groups the row.
    /// </summary>
    internal enum ImportCode
    {
        MalformedGlb, UnsupportedGlb,
        Oversize, ExternalBuffer, NoMesh, NonTriangle, NotIndexed, TooManyInfluences,
        NoVertices, NoNormals
    }

    /// <summary>
    /// What GlbReader and ModelBuild already threw, plus the code. Deriving from FormatException is
    /// deliberate and load-bearing: LiveMesh.cs:52, BundleBaker.cs:197 and
    /// tests\ObjCodecTests\BoneNames.cs:100 all catch FormatException today, and adding a code must
    /// not quietly change which of them stops catching.
    /// </summary>
    internal sealed class ImportRefusedException : FormatException
    {
        internal ImportRefusedException(ImportCode code, string message) : base(message) { Code = code; }

        internal ImportCode Code { get; }
    }
}
