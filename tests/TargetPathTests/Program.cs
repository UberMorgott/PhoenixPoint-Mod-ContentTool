using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Dev;
using Morgott.ContentTool.Project;
using Morgott.ContentTool.Tactical;

/// <summary>
/// Gate R0 (FINAL-PLAN 39.7, Task 22), offline: every anchor form and every subpath form survives
/// parse -> format -> parse byte-identically, a malformed path is a NAMED refusal, and - the control
/// in the same run - an ambiguous name: record is refused while the guid: record beside it loads.
///
///   dotnet run --project tests\TargetPathTests
/// </summary>
internal static class Program
{
    private static int failures;

    private static int Main()
    {
        RoundTrip();
        Refusals();
        SetControl();
        BakeFilter();
        Sha1Shape();
        ModGateArm();
        ContentModsArm();
        KeepAliveArm();
        ContentStateArm();
        BundleClaimsArm();
        ResidencyArm();
        ReclaimCrcArm();
        TwoRouteToggleArm();
        EnableOrderArm();
        LegacyDiskArm();
        KeyClaimsArm();
        AddOnlyArm();
        ProjectDirArm();
        StartupScanArm();
        StaleBankArm();
        OutcomeArm();
        SoundOwnerArm();
        PackageArm();
        TypeResolveArm();
        CacheKeyArm();
        CachePruneArm();
        InstallWriteArm();
        DevLoopArm();
        VideoOnlyReportArm();
        DeclaredTypeArm();
        StopEventArm();
        WeaponTintArm();
        FitBelowRootArm();
        FitAlgebraArm();
        FitOffsetArm();
        FitSaveArm();
        BenchListArm();
        GizmoPickArm();
        GizmoDragArm();
        MirrorSaveArm();
        BenchHolesArm();
        TransportArm();
        RingArm();

        Console.WriteLine(failures == 0 ? "R0: ALL PASS" : "R0: " + failures + " FAILURE(S)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Every form the plan writes down, plus the edge cases the grammar allows.</summary>
    private static void RoundTrip()
    {
        string[] forms =
        {
            "guid:8f3ca1b2c3d4e5f60718293a4b5c6d7e#Root/Chest/Arm_R@SkinnedMeshRenderer.mesh",
            "guid:8f3ca1b2c3d4e5f60718293a4b5c6d7e#Root/Chest/Arm_R@Renderer.materials[1]",
            "guid:8f3ca1b2c3d4e5f60718293a4b5c6d7e#Root/Chest/Arm_R@Renderer.materials[1].tex:_MainTex",
            "guid:8f3ca1b2c3d4e5f60718293a4b5c6d7e#@Animator.clip:Idle_Rifle",
            "guid:5343bae3ed7b266f24c83e5fbbc77be2#Root@MeshFilter.mesh",
            "media:18839791",
            "media:0",
            "name:Geo_Head02_V01#Root/Head@SkinnedMeshRenderer.mesh",
            "defname:E_SkinData [AN_Assault_Helmet_BodyPartDef]#Root@Renderer.materials[0].tex:_BumpMap",
        };
        foreach (string s in forms)
        {
            TargetPath p;
            string err;
            if (!TargetPath.TryParse(s, out p, out err)) { Fail("R0-parse", s + " -> refused: " + err); continue; }

            string once = p.Format();
            if (once != s) { Fail("R0-format", s + " -> formatted as " + once); continue; }

            TargetPath again;
            if (!TargetPath.TryParse(once, out again, out err) || again.Format() != s)
            { Fail("R0-roundtrip", s + " does not survive a second pass (" + err + ")"); continue; }

            Pass("R0-roundtrip", s);
        }

        // The parse must actually decompose, not just echo: a formatter that returned its input
        // would pass every check above.
        TargetPath q;
        string why;
        TargetPath.TryParse(forms[2], out q, out why);
        Check("R0-fields",
            q != null && q.Anchor == AnchorKind.Guid && q.Transform == "Root/Chest/Arm_R" &&
            q.Component == "Renderer" && q.Field == "materials" && q.Index == 1 &&
            q.Qualifier == "tex" && q.Member == "_MainTex",
            q == null ? "did not parse" : q.Anchor + " transform=" + q.Transform + " comp=" + q.Component +
            " field=" + q.Field + " idx=" + q.Index + " qual=" + q.Qualifier + " member=" + q.Member);

        TargetPath root;
        TargetPath.TryParse(forms[3], out root, out why);
        Check("R0-emptytransform", root != null && root.Transform == "" && root.Member == "Idle_Rifle",
            root == null ? "did not parse" : "transform='" + root.Transform + "' member=" + root.Member);
    }

    /// <summary>A malformed path is refused BY NAME - never a silent skip, never a fuzzy match.</summary>
    private static void Refusals()
    {
        string[] bad =
        {
            "",
            "Root/Chest@Renderer.mesh",                                             // no anchor
            "asset:8f3ca1b2c3d4e5f60718293a4b5c6d7e#Root@Renderer.mesh",            // unknown anchor
            "guid:8F3CA1B2C3D4E5F60718293A4B5C6D7E#Root@Renderer.mesh",             // uppercase guid
            "guid:8f3c#Root@Renderer.mesh",                                         // short guid
            "guid:8f3ca1b2c3d4e5f60718293a4b5c6d7e",                                // no subpath
            "guid:8f3ca1b2c3d4e5f60718293a4b5c6d7e#Root/Chest",                     // no @component
            "guid:8f3ca1b2c3d4e5f60718293a4b5c6d7e#Root@Renderer",                  // no slot
            "guid:8f3ca1b2c3d4e5f60718293a4b5c6d7e#Root//Arm@Renderer.mesh",        // empty segment
            "guid:8f3ca1b2c3d4e5f60718293a4b5c6d7e#Root@Renderer.materials[1",      // unclosed index
            "guid:8f3ca1b2c3d4e5f60718293a4b5c6d7e#Root@Renderer.materials[x]",     // non-numeric index
            "guid:8f3ca1b2c3d4e5f60718293a4b5c6d7e#Root@Renderer.materials[1].tex", // qualifier, no member
            "guid:8f3ca1b2c3d4e5f60718293a4b5c6d7e#Root@Animator.clip:",            // empty member
            "media:18839791#Root@Renderer.mesh",                                    // media takes no subpath
            "media:-1",
            "media:notanumber",
            "name:#Root@Renderer.mesh",                                             // nameless
        };
        foreach (string s in bad)
        {
            TargetPath p;
            string err;
            bool parsed = TargetPath.TryParse(s, out p, out err);
            Check("R0-refuse", !parsed && !string.IsNullOrEmpty(err),
                "'" + s + "' -> " + (parsed ? "ACCEPTED (should not be)" : err));
        }
    }

    /// <summary>
    /// Control in the same run: the ambiguous name: pair is refused while the guid: record beside it
    /// loads. If the validator ever stops rejecting, this is the check that goes red.
    /// </summary>
    private static void SetControl()
    {
        const string good = "guid:8f3ca1b2c3d4e5f60718293a4b5c6d7e#Root/Chest/Arm_R@SkinnedMeshRenderer.mesh";
        const string ambiguous = "name:Geo_Head02_V01#Root/Head@SkinnedMeshRenderer.mesh";
        List<ReplacementRule> rules = new List<ReplacementRule>
        {
            new ReplacementRule { Target = good, Content = "meshes/arm_r" },
            new ReplacementRule { Target = ambiguous, Content = "meshes/head_a" },
            new ReplacementRule { Target = ambiguous, Content = "meshes/head_b" },   // same target, different content
            new ReplacementRule { Target = "guid:zz#Root@Renderer.mesh", Content = "meshes/x" },
            new ReplacementRule { Target = good + "x", Content = null },              // no content
            new ReplacementRule { Target = "media:18839791", Content = "audio/boom", Sha1 = "not-a-sha1" },
        };

        List<string> refusals = new List<string>();
        List<ReplacementRule> ok = ReplacementSet.Validate(rules, refusals);

        Check("R0-control-loads", ok.Count == 1 && ok[0].Target == good && ok[0].Path != null,
            "accepted " + ok.Count + " record(s): " + Join(ok));
        Check("R0-control-refuses", refusals.Count == 5, refusals.Count + " refusal(s): " + string.Join(" | ", refusals.ToArray()));
        foreach (string r in refusals)
            Check("R0-named", !string.IsNullOrEmpty(r) && r.Length > 10, "refusal text: '" + r + "'");
    }

    /// <summary>Shipping mode has no scan and no def resolution, so bake must refuse both by name.</summary>
    private static void BakeFilter()
    {
        List<string> refusals = new List<string>();
        List<ReplacementRule> ok = ReplacementSet.Validate(new List<ReplacementRule>
        {
            new ReplacementRule { Target = "guid:8f3ca1b2c3d4e5f60718293a4b5c6d7e#Root@Renderer.mesh", Content = "m" },
            new ReplacementRule { Target = "name:Prop_Crate#Root@MeshFilter.mesh", Content = "m" },
            new ReplacementRule { Target = "defname:E_SkinData [X]#Root@Renderer.mesh", Content = "m" },
        }, refusals);
        Check("R0-bake-loads", ok.Count == 3 && refusals.Count == 0, ok.Count + " loaded, " + refusals.Count + " refused");

        string err;
        Check("R0-bake-guid", ReplacementSet.Bakeable(ok[0], out err), "guid: record bakeable, err=" + err);
        Check("R0-bake-name", !ReplacementSet.Bakeable(ok[1], out err) && err != null, "name: -> " + err);
        Check("R0-bake-defname", !ReplacementSet.Bakeable(ok[2], out err) && err != null, "defname: -> " + err);
    }

    /// <summary>sha1 is verification only; the three outcomes have to be distinguishable.</summary>
    private static void Sha1Shape()
    {
        string h = Sha1.Hex(new byte[] { 0x00, 0xFF, 0xC3, 0x28 });
        Check("R0-sha1", h != null && h.Length == 40 && h == h.ToLowerInvariant(), "sha1=" + h);

        ReplacementRule r = new ReplacementRule { Target = "x", Content = "y", Sha1 = h };
        Check("R0-verify-match", ReplacementSet.Verify(r, h) == Verification.Match, "match");
        Check("R0-verify-differ", ReplacementSet.Verify(r, Sha1.Hex(new byte[] { 1 })) == Verification.Differ, "differ");
        Check("R0-verify-unknown", ReplacementSet.Verify(r, null) == Verification.Unverifiable, "unverifiable");
    }

    /// <summary>
    /// Gate G1, offline: content from a mod the player switched OFF must not be applied.
    ///
    /// The arm can TELL THEM APART because the same call is made twice against the same roster, with
    /// only the Enabled flag differing between the two folders: an implementation that ignored the
    /// flag (the bug: walk the Mods folder, apply everything) would answer Apply for both and fail the
    /// disabled arm. The Unknown and NoRoster arms are the other two answers, so no single constant
    /// return value can pass this set.
    /// </summary>
    /// <summary>
    /// Gate G5, offline: the dependency keep-alive decision, which in the game is the difference
    /// between a subscribed content mod working and silently reverting one frame after it applied
    /// (ModManager.cs:293-299 disables everything the stored list does not name, and takes the
    /// dependents down with it). The rule has to hold BOTH ways, so both are measured here: keep a
    /// dependency a still-wanted mod needs, and never re-enable something the player turned off.
    /// </summary>
    private static void KeepAliveArm()
    {
        const string Ct = "com.morgott.ContentTool";
        string[] listed = { "com.someone.WeaponMesh" };
        List<KeyValuePair<string, bool>> on =
            new List<KeyValuePair<string, bool>> { new KeyValuePair<string, bool>("com.someone.WeaponMesh", true) };

        Check("G5-fresh-subscribe", ContentMods.KeepAlive(Ct, listed, on),
            "a dependency the list does not name stays enabled while a LISTED, ENABLED mod needs it");
        Check("G5-player-owns-it", !ContentMods.KeepAlive(Ct, new[] { Ct, "com.someone.WeaponMesh" }, on),
            "once the list names the dependency, its disable is the player's own and is never vetoed");
        Check("G5-dependent-off", !ContentMods.KeepAlive(Ct, listed,
                new List<KeyValuePair<string, bool>> { new KeyValuePair<string, bool>("com.someone.WeaponMesh", false) }),
            "no ENABLED dependent, no re-assertion");
        Check("G5-dependent-unlisted", !ContentMods.KeepAlive(Ct, new string[0], on),
            "a dependent the player's own list does not name cannot vouch for the dependency");
        Check("G5-no-list", !ContentMods.KeepAlive(Ct, null, on),
            "'I cannot read what he chose' never becomes 'so I'll keep it on'");
        Check("G5-no-dependents", !ContentMods.KeepAlive(Ct, listed, new List<KeyValuePair<string, bool>>()),
            "a dependency nothing depends on is switched off like any other unlisted mod");
    }

    private static void ModGateArm()
    {
        Dictionary<string, bool> roster = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            { ModGate.Key(@"C:\Games\PP\Mods\ContentTool"), true },
            { ModGate.Key(@"C:\Games\PP\Mods\MenuMusic"), false },
        };

        Check("G1-enabled", ModGate.Decide(@"C:\Games\PP\Mods\ContentTool", roster) == ModVerdict.Apply,
            "an ENABLED mod's content is applied");
        Check("G1-disabled", ModGate.Decide(@"C:\Games\PP\Mods\MenuMusic", roster) == ModVerdict.Disabled,
            "a mod the manager has switched OFF is refused, on the same roster");
        Check("G1-unknown", ModGate.Decide(@"C:\Games\PP\Mods\NoMetaJson", roster) == ModVerdict.Unknown,
            "a folder the manager never discovered is refused, not guessed");
        Check("G1-noroster", ModGate.Decide(@"C:\Games\PP\Mods\ContentTool", null) == ModVerdict.NoRoster,
            "no manager = nothing applied, never a fallback to apply-everything");
        Check("G1-noroster-empty", ModGate.Decide(@"C:\Games\PP\Mods\ContentTool", new Dictionary<string, bool>()) == ModVerdict.NoRoster,
            "an empty roster is 'could not be read', not 'nothing is enabled'");

        // The roster is keyed on ModEntry.Directory and looked up with a DirectoryInfo.FullName; if
        // those two spellings did not collapse, every mod would read Unknown and nothing would load.
        Check("G1-key-shape", ModGate.Decide(@"C:\Games\PP\Mods\MENUMUSIC\", roster) == ModVerdict.Disabled,
            "trailing separator and case do not change the verdict");

        Check("G1-why", ModGate.Why(ModVerdict.Disabled).Contains("disabled in the mod manager")
                        && ModGate.Why(ModVerdict.Disabled) != ModGate.Why(ModVerdict.Unknown),
            "the skip reason is in the log line and each refusal reads differently");
    }

    /// <summary>
    /// Gate G2, offline, against REAL folders: a shipped content mod applies because the player has
    /// it switched on, and for no other reason.
    ///
    /// It can tell APPLIED from NOT APPLIED because all four folders exist on disk and three of them
    /// carry the marker - the only thing that differs is what the roster says. An implementation that
    /// walked the folder (the bug) would return all three; one that returned nothing would fail the
    /// enabled arm; a constant skipped count cannot satisfy both halves of the count arm.
    /// </summary>
    private static void ContentModsArm()
    {
        string mods = Path.Combine(Path.GetTempPath(), "ct_g2_" + Guid.NewGuid().ToString("N"));
        try
        {
            // Mods\ContentTool is us; the other four are siblings, exactly the shape on a player's disk.
            string us = Dir(mods, "ContentTool");
            string on = Dir(mods, "OnMod"), off = Dir(mods, "OffMod");
            string unknown = Dir(mods, "NoMeta"), bare = Dir(mods, "NoContent");
            foreach (string d in new[] { on, off, unknown })
                File.WriteAllText(Path.Combine(d, ContentMods.Manifest), "{ \"id\": \"x\" }");

            Dictionary<string, bool> roster = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                { ModGate.Key(us), true },
                { ModGate.Key(on), true },
                { ModGate.Key(off), false },
                // NoMeta is deliberately absent: the manager never discovered it.
            };

            StringBuilder log = new StringBuilder();
            int skipped;
            List<string> hits = ContentMods.Enabled(us, ContentMods.Manifest, roster, log, out skipped);

            Check("G2-enabled", hits.Count == 1 && ModGate.Key(hits[0]) == ModGate.Key(on),
                "of four sibling folders on disk, only the ENABLED content mod is applied -> " +
                string.Join(", ", hits.ToArray()));
            Check("G2-refused", skipped == 2 && log.ToString().Contains("OffMod") && log.ToString().Contains("NoMeta"),
                "the two it refused are named with their reason, " + skipped + " skipped: " +
                log.ToString().Replace(Environment.NewLine, " | ").Trim());
            Check("G2-nomarker", !log.ToString().Contains("NoContent"),
                "a folder carrying no content is not a refusal - it is not a content mod at all");

            // The whole point of the marker being a NAME: a directory marker (Dist\Sounds) and a file
            // marker (ppcontent.json) are the same discovery, or the sound route drifts from the video one.
            Directory.CreateDirectory(Path.Combine(bare, "Dist\\Sounds"));
            roster[ModGate.Key(bare)] = true;
            hits = ContentMods.Enabled(us, "Dist\\Sounds", roster, null, out skipped);
            Check("G2-dirmarker", hits.Count == 1 && ModGate.Key(hits[0]) == ModGate.Key(bare),
                "a DIRECTORY marker discovers exactly the folder that has it -> " +
                string.Join(", ", hits.ToArray()));

            Check("G2-noroster", ContentMods.Enabled(us, ContentMods.Manifest, null, null, out skipped).Count == 0
                                 && skipped == 3,
                "an unreadable manager applies NOTHING, and says so " + skipped + " time(s) - never apply-everything");

            // Gate S3: a Steam Workshop content mod is NOT beside us - it lives in its own
            // UgcItemInstallInfo.InstallDir (workshop\content\839770\<id>), so the sibling walk alone
            // could never see it. The roster is the source that does. Both items sit in the same
            // non-sibling tree and differ only in what the manager says, so an implementation that
            // ignored the roster fails the first arm and one that ignored the switch fails the second.
            string ws = Path.Combine(mods, "workshop_content", "839770");
            string wsOn = Dir(ws, "111"), wsOff = Dir(ws, "222");
            foreach (string d in new[] { wsOn, wsOff })
                File.WriteAllText(Path.Combine(d, ContentMods.Manifest), "{ \"id\": \"ws\" }");
            roster[ModGate.Key(wsOn)] = true;
            roster[ModGate.Key(wsOff)] = false;

            log = new StringBuilder();
            hits = ContentMods.Enabled(us, ContentMods.Manifest, roster, log, out skipped);
            Check("S3-workshop", hits.Exists(h => ModGate.Key(h) == ModGate.Key(wsOn)),
                "a subscribed Workshop mod, which is not a sibling of ours at all, is discovered -> " +
                string.Join(", ", hits.ToArray()));
            Check("S3-workshop-off", !hits.Exists(h => ModGate.Key(h) == ModGate.Key(wsOff))
                                     && log.ToString().Contains("222"),
                "and the switch governs it exactly like a local one: " +
                log.ToString().Replace(Environment.NewLine, " | ").Trim());
            Check("S3-dedup", hits.FindAll(h => ModGate.Key(h) == ModGate.Key(on)).Count == 1,
                "a LOCAL mod is in the roster and beside us at once, and is applied ONCE, not twice -> " +
                string.Join(", ", hits.ToArray()));
            // The diagnostic the roster cannot give: a content folder the manager never discovered.
            Check("S3-unknown-still-named", log.ToString().Contains("NoMeta"),
                "a content folder with no meta.json is still refused BY NAME, not silently dropped");

            // ProjectDir: a console name reaches the mod the player actually installed.
            Check("G2-project-sibling", ModGate.Key(ContentMods.ProjectDir(us, "OnMod")) == ModGate.Key(on),
                "'ct_project OnMod' resolves to the SIBLING mod folder -> " + ContentMods.ProjectDir(us, "OnMod"));
            // A STALE COPY inside our own folder must never shadow the installed mod: the manager
            // knows nothing about it, so serving it would apply content the player cannot switch off.
            string stale = Path.Combine(us, "OnMod");
            Directory.CreateDirectory(stale);
            File.WriteAllText(Path.Combine(stale, ContentMods.Manifest), "{ \"id\": \"stale\" }");
            Check("G2-project-stale", ModGate.Key(ContentMods.ProjectDir(us, "OnMod")) == ModGate.Key(on)
                                      && ContentMods.Sibling(us, "OnMod") != null,
                "a leftover copy inside Mods\\ContentTool loses to the installed mod -> " +
                ContentMods.ProjectDir(us, "OnMod"));
            string ours = Path.Combine(us, "Sample");
            Directory.CreateDirectory(ours);
            File.WriteAllText(Path.Combine(ours, ContentMods.Manifest), "{ \"id\": \"sample\" }");
            Check("G2-project-ours", ModGate.Key(ContentMods.ProjectDir(us, "Sample")) == ModGate.Key(ours)
                                     && ContentMods.Sibling(us, "Sample") == null,
                "while our OWN project, which has no installed mod of that name, still resolves to us -> " +
                ContentMods.ProjectDir(us, "Sample"));
            Check("G2-project-neither", ModGate.Key(ContentMods.ProjectDir(us, "Nowhere")) == ModGate.Key(Path.Combine(us, "Nowhere")),
                "a name that is nowhere still names a path, so the refusal can print it -> " +
                ContentMods.ProjectDir(us, "Nowhere"));
        }
        finally { try { Directory.Delete(mods, true); } catch (IOException) { } }
    }

    /// <summary>
    /// Gate G3, offline: a mod switched OFF at runtime stops being applied, and one switched ON gets
    /// applied exactly once.
    ///
    /// It can tell APPLIED from RELEASED because every arm reads the state back after a TRANSITION,
    /// never a constant: claim -> Holds true, release -> Holds false, and the release hands back the
    /// very items the apply recorded. An implementation that never released (the P1 bug: disable
    /// unloads nothing) leaves Holds true and returns an empty list, and fails both. One that
    /// re-applied on every call (the double-apply the startup scan plus the runtime hook would make)
    /// fails the second-claim arm. A constant answer cannot pass the set.
    /// </summary>
    private static void ContentStateArm()
    {
        const string mod = @"C:\Games\PP\Mods\MenuMusic", other = @"C:\Games\PP\Mods\IntroVideo";

        Check("G3-fresh", !ContentState.Holds(mod, "sound"),
            "nothing is applied before anything is claimed");

        Check("G3-claim", ContentState.Claim(mod, "sound") && ContentState.Holds(mod, "sound"),
            "switching a mod ON claims its route and the state says so");
        ContentState.Served(mod, "sound", "208540756");
        ContentState.Served(mod, "sound", "423563089");

        Check("G3-once", !ContentState.Claim(mod, "sound"),
            "a SECOND apply of the same route is refused - the startup scan and the runtime hook " +
            "cannot both load the same 24 MB bank");

        // Per ROUTE, not per mod: one mod's sound and video are applied and undone independently,
        // because only one of the two can be handed back in-session.
        Check("G3-routes", ContentState.Claim(mod, "video") && ContentState.Holds(mod, "sound"),
            "a second ROUTE of the same mod claims separately, and the first is still held");

        List<string> released = ContentState.Release(mod, "sound");
        Check("G3-release",
            released.Count == 2 && released[0] == "208540756" && released[1] == "423563089"
            && !ContentState.Holds(mod, "sound"),
            "switching it OFF hands back exactly what the apply recorded (" +
            string.Join(", ", released.ToArray()) + ") and the route is no longer held");

        Check("G3-release-ctl", ContentState.Holds(mod, "video"),
            "while the OTHER route of the same mod is untouched by that release");

        Check("G3-release-twice", ContentState.Release(mod, "sound").Count == 0,
            "releasing again hands back nothing - an undo cannot run twice");
        Check("G3-release-unknown", ContentState.Release(other, "video").Count == 0
                                    && ContentState.Holds(mod, "video"),
            "and releasing a mod that was never applied disturbs nobody else's rows");

        Check("G3-recycle", ContentState.Claim(mod, "sound") && ContentState.Release(mod, "sound").Count == 0,
            "a released route can be claimed again - off, then on, in one session, with nothing carried over");

        ReportedCountArm();
    }

    /// <summary>
    /// Gate G4, offline: the number the summary line PRINTS is the number of things actually
    /// applied. It shipped as "ct_sound: 0 shipped replacement bank(s)" while nine banks were
    /// audibly playing, because the work had moved to the per-mod path and the aggregate line still
    /// counted its own loop.
    ///
    /// The arm applies through the SAME calls the runtime toggle uses and then reads the count the
    /// summary reads. There is only one list, so the two cannot disagree - which is the fix, not
    /// just the test. An implementation that counted a loop instead reports 0 here, because this
    /// arm never runs that loop.
    /// </summary>
    private static void ReportedCountArm()
    {
        const string a = @"C:\Games\PP\Mods\CountA", b = @"C:\Games\PP\Mods\CountB";

        Check("G4-zero", ContentState.Items("bank") == 0 && ContentState.Mods("bank") == 0,
            "an untouched route reports 0 - the count is not merely never-decreasing");

        ContentState.Claim(a, "bank");
        ContentState.Served(a, "bank", "3817623587");
        ContentState.Served(a, "bank", "1953710523");
        ContentState.Claim(b, "bank");
        ContentState.Served(b, "bank", "208540756");

        Check("G4-items", ContentState.Items("bank") == 3,
            "three banks were loaded and the line reports " + ContentState.Items("bank") +
            " - the report reads the same list the loading writes");
        Check("G4-mods", ContentState.Mods("bank") == 2,
            "from " + ContentState.Mods("bank") + " mods, counted off that same list");
        Check("G4-route", ContentState.Items("clip") == 0,
            "and another route's line still reports 0 - the tally is per route, not a global total");

        ContentState.Release(a, "bank");
        Check("G4-after-release", ContentState.Items("bank") == 1 && ContentState.Mods("bank") == 1,
            "switching one of them off drops the reported count to " + ContentState.Items("bank") +
            " bank(s) from " + ContentState.Mods("bank") + " mod - the line tracks what is live now");
        ContentState.Release(b, "bank");
    }

    /// <summary>
    /// Gate S1, offline: the live bundle seam's bookkeeping. Three things it can get wrong silently
    /// and the arms that would go red for each.
    ///
    /// It can tell REDIRECTED from NOT REDIRECTED because every Resolve arm is run against two
    /// locations in the same registry - one owned, one not - so a func that returned its argument's
    /// id always, or our path always, fails one half. The fall-through arm passes a DIFFERENT previous
    /// delegate each time, so an implementation that overwrote the pre-existing func instead of
    /// composing with it (the seam's worst failure: it silently breaks another mod) answers "orig"
    /// where the arm demands "PREV". The ownership arms make the same two mods claim the same bundle
    /// in BOTH orders and demand the SAME winner, so "first claim wins" cannot pass.
    /// </summary>
    private static void BundleClaimsArm()
    {
        const string bundle = "mutoid_assets_all.bundle";

        Check("S1-match", BundleClaims.Matches(@"D:\PP\...\aa\StandaloneWindows64\" + bundle, bundle)
                          && BundleClaims.Matches("aa/StandaloneWindows64/" + bundle, bundle)
                          && BundleClaims.Matches(bundle, bundle)
                          && BundleClaims.Matches("aa/" + bundle.ToUpperInvariant(), bundle),
            "a catalog internalId is matched by its file name, on either separator and either case");
        Check("S1-match-boundary", !BundleClaims.Matches("aa/x" + bundle, bundle)
                                   && !BundleClaims.Matches("aa/mutoid_assets_all.bundle.bak", bundle)
                                   && !BundleClaims.Matches("aa/other_assets_all.bundle", bundle)
                                   && !BundleClaims.Matches(null, bundle),
            "and NOT by a longer name that merely ends the same way - the suffix has to sit on a path boundary");

        // Order A: the loser claims first.
        string refusal;
        BundleClaim evicted;
        BundleClaim m = BundleClaims.Claim("mod.b", bundle, @"C:\Mods\B\b.bundle", out refusal, out evicted);
        Check("S1-claim", m != null && refusal == null && evicted == null && BundleClaims.Holds("mod.b"),
            "an unclaimed shipped bundle is claimed");
        BundleClaim z = BundleClaims.Claim("mod.z", bundle, @"C:\Mods\Z\z.bundle", out refusal, out evicted);
        Check("S1-refuse-by-name", z == null && refusal != null && refusal.Contains("mod.b")
                                   && refusal.Contains(bundle) && refusal.StartsWith("REFUSED"),
            "a second mod is refused BY NAME -> " + refusal);
        BundleClaim a = BundleClaims.Claim("mod.a", bundle, @"C:\Mods\A\a.bundle", out refusal, out evicted);
        Check("S1-lowest-wins", a != null && refusal == null && evicted != null && evicted.Mod == "mod.b"
                                && !BundleClaims.Holds("mod.b"),
            "while a LOWER mod id takes it, and the loser is handed back so its CRC can be restored");
        BundleClaims.Drop("mod.a");

        // Order B: the same two mods, claiming in the opposite order, must produce the SAME owner.
        BundleClaims.Claim("mod.z", bundle, @"C:\Mods\Z\z.bundle", out refusal, out evicted);
        BundleClaims.Claim("mod.b", bundle, @"C:\Mods\B\b.bundle", out refusal, out evicted);
        Check("S1-deterministic", BundleClaims.Holds("mod.b") && !BundleClaims.Holds("mod.z"),
            "arrival order does not decide the owner - 'mod.b' wins from either side");
        BundleClaims.Drop("mod.b");

        Check("S1-guard", BundleClaims.Claim(null, bundle, "p", out refusal, out evicted) == null && refusal != null
                          && BundleClaims.Claim("mod.a", bundle, null, out refusal, out evicted) == null,
            "an incomplete claim is refused, not half-recorded");

        // The transform func, with a location we own and one we do not, in one registry.
        object mine = new object(), theirs = new object();
        BundleClaim c = BundleClaims.Claim("mod.a", bundle, @"C:\Mods\A\a.bundle", out refusal, out evicted);
        c.Location = mine;
        Func<object, string> prev = o => "PREV";

        Check("S1-resolve-owned", BundleClaims.Resolve(mine, null, "orig") == @"C:\Mods\A\a.bundle"
                                  && BundleClaims.Resolve(mine, prev, "orig") == @"C:\Mods\A\a.bundle",
            "a location we own resolves to our patched copy, whoever else installed a delegate");
        Check("S1-resolve-fallthrough", BundleClaims.Resolve(theirs, null, "orig") == "orig",
            "a location we do not own keeps its own internalId when nobody else set a func");
        Check("S1-resolve-compose", BundleClaims.Resolve(theirs, prev, "orig") == "PREV"
                                    && BundleClaims.Resolve(null, prev, "orig") == "PREV",
            "and goes to the PRE-EXISTING delegate when there is one - composed, never overwritten");

        List<BundleClaim> dropped = BundleClaims.Drop("mod.a");
        Check("S1-drop", dropped.Count == 1 && dropped[0].Bundle == bundle && !BundleClaims.Holds("mod.a")
                         && BundleClaims.Resolve(mine, null, "orig") == "orig",
            "unregistering hands the claim back and the location resolves to itself again");
        Check("S1-drop-twice", BundleClaims.Drop("mod.a").Count == 0 && BundleClaims.All.Count == 0,
            "and a second unregister hands back nothing - the registry is empty, not merely quiet");
    }

    /// <summary>
    /// Gate S1-resident, offline: the "already loaded, restart required" refusal is decided against
    /// the name UNITY loaded the bundle under, not the catalog's file name.
    ///
    /// The fixtures are the real spellings measured in the running game 2026-08-27: the shipped
    /// px_equipment_assets_all.bundle is resident as "2b20742ec3da14eed347ece50e87df9d.bundle" and its
    /// AssetBundleRequestOptions.BundleName is the bare hash, while a second loaded bundle reads
    /// "64a410770ae890f6800176837c41d38b.bundle". The old comparison asked whether the LOADED name
    /// equalled the catalog file name (or that name without its extension), so it answered false for
    /// every bundle in the game: the refusal was dead code, and a re-enable against a resident bundle
    /// registered a redirect Unity then rejected at load time. That implementation fails S1-resident
    /// below; one that matched everything fails S1-resident-other.
    /// </summary>
    private static void ResidencyArm()
    {
        const string name = "2b20742ec3da14eed347ece50e87df9d";      // options.BundleName
        const string loaded = name + ".bundle";                      // AssetBundle.name
        const string file = "px_equipment_assets_all.bundle";        // the catalog's file name

        Check("S1-resident", BundleClaims.SameBundle(loaded, name) && BundleClaims.SameBundle(name, name)
                             && BundleClaims.SameBundle(loaded, loaded) && BundleClaims.SameBundle(name, loaded),
            "the resident bundle is recognised from the location's BundleName, with the .bundle " +
            "extension on either side or neither");
        Check("S1-resident-filename", !BundleClaims.SameBundle(loaded, file),
            "and never from the catalog FILE name " + file + " - the comparison that could not match");
        Check("S1-resident-other", !BundleClaims.SameBundle("64a410770ae890f6800176837c41d38b.bundle", name)
                                   && !BundleClaims.SameBundle(null, name) && !BundleClaims.SameBundle(loaded, null),
            "a different loaded bundle is not this one, and a missing name is not a match either");
    }

    /// <summary>
    /// Gate S1-reclaim, offline: the SHIPPED CRC survives a mod re-claiming a bundle it already owns
    /// (a second 'ct_route7 apply', or disable-then-enable inside one session).
    ///
    /// It can tell KEPT from LOST because the CRC is written once, on the first claim, and read back
    /// after the second - and the live options object it came from is already suppressed to 0 by then,
    /// exactly as in the game. An implementation that removed the standing record and started a fresh
    /// one (the bug) reads 0 here, and its Uninstall would restore 0 to the game's own options object,
    /// leaving the shipped bundle loading unchecked for the rest of the session.
    /// </summary>
    private static void ReclaimCrcArm()
    {
        const string bundle = "aln_fireworm_assets_all.bundle";
        string refusal;
        BundleClaim evicted;

        BundleClaim first = BundleClaims.Claim("mod.a", bundle, @"C:\Mods\A\v1.bundle", out refusal, out evicted);
        object opts = new object();
        first.Location = new object();
        first.Options = opts;
        first.Crc = 3735928559;          // what the game shipped, read before it was zeroed
        first.CrcSuppressed = true;

        BundleClaim again = BundleClaims.Claim("mod.a", bundle, @"C:\Mods\A\v2.bundle", out refusal, out evicted);
        Check("S1-reclaim", again != null && refusal == null && evicted == null
                            && again.Crc == 3735928559 && again.CrcSuppressed
                            && ReferenceEquals(again.Options, opts),
            "re-claiming a bundle this mod already owns keeps the shipped crc " +
            (again == null ? "(REFUSED)" : again.Crc.ToString()) + " - it is unreadable a second time, " +
            "the live options object says 0 by then");
        Check("S1-reclaim-path", again != null && again.Path == @"C:\Mods\A\v2.bundle"
                                 && BundleClaims.All.Count == 1,
            "while the path moves to the new copy and the registry still holds exactly one record");

        List<BundleClaim> gone = BundleClaims.Drop("mod.a");
        Check("S1-reclaim-drop", gone.Count == 1 && gone[0].Crc == 3735928559,
            "so the uninstall that follows can put the shipped crc back, not 0");
    }

    /// <summary>
    /// Gate S5, offline: a project's two routes are toggled INDEPENDENTLY.
    ///
    /// It can tell the routes apart because every arm feeds a DIFFERENT applied-state per route while
    /// asking one question per route. The collapsed implementation - "either registry holds this mod"
    /// - answers the same thing for both routes of a given call, so the mixed arms below, where one
    /// route must move and the other must not, cannot pass under it.
    /// </summary>
    private static void TwoRouteToggleArm()
    {
        // Both declared, only the keys route applied, checkbox switched ON: the replacement is the
        // half that is missing and must be repaired. The old toggle returned early here.
        Check("S5-repair", BundleClaims.RouteMoves(true, false, true)
                           && !BundleClaims.RouteMoves(true, true, true),
            "with one route applied and one not, enabling moves ONLY the missing route");

        // Same project, checkbox switched OFF: the applied route must be undone even though the other
        // one is already off. The old toggle skipped the whole toggle instead.
        Check("S5-undo", BundleClaims.RouteMoves(true, true, false)
                         && !BundleClaims.RouteMoves(true, false, false),
            "and disabling moves ONLY the route that is actually applied");

        Check("S5-undeclared", !BundleClaims.RouteMoves(false, false, true)
                               && !BundleClaims.RouteMoves(false, true, false),
            "a route the project does not declare is never applied and never touched");

        Check("S5-idempotent", !BundleClaims.RouteMoves(true, true, true)
                               && !BundleClaims.RouteMoves(true, false, false),
            "and a route already in the wanted state costs nothing - the startup enable pass runs " +
            "this for every enabled mod");
    }

    /// <summary>
    /// Gate S13, offline: a content mod's keys are published BEFORE the mod that needs them starts.
    ///
    /// The defect this pins: ContentTool ran only as a POSTFIX on ModEntry.SetEnabled, whose body
    /// loads the mod and calls its OnModEnabled inside itself (ModEntry.cs:198-220) - so every launch
    /// printed three `ct_weapon FAIL key '...' did not load (Failed)` lines from WeaponAdd while
    /// `ct_catalog verify` resolved the very same keys moments later.
    ///
    /// It can tell the fixed order from the broken one because the arm reads the INDEX of Publish
    /// against the index of Init in the same sequence, not merely that both happen. A postfix-only
    /// implementation still contains both steps and still passes any "did we publish?" check; it
    /// cannot pass a check that Publish comes first. Falsified by deleting the prefix line from
    /// BundleClaims.EnableSteps: S13-order then reads [Init, Publish] and fails.
    /// </summary>
    private static void EnableOrderArm()
    {
        IList<BundleClaims.EnableStep> on = BundleClaims.EnableSteps(true, false, true);
        int publish = on.IndexOf(BundleClaims.EnableStep.Publish);
        int init = on.IndexOf(BundleClaims.EnableStep.Init);

        Check("S13-order", publish == 0 && init > publish,
            "switching a content mod ON publishes its keys before the game runs the mod's own " +
            "OnModEnabled (got " + string.Join(",", Names(on)) + ")");

        // The second Publish is the postfix, which is kept because it is the only hook that sees the
        // FINAL Enabled flag. It must cost nothing, or the mod would register its routes twice.
        Check("S13-nodouble", on.Count == 3 && on[2] == BundleClaims.EnableStep.Publish
                              && !BundleClaims.RouteMoves(true, true, true),
            "and the repeat that follows the mod's init is a no-op - a route already in the wanted " +
            "state does not move");

        // The prefix must not fire where there is no init to be early for, or it would publish for a
        // mod the game is about to refuse to load.
        Check("S13-already-on", !BundleClaims.PublishesBeforeInit(true, true, true)
                                && BundleClaims.EnableSteps(true, true, true).Count == 1,
            "a mod that is already enabled gets no second install, only the postfix's no-op");
        Check("S13-no-content", !BundleClaims.PublishesBeforeInit(true, false, false)
                                && BundleClaims.EnableSteps(true, false, false).Count == 1
                                && BundleClaims.EnableSteps(true, false, false)[0] == BundleClaims.EnableStep.Init,
            "and a mod that carries no ContentTool content is not touched at all");

        // The OFF direction is the control: it must NOT gain a prefix step, and the startup pass that
        // disables a mod which was never enabled still has to undo what a previous session left.
        Check("S13-off", BundleClaims.EnableSteps(false, true, true).Count == 2
                         && BundleClaims.EnableSteps(false, true, true)[0] == BundleClaims.EnableStep.Deinit
                         && BundleClaims.EnableSteps(false, true, true)[1] == BundleClaims.EnableStep.Undo,
            "switching OFF undoes the registrations after the game has unloaded the mod, never before");
        Check("S13-off-startup", BundleClaims.EnableSteps(false, false, true).Count == 1
                                 && BundleClaims.EnableSteps(false, false, true)[0] == BundleClaims.EnableStep.Undo,
            "and a mod already off at startup still reaches the undo - SetEnabled returns early, the " +
            "postfix does not");

        // And the model above is only worth anything if ModRoster actually installs a PREFIX. Over
        // the source, with the postfix-only body that shipped as the control in the same run.
        string src = SrcRoot();
        string roster = src == null ? null : Path.Combine(src, "Project", "ModRoster.cs");
        Check("S13-wired", roster != null && File.Exists(roster) && PrefixWired(Strip(File.ReadAllText(roster))),
            "ModRoster patches ModEntry.SetEnabled with a prefix that runs the install");
        Check("S13-wired-ctl",
            !PrefixWired(Strip("class R { static string I() { harmony.Patch(toggle, postfix: " +
                               "new HarmonyMethod(AccessTools.Method(typeof(ModRoster), " +
                               "nameof(AfterSetEnabled)))); return null; } }")),
            "while the postfix-only body that shipped fails the same check, so the arm above is a " +
            "measurement and not a blind pass");
    }

    /// <summary>Does this source hang the install on a PREFIX of the toggle, not only a postfix?</summary>
    private static bool PrefixWired(string text)
    {
        return Regex.IsMatch(text, @"prefix\s*:\s*new\s+HarmonyMethod")
               && Regex.IsMatch(text, @"nameof\s*\(\s*BeforeSetEnabled\s*\)");
    }

    private static string[] Names(IList<BundleClaims.EnableStep> steps)
    {
        string[] names = new string[steps.Count];
        for (int i = 0; i < steps.Count; i++) names[i] = steps[i].ToString();
        return names;
    }

    /// <summary>
    /// Gate S6, offline: an install still carrying the OLD on-disk route-vii edit is DETECTED and
    /// named, never silently treated as off.
    ///
    /// Pure decision, no filesystem: undoing that edit would mean writing into the player's game
    /// installation, which this mod does not do (M2), so the only honest output is a refusal that
    /// names the files and the repair. The control in the same run is the install that never ran the
    /// old code - it must stay null, or every player would be told to verify their game files.
    /// </summary>
    private static void LegacyDiskArm()
    {
        const string ledger = @"D:\PP\...\StreamingAssets\aa\catalog.json.ct-edits";
        const string catalog = @"D:\PP\...\StreamingAssets\aa\catalog.json";

        Check("S6-clean", BundleClaims.LegacyRefusal("mod.a", new List<string>(), ledger, catalog) == null
                          && BundleClaims.LegacyRefusal("mod.a", null, ledger, catalog) == null,
            "an install with no on-disk record says nothing - the normal case must stay silent");

        string said = BundleClaims.LegacyRefusal("mod.a",
            new List<string> { "mutoid_assets_all.bundle", "aln_fireworm_assets_all.bundle" }, ledger, catalog);
        Check("S6-named", said != null && said.StartsWith("REFUSED")
                          && said.Contains("mod.a")
                          && said.Contains("mutoid_assets_all.bundle")
                          && said.Contains("aln_fireworm_assets_all.bundle"),
            "a leftover record is refused BY NAME, naming the mod and every bundle -> " + said);
        Check("S6-repair", said != null && said.Contains(catalog) && said.Contains(ledger)
                           && said.Contains("Verify integrity of game files")
                           && said.Contains("STILL"),
            "and the player is told the exact files and the one sanctioned repair, plus that the " +
            "content is still applied until they run it");
    }

    /// <summary>
    /// Gate S7, offline: route iii's LIVE ownership - one published key, one owning mod, the same
    /// deterministic policy route vii uses, and an unregister that leaves nothing behind.
    ///
    /// It can tell a real policy from "first claim wins" because the same two mods are claimed in BOTH
    /// orders and must produce the SAME winner. An implementation that kept whoever arrived first
    /// fails S7-key-deterministic, and one that let the second in unconditionally fails S7-key-refuse.
    /// </summary>
    private static void KeyClaimsArm()
    {
        const string key = "morgott.demo.weaponadd/sniper";
        string refusal;
        KeyClaim evicted;

        KeyClaim b = KeyClaims.Claim("mod.b", key, @"C:\Mods\B\b.bundle", "assets/mod.b/models/x",
                                     "GameObject", out refusal, out evicted);
        Check("S7-key-claim", b != null && refusal == null && evicted == null && KeyClaims.Holds("mod.b"),
            "the first mod to publish a key owns it");

        KeyClaim z = KeyClaims.Claim("mod.z", key, @"C:\Mods\Z\z.bundle", "assets/mod.z/models/x",
                                     "GameObject", out refusal, out evicted);
        Check("S7-key-refuse", z == null && refusal != null && refusal.StartsWith("REFUSED")
                               && refusal.Contains("mod.b") && refusal.Contains(key),
            "a higher mod id is refused BY NAME, naming the owner and the key -> " + refusal);

        b.Locator = new object();
        object bLocator = b.Locator;
        KeyClaim a = KeyClaims.Claim("mod.a", key, @"C:\Mods\A\a.bundle", "assets/mod.a/models/x",
                                     "GameObject", out refusal, out evicted);
        Check("S7-key-evict", a != null && evicted != null && evicted.Mod == "mod.b"
                              && ReferenceEquals(evicted.Locator, bLocator) && !KeyClaims.Holds("mod.b"),
            "a LOWER mod id takes the key and the loser is handed back WITH its locator, so the caller " +
            "can take it down - a dropped record with a live locator is content nobody can switch off");

        KeyClaims.Drop("mod.a");
        KeyClaims.Claim("mod.z", key, @"C:\Mods\Z\z.bundle", "a", "GameObject", out refusal, out evicted);
        KeyClaims.Claim("mod.b", key, @"C:\Mods\B\b.bundle", "a", "GameObject", out refusal, out evicted);
        Check("S7-key-deterministic", KeyClaims.Holds("mod.b") && !KeyClaims.Holds("mod.z"),
            "and the winner is the same whichever order the manager enables them in");

        // Re-claiming: the SAME record moves, and the old locator comes back so it can be removed.
        KeyClaim held = KeyClaims.Find(key);
        held.Locator = new object();
        object was = held.Locator;
        KeyClaim again = KeyClaims.Claim("mod.b", key, @"C:\Mods\B\v2.bundle", "assets/mod.b/models/y",
                                         "GameObject", out refusal, out evicted);
        Check("S7-key-reclaim", again != null && refusal == null && evicted != null
                                && ReferenceEquals(evicted.Locator, was) && again.Locator == null
                                && again.BundlePath == @"C:\Mods\B\v2.bundle" && KeyClaims.All.Count == 1,
            "a mod re-publishing its own key gets ONE record on the new bundle, and its previous " +
            "locator handed back - a second record would leave a locator nobody ever removes");

        Check("S7-key-owns", KeyClaims.Owns(was) == false && KeyClaims.Owns(null) == false,
            "and a retired locator is no longer recognised as ours, so a re-register cannot read its " +
            "own leftovers as 'the game already has this key'");

        List<KeyClaim> gone = KeyClaims.Drop("mod.b");
        Check("S7-key-drop", gone.Count == 1 && gone[0].Key == key
                             && !KeyClaims.Holds("mod.b") && KeyClaims.All.Count == 0,
            "and unregistering hands every record back and leaves the registry empty");

        Check("S7-key-guard", KeyClaims.Claim(null, key, "p", "a", "GameObject", out refusal, out evicted) == null
                              && refusal != null
                              && KeyClaims.Claim("mod.a", key, "p", null, "GameObject", out refusal, out evicted) == null,
            "an incomplete record is refused rather than registered half-way");
    }

    /// <summary>
    /// Gate S7-add-only, offline: publishing ADDS keys the game does not have, and REFUSES one it does.
    ///
    /// Not a policy choice - a measurement. Addressables.AddResourceLocator appends, GetResourceLocations
    /// unions the locators in order, and LoadAssetAsync takes the first provider-compatible hit, which is
    /// the shipped locator at index 0. A "repoint" would therefore register a locator nobody ever reads:
    /// content silently missing, no error line anywhere - v2's dominant bug class. The control in the
    /// same run is the ADD, which must stay silent, or no mod could publish anything at all.
    /// </summary>
    private static void AddOnlyArm()
    {
        Check("S7-add", KeyClaims.ShippedKeyRefusal("mod.a", "morgott.demo/new_thing", false) == null,
            "a key the game does not have is published without a word - that is the whole route");

        string said = KeyClaims.ShippedKeyRefusal("mod.a", "02_Bodyparts/ALN_Fireworm_BodyAll_Ready.prefab", true);
        Check("S7-no-repoint", said != null && said.StartsWith("REFUSED")
                               && said.Contains("02_Bodyparts/ALN_Fireworm_BodyAll_Ready.prefab")
                               && said.Contains("mod.a") && said.Contains("\"replace\""),
            "a key the game already has is refused BY NAME and the modder is sent to the replacement " +
            "route, which is the one that CAN replace shipped content -> " + said);
    }

    /// <summary>
    /// Gate S8, offline: a project directory OUTSIDE ContentTool's sibling tree resolves to itself.
    ///
    /// A Steam Workshop mod lives at workshop\content\839770\&lt;id&gt;, beside neither ContentTool nor
    /// its siblings. Route7.Toggle used to hand ProjectDir the mod folder's NAME, which resolves against
    /// those two places only - so a Workshop mod declaring "replace" or "publish" silently resolved to
    /// nothing (or, worse, to a same-named local project) and never applied. The fix is to pass the
    /// already-validated DIRECTORY, and this arm pins that ProjectDir takes one. The name form is
    /// asserted BROKEN in the same run: without that control the arm would pass on the old code too.
    /// </summary>
    private static void ProjectDirArm()
    {
        string tmp = Dir(Path.GetTempPath(), "ct_s8_" + Guid.NewGuid().ToString("N"));
        try
        {
            string mods = Dir(tmp, "Mods");
            string modDir = Dir(mods, "ContentTool");
            string sibling = Dir(mods, "Demo");
            File.WriteAllText(Path.Combine(sibling, ContentMods.Manifest), "{}");
            // Not under Mods\ at all - the shape of a subscribed Workshop item.
            string workshop = Dir(Dir(Dir(tmp, "workshop"), "839770"), "3739613434");
            File.WriteAllText(Path.Combine(workshop, ContentMods.Manifest), "{}");

            Check("S8-sibling", ContentMods.ProjectDir(modDir, "Demo") == sibling,
                "a local mod beside ContentTool still resolves by name");
            Check("S8-workshop-path",
                ContentMods.ProjectDir(modDir, workshop) == workshop,
                "and a project DIRECTORY outside that tree resolves to itself -> " + workshop);
            Check("S8-workshop-name",
                ContentMods.ProjectDir(modDir, new DirectoryInfo(workshop).Name) != workshop,
                "while its folder NAME cannot: it is beside nothing we know, which is exactly why the " +
                "enable-time toggle has to pass the directory it already validated");
        }
        finally { try { Directory.Delete(tmp, true); } catch { } }
    }

    /// <summary>
    /// Gate S10, offline: the STARTUP pass installs the live routes of every ENABLED content mod.
    ///
    /// The bug it pins: the deferred startup pass only ever visited DISABLED roster entries, so the
    /// route-vii redirections and route-iii keys - which are session-only in-memory state - were never
    /// re-installed on a later launch. Enable a content mod, it works; restart, the content is gone.
    ///
    /// Two halves, because the fix has two: the SELECTION (the same ContentMods.Enabled discovery the
    /// video and sound routes use, so a Workshop mod is found and a switched-off one is not) and the
    /// IDEMPOTENCE (a mod the SetEnabled postfix already handled must not be registered a second time,
    /// and must not be refused against its own standing claim). The wiring itself is asserted over the
    /// source, with the OLD body as the control in the same run - the arms below cannot reach
    /// ModRoster, which needs the game's own assemblies.
    /// </summary>
    private static void StartupScanArm()
    {
        string mods = Path.Combine(Path.GetTempPath(), "ct_s10_" + Guid.NewGuid().ToString("N"));
        try
        {
            string us = Dir(mods, "ContentTool");
            string on = Dir(mods, "OnMod"), off = Dir(mods, "OffMod");
            string wsOn = Dir(Dir(Dir(mods, "workshop_content"), "839770"), "111");
            foreach (string d in new[] { on, off, wsOn })
                File.WriteAllText(Path.Combine(d, ContentMods.Manifest), "{ \"id\": \"x\" }");

            Dictionary<string, bool> roster = new Dictionary<string, bool>(StringComparer.Ordinal)
            {
                { ModGate.Key(us), true }, { ModGate.Key(on), true },
                { ModGate.Key(wsOn), true }, { ModGate.Key(off), false },
            };

            int skipped;
            List<string> install = ContentMods.Enabled(us, ContentMods.Manifest, roster, null, out skipped);
            Check("S10-selects-enabled",
                install.Exists(h => ModGate.Key(h) == ModGate.Key(on))
                && install.Exists(h => ModGate.Key(h) == ModGate.Key(wsOn)),
                "the startup pass installs the local AND the Workshop content mod the player has " +
                "switched on -> " + string.Join(", ", install.ToArray()));
            Check("S10-skips-disabled", !install.Exists(h => ModGate.Key(h) == ModGate.Key(off)) && skipped == 1,
                "and never the one switched off, which the OFF half of the same pass undoes instead " +
                "(" + skipped + " skipped)");

            // Idempotence: the SetEnabled postfix got there first for this mod, one frame earlier.
            string refusal;
            BundleClaim evicted;
            BundleClaims.Claim("mod.on", "mutoid_assets_all.bundle", @"C:\Mods\On\a.bundle", out refusal, out evicted);
            Check("S10-idempotent", !BundleClaims.RouteMoves(true, BundleClaims.Holds("mod.on"), true),
                "a mod whose replacement is already registered is not touched again by the startup pass");
            BundleClaim again = BundleClaims.Claim("mod.on", "mutoid_assets_all.bundle",
                                                  @"C:\Mods\On\a.bundle", out refusal, out evicted);
            Check("S10-no-self-refusal", again != null && refusal == null && BundleClaims.All.Count == 1,
                "and if it did register again, its own standing claim would not refuse it as somebody " +
                "else's - one record, no REFUSED -> " + (refusal ?? "no refusal"));
            BundleClaims.Drop("mod.on");

            // The wiring, over the source, with the shipped body as the control.
            string src = SrcRoot();
            string roster_cs = src == null ? null : Path.Combine(src, "Project", "ModRoster.cs");
            Check("S10-wired", roster_cs != null && File.Exists(roster_cs) && Wired(Strip(File.ReadAllText(roster_cs))),
                "ModRoster's startup pass runs that discovery and toggles what it finds ON");
            Check("S10-wired-ctl",
                !Wired(Strip("class R { static void Reconcile() { foreach (ModEntry e in m.Mods) { " +
                             "if (e == null || e.Enabled) continue; Bake.Route7.Toggle(e.Directory, false); } } }")),
                "while the body that shipped - disabled entries only, toggled OFF - fails the same " +
                "check, so the arm above is a measurement and not a blind pass");
        }
        finally { try { Directory.Delete(mods, true); } catch (IOException) { } }
    }

    /// <summary>Does this source install the live routes of the mods the shared discovery finds?</summary>
    private static bool Wired(string text)
    {
        return Regex.IsMatch(text, @"ContentMods\s*\.\s*Enabled")
               && Regex.IsMatch(text, @"Route7\s*\.\s*Toggle\s*\(\s*\w+\s*,\s*(on|true)\s*\)");
    }

    /// <summary>
    /// Gate S9, offline: a re-bake takes back the banks the project has DROPPED, and touches nothing
    /// else in the mod's own folder.
    ///
    /// The bug it pins: the bake only overwrote the banks it was writing, so a removed replacement
    /// stayed in Dist\Sounds - and SoundLoad loads every .bnk there, so it kept playing and would
    /// ship inside the package.
    ///
    /// It can tell OURS from NOT OURS because all five files sit in the same folder and three of them
    /// are named exactly like ours: the only thing that differs is the bank id in the BKHD. An
    /// implementation that deleted by file name takes 333.bnk with it and fails; one that deleted
    /// nothing fails the first arm; one that wiped the folder fails every kept arm.
    /// </summary>
    private static void StaleBankArm()
    {
        const string mod = "morgott.demo";
        string dir = Path.Combine(Path.GetTempPath(), "ct_s9_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string current = Path.Combine(dir, "111.bnk"), stale = Path.Combine(dir, "222.bnk");
            string other = Path.Combine(dir, "333.bnk"), named = Path.Combine(dir, "music.bnk");
            string junk = Path.Combine(dir, "444.bnk");
            Bnk(current, BankPrune.BankId(mod, 111));
            Bnk(stale, BankPrune.BankId(mod, 222));
            Bnk(other, BankPrune.BankId("somebody.else", 333));      // another project's bake
            Bnk(named, BankPrune.BankId(mod, 555));                  // hand-placed, not <media>.bnk
            File.WriteAllBytes(junk, Encoding.ASCII.GetBytes("this is not a wwise bank at all"));

            uint m;
            Check("S9-owns", BankPrune.Generated(stale, mod, out m) && m == 222
                             && BankPrune.Generated(current, mod, out m),
                "a bank whose NAME and BKHD bank id both say this project's bake wrote it is ours");
            Check("S9-not-ours", !BankPrune.Generated(other, mod, out m)
                                 && !BankPrune.Generated(named, mod, out m)
                                 && !BankPrune.Generated(junk, mod, out m),
                "another project's bake, a hand-placed name and a file that is not a bank are not");

            string said = BankPrune.Sweep(dir, mod, new List<uint> { 111 });
            Check("S9-drops-stale", !File.Exists(stale) && said != null && said.Contains("222.bnk"),
                "the media the project no longer declares loses its bank, and the line names it -> " + said);
            Check("S9-keeps-current", File.Exists(current) && !said.Contains("111.bnk"),
                "the media it still declares keeps the bank the same run just wrote");
            Check("S9-keeps-foreign", File.Exists(other) && File.Exists(named) && File.Exists(junk)
                                      && said.Contains("333.bnk") && said.Contains("music.bnk"),
                "and every file this bake did not write stays, named in the log rather than removed");

            string twice = BankPrune.Sweep(dir, mod, new List<uint> { 111 });
            Check("S9-idempotent", twice != null && !twice.Contains("removed") && File.Exists(current),
                "a second sweep removes nothing - the folder is already what the project declares -> " + twice);

            Check("S9-empty", BankPrune.Sweep(Path.Combine(dir, "nope"), mod, new List<uint>()) == null,
                "and a folder that does not exist is silence, not a failure");
        }
        finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
    }

    /// <summary>The 16-byte BKHD prologue of a media-only bank, which is all the delete rule reads.</summary>
    private static void Bnk(string path, uint bankId)
    {
        List<byte> b = new List<byte>();
        b.AddRange(Encoding.ASCII.GetBytes("BKHD"));
        b.AddRange(BitConverter.GetBytes((uint)20));
        b.AddRange(BitConverter.GetBytes((uint)140));
        b.AddRange(BitConverter.GetBytes(bankId));
        b.AddRange(new byte[12]);
        File.WriteAllBytes(path, b.ToArray());
    }

    /// <summary>
    /// Gate S11, offline: a route's summary line counts what its register calls ACTUALLY answered,
    /// not what the manifest declared. It shipped as "PUBLISHED 2 key(s)" over a run where one of
    /// the two was refused, so a manifest error read as a success and the mod was believed applied.
    /// An implementation that counted the input list reports 2/2 here and goes red.
    /// </summary>
    private static void OutcomeArm()
    {
        List<string> mixed = new List<string>
        {
            "published 'demo/sniper' LIVE for 'mod.a'",
            "REFUSED: mod 'mod.b' already publishes key 'demo/axe'"
        };
        string said = BundleClaims.Outcome(mixed, "key(s)", "published LIVE", "mod.a");
        Check("S11-count", said.StartsWith("1/2 key(s) published LIVE for 'mod.a'", StringComparison.Ordinal),
            "one of two published and the line says so -> " + said.Replace(Environment.NewLine, " / "));
        Check("S11-names", said.Contains("1 of them refused") && said.Contains("mod.b")
                           && said.Contains("demo/axe"),
            "and the refusal is NAMED in the summary, not only in the scrollback above it");

        string clean = BundleClaims.Outcome(new List<string> { "a", "b" }, "bundle(s)", "redirected LIVE", "mod.a");
        Check("S11-clean", clean.StartsWith("2/2 bundle(s) redirected LIVE", StringComparison.Ordinal)
                           && !clean.Contains("refused"),
            "a run with nothing refused says 2/2 and stays quiet - the control in the same arm");
        Check("S11-none", BundleClaims.Outcome(new List<string> { "REFUSED: x", "REFUSED: y" },
                  "key(s)", "published LIVE", "mod.a").StartsWith("0/2", StringComparison.Ordinal),
            "and a run where everything was refused reports 0, never the declared count");
    }

    /// <summary>
    /// Gate S12, offline: the SOUND route has the same per-media ownership policy the bundle and key
    /// routes have. It had none - ContentState.Claim is per mod per ROUTE, so two mods shipping the
    /// same &lt;mediaId&gt;.bnk both loaded and the winner was whoever the mod manager listed last
    /// (measured in game: bank B over bank A, silently, no warning anywhere).
    ///
    /// Two halves, both here: the REFUSAL (a standing owner keeps the media, and the loser is named)
    /// and the DETERMINISM (the loader walks the enabled mods lowest id first, so the same two mods
    /// give the same owner on every machine). Sound refuses instead of evicting because a loaded
    /// bank cannot be unloaded in-session - that is gate C-restore, measured.
    /// </summary>
    private static void SoundOwnerArm()
    {
        const string a = @"C:\Games\PP\Mods\AAA_Sounds", b = @"C:\Games\PP\Mods\ZZZ_Sounds";
        const string media = "208540756", bnk = media + ".bnk";

        Check("S12-free", ContentState.Owner("sound", media) == null
                          && BundleClaims.MediaRefusal(null, "ZZZ_Sounds", media, bnk) == null,
            "a media nobody replaces is loaded without a word - that is the normal case");

        ContentState.Claim(a, "sound");
        ContentState.Served(a, "sound", media);
        string owner = ContentState.Owner("sound", media);
        Check("S12-owner", owner != null && Path.GetFileName(owner) == "aaa_sounds",
            "the mod that loaded it is found back BY MEDIA, off the same ledger the loading writes");

        string refusal = BundleClaims.MediaRefusal(Path.GetFileName(owner), "ZZZ_Sounds", media, bnk);
        Check("S12-refuse", refusal != null && refusal.StartsWith("REFUSED", StringComparison.Ordinal)
                            && refusal.Contains("aaa_sounds") && refusal.Contains("ZZZ_Sounds")
                            && refusal.Contains(media),
            "a second mod shipping the same media is refused BY NAME, naming both mods -> " + refusal);
        Check("S12-self", BundleClaims.MediaRefusal(Path.GetFileName(owner), "AAA_Sounds", media, bnk) == null,
            "while the OWNER is not refused its own media - a re-enable must still load its banks");
        Check("S12-scope", ContentState.Owner("sound", "1953710523") == null
                           && ContentState.Owner("video", media) == null,
            "and ownership is per media AND per route - it does not spill onto either neighbour");

        Check("S12-lowest", BundleClaims.Keeps("mod.a", "mod.z") && !BundleClaims.Keeps("mod.z", "mod.a")
                            && BundleClaims.Keeps("mod.a", "mod.a"),
            "the policy is the routes' own: the lower mod id keeps a contested thing");
        List<string> order = new List<string> { b, a };
        order.Sort(StringComparer.OrdinalIgnoreCase);
        Check("S12-order", order[0] == a,
            "and the loader walks the enabled mods in that order, so the winner reaches a contested " +
            "media before the loser whichever order the manager happened to list them in");

        ContentState.Release(a, "sound");
        Check("S12-release", ContentState.Owner("sound", media) == null,
            "switching the owner off frees the media again - the refusal reads live state, not a boot scan");
    }

    /// <summary>
    /// Gate S14, offline, against real folders: a package that would redistribute Phoenix Point's
    /// own data is REFUSED BY NAME, and nothing is left on disk when it is.
    ///
    /// It can tell REDISTRIBUTED from OURS because the SAME author folder is packaged five times and
    /// only ONE file differs between the runs: the first run is a clean project that must package,
    /// and each of the other four adds exactly one forbidden thing. An implementation that refused
    /// everything fails the clean arm; one that refused nothing fails all four; one that refused by
    /// count rather than by name fails the naming half of each.
    ///
    /// The categories are the four ways the game's own bytes reach a zip: a patched copy of a shipped
    /// bundle (built on the PLAYER's machine by Route7.ApplyProject, which is exactly why it never
    /// has to ship), a shipped bundle identity, the .ct-backup an older ContentTool left inside the
    /// installation, and the edit ledger that went with it.
    /// </summary>
    private static void PackageArm()
    {
        string tmp = Dir(Path.GetTempPath(), "ct_s14_" + Guid.NewGuid().ToString("N"));
        try
        {
            string author = Dir(tmp, "MyMod");
            File.WriteAllText(Path.Combine(author, "meta.json"),
                "{ \"ID\": \"morgott.demo.mymod\", \"AssemblyName\": \"\", " +
                "\"Dependencies\": [ \"com.morgott.ContentTool\" ] }");
            File.WriteAllText(Path.Combine(author, "ppcontent.json"),
                "{ \"id\": \"morgott.demo.mymod\", \"bundle\": \"MyMod.bundle\", " +
                "\"replace\": [ { \"bundle\": \"px_equipment_assets_all.bundle\", " +
                "\"asset\": \"WPN\", \"texture\": \"rifle\" } ] }");
            File.WriteAllBytes(Path.Combine(Dir(Dir(author, "Content"), "Textures"), "rifle.png"), new byte[] { 1, 2, 3 });
            string dist = Dir(author, "Dist");
            File.WriteAllBytes(Path.Combine(dist, "MyMod.bundle"), new byte[64]);
            File.WriteAllBytes(Path.Combine(Dir(dist, "Sounds"), "208540756.bnk"), new byte[16]);

            bool ok;
            string outDir = Path.Combine(tmp, "out0");
            string said = Package.Run(author, outDir, null, out ok);
            Check("S14-clean", ok && File.Exists(Path.Combine(outDir, "meta.json"))
                              && File.Exists(Path.Combine(outDir, "Dist\\MyMod.bundle"))
                              && File.Exists(Path.Combine(outDir, "Dist\\Sounds\\208540756.bnk"))
                              && File.Exists(Path.Combine(outDir, "Content\\Textures\\rifle.png")),
                "a project with its OWN bundle, its prebuilt bank and its sources packages -> " +
                said.Replace("\n", " "));
            Check("S14-nothing-extra", !Directory.Exists(Path.Combine(outDir, "src"))
                                       && !Directory.Exists(Path.Combine(outDir, "bin")),
                "and carries nothing the player has no use for");

            // Each forbidden category, one at a time, into a FRESH output folder.
            string[,] cases =
            {
                { "Patched\\px_equipment_assets_all.bundle", "PATCHED COPY", "px_equipment_assets_all.bundle" },
                { "px_equipment_assets_all.bundle",          "SHIPPED PHOENIX POINT BUNDLE IDENTITY", "px_equipment_assets_all.bundle" },
                { "catalog.json.ct-backup",                  "INSTALL BACKUP", "catalog.json.ct-backup" },
                { "catalog.json.ct-edits",                   "EDIT LEDGER", "catalog.json.ct-edits" },
            };
            for (int i = 0; i < cases.GetLength(0); i++)
            {
                string planted = Path.Combine(dist, cases[i, 0]);
                Directory.CreateDirectory(Path.GetDirectoryName(planted));
                File.WriteAllBytes(planted, new byte[8]);
                string into = Path.Combine(tmp, "bad" + i);
                said = Package.Run(author, into, null, out ok);
                Check("S14-refuse-" + i,
                    !ok && said.StartsWith("REFUSED", StringComparison.Ordinal)
                    && said.Contains(cases[i, 1]) && said.Contains(cases[i, 2])
                    && !Directory.Exists(into),
                    "Dist\\" + cases[i, 0] + " -> " + said.Replace(Environment.NewLine, " ").Replace("\n", " "));
                File.Delete(planted);
                if (cases[i, 0].StartsWith("Patched", StringComparison.Ordinal))
                    Directory.Delete(Path.GetDirectoryName(planted), true);
            }

            // ...and the clean project still packages after all four are gone, so the arms above
            // measured the planted file and not some state the first run left behind.
            said = Package.Run(author, Path.Combine(tmp, "out1"), null, out ok);
            Check("S14-clean-again", ok, "the same project packages again once the four are removed -> " +
                said.Replace("\n", " "));

            // An empty project ships nothing: refused rather than uploaded.
            string bare = Dir(tmp, "Bare");
            File.Copy(Path.Combine(author, "meta.json"), Path.Combine(bare, "meta.json"));
            File.WriteAllText(Path.Combine(bare, "ppcontent.json"), "{ \"id\": \"x\", \"bundle\": \"x.bundle\" }");
            said = Package.Run(bare, Path.Combine(tmp, "bare-out"), null, out ok);
            Check("S14-empty", !ok && said.Contains("ships nothing at all"),
                "a project with nothing baked is refused instead of packaged empty -> " +
                said.Replace("\n", " "));

            ModelessArm(tmp);

            // meta.json, the half that decides whether an ORDINARY PLAYER ends up with a working mod.
            List<string> files = new List<string> { "meta.json", "MyMod.dll" };
            Check("S14-meta-ok",
                Package.MetaRefusal("{ \"ID\": \"a.b\", \"AssemblyName\": \"MyMod.dll\", " +
                                    "\"Dependencies\": [ \"com.morgott.ContentTool\" ] }", files) == null,
                "a meta.json declaring the engine dependency and shipping its own DLL passes");
            string why = Package.MetaRefusal("{ \"ID\": \"a.b\", \"Dependencies\": [ ] }", files);
            Check("S14-meta-dep", why != null && why.Contains("com.morgott.ContentTool"),
                "one that does not declare ContentTool is refused - the player would install a mod " +
                "that silently does nothing -> " + why);
            why = Package.MetaRefusal("{ \"ID\": \"a.b\", \"AssemblyName\": \"Missing.dll\", " +
                                      "\"Dependencies\": [ \"com.morgott.ContentTool\" ] }", files);
            Check("S14-meta-dll", why != null && why.Contains("Missing.dll"),
                "and one declaring an assembly the package does not contain is refused BY NAME -> " + why);
            Check("S14-meta-id",
                Package.MetaRefusal("{ \"Dependencies\": [ \"com.morgott.ContentTool\" ] }", files) != null,
                "as is one with no ID at all");

            // The manifest reader the refusal leans on: the mod's OWN bundle is the top-level one,
            // never a "replace" target. If those two swapped, every package would refuse its own
            // bundle and accept the shipped one - the exact inversion this gate exists to prevent.
            string manifest = File.ReadAllText(Path.Combine(author, "ppcontent.json"));
            Check("S14-ownbundle", Package.OwnBundle(manifest) == "MyMod.bundle"
                                   && Package.ReplaceTargets(manifest).Contains("px_equipment_assets_all.bundle")
                                   && !Package.ReplaceTargets(manifest).Contains("MyMod.bundle"),
                "own bundle '" + Package.OwnBundle(manifest) + "', replace target(s) " +
                string.Join(", ", Package.ReplaceTargets(manifest).ToArray()));

            // ...and the SAME manifest with its properties in the other order is the same manifest.
            // JSON property order is not significant and the runtime reader takes either, so reading
            // "the bundle before the replace array" made a legal manifest answer null - and the
            // packager then refused the mod's OWN bundle as a shipped one.
            string swapped =
                "{ \"id\": \"morgott.demo.mymod\", " +
                "\"replace\": [ { \"bundle\": \"px_equipment_assets_all.bundle\", " +
                "\"asset\": \"WPN\", \"texture\": \"rifle\" } ], \"bundle\": \"MyMod.bundle\" }";
            Check("S14-order-blind",
                Package.OwnBundle(swapped) == Package.OwnBundle(manifest)
                && string.Join(",", Package.ReplaceTargets(swapped).ToArray()) ==
                   string.Join(",", Package.ReplaceTargets(manifest).ToArray()),
                "\"replace\" first reads the same as \"bundle\" first: own '" +
                Package.OwnBundle(swapped) + "', target(s) " +
                string.Join(", ", Package.ReplaceTargets(swapped).ToArray()));

            // And the whole packaging run agrees: the same project packages with either ordering.
            File.WriteAllText(Path.Combine(author, "ppcontent.json"), swapped);
            said = Package.Run(author, Path.Combine(tmp, "out2"), null, out ok);
            Check("S14-order-packages", ok,
                "a project whose ppcontent.json writes \"replace\" before \"bundle\" packages, its own " +
                "Dist\\MyMod.bundle accepted -> " + said.Replace("\n", " "));

            BakedSourcesArm(tmp);
            PickupArm(tmp);
        }
        finally { try { Directory.Delete(tmp, true); } catch (IOException) { } }
    }

    /// <summary>
    /// Gate S14-modeless, offline: THE SHAPE THE OLD RULE REFUSED, and the shape it must go on
    /// refusing.
    ///
    /// "model" is optional on a weapons entry, so the smallest legal weapon mod is meta.json +
    /// ppcontent.json + its own .dll and no Content\ and no Dist\ - and the packager used to delete
    /// its staging and tell it to bake something that does not exist. The rule is now "is there a
    /// payload": a staged file that is not paperwork, or a manifest that declares a rung.
    ///
    /// FALSIFIED BOTH WAYS, one variable at a time on one folder. An implementation that went back
    /// to "a Content\ or a Dist\ folder" fails all three accept arms; one that simply stopped
    /// refusing fails S14-modeless-nothing, where a manifest declaring no rung sits beside no file
    /// at all and is exactly the folder a player would install for no effect.
    ///
    /// S14-modeless-empty-rung is the same shape one step subtler, and it is the one a TEXT MATCH on
    /// the manifest gets wrong: "weapons": [] names a rung and holds nothing, so the mod adds no
    /// weapon, ships no file, and used to package clean - past a refusal whose own words say a row
    /// is required. A rung counts only when something is inside it.
    /// </summary>
    private static void ModelessArm(string tmp)
    {
        string dll = Path.Combine(Dir(tmp, "modeless-bin"), "WeaponAdd.dll");
        File.WriteAllBytes(dll, new byte[32]);
        string weapons =
            "{ \"id\": \"morgott.demo.weaponadd\", \"bundle\": \"WeaponAdd.bundle\", " +
            "\"weapons\": [ { \"id\": \"Morgott_X_WeaponDef\", \"clone\": \"PX_ShotgunRifle_WeaponDef\", " +
            "\"guid\": \"c7a9f1d2-4b6e-4a3c-8f5b-7d1e9a2c4b01\" } ] }";
        string norung = "{ \"id\": \"morgott.demo.weaponadd\", \"bundle\": \"WeaponAdd.bundle\" }";
        string emptyrung = "{ \"id\": \"morgott.demo.weaponadd\", \"bundle\": \"WeaponAdd.bundle\", " +
                           "\"weapons\": [] }";

        // { staged assembly, manifest, meta's AssemblyName, expected }
        object[,] cells =
        {
            { dll,  weapons, "WeaponAdd.dll", true  },   // the blocker: a weapon with no model of its own
            { null, weapons, "",              true  },   // the manifest alone is a payload
            { dll,  norung,  "WeaponAdd.dll", true  },   // the assembly alone is a payload
            { null, norung,  "",              false },   // neither: still refused
            { null, emptyrung, "",            false },   // a rung that declares nothing is not one
        };
        string[] names = { "dll+rung", "rung-only", "dll-only", "nothing", "empty-rung" };

        for (int i = 0; i < cells.GetLength(0); i++)
        {
            string author = Dir(tmp, "Modeless" + i);
            File.WriteAllText(Path.Combine(author, "meta.json"),
                "{ \"ID\": \"morgott.demo.weaponadd\", \"AssemblyName\": \"" + (string)cells[i, 2] + "\", " +
                "\"Dependencies\": [ \"com.morgott.ContentTool\" ] }");
            File.WriteAllText(Path.Combine(author, "ppcontent.json"), (string)cells[i, 1]);

            bool ok;
            string outDir = Path.Combine(tmp, "modeless-out" + i);
            string said = Package.Run(author, outDir, (string)cells[i, 0], out ok);
            bool want = (bool)cells[i, 3];
            // An accepted package that declared an assembly must actually carry it; a refused one
            // must have been deleted whole, which the third clause below checks instead.
            bool staged = !want || cells[i, 0] == null ||
                          File.Exists(Path.Combine(outDir, "WeaponAdd.dll"));
            Check("S14-modeless-" + names[i],
                ok == want && staged &&
                (want || (said.Contains("ships nothing at all") && !Directory.Exists(outDir))),
                (want ? "packages" : "is refused") + " with no Content\\ and no Dist\\ -> " +
                said.Replace(Environment.NewLine, " ").Replace("\n", " "));
        }
    }

    /// <summary>
    /// Gate S22, offline: the DLL PICKUP `ct_package` runs on instead of compiling, and the refusal
    /// that stands behind it.
    ///
    /// ONE author folder, and only the DLL moves. It ships an assembly nobody compiles at package
    /// time, so the whole in-game verb rests on Package.BuiltAssembly finding the file meta.json
    /// names - and on the package being REFUSED BY NAME when it does not. An implementation that
    /// stopped looking fails the accept arms; one that started passing a path back for a file that
    /// is not there, or that quietly dropped the AssemblyName refusal, fails S22-missing; one that
    /// grabbed any .dll it saw fails S22-content-only, where the mod declares none and a stray DLL
    /// sits in the folder.
    /// </summary>
    private static void PickupArm(string tmp)
    {
        string author = Dir(tmp, "Pickup");
        File.WriteAllText(Path.Combine(author, "meta.json"),
            "{ \"ID\": \"morgott.demo.pickup\", \"AssemblyName\": \"Pickup.dll\", " +
            "\"Dependencies\": [ \"com.morgott.ContentTool\" ] }");
        File.WriteAllText(Path.Combine(author, "ppcontent.json"),
            "{ \"id\": \"morgott.demo.pickup\", \"bundle\": \"Pickup.bundle\", " +
            "\"weapons\": [ { \"id\": \"Morgott_X_WeaponDef\", \"clone\": \"PX_ShotgunRifle_WeaponDef\" } ] }");

        // Nowhere yet: the verb hands Run a null and Run refuses the package by the declared name.
        bool ok;
        Check("S22-missing-none", Package.BuiltAssembly(author) == null,
            "a declared assembly that is nowhere under the project is not invented");
        string outDir = Path.Combine(tmp, "pickup-out-missing");
        string said = Package.Run(author, outDir, Package.BuiltAssembly(author), out ok);
        Check("S22-missing", !ok && said.Contains("Pickup.dll") && !Directory.Exists(outDir),
            "and the package is refused BY NAME rather than shipping a mod the game will not load " +
            "-> " + said.Replace("\n", " "));

        // The author builds it in their IDE. Nothing else about the project changes.
        string built = Path.Combine(Dir(Dir(Dir(author, "bin"), "Release"), "net472"), "Pickup.dll");
        File.WriteAllBytes(built, new byte[32]);
        Check("S22-finds-built", Package.BuiltAssembly(author) == built,
            "the DLL the author built is found where a csproj puts it -> " + Package.BuiltAssembly(author));
        outDir = Path.Combine(tmp, "pickup-out");
        said = Package.Run(author, outDir, Package.BuiltAssembly(author), out ok);
        Check("S22-packages", ok && File.Exists(Path.Combine(outDir, "Pickup.dll"))
                              && !Directory.Exists(Path.Combine(outDir, "bin")),
            "the same project now packages, carrying the assembly at the ROOT of the release and " +
            "not the bin\\ tree it was found in -> " + said.Replace("\n", " "));
        Check("S22-says-where", said.Contains(outDir),
            "and the result names the folder it wrote, which is the only way an author finds it " +
            "on their own machine -> " + said.Replace("\n", " "));

        // A CONTENT-ONLY mod declares no assembly, and a stray DLL beside it is not one.
        string content = Dir(tmp, "PickupContentOnly");
        File.WriteAllText(Path.Combine(content, "meta.json"),
            "{ \"ID\": \"morgott.demo.pickup2\", \"AssemblyName\": \"\", " +
            "\"Dependencies\": [ \"com.morgott.ContentTool\" ] }");
        File.WriteAllBytes(Path.Combine(content, "Stray.dll"), new byte[8]);
        Check("S22-content-only", Package.BuiltAssembly(content) == null,
            "a mod that declares no assembly picks up nothing, whatever is lying in the folder");
    }

    /// <summary>
    /// Gate S19, offline: a package leaves behind the source media whose baked bank it already
    /// carries, and carries every source that has no such bank.
    ///
    /// ONE author folder, FOUR sources, and only the Dist\Sounds\ contents differ between them - so
    /// an implementation that dropped all four fails the two keep arms, one that dropped none fails
    /// the two drop arms, and one that keyed on the FOLDER rather than on the bank fails
    /// S19-keep-unbaked. The texture arm is the one that matters most: Dist\MyMod.bundle IS a baked
    /// artefact and the .png IS a source, and the .png must still ship, because Route7.ApplyProject
    /// reads it on the PLAYER's machine. "Something was baked" can never license dropping a source.
    /// </summary>
    private static void BakedSourcesArm(string tmp)
    {
        string author = Dir(tmp, "Sounded");
        File.WriteAllText(Path.Combine(author, "meta.json"),
            "{ \"ID\": \"morgott.demo.sounded\", \"AssemblyName\": \"\", " +
            "\"Dependencies\": [ \"com.morgott.ContentTool\" ] }");
        // 423563089 is DECLARED under the author's own filename; 208540756 uses the bare
        // <mediaId>.ext convention. Both are the shapes ct_sound bake itself accepts.
        File.WriteAllText(Path.Combine(author, "ppcontent.json"),
            "{ \"id\": \"morgott.demo.sounded\", \"bundle\": \"MyMod.bundle\", " +
            "\"sounds\": [ { \"media\": 423563089, \"file\": \"my_track.mp3\" } ] }");
        string replace = Dir(Dir(Dir(author, "Content"), "Audio"), "Replace");
        foreach (string f in new[] { "208540756.mp3", "my_track.mp3", "18839791.mp3" })
            File.WriteAllBytes(Path.Combine(replace, f), new byte[2048]);
        File.WriteAllBytes(Path.Combine(Dir(Dir(author, "Content"), "Textures"), "rifle.png"), new byte[512]);
        string dist = Dir(author, "Dist");
        File.WriteAllBytes(Path.Combine(dist, "MyMod.bundle"), new byte[64]);
        // Banks for two of the three sources. 18839791 deliberately has NONE.
        foreach (string b in new[] { "208540756.bnk", "423563089.bnk" })
            File.WriteAllBytes(Path.Combine(Dir(dist, "Sounds"), b), new byte[16]);

        bool ok;
        string manifestText = File.ReadAllText(Path.Combine(author, "ppcontent.json"));

        // The DROP RULE itself, measured on the function rather than through Run: with 18839791
        // unbaked the whole package is refused (S19-refuse-unbaked below), so this is the only place
        // the keep-vs-drop split is still observable with a source that has no bank.
        List<string> unbaked;
        List<string> drop = Package.BakedAlready(author, manifestText, out unbaked);
        Check("S19-drop-convention", drop.Contains("Content\\Audio\\Replace\\208540756.mp3"),
            "a source named after the media it replaces is baked-already once its bank is in " +
            "Dist\\Sounds -> " + string.Join(", ", drop.ToArray()));
        Check("S19-drop-declared", drop.Contains("Content\\Audio\\Replace\\my_track.mp3"),
            "and so is one the manifest declares under the author's own filename, so the rule reads " +
            "\"sounds\" and not just the file name");
        Check("S19-keep-unbaked", !drop.Contains("Content\\Audio\\Replace\\18839791.mp3")
                                  && unbaked.Count == 1 && unbaked[0].Contains("18839791.mp3"),
            "while a source with NO bank in Dist\\Sounds is not dropped, it is reported UNBAKED - the " +
            "rule is the baked artefact, not the folder the source sits in -> " +
            string.Join(", ", unbaked.ToArray()));

        // S19-refuse-unbaked: the silent-dead-package hole. Content\Audio\Replace\18839791.mp3 with no
        // Dist\Sounds\18839791.bnk is a mod that installs, enables and plays the SHIPPED sound, because
        // the player's game only ever loads the bank. It used to package clean.
        string outDir = Path.Combine(tmp, "sounded-out");
        string said = Package.Run(author, outDir, null, out ok);
        Check("S19-refuse-unbaked", !ok && said.Contains("NEVER BAKED") && said.Contains("18839791.mp3")
                                    && !Directory.Exists(outDir),
            "a sound source with no bank beside it is refused BY NAME and the staged folder is deleted " +
            "-> " + said.Replace("\n", " "));
        Check("S19-refuse-is-not-about-redistribution",
            !said.Contains("must never be redistributed"),
            "and the refusal does not claim the author was shipping Phoenix Point's own data, which " +
            "this one is not about");

        // FALSIFIED THE OTHER WAY: bake the missing bank and NOTHING else changes - the same project
        // packages, and now leaves all three sources behind because all three have banks.
        File.WriteAllBytes(Path.Combine(Path.Combine(dist, "Sounds"), "18839791.bnk"), new byte[16]);
        string good = Path.Combine(tmp, "sounded-out-baked");
        said = Package.Run(author, good, null, out ok);
        string staged = Path.Combine(good, "Content\\Audio\\Replace");
        Check("S19-baked-packages", ok && !Directory.Exists(staged),
            "with every source baked the same project packages and the source folder is pruned away " +
            "entirely -> " + said.Replace("\n", " "));
        Check("S19-keep-texture", File.Exists(Path.Combine(good, "Content\\Textures\\rifle.png")),
            "and a texture still ships even though Dist\\MyMod.bundle was baked, because " +
            "Route7.ApplyProject reads it on the PLAYER's machine");
        Check("S19-says-so", said.Contains("LEFT BEHIND") && said.Contains("208540756.mp3")
                             && said.Contains("my_track.mp3") && said.Contains("18839791.mp3"),
            "and the packager names what it left behind rather than shrinking the zip silently -> " +
            said.Replace("\n", " "));

        // And with NO banks at all the refusal names every one of the three, so the arm above measured
        // the bank and not the extension.
        Directory.Delete(Path.Combine(dist, "Sounds"), true);
        said = Package.Run(author, Path.Combine(tmp, "sounded-out-nobank"), null, out ok);
        Check("S19-no-bank-refuses-all", !ok && said.Contains("208540756.mp3")
                                         && said.Contains("my_track.mp3") && said.Contains("18839791.mp3"),
            "with no banks baked at all all three sources are named as never baked -> " +
            said.Replace("\n", " "));

        // The CONTROL for the whole rule: a project with no Content\Audio\Replace\ at all is untouched
        // by it. A refusal that fired here would be a packager nobody could ship a texture mod with.
        string tex = Dir(tmp, "TexOnly");
        File.WriteAllText(Path.Combine(tex, "meta.json"),
            "{ \"ID\": \"morgott.demo.texonly\", \"AssemblyName\": \"\", " +
            "\"Dependencies\": [ \"com.morgott.ContentTool\" ] }");
        File.WriteAllText(Path.Combine(tex, "ppcontent.json"),
            "{ \"id\": \"morgott.demo.texonly\", \"bundle\": \"TexOnly.bundle\" }");
        File.WriteAllBytes(Path.Combine(Dir(Dir(tex, "Content"), "Textures"), "a.png"), new byte[64]);
        said = Package.Run(tex, Path.Combine(tmp, "texonly-out"), null, out ok);
        Check("S19-control-no-sounds", ok && !said.Contains("NEVER BAKED"),
            "a project with no sound sources packages exactly as before, Content\\ and no Dist\\ " +
            "included -> " + said.Replace("\n", " "));
    }

    /// <summary>
    /// Gate S16, offline: a published key's declared "type" resolves ACROSS assemblies, so a class
    /// that lives in another Unity module than UnityEngine.Object is not refused as "not a type this
    /// game has" - while a name that really is unknown still is.
    ///
    /// It can tell WIDER from LOOSER because every arm names a type, never a shape: the split arm
    /// proves the assembly holding UnityEngine.Object does NOT have AnimationClip and that resolution
    /// finds it anyway; the refusal arm proves an invented name still answers null. An implementation
    /// that returned typeof(object) for anything passes the first and fails the second.
    /// </summary>
    private static void TypeResolveArm()
    {
        // The real split, out of the player's own install: AnimationClip is in
        // UnityEngine.AnimationModule.dll, UnityEngine.Object in UnityEngine.CoreModule.dll.
        string managed = "D:\\PP-Instance2\\PhoenixPointWin64_Data\\Managed";
        List<Assembly> modules = new List<Assembly>();
        ResolveEventHandler beside = (s, e) =>
        {
            string dll = Path.Combine(managed, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(dll) ? Assembly.LoadFrom(dll) : null;
        };
        AppDomain.CurrentDomain.AssemblyResolve += beside;
        try
        {
            if (Directory.Exists(managed))
                foreach (string dll in new[] { "UnityEngine.CoreModule.dll", "UnityEngine.AnimationModule.dll" })
                    try { modules.Add(Assembly.LoadFrom(Path.Combine(managed, dll))); }
                    catch (Exception e) { Console.WriteLine("S16 could not load " + dll + ": " + e.Message); }

            if (modules.Count == 2)
            {
                Type unityObject = modules[0].GetType("UnityEngine.Object", false);
                Type clip = TypeNames.Resolve("AnimationClip", "UnityEngine", modules);
                Check("S16-unity-split",
                    unityObject != null && unityObject.Assembly.GetType("UnityEngine.AnimationClip", false) == null
                    && clip != null && clip.FullName == "UnityEngine.AnimationClip",
                    "AnimationClip is NOT in " + (unityObject == null ? "(no UnityEngine.Object)" :
                        Path.GetFileName(unityObject.Assembly.Location)) + " and resolves anyway, out of " +
                    (clip == null ? "nowhere" : Path.GetFileName(clip.Assembly.Location)));
                Check("S16-unity-full-name",
                    TypeNames.Resolve("UnityEngine.AnimationClip", "UnityEngine", modules) != null,
                    "spelled out in full it resolves too - Type.GetType alone cannot, an unqualified " +
                    "full name only ever reaches mscorlib and the caller");
                Check("S16-unity-unknown",
                    TypeNames.Resolve("NotAThingTheGameHas", "UnityEngine", modules) == null,
                    "and an invented name is still refused BY NAME rather than guessed at");
            }
            else
            {
                Console.WriteLine("S16-unity-split SKIPPED - no " + managed);
            }

            // The same rule with no game on the machine at all: System.Uri is split away from
            // System.Object exactly as AnimationClip is split away from UnityEngine.Object.
            Regex.IsMatch("x", "x");   // make sure System.dll is loaded before we go looking in it
            Assembly[] here = AppDomain.CurrentDomain.GetAssemblies();
            Check("S16-split-analogue",
                typeof(object).Assembly.GetType("System.Uri", false) == null
                && TypeNames.Resolve("Uri", "System", here) == typeof(Uri)
                && TypeNames.Resolve("Nowhere.At.All", "System", here) == null
                && TypeNames.Resolve(null, "System", here) == null,
                "a type outside the anchor's own assembly resolves, an unknown one does not");
        }
        finally { AppDomain.CurrentDomain.AssemblyResolve -= beside; }
    }

    /// <summary>
    /// Gate S15, offline: the cached patched bundle in the player's AppData is rebuilt when anything
    /// it was built FROM has moved.
    ///
    /// The defect it pins: ApplyProject accepted "every declared file exists" as proof the copy was
    /// current, so a game update, a mod update or a change in ContentTool's own bake format kept
    /// serving a stale AppData bundle forever, with no line anywhere saying so.
    ///
    /// It can tell HIT from MISS because every arm changes exactly ONE of the three inputs against
    /// the same baseline and demands a different key. A key that ignored its inputs passes the
    /// repeat arm and fails all four change arms; a Fresh() that answered true always fails the
    /// first arm, where the folder holds copies and no key at all - which is precisely the shape a
    /// pre-S3 ContentTool left behind.
    /// </summary>
    private static void CacheKeyArm()
    {
        string tmp = Dir(Path.GetTempPath(), "ct_s15_" + Guid.NewGuid().ToString("N"));
        try
        {
            string project = Dir(tmp, "MyMod");
            string manifest = Path.Combine(project, "ppcontent.json");
            File.WriteAllText(manifest, "{ \"id\": \"m\", \"bundle\": \"MyMod.bundle\" }");
            string tex = Dir(Dir(project, "Content"), "Textures");
            File.WriteAllBytes(Path.Combine(tex, "rifle.png"), new byte[] { 1, 2, 3 });
            string shipped = Path.Combine(tmp, "px_equipment_assets_all.bundle");
            File.WriteAllBytes(shipped, new byte[128]);
            List<string> sources = new List<string> { shipped };
            string patched = Dir(tmp, "Patched");
            File.WriteAllBytes(Path.Combine(patched, "px_equipment_assets_all.bundle"), new byte[64]);

            string baseline = PatchCache.Key(project, sources);
            Check("S15-stale-without-key", !PatchCache.Fresh(patched, baseline),
                "a patched folder written by the ContentTool that had no key is STALE - the copies " +
                "are all there and that is not evidence of anything");

            PatchCache.Write(patched, baseline);
            Check("S15-hit", PatchCache.Fresh(patched, baseline)
                             && PatchCache.Key(project, sources) == baseline,
                "after a bake stamps it, the same project against the same game reads FRESH -> " + baseline);

            File.WriteAllText(manifest, "{ \"id\": \"m\", \"bundle\": \"MyMod.bundle\", \"scale\": 2 }");
            string afterManifest = PatchCache.Key(project, sources);
            Check("S15-manifest", afterManifest != baseline && !PatchCache.Fresh(patched, afterManifest),
                "an edited ppcontent.json - a MOD UPDATE - misses -> " + afterManifest);

            File.WriteAllBytes(Path.Combine(tex, "scope.png"), new byte[] { 9 });
            string afterSource = PatchCache.Key(project, sources);
            Check("S15-source", afterSource != afterManifest,
                "so does a new file under Content\\ -> " + afterSource);

            DateTime shippedWas = File.GetLastWriteTimeUtc(shipped);
            File.WriteAllBytes(shipped, new byte[256]);
            string afterGame = PatchCache.Key(project, sources);
            Check("S15-shipped", afterGame != afterSource,
                "and so does the SHIPPED bundle the copy was cloned from changing under it - a GAME " +
                "UPDATE, which nothing about the mod's own files could ever reveal -> " + afterGame);

            Check("S15-format",
                PatchCache.Key(project, sources, PatchCache.FormatVersion + 1) != afterGame
                && PatchCache.Key(project, sources, PatchCache.FormatVersion) == afterGame,
                "and bumping ContentTool's own format version misses with nothing on disk moved at " +
                "all, while the current version still hits - version is " + PatchCache.FormatVersion);

            // The control the four arms above need: with every input put back, the key is the
            // baseline again. A key that merely drifted (a timestamp of its own, a counter) would
            // pass all four misses and fail this. The shipped bundle's mtime is restored WITH its
            // bytes, because size-and-mtime is what the key says a game file's identity is - a
            // content hash of a 300 MB bundle on every enable is not a trade this makes.
            File.WriteAllText(manifest, "{ \"id\": \"m\", \"bundle\": \"MyMod.bundle\" }");
            File.Delete(Path.Combine(tex, "scope.png"));
            File.WriteAllBytes(shipped, new byte[128]);
            File.SetLastWriteTimeUtc(shipped, shippedWas);
            Check("S15-restore", PatchCache.Key(project, sources) == baseline
                                 && PatchCache.Fresh(patched, baseline),
                "putting all three back reproduces the baseline key exactly - the misses were the " +
                "inputs and not the clock");
        }
        finally { try { Directory.Delete(tmp, true); } catch (IOException) { } }
    }

    /// <summary>
    /// Gate S21, offline: the patched cache is namespaced BY INSTALL, and what nobody owns is
    /// DELETED instead of stepped over.
    ///
    /// The two defects it pins, both measured against a real folder tree:
    ///   1. the cache key was &lt;modId&gt; alone, so the player's Steam install and his second test
    ///      instance wrote to one folder and thrashed each other's hundreds of megabytes;
    ///   2. an obsolete copy was only ever SKIPPED (Route7's "the project no longer declares it"),
    ///      so removing a content mod left its bundles in AppData forever.
    ///
    /// It can tell a sweep from a massacre because the same run holds four fates: a live mod SURVIVES
    /// with its key intact, a dead one is GONE, the OTHER install's tag is UNTOUCHED, and a locked
    /// entry is left alone - and, the arm that matters most, left STALE rather than half-fresh, so
    /// the worst a delete can cost is a re-bake.
    /// </summary>
    private static void CachePruneArm()
    {
        string tmp = Dir(Path.GetTempPath(), "ct_s19_" + Guid.NewGuid().ToString("N"));
        try
        {
            string steam = @"D:\Steam\steamapps\common\Phoenix Point\PhoenixPointWin64_Data";
            string mine = PatchCache.InstallTag(steam);
            string other = PatchCache.InstallTag(@"D:\PP-Instance2\PhoenixPointWin64_Data");
            Check("S21-tag-splits", mine != other && mine.Length == PatchCache.TagLength,
                "two installs on one machine get two folders -> " + mine + " vs " + other);
            Check("S21-tag-stable",
                PatchCache.InstallTag(steam + "\\") == mine
                && PatchCache.InstallTag(steam.ToUpperInvariant()) == mine
                && PatchCache.InstallTag(steam.Replace('\\', '/')) == mine,
                "while a trailing slash, the case and the slash direction are the same install - a " +
                "tag that drifted would re-bake the whole cache at every launch");

            string root = Dir(tmp, "Patched");
            string live = Stamped(Dir(Dir(root, mine), "com.morgott.Live"), "live");
            string dead = Stamped(Dir(Dir(root, mine), "com.morgott.Dead"), "dead");
            string theirs = Stamped(Dir(Dir(root, other), "com.morgott.Live"), "theirs");
            string legacy = Stamped(Dir(root, "com.morgott.Legacy"), "legacy");
            string locked = Stamped(Dir(Dir(root, mine), "com.morgott.Locked"), "locked");

            string said;
            using (FileStream hold = new FileStream(Path.Combine(locked, "held.bundle"),
                                                    FileMode.Create, FileAccess.Write, FileShare.None))
            {
                hold.WriteByte(7);
                said = PatchCache.Prune(root, mine, new[] { "com.morgott.Live" });
            }

            Check("S21-keeps-live", Directory.Exists(live) && PatchCache.Fresh(live, "live"),
                "the enabled mod's copy survives with its key intact - no re-bake for doing nothing");
            Check("S21-drops-dead", !Directory.Exists(dead),
                "the mod the player removed is DELETED, not skipped -> " + dead);
            Check("S21-other-install", Directory.Exists(theirs) && PatchCache.Fresh(theirs, "theirs"),
                "the OTHER install's tag is not this sweep's business and is untouched -> " + theirs);
            Check("S21-drops-legacy", !Directory.Exists(legacy),
                "and the flat pre-tag layout goes with it, because nothing reads it any more");
            Check("S21-locked-safe",
                Directory.Exists(locked) && !PatchCache.Fresh(locked, "locked")
                && said != null && said.Contains("com.morgott.Locked"),
                "a locked entry is named and left alone, and left STALE - the key goes first, so the " +
                "worst an interrupted delete can cost is a re-bake -> " + said);

            // The control in the same run: with the live mod gone from the roster too, the SAME call
            // takes it. An arm that passed because Prune deletes nothing fails here.
            PatchCache.Prune(root, mine, new string[0]);
            Check("S21-ctl-not-inert", !Directory.Exists(live) && Directory.Exists(theirs),
                "naming no live mod at all empties this install's tag and still leaves the other's");
        }
        finally { try { Directory.Delete(tmp, true); } catch (IOException) { } }
    }

    /// <summary>A cache entry on disk: one patched copy plus the key that says it is current.</summary>
    private static string Stamped(string dir, string key)
    {
        File.WriteAllBytes(Path.Combine(dir, "shipped_assets_all.bundle"), new byte[32]);
        PatchCache.Write(dir, key);
        return dir;
    }

    // ---------------------------------------------------------------- gate M2

    /// <summary>
    /// Gate M2 - the mandate itself, asserted over the SOURCE instead of over one route's behaviour:
    /// nothing ContentTool compiles can open a file inside the Phoenix Point installation for
    /// writing. A behavioural arm can only measure the routes somebody remembered to drive; this one
    /// measures every reachable route at once, including the next one somebody adds.
    ///
    /// HOW. Comments and string literals are stripped first, so a path that only appears in prose
    /// (this file is full of them) can never be mistaken for a call. Then every string-valued member
    /// is read out of every file and TAINTED by fixed point: a member is install-rooted when its own
    /// text mentions Application.streamingAssetsPath or another install-rooted member. Finally every
    /// File./Directory. WRITE call site is resolved - the argument that is actually written, which is
    /// arg 1 for Copy and BOTH args for Move/Replace, because those consume the source too - and the
    /// destination must not mention an install-rooted name.
    ///
    /// ponytail: the census below is a NEGATIVE - "no write resolves to the install" - plus the
    /// classification of every site by its root, printed. A positive "only ModDir and
    /// persistentDataPath" cannot be decided here for the many sites whose destination is a
    /// caller-supplied parameter (a bake target, a scratch file, an author's own project folder);
    /// those show up as CALLER in the census and are the reviewer's list. Make it a whitelist if a
    /// caller-supplied destination ever turns out to be reachable from an install path.
    /// </summary>
    private static void InstallWriteArm()
    {
        string src = SrcRoot();
        if (src == null) { Fail("M2", "could not find the src\\ tree from " + AppContext.BaseDirectory); return; }

        // PER FILE, not over the whole tree: names like 'root', 'path' and 'dir' are local and mean
        // something different in every file, and one global map made every one of them install-rooted
        // through whichever file happened to build a shipped path with that name. What DOES cross a
        // file is a member reached as Type.Member, so those are collected separately and only ever
        // matched in qualified form.
        string[] files = Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories);
        Dictionary<string, HashSet<string>> localTaint = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        Dictionary<string, string> texts = new Dictionary<string, string>(StringComparer.Ordinal);
        HashSet<string> crossFile = new HashSet<string>(StringComparer.Ordinal);
        foreach (string f in files)
        {
            string name = Path.GetFileName(f);
            texts[name] = Strip(File.ReadAllText(f));
            Dictionary<string, string> members = new Dictionary<string, string>(StringComparer.Ordinal);
            CollectStringMembers(texts[name], members);
            HashSet<string> t = Taint(members);
            localTaint[name] = t;
            foreach (string m in t) if (m != "streamingAssetsPath") crossFile.Add(m);
        }

        List<string[]> writes = new List<string[]>();   // { file, line, api, destination expression }
        foreach (string f in files) CollectWrites(Path.GetFileName(f), texts[Path.GetFileName(f)], writes);

        List<string> bad = new List<string>();
        Dictionary<string, int> census = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string[] w in writes)
        {
            string root = RootOf(w[3], localTaint[w[0]], crossFile);
            census[root] = (census.ContainsKey(root) ? census[root] : 0) + 1;
            if (root == "INSTALL") bad.Add(w[0] + ":" + w[1] + " " + w[2] + "(" + w[3].Trim() + ")");
        }

        // The scanner must be able to SEE the thing it is looking for, or "no install write" is a
        // statement about a scanner that read nothing. All three halves are asserted before the verdict.
        HashSet<string> route7 = localTaint.ContainsKey("Route7.cs") ? localTaint["Route7.cs"] : new HashSet<string>();
        Check("M2-scanner", files.Length > 20 && writes.Count > 0 && route7.Contains("Catalog"),
            "scanned " + files.Length + " source file(s), found " + writes.Count +
            " File./Directory. write call site(s), and resolved " + crossFile.Count +
            " install-rooted name(s) - Route7's own are: " + string.Join(", ", Sorted(route7)));

        List<string> parts = new List<string>();
        foreach (string k in Sorted(census)) parts.Add(k + "=" + census[k]);
        Check("M2-no-install-write", bad.Count == 0,
            "every write site in src\\ resolves away from the game installation (" +
            string.Join(", ", parts.ToArray()) + ")" +
            (bad.Count == 0 ? "" : " - INSTALL WRITES: " + string.Join(" | ", bad.ToArray())));

        // The falsification control, in the same run: the SAME resolver, handed the call that used to
        // BE here, must come out INSTALL. Without it the arm above passes on a resolver that
        // classifies everything as CALLER.
        List<string[]> probe = new List<string[]>();
        CollectWrites("probe.cs", Strip("class P { void M() { File.WriteAllText(Catalog, x); } }"), probe);
        string got = probe.Count == 1 ? RootOf(probe[0][3], route7, crossFile) : "(" + probe.Count + " sites)";
        Check("M2-ctl-resolver", got == "INSTALL",
            "the resolver classifies Route7's own File.WriteAllText(Catalog, ...) - the call S1-b " +
            "deleted - as " + got + ", so the arm above is a measurement and not a blind pass");
    }

    /// <summary>The repo's src\ folder, walked up from wherever the test binary landed.</summary>
    /// <summary>
    /// Gate S17, offline: a project whose "replace" holds VIDEO rows alone does not claim patched
    /// copies it never wrote.
    ///
    /// A video needs no patched bundle - Bundles(p) skips those rows, because the clip is a loose file
    /// served live by ct_video - so a video-only project (demos\IntroVideo) writes NO .bundle while
    /// p.Replace.Count is non-zero. Keyed on that count, the bake reported "ct_project: ALL PASS" and
    /// named the patched copies as its whole output, then route vii refused the very install it had
    /// just told the author to run, for holding no .bundle.
    ///
    /// ProjectBake needs UnityEngine and the game's own assemblies, so it cannot be compiled here and
    /// the run itself cannot be reached offline: the arm is over the SOURCE, the same arrangement
    /// S13-wired uses, with the body that shipped as the control in the SAME run - it keyed both the
    /// success line and the "ct_route7 apply" line on the replacement count and must fail the check
    /// the fixed body passes.
    /// </summary>
    private static void VideoOnlyReportArm()
    {
        string src = SrcRoot();
        string file = src == null ? null : Path.Combine(src, "Bake", "ProjectBake.cs");
        string text = file != null && File.Exists(file) ? File.ReadAllText(file) : null;

        Check("S17-video-only", text != null && ClaimsOnlyWhatWasPatched(text),
            "the bake's success line and its 'ct_route7 apply' line are both keyed on a bundle having " +
            "actually been patched, never on the raw replacement count -> " + file);
        Check("S17-video-only-ctl", !ClaimsOnlyWhatWasPatched(ShippedReportBody),
            "while the body that shipped fails the same check, so the arm above is a measurement and " +
            "not a blind pass");
        Check("S17-video-only-says-so",
            text != null && text.Contains("nothing needed patching") &&
            !ShippedReportBody.Contains("nothing needed patching"),
            "and a project that needed no patch gets a line of its own instead of the patched-copy one");
    }

    /// <summary>
    /// Gate S18: C1 cannot report a published AnimationClip as proven without ever looking at one.
    ///
    /// As it shipped (a2ea7c4), the clip block was entered only when `got as AnimationClip` was
    /// non-null. A key that resolved to the WRONG type but carried the expected leaf name passed the
    /// generic C1-pub check, the clip block was SKIPPED, and `ct_catalog verify` reported success
    /// having measured nothing about clip publication - a check that cannot fail, which reads as
    /// evidence. The fix asserts the DECLARED type (Pub.TypeName, the same name KeysLive.Register
    /// admits the publication through) against the object the engine handed back, once, before every
    /// typed block, so a wrong type is a named failure.
    ///
    /// CatalogKeys.Verify needs UnityEngine and Addressables, so it cannot run here: the arm is over
    /// the SOURCE, the arrangement S13-wired and S17 use, with the shipped body as the control in the
    /// same run.
    /// </summary>
    private static void DeclaredTypeArm()
    {
        string src = SrcRoot();
        string file = src == null ? null : Path.Combine(src, "Bake", "CatalogKeys.cs");
        string text = file != null && File.Exists(file) ? Strip(File.ReadAllText(file)) : null;

        Check("S18-declared-type", text != null && DeclaredTypeAsserted(text),
            "C1 resolves the DECLARED type and counts a mismatch as a failure before it reaches the " +
            "typed blocks, so a key resolving to the wrong type cannot skip them silently -> " + file);
        Check("S18-declared-type-ctl", !DeclaredTypeAsserted(Strip(ShippedClipBlock)),
            "while the body that shipped - the clip block entered only on an `as` cast - fails the " +
            "same check, so the arm above is a measurement and not a blind pass");
    }

    /// <summary>Is the declared type resolved, asserted on the resolved object, COUNTED as a failure,
    /// and does all of that happen before the first typed block?</summary>
    private static bool DeclaredTypeAsserted(string text)
    {
        Match guard = Regex.Match(text, @"fail\s*\+=\s*Check\([^;]*\.IsInstanceOfType\(\s*got\s*\)");
        int clip = text.IndexOf("got as AnimationClip", StringComparison.Ordinal);
        return guard.Success
               && Regex.IsMatch(text, @"TypeNames\.Resolve\(\s*p\.TypeName")
               && clip >= 0 && guard.Index < clip;
    }

    /// <summary>The clip block as it shipped - the control S18 is measured against.</summary>
    private const string ShippedClipBlock =
        "AnimationClip anim = got as AnimationClip;\n" +
        "if (anim != null)\n" +
        "    fail += Check(log, \"C1-clip\", !anim.empty && anim.frameRate > 0f && anim.length > 0f,\n" +
        "        \"'\" + p.Key + \"' -> AnimationClip '\" + anim.name + \"'\");\n";

    /// <summary>The report body as it shipped - the control S17 is measured against.</summary>
    private const string ShippedReportBody =
        "if (p.Replace.Count > 0) failures += Patch(p, log);\n" +
        "if (p.Textures.Count == 0 && p.Audio.Count == 0 && p.Models.Count == 0)\n" +
        "    return log.Append(p.Replace.Count == 0\n" +
        "        ? \"nothing to bake - put .png/.jpg under Content\\\\Textures\\\\\"\n" +
        "        : failures == 0\n" +
        "            ? \"ct_project: ALL PASS - this project has no bundle of its own; the patched \" +\n" +
        "              \"copy(ies) above are the whole output\"\n" +
        "            : \"ct_project: \" + failures + \" FAILURE(S)\").ToString();\n" +
        "log.AppendLine(\"copies ready in \" + outDir + \" - install them with: ct_route7 apply \" + name);\n";

    /// <summary>Both claims gated on a bundle actually having been patched, not on a row count.</summary>
    private static bool ClaimsOnlyWhatWasPatched(string text)
    {
        return Regex.IsMatch(text, "patchedBundles\\s*>\\s*0\\s*\\?\\s*\"ct_project: ALL PASS - this " +
                                   "project has no bundle")
               && Regex.IsMatch(text, "if\\s*\\(copies\\.Count\\s*>\\s*0\\)\\s*log\\.AppendLine\\(\"copies ready in ");
    }

    /// <summary>
    /// Gate S20: a Wwise STOP event does not play media, so ct_voices must not name one as
    /// replaceable.
    ///
    /// As it shipped, MediaOfEvent stripped "Start" and "Stop" from the event name with the same
    /// blanket list before it ever looked for media. MainMenuMusicStop therefore resolved to the
    /// sound MainMenuMusic and was reported "- replaceable", pointing an author at media 208540756,
    /// a file that event never plays; and StatXPBangupStop, which DOES declare streamed media of
    /// exactly its own name (300750976 in UI.txt), was stripped to StatXPBangup, matched nothing,
    /// and was reported as embedded in a bank. Both readings are wrong in the same place. The fix
    /// looks the event's OWN name up first - four shipped events need that - falls back to the pair
    /// spelling for Start alone, and reports a Stop that owns no media AS a stop event.
    ///
    /// MediaOfEvent reads Application.streamingAssetsPath, so it cannot be run here: the arm is over
    /// the SOURCE, the arrangement S13-wired, S17 and S18 use, with the shipped body as the control
    /// in the same run. Scanned RAW, not through Strip(): the evidence is in the string literals.
    /// </summary>
    private static void StopEventArm()
    {
        string src = SrcRoot();
        string file = src == null ? null : Path.Combine(src, "Bake", "SoundReplace.cs");
        string text = file != null && File.Exists(file) ? File.ReadAllText(file) : null;

        Check("S20-stop-event", text != null && StopEventTold(text),
            "a Stop event is reported as a stop event with nothing to replace, and the event's own " +
            "name is looked up before any suffix is stripped -> " + file);
        Check("S20-stop-event-ctl", !StopEventTold(ShippedEventStrip),
            "while the body that shipped - one strip list holding both Start and Stop - fails the " +
            "same check, so the arm above is a measurement and not a blind pass");
    }

    /// <summary>
    /// Gate S21: the fit must never be written to the prefab ROOT, because the engine erases it there.
    ///
    /// Addon.AttachVisuals does `VisualRoot.SetParent(attachTransform); VisualRoot.ResetTransform();`
    /// (Addon.cs:1079-1080) and VisualRoot IS the instantiated prefab root (Addon.cs:1039), so a
    /// position, rotation or scale on the root is zeroed the moment the gun reaches the hand.
    /// MEASURED live on D:\PP-Instance2 with the shipped body: ar181 rendered at lossyScale 1.0000
    /// and an effective length of 1.000 against the 0.553 of the donor it had been fitted to, and
    /// sniper 0.920 against 0.819. With the fit one level down: 0.553 and 0.819, exactly the donors.
    ///
    /// Unity cannot run here, so the arm is over the SOURCE - the arrangement S17, S18 and S20 use -
    /// with the body that shipped as the control in the same run.
    /// </summary>
    private static void FitBelowRootArm()
    {
        string src = SrcRoot();
        string file = src == null ? null : Path.Combine(src, "Tactical", "WeaponBuild.cs");
        string text = file != null && File.Exists(file) ? File.ReadAllText(file) : null;

        Check("S21-fit-below-root", text != null && FitStaysBelowRoot(text),
            "the fit is written to a transform BELOW the prefab root, which is the only place the " +
            "engine's attach-time ResetTransform does not erase -> " + file);
        Check("S21-fit-below-root-ctl", !FitStaysBelowRoot(ShippedRootFit),
            "while the body that shipped - localScale and localPosition straight onto " +
            "prefab.transform - fails the same check, so the arm above is a measurement and not a " +
            "blind pass");
    }

    /// <summary>
    /// Gate S22: the fit's TRANSFORM ALGEBRA. The node the fit is written to carries a rotation as
    /// well as a scale and an offset, and a child transform composes as `p + R * (s * v)` - so the
    /// offset that lands the model's centre on the donor's is `dst - s * (R * src)`. The shipped body
    /// computed `dst - s * src`, in unrotated space, and the rotation then swung the model off that
    /// centre: the gun rode above the hand that was supposed to be on its trigger, at the right size.
    ///
    /// Every arm below carries its own falsifying control in the same run: the offset with the
    /// rotation dropped must MISS, the scale must change when the turn is taken out, and a DECLARED
    /// rotation must be measured through its own matrix rather than through the long-axis permutation
    /// the shipped body used for every case.
    /// </summary>
    private static void FitAlgebraArm()
    {
        // The donor's real box (PX_AssaultRifle's own mesh) against a gun modelled 1 m down X and
        // OFF its own origin - the only case where the rotation shows up in the offset at all.
        float[] tc = { 0.00435f, 0.02574f, 0.30869f };
        float[] te = { 0.03774f, 0.11355f, 0.46011f };
        float[] sc = { 0.42f, 0.11f, -0.03f };
        float[] se = { 0.5f, 0.06f, 0.04f };
        float[] yaw = { 0, 0, 1, 0, 1, 0, -1, 0, 0 };     // Unity's yaw 90: (x,y,z) -> (z, y, -x)

        float scale; float[] offset; string why;
        if (!FitBox.Solve(sc, se, tc, te, yaw, out scale, out offset, out why))
        { Fail("S23-solve", "a normal turned fit was refused: " + why); return; }

        float[] landed = Land(offset, scale, yaw, sc);
        Check("S23-centre", Dist(landed, tc) < 1e-5f,
            "the TURNED, scaled centre lands on the donor's own centre -> " + Fmt(landed) + " vs " + Fmt(tc));

        // The control: the offset the shipped body computed, with the rotation left out.
        float[] blind = Land(new[] { tc[0] - scale * sc[0], tc[1] - scale * sc[1], tc[2] - scale * sc[2] },
                             scale, yaw, sc);
        Check("S23-centre-ctl", Dist(blind, tc) > 0.05f,
            "while dropping the rotation from the offset misses that centre by " +
            Dist(blind, tc).ToString("0.000") + " m -> " + Fmt(blind) + " - the gun above the hand");

        // The extents are seen through the turn as well, or the scale is measured against a box the
        // mesh never presents.
        float want = Math.Min(te[0] / se[2], Math.Min(te[1] / se[1], te[2] / se[0]));
        Check("S23-extent", Math.Abs(scale - want) < 1e-6f,
            "the scale is measured against the TURNED box: " + scale.ToString("0.0000") + " vs " +
            want.ToString("0.0000"));
        float straight; float[] unused;
        FitBox.Solve(sc, se, tc, te, null, out straight, out unused, out why);
        Check("S23-extent-ctl", Math.Abs(straight - scale) > 1e-4f,
            "and taking the turn out changes it to " + straight.ToString("0.0000") +
            ", so the arm above is a measurement and not a blind pass");

        // A DECLARED rotation that does NOT move the long axis: pitch -90 about X on the same X-long
        // gun. The shipped permutation was keyed on the LONG AXIS, so it measured this against
        // {z,y,x} whatever the manifest actually asked for.
        float[] pitch = { 1, 0, 0, 0, 0, 1, 0, -1, 0 };   // (x,y,z) -> (x, z, -y)
        float declared;
        FitBox.Solve(sc, se, tc, te, pitch, out declared, out unused, out why);
        float real = Math.Min(te[0] / se[0], Math.Min(te[1] / se[2], te[2] / se[1]));
        Check("S23-declared", Math.Abs(declared - real) < 1e-6f && Math.Abs(real - want) > 1e-4f,
            "a declared rotation is measured through its OWN matrix (" + declared.ToString("0.0000") +
            " = " + real.ToString("0.0000") + "), not through the long-axis permutation, which would " +
            "have answered " + want.ToString("0.0000"));

        Check("S23-identity", FitBox.Solve(sc, se, tc, te, null, out straight, out unused, out why)
                              && Math.Abs((straight * sc[0] + unused[0]) - tc[0]) < 1e-6f,
            "and the un-rotated case is exactly what it always was");
    }

    /// <summary>Where a child transform actually puts a point: offset + R * (scale * v).</summary>
    private static float[] Land(float[] offset, float scale, float[] r, float[] v)
    {
        float[] o = new float[3];
        for (int i = 0; i < 3; i++)
            o[i] = offset[i] + scale * (r[i * 3] * v[0] + r[i * 3 + 1] * v[1] + r[i * 3 + 2] * v[2]);
        return o;
    }

    private static float Dist(float[] a, float[] b)
    {
        float d = 0f;
        for (int i = 0; i < 3; i++) d += (a[i] - b[i]) * (a[i] - b[i]);
        return (float)Math.Sqrt(d);
    }

    private static string Fmt(float[] v)
    {
        return v[0].ToString("0.000") + "," + v[1].ToString("0.000") + "," + v[2].ToString("0.000");
    }

    /// <summary>No transform component of the fit is assigned to `prefab.transform` anywhere.</summary>
    private static bool FitStaysBelowRoot(string text)
    {
        return !Regex.IsMatch(text, @"prefab\.transform\.local(Scale|Position|Rotation)\s*=")
               && text.Contains("FitNode(prefab)");
    }

    /// <summary>
    /// Gate S24: "offset" NUDGES the auto fit, it does not replace it, and the sockets go with it.
    ///
    /// A bounding-box fit aligns CENTRES and a hand grips a GRIP, so the last centimetres are a thing
    /// only an eye can judge - which is what the key is for. Two ways to get it wrong, and both are
    /// silent: assigning it would throw the measured size and turn away, and leaving the socket box
    /// alone would leave the muzzle where the donor's was while the barrel is elsewhere. MEASURED live
    /// on D:\PP-Instance2 against the guns' own clone sources: the AR's trigger hand closed on nothing
    /// until "0,-0.07,0", the sniper's grip sat 6 cm ahead of the hand until "0,0,0.06".
    ///
    /// Unity cannot run here, so the arm is over the SOURCE, with the assigning body as its control.
    /// </summary>
    private static void FitOffsetArm()
    {
        string src = SrcRoot();
        string file = src == null ? null : Path.Combine(src, "Tactical", "WeaponBuild.cs");
        string text = file != null && File.Exists(file) ? File.ReadAllText(file) : null;

        Check("S24-offset-nudges", text != null && OffsetNudges(text),
            "\"offset\" is ADDED to the offset the fit solved and the socket box moves with it, so " +
            "the auto fit keeps the size and the turn and the muzzle stays on the barrel -> " + file);
        Check("S24-offset-nudges-ctl", !OffsetNudges(AssignedOffset),
            "while a body that ASSIGNS the declared offset over the solved one fails the same check, " +
            "so the arm above is a measurement and not a blind pass");
    }

    /// <summary>
    /// Gate S25: what the eye dialled in is what the manifest reloads - byte for byte everywhere else.
    ///
    /// The live fit workbench is only worth anything if SAVE and RELOAD are the same numbers. Two ways
    /// to lose that, both silent: a value printed at one precision and re-read at another, and a splice
    /// that lands in the wrong entry (two content mods may declare the same weapon id, and two entries
    /// in one file certainly can). So the arm dials a fit into a manifest that has NONE of the three
    /// keys yet, re-reads it through the very reader WeaponBuild.Parse uses, and asserts the numbers
    /// came back - then asserts the sibling entry and every other byte are untouched, and that an
    /// ambiguous id is a NAMED refusal rather than a write.
    ///
    /// The file half runs too, against a real file with a BOM and CRLF: preserving those is not
    /// cosmetic - flipping either turns the author's whole ppcontent.json into a diff.
    /// </summary>
    private static void FitSaveArm()
    {
        const string manifest =
            "{\r\n" +
            "  \"publish\": { \"ar181\": \"WPN_AR181\" },\r\n" +
            "  \"weapons\": [\r\n" +
            "    {\r\n" +
            "      \"id\": \"ar181\",\r\n" +
            "      \"clone\": \"PX_AssaultRifle_WeaponDef\",\r\n" +
            "      \"guid\": \"c0ffee01\",\r\n" +
            "      \"model\": \"WPN_AR181\",\r\n" +
            "      \"fit\": \"auto\",\r\n" +
            "      \"flip\": \"true\"\r\n" +
            "    },\r\n" +
            "    {\r\n" +
            "      \"id\": \"sniper\",\r\n" +
            "      \"clone\": \"PX_SniperRifle_WeaponDef\",\r\n" +
            "      \"guid\": \"c0ffee02\",\r\n" +
            "      \"scale\": \"0.5000\",\r\n" +
            "      \"rotate\": \"0,180,0\",\r\n" +
            "      \"offset\": \"0,0,0.06\"\r\n" +
            "    }\r\n" +
            "  ]\r\n" +
            "}\r\n";

        // Three values no default and no accident produces, one negative, all exactly representable
        // at the precision the writer prints: the arm must fail on a rounding change, not tolerate one.
        const float scale = 0.7351f;
        float[] rotate = { 0f, -90f, 12.5f };
        float[] offset = { 0.012f, -0.0725f, 0.06f };

        string why;
        string wrote = WeaponManifest.Splice(manifest, "ar181", scale, rotate, offset, out why);
        Check("S25-splice", wrote != null, "a dialled fit splices into the entry that owns the id: " + why);
        if (wrote == null) return;

        // The round trip, through the reader the game builds from.
        string row = null, other = null;
        foreach (WeaponManifest.Row r in WeaponManifest.Rows(wrote))
            if (WeaponManifest.Field(r.Text, "id") == "ar181") row = r.Text; else other = r.Text;

        float[] gotRot = row == null ? new float[3] : WeaponManifest.Vec(row, "rotate");
        float[] gotOff = row == null ? new float[3] : WeaponManifest.Vec(row, "offset");
        float gotScale = row == null ? 0f : WeaponManifest.Num(row, "scale");
        Check("S25-roundtrip",
            row != null && Math.Abs(gotScale - scale) < 1e-4f &&
            Dist(gotRot, rotate) < 1e-3f && Dist(gotOff, offset) < 1e-4f,
            "and it re-reads as exactly what was dialled: scale " + gotScale.ToString("0.0000") +
            " rotate " + Fmt(gotRot) + " offset " + Fmt(gotOff));

        // The keys the entry already carried are still there, and the OTHER entry is untouched.
        Check("S25-preserves",
            row != null && WeaponManifest.Field(row, "clone") == "PX_AssaultRifle_WeaponDef" &&
            WeaponManifest.Field(row, "flip") == "true" && WeaponManifest.Field(row, "guid") == "c0ffee01" &&
            other != null && WeaponManifest.Field(other, "offset") == "0,0,0.06" &&
            wrote.Contains("\"publish\": { \"ar181\": \"WPN_AR181\" }") &&
            wrote.IndexOf('\n') > 0 && wrote[wrote.IndexOf('\n') - 1] == '\r',
            "every other byte survives - the sibling entry, the unrelated keys, the CRLF");

        // An entry that ALREADY has the three keys is rewritten in place, not given a second copy.
        string twice = WeaponManifest.Splice(wrote, "ar181", scale, rotate, offset, out why);
        Check("S25-idempotent",
            twice != null && twice == wrote,
            "and saving the same numbers again changes nothing at all");

        // The refusals. Both are ownership questions, and a wrong answer writes a fit into a weapon
        // its author never dialled.
        Check("S25-unknown-id",
            WeaponManifest.Splice(manifest, "shotgun", scale, rotate, offset, out why) == null &&
            why != null && why.Contains("0"),
            "an id no entry carries is a named refusal: " + why);
        Check("S25-two-arrays",
            WeaponManifest.Splice(manifest + manifest, "ar181", scale, rotate, offset, out why) == null,
            "and so is a file with two \"weapons\" arrays, where the row found may not be the row the " +
            "game built from: " + why);

        // The file half: a real write, with a BOM, atomically replaced.
        string dir = Path.Combine(Path.GetTempPath(), "ct_s25_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "ppcontent.json");
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                fs.Write(new byte[] { 0xEF, 0xBB, 0xBF }, 0, 3);
                byte[] body = new UTF8Encoding(false).GetBytes(manifest);
                fs.Write(body, 0, body.Length);
            }
            string dest = WeaponManifest.Save(path, "ar181", scale, rotate, offset, out why);
            byte[] raw = File.ReadAllBytes(path);
            string text = new UTF8Encoding(false).GetString(raw, 3, raw.Length - 3);
            float[] backRot = new float[3], backOff = new float[3];
            float backScale = 0f;
            foreach (WeaponManifest.Row r in WeaponManifest.Rows(text))
                if (WeaponManifest.Field(r.Text, "id") == "ar181")
                {
                    backRot = WeaponManifest.Vec(r.Text, "rotate");
                    backOff = WeaponManifest.Vec(r.Text, "offset");
                    backScale = WeaponManifest.Num(r.Text, "scale");
                }
            Check("S25-file",
                dest == path && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF &&
                Math.Abs(backScale - scale) < 1e-4f && Dist(backRot, rotate) < 1e-3f &&
                Dist(backOff, offset) < 1e-4f && !File.Exists(path + ".ct_tmp"),
                "the file on disk keeps its BOM, re-reads as what was dialled and leaves no temp " +
                "behind -> " + (dest ?? ("REFUSED " + why)));

            Check("S25-no-file",
                WeaponManifest.Save(Path.Combine(dir, "nothing.json"), "ar181", scale, rotate, offset,
                                    out why) == null && why != null,
                "and a manifest that is not there is a named refusal, not an exception: " + why);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    /// <summary>Is the declared offset added to the solved one, and carried into the socket box?</summary>
    private static bool OffsetNudges(string text)
    {
        return Regex.IsMatch(text, @"offset\[0\]\s*\+=\s*e\.offset\.x")
               && Regex.IsMatch(text, @"offset\[1\]\s*\+=\s*e\.offset\.y")
               && Regex.IsMatch(text, @"offset\[2\]\s*\+=\s*e\.offset\.z")
               && Regex.IsMatch(text, @"dst\.center\s*\+=\s*e\.offset");
    }

    /// <summary>The wrong way round: the declared offset replacing the solved one, sockets untouched.</summary>
    private const string AssignedOffset =
        "offset[0] = e.offset.x; offset[1] = e.offset.y; offset[2] = e.offset.z;\n" +
        "Sockets(dst, out shoot, out aim, out shell);\n";

    /// <summary>The body that shipped, and that the live game measured as a no-op.</summary>
    private const string ShippedRootFit =
        "prefab.transform.localScale = new Vector3(scale, scale, scale);\n" +
        "prefab.transform.localPosition = new Vector3(offset[0], offset[1], offset[2]);\n";

    /// <summary>Does the resolver look the event's own name up first, keep Stop out of the strip
    /// fallback, and return a stop line instead of a media list for a Stop that owns no media?</summary>
    private static bool StopEventTold(string text)
    {
        int at = text.IndexOf("MediaOfEvent", StringComparison.Ordinal);
        if (at < 0) return false;
        string m = text.Substring(at);
        if (Regex.IsMatch(m, "\"Start\"\\s*,\\s*\"Stop\"")) return false;          // no blanket strip list
        if (!Regex.IsMatch(m, "string sound = evName;[\\s\\S]{0,200}?StreamedFor\\(sound\\)")) return false;
        return Regex.IsMatch(m, "EndsWith\\(\\s*\"Stop\"[\\s\\S]{0,300}?return\\b")   // it returns there
               && Regex.IsMatch(m, "EndsWith\\(\\s*\"Stop\"[\\s\\S]{0,600}?STOP event");
    }

    /// <summary>The name mapping as it shipped - the control S20 is measured against.</summary>
    private const string ShippedEventStrip =
        "internal static string MediaOfEvent(uint eventId)\n" +
        "{\n" +
        "    string sound = evName;\n" +
        "    foreach (string suffix in new string[] { \"Start\", \"Stop\" })\n" +
        "        if (sound.EndsWith(suffix, StringComparison.Ordinal) && sound.Length > suffix.Length)\n" +
        "        { sound = sound.Substring(0, sound.Length - suffix.Length); break; }\n" +
        "    return \"'\" + evName + \"' -> \" + hits + \" - replaceable\";\n" +
        "}\n";

    private static string SrcRoot()
    {
        DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null)
        {
            string s = Path.Combine(d.FullName, "src");
            if (File.Exists(Path.Combine(s, "Bake", "BundleLive.cs"))) return s;
            d = d.Parent;
        }
        return null;
    }

    /// <summary>Comments and string literals out, so only CODE is scanned. Quotes are kept as an
    /// empty literal, which keeps every argument list balanced.</summary>
    private static string Strip(string s)
    {
        StringBuilder b = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '/')
            { while (i < s.Length && s[i] != '\n') i++; b.Append('\n'); continue; }
            if (s[i] == '/' && i + 1 < s.Length && s[i + 1] == '*')
            {
                int e = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                int stop = e < 0 ? s.Length : e + 2;
                for (int k = i; k < stop; k++) if (s[k] == '\n') b.Append('\n');
                i = stop - 1; continue;
            }
            if (s[i] == '@' && i + 1 < s.Length && s[i + 1] == '"')
            {
                i += 2;
                while (i < s.Length && !(s[i] == '"' && (i + 1 >= s.Length || s[i + 1] != '"')))
                { if (s[i] == '\n') b.Append('\n'); i += s[i] == '"' ? 2 : 1; }
                b.Append("\"\""); continue;
            }
            if (s[i] == '"')
            {
                i++;
                while (i < s.Length && s[i] != '"') i += s[i] == '\\' ? 2 : 1;
                b.Append("\"\""); continue;
            }
            if (s[i] == '\'')
            {
                int k = i + 1;
                while (k < s.Length && s[k] != '\'') k += s[k] == '\\' ? 2 : 1;
                b.Append("' '"); i = k; continue;
            }
            b.Append(s[i]);
        }
        return b.ToString();
    }

    /// <summary>Every string-valued member and local, with the text that defines it. Bodies are taken
    /// by brace matching so a whole method's returns are part of its own definition.</summary>
    private static void CollectStringMembers(string text, Dictionary<string, string> into)
    {
        foreach (Match m in Regex.Matches(text, @"\bstring\s+(\w+)\s*(\(|\{|=>|=)"))
        {
            string name = m.Groups[1].Value, kind = m.Groups[2].Value, body;
            int at = m.Groups[2].Index;
            if (kind == "=>" || kind == "=")
            {
                int end = text.IndexOf(';', at);
                if (end < 0) continue;
                body = text.Substring(at, end - at);
            }
            else
            {
                int open = kind == "{" ? at : text.IndexOf('{', at);
                if (open < 0) continue;
                int depth = 0, k = open;
                for (; k < text.Length; k++)
                {
                    if (text[k] == '{') depth++;
                    else if (text[k] == '}' && --depth == 0) break;
                }
                body = text.Substring(open, Math.Min(k, text.Length - 1) - open + 1);
            }
            into[name] = into.ContainsKey(name) ? into[name] + "\n" + body : body;
        }
    }

    /// <summary>Fixed point from Application.streamingAssetsPath outwards.</summary>
    private static HashSet<string> Taint(Dictionary<string, string> members)
    {
        HashSet<string> t = new HashSet<string>(StringComparer.Ordinal) { "streamingAssetsPath" };
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach (KeyValuePair<string, string> m in members)
            {
                if (t.Contains(m.Key)) continue;
                foreach (string w in t)
                    if (Regex.IsMatch(m.Value, @"\b" + Regex.Escape(w) + @"\b")) { t.Add(m.Key); grew = true; break; }
                if (grew) break;
            }
        }
        return t;
    }

    /// <summary>The write API surface, and WHICH argument each one actually writes.</summary>
    private static readonly Dictionary<string, int[]> WriteApis = new Dictionary<string, int[]>(StringComparer.Ordinal)
    {
        { "File.WriteAllBytes", new[] { 0 } }, { "File.WriteAllText", new[] { 0 } },
        { "File.WriteAllLines", new[] { 0 } }, { "File.AppendAllText", new[] { 0 } },
        { "File.AppendAllLines", new[] { 0 } }, { "File.Create", new[] { 0 } },
        { "File.CreateText", new[] { 0 } }, { "File.OpenWrite", new[] { 0 } },
        { "File.Delete", new[] { 0 } }, { "File.SetLastWriteTime", new[] { 0 } },
        { "File.Copy", new[] { 1 } },                       // arg 0 is READ
        { "File.Move", new[] { 0, 1 } },                    // the source is consumed too
        { "File.Replace", new[] { 0, 1 } },                 // ditto
        { "Directory.CreateDirectory", new[] { 0 } }, { "Directory.Delete", new[] { 0 } },
        { "Directory.Move", new[] { 0, 1 } },
    };

    private static void CollectWrites(string file, string text, List<string[]> into)
    {
        foreach (KeyValuePair<string, int[]> api in WriteApis)
            foreach (Match m in Regex.Matches(text, @"\b" + Regex.Escape(api.Key) + @"\s*\("))
            {
                List<string> args = Args(text, text.IndexOf('(', m.Index));
                foreach (int i in api.Value)
                    if (i < args.Count)
                        into.Add(new[] { file, (1 + Count(text, '\n', m.Index)).ToString(), api.Key, args[i] });
            }
    }

    /// <summary>Top-level comma-separated arguments of the call whose '(' is at <paramref name="open"/>.</summary>
    private static List<string> Args(string text, int open)
    {
        List<string> args = new List<string>();
        int depth = 0, start = open + 1;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '(' || c == '[') depth++;
            else if (c == ')' || c == ']')
            {
                if (--depth == 0) { args.Add(text.Substring(start, i - start)); break; }
            }
            else if (c == ',' && depth == 1) { args.Add(text.Substring(start, i - start)); start = i + 1; }
        }
        return args;
    }

    /// <summary>
    /// Where one destination expression is rooted, as far as this can be decided statically. A BARE
    /// name is only install-rooted when THIS file says so; a name from another file has to appear
    /// qualified (Type.Member), which is the only way a static of another class can be reached.
    /// </summary>
    private static string RootOf(string expr, HashSet<string> local, HashSet<string> crossFile)
    {
        foreach (Match m in Regex.Matches(expr, @"\b\w+\s*\.\s*(\w+)"))
            if (crossFile.Contains(m.Groups[1].Value)) return "INSTALL";
        foreach (Match m in Regex.Matches(expr, @"\w+"))
            if (local.Contains(m.Value)) return "INSTALL";
        if (Regex.IsMatch(expr, @"\bpersistentDataPath\b")) return "AppData";
        if (Regex.IsMatch(expr, @"\bModDir\b")) return "ModDir";
        if (Regex.IsMatch(expr, @"\bGetTempPath\b")) return "Temp";
        return "CALLER";
    }

    private static int Count(string s, char c, int upTo)
    {
        int n = 0;
        for (int i = 0; i < upTo && i < s.Length; i++) if (s[i] == c) n++;
        return n;
    }

    private static List<string> Sorted(IEnumerable<string> names)
    {
        List<string> l = new List<string>(names);
        l.Sort(StringComparer.Ordinal);
        return l;
    }

    private static List<string> Sorted(Dictionary<string, int> d) { return Sorted(d.Keys); }

    /// <summary>
    /// Gate S2 (M1 parity, the developer live loop), offline: the debounce actually debounces, the
    /// dirty hand-off coalesces, a variant set resolves and cycles - and, the arm the whole slice
    /// hangs on, developer mode OFF schedules NOTHING. Every clock reading is a parameter, so the
    /// arms are deterministic instead of racing a real timer.
    /// </summary>
    private static void DevLoopArm()
    {
        string root = Dir(Path.GetTempPath(), "ct_s2_" + Guid.NewGuid().ToString("N"));
        try
        {
            DateTime t0 = new DateTime(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);
            string loose = Path.Combine(root, "hull.png");
            File.WriteAllText(loose, "png");
            string sets = Dir(root, DevLoop.SetsFolder);
            string night = Dir(sets, "Night"), day = Dir(sets, "Day");
            File.WriteAllText(Path.Combine(night, "hull.png"), "png");
            File.WriteAllText(Path.Combine(day, "other.png"), "png");

            // ---- OFF COSTS NOTHING. A player's session never leaves this state.
            DevLoop.Off();
            DevLoop.Mark(loose, t0);
            Check("S2-off-schedules-nothing",
                !DevLoop.Enabled && DevLoop.WatcherCount == 0 &&
                DevLoop.Scheduled == "watchers=0 loop=off hotkey=off" &&
                DevLoop.DirtyCount == 0 &&
                DevLoop.Pump(t0.AddMinutes(5)) == null && !DevLoop.DueScan(t0.AddMinutes(5)) &&
                DevLoop.Resolve(loose) == loose,
                DevLoop.Scheduled + " queued=" + DevLoop.DirtyCount + "; a watcher event is not even " +
                "RECORDED (asserting only that Pump returns null would pass vacuously - Pump gates on " +
                "Enabled too), no scan is ever due, and a path resolves to itself");

            // ---- a folder that is not there is a NAMED refusal that starts nothing.
            string refused = DevLoop.On(Path.Combine(root, "nope"), null);
            Check("S2-on-refuses",
                refused.StartsWith("ct_dev REFUSED", StringComparison.Ordinal) &&
                !DevLoop.Enabled && DevLoop.WatcherCount == 0,
                refused);

            string on = DevLoop.On(root, null);
            Check("S2-on", DevLoop.Enabled && DevLoop.WatcherCount == 1, on);

            // ---- N rapid writes to ONE file are ONE reload, and only after the writes stop.
            DevLoop.Mark(loose, t0);
            DevLoop.Mark(loose, t0.AddMilliseconds(100));
            DevLoop.Mark(loose, t0.AddMilliseconds(200));
            bool quiet = DevLoop.Pump(t0.AddMilliseconds(400)) == null;
            List<string> drained = DevLoop.Pump(t0.AddMilliseconds(200 + 600));
            bool emptied = DevLoop.Pump(t0.AddMinutes(1)) == null;
            Check("S2-debounce",
                quiet && drained != null && drained.Count == 1 && drained[0] == loose && emptied,
                "3 writes -> " + (drained == null ? "nothing" : drained.Count + " reload(s)") +
                "; inside the quiet period the queue stays shut (" + quiet + "), and a drained queue " +
                "does not fire twice (" + emptied + ")");

            // ---- a burst across files is one pass, not one pass per file.
            DateTime t1 = t0.AddMinutes(2);
            DevLoop.Mark(loose, t1);
            DevLoop.Mark(Path.Combine(day, "other.png"), t1);
            DevLoop.Mark(loose, t1.AddMilliseconds(50));
            List<string> both = DevLoop.Pump(t1.AddSeconds(1));
            Check("S2-coalesce", both != null && both.Count == 2,
                "3 events over 2 files -> " + (both == null ? "nothing" : both.Count + " path(s)"));

            // ---- the periodic pass is periodic, not per-frame.
            DateTime t2 = t1.AddMinutes(1);
            DevLoop.ForceScan();
            bool first = DevLoop.DueScan(t2);
            bool again = DevLoop.DueScan(t2.AddSeconds(1));
            bool later = DevLoop.DueScan(t2.AddSeconds(DevLoop.ScanSeconds + 0.1));
            Check("S2-scan-interval", first && !again && later,
                "due=" + first + " one second later=" + again + " after " + DevLoop.ScanSeconds + "s=" + later);

            // ---- variant sets: Default first, then sorted, and the cycle wraps.
            List<string> names = DevLoop.Sets();
            Check("S2-sets",
                names.Count == 3 && names[0] == DevLoop.DefaultSet && names[1] == "Day" && names[2] == "Night",
                string.Join(", ", names.ToArray()));
            Check("S2-set-default", DevLoop.Resolve(loose) == loose,
                "with no set active the authored file is read");
            string a = DevLoop.Next();
            Check("S2-set-partial", a == "Day" && DevLoop.Resolve(loose) == loose,
                "'Day' carries no hull.png, so that binding keeps the authored file - a set re-skins " +
                "what it has and nothing else");
            string b = DevLoop.Next();
            Check("S2-set-resolve",
                b == "Night" && DevLoop.Resolve(loose) == Path.Combine(night, "hull.png"),
                "'Night' -> " + DevLoop.Resolve(loose));
            string c = DevLoop.Next();
            Check("S2-set-wrap", c == DevLoop.DefaultSet && DevLoop.Resolve(loose) == loose,
                "the cycle comes back to " + c);

            string why;
            bool picked = DevLoop.Select("night", out why) && DevLoop.ActiveSet == "Night";
            bool unknown = !DevLoop.Select("Dusk", out why) && why != null &&
                           why.IndexOf("Dusk", StringComparison.Ordinal) >= 0 && DevLoop.ActiveSet == "Night";
            Check("S2-set-select", picked && unknown,
                "a name is matched case-insensitively and an unknown one is refused BY NAME: " + why);

            // ---- and off again: every watcher disposed, nothing remembered.
            string off = DevLoop.Off();
            Check("S2-off-disposes",
                !DevLoop.Enabled && DevLoop.WatcherCount == 0 && DevLoop.Root == null &&
                DevLoop.ActiveSet == DevLoop.DefaultSet && DevLoop.Resolve(loose) == loose &&
                DevLoop.Scheduled == "watchers=0 loop=off hotkey=off",
                off);
        }
        finally
        {
            DevLoop.Off();
            try { Directory.Delete(root, true); } catch (Exception) { }
        }
    }

    private static string Dir(string parent, string name)
    {
        string d = Path.Combine(parent, name);
        Directory.CreateDirectory(d);
        return d;
    }

    private static string Join(List<ReplacementRule> rules)
    {
        List<string> s = new List<string>();
        foreach (ReplacementRule r in rules) s.Add(r.Target);
        return string.Join(", ", s.ToArray());
    }

    private static void Check(string gate, bool ok, string detail)
    {
        if (ok) Pass(gate, detail); else Fail(gate, detail);
    }

    private static void Pass(string gate, string detail) { Console.WriteLine(gate + " PASS " + detail); }

    /// <summary>
    /// The weapon manifest's <c>"tint"</c>, offline. A colour is the one manifest value where a
    /// LENIENT parse is the dangerous one: "#00FF0" read as some green is a bolt that is almost the
    /// right colour, which nobody would ever report as a bug. So this arm measures both halves -
    /// the exact channels for a well-formed colour, and a NAMED refusal for every near-miss.
    ///
    /// The Unity half - that the tint lands on a PRIVATE ProjectileDef and a PRIVATE prefab copy
    /// rather than the shared one every shipped laser uses - cannot run here (it needs a live
    /// DefRepository and Object.Instantiate). Its instrument is the in-game one:
    /// WeaponBuild.Vfx prints "projectile=... (own copy)" vs "(shared)" per weapon.
    /// </summary>
    private static void WeaponTintArm()
    {
        float[] rgb;
        string why;
        if (!HexColor.TryParse("#3FA9FF", out rgb, out why)) Fail("W1-parse", "#3FA9FF refused: " + why);
        else if (Math.Abs(rgb[0] - 63f / 255f) > 1e-6f || Math.Abs(rgb[1] - 169f / 255f) > 1e-6f ||
                 Math.Abs(rgb[2] - 1f) > 1e-6f)
            Fail("W1-value", "#3FA9FF -> " + rgb[0] + "," + rgb[1] + "," + rgb[2] + ", expected 63,169,255 / 255");
        // The '#' is optional, and case does not matter.
        if (!HexColor.TryParse("4cff5a", out rgb, out why)) Fail("W1-bare", "4cff5a refused: " + why);
        else if (Math.Abs(rgb[1] - 1f) > 1e-6f) Fail("W1-bare", "4cff5a green channel " + rgb[1] + ", expected 1");
        foreach (string bad in new[] { "#3FA9F", "#3FA9FFF", "#3FA9FG", "#3FA9FF80", "", "red", null })
            if (HexColor.TryParse(bad, out rgb, out why))
                Fail("W1-lenient", "'" + (bad ?? "<null>") + "' was accepted as a colour");
    }

    /// <summary>
    /// Gate S26, offline: the weapon fit workbench's own decisions - which weapon in the list a live
    /// fit can be dialled on, what a typed filter leaves standing, and how far one axis press moves
    /// the gun.
    ///
    /// THE CLASSIFICATION IS THE POINT. A shipped weapon has no ppcontent.json row, so a save on one
    /// goes nowhere; the workbench lists it anyway - standing a downloaded gun beside the shipped one
    /// it clones is the whole reason to look - and greys the axis buttons out. Get that backwards and
    /// the only symptom in game is a save that refuses AFTER an author has dialled a gun in by eye.
    ///
    /// The Unity half - the squad bay, the camera hint, the canvases hidden and restored - cannot run
    /// here and has no offline instrument. It is checked by opening it.
    /// </summary>
    /// <summary>The hotkey FitBench.cs actually declares, read out of the source. FitBench is full of
    /// UnityEngine types and cannot be compiled into this gate, so the source line IS the instrument -
    /// and it is the right one: the regression was someone choosing a KeyCode, not calling a
    /// function. Null when the line cannot be found, which fails the check rather than passing it.</summary>
    private static string FitBenchHotkey()
    {
        string root = SrcRoot();
        if (root == null) return null;
        string src = Path.Combine(root, @"Dev\FitBench.cs");
        if (!File.Exists(src)) return null;
        Match m = Regex.Match(File.ReadAllText(src),
                              @"const\s+KeyCode\s+Hotkey\s*=\s*KeyCode\.(\w+)\s*;");
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>
    /// Gate S34, offline: WHICH ARROW A CLICK LANDS ON.
    ///
    /// The workbench now draws editor-style translation handles on the weapon. Every part of that
    /// which can be wrong SILENTLY is here, because in game a bad hit test looks like "the gizmo
    /// ignored me" or - far worse - "I grabbed X and Y moved", and neither says a word in the log.
    ///
    /// Two refusals carry most of the weight. WorldToScreenPoint returns a screen position for a
    /// point BEHIND the camera, mirrored through the centre, so an arrow whose tip has gone behind
    /// the near plane would be pickable in a place it is not drawn. And an arrow pointing nearly at
    /// the camera projects to a few pixels: unaimable, and its drag divides by almost nothing.
    /// </summary>
    private static void GizmoPickArm()
    {
        // ---- the distance is to a SEGMENT, not to the infinite line ----
        Check("S34-seg", Math.Abs(BenchList.SegmentDistance(150f, 103f, 100f, 100f, 200f, 100f) - 3f) < 1e-4f,
              "3 px off the middle of the shaft");
        Check("S34-segpast",
              Math.Abs(BenchList.SegmentDistance(300f, 100f, 100f, 100f, 200f, 100f) - 100f) < 1e-4f,
              "100 px PAST the tip is 100 px away, not 0 - the arrow is finite");

        // ---- behind the camera, and too short ----
        Check("S34-behindpivot",
              !BenchList.AxisVisible(-1f, 5f, 0.3f, 100f, 100f, 300f, 100f, 12f),
              "a pivot behind the near plane is never visible or pickable");
        Check("S34-behindtip",
              !BenchList.AxisVisible(5f, -1f, 0.3f, 100f, 100f, 300f, 100f, 12f),
              "a tip behind the near plane likewise (its projection is MIRRORED)");
        Check("S34-tooshort",
              !BenchList.AxisVisible(5f, 5f, 0.3f, 100f, 100f, 105f, 100f, 12f),
              "5 px of arrow is an axis pointing at the camera: refused, not approximated");
        Check("S34-visible",
              BenchList.AxisVisible(5f, 5f, 0.3f, 100f, 100f, 130f, 100f, 12f),
              "30 px of arrow is aimable");

        // ---- the pick itself, against three arrows sharing one pivot ----
        float[] tipX = { 200f, 100f, 105f }, tipY = { 100f, 200f, 105f };
        bool[] valid = { true, true, false };            // Z is the near-parallel one, dimmed
        Check("S34-pickx",
              BenchList.NearestAxis(100f, 100f, tipX, tipY, valid, 150f, 103f, 10f) == 0, "X");
        Check("S34-picky",
              BenchList.NearestAxis(100f, 100f, tipX, tipY, valid, 103f, 150f, 10f) == 1, "Y");
        // The dimmed axis lies right under the pivot, so without the validity gate it would win every
        // click near the origin of the gizmo - the exact bug this refusal prevents.
        Check("S34-pickdim",
              BenchList.NearestAxis(100f, 100f, tipX, tipY, valid, 103f, 103f, 10f) != 2,
              "a refused axis is never picked, even when it is nearest");
        Check("S34-pickmiss",
              BenchList.NearestAxis(100f, 100f, tipX, tipY, valid, 400f, 400f, 10f) == -1,
              "empty space picks nothing, so the orbit gets the press");

        // ---- constant screen size ----
        // 90 px at 10 m of depth, 60 deg vertical FOV, 800 px tall: 90 * 2*10*tan(30) / 800.
        float want = (float)(90.0 * 2.0 * 10.0 * Math.Tan(30.0 * Math.PI / 180.0) / 800.0);
        float got = BenchList.WorldSize(90f, 10f, 60f, 800f);
        Check("S34-size", Math.Abs(got - want) < 1e-4f, got + " (want " + want + ")");
        // Twice as far away, twice as big in metres - which is what keeps it 90 px on screen.
        Check("S34-sizescales",
              Math.Abs(BenchList.WorldSize(90f, 20f, 60f, 800f) - 2f * got) < 1e-4f,
              "doubling the depth doubles the world size");
    }

    /// <summary>
    /// Gate S35, offline: HOW FAR THE DRAG MOVED THE GUN, and in WHICH SPACE.
    ///
    /// Two separate ways to be wrong, both invisible in game until a save is reloaded.
    ///
    /// The constraint plane is the standard editor technique and it has two degenerate cases that must
    /// REFUSE rather than answer: an axis pointing at the camera (no plane normal exists) and a ray
    /// sliding along the plane (the intersection runs off to infinity). Either one answers a one-pixel
    /// drag with a jump of tens of metres.
    ///
    /// And the SPACE. The manifest's "offset" is in the mesh child's PARENT-LOCAL frame, so a world
    /// displacement has to be divided by the parent's scale on the way in. Unity's TransformDirection
    /// normalises that scale away - use it and every drag on a hand scaled by 2 saves half the number
    /// the eye just approved. That is the arm below with a scaled parent, and it is the one that would
    /// have caught it.
    /// </summary>
    private static void GizmoDragArm()
    {
        float[] pivot = { 0f, 0f, 0f };
        float[] view = { 0f, 0f, 1f };
        float[] eye = { 0f, 0f, -10f };
        float[] straight = { 0f, 0f, 1f };

        // Camera 10 m back looking down +Z, dragging the X arrow. The second ray leans 2 m to the
        // right at the plane z = 0, so the gun must move exactly 2 m along +X.
        double len = Math.Sqrt(2.0 * 2.0 + 10.0 * 10.0);
        float[] leaning = { (float)(2.0 / len), 0f, (float)(10.0 / len) };
        float along;
        bool ok = BenchList.PlaneDelta(pivot, new[] { 1f, 0f, 0f }, view, eye, straight, eye, leaning,
                                       BenchList.MinPlaneDenom, out along);
        Check("S35-delta", ok && Math.Abs(along - 2f) < 1e-3f, ok ? along.ToString() : "REFUSED");

        // Nothing moved: the same ray twice is zero, not noise.
        ok = BenchList.PlaneDelta(pivot, new[] { 1f, 0f, 0f }, view, eye, straight, eye, straight,
                                  BenchList.MinPlaneDenom, out along);
        Check("S35-still", ok && Math.Abs(along) < 1e-5f, ok ? along.ToString() : "REFUSED");

        // Only the ALONG-AXIS part counts: a drag straight up the screen moves the X arrow by nothing.
        double up = Math.Sqrt(2.0 * 2.0 + 10.0 * 10.0);
        float[] upward = { 0f, (float)(2.0 / up), (float)(10.0 / up) };
        ok = BenchList.PlaneDelta(pivot, new[] { 1f, 0f, 0f }, view, eye, straight, eye, upward,
                                  BenchList.MinPlaneDenom, out along);
        Check("S35-onlyalong", ok && Math.Abs(along) < 1e-4f,
              ok ? "sideways drag along X = " + along : "REFUSED");

        // REFUSAL 1: the axis points straight at the camera, so there is no plane containing it that
        // faces the viewer at all.
        Check("S35-parallel",
              !BenchList.PlaneDelta(pivot, new[] { 0f, 0f, 1f }, view, eye, straight, eye, leaning,
                                    BenchList.MinPlaneDenom, out along),
              "an axis aimed at the camera is REFUSED, not approximated");
        // REFUSAL 2: the ray runs along the plane rather than across it.
        Check("S35-grazing",
              !BenchList.PlaneDelta(pivot, new[] { 1f, 0f, 0f }, view, eye, new[] { 1f, 0f, 0f },
                                    eye, leaning, BenchList.MinPlaneDenom, out along),
              "a ray grazing the drag plane is REFUSED");

        // ---- world -> parent-local, WITH the parent's scale ----
        // A parent scaled 2x on its own x: columns of localToWorld are (2,0,0),(0,1,0),(0,0,1).
        float[] scaled = { 2f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f };
        float[] local;
        ok = BenchList.LocalFromWorld(scaled, new[] { 2f, 0f, 0f }, out local);
        Check("S35-scaledparent",
              ok && Math.Abs(local[0] - 1f) < 1e-5f && Math.Abs(local[1]) < 1e-5f &&
              Math.Abs(local[2]) < 1e-5f,
              ok ? "2 m of world along a 2x parent = " + local[0] + " local (TransformDirection would " +
                   "have said 2)" : "REFUSED");

        // ... and with a rotation in the basis too: local x maps to world -2z, local y to +2y,
        // local z to +2x. 4 m along world -z must read as 2 units of local x.
        float[] turned = { 0f, 0f, -2f, 0f, 2f, 0f, 2f, 0f, 0f };
        ok = BenchList.LocalFromWorld(turned, new[] { 0f, 0f, -4f }, out local);
        Check("S35-turnedparent",
              ok && Math.Abs(local[0] - 2f) < 1e-5f && Math.Abs(local[1]) < 1e-5f &&
              Math.Abs(local[2]) < 1e-5f,
              ok ? local[0] + "," + local[1] + "," + local[2] : "REFUSED");

        // A parent flattened on one axis has no local answer at all. Inventing one sends the gun to
        // infinity and the manifest with it.
        Check("S35-degenerate",
              !BenchList.LocalFromWorld(new[] { 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f },
                                        new[] { 1f, 0f, 0f }, out local),
              "a collapsed parent basis is REFUSED");
        // But a legitimately TINY parent (a 1/100-scale hand) is not degenerate and must still work -
        // which is why the test is relative to the columns' own size, not an absolute epsilon.
        ok = BenchList.LocalFromWorld(new[] { 0.01f, 0f, 0f, 0f, 0.01f, 0f, 0f, 0f, 0.01f },
                                      new[] { 0.02f, 0f, 0f }, out local);
        Check("S35-tinyparent", ok && Math.Abs(local[0] - 2f) < 1e-4f,
              ok ? local[0].ToString() : "REFUSED a legitimately small parent");
    }

    /// <summary>
    /// Gate S36, offline: A SAVE THAT SURVIVES THE NEXT DEPLOY.
    ///
    /// SAVE writes the manifest the GAME loaded - the deployed copy - while the author's truth is his
    /// repo, and deploy.ps1 copies repo over deployed. So the bench mirrors the saved bytes back to
    /// the source folder deploy.ps1 recorded in its marker. This runs against REAL folders because
    /// every failure mode here is a filesystem fact: a mod that was not deployed by our script, a
    /// source folder that has since moved, a mod deployed onto its own source.
    ///
    /// The rule under all of them: a mirror that cannot happen SAYS SO. Losing an afternoon of
    /// dialling to a silent no-op is the whole thing this exists to prevent.
    /// </summary>
    private static void MirrorSaveArm()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "ct-mirror-" + Guid.NewGuid().ToString("N"));
        try
        {
            string deployed = Path.Combine(tmp, "Mods", "WeaponAdd");
            string source = Path.Combine(tmp, "repo", "demos", "WeaponAdd");
            Directory.CreateDirectory(deployed);
            Directory.CreateDirectory(source);
            string live = Path.Combine(deployed, "ppcontent.json");
            File.WriteAllText(live, "{ \"weapons\": [ { \"id\": \"tuned\" } ] }");
            File.WriteAllText(Path.Combine(source, "ppcontent.json"), "{ \"weapons\": [] }");

            // 1. NO MARKER: an honest "there is nowhere to put it", never a silent success.
            string why;
            Check("S36-nomarker", BenchList.MirrorTarget(live, out why) == null &&
                                  why != null && why.IndexOf("DEPLOYED COPY ONLY", StringComparison.Ordinal) >= 0,
                  why ?? "returned a target with no marker");

            // 2. THE ROUND TRIP: deploy.ps1's marker written, read back, target resolved, bytes copied.
            File.WriteAllText(Path.Combine(deployed, BenchList.SourceMarker), source + Environment.NewLine);
            string target = BenchList.MirrorTarget(live, out why);
            Check("S36-target",
                  target != null &&
                  string.Equals(target, Path.GetFullPath(Path.Combine(source, "ppcontent.json")),
                                StringComparison.OrdinalIgnoreCase),
                  target ?? ("refused: " + why));
            string said = BenchList.MirrorSave(live);
            Check("S36-copied",
                  File.ReadAllText(Path.Combine(source, "ppcontent.json")) == File.ReadAllText(live) &&
                  said.IndexOf("mirrored back", StringComparison.Ordinal) >= 0,
                  said);

            // 3. THE SOURCE HAS MOVED. The deployed save already succeeded, so this is a warning about
            //    the mirror and must say the save itself is safe.
            Directory.Delete(source, true);
            Check("S36-sourcegone", BenchList.MirrorTarget(live, out why) == null &&
                                    why != null && why.IndexOf("GONE", StringComparison.Ordinal) >= 0,
                  why ?? "resolved a target into a folder that does not exist");

            // 4. DEPLOYED ONTO ITSELF: the two paths are one file and there is nothing to do.
            File.WriteAllText(Path.Combine(deployed, BenchList.SourceMarker), deployed);
            Check("S36-selfsame", BenchList.MirrorTarget(live, out why) == null &&
                                  why != null && why.IndexOf("IS the source", StringComparison.Ordinal) >= 0,
                  why ?? "tried to copy a file onto itself");

            // 5. An EMPTY marker is not a path.
            File.WriteAllText(Path.Combine(deployed, BenchList.SourceMarker), "   \r\n");
            Check("S36-emptymarker", BenchList.MirrorTarget(live, out why) == null && why != null,
                  why ?? "accepted an empty marker");

            // 6. The key carries the manifest path, and that is what the bench hands in.
            string key = "Vulture_WeaponDef" + BenchList.Separator + live;
            Check("S36-key", BenchList.Manifest(key) == live && BenchList.Manifest("loose") == null,
                  BenchList.Manifest(key) ?? "null");
        }
        finally
        {
            try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch (Exception) { }
        }
    }

    /// <summary>
    /// Gate S37: THE WORKBENCH CAN ALWAYS BE LEFT.
    ///
    /// FitBench.cs is full of UnityEngine types and cannot be compiled into this gate, so - exactly as
    /// with the hotkey above - the SOURCE is the instrument, and it is the right one: every defect
    /// here was a missing line, not a wrong computation. Five holes an independent review found, each
    /// asserted by the shape that closes it.
    ///
    /// They share one failure mode. Each leaves the GAME altered - canvases hidden, a CinemachineBrain
    /// disabled, an addons manager quiesced - with NO PANEL on screen and no key that reaches the exit
    /// path any more. There is no in-game instrument for that: by the time it is visible, the thing
    /// that would report it is gone. The source shape is what there is.
    /// </summary>
    private static void BenchHolesArm()
    {
        string root = SrcRoot();
        string path = root == null ? null : Path.Combine(root, @"Dev\FitBench.cs");
        if (path == null || !File.Exists(path)) { Fail("S37-src", "cannot find src\\Dev\\FitBench.cs"); return; }
        string src = File.ReadAllText(path);

        // HOLE 1: a throw between SetAutorefreshOnTagsChanged(false) and the rebuild's own callback
        // left the addons manager quiesced for the session. It must be un-quiesced on the throw path
        // AND unconditionally by Close (the rebuild is a coroutine, so a finally would re-enable it
        // inside the very window the game turns it off for).
        Check("S37-autorefresh",
              Regex.IsMatch(src, @"catch \(Exception ex\)[\s\S]{0,2000}?SetAutorefreshOnTagsChanged\(true\)") &&
              Regex.IsMatch(src, @"Step\(failed, ""the addons manager's autorefresh"""),
              "autorefresh restored on the throw path and by Close");

        // HOLE 2: one brain reference and one remembered pose could not survive a REPLACEMENT camera -
        // the old brain stayed disabled forever and Close wrote the old pose onto the new camera.
        Check("S37-cameraledger",
              Regex.IsMatch(src, @"List<Held>\s+cameras") &&
              Regex.IsMatch(src, @"foreach \(Held h in cameras\) if \(h\.camera == cam\) return;") &&
              Regex.IsMatch(src, @"foreach \(Held h in cameras\)[\s\S]{0,800}?h\.brain\.enabled = true"),
              "one ledger row per touched camera, never overwritten, all restored");
        Check("S37-noSingleBrain",
              !Regex.IsMatch(src, @"^\s*private static Behaviour brain;", RegexOptions.Multiline),
              "the single-slot brain/pose fields are gone, not merely shadowed");

        // HOLE 3: Close cleared `entered` on its first line and then swallowed every failure, so one
        // bad restore left 'ct_bench close' answering "not open" while the screen stayed gone.
        Check("S37-closeretryable",
              Regex.IsMatch(src, @"if \(failed\.Count > 0\)[\s\S]{0,600}?return ""ct_bench NOT FULLY CLOSED") &&
              Regex.IsMatch(src, @"if \(failed\.Count > 0\)[\s\S]{0,700}?entered = false;"),
              "entered is cleared only AFTER the failure check, so the rescue stays retryable");
        Check("S37-closenames",
              Regex.IsMatch(src, @"string\.Join\("";\s*"", failed\.ToArray\(\)\)"),
              "the failures are NAMED, not counted");
        Check("S37-partialundo",
              Regex.IsMatch(src, @"masks\.RemoveAll\(") && Regex.IsMatch(src, @"hidden\.RemoveAll\("),
              "a canvas/mask that failed stays on its list for the retry; the rest are dropped");

        // HOLE 4: only Uninstall reached Close, so a destroyed or disabled arm left everything altered.
        Check("S37-lifecycle",
              Regex.IsMatch(src, @"private void OnDisable\(\)\s*\{\s*Rescue\(") &&
              Regex.IsMatch(src, @"private void OnDestroy\(\)\s*\{\s*Rescue\(") &&
              Regex.IsMatch(src, @"if \(!entered\) \{ open = false; return ""ct_bench: nothing to close"),
              "cleanup hangs off the lifecycle too, and Close is idempotent");

        // HOLE 5: Close forced the Geoscape scene and DefaultLightingSettings unconditionally - which
        // is a CHANGE unless that is what was there. Both are readable, so both are snapshotted.
        Check("S37-snapshot",
              Regex.IsMatch(src, @"priorScene = ActiveScene\(") &&
              Regex.IsMatch(src, @"priorLighting = lighting == null \? null : lighting\.CurrentLightingSettingsDef") &&
              Regex.IsMatch(src, @"ActivateScene\(priorScene\)") &&
              Regex.IsMatch(src, @"lighting\.SetLighting\(priorLighting, null\)"),
              "the scene and lighting that WERE there, restored - not 'default'");
        Check("S37-nodefaultrestore",
              !Regex.IsMatch(src, @"SetLighting\(level\.View\.DefaultLightingSettings"),
              "no unconditional restore to DefaultLightingSettings anywhere");

        // And the gizmo's own two non-negotiables, likewise unreachable from here except as shape.
        string gizmo = Path.Combine(root, @"Dev\FitGizmo.cs");
        if (!File.Exists(gizmo)) { Fail("S37-gizmosrc", "cannot find src\\Dev\\FitGizmo.cs"); return; }
        string g = File.ReadAllText(gizmo);
        Check("S37-commitpath",
              Regex.IsMatch(g, @"WeaponBuild\.Set\(dragKey,") &&
              !Regex.IsMatch(g, @"mesh\.(localPosition|position)\s*="),
              "every drag commits through WeaponBuild.Set; the live transform is never written");
        Check("S37-shaderprobe",
              Regex.IsMatch(g, @"Shader\.Find\(""Hidden/Internal-Colored""\)") &&
              Regex.IsMatch(g, @"if \(shader == null\)[\s\S]{0,400}?return null;"),
              "the shader is probed once and its absence disables the gizmo cleanly");
        Check("S37-onecamera",
              Regex.IsMatch(g, @"Camera\.current != cam"),
              "the handles are drawn for ONE camera, so no secondary camera duplicates them");
    }

    private static void BenchListArm()
    {
        string[] keys =
        {
            "SpiderGun_WeaponDef @ D:\\Mods\\SpiderDemo\\ppcontent.json",
            "Sidearm_WeaponDef @ D:\\Mods\\Other\\ppcontent.json"
        };
        Check("S26-id", BenchList.Id(keys[0]) == "SpiderGun_WeaponDef", BenchList.Id(keys[0]));
        // A path with no separator is its own id rather than an exception - the GUI loop reads this
        // every repaint and must not be the thing that throws.
        Check("S26-idbare", BenchList.Id("loose") == "loose" && BenchList.Id(null) == "", "degenerate keys");

        Check("S26-tunable", BenchList.KeyFor("SpiderGun_WeaponDef", keys) == keys[0],
              "the mod's own weapon resolves to its manifest key");
        Check("S26-vanilla", BenchList.KeyFor("PX_AssaultRifle_WeaponDef", keys) == null,
              "a shipped weapon is NOT tunable");
        // The id must match WHOLE, not by prefix: "Sidearm_WeaponDef" and a hypothetical
        // "Sidearm_WeaponDef_Mk2" are different guns and must not share a manifest row.
        Check("S26-whole", BenchList.KeyFor("Sidearm", keys) == null &&
                           BenchList.KeyFor("Sidearm_WeaponDef_Mk2", keys) == null,
              "no prefix or substring match on the id");

        Check("S26-filter", BenchList.Matches("SpiderGun_WeaponDef", "GUN") &&
                            !BenchList.Matches("PX_AssaultRifle_WeaponDef", "gun"),
              "case-insensitive substring");
        Check("S26-filterall", BenchList.Matches("PX_AssaultRifle_WeaponDef", "") &&
                               BenchList.Matches("PX_AssaultRifle_WeaponDef", null),
              "an empty filter keeps everything");

        float[] d = BenchList.Delta(1, -1f, 0.05f);
        Check("S26-delta", d[0] == 0f && Math.Abs(d[1] + 0.05f) < 1e-6f && d[2] == 0f,
              "Y- 0.05 -> " + d[0] + "," + d[1] + "," + d[2]);
        float[] none = BenchList.Delta(3, 1f, 0.05f);
        Check("S26-deltabad", none[0] == 0f && none[1] == 0f && none[2] == 0f, "an axis out of range moves nothing");

        Check("S26-step", Math.Abs(BenchList.NextStep(0.001f) - 0.005f) < 1e-6f &&
                          Math.Abs(BenchList.NextStep(BenchList.Steps[BenchList.Steps.Length - 1]) -
                                   BenchList.Steps[0]) < 1e-6f &&
                          Math.Abs(BenchList.NextStep(7f) - BenchList.Steps[0]) < 1e-6f,
              "the move ladder advances, wraps, and recovers from an off-ladder value");
        Check("S26-turn", Math.Abs(BenchList.NextTurn(90f) - 0.5f) < 1e-6f, "the turn ladder wraps");

        // ---- the hotkey, which is not a free choice ----
        // The 2026-08-29 regression, as an assert. The workbench opened on F9; F9 is the game's own
        // QuickLoad (GeoscapeViewState.cs:175-178), so every press opened the panel AND reloaded the
        // campaign behind it - and on a quicksave the game could not deserialize, that load ended at
        // the main menu with the campaign gone. In game there is nothing to see: the game's action
        // fires normally and no mod code throws. Offline it is one line.
        Check("S26-keyowned", BenchList.IsGameOwned("F9") && BenchList.IsGameOwned("f5") &&
                              BenchList.IsGameOwned("F4") && BenchList.IsGameOwned("F10"),
              "the game's own keys are recognised, case-insensitively");
        Check("S26-keyfree", !BenchList.IsGameOwned("B") && !BenchList.IsGameOwned("F12") &&
                             !BenchList.IsGameOwned("") && !BenchList.IsGameOwned(null),
              "a free key, DevRunner's own F12 and a degenerate name are not claimed");
        // The one that would have caught it: the workbench's ACTUAL hotkey, by the same name the
        // KeyCode prints, must not be a key the game answers to.
        string chosen = FitBenchHotkey();
        Check("S26-keybench", chosen != null && !BenchList.IsGameOwned(chosen),
              "the fit workbench's hotkey is '" + (chosen ?? "UNREADABLE") + "', not a key the game owns");

        // ---- the offer list: what a unit may be handed, and what falls out of its hand ----
        // Stand-ins for the WeaponDefs: the game's slot test is the predicate in the real thing, and
        // it needs a rig and a chassis; what is measured HERE is the half that has neither - keep,
        // drop, and above all what happens to the SELECTION when the unit underneath it changes.
        List<string> catalogue = new List<string> { "Rifle", "Claw", "Cannon" };
        string held = "Rifle";
        List<string> handed = BenchList.Offer(catalogue, n => n != "Claw", ref held);
        Check("S26-offer", handed.Count == 2 && handed.Contains("Rifle") && handed.Contains("Cannon") &&
                           held == "Rifle",
              "incompatible dropped, compatible kept, a still-valid selection survives");

        // The mutoid case: the rifle in the previous soldier's hand must not survive the switch.
        held = "Rifle";
        List<string> quadruped = BenchList.Offer(catalogue, n => n == "Claw", ref held);
        Check("S26-offerdrop", quadruped.Count == 1 && quadruped[0] == "Claw" && held == null,
              "a selection the new unit cannot hold comes back null, not stale");

        // "Nothing fits" and "we could not ask" must not look the same. A null test keeps everything
        // (the caller passes null when the unit has no addons manager to ask about); a test that
        // refuses everything really does leave an empty list, which is what the message line is for.
        held = "Rifle";
        Check("S26-offerall", BenchList.Offer(catalogue, null, ref held).Count == 3 && held == "Rifle",
              "no test means the whole catalogue, never an empty panel");
        held = "Rifle";
        List<string> nothing = BenchList.Offer(catalogue, n => false, ref held);
        Check("S26-offernone", nothing.Count == 0 && held == null,
              "a unit that can hold nothing gets an empty list AND an empty hand");

        held = "Rifle";
        Check("S26-offernull", BenchList.Offer<string>(null, null, ref held).Count == 0 && held == null,
              "no catalogue at all is empty rather than an exception in the GUI loop");

        BenchMineArm();
        BenchFrameArm();
        BenchPanelArm();
        BenchOrbitArm();
        BenchSavedArm();
        BenchRecoveryArm();
    }

    /// <summary>
    /// ============ S29: THE PANEL FITS, BOTH WAYS ============
    ///
    /// Defect 2, as asserts. Two independent clippings, and IMGUI is silent about both:
    ///   WIDTH  - an over-wide GUILayout row is not clipped to its BeginArea, it is DRAWN PAST THE EDGE.
    ///            The step row was three 140 px buttons inside a 324 px content width, so the third one
    ///            was on screen only in the sense that its pixels existed.
    ///   HEIGHT - the dial block was drawn last, below a 130 px and a 180 px list, and fell off the
    ///            bottom of an 803 px window. "какие то кнопки в самом низу когда выбрал кастомное
    ///            оружие из модов" - exactly that.
    /// Both are pure arithmetic over the panel's own constants, so both are decidable here, which is
    /// the only place they CAN be decided: in game the symptom is a button that is simply not there.
    /// </summary>
    private static void BenchPanelArm()
    {
        // ---- width ----
        Check("S29-row", BenchList.RowFits(3, 112f, BenchList.PanelWidth),
              "the three step buttons fit the panel: 3x112 + gaps vs content width " +
              BenchList.ContentWidth(BenchList.PanelWidth));
        // The shipped geometry, as the control in the same run. Without it the arm above would pass on
        // any panel wide enough for anything, and would never have caught what the user saw.
        Check("S29-row-ctl", !BenchList.RowFits(3, 140f, 340f),
              "while the geometry that shipped - three 140 px buttons in a 340 px panel - does NOT, " +
              "so the check is a measurement and not a blind pass");
        Check("S29-row-edge", BenchList.RowFits(0, 999f, 100f) && !BenchList.RowFits(1, 999f, 100f),
              "no buttons always fit; one button wider than the panel never does");

        // ---- the long def name ----
        const string longName = "Morgott_VultureAssaultRifle_Mk2_WeaponDef";
        string cut = BenchList.Elide(longName, 20);
        Check("S29-elide", cut.Length == 20 && cut.StartsWith("Morgott") && cut.EndsWith("WeaponDef"),
              "a long name is shortened from the MIDDLE, keeping both ends -> " + cut);
        Check("S29-elide-short", BenchList.Elide("AR_181", 20) == "AR_181" &&
                                 BenchList.Elide(null, 20) == "" && BenchList.Elide("abcdefgh", 1).Length == 5,
              "a name that already fits is untouched, and neither null nor a silly cap throws in the " +
              "GUI loop");
        // The point of eliding at all: it must never make the string LONGER, i.e. never widen the panel.
        foreach (int cap in new[] { 5, 12, 20, 44, 200 })
            Check("S29-elide-cap", BenchList.Elide(longName, cap).Length <= Math.Max(5, cap),
                  "cap " + cap + " -> " + BenchList.Elide(longName, cap).Length + " chars");

        // ---- height: the dial block is above the fold ----
        // 1277x803 is the window in the screenshot the user sent; the panel insets 8 px top and bottom.
        const float His = 803f - 2f * BenchList.PanelInset;
        Check("S29-dial-his", BenchList.DialReachable(His),
              "at the user's own 803 px window the dial block, SAVE row and answer line are all on " +
              "screen without scrolling");
        Check("S29-dial-small", BenchList.DialReachable(600f - 2f * BenchList.PanelInset),
              "and still at 600 px, which is smaller than anything he is likely to play at");
        Check("S29-dial-tiny", !BenchList.DialReachable(300f),
              "while a 300 px window genuinely cannot hold it - the outer scroll view is the backstop " +
              "there, and this arm proves the check is not vacuously true");

        // ---- height: the lists take only what is left, and collapse when there is nothing left ----
        float u, w;
        BenchList.Rows(His, true, true, true, out u, out w);
        Check("S29-rows", u > 0f && w > 0f && u <= BenchList.ListMax && w <= BenchList.ListMax,
              "at 803 px both pickers get room, capped at " + BenchList.ListMax + " -> " + u + " / " + w);
        Check("S29-rows-fit",
              BenchList.ChromeRows * BenchList.Row + BenchList.DialRows * BenchList.Row +
              BenchList.MessageHeight + 2f * BenchList.Row + u + w <= His,
              "and the whole panel - chrome, dial, message, both headers, both lists - fits the window");

        // 536 px, not 492: the chrome grew the tab strip and the Advanced toggle (ChromeRows 8 -> 10,
        // two more Row). The arm is the same assertion - squeezed to just above the dial block's own
        // height, the pickers give way and the dial still fits.
        BenchList.Rows(536f, true, true, true, out u, out w);
        Check("S29-rows-collapse", u == 0f && w == 0f && BenchList.DialReachable(536f),
              "squeezed, the PICKERS give way and the dial block still fits: " + u + " / " + w);

        BenchList.Rows(His, true, false, true, out u, out w);
        Check("S29-rows-one", u == 0f && w > 0f,
              "a collapsed list takes nothing, and the other one still gets its cap: " + u + " / " + w);
        BenchList.Rows(His, true, false, false, out u, out w);
        Check("S29-rows-none", u == 0f && w == 0f, "both collapsed is zero, not negative");
        BenchList.Rows(-5f, true, true, true, out u, out w);
        Check("S29-rows-junk", u == 0f && w == 0f, "a degenerate viewport collapses rather than throwing");

        // Without a weapon selected there is no dial block, so the pickers may have MORE room. If they
        // did not, the panel would waste a third of itself on a block that is one line of text.
        float u2, w2;
        BenchList.Rows(500f, false, true, true, out u2, out w2);
        BenchList.Rows(500f, true, true, true, out u, out w);
        Check("S29-rows-nodial", u2 > u,
              "with no weapon picked the pickers get the dial block's room back: " + u2 + " vs " + u);
    }

    /// <summary>
    /// ============ S30: THE VIEW CANNOT BE DRIVEN SOMEWHERE THERE IS NO WAY BACK FROM ============
    ///
    /// Defect 1's second half. The workbench's own log line is followed by nothing that says what went
    /// wrong, because nothing DID go wrong in the exception sense: 'lift' was an unbounded float that
    /// a repeated button press walked off the model, and 'reframe' only re-MEASURED at the current
    /// knobs, so it re-computed the same empty screen. The user's words were "я что-то нажал и вообще
    /// всё исчезло и не вернуть обратно" and that is the mechanism.
    ///
    /// Every knob the mouse and the buttons drive is now clamped, and the clamp is asserted against
    /// deliberately absurd input - a thousand-pixel drag, a hundred wheel notches, a NaN - because the
    /// in-game symptom of an unclamped one is a black screen with no message anywhere.
    /// </summary>
    private static void BenchOrbitArm()
    {
        // ---- pitch: the clamp that matters, since an unclamped one flips the camera upside down ----
        float pitch = 0f;
        for (int i = 0; i < 200; i++) pitch = BenchList.Tilt(pitch, -50f);
        Check("S30-pitch-max", pitch <= BenchList.PitchMax + 1e-4f && pitch >= BenchList.PitchMax - 1e-4f,
              "ten thousand pixels of downward drag stops dead at " + BenchList.PitchMax + " -> " + pitch);
        for (int i = 0; i < 400; i++) pitch = BenchList.Tilt(pitch, 50f);
        Check("S30-pitch-min", Math.Abs(pitch - BenchList.PitchMin) < 1e-4f,
              "and the same upward -> " + pitch);
        Check("S30-pitch-nan", BenchList.Tilt(float.NaN, 1f) == BenchList.PitchMin &&
                               BenchList.Clamp(float.NaN, -1f, 1f) == -1f,
              "a NaN lands on a real number rather than propagating into the camera transform");

        // ---- yaw: free, but WRAPPED, so the number on screen stays readable and never drifts to 1e9 ----
        float yaw = 0f;
        for (int i = 0; i < 500; i++) yaw = BenchList.Orbit(yaw, 37f);
        Check("S30-yaw-wrap", yaw >= 0f && yaw < 360f, "yaw stays in [0,360) after 500 drags -> " + yaw);
        Check("S30-yaw-dir", BenchList.Orbit(0f, 100f) < 180f && BenchList.Orbit(0f, -100f) > 180f,
              "dragging right swings the camera one way and left the other - not the same way twice");
        Check("S30-yaw-gain",
              Math.Abs(BenchList.Orbit(180f, 100f) - (180f + 100f * BenchList.DegreesPerPixel)) < 1e-3f,
              "at FreeCamera's own 0.2 deg/px: 100 px -> 20 deg");

        // ---- the drag SENSE, and the two toggles that own it ----
        // The reported defect: both axes felt backwards, the drag pushed the model away from the hand
        // instead of carrying it along. The default is now the flipped one, and each toggle flips
        // EXACTLY its own axis - a toggle that quietly moved the other one would be the same bug again.
        Check("S30-invert-default", BenchList.InvertX && BenchList.InvertY,
              "both axes ship inverted: dragging grabs and turns the model itself");
        Check("S30-drag-sign-x", BenchList.Orbit(180f, 100f) > 180f,
              "a positive (rightward) drag raises yaw -> " + BenchList.Orbit(180f, 100f));
        Check("S30-drag-sign-y", BenchList.Tilt(0f, 100f) < 0f,
              "a positive (upward) drag lowers pitch -> " + BenchList.Tilt(0f, 100f));
        try
        {
            BenchList.InvertX = false;
            Check("S30-invert-x-only",
                  BenchList.Orbit(180f, 100f) < 180f && BenchList.Tilt(0f, 100f) < 0f,
                  "'invert X' off flips yaw back and leaves pitch exactly where it was");
            BenchList.InvertX = true;
            BenchList.InvertY = false;
            Check("S30-invert-y-only",
                  BenchList.Tilt(0f, 100f) > 0f && BenchList.Orbit(180f, 100f) > 180f,
                  "'invert Y' off flips pitch back and leaves yaw exactly where it was");
        }
        finally { BenchList.InvertX = true; BenchList.InvertY = true; }

        // ---- zoom: proportional, clamped, and monotone in the right direction ----
        float zoom = BenchList.ZoomDefault;
        for (int i = 0; i < 100; i++) zoom = BenchList.Wheel(zoom, 1f);
        Check("S30-zoom-in", Math.Abs(zoom - BenchList.ZoomMin) < 1e-4f,
              "a hundred notches in bottoms out at " + BenchList.ZoomMin + " -> " + zoom);
        for (int i = 0; i < 200; i++) zoom = BenchList.Wheel(zoom, -1f);
        Check("S30-zoom-out", Math.Abs(zoom - BenchList.ZoomMax) < 1e-4f,
              "and out tops out at " + BenchList.ZoomMax + " -> " + zoom);
        Check("S30-zoom-dir", BenchList.Wheel(4f, 1f) < 4f && BenchList.Wheel(4f, -1f) > 4f,
              "scroll up is closer, scroll down is further - " + BenchList.Wheel(4f, 1f) + " / " +
              BenchList.Wheel(4f, -1f));
        Check("S30-zoom-proportional",
              Math.Abs(4f - BenchList.Wheel(4f, 1f)) > Math.Abs(2f - BenchList.Wheel(2f, 1f)),
              "and one notch covers more ground when further out - FreeCamera's proportional feel");
        Check("S30-zoom-junk", BenchList.Wheel(4f, float.NaN) >= BenchList.ZoomMin &&
                               BenchList.Wheel(4f, 10000f) >= BenchList.ZoomMin &&
                               BenchList.Wheel(4f, -10000f) <= BenchList.ZoomMax,
              "a trackpad's absurd notch count and a NaN both stay inside the band");

        // ---- lift: the one that actually caused it, now bounded ----
        float lift = 0f;
        for (int i = 0; i < 500; i++)
            lift = BenchList.Clamp(lift - BenchList.LiftStep, BenchList.LiftMin, BenchList.LiftMax);
        Check("S30-lift", Math.Abs(lift - BenchList.LiftMin) < 1e-4f,
              "five hundred presses of 'up' stop at " + BenchList.LiftMin + " radii, not at minus sixty " +
              "-> " + lift);

        // ---- the pivot ----
        // The orbit angles are NOT inputs to Frame, and that is the whole guarantee: the distance and
        // the sideways step are functions of the unit's radius and the panel alone, so turning the
        // camera cannot change how far it stands from the aim point. The aim point is the measured
        // bounds centre (FitBench.Reframe), so the model stays the centre of the orbit by construction.
        float d1, lat1, d2, lat2;
        BenchList.Frame(1.7f, 40f, 1277f, 803f, BenchList.PanelWidth, 1.35f, out d1, out lat1);
        BenchList.Frame(1.7f, 40f, 1277f, 803f, BenchList.PanelWidth, 1.35f, out d2, out lat2);
        Check("S30-pivot", d1 == d2 && lat1 == lat2 && d1 > 0f,
              "the framing distance is a function of the unit and the panel only - no orbit angle " +
              "enters it, so an orbit is a rotation about the bounds centre and nothing else");

        // Where the mouse is allowed to act at all is no longer BenchList's answer: OrbitCamera.InViewport
        // owns it (src\Dev\OrbitCamera.cs), and it is measured in tests\ObjCodecTests\OrbitTests.cs -
        // "the panel's own column is out". Re-asserting it here would only be a second copy.
    }

    /// <summary>
    /// ============ S31: SAVED, OR MESSED ABOUT WITH ============
    ///
    /// "потом кнопку сохранить и сбросить. как бы я сейчас там не наколбасил" - the panel now says which
    /// of the two states a fit is in, and this is the whole of that decision. The in-game symptom of
    /// getting it wrong is the worst kind: a line that says SAVED over numbers that are not in the file,
    /// which is precisely the reassurance being asked for, given falsely.
    /// </summary>
    private static void BenchSavedArm()
    {
        float[] euler = { 0f, 90f, 0f }, offset = { 0.01f, -0.02f, 0.1f };
        const float scale = 0.5528f;

        Check("S31-same", BenchList.Same(scale, euler, offset, scale,
                                         new[] { 0f, 90f, 0f }, new[] { 0.01f, -0.02f, 0.1f }, 1e-5f),
              "untouched reads SAVED");
        Check("S31-offset", !BenchList.Same(scale, euler, offset, scale, euler,
                                            new[] { 0.01f, -0.02f, 0.11f }, 1e-5f),
              "one axis button - 0.01 on Z - flips it to MODIFIED");
        Check("S31-euler", !BenchList.Same(scale, euler, offset, scale, new[] { 0f, 95f, 0f }, offset, 1e-5f),
              "so does a turn");
        Check("S31-scale", !BenchList.Same(scale, euler, offset, 0.56f, euler, offset, 1e-5f),
              "so does a resize");
        // The smallest nudge the panel can make is 0.001 (BenchList.Steps[0]); it MUST register, or the
        // line would read SAVED through a whole session of fine work.
        Check("S31-finest", !BenchList.Same(scale, euler, offset, scale, euler,
                                            new[] { 0.01f, -0.02f, 0.1f + BenchList.Steps[0] }, 1e-5f),
              "including the FINEST step on the ladder, " + BenchList.Steps[0]);
        // ... while a float round-trip must not. Save writes these numbers out and reads them back, and
        // an exact comparison would report MODIFIED the instant after a successful save.
        Check("S31-roundtrip", BenchList.Same(scale, euler, offset, scale + 1e-7f,
                                              new[] { 0f, 90f + 1e-6f, 0f },
                                              new[] { 0.01f, -0.02f + 1e-7f, 0.1f }, 1e-5f),
              "a float round-trip's last bits do not: SAVE would otherwise read MODIFIED immediately");
        Check("S31-junk", !BenchList.Same(scale, null, offset, scale, euler, offset, 1e-5f) &&
                          BenchList.Same(scale, null, null, scale, null, null, 1e-5f) &&
                          !BenchList.Same(scale, new[] { 1f }, offset, scale, euler, offset, 1e-5f),
              "a missing or short triple is answered, not dereferenced - this runs every repaint");
    }

    /// <summary>
    /// ============ S32: A HALF-OPENED WORKBENCH CAN STILL BE CLOSED ============
    ///
    /// The heart of defect 1, and the one part of it that lives in the Unity half where no offline arm
    /// can execute it - so it is asserted OVER THE SOURCE, the way S13-wired asserts the Harmony prefix,
    /// with the broken shape as the control in the same run.
    ///
    /// What went wrong: Open mutated the game - hid the canvases, took the camera, swung the scene -
    /// and only set `open = true` near the END. Anything that threw in between left `open` false with
    /// every one of those changes still applied, and then 'ct_bench close' answered "not open" while
    /// the screen stayed gone. Three things had to become true and all three are checked:
    ///   1. a separate `entered` flag is raised BEFORE the first mutation,
    ///   2. the console's close is gated on `entered`, never on `open`,
    ///   3. Open's mutating half is inside a try whose catch calls Close.
    /// Plus the SoftMask restore, which is the other half of the same defect.
    /// </summary>
    private static void BenchRecoveryArm()
    {
        string src = SrcRoot();
        string path = src == null ? null : Path.Combine(src, "Dev", "FitBench.cs");
        string raw = path != null && File.Exists(path) ? File.ReadAllText(path) : null;
        Check("S32-source", raw != null, "src\\Dev\\FitBench.cs is readable -> " + (path ?? "NOT FOUND"));
        if (raw == null) return;
        // Strip() takes the string LITERALS out along with the comments, which is what makes these
        // checks measure code rather than prose - so nothing below may depend on literal text.
        string text = Strip(raw);

        Check("S32-entered", Recoverable(text),
              "close is gated on `entered`, Open raises it before mutating and undoes itself on a throw");
        // The shape that shipped, as the control: `open` gates everything and there is no try.
        Check("S32-entered-ctl",
              !Recoverable(Strip("static string Run(string[] a) { case \"close\": return open ? Close() " +
                                 ": \"not open\"; } static string Open() { hidden.Clear(); open = true; " +
                                 "return \"open\"; }")),
              "while the body that shipped fails the same check, so the arm is a measurement");
        Check("S32-not-open-gated", !Regex.IsMatch(text, @"return\s+open\s*\?\s*Close\s*\("),
              "and NO close path is gated on `open` any more - that is the one that answered " +
              "'not open' while the screen was gone");

        // The restore is now a RemoveAll rather than a foreach, because a mask that fails to come back
        // has to STAY on the list for the retry (S37-partialundo). What this arm measures is unchanged:
        // the masks are collected on the way in and switched back on by Close.
        Check("S32-softmask", raw.Contains("SoftMask") &&
                              Regex.IsMatch(text, @"masks\s*\.\s*Add") &&
                              Regex.IsMatch(text, @"masks\s*\.\s*RemoveAll\s*\([\s\S]{0,200}?enabled\s*=\s*true"),
              "the SoftMask components hidden with the canvases are switched back on by Close - the " +
              "13,128 NullReferenceExceptions in the 2026-08-29 Player.log");
        Check("S32-reset", Regex.IsMatch(text, @"static\s+string\s+ResetView\s*\(") &&
                           raw.Contains("RESET VIEW") &&
                           Regex.IsMatch(text, @"view\s*\.\s*Reset\s*\(\s*\)\s*;\s*lift\s*=\s*0f\s*;"),
              "and there is a RESET VIEW button that puts the knobs themselves back, not merely " +
              "re-measures at the knobs that lost the picture - the zoom and the orbit through " +
              "OrbitCamera.Reset (proven in tests\\ObjCodecTests\\OrbitTests.cs), the lift here");
        // The "record the pose once" rule, in the form that replaced the single `cameraTaken` bool:
        // a camera already on the ledger is returned from immediately, so a re-take can never record
        // OUR computed pose as the one to restore. Same guarantee, now per camera rather than global -
        // see S37-cameraledger for why a global one was not enough.
        Check("S32-camera-once",
              Regex.IsMatch(text, @"foreach\s*\(\s*Held\s+\w+\s+in\s+cameras\s*\)\s*if\s*\(\s*\w+\.camera\s*==\s*cam\s*\)\s*return\s*;"),
              "a re-taken camera does not record OUR pose as the one to restore - or Close would " +
              "leave the geoscape looking at a soldier's shoulder");
    }

    /// <summary>Does this source keep the workbench recoverable? See <see cref="BenchRecoveryArm"/>.</summary>
    private static bool Recoverable(string text)
    {
        return Regex.IsMatch(text, @"return\s+entered\s*\?\s*Close\s*\(\s*\)")
               && Regex.IsMatch(text, @"entered\s*=\s*true\s*;")
               && Regex.IsMatch(text, @"catch\s*\(\s*Exception\s+\w+\s*\)\s*\{\s*string\s+\w+\s*=\s*Close\s*\(\s*\)\s*;");
    }

    /// <summary>
    /// ============ S27: THE AUTHOR'S OWN WEAPON CANNOT VANISH ============
    ///
    /// The in-game defect this is the assert for: the weapon list showed nothing but shipped AC_/AN_
    /// guns, so the one kind of weapon the workbench can actually TUNE was the one kind not on screen.
    /// Two independent causes, both measured here:
    ///   1. the mod's weapons sort wherever the alphabet puts them, i.e. below the fold of a scrolling
    ///      list of two hundred - so they are now listed FIRST, unconditionally;
    ///   2. the game's own slot test can legitimately refuse one for the selected unit, and a refused
    ///      weapon simply disappeared. It is now kept and COUNTED, so the panel can say so in words.
    /// In game both look identical to "my bake did not load". Offline they are two asserts.
    /// </summary>
    private static void BenchMineArm()
    {
        // The identity is the def's own ResourcePath (WeaponBuild.cs:136), not a live-fit lookup:
        // WeaponBuild.Fitted() is EMPTY until a weapon has been instantiated in a hand, which is why
        // the panel used to call the author's own guns vanilla for as long as it mattered.
        Check("S27-mine", BenchList.IsMine("Morgott/ContentTool/SpiderGun") &&
                          !BenchList.IsMine("Weapons/PX_AssaultRifle") && !BenchList.IsMine(null) &&
                          !BenchList.IsMine("morgott/contenttool/x"),
              "ResourcePath decides, exactly and case-sensitively");

        List<string> shelf = new List<string> { "AC_Rifle", "AN_Cannon", "MyGun", "AZ_Pistol", "MyClaw" };
        Func<string, bool> mine = n => n.StartsWith("My", StringComparison.Ordinal);
        int refused;
        string sel = "AC_Rifle";
        // The unit refuses BOTH of the author's weapons and accepts every shipped one.
        List<string> shown = BenchList.Offer(shelf, n => !mine(n), mine, ref sel, out refused);
        Check("S27-kept", shown.Count == 5 && refused == 2,
              "a refused mod weapon is still listed, and counted: " + shown.Count + "/" + refused);
        Check("S27-first", shown[0] == "MyGun" && shown[1] == "MyClaw",
              "the mod's own come FIRST, in catalogue order: " + string.Join(",", shown.ToArray()));
        Check("S27-rest", shown[2] == "AC_Rifle" && shown[4] == "AZ_Pistol",
              "the shipped ones follow, order untouched");
        Check("S27-sel", sel == "AC_Rifle", "a still-listed selection survives");

        // A shipped weapon the unit refuses still drops - the filter is not weakened for everyone,
        // only for the weapons whose absence would read as a failed bake.
        sel = "AC_Rifle";
        List<string> narrow = BenchList.Offer(shelf, n => n == "AZ_Pistol", mine, ref sel, out refused);
        Check("S27-drop", narrow.Count == 3 && narrow[2] == "AZ_Pistol" && sel == null && refused == 2,
              "shipped refusals still drop, mod refusals do not: " + string.Join(",", narrow.ToArray()));

        // And with no mod weapons at all, this is exactly the old list.
        sel = null;
        List<string> none = BenchList.Offer(new List<string> { "AC_Rifle", "AZ_Pistol" },
                                            n => true, mine, ref sel, out refused);
        Check("S27-nomine", none.Count == 2 && refused == 0, "no mod weapons changes nothing");
    }

    /// <summary>
    /// ============ S28: THE CAMERA STANDS WHERE THE UNIT IS ON SCREEN ============
    ///
    /// The in-game defect: the workbench leaned on a CameraDirector hint, i.e. on framing authored for
    /// the game's OWN equip screen, and the unit ended up a shoulder at the right edge with an empty
    /// screen beside it. The framing is computed now, and this is the half of it that can be measured
    /// without a screenshot: PROJECT the bounds back through the pose the math produced and assert the
    /// pixels land inside the region the panel does not cover.
    /// </summary>
    private static void BenchFrameArm()
    {
        const float W = 1277f, H = 720f, P = 340f, M = 1.35f, FOV = 40f;

        float d, lat;
        BenchList.Frame(1f, FOV, W, H, P, M, out d, out lat);

        // The projection, done the plain way: the camera looks straight down its own forward axis, the
        // unit's centre sits `lat` to its right, and a point `x` right of the camera lands at
        // W/2 * (1 + x / (d*tanH)).
        double tanV = Math.Tan(FOV * 0.5 * Math.PI / 180.0);
        double tanH = tanV * (W / H);
        Func<double, double> sx = x => W / 2.0 * (1.0 + x / (d * tanH));
        Func<double, double> sy = y => H / 2.0 * (1.0 + y / (d * tanV));

        Check("S28-centre", Math.Abs(sx(lat) - (P + W) / 2.0) < 0.5,
              "the unit lands dead centre of the FREE region, not of the screen: " +
              sx(lat).ToString("0.0") + " vs " + ((P + W) / 2.0).ToString("0.0"));

        double left = sx(lat - 1.0), right = sx(lat + 1.0), top = sy(1.0), bottom = sy(-1.0);
        Check("S28-clear", left > P && right < W && bottom > 0f && top < H,
              "the whole unit is inside the free region: x " + left.ToString("0") + ".." +
              right.ToString("0") + " (panel ends " + P + ", screen " + W + "), y " +
              bottom.ToString("0") + ".." + top.ToString("0"));
        // ... and with room to spare, which is what the margin is for. A frame that only just fits
        // reads as cramped, and "just fits" is also where a rounding error becomes a clipped elbow.
        Check("S28-margin", left - P > (W - P) * 0.05 && W - right > (W - P) * 0.05,
              "at least 5% of the free width as breathing room on each side");

        // The panel is EXCLUDED, not merely accounted for: with no panel the camera does not step
        // sideways at all, and the unit is centred on the screen.
        float d0, lat0;
        BenchList.Frame(1f, FOV, W, H, 0f, M, out d0, out lat0);
        Check("S28-nopanel", Math.Abs(lat0) < 1e-4f && lat > 0.05f,
              "no panel means no sideways step; a panel means a positive one: " + lat0 + " / " + lat);
        Check("S28-narrower", d >= d0,
              "a panel never brings the camera CLOSER: " + d + " >= " + d0);
        // ... and when the width is what binds - a wide window, where the unit would otherwise be
        // nowhere near the top and bottom edges - the panel really does push the camera back.
        float dNoPanel, dPanel, latIgnore;
        BenchList.Frame(1f, FOV, 2400f, 600f, 0f, 1f, out dNoPanel, out latIgnore);
        BenchList.Frame(1f, FOV, 2400f, 600f, 2100f, 1f, out dPanel, out latIgnore);
        Check("S28-widthbound", dPanel > dNoPanel * 1.5f,
              "when the free WIDTH is the binding constraint the panel pushes the camera back: " +
              dPanel + " vs " + dNoPanel);

        // A bigger unit is seen from further back, proportionally - this is what makes a Crabman, a
        // four-legged mutoid and a vehicle work without three hand-tuned numbers each.
        float dBig, latBig;
        BenchList.Frame(3f, FOV, W, H, P, M, out dBig, out latBig);
        Check("S28-taller", dBig > d && Math.Abs(dBig - 3f * d) < 1e-3f,
              "three times the radius is three times the distance: " + dBig + " vs " + d);
        double bigLeft = W / 2.0 * (1.0 + (latBig - 3.0) / (dBig * tanH));
        double bigRight = W / 2.0 * (1.0 + (latBig + 3.0) / (dBig * tanH));
        Check("S28-tallclear", bigLeft > P && bigRight < W,
              "and the bigger unit is inside the free region too: " + bigLeft.ToString("0") + ".." +
              bigRight.ToString("0"));

        // ONE formula, both constraints, and which one binds is the aspect's business rather than a
        // branch of ours: a PORTRAIT window is narrow, so the free width is what runs out first; a wide
        // one has width to spare and the viewport HEIGHT binds instead - at exactly radius/tan(fov/2).
        float dTall, dWide, ignore;
        BenchList.Frame(1f, FOV, 600f, 1200f, 100f, 1f, out dTall, out ignore);
        BenchList.Frame(1f, FOV, 2400f, 600f, 100f, 1f, out dWide, out ignore);
        Check("S28-bound", dTall > dWide && Math.Abs(dWide - 1f / (float)tanV) < 1e-3f,
              "portrait is width-bound (" + dTall + "), landscape is height-bound (" + dWide +
              " == radius/tanV " + (1f / tanV).ToString("0.000") + ")");

        // A GUI loop must not be where a minimised window, a silly FOV or a unit whose renderers all
        // failed to load turns into an exception or a NaN camera position.
        float dJunk, latJunk;
        BenchList.Frame(0f, 0f, 0f, 0f, -50f, 0f, out dJunk, out latJunk);
        Check("S28-junk", !float.IsNaN(dJunk) && !float.IsInfinity(dJunk) && dJunk > 0f &&
                          !float.IsNaN(latJunk) && Math.Abs(latJunk) < 1e-6f,
              "degenerate input gives a finite pose, not a NaN: " + dJunk + " / " + latJunk);
        float dHuge, latHuge;
        BenchList.Frame(1f, FOV, 1000f, 800f, 100000f, M, out dHuge, out latHuge);
        Check("S28-panelcap", !float.IsInfinity(dHuge) && dHuge > 0f,
              "a panel wider than the screen is clamped rather than dividing by zero: " + dHuge);
    }

    /// <summary>
    /// ============ S38: THE TRANSPORT UNDER THE MODEL ============
    ///
    /// The animation strip's arithmetic. Every arm here is something whose in-game symptom is a WRONG
    /// PICTURE with no error anywhere: a looped clip that sits on its last frame instead of restarting,
    /// a scrub that walks past the end of the clip, a drag on the slider that also swings the camera, or
    /// a unit framed into screen the strip is standing on top of.
    /// </summary>
    private static void TransportArm()
    {
        // ---- the scrub slider's value, which arrives from a mouse and can be past either end ----
        Check("S38-normalized",
              BenchList.Normalized(-0.2f) == 0f && BenchList.Normalized(1.4f) == 1f &&
              BenchList.Normalized(0.5f) == 0.5f && BenchList.Normalized(float.NaN) == 0f,
              "a slider value is clamped into [0,1] at BOTH ends - a normalized time outside it is a " +
              "state the animator does not have");

        // ---- one frame of playback ----
        bool ended;
        // A LOOP MUST RESTART, NOT STALL. next = 0.9 + 0.25*1/1.25 = 1.1 -> 0.1, i.e. back round.
        // (0.25 s is exactly BenchList.MaxDelta, so the cap below is not what is being measured here.)
        float wrapped = BenchList.Advance(0.9f, 0.25f, 1f, 1.25f, true, out ended);
        Check("S38-loop-wrap", Math.Abs(wrapped - 0.1f) < 1e-4f && !ended,
              "a looped clip past its end comes back to the START (" + wrapped + "), which is a " +
              "restart - a clamp here reads in game as 'the animation froze on the last frame'");
        // ... and it must not stall on the NEXT frame either: from the wrapped position it keeps going.
        float again = BenchList.Advance(wrapped, 0.25f, 1f, 1.25f, true, out ended);
        Check("S38-loop-runs", again > wrapped && again < 1f,
              "and it keeps running from there rather than sticking: " + wrapped + " -> " + again);

        float clamped = BenchList.Advance(0.9f, 0.25f, 1f, 1.25f, false, out ended);
        Check("S38-noloop-clamp", Math.Abs(clamped - 1f) < 1e-6f && ended,
              "with loop OFF the same frame clamps to the end AND says so, so the caller can stop " +
              "rather than re-asserting the last pose forever");

        // The speed knob is clamped, not trusted: it drives a division and a runaway one skips the clip.
        float fast = BenchList.Advance(0f, 1f, 1000f, 1f, false, out ended);
        float capped = BenchList.Advance(0f, 1f, BenchList.SpeedMax, 1f, false, out ended);
        Check("S38-speed-clamp", Math.Abs(fast - capped) < 1e-6f,
              "an absurd speed is clamped to SpeedMax (" + BenchList.SpeedMax + "), not obeyed");
        float slow = BenchList.Advance(0f, 1f, 0f, 10f, false, out ended);
        Check("S38-speed-floor", slow > 0f &&
                                 Math.Abs(slow - BenchList.MaxDelta * BenchList.SpeedMin / 10f) < 1e-6f,
              "and a zero speed lands on SpeedMin rather than making PLAY do nothing at all: " + slow);

        // A stall, a breakpoint or a level load hands over a whole second of delta.
        float hitch = BenchList.Advance(0f, 10f, 1f, 4f, false, out ended);
        float cappedDt = BenchList.Advance(0f, BenchList.MaxDelta, 1f, 4f, false, out ended);
        Check("S38-dt-cap", Math.Abs(hitch - cappedDt) < 1e-6f,
              "a hitch does not teleport the clip - dt is capped at " + BenchList.MaxDelta + "s");
        Check("S38-junk",
              !float.IsNaN(BenchList.Advance(float.NaN, float.NaN, float.NaN, 0f, true, out ended)),
              "and every input being junk gives a number, not a NaN handed to the animator");

        Check("S38-seconds",
              Math.Abs(BenchList.Seconds(0.5f, 2f) - 1f) < 1e-6f && BenchList.Seconds(0.5f, 0f) == 0f,
              "the readout is normalized*length, and a clip with no length reads 0 rather than NaN");

        Check("S38-speed-ladder",
              BenchList.NextSpeed(1f) == 2f && BenchList.NextSpeed(2f) == BenchList.Speeds[0] &&
              BenchList.NextSpeed(3.7f) == BenchList.Speeds[0],
              "the speed ladder wraps, and a value off the ladder lands on the first rung");

        // ---- where the strip is, and what it is allowed to claim ----
        const float W = 1920f, H = 1080f, P = BenchList.PanelWidth;
        Check("S38-strip-shown", BenchList.StripShown(W, H, P),
              "a normal window has room for the strip");
        // Which pixels the strip CLAIMS from the mouse is no longer BenchList's answer either: the band
        // is exactly what OrbitCamera.InViewport subtracts (via StripReserve, asserted just below), and
        // tests\ObjCodecTests\OrbitTests.cs measures all three cases against it - "the transport strip is
        // out", "the panel's own column is out", "the middle of the scene is in".
        Check("S38-strip-top",
              Math.Abs(BenchList.StripTop(W, H, P) - (H - BenchList.StripHeight)) < 1e-6f,
              "the same edge in IMGUI's own convention, which is what the gizmo compares against");

        // No room = no strip, and then NO HEIGHT IS RESERVED either: a framing that paid for a strip
        // that was never drawn would stand the unit too low with nothing on screen to say why.
        const float Narrow = BenchList.PanelWidth + BenchList.StripMinWidth - 1f;
        Check("S38-strip-noroom",
              !BenchList.StripShown(Narrow, H, P) &&
              BenchList.StripReserve(Narrow, H, P) == 0f &&
              BenchList.StripTop(Narrow, H, P) == float.MaxValue,
              "a free region narrower than " + BenchList.StripMinWidth + " px gets no strip and costs " +
              "no height - and so claims no mouse either, since a zero reserve is what InViewport reads");
        Check("S38-strip-short",
              !BenchList.StripShown(W, BenchList.StripHeight * 3f - 1f, P) &&
              BenchList.StripReserve(W, H, P) == BenchList.StripHeight,
              "nor does a window too short to spare the height; a normal one reserves exactly " +
              BenchList.StripHeight);

        // ---- and the framing that has to leave room for it ----
        float dNo, latNo, vNo, dYes, latYes, vYes;
        BenchList.Frame(1.7f, 40f, W, H, P, 0f, 1.35f, out dNo, out latNo, out vNo);
        BenchList.Frame(1.7f, 40f, W, H, P, BenchList.StripHeight, 1.35f, out dYes, out latYes, out vYes);
        Check("S38-frame-strip", dYes > dNo && vYes > 0f && vNo == 0f,
              "the strip's height pushes the camera BACK (" + dNo + " -> " + dYes + ") and DOWN (" +
              vYes + "), which is what stands the unit clear above the transport");
        Check("S38-frame-vertical-shape",
              Math.Abs(vYes - dYes * (float)Math.Tan(40f * 0.5 * Math.PI / 180.0) *
                              (BenchList.StripHeight / H)) < 1e-3f,
              "and that drop is the vertical mirror of the panel's lateral offset - the same sum with " +
              "the vertical half-angle, not a second hand-tuned number");
        // The old five-out-parameter call is what S28 and S30 measure; it must still mean "no strip".
        float dOld, latOld;
        BenchList.Frame(1.7f, 40f, W, H, P, 1.35f, out dOld, out latOld);
        Check("S38-frame-compat", dOld == dNo && latOld == latNo,
              "and the strip-less overload is exactly the strip height of zero, so every earlier arm " +
              "still measures what it measured");
        float dJunk, latJunk, vJunk;
        BenchList.Frame(1.7f, 40f, W, H, P, float.NaN, 1.35f, out dJunk, out latJunk, out vJunk);
        Check("S38-frame-junk", !float.IsNaN(dJunk) && vJunk == 0f && dJunk > 0f,
              "a junk strip height is answered, not multiplied into the camera's position");
    }

    /// <summary>
    /// ============ S39: THE ROTATION RINGS ============
    ///
    /// "ещё бы сферу сделать а не тока стрелки чтобы по осям можно было вертеть". A ring drag has one
    /// number in it - a signed angle about one axis - and every way of getting it wrong looks in game
    /// like "the gun jumped" with nothing to read. So the angle, the accumulate-from-press rule, each
    /// degenerate refusal and the non-uniform-parent guard are all measured here instead.
    /// </summary>
    private static void RingArm()
    {
        float[] pivot = { 0f, 0f, 0f };
        float[] up = { 0f, 1f, 0f };
        // Two rays straight DOWN onto the y = 0 plane: the first hits (1,0,0), the second (0,0,1).
        float[] down = { 0f, -1f, 0f };
        float[] fromX = { 1f, 5f, 0f };
        float[] fromZ = { 0f, 5f, 1f };
        float[] fromDiag = { 1f, 5f, 1f };

        float deg;
        bool ok = BenchList.RingAngle(pivot, up, fromX, down, fromZ, down,
                                      BenchList.MinRingDot, 0.01f, out deg);
        // +X to +Z about +Y is MINUS ninety by the right-hand rule (about +Y, +X goes to -Z).
        Check("S39-angle-sign", ok && Math.Abs(deg + 90f) < 1e-3f,
              "+X to +Z about +Y is a signed -90 degrees, not +90 and not 90 without a sign -> " + deg);
        ok = BenchList.RingAngle(pivot, up, fromX, down, fromDiag, down,
                                 BenchList.MinRingDot, 0.01f, out deg);
        Check("S39-angle-magnitude", ok && Math.Abs(deg + 45f) < 1e-3f,
              "and half that turn reads exactly half the angle -> " + deg);
        // Flipping the AXIS flips the sign, which is the whole reason the measure is signed about it.
        float[] downAxis = { 0f, -1f, 0f };
        float flipped;
        BenchList.RingAngle(pivot, downAxis, fromX, down, fromZ, down,
                            BenchList.MinRingDot, 0.01f, out flipped);
        Check("S39-angle-axis", Math.Abs(flipped - 90f) < 1e-3f,
              "measured about -Y the same drag is +90 -> " + flipped);

        // ---- accumulate from the PRESS, never from the previous frame ----
        // The same 'now' ray asked twice gives the SAME angle, so a drag held still cannot creep and a
        // drag re-measured cannot double-apply. This is the arm that fails if anyone rewrites the ring
        // as a per-frame delta.
        float first, second;
        BenchList.RingAngle(pivot, up, fromX, down, fromZ, down, BenchList.MinRingDot, 0.01f, out first);
        BenchList.RingAngle(pivot, up, fromX, down, fromZ, down, BenchList.MinRingDot, 0.01f, out second);
        Check("S39-no-double", first == second,
              "two successive measures of the same pointer position are IDENTICAL (" + first + " / " +
              second + ") - the angle is always press-to-now, so nothing accumulates twice");
        // ... and walking the pointer through the halfway point does not add up to more than the whole.
        float half, whole;
        BenchList.RingAngle(pivot, up, fromX, down, fromDiag, down, BenchList.MinRingDot, 0.01f, out half);
        BenchList.RingAngle(pivot, up, fromX, down, fromZ, down, BenchList.MinRingDot, 0.01f, out whole);
        Check("S39-path-free", Math.Abs(whole - 2f * half) < 1e-3f,
              "a drag through the halfway point ends at the same angle as one straight there (" +
              half + " then " + whole + "), because the path is never integrated");

        // ---- the three refusals, none of them approximated ----
        // NEARLY parallel, not exactly: an exactly-parallel ray is refused by the plane solve itself,
        // so an arm built on one would pass with the minDot guard deleted. |dir.axis| here is ~0.05,
        // which is a real intersection a hundred units away and below MinRingDot.
        float[] sideways = { 1f, -0.05f, 0f };
        Check("S39-parallel",
              !BenchList.RingAngle(pivot, up, fromX, down, fromX, sideways,
                                   BenchList.MinRingDot, 0.01f, out deg) && deg == 0f,
              "a ray sliding ALONG the ring's plane is refused - its intersection runs to infinity and " +
              "one pixel of drag would answer with a hundred degrees");
        float[] away = { 0f, 1f, 0f };
        Check("S39-behind",
              !BenchList.RingAngle(pivot, up, fromX, down, fromX, away,
                                   BenchList.MinRingDot, 0.01f, out deg),
              "so is a ray pointing away from the plane - the ring is behind the camera");
        // NEAR the pivot, not exactly on it, for the same reason: an exact hit has no direction and is
        // refused by the normalise, so the minRadius guard would not be what the arm measured.
        float[] atPivot = { 0.1f, 5f, 0f };
        Check("S39-on-pivot",
              !BenchList.RingAngle(pivot, up, fromX, down, atPivot, down,
                                   BenchList.MinRingDot, 0.5f, out deg) &&
              BenchList.RingAngle(pivot, up, fromX, down, atPivot, down,
                                  BenchList.MinRingDot, 0.05f, out deg),
              "a hit landing inside the ring's own middle is refused - the angle read off a pixel-wide " +
              "lever is noise - while the SAME hit is accepted once the ring is small enough to mean it");
        Check("S39-degenerate-axis",
              !BenchList.RingAngle(pivot, new[] { 0f, 0f, 0f }, fromX, down, fromZ, down,
                                   BenchList.MinRingDot, 0.01f, out deg) &&
              !BenchList.RingAngle(null, up, fromX, down, fromZ, down,
                                   BenchList.MinRingDot, 0.01f, out deg),
              "a zero axis or a missing pivot is answered, not dereferenced - this runs every drag frame");

        // ---- the parent frame the rotation has to be writable in ----
        string why;
        float[] identity = { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f };
        Check("S39-uniform-ok", BenchList.RingsUsable(identity, BenchList.ScaleTolerance, out why) &&
                                why == null,
              "an ordinary parent frame carries a ring");
        float[] scaled = { 0.4f, 0f, 0f, 0f, 0.4f, 0f, 0f, 0f, 0.4f };
        Check("S39-uniform-scaled", BenchList.RingsUsable(scaled, BenchList.ScaleTolerance, out why),
              "and so does a uniformly SCALED one - a hand at 0.4 is legitimate, not degenerate");
        float[] stretched = { 1f, 0f, 0f, 0f, 2f, 0f, 0f, 0f, 1f };
        Check("S39-nonuniform",
              !BenchList.RingsUsable(stretched, BenchList.ScaleTolerance, out why) &&
              why != null && why.Contains("UNEVENLY"),
              "a materially UNEVEN parent scale is refused with a word, because a world rotation under " +
              "it is not representable as a child-local one and the skew would be silent -> " + why);
        float[] mirrored = { 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, -1f };
        Check("S39-mirrored",
              !BenchList.RingsUsable(mirrored, BenchList.ScaleTolerance, out why) &&
              why != null && why.Contains("MIRRORED"),
              "so is a mirrored one, where the ring would turn the gun the opposite way to the drag");
        float[] collapsed = { 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f };
        Check("S39-collapsed",
              !BenchList.RingsUsable(collapsed, BenchList.ScaleTolerance, out why) && why != null &&
              !BenchList.RingsUsable(null, BenchList.ScaleTolerance, out why),
              "and a parent scaled to nothing, and no parent at all");

        // ---- what is clickable is what is drawn ----
        // A unit square as a closed polyline: the ring, sampled.
        float[] sqX = { 0f, 10f, 10f, 0f }, sqY = { 0f, 0f, 10f, 10f };
        Check("S39-polyline",
              Math.Abs(BenchList.PolylineDistance(sqX, sqY, 5f, 0f)) < 1e-4f &&
              Math.Abs(BenchList.PolylineDistance(sqX, sqY, 5f, 5f) - 5f) < 1e-4f &&
              Math.Abs(BenchList.PolylineDistance(sqX, sqY, 0f, 5f)) < 1e-4f,
              "distance to the CLOSED polyline: zero on an edge, and the centre is half a side away - " +
              "the closing segment is walked, or a quarter of every ring would be unclickable");
        Check("S39-polyline-junk",
              BenchList.PolylineDistance(null, sqY, 0f, 0f) == float.MaxValue &&
              BenchList.PolylineDistance(new[] { 1f }, new[] { 1f }, 0f, 0f) == float.MaxValue,
              "and a missing or one-point ring answers 'nowhere near', not an exception");

        float[][] rings = { new[] { 0f, 10f, 10f, 0f }, new[] { 100f, 110f, 110f, 100f },
                            new[] { 0f, 10f, 10f, 0f } };
        float[][] ringsY = { new[] { 0f, 0f, 10f, 10f }, new[] { 0f, 0f, 10f, 10f },
                             new[] { 0f, 0f, 10f, 10f } };
        bool[] all = { true, true, true }, second0 = { false, true, false };
        Check("S39-nearest",
              BenchList.NearestRing(rings, ringsY, all, 102f, 1f, BenchList.RingPickRadius) == 1 &&
              BenchList.NearestRing(rings, ringsY, all, 5f, 1f, BenchList.RingPickRadius) == 0,
              "the nearest ring wins");
        Check("S39-nearest-valid",
              BenchList.NearestRing(rings, ringsY, second0, 5f, 1f, BenchList.RingPickRadius) == -1,
              "a ring marked unusable is NOT pickable, which is what makes a dimmed ring honest");
        Check("S39-nearest-radius",
              BenchList.NearestRing(rings, ringsY, all, 5f, 60f, BenchList.RingPickRadius) == -1 &&
              BenchList.NearestRing(rings, ringsY, null, 5f, 1f, BenchList.RingPickRadius) == -1,
              "and a click further than " + BenchList.RingPickRadius + " px from every ring picks none");
    }

    private static void Fail(string gate, string detail) { failures++; Console.WriteLine(gate + " FAIL " + detail); }
}
