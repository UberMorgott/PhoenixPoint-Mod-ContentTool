using System;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Bake;
using Morgott.ContentTool.Wwise;

/// <summary>
/// WHAT A SHIPPED SOUND IS CALLED, offline. The 3105 loose .wem are named by their Wwise media id and
/// by nothing else, so `ct_list audio` could only print numbers and its filter - a FILE NAME substring -
/// matched nothing a human would ever type. The names come off the game's own SoundbanksInfo.xml, which
/// carries no UnityEngine type at all, so the whole map is proven here on a fixture instead of by
/// reading a console pane in a running game.
///
/// The fixture reproduces the SHAPE of the real file, measured off the install: a top-level
/// &lt;StreamedFiles&gt; block whose media no bank owns yet, banks whose own &lt;ShortName&gt; arrives
/// AFTER an &lt;ObjectPath&gt; and before any File, the same media repeated once per Event and again in
/// &lt;IncludedMemoryFiles&gt;, and a &lt;ReferencedStreamedFiles&gt; block that is the only thing which
/// tells a streamed media which bank plays it.
///
/// A MISSING FILE IS NOT A FAILURE - it is the numeric listing the tool printed before any of this
/// existed - so that arm is asserted too, not merely hoped for.
/// </summary>
internal static class SoundbankNamesTests
{
    private static int checks;

    /// <summary>
    /// 6 media across 2 banks plus one event: id 100 is streamed (loose, and claimed by BOTH banks, only
    /// through their ReferencedStreamedFiles), 200 is repeated inside an Event and again in the bank body,
    /// 201 is named with characters no file name may carry, and 300/301/302 live in a second bank.
    /// </summary>
    private const string Fixture = @"<?xml version=""1.0"" encoding=""utf-8""?>
<SoundBanksInfo Platform=""Windows"" SchemaVersion=""12"">
  <RootPaths><ProjectRoot>X:\Wwise\</ProjectRoot></RootPaths>
  <DialogueEvents/>
  <StreamedFiles>
    <File Id=""100"" Language=""SFX"">
      <ShortName>6_IND_Taunt_02.wav</ShortName>
      <Path>SFX\6_IND_Taunt_02_AAAA1111.wem</Path>
    </File>
  </StreamedFiles>
  <SoundBanks>
    <SoundBank Id=""111"" Language=""SFX"">
      <ObjectPath>\SoundBanks\Default Work Unit\VoiceBank</ObjectPath>
      <ShortName>VoiceBank</ShortName>
      <Path>VoiceBank.bnk</Path>
      <IncludedEvents>
        <Event Id=""222"" Name=""PlayTaunt"" ObjectPath=""\Events\PlayTaunt"" DurationType=""OneShot"">
          <IncludedMemoryFiles>
            <File Id=""200"" Language=""SFX"">
              <ShortName>2_IND_Taunt_01.wav</ShortName>
              <Path>SFX\2_IND_Taunt_01_BBBB2222.wem</Path>
            </File>
          </IncludedMemoryFiles>
        </Event>
      </IncludedEvents>
      <IncludedMemoryFiles>
        <File Id=""200"" Language=""SFX"">
          <ShortName>2_IND_Taunt_01.wav</ShortName>
          <Path>SFX\2_IND_Taunt_01_BBBB2222.wem</Path>
        </File>
        <File Id=""201"" Language=""SFX"">
          <ShortName>Confirm: Yes/No, ok.wav</ShortName>
          <Path>SFX\Confirm_BBBB2222.wem</Path>
        </File>
      </IncludedMemoryFiles>
      <ReferencedStreamedFiles>
        <File Id=""100"" Language=""SFX"">
          <ShortName>6_IND_Taunt_02.wav</ShortName>
          <Path>SFX\6_IND_Taunt_02_AAAA1111.wem</Path>
        </File>
      </ReferencedStreamedFiles>
    </SoundBank>
    <SoundBank Id=""333"" Language=""SFX"">
      <ObjectPath>\SoundBanks\Default Work Unit\MusicBank</ObjectPath>
      <ShortName>MusicBank</ShortName>
      <Path>MusicBank.bnk</Path>
      <IncludedMemoryFiles>
        <File Id=""300"" Language=""SFX""><ShortName>Geoscape_Theme.wav</ShortName><Path>SFX\g.wem</Path></File>
        <File Id=""301"" Language=""SFX""><ShortName>Combat_Theme.wav</ShortName><Path>SFX\c.wem</Path></File>
        <File Id=""302"" Language=""SFX""><ShortName>Menu_Theme.wav</ShortName><Path>SFX\m.wem</Path></File>
      </IncludedMemoryFiles>
      <ReferencedStreamedFiles>
        <File Id=""100"" Language=""SFX"">
          <ShortName>6_IND_Taunt_02.wav</ShortName>
          <Path>SFX\6_IND_Taunt_02_AAAA1111.wem</Path>
        </File>
      </ReferencedStreamedFiles>
    </SoundBank>
  </SoundBanks>
</SoundBanksInfo>";

