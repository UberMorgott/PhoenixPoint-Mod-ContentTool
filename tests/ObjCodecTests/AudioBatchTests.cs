using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Morgott.ContentTool.Dev;
using Morgott.ContentTool.Wwise;

/// <summary>
/// `ct_extract audio --all` OFF THE MAIN THREAD, offline. The body moved out of the console command
/// into AudioBatch so the game no longer freezes for the whole decode; what that must not change is
/// the run itself, and what it must add is the one-run-at-a-time rule.
///
/// Built from files written here, never the install: one real PCM .wem (WwisePcm.BuildWem, the same
/// writer the bake uses) and one that is not a .wem at all - so the counted-and-named failure arm is
/// exercised on every machine. The decode runs on a WORKER THREAD, as in the game.
/// </summary>
internal static class AudioBatchTests
{
    private static int checks;

    internal static string Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "ct-audiobatch");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        string audio = Path.Combine(root, "Audio");
        string outDir = Path.Combine(root, "out");
        Directory.CreateDirectory(Path.Combine(audio, "sub"));

        byte[] pcm = new byte[400];
        for (int i = 0; i < pcm.Length; i += 2) pcm[i + 1] = (byte)(i % 64);
        File.WriteAllBytes(Path.Combine(audio, "1001.wem"), WwisePcm.BuildWem(pcm, 1, 48000));
        File.WriteAllBytes(Path.Combine(audio, Path.Combine("sub", "2002.wem")), new byte[64]);
        var rel = new List<string> { "1001.wem", "sub/2002.wem" };
        var stems = new List<string> { "1001", "2002" };
        SoundbankNames names = SoundbankNames.Load(null);   // no name map: extracts by number

        string refusal;
        AudioBatch run = AudioBatch.TryBegin(names, audio, rel, stems, outDir, null, out refusal);
        Check(run != null && refusal == null, "the first run claims the slot");
        Check(run.Matched == 2 && run.Processed == 0 && !run.Finished,
              "a claimed run knows its matched count before it starts and has done nothing yet");

        AudioBatch second = AudioBatch.TryBegin(names, audio, rel, stems, outDir, null, out refusal);
        Check(second == null && refusal != null && refusal.StartsWith("ct_extract REFUSED"),
              "a second run while one is going is refused by name, never queued: " + refusal);

        Thread worker = new Thread(run.Run) { IsBackground = true };
        worker.Start();
        Check(worker.Join(TimeSpan.FromSeconds(30)), "the worker finishes");
        Check(run.Finished && run.Summary != null, "a finished run carries its report");
        Check(run.Processed == 2 && run.Done == 1,
              "both files were attempted, the real .wem decoded (" + run.Done + " of " + run.Processed + ")");
        Check(run.Summary.Contains("extracted 1 of 2 loose media") && run.Summary.Contains("FAILED 2002"),
              "the report counts the pass AND names the failure: " + run.Summary.Replace('\n', '|'));
        Check(File.Exists(Path.Combine(outDir, "1001.wav")) && !File.Exists(Path.Combine(outDir, "2002.wav")),
              "the decoded .wav is written, the failed one leaves nothing under its name");
        string csv = File.ReadAllText(Path.Combine(outDir, "index.csv"));
        Check(csv.Contains("1001,") && csv.Contains("1001.wav"), "index.csv maps the id to the .wav written");

        // The slot is released when the run returns, so a later run is allowed - and the filter
        // narrows it to the media it names.
        AudioBatch again = AudioBatch.TryBegin(names, audio, rel, stems, outDir, "2002", out refusal);
        Check(again != null && again.Matched == 1, "after a run ends the next one starts; the filter matches one");
        again.Run();
        Check(again.Summary.Contains("extracted 0 of 1"), "the filtered run decodes only what it matched");

        Directory.Delete(root, true);
        return "AUDIO batch PASS, " + checks + " check(s)";
    }

    private static void Check(bool ok, string what)
    {
        checks++;
        if (!ok) throw new Exception("AUDIO batch FAIL: " + what);
    }
}
