using System;
using System.IO;
using Morgott.ContentTool.Tactical;

/// <summary>
/// THE ONE KEY THAT DECIDES WHETHER A MOD TOUCHES THE PLAYER'S CAMPAIGN START, offline.
///
/// <c>"startingRoster": true</c> is the whole opt-in for putting a code-less mod's creature aboard the
/// starting aircraft (CreatureBuild's StartingSquad postfixes). Both directions are expensive to get
/// wrong and neither shows up in a bake log:
///  * a FALSE POSITIVE - "false", a typo, or a key belonging to some other block read as an opt-in -
///    silently rewrites the starting squad of every player who installed the mod for its model;
///  * a FALSE NEGATIVE - a declared "true" that parses to false - is a mod that loads, builds its
///    creature and puts it nowhere, which is exactly the silence this key exists to end.
///
/// The parser is hand-rolled regex over the raw text (CreatureManifest.Parse says why), so the nesting
/// arm is not paranoia: <c>Flat()</c> is the only thing stopping a key inside "clips" from being read
/// as a key of the creature itself.
///
/// <c>"replaceBody"</c> is checked here too, and for the same reason rather than out of convenience:
/// it is the OTHER key whose false positive rewrites something the player already had - a shipped
/// character's body - and whose false negative is a mod that loads and changes nothing while saying
/// it did. Both keys are one scalar read off the same flattened block.
/// </summary>
internal static class StartRoster
{
    private static int checks;

