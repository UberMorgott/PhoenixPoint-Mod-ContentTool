using System;
using System.Collections.Generic;
using Morgott.ContentTool.Wwise;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// The audio half of a bake: turns a list of sounds and their storage mode into the assets that
    /// go inside MyMod.bundle - one generated bank, one packaged .wem per streamed sound, and the
    /// stream manifest (FINAL-PLAN 4.2/4.3, 5).
    ///
    /// Two modes, no Auto: FINAL-PLAN 5 requires the automatic threshold to be MEASURED, and
    /// nothing has measured it, so the tool does not pretend to know it.
    ///   embed  - media rides in the bank's DIDX+DATA and lives in Wwise RAM until UnloadBank
    ///   stream - media ships as its own bundle asset, materialized to &lt;modDir&gt;\WwiseAudio\
    /// </summary>
    internal static class AudioBake
    {
        internal sealed class Sound
        {
            internal string Name;       // namespaced; the Event ID is fnv1_lower32 of it
            internal byte[] Wem;
            internal uint MediaId;      // ALLOCATED, never derived from the name
            internal bool Stream;
            /// <summary>
            /// Declares that <see cref="MediaId"/> is a Phoenix Point media ID we mean to REPLACE.
            /// Suppresses only the PP-side collision - a duplicate inside the project stays an error
            /// (FINAL-PLAN 9.4).
            /// </summary>
            internal bool Replaces;
        }

        internal sealed class Result
        {
            internal uint BankId;
            /// <summary>The generated bank bytes, so a check can compare them with what the bundle gives back.</summary>
            internal byte[] Bank;
            internal string BankAsset;
            /// <summary>null when nothing is streamed - there is then no manifest to read.</summary>
            internal string ManifestAsset;
            internal int StreamCount;
        }

        /// <summary>
        /// Packages <paramref name="sounds"/> into the bundle being baked. Every returned name comes
        /// out of the baker itself, so the release loader's constants and m_Container can never
        /// disagree (FINAL-PLAN 2.3).
        /// </summary>
        internal static Result Package(BundleBaker baker, string bankName, IList<Sound> sounds)
        {
            Validate(bankName, sounds);

            List<BankGen.Src> srcs = new List<BankGen.Src>();
            List<StreamCache.Entry> streams = new List<StreamCache.Entry>();

            foreach (Sound s in sounds)
            {
                srcs.Add(new BankGen.Src { Name = s.Name, Wem = s.Wem, MediaId = s.MediaId, Stream = s.Stream });
                if (!s.Stream) continue;
                string key = baker.AddTextAsset("audio/streams/" + s.MediaId + ".wem", s.Wem);
                streams.Add(StreamCache.Describe(s.MediaId, key, s.Wem));
            }

            Result r = new Result { BankId = WwiseId.Hash(bankName), StreamCount = streams.Count };
            r.Bank = BankGen.Build(r.BankId, srcs);
            string flaw = BankGen.SelfCheck(r.Bank);
            if (flaw != null) throw new System.InvalidOperationException("generated bank is malformed: " + flaw);

            if (streams.Count > 0)
                r.ManifestAsset = baker.AddTextAsset("audio/streams.manifest", StreamCache.BuildManifest(streams));
            r.BankAsset = baker.AddTextAsset("audio/banks/" + bankName + ".bnk", r.Bank);
            return r;
        }

        /// <summary>
        /// The collision matrix of FINAL-PLAN 9.4, run before anything is written - a bake that
        /// shadows a shipped ID produces no error at runtime at all, so this is the only thing
        /// standing between a mod and a silently broken game sound. Reports every violation at once.
        ///
        /// COMPUTED ids are checked against PP's computed set and ALLOCATED media against PP's media
        /// set: the two live in different resolution spaces (a shipped bank routinely holds a HIRC
        /// object and a media sharing no namespace), so cross-family equality is not a collision.
        /// </summary>
        internal static void Validate(string bankName, IList<Sound> sounds)
        {
            List<string> bad = new List<string>();
            Dictionary<uint, string> ours = new Dictionary<uint, string>();
            Dictionary<uint, string> media = new Dictionary<uint, string>();

            Computed(bad, ours, WwiseId.Hash(bankName), "bank '" + bankName + "'");
            foreach (Sound s in sounds)
            {
                Computed(bad, ours, WwiseId.Hash(s.Name), "event '" + s.Name + "'");
                Computed(bad, ours, WwiseId.Hash(s.Name + "_sound"), "sound '" + s.Name + "'");
                Computed(bad, ours, WwiseId.Hash(s.Name + "_action"), "action '" + s.Name + "'");

                string owner;
                if (media.TryGetValue(s.MediaId, out owner))
                    bad.Add("media ID " + s.MediaId + " is used by both '" + owner + "' and '" + s.Name + "'");
                else media[s.MediaId] = s.Name;

                bool taken = IdIndex.IsPpMedia(s.MediaId);
                if (taken && !s.Replaces)
                    bad.Add("media ID " + s.MediaId + " ('" + s.Name + "') already belongs to Phoenix Point; " +
                            "allocate another one, or set Replaces to declare the replacement");
                if (!taken && s.Replaces)
                    bad.Add("'" + s.Name + "' declares Replaces but " + s.MediaId + " is not a Phoenix Point media ID");
            }

            if (bad.Count > 0)
                throw new InvalidOperationException("Wwise ID validation failed against " + IdIndex.MediaCount +
                    " known media and " + IdIndex.ComputedCount + " known computed IDs: " + string.Join("; ", bad.ToArray()));
        }

        private static void Computed(List<string> bad, Dictionary<uint, string> ours, uint id, string what)
        {
            string owner;
            if (ours.TryGetValue(id, out owner)) bad.Add("ID " + id + " is generated by both " + owner + " and " + what);
            else ours[id] = what;
            if (IdIndex.IsPpComputed(id))
                bad.Add(what + " hashes to " + id + ", which Phoenix Point already uses; rename it");
        }
    }
}
