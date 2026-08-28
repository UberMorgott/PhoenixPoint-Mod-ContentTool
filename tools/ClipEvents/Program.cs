using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Morgott.ContentTool.Import;

namespace Morgott.ContentTool.Tools
{
    /// <summary>
    /// ============ WHEN DOES THIS CLIP ACTUALLY CONNECT? ============
    ///
    ///     ClipEvents &lt;model.glb&gt; [clipName]
    ///
    /// The one manifest number no author can read off a file and no engine may invent: the fraction of
    /// an attack clip at which the blow lands. <c>ppcontent.json</c>'s <c>"events"</c> spends it as
    /// <c>ShootShot</c>, and the game BLOCKS on that event - a wrong frame is damage on the wrong
    /// frame, a missing one is ten seconds of nothing per swing (AnimEventReceiver.cs:100,126).
    /// <see cref="CreatureManifest"/> therefore refuses to guess. This measures it instead.
    ///
    /// THE METRIC, and why it is honest rather than clever: while a creature strikes, the striking end
    /// of it travels AWAY from the rest of it. So for every frame, take each bone's displacement from
    /// its own frame-0 position, project that onto the clip's dominant direction of travel, and keep
    /// the largest. That curve rises through the wind-up, peaks at full extension - the strike - and
    /// falls through the recovery. No gait model, no per-limb tagging, no bone-name list: whatever part
    /// of the creature does the hitting is by definition the part that reaches furthest.
    ///
    /// The direction is measured too, not assumed: it is the principal axis of all bone displacement
    /// over the clip, so a creature that lunges sideways or rears upward reads the same way as one that
    /// stabs forward. A file's own axis convention never enters into it.
    ///
    /// ponytail: peak of one scalar curve, and the thresholds below are the calibration knob a real rig
    /// may need. This deliberately does NOT try to tell a two-hit combo from a single strike - it
    /// reports the largest peak and prints the whole curve so an author can see a second one and pick
    /// it by hand. A clip whose curve has no clear peak is reported as exactly that, not rounded off
    /// to a number that would look measured.
    /// </summary>
    internal static class Program
    {
        /// <summary>Fraction of the peak reach at which the strike is considered under way (ActionDo)
        /// and finished (ActionEnd). Not physics - a threshold, stated so it can be tuned.</summary>
        private const float StartsAt = 0.25f, EndsAt = 0.25f;

        /// <summary>A peak this shallow relative to the rig's own size is not a strike at all - it is
        /// an idle breathing. Refused rather than reported, so no number that was never measured ends
        /// up pasted into a manifest.</summary>
        private const float MinPeakOfHeight = 0.05f;

        private static int Main(string[] argv)
        {
            if (argv.Length < 1)
            {
                Console.WriteLine("usage: ClipEvents <model.glb> [clipName]   " +
                                  "(no clip name lists every clip in the file)");
                return 2;
            }
            string path = argv[0];
            if (!File.Exists(path)) { Console.WriteLine("no such file: " + path); return 2; }

            List<SampledClip> clips = new List<SampledClip>();
            SkinnedModel model = GlbReader.Read(File.ReadAllBytes(path), clips);
            BakedSkin skin = ModelBuild.From(model, Path.GetFileNameWithoutExtension(path));
            if (!skin.Rigged) { Console.WriteLine("the file carries no rig, so it has no clips to time"); return 2; }

            if (clips.Count == 0) { Console.WriteLine("the file carries no animation"); return 2; }

            if (argv.Length < 2)
            {
                Console.WriteLine(Path.GetFileName(path) + " carries " + clips.Count + " clip(s):");
                foreach (SampledClip c in clips)
                    Console.WriteLine("  " + c.Name + "  " + c.Times.Length + " frame(s) @ " +
                                      F(c.SampleRate) + " Hz = " + F(Duration(c)) + " s");
                return 0;
            }

            SampledClip clip = clips.FirstOrDefault(c =>
                string.Equals(c.Name, argv[1], StringComparison.OrdinalIgnoreCase));
            if (clip == null)
            {
                Console.WriteLine("no clip called '" + argv[1] + "'; the file has [" +
                                  string.Join(", ", clips.Select(c => c.Name).ToArray()) + "]");
                return 2;
            }
            return Report(clip, skin);
        }

