namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// The one piece of <see cref="SkinFields"/> that is pure arithmetic over a glTF weight array and
    /// touches no AssetsTools type. It lives in its own file so the UnityEngine-free command-line tools
    /// (tools\ClipEvents, tools\SpiderAxisCheck) can compile the import path that calls it without
    /// dragging AssetsTools.NET in behind it.
    /// </summary>
    // Split out of SkinFields.cs so a UnityEngine-free tool can link this half alone.
    internal static partial class SkinFields
    {
        /// <summary>
        /// The heaviest of a glTF vertex's four influences, dominant first, one per slot of
        /// <paramref name="slots"/>; -1 for a slot the file has no non-zero weight left for. Shared
        /// with the P6 arm so the gate and the bake cannot disagree about WHICH of the four survive a
        /// narrower target - the arm's own question is which BONE they land on, and that it derives
        /// independently.
        /// </summary>
        internal static void Heaviest(float[] weights, int vertex, int[] slots)
        {
            for (int s = 0; s < slots.Length; s++)
            {
                int best = -1;
                for (int k = 0; k < 4; k++)
                {
                    if (weights[vertex * 4 + k] <= 0f) continue;
                    bool taken = false;
                    for (int t = 0; t < s; t++) if (slots[t] == k) { taken = true; break; }
                    if (taken) continue;
                    if (best < 0 || weights[vertex * 4 + k] > weights[vertex * 4 + best]) best = k;
                }
                slots[s] = best;
            }
        }
    }
}
