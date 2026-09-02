using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Doctor;
using Morgott.ContentTool.Import;

/// <summary>
/// THE PROTOTYPE CATALOG, proven against the live census instead of against a game.
/// <c>internal-docs\research\rig-census-2026-09-02.json</c> is a real measurement off
/// D:\PP-Instance2 - 46 AddonsManagerDefs, 42 rigged, 37 distinct rig prefabs, 2551 transforms - so
/// which rigs are ONE binding prototype (and, above all, that Crabman and Oilcrab are NOT, despite
/// 34 shared names) is decided here rather than by baking a mesh onto the wrong creature and
/// watching it bind silently and partially.
/// </summary>
internal static class CatalogTests
{
    /// <summary>The shipped Human slot defs, taxonomy 2026-09-02 :126-129. Only the ones that carry
    /// a searchable role word are needed here - "mutoid" must find the HUMAN prototype, because
    /// Mutoid is a slot on the human rig and not a rig of its own.</summary>
    private static readonly string[] HumanSlots =
    {
        "Human_Head_SlotDef", "Human_Torso_SlotDef", "Human_Legs_SlotDef",
        "Heavy_Jetpack_SlotDef", "Mutoid_RightArmSyphonWeapon_SlotDef"
    };

    /// <summary>The four managers with <c>Rig == null</c> (taxonomy family 10). They are in the def
    /// repository, they are not picker entries, and they must not become records.</summary>
    private static readonly string[] RigLess =
    {
        "DefaultTacCharacter_AddonsManagerDef", "Dropped_AddonsManagerDef",
        "FallDown_AddonsManagerDef", "YuggothianDropped_ItemContainer_AddonsManagerDef"
    };

