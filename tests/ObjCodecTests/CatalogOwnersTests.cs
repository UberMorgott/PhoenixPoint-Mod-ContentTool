using System;
using Morgott.ContentTool.Bake;

/// <summary>
/// WHO SERVES A VIDEO KEY TWO MODS BOTH WANT. <see cref="CatalogOwners"/> is the bookkeeping half of
/// CatalogLive and carries no UnityEngine type, so the policy is measured here: the lower mod id
/// serves whatever order the mods arrive in (BundleClaims.Keeps, the rule every route shares), the
/// other is HELD rather than lost, and one mod letting go never takes the other's row with it.
/// </summary>
internal static class CatalogOwnersTests
{
    private static int checks;

    internal static string Run()
    {
        checks = 0;
        const string key = "k";

        // ---- arrival order must not decide: the higher id first, then the lower one.
        CatalogOwners o = new CatalogOwners();
        Check(o.Want(key, "mod_b", "b.webm") == "mod_b", "a lone mod serves its key");
        Check(o.Want(key, "mod_a", "a.webm") == "mod_a" && o.PathOf(key) == "a.webm",
              "a LOWER id arriving later takes the key: " + o.Serving(key) + " -> " + o.PathOf(key));
        CatalogOwners r = new CatalogOwners();
        r.Want(key, "mod_a", "a.webm");
        Check(r.Want(key, "mod_b", "b.webm") == "mod_a" && r.PathOf(key) == "a.webm",
              "and a HIGHER id arriving later is held behind it - same winner either way");

        // ---- the held mod is remembered: the server letting go hands the key to it, not the game.
        Check(o.Drop(key, "mod_a") && o.Wanted(key) && o.Serving(key) == "mod_b" && o.PathOf(key) == "b.webm",
              "the lower id letting go serves the next one: " + o.Serving(key) + " -> " + o.PathOf(key));
        Check(o.Drop(key, "mod_b") && !o.Wanted(key) && o.Serving(key) == null,
              "and the last one letting go leaves nobody wanting the key");

        // ---- a held mod letting go never disturbs the server.
        Check(r.Drop(key, "mod_b") && r.Serving(key) == "mod_a" && r.PathOf(key) == "a.webm",
              "dropping the HELD mod leaves the serving one untouched");
        Check(!r.Drop(key, "mod_c") && r.Serving(key) == "mod_a",
              "and a mod that never wanted the key drops nothing");

        // ---- a re-register moves the path, never duplicates the owner.
        r.Want(key, "mod_a", "a2.webm");
        Check(r.PathOf(key) == "a2.webm" && r.PathFor(key, "mod_a") == "a2.webm" && r.Served().Count == 1,
              "the same mod registering again moves its path: " + r.PathOf(key));

        // ---- the anonymous owner (CatalogLive.Register without a mod id) sorts below every id.
        r.Want(key, "", "anon.webm");
        Check(r.Serving(key) == "" && r.PathOf(key) == "anon.webm",
              "the anonymous owner outranks every mod id - what a direct Register always did");

        // ---- ORDINAL, like BundleClaims.Keeps: 'Z' (0x5A) sorts before 'a' (0x61).
        CatalogOwners c = new CatalogOwners();
        c.Want(key, "a_mod", "lower.webm");
        c.Want(key, "Z_mod", "upper.webm");
        Check(c.Serving(key) == "Z_mod" && BundleClaims.Keeps("Z_mod", "a_mod"),
              "ownership is the ordinal order BundleClaims.Keeps uses: " + c.Serving(key));

        return "CATALOG-OWNERS PASS, " + checks + " check(s)";
    }

    private static void Check(bool condition, string what)
    {
        if (!condition) throw new Exception("CATALOG-OWNERS FAILURE: " + what);
        checks++;
    }
}
