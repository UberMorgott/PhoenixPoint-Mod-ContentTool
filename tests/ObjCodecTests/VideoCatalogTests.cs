using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Bake;

/// <summary>
/// The streamable Catalog.json surgery, OFFLINE and against the REAL shipped file - the same
/// <see cref="CatalogText"/> the mod compiles, so a green run here is the code that lands in the
/// game's folder. Nothing is written: every arm is a string in, a string out.
///
/// What it is for: ADDING a video means APPENDING a row, and an append is the only operation in
/// this tool that can produce a duplicate RuntimeKey. StreamableAssetsCatalog.cs:22 does
/// ToDictionary(l =&gt; l.RuntimeKey) inside Awake, so a duplicate does not degrade anything - it
/// throws and the game never boots. That is why the refusal is asserted here, on the shipped
/// catalog's own keys, before any of it can reach a player's install.
///
/// The game install is machine-specific, so a missing file is VOID, never PASS.
/// </summary>
internal static class VideoCatalogTests
{
    private static int checks;

    internal static string Run()
    {
        string root = Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point";
        string path = Path.Combine(root, @"PhoenixPointWin64_Data\StreamingAssets\StreamableCopiedAssets\Catalog.json");
        if (!File.Exists(path)) return "VIDEO catalog VOID - no " + path + " (set PPRoot to the game folder)";
        string pristine = File.ReadAllText(path);

        // ---- what the shipped file actually is. Every later number is relative to these two.
        List<CatalogText.Row> rows = CatalogText.Rows(pristine);
        Check(rows.Count > 1, "the shipped catalog parses to " + rows.Count + " row(s)");
        Check(CatalogText.DuplicateKey(pristine) == null, "and no shipped RuntimeKey appears twice");
        Check(CatalogText.Guard(pristine) == null, "so the shipped catalog passes the write guard unchanged");

        // ---- REPLACE: mutate in place, row count fixed.
        CatalogText.Rec a = new CatalogText.Rec("ct_a", rows[0].Key, "StreamableCopiedAssets/Videos/ct_a/a.webm");
        string repl = CatalogText.Rebuild(pristine, new List<CatalogText.Rec> { a });
        Check(CatalogText.PathOf(repl, a.Key) == a.Path, "a replacement rewrites its row's StreamingPath");
        Check(CatalogText.Rows(repl).Count == rows.Count,
              "and neither adds nor drops a row: " + CatalogText.Rows(repl).Count + " == " + rows.Count);

        // ---- ADD: a key the shipped file does not have is APPENDED.
        string key = CatalogText.KeyFor("ct_c", "newclip");
        Check(key.Length == 32 && key == CatalogText.KeyFor("ct_c", "newclip"),
              "the derived RuntimeKey is 32 hex and deterministic: ct_c/newclip -> " + key);
        Check(CatalogText.KeyFor("ct_c", "other") != key && CatalogText.KeyFor("ct_other", "newclip") != key,
              "a different clip name and a different mod id each derive a different key (" +
              CatalogText.KeyFor("ct_c", "other") + ", " + CatalogText.KeyFor("ct_other", "newclip") + ")");
        Check(CatalogText.PathOf(pristine, key) == null,
              "and the derived key is not one of the " + rows.Count + " keys Phoenix Point ships");

        CatalogText.Rec add = new CatalogText.Rec("ct_c", key, "StreamableCopiedAssets/Videos/ct_c/newclip.webm");
        string added = CatalogText.Rebuild(pristine, new List<CatalogText.Rec> { a, add });
        Check(CatalogText.Rows(added).Count == rows.Count + 1,
              "one add grows the catalog by exactly one row: " + CatalogText.Rows(added).Count + " == " + rows.Count + " + 1");
        Check(CatalogText.PathOf(added, add.Key) == add.Path,
              "the appended row resolves: " + add.Key + " -> " + CatalogText.PathOf(added, add.Key));
        // POSITIVE identity for the surrounding text: the edited row still carries THAT edit and the
        // LAST shipped row still carries the exact StreamingPath it shipped with. An append that
        // clipped the tail of the file, or landed inside another object, comes out RED here.
        Check(CatalogText.PathOf(added, a.Key) == a.Path,
              "the replacement in the same text is untouched by the append");
        Check(CatalogText.PathOf(added, rows[rows.Count - 1].Key) == rows[rows.Count - 1].Path,
              "and the last shipped row still reads " + rows[rows.Count - 1].Path);
        Check(CatalogText.Guard(added) == null, "the appended catalog passes the write guard");

        // ---- THE arm. Append a key the shipped catalog already carries and prove the guard refuses.
        //      The row-count assertion is what keeps it non-vacuous: the append really happened, so
        //      the refusal is a refusal of a real duplicate and not of a no-op.
        string dup = CatalogText.Append(pristine, new CatalogText.Rec("ct_d", rows[0].Key, "StreamableCopiedAssets/Videos/ct_d/x.webm"));
        Check(CatalogText.Rows(dup).Count == rows.Count + 1,
              "a colliding append really lands in the text (" + CatalogText.Rows(dup).Count + " rows)");
        Check(CatalogText.DuplicateKey(dup) == rows[0].Key,
              "the duplicate is FOUND, and it is the shipped key " + rows[0].Key);
        string refused = CatalogText.Guard(dup);
        Check(refused != null && refused.Contains(rows[0].Key),
              "and the guard every write goes through REFUSES that catalog by name -> " +
              (refused == null ? "(NOT REFUSED)" : refused.Substring(0, Math.Min(60, refused.Length)) + "..."));

        // ---- the reason the whole file exists: what the GAME would do with each of those two texts.
        //      ToDictionary throws on the duplicate and returns on the clean one - measured here with
        //      the game's own operation rather than assumed.
        Check(!ToDictionaryThrows(added), "ToDictionary(RuntimeKey) over the appended catalog succeeds - the boot scene would live");
        Check(ToDictionaryThrows(dup), "ToDictionary(RuntimeKey) over the colliding one THROWS - which is the boot the guard prevents");

        return "VIDEO catalog PASS, " + checks + " check(s) - " + rows.Count + " shipped rows, add -> " +
               (rows.Count + 1) + ", duplicate key refused by name";
    }

    /// <summary>The game's own line, StreamableAssetsCatalog.cs:22, over a parsed catalog.</summary>
    private static bool ToDictionaryThrows(string json)
    {
        Dictionary<string, string> d = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (CatalogText.Row r in CatalogText.Rows(json)) d.Add(r.Key, r.Path);
            return false;
        }
        catch (ArgumentException) { return true; }
    }

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception("VIDEO catalog FAIL: " + what);
    }
}