    internal static string Run()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ct-soundbanknames");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        string xml = Path.Combine(dir, "SoundbanksInfo.xml");
        File.WriteAllText(xml, Fixture);

        SoundbankNames n = SoundbankNames.Load(xml);
        Check(n.Why == null, "a readable file reports no reason it could not be read: " + n.Why);
        Check(n.Count == 6, "the 6 distinct media are found once each, not once per repetition (got " + n.Count + ")");

        // ---- the name, without the .wav the XML spells and with it
        Check(n.Name(100) == "6_IND_Taunt_02", "the display name drops the .wav the ShortName carries");
        Check(n.ShortName(100) == "6_IND_Taunt_02.wav", "the ShortName is kept verbatim for the .csv");
        Check(n.Name(999) == "" && n.ShortName(999) == "", "an id nothing names answers empty, never null");

        // ---- the bank column, including the one case that fills it late
        Check(n.Bank(300) == "MusicBank" && n.Bank(200) == "VoiceBank", "a bank's own ShortName lands on its media");
        Check(n.Bank(100) == "VoiceBank+MusicBank",
              "a STREAMED media gets its banks from ReferencedStreamedFiles, not from where it was first seen, " +
              "and a media SEVERAL banks carry keeps them all (got '" + n.Bank(100) + "')");

        // ---- what is on disk vs what the XML names: 100 and 201 are loose, 999 is a file no XML mentions,
        //      readme is a loose file whose name is not a number at all, and 200/300/301/302 are in-bank.
        var loose = new List<string> { "100", "201", "999", "readme" };
        string all = n.Report(loose, null);
        Check(Rows(all) == 8, "every media is a row - the 6 named plus the 2 loose files nothing names (got " + Rows(all) + ")");
        Check(all.Contains("999") && all.Contains("readme"),
              "a loose file NO listing mentioned is exactly the hole this closes, so it gets a row");
        Check(Marked(all, "in-bank") == 4 && Marked(all, "loose") == 4,
              "the 4 in-bank media are listed and MARKED, the 4 loose files are marked extractable");
        Check(all.Contains("(unnamed)"), "a media with no name says so rather than printing a blank column");

        // ---- the filter: a NAME, an id or a bank, case-insensitive substring. The old one was the file
        //      name, which for these files is a number, so 'taunt' matched nothing that exists.
        string taunt = n.Report(loose, "taunt");
        Check(Rows(taunt) == 2 && taunt.Contains("6_IND_Taunt_02") && taunt.Contains("2_IND_Taunt_01"),
              "a NAME substring finds both taunts - one loose, one in-bank (got " + Rows(taunt) + ")");
        Check(Marked(taunt, "in-bank") == 1 && Marked(taunt, "loose") == 1,
              "the filtered listing keeps the loose/in-bank marking");
        Check(Rows(n.Report(loose, "301")) == 1, "an id still filters, for the caller who already has one");
        Check(Rows(n.Report(loose, "musicbank")) == 4, "a BANK name filters to that bank's media");
        Check(Rows(n.Report(loose, "MUSICbank")) == 4, "the filter is case-insensitive");

        // ---- A MEDIA IN SEVERAL BANKS. Filing it under the FIRST bank only lost it for every other
        //      bank's filter: measured on the shipped file, `ct_list audio TutorialCinematics` answered 0
        //      although that bank holds 4 media - each recorded under Cinematics, which was seen first.
        Check(n.Matches(100, "100", "MusicBank") && n.Matches(100, "100", "VoiceBank"),
              "EITHER bank of a multi-bank media matches, not just the one that was seen first");
        Check(!n.Matches(100, "100", "VoiceBank+Music"),
              "each bank is matched on its own - a filter cannot span the '+' that joins two of them");
        Check(Marked(n.Report(loose, "MusicBank"), "loose") == 1,
              "the later bank's filter reaches the LOOSE media, so `ct_extract audio --all` stops skipping it");
        Check(n.Report(loose, "6_IND_Taunt_02").Contains("VoiceBank+MusicBank"),
              "the row shows every bank, joined with '+'");
        Check(n.Report(loose, "301").Contains("  301" + Sp(8) + "Combat_Theme" + Sp(22) + "MusicBank" + Sp(17) + "in-bank"),
              "a SINGLE-bank row is byte-identical to before: id, name, bank, marking in fixed columns");
        Check(Rows(n.Report(loose, "readme")) == 1, "a loose file's own name still filters when nothing names it");
        Check(Rows(n.Report(loose, "nothing-is-called-this")) == 0, "a filter that matches nothing prints no rows");
        Check(n.InBankMatches(loose, "taunt") == 1 && n.InBankMatches(loose, null) == 4,
              "what the bulk extract must SKIP is counted separately from what it writes");

