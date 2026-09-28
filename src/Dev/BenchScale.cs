using UnityEngine;

namespace Morgott.ContentTool.Dev
{
    /// <summary>
    /// ONE SCALE FOR THE WHOLE BENCH, at any resolution, aspect and window mode. The bench is laid out in
    /// VIRTUAL pixels of a 720-tall screen (the size it was designed at); <see cref="S"/> = real height / 720
    /// maps them to the window. IMGUI gets it as GUI.matrix, the native shell's canvas as its scale factor
    /// (NativeKit.K = S / 3, the game's own 3840x2160 authoring over the same height), so both grow and
    /// shrink together, and a window resized live is simply a different S on the next frame.
    ///
    /// The rule for the code: everything the bench computes is in VIRTUAL pixels (<see cref="W"/>,
    /// <see cref="H"/>, <see cref="Mouse"/>). Only the few places that talk to REAL pixels - the camera's
    /// WorldToScreenPoint / ScreenPointToRay, GL.LoadPixelMatrix, which the gizmo and the Doctor's skeleton
    /// overlay are built on - run with an identity matrix and are handed real numbers (<see cref="Real"/>).
    /// </summary>
    internal static class BenchScale
    {
        /// <summary>Below this the text stops being readable; a smaller window gets a wider virtual screen
        /// instead (and the panel's scroll views take the rest).</summary>
        private const float MinScale = 0.75f;
        internal const float DesignHeight = 720f;

        internal static float S { get { return Mathf.Max(MinScale, Mathf.Max(1f, Screen.height) / DesignHeight); } }
        internal static float W { get { return Screen.width / S; } }
        internal static float H { get { return Screen.height / S; } }
        internal static float Real(float virtualPx) { return virtualPx * S; }

        /// <summary>Input.mousePosition in virtual pixels (still y from the BOTTOM, like the original).</summary>
        internal static Vector3 Mouse { get { Vector3 m = Input.mousePosition; float s = S; return new Vector3(m.x / s, m.y / s, 0f); } }

        /// <summary>The GUI matrix for the bench's IMGUI.</summary>
        internal static Matrix4x4 Matrix { get { float s = S; return Matrix4x4.Scale(new Vector3(s, s, 1f)); } }
    }
}