    internal static string Run()
    {
        int checks = 0;

        string census = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            @"..\..\..\..\..\internal-docs\research\rig-census-2026-09-02.json"));
        if (!File.Exists(census)) throw new Exception("CATALOG FAILURE: the rig census is missing at " + census);
        var root = (Dictionary<string, object>)Json.Parse(File.ReadAllText(census), 64);
        var meta = (Dictionary<string, object>)root["_meta"];

        var rigs = new List<RigScan>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object> entry in root)
        {
            if (entry.Key == "_meta") continue;
            var body = (Dictionary<string, object>)entry.Value;
            var scan = new RigScan { RigName = entry.Key };
            foreach (object manager in (List<object>)body["managers"]) scan.Managers.Add((string)manager);
            foreach (object node in (List<object>)body["bones"])
            {
                var bone = (Dictionary<string, object>)node;
                scan.Bones.Add(new PrototypeBone
                {
                    Name = (string)bone["name"],
                    Parent = bone["parent"] as string,
                    Path = (string)bone["path"]
                });
            }
            counts[scan.RigName] = (int)(double)body["count"];
            rigs.Add(scan);
        }

        // The manager side of the scan the game-side harvester will produce (task 4). Everything the
        // census knows: which manager sits on which rig. Plus the four rig-less ones, which the
        // census cannot list because it is keyed by rig.
        var managers = new List<ManagerScan>();
        foreach (RigScan rig in rigs)
            foreach (string name in rig.Managers)
                managers.Add(new ManagerScan { ManagerName = name, RigName = rig.RigName, HasRig = true });
        foreach (string name in RigLess)
            managers.Add(new ManagerScan { ManagerName = name, HasRig = false });
        foreach (ManagerScan manager in managers)
            if (manager.ManagerName == "Human_AddonsManagerDef") manager.SlotNames.AddRange(HumanSlots);

        checks += Check(rigs.Count == 37, "the census carries 37 rigs, not " + rigs.Count);
        checks += Check((int)(double)meta["distinctRigs"] == 37 && (int)(double)meta["transformsTotal"] == 2551,
                        "the census _meta still says 37 rigs / 2551 transforms");

        IList<PrototypeRecord> all = PrototypeCatalog.Build(rigs, managers);
        checks += Check(all.Count == 36,
                        "37 rigs collapse to 36 binding prototypes, not " + all.Count + " - the merge rule moved");

        // ---- THE ONLY MERGE. Two prefabs, one bone set: the 14th transform is the prefab root's
        // own name, which nothing ever binds to.
        IList<PrototypeBone> fireworm = Bones(rigs, "ALN_Fireworm_Rig_Ready");
        IList<PrototypeBone> acidworm = Bones(rigs, "ALN_Acidworm_Rig_Ready");
        checks += Check(PrototypeCatalog.Signature(fireworm) == PrototypeCatalog.Signature(acidworm),
                        "Fireworm and Acidworm share one binding signature");
        checks += Check(fireworm.Count == 14 && acidworm.Count == 14 &&
                        Shared(Names(fireworm), Names(acidworm)) == 13,
                        "Fireworm/Acidworm are 14 transforms of which 13 names are identical");

        // ---- THE PAIR THIS PICKER EXISTS FOR. Most of a naming scheme in common, and a Crabman
        // mesh still binds partially and SILENTLY on an Oilcrab.
        IList<PrototypeBone> crabman = Bones(rigs, "ALN_Crabman_Rig_Ready");
        IList<PrototypeBone> oilcrab = Bones(rigs, "ALN_Oilcrab_Protean_Rig_Ready");
        checks += Check(PrototypeCatalog.Signature(crabman) != PrototypeCatalog.Signature(oilcrab),
                        "Crabman and Oilcrab are NOT one prototype");
        int overlap = Shared(PrototypeCatalog.Bindable(crabman), PrototypeCatalog.Bindable(oilcrab));
        checks += Check(overlap >= 25, "Crabman and Oilcrab still share most of a naming scheme (" + overlap + ")");

        // ---- EXT_* relates every rig to every other, so it can never be part of a comparison.
        int without = 0;
        List<string> universal = null;
        foreach (RigScan rig in rigs)
        {
            if (PrototypeCatalog.AttachmentPoints(rig.Bones).Count == 0) without++;
            List<string> names = Names(rig.Bones);
            if (universal == null) universal = names;
            else universal.RemoveAll(name => !names.Contains(name));
        }
        checks += Check(without == 0, "every one of the 37 rigs has at least one attachment point");
        checks += Check(universal.Contains("EXT_VoiceContext"), "EXT_VoiceContext is on all 37 rigs");
        checks += Check(universal.Count == 1,
                        "EXT_VoiceContext is the ONLY name all 37 rigs share, not " + universal.Count);

        // ---- The partition loses nothing: bindable + attachment == every transform censused.
        checks += Check(PrototypeCatalog.Bindable(Bones(rigs, "CHR_Human_Rig_Ready")).Count +
                        PrototypeCatalog.AttachmentPoints(Bones(rigs, "CHR_Human_Rig_Ready")).Count ==
                        counts["CHR_Human_Rig_Ready"], "the Human partition is the whole 124-transform rig");
        checks += Check(PrototypeCatalog.Bindable(crabman).Count + PrototypeCatalog.AttachmentPoints(crabman).Count ==
                        counts["ALN_Crabman_Rig_Ready"], "the Crabman partition is the whole 58-transform rig");

        // ---- Duplicates are shipped and real. The game takes FirstOrDefault, so the second one is
        // unreachable and must never be index-matched.
        // Fishman is the CORRECTION to the design's "two duplicated wrist pairs": slice 0 read the
        // census case-INSENSITIVELY. Ordinally the rig carries Fishman_upWrist_l AND
        // Fishman_upWrist_L - two different transforms - and Addon.GetEquivalentBones compares with
        // case-sensitive string equality (Addon.cs:1202-1231, taxonomy :105-106), so BOTH are
        // reachable and neither is ambiguous. Calling that pair ambiguous would block a Fishman wrist
        // slot the game handles perfectly well.
        List<string> fishman = Names(Bones(rigs, "ALN_Fishman_Rig_Ready"));
        checks += Check(PrototypeCatalog.Ambiguous(Bones(rigs, "ALN_Fishman_Rig_Ready")).Count == 0 &&
                        fishman.Contains("Fishman_upWrist_l") && fishman.Contains("Fishman_upWrist_L"),
                        "Fishman's wrists are a CASE collision the game tells apart, not an ambiguity");
        checks += Check(PrototypeCatalog.Ambiguous(Bones(rigs, "VEH_NJ_Armadillo_Rig_Ready")).Contains("light"),
                        "the Armadillo's duplicated 'light'");
        checks += Check(PrototypeCatalog.Ambiguous(Bones(rigs, "VEH_PX_Scarab_V01_Rig_Ready")).Contains("light"),
                        "the Scarab's duplicated 'light'");
        checks += Check(PrototypeCatalog.Ambiguous(Bones(rigs, "VEH_SYN_Sanator_Rig_Ready")).Contains("light"),
                        "the Sanator's duplicated 'light'");
        checks += Check(PrototypeCatalog.Ambiguous(crabman).Count == 0, "Crabman has no ambiguous name at all");

        // ---- A manager with no rig has nothing to verify against and is not a picker entry.
        bool leaked = false;
        foreach (PrototypeRecord record in all)
            foreach (PrototypeVariant variant in record.Variants)
                if (Array.IndexOf(RigLess, variant.ManagerName) >= 0) leaked = true;
        checks += Check(!leaked, "the four rig-less managers produce no record");

        PrototypeRecord worm = Find(all, "ALN_Fireworm_Rig_Ready");
        checks += Check(worm == Find(all, "ALN_Acidworm_Rig_Ready") && worm.RigPrefabNames.Count == 2,
                        "the worm prototype is ONE record over two prefabs");
        checks += Check(worm.Variants.Count == 3 && Named(worm, "Fireworm") && Named(worm, "Acidworm") &&
                        Named(worm, "Poisonworm"), "the worm prototype has the three worm variants");

        PrototypeRecord turrets = Find(all, "CHR_NJ_TEC_Turret_T01_V01_Rig_Ready");
        checks += Check(turrets.RigPrefabNames.Count == 1 && turrets.Variants.Count == 3,
                        "the three tech turrets are one prefab and three variants");

        // ---- Search: token-AND, case-insensitive, over the record's own vocabulary. Mutoid is a
        // human SLOT, so searching it must land on the human rig rather than invent a rig.
        IList<PrototypeRecord> mutoid = PrototypeCatalog.Search(all, "mutoid");
        checks += Check(mutoid.Count == 1 && mutoid[0].RigPrefabNames.Contains("CHR_Human_Rig_Ready"),
                        "'mutoid' selects the Human prototype (" + mutoid.Count + " hit(s))");
        checks += Check(Contains(PrototypeCatalog.Search(all, "crab man"), "ALN_Crabman_Rig_Ready"),
                        "'crab man' finds Crabman");
        checks += Check(PrototypeCatalog.Search(all, "crab zzz").Count == 0,
                        "'crab zzz' finds nothing - every token has to match");

        return "CATALOG PASS, " + checks + " check(s) - 37 rigs, 36 binding prototypes, off the live census";
    }

    private static IList<PrototypeBone> Bones(IList<RigScan> rigs, string rigName)
    {
        foreach (RigScan rig in rigs) if (rig.RigName == rigName) return rig.Bones;
        throw new Exception("CATALOG FAILURE: the census has no rig named " + rigName);
    }

    private static List<string> Names(IList<PrototypeBone> bones)
    {
        var names = new List<string>();
        foreach (PrototypeBone bone in bones) if (!names.Contains(bone.Name)) names.Add(bone.Name);
        return names;
    }

    private static int Shared(IList<string> a, IList<string> b)
    {
        int n = 0;
        foreach (string name in a) if (b.Contains(name)) n++;
        return n;
    }

    private static PrototypeRecord Find(IList<PrototypeRecord> all, string rigName)
    {
        foreach (PrototypeRecord record in all) if (record.RigPrefabNames.Contains(rigName)) return record;
        throw new Exception("CATALOG FAILURE: no record covers " + rigName);
    }

    private static bool Named(PrototypeRecord record, string variantName)
    {
        foreach (PrototypeVariant variant in record.Variants) if (variant.Name == variantName) return true;
        return false;
    }

    private static bool Contains(IList<PrototypeRecord> hits, string rigName)
    {
        foreach (PrototypeRecord record in hits) if (record.RigPrefabNames.Contains(rigName)) return true;
        return false;
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("CATALOG FAILURE: " + what);
        return 1;
    }
}