        // ---- NOT CAPPED. The old 60-row cut made rows 61+ unreachable by anything but a luckier filter.
        var many = new List<string>();
        for (int i = 0; i < 70; i++) many.Add((900000 + i).ToString());
        Check(Rows(n.Report(many, "9000")) == 70, "70 matching rows all print - there is no 60-row cut any more");

        // ---- ...but the game console PANE gets ten of them and a pointer. Thousands of rows there are
        //      one UI.Text each and nobody reads past the first screen; the file and the capture keep all.
        string big = n.Report(many, "9000");
        string cut = SoundbankNames.PaneCut(big, @"C:\L\ct_list-audio-1.txt");
        Check(Rows(cut) == 10, "the pane gets ten rows of the 70 (got " + Rows(cut) + ")");
        Check(cut.Split('\n')[0] == big.Split('\n')[0], "the header line is the report's own, unchanged");
        Check(cut.EndsWith("\n... 60 more - the whole list is in C:\\L\\ct_list-audio-1.txt"),
              "the trailer says how many rows were cut and where the whole list is");
        Check(Rows(big) == 70 && big.IndexOf("the whole list is in", StringComparison.Ordinal) < 0,
              "the full text is untouched - the file and PPCLI still get every row and no trailer");
        string few = SoundbankNames.PaneCut(n.Report(loose, "taunt"), @"C:\L\a.txt");
        Check(Rows(few) == 2 && few.EndsWith("\n... the whole list is in C:\\L\\a.txt"),
              "a listing under the cut keeps every row and still names the file, without 'more'");

        // ---- the .wav's name: what an author has to recognise in a folder, and what no file system takes
        Check(SoundbankNames.WavName(n.Name(201), 201, "201") == "Confirm_ Yes_No, ok__201",
              "':' and '/' become '_' - a name straight from the XML would be an unwritable path");
        Check(SoundbankNames.WavName(n.Name(100), 100, "100") == "6_IND_Taunt_02__100",
              "the id stays on the end: two sources can share a ShortName, and the id is what every command takes");
        Check(SoundbankNames.WavName("", 999, "999") == "999", "an unnamed media keeps the bare numeric name");
        Check(SoundbankNames.WavName("", 0, "readme") == "readme", "a non-numeric loose file keeps its own stem");
        Check(SoundbankNames.WavName(new string('x', 200), 5, "5").Length <= 84,
              "a pathological name is cut, not handed to the file system whole");
        Check(SoundbankNames.IdOf("6372602") == 6372602u && SoundbankNames.IdOf("readme") == 0u &&
              SoundbankNames.IdOf("-1") == 0u, "the id a file name spells, and 0 when it spells none");

        // ---- index.csv: the map outside the console, for ALL media including the ones this cannot write
        var wavs = new Dictionary<uint, string> { { 100u, "6_IND_Taunt_02__100.wav" } };
        string csv = n.Csv(loose, wavs);
        string[] lines = csv.TrimEnd('\n').Split('\n');
        Check(lines[0] == "id,shortName,bank,loose,wav", "the header names the five columns");
        Check(lines.Length == 9, "one row per media, in-bank included (got " + (lines.Length - 1) + ")");
        Check(csv.Contains("100,6_IND_Taunt_02.wav,VoiceBank+MusicBank,yes,6_IND_Taunt_02__100.wav"),
              "a written media carries the .wav that was actually produced for it, and every bank that holds it");
        Check(csv.Contains("300,Geoscape_Theme.wav,MusicBank,no,"),
              "an in-bank media is in the map, marked not loose, with no .wav");
        Check(csv.Contains("201,\"Confirm: Yes/No, ok.wav\",VoiceBank,yes,"),
              "a name containing a comma is QUOTED - an unquoted one would shift every later column");

