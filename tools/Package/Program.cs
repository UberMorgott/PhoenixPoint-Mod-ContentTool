using System;
using Morgott.ContentTool.Project;

/// <summary>
/// The packager's entry point. All of it is <see cref="Package"/>, which is compiled into the mod
/// as well - this exists so a modder can build a release with the game shut.
///   dotnet run --project tools\Package -c Release -- &lt;authorFolder&gt; &lt;outFolder&gt; [assembly.dll]
/// Normally reached through package.ps1, which builds the mod's own DLL first.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: Package <authorFolder> <outFolder> [assembly.dll]");
            return 2;
        }
        bool ok;
        Console.WriteLine(Package.Run(args[0], args[1], args.Length > 2 ? args[2] : null, out ok));
        return ok ? 0 : 1;
    }
}
