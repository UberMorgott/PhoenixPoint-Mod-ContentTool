using System.Globalization;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// ============ "#RRGGBB" OUT OF A MANIFEST, AS THREE NUMBERS ============
    ///
    /// The manifest's <c>"tint"</c> key is the one place a content author types a COLOUR, and a
    /// colour typed wrong is the worst kind of wrong: <c>"#00FF0"</c> parsed leniently is a green
    /// that is almost right, which is indistinguishable in play from the green that was asked for.
    /// So the parse is strict and REFUSES BY NAME, and it is deliberately free of UnityEngine so the
    /// refusal is measurable offline instead of only inside a game session.
    ///
    /// ponytail: six digits only - no "#RGB", no "#RRGGBBAA". A projectile's alpha comes from its
    /// own gradient's alpha keys, which the tint multiply deliberately leaves alone, so an alpha
    /// here would have nowhere honest to go. Add the short form when someone actually types one.
    /// </summary>
    internal static class HexColor
    {
        /// <summary>
        /// "#RRGGBB" (the leading '#' optional) to three 0..1 channels. False with a reason for
        /// anything else - never a silent fallback colour.
        /// </summary>
        internal static bool TryParse(string text, out float[] rgb, out string why)
        {
            rgb = null;
            why = null;
            string s = text == null ? "" : text.Trim();
            if (s.Length > 0 && s[0] == '#') s = s.Substring(1);
            if (s.Length != 6)
            {
                why = "a colour is six hex digits, optionally behind a '#', e.g. \"#3FA9FF\"; got '" +
                      (text ?? "") + "'";
                return false;
            }
            float[] made = new float[3];
            for (int i = 0; i < 3; i++)
            {
                int v;
                if (!int.TryParse(s.Substring(i * 2, 2), NumberStyles.HexNumber,
                                  CultureInfo.InvariantCulture, out v))
                {
                    why = "'" + s.Substring(i * 2, 2) + "' in '" + text + "' is not a hex byte";
                    return false;
                }
                made[i] = v / 255f;
            }
            rgb = made;
            return true;
        }
    }
}