        // ---- A MISSING OR UNPARSABLE FILE IS NOT AN ERROR: the numeric listing is what the tool printed
        //      before any of this existed, and it must still be printed rather than thrown.
        SoundbankNames gone = SoundbankNames.Load(Path.Combine(dir, "not-here.xml"));
        Check(gone.Why != null && gone.Count == 0, "a missing SoundbanksInfo.xml gives an empty map and a reason: " + gone.Why);
        string fallback = gone.Report(loose, null);
        Check(Rows(fallback) == 4 && Marked(fallback, "loose") == 4 &&
              fallback.Contains("NO NAMES") && fallback.Contains("(unnamed)"),
              "with no names the listing is the 4 loose files, by number, saying WHY they have no names");
        Check(Rows(gone.Report(loose, "201")) == 1 && Rows(gone.Report(loose, "taunt")) == 0,
              "with no names an id still filters and a name cannot - the honest answer, not a throw");

        string bad = Path.Combine(dir, "broken.xml");
        File.WriteAllText(bad, "<SoundBanksInfo><SoundBank><ShortName>B</ShortName");
        SoundbankNames broken = SoundbankNames.Load(bad);
        Check(broken.Why != null && broken.Count == 0 && broken.Report(loose, null).Contains("NO NAMES"),
              "a TRUNCATED file is survived the same way, not thrown out of the console");

        // ---- the loose listing itself is uncapped now: the cap is the caller's number, and the audio
        //      caller no longer passes one (Dev.ConsoleText.Bound + the spill file own the pane).
        string wemDir = Path.Combine(dir, "wem");
        Directory.CreateDirectory(wemDir);
        for (int i = 0; i < 70; i++) File.WriteAllBytes(Path.Combine(wemDir, (900000 + i) + ".wem"), new byte[1]);
        string looseReport = LooseFiles.Report(wemDir, ".wem", null, int.MaxValue);
        Check(Rows(looseReport) == 70 && !looseReport.Contains("narrow the filter"),
              "LooseFiles.Report prints all 70 when the caller stops capping it (got " + Rows(looseReport) + ")");

        string real = Real();
        Directory.Delete(dir, true);
        return "SOUNDBANK names PASS, " + checks + " check(s) - fixture: 6 media, 2 banks, one media in " +
               "BOTH, loose/in-bank marked, missing + truncated XML survived" + real;
    }

    /// <summary>
    /// The same parse against the GAME's own 4.4 MB file when this machine has one. The fixture proves
    /// the grammar; this proves the fixture is FAITHFUL to it - a shape assumption that only holds in
    /// the fixture would pass every check above and still print numbers in game. Missing install is
    /// VOID, never a failure: the file is machine-specific.
    /// </summary>
    private static string Real()
    {
        string root = Environment.GetEnvironmentVariable("PPRoot") ?? @"D:\Steam\steamapps\common\Phoenix Point";
        string audio = Path.Combine(root, @"PhoenixPointWin64_Data\StreamingAssets\Audio");
        string xml = Path.Combine(audio, @"GeneratedSoundBanks\Windows\SoundbanksInfo.xml");
        if (!File.Exists(xml)) return "\n  (the shipped SoundbanksInfo.xml is VOID here - set PPRoot to the game folder)";

        SoundbankNames n = SoundbankNames.Load(xml);
        Check(n.Why == null && n.Count > 1000, "the shipped SoundbanksInfo.xml parses (" + n.Count + " media)");

        var stems = new List<string>();
        foreach (string rel in LooseFiles.Find(audio, ".wem", null)) stems.Add(Path.GetFileNameWithoutExtension(rel));
        int named = 0;
        foreach (string s in stems) if (n.Name(SoundbankNames.IdOf(s)).Length > 0) named++;
        Check(named * 10 > stems.Count * 9,
              "at least 9 in 10 shipped .wem are NAMED by that file (" + named + " of " + stems.Count + ")");
        Check(n.Count > stems.Count, "the map covers more media than are loose - the rest are in .bnk");

        int inBank = n.InBankMatches(stems, null);
        return "\n  shipped: " + n.Count + " named media, " + stems.Count + " loose .wem (" + named +
               " named), " + inBank + " in-bank";
    }

    /// <summary>Rows are the indented lines under the one-line verdict.</summary>
    private static int Rows(string report)
    {
        int n = 0;
        foreach (string line in report.Split('\n')) if (line.StartsWith("  ")) n++;
        return n;
    }

    /// <summary>
    /// Rows carrying that marking in their LAST column. Counting the whole text would also count the
    /// verdict line, which says both words itself.
    /// </summary>
    private static int Marked(string report, string marking)
    {
        int n = 0;
        foreach (string line in report.Split('\n'))
            if (line.StartsWith("  ") && line.TrimEnd().EndsWith(marking)) n++;
        return n;
    }

    /// <summary>Column padding, spelled out so a hand-counted literal cannot drift off by one.</summary>
    private static string Sp(int n) { return new string(' ', n); }

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception("SOUNDBANK names FAIL: " + what);
    }
}
