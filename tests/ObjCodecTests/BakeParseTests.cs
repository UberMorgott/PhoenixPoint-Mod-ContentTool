using System;
using Morgott.ContentTool.Bake;

/// <summary>
/// The two manifest strings the bake PARSES rather than copies: a "tint" colour and a "loop"/"play"
/// clip glob. Both are typed by hand, and both used to accept or refuse the wrong thing - a colour
/// with a space in it parsed, and a suffix glob refused a name whose END it matched.
/// </summary>
internal static class BakeParseTests
{
    private static int checks;

    internal static string Run()
    {
        checks = 0;
        float[] rgb;
        string why;

        // ---- HexColor: six hex DIGITS, nothing else - white space is not a digit.
        Check(HexColor.TryParse("#3FA9FF", out rgb, out why) && Math.Abs(rgb[0] - 0x3F / 255f) < 1e-6,
              "a plain #RRGGBB parses: " + why);
        foreach (string bad in new[] { "#00FF 0", "# 0FF00", "00FF0 ", "#+1FF00" })
            Check(!HexColor.TryParse(bad, out rgb, out why) && why != null,
                  "'" + bad + "' is refused, not read as a colour: " + (why ?? "PARSED"));

        // ---- ClipFields.Wants: a trailing part is a SUFFIX, wherever its first occurrence is.
        Check(ClipFields.Wants("*_Loop", "MV_Loop_Run_Loop"), "'*_Loop' matches 'MV_Loop_Run_Loop'");
        Check(ClipFields.Wants("MV_*_Loop", "MV_Loop_Run_Loop"), "'MV_*_Loop' matches it too");
        Check(!ClipFields.Wants("*_Loop", "MV_Loop_Run"), "but not a name that does not END in _Loop");
        Check(!ClipFields.Wants("A*A", "A"), "a start and an end part cannot share one character");
        Check(ClipFields.Wants("*_loop_*", "MV_Loop_Run") && ClipFields.Wants("Idle*", "idle_a"),
              "middle and prefix globs, case-insensitive, unchanged");
        Check(ClipFields.Wants("Walk", "walk") && !ClipFields.Wants("Walk", "Walk2"),
              "a name with no '*' is still an exact, case-insensitive match");

        return "BAKE-PARSE PASS, " + checks + " check(s)";
    }

    private static void Check(bool condition, string what)
    {
        if (!condition) throw new Exception("BAKE-PARSE FAILURE: " + what);
        checks++;
    }
}
