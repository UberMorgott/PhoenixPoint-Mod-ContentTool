using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Project;

/// <summary>The DISK half of the bench's Sounds, Videos and Add-a-weapon screens: every press lands a file
/// where the existing verbs read it and a row the existing readers parse - and never leaves a row without its
/// file, a second file for one sound, or someone else's bytes overwritten.</summary>
internal static class TaskScaffoldTests
{
    internal static string Run()
    {
        int checks = 0;
        string dir = Path.Combine(Path.GetTempPath(), "ct_tasks_" + Guid.NewGuid().ToString("N"));
        string mods = Path.Combine(dir, "Mods");
        string modDir = Path.Combine(mods, "ContentTool");
        Directory.CreateDirectory(modDir);
        try
        {
            // ---- EnsureProject: a sibling of ContentTool, manifest + meta, idempotent.
            string root = ProjectScaffold.EnsureProject(modDir, "MySounds");
            checks += Check(root == Path.Combine(mods, "MySounds"), "the project is a sibling of ContentTool: " + root);
            checks += Check(File.Exists(Path.Combine(root, "ppcontent.json")) && File.Exists(Path.Combine(root, "meta.json")),
                            "ppcontent.json and meta.json are written");
            checks += Check(ManifestFile.Load(Path.Combine(root, "ppcontent.json")).Manifest.Id == "MySounds",
                            "the manifest's id is the mod name");
            string meta = File.ReadAllText(Path.Combine(root, "meta.json"));
            checks += Check(ProjectScaffold.EnsureProject(modDir, "MySounds") == root &&
                            File.ReadAllText(Path.Combine(root, "meta.json")) == meta, "a second call changes nothing");
            string foreign = Path.Combine(mods, "Someone");
            Directory.CreateDirectory(foreign);
            File.WriteAllText(Path.Combine(foreign, "notes.txt"), "mine");
            checks += Check(Refused(() => ProjectScaffold.EnsureProject(modDir, "Someone")).Contains("not a ContentTool project"),
                            "a folder holding files but no ppcontent.json is refused");

            // ---- AddSound: the file name IS the target; one file per media.
            string mp3 = Write(dir, "boom.mp3", new byte[] { 1, 2, 3 });
            string wav = Write(dir, "boom.wav", new byte[] { 4, 5 });
            string copy = ProjectScaffold.AddSound(modDir, "MySounds", mp3, 18839791);
            checks += Check(copy == Path.Combine(ProjectScaffold.SoundDir(root), "18839791.mp3") && File.Exists(copy),
                            "the sound lands in Content\\Audio\\Replace\\<media>.mp3: " + copy);
            List<ProjectScaffold.SoundItem> items = ProjectScaffold.Sounds(root);
            checks += Check(items.Count == 1 && items[0].Media == 18839791 && items[0].State == "not built",
                            "the list shows it, not built yet");
            ProjectScaffold.AddSound(modDir, "MySounds", wav, 18839791);
            checks += Check(!File.Exists(copy) && File.Exists(Path.Combine(ProjectScaffold.SoundDir(root), "18839791.wav")),
                            "the same media again under another extension replaces the old copy (one file per media)");
            string bank = Path.Combine(root, "Dist", "Sounds", "18839791.bnk");
            Directory.CreateDirectory(Path.GetDirectoryName(bank));
            File.WriteAllBytes(bank, new byte[] { 0 });
            File.SetLastWriteTimeUtc(bank, DateTime.UtcNow.AddMinutes(1));
            checks += Check(ProjectScaffold.Sounds(root)[0].State == "built", "a newer bank reads as built");
            File.SetLastWriteTimeUtc(bank, DateTime.UtcNow.AddMinutes(-10));
            checks += Check(ProjectScaffold.Sounds(root)[0].State == "out of date", "an older bank reads as out of date");
            checks += Check(Refused(() => ProjectScaffold.AddSound(modDir, "MySounds", Write(dir, "x.flac", new byte[] { 1 }), 5))
                                .Contains("not a sound file"), "a .flac is refused by extension");
            checks += Check(Refused(() => ProjectScaffold.AddSound(modDir, "MySounds", wav, 0)).Contains("pick the game sound"),
                            "no target media is refused");

            // A "sounds" row naming ANOTHER file for the media would be the pair the bake refuses.
            string declared = ProjectScaffold.EnsureProject(modDir, "Declared");
            File.WriteAllText(Path.Combine(declared, "ppcontent.json"),
                              "{ \"id\": \"Declared\", \"bundle\": \"Declared.bundle\", \"sounds\": [ { \"media\": 7, \"file\": \"mine.wav\" } ] }");
            checks += Check(Refused(() => ProjectScaffold.AddSound(modDir, "Declared", wav, 7)).Contains("already replaces media 7"),
                            "a media a \"sounds\" row already claims is refused");

            // ---- AddVideo: one "replace" row, the clip beside it, the row last.
            string webm = Write(dir, "intro.webm", new byte[] { 9, 9, 9 });
            const string asset = "StreamableCopiedAssets/Videos/Factions/Phoenix/PP_Intro.webm";
            string clip = ProjectScaffold.AddVideo(modDir, "MyVideos", webm, asset);
            string vroot = Path.Combine(mods, "MyVideos");
            checks += Check(clip == Path.Combine(vroot, "Content", "Videos", "intro.webm") && File.Exists(clip),
                            "the clip lands in Content\\Videos: " + clip);
            List<bool> present;
            List<KeyValuePair<string, string>> rows = ProjectScaffold.Videos(vroot, out present);
            checks += Check(rows.Count == 1 && rows[0].Key == "intro" && rows[0].Value == asset && present[0],
                            "one video row names the stem and the shipped clip, and its file is there");
            ProjectScaffold.AddVideo(modDir, "MyVideos", webm, asset);
            checks += Check(ProjectScaffold.Videos(vroot, out present).Count == 1, "the same press again reuses the row");
            string other = Write(dir, "outro.webm", new byte[] { 1 });
            checks += Check(Refused(() => ProjectScaffold.AddVideo(modDir, "MyVideos", other, asset)).Contains("already replaces"),
                            "a second clip for the same shipped video is refused");
            ProjectScaffold.AddVideo(modDir, "MyVideos", other, null);
            rows = ProjectScaffold.Videos(vroot, out present);
            checks += Check(rows.Count == 2 && rows[1].Key == "outro" && rows[1].Value == null,
                            "a NEW clip is a row with no asset");
            checks += Check(Manifest.Parse(File.ReadAllText(Path.Combine(vroot, "ppcontent.json"))).Replace.Count == 2,
                            "and the manifest re-reads with both rows");

            // ---- AddWeapon: publish + weapons rows, the model in Content\Models, the author's bytes kept.
            string glb = Write(dir, "rifle.glb", new byte[] { 7, 7, 7, 7 });
            ProjectScaffold.WeaponResult w = ProjectScaffold.AddWeapon(modDir, "MyGuns", glb,
                                                                       "PX_AssaultRifle_WeaponDef", "Big Rifle");
            string groot = Path.Combine(mods, "MyGuns");
            checks += Check(w.WeaponId == "MyGuns_BigRifle_WeaponDef", "the def name is mod + name: " + w.WeaponId);
            checks += Check(File.Exists(Path.Combine(groot, "Content", "Models", "rifle.glb")), "the model is copied");
            var tree = Manifest.Tree(File.ReadAllText(Path.Combine(groot, "ppcontent.json")), "ppcontent.json");
            List<Dictionary<string, object>> pub = Manifest.Rows(tree, "publish", new List<object>());
            List<Dictionary<string, object>> weapons = Manifest.Rows(tree, "weapons", new List<object>());
            checks += Check(pub.Count == 1 && Manifest.Str(pub[0], "asset") == "models/rifle" &&
                            Manifest.Str(pub[0], "type") == "GameObject" &&
                            Manifest.Str(pub[0], "deps") == ProjectScaffold.ModelDeps, "one publish row for models/rifle");
            checks += Check(weapons.Count == 1 && Manifest.Str(weapons[0], "clone") == "PX_AssaultRifle_WeaponDef" &&
                            Manifest.Str(weapons[0], "model") == Manifest.Str(pub[0], "key") &&
                            Manifest.Str(weapons[0], "fit") == "auto" && Manifest.Str(weapons[0], "name") == "Big Rifle" &&
                            Guid.TryParse(Manifest.Str(weapons[0], "guid"), out Guid _),
                            "one weapons row: clone, fit auto, its model = the publish key, a real guid");
            checks += Check(Refused(() => ProjectScaffold.AddWeapon(modDir, "MyGuns", glb, "X_WeaponDef", "Big Rifle"))
                                .Contains("already has a weapon"), "the same weapon name twice is refused");
            ProjectScaffold.WeaponResult w2 = ProjectScaffold.AddWeapon(modDir, "MyGuns", glb, "PX_Pistol_WeaponDef", "Small");
            tree = Manifest.Tree(File.ReadAllText(Path.Combine(groot, "ppcontent.json")), "ppcontent.json");
            checks += Check(w2.ModelReused && w2.ModelKey == w.ModelKey &&
                            Manifest.Rows(tree, "publish", new List<object>()).Count == 1 &&
                            Manifest.Rows(tree, "weapons", new List<object>()).Count == 2,
                            "a second weapon on the same model reuses its publish row");
            string changed = Write(Path.Combine(dir, "sub"), "rifle.glb", new byte[] { 1 });
            string before = File.ReadAllText(Path.Combine(groot, "ppcontent.json"));
            checks += Check(Refused(() => ProjectScaffold.AddWeapon(modDir, "MyGuns", changed, "X_WeaponDef", "Other"))
                                .Contains("DIFFERENT file") &&
                            File.ReadAllText(Path.Combine(groot, "ppcontent.json")) == before,
                            "a different file under the same model name is refused and nothing is written");
            checks += Check(ProjectScaffold.Weapons(groot).Count == 2, "the weapons list reads both");

            // ---- the splice keeps the author's bytes: an existing "weapons" array gains one row, the rest stays.
            string hand = Path.Combine(mods, "Hand");
            Directory.CreateDirectory(hand);
            string authored = "{\n  \"id\": \"Hand\",\n  \"bundle\": \"Hand.bundle\",\n  \"note\": 1.50,\n" +
                              "  \"weapons\": [\n    { \"id\": \"A_WeaponDef\", \"clone\": \"C\", \"guid\": \"g\" }\n  ]\n}\n";
            File.WriteAllText(Path.Combine(hand, "ppcontent.json"), authored);
            ProjectScaffold.AddWeapon(modDir, "Hand", glb, "PX_Pistol_WeaponDef", "New");
            string after = File.ReadAllText(Path.Combine(hand, "ppcontent.json"));
            checks += Check(after.StartsWith("{\n  \"id\": \"Hand\",\n  \"bundle\": \"Hand.bundle\",\n  \"note\": 1.50,\n" +
                                             "  \"weapons\": [\n    { \"id\": \"A_WeaponDef\", \"clone\": \"C\", \"guid\": \"g\" },\n",
                                             StringComparison.Ordinal) &&
                            Manifest.Rows(Manifest.Tree(after, "x"), "weapons", new List<object>()).Count == 2 &&
                            Manifest.Rows(Manifest.Tree(after, "x"), "publish", new List<object>()).Count == 1,
                            "an authored manifest keeps its bytes; weapons gains a row, publish is added: " + after);

            // ---- previews: what other mods ship, found beside ours; the clip a row names.
            string otherMod = Path.Combine(mods, "OtherMod");
            Write(Path.Combine(otherMod, "Content", "Videos"), "boss.webm", new byte[] { 1 });
            Write(Path.Combine(otherMod, "Content", "Videos"), "notes.txt", new byte[] { 1 });
            Write(Path.Combine(otherMod, "Dist", "Sounds"), "633458426.bnk", new byte[] { 1 });
            List<KeyValuePair<string, string>> vids = ProjectScaffold.OtherModFiles(modDir, "MyVideos",
                Path.Combine("Content", "Videos"), ProjectScaffold.VideoExtensions);
            checks += Check(vids.Count == 1 && vids[0].Key == "OtherMod" && vids[0].Value.EndsWith("boss.webm"),
                            "another mod's clip is offered, its .txt is not, and our own mod is left out: " + vids.Count);
            checks += Check(ProjectScaffold.OtherModFiles(modDir, "OtherMod", Path.Combine("Content", "Videos"),
                                                          ProjectScaffold.VideoExtensions).Count == 2,
                            "with OtherMod as the current mod, MyVideos' two clips are the others (its .bak is not a clip)");
            checks += Check(ProjectScaffold.OtherModFiles(modDir, "MySounds", Path.Combine("Dist", "Sounds"), new[] { ".bnk" }).Count == 1,
                            "a built sound bank of another mod is offered");
            checks += Check(ProjectScaffold.VideoFile(vroot, "intro") == clip && ProjectScaffold.VideoFile(vroot, "nope") == null,
                            "a video row's stem resolves to its clip, or to nothing");

            // ---- the preview bank: our own ids, one embedded sound, a bank the loader walks cleanly, capped.
            var pcm = new byte[44100 * 2 * 2 * (Morgott.ContentTool.Wwise.PreviewBank.MaxSeconds + 5)];
            byte[] bnk = Morgott.ContentTool.Wwise.PreviewBank.Build(pcm, 2, 44100);
            checks += Check(Morgott.ContentTool.Wwise.BankGen.SelfCheck(bnk) == null, "the preview bank walks cleanly");
            checks += Check(bnk.Length < pcm.Length, "and a long source is cut to " +
                            Morgott.ContentTool.Wwise.PreviewBank.MaxSeconds + " s: " + bnk.Length + " < " + pcm.Length);
            checks += Check(Morgott.ContentTool.Wwise.PreviewBank.MediaId != 633458426 &&
                            Morgott.ContentTool.Wwise.PreviewBank.MediaId != Morgott.ContentTool.Wwise.PreviewBank.BankId,
                            "its ids are the tool's own hashes");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
        return "TASK-SCAFFOLD ALL PASS (" + checks + " checks)";
    }

    private static string Write(string dir, string name, byte[] bytes)
    {
        Directory.CreateDirectory(dir);
        string p = Path.Combine(dir, name);
        File.WriteAllBytes(p, bytes);
        return p;
    }

    private static string Refused(Action a)
    {
        try { a(); }
        catch (Exception ex) when (ex is InvalidDataException || ex is IOException) { return ex.Message; }
        return "(not refused)";
    }

    private static int Check(bool condition, string what)
    {
        if (!condition) throw new Exception("TASK-SCAFFOLD FAILURE: " + what);
        return 1;
    }
}