        private static int Report(SampledClip clip, BakedSkin skin)
        {
            float[][] pos = Treadmill.Positions(clip, skin);
            if (pos == null) { Console.WriteLine("the rig's bone tree does not resolve"); return 2; }
            int frames = clip.Times.Length, bones = skin.BoneNames.Length;

            // The rig's own size, so every threshold below is relative to the model and not to a unit
            // system the file never promised.
            float low = float.MaxValue, high = float.MinValue;
            for (int f = 0; f < frames; f++)
                for (int b = 0; b < bones; b++)
                {
                    float y = pos[f][b * 3 + 1];
                    if (y < low) low = y;
                    if (y > high) high = y;
                }
            float height = high - low;

            // THE DIRECTION OF THE STRIKE, measured: the principal axis of every bone's displacement
            // from its own frame-0 position, over the whole clip. Same second-moment trick Treadmill
            // uses for the ground slide, in 3D here because a strike may rear upward.
            double[] m = new double[6];      // xx, xy, xz, yy, yz, zz
            for (int f = 1; f < frames; f++)
                for (int b = 0; b < bones; b++)
                {
                    double dx = pos[f][b * 3] - pos[0][b * 3];
                    double dy = pos[f][b * 3 + 1] - pos[0][b * 3 + 1];
                    double dz = pos[f][b * 3 + 2] - pos[0][b * 3 + 2];
                    m[0] += dx * dx; m[1] += dx * dy; m[2] += dx * dz;
                    m[3] += dy * dy; m[4] += dy * dz; m[5] += dz * dz;
                }
            float[] axis = Principal(m);

            // The reach curve, and WHICH bone owns the peak - printed because it is the sanity check an
            // author applies by eye: a spider's strike should peak on a tooth or a front leg, and if it
            // peaks on the abdomen the measurement found something other than the attack.
            float[] reach = new float[frames];
            string[] who = new string[frames];
            for (int f = 0; f < frames; f++)
            {
                float best = 0f; int bestBone = -1;
                for (int b = 0; b < bones; b++)
                {
                    float d = (pos[f][b * 3] - pos[0][b * 3]) * axis[0] +
                              (pos[f][b * 3 + 1] - pos[0][b * 3 + 1]) * axis[1] +
                              (pos[f][b * 3 + 2] - pos[0][b * 3 + 2]) * axis[2];
                    if (d > best) { best = d; bestBone = b; }
                }
                reach[f] = best;
                who[f] = bestBone < 0 ? "-" : skin.BoneNames[bestBone];
            }

            int peak = 0;
            for (int f = 1; f < frames; f++) if (reach[f] > reach[peak]) peak = f;
            float duration = Duration(clip);

            Console.WriteLine("clip '" + clip.Name + "': " + frames + " frame(s) @ " +
                              F(clip.SampleRate) + " Hz = " + F(duration) + " s, rig " + F(height) +
                              " tall, strike axis (" + F(axis[0]) + "," + F(axis[1]) + "," + F(axis[2]) + ")");
            for (int f = 0; f < frames; f++)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  f{0,-3} t={1,-8} reach={2,-10} {3}{4}", f, F(clip.Times[f] - clip.Times[0]),
                    F(reach[f]), who[f], f == peak ? "   <- PEAK" : ""));

            if (reach[peak] < MinPeakOfHeight * height)
            {
                Console.WriteLine();
                Console.WriteLine("REFUSED: the furthest anything reaches is " + F(reach[peak]) +
                    ", under " + MinPeakOfHeight + " of the rig's own " + F(height) +
                    " height - this clip does not strike at anything, so there is no honest frame to " +
                    "put ShootShot on. Pick the clip that actually attacks.");
                return 1;
            }

            // ActionDo where the strike gets under way, ActionEnd where it has subsided - both found on
            // the same curve, so all three numbers come from one measurement.
            int start = peak; while (start > 0 && reach[start - 1] > StartsAt * reach[peak]) start--;
            int end = peak; while (end < frames - 1 && reach[end + 1] > EndsAt * reach[peak]) end++;

            float fPeak = Frac(clip, peak), fStart = Frac(clip, start), fEnd = Frac(clip, end);
            // ActionEnd must not sit ON the last frame: the wait is registered after ShootShot returns,
            // and an event on the final frame of a non-looping clip can be missed entirely.
            if (fEnd > 0.98f) fEnd = 0.98f;

            Console.WriteLine();
            Console.WriteLine("peak reach " + F(reach[peak]) + " on '" + who[peak] + "' at frame " +
                              peak + " of " + (frames - 1) + " = " + F(fPeak) + " of the clip");
            Console.WriteLine("paste into ppcontent.json \"creature\": \"events\":");
            Console.WriteLine("    \"attack\": \"ActionDo " + F(fStart) + ", ShootShot " + F(fPeak) +
                              ", ActionEnd " + F(fEnd) + "\"");
            Console.WriteLine("(ActionDo = reach passes " + StartsAt + " of peak on the way up, " +
                              "ActionEnd = it falls back through " + EndsAt + " on the way down)");
            return 0;
        }

        private static float Frac(SampledClip clip, int frame)
        {
            float span = Duration(clip);
            return span <= 0f ? 0f : (clip.Times[frame] - clip.Times[0]) / span;
        }

        private static float Duration(SampledClip clip)
        {
            return clip.Times[clip.Times.Length - 1] - clip.Times[0];
        }

        /// <summary>Dominant eigenvector of a symmetric 3x3 second-moment matrix, by power iteration -
        /// twenty rounds is far past convergence for a matrix this well separated, and it avoids a
        /// closed-form cubic that would need its own degenerate cases.</summary>
        private static float[] Principal(double[] m)
        {
            double[,] a = { { m[0], m[1], m[2] }, { m[1], m[3], m[4] }, { m[2], m[4], m[5] } };
            double[] v = { 1, 1, 1 };
            for (int it = 0; it < 20; it++)
            {
                double[] n = new double[3];
                for (int r = 0; r < 3; r++) n[r] = a[r, 0] * v[0] + a[r, 1] * v[1] + a[r, 2] * v[2];
                double len = Math.Sqrt(n[0] * n[0] + n[1] * n[1] + n[2] * n[2]);
                if (len < 1e-12) return new[] { 0f, 0f, 1f };
                for (int r = 0; r < 3; r++) v[r] = n[r] / len;
            }
            return new[] { (float)v[0], (float)v[1], (float)v[2] };
        }

        private static string F(float v)
        {
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }
    }
}
