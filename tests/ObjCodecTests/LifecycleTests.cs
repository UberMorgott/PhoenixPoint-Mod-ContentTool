/// <summary>Step 0 stub - registered in ObjCodecTests.csproj and Program.cs BEFORE any arm is written,
/// so the RED run below is a run that actually happens (EnableDefaultCompileItems=false silently drops a
/// file that is not in the Compile list, and a gate nobody compiles is a gate nobody fails).</summary>
internal static class LifecycleTests
{
    internal static string Run()
    {
        return "LIFECYCLE PASS, 0 check(s)";
    }
}