    internal static string Run()
    {
        checks = 0;

        // ---- the opt-in, and ONLY the opt-in
        Check(Parsed("\"startingRoster\": true").StartingRoster,
              "\"startingRoster\": true is the opt-in");
        Check(Parsed("\"startingRoster\": \"true\"").StartingRoster,
              "a quoted true is the same answer - the manifest is hand-edited and both spellings occur");
        Check(!Parsed("\"startingRoster\": false").StartingRoster,
              "false leaves the campaign start alone");
        Check(!Parsed("\"name\": \"X\"").StartingRoster,
              "a manifest that never mentions the key does not touch the campaign start - the standing " +
              "behaviour of every creature mod built before it existed");
        Check(!Parsed("\"startingRoster\": yes").StartingRoster,
              "only the literal true opts in; a typo must not rewrite a player's starting squad");
        Check(!CreatureManifest.None.StartingRoster,
              "the engine's own defaults never add anybody");

        // ---- a key of the same name INSIDE a nested block is not this key
        Check(!CreatureManifest.Parse(
                  "{\"creature\": {\"clips\": {\"startingRoster\": \"true\"}, \"name\": \"X\"}}")
              .StartingRoster,
              "a \"startingRoster\" spelled inside \"clips\" is a clip name, not the opt-in - " +
              "CreatureManifest.Flat() is what keeps the two apart");

        // ---- THE ONE MOD THAT ASKS FOR IT, read off the file the game reads
        string ppfit = Path.Combine(Root(), "local\\PpFit\\ppcontent.json");
        if (File.Exists(ppfit))
        {
            CreatureManifest m = CreatureManifest.Parse(File.ReadAllText(ppfit));
            Check(m.StartingRoster, "the PpFit bench mod asks for the campaign start");
            Check(m.Donor == "PX_SniperStarting_TacCharacterDef",
                  "...as a SNIPER: the class is the donor template's, so the role is data and not code " +
                  "(def name read live out of sharedassets0.assets, not guessed)");
        }

        // ---- THE RECIPE DEMO: a manifest is its ENTIRE content, so the manifest is what is checked.
        // Two ways it rots silently, both invisible until a player starts a campaign: the opt-in or a
        // blocking role goes missing (a mod that loads, builds nobody, and says nothing), or somebody
        // "fixes" the demo by committing a model into it - which is the licence rule broken, not a
        // convenience (SOURCES.md: nothing here may be redistributed).
        string recipe = Path.Combine(Root(), "demos\\HumanoidSoldier");
        string recipeJson = File.ReadAllText(Path.Combine(recipe, "ppcontent.json"));
        CreatureManifest hs = CreatureManifest.Parse(recipeJson);
        Check(hs.StartingRoster, "the HumanoidSoldier demo asks for the campaign start - it is the " +
              "whole point of the recipe, and nothing else in the mod would put the soldier anywhere");
        Check(hs.Donor == "PX_SniperStarting_TacCharacterDef",
              "...as a SNIPER: the class is the donor template's, so the role stays data and not code");
        Check(hs.Model == "soldier", "the manifest names the stem the drop slot tells a modder to use " +
              "(Content\\Models\\DROP-YOUR-MODEL-HERE.txt) - a mismatch is ct_creature FAIL, not a model");
        foreach (string role in CreatureRoles.Fill)
            Check(recipeJson.Contains("\": \"" + role + "\""), "the HumanoidSoldier demo maps '" + role +
                  "' - a blocking role left to the auto-fill would take a stand-in clip from a model " +
                  "that ships none of its own");
        Check(File.Exists(Path.Combine(recipe, "Content\\Models\\soldier.glb")),
              "the HumanoidSoldier demo SHIPS its model, so it works as installed - a manifest whose " +
              "\"model\" stem no file answers to is ct_creature FAIL and a demo nobody can run");

        // ---- THE REPLACE HALF: the same five roles, landing on a SHIPPED character instead of a new
        // def. Every arm below is the mirror of one above, because the two halves differ by ONE key.
        string swap = Path.Combine(Root(), "demos\\ReplaceCharacterBody");
        string swapJson = File.ReadAllText(Path.Combine(swap, "ppcontent.json"));
        CreatureManifest rb = CreatureManifest.Parse(swapJson);
        Check(rb.ReplaceBody == "S_SY_Eileen_CharacterTemplateDef",
              "the ReplaceCharacterBody demo names the shipped character it re-bodies - the def name " +
              "read live out of a running game's DefRepository, not guessed");
        Check(rb.Donor == "Swarmer_TacCharacterDef",
              "...and states no \"donor\": \"replaceBody\" REPLACES it, so the manifest must not carry " +
              "both and the field is left at the engine default nothing reads on this path");
        Check(!rb.StartingRoster,
              "a body swap does NOT touch the campaign start - the character is already wherever the " +
              "story puts her, and boarding her twice is not what \"same person, new body\" means");
        Check(rb.Model == "body", "the manifest names the stem the shipped model uses");
        Check(File.Exists(Path.Combine(swap, "Content\\Models\\body.glb")),
              "the ReplaceCharacterBody demo SHIPS its model too");
        foreach (string role in CreatureRoles.Fill)
            Check(swapJson.Contains("\": \"" + role + "\""), "the ReplaceCharacterBody demo maps '" +
                  role + "' - a swapped body plays the clips in ITS file and nothing else");

        // ---- and the key is INERT everywhere else, which is the whole safety property: every mod
        // written before it existed must still mint its own def and touch nothing shipped.
        Check(CreatureManifest.Parse(recipeJson).ReplaceBody.Length == 0,
              "the HumanoidSoldier demo does NOT set it - the ADD half mints a def and rewrites nobody");
        Check(CreatureManifest.None.ReplaceBody.Length == 0,
              "the engine's own defaults never replace a shipped character's body");
        Check(!CreatureManifest.Parse(
                  "{\"creature\": {\"clips\": {\"replaceBody\": \"X_TacCharacterDef\"}}}")
              .ReplaceBody.Equals("X_TacCharacterDef"),
              "a \"replaceBody\" spelled inside \"clips\" is a clip name, not the key - Flat() again");
        Check(CreatureManifest.Parse(
                  "{\"replace\": [ { \"texture\": \"t\" } ], \"creature\": {\"name\": \"X\"}}")
              .ReplaceBody.Length == 0,
              "the TOP-LEVEL \"replace\" array (textures, materials, meshes) is not this key - the two " +
              "are spelled differently on purpose and this one is read off the creature block only");

        // ---- the shipped demo does NOT, so its behaviour is byte-for-byte what it was
        string demo = Path.Combine(Root(), "demos\\CustomCreature\\ppcontent.json");
        Check(!CreatureManifest.Parse(File.ReadAllText(demo)).StartingRoster,
              "the CustomCreature demo still joins the squad from its OWN two postfixes, so this key " +
              "changes nothing for it - and JoinPlayerVehicle's template-identity guard is what keeps " +
              "a mod that does both from boarding two soldiers");

        return "START-ROSTER: ALL PASS, " + checks + " check(s)";
    }

    /// <summary>One scalar, in a minimal but real "creature" block.</summary>
    private static CreatureManifest Parsed(string keys)
    {
        return CreatureManifest.Parse("{\"id\": \"t\", \"creature\": {" + keys + "}}");
    }

    private static string Root()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "demos")))
            dir = Path.GetDirectoryName(dir.TrimEnd('\\'));
        if (dir == null) throw new Exception("ContentTool root not found above " +
                                             AppDomain.CurrentDomain.BaseDirectory);
        return dir;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("Check failed: " + name);
        checks++;
    }
}
