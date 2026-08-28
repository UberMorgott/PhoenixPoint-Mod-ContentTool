using System;
using System.IO;
using System.Reflection;

namespace Morgott.ContentTool.Wwise
{
    /// <summary>
    /// The set of Wwise IDs Phoenix Point already occupies, as a MEASURED set loaded from the mod's
    /// own resource - never a hardcoded count (FINAL-PLAN 9.3). Two sorted arrays packed from
    /// data\pp_wwise_index.json by tools\pack_id_index.py:
    ///   media    7697 = the COMPLETE occupied media set (_media_ids_all), including the 5 loose
    ///                   .wem absent from SoundbanksInfo.xml and the 1 with no ShortName;
    ///   computed 1064 = every name-hashed id (events, banks, switches, states, RTPCs, triggers).
    ///
    /// Binary, not the 517 KB JSON: the validator only ever asks "is this uint32 taken", so the
    /// names would be dead weight in the DLL and would need a JSON parser to reach.
    /// </summary>
    internal static class IdIndex
    {
        private static uint[] media, computed;

        internal static int MediaCount { get { Load(); return media.Length; } }
        internal static int ComputedCount { get { Load(); return computed.Length; } }

        /// <summary>True if a shipped media/WEM already owns this ID - the collision that silently shadows a game sound.</summary>
        internal static bool IsPpMedia(uint id) { Load(); return Array.BinarySearch(media, id) >= 0; }

        /// <summary>True if a shipped event/bank/switch/state/RTPC/trigger already owns this ID.</summary>
        internal static bool IsPpComputed(uint id) { Load(); return Array.BinarySearch(computed, id) >= 0; }

        private static void Load()
        {
            if (media != null) return;
            using (Stream s = Assembly.GetExecutingAssembly()
                       .GetManifestResourceStream("Morgott.ContentTool.ppids.bin"))
            {
                if (s == null) throw new InvalidOperationException("ppids.bin is missing from ContentTool.dll");
                using (BinaryReader r = new BinaryReader(s))
                {
                    if (new string(r.ReadChars(4)) != "CTID") throw new InvalidDataException("ppids.bin has no CTID header");
                    uint[] m = ReadArray(r);
                    computed = ReadArray(r);
                    media = m;      // assigned last: Load() is only complete when both are there
                }
            }
        }

        private static uint[] ReadArray(BinaryReader r)
        {
            uint[] a = new uint[r.ReadUInt32()];
            for (int i = 0; i < a.Length; i++) a[i] = r.ReadUInt32();
            return a;
        }
    }
}
