namespace Morgott.ContentTool.Wwise
{
    /// <summary>
    /// Wwise name-&gt;ID. Offline-proven exact on 1078/1078 shipped name-based pairs
    /// (PROVEN-FOUNDATIONS O1): FNV-1 32-bit, multiply THEN xor, name lowercased UTF-8, no masking.
    /// FNV-1a scores 0/1078, non-lowercased FNV-1 scores 0/1078, and the 30-bit mask matches only
    /// the subset whose top two bits are already zero - so none of those is a tidier spelling of
    /// this, they are simply wrong.
    ///
    /// COMPUTED family only: Event, SoundBank, Bus/AuxBus, Switch, SwitchGroup, State, StateGroup,
    /// GameParameter, Trigger. Media/WEM IDs are Wwise-ALLOCATED and match no hash of their name
    /// (0/7691 measured) - deriving one from a filename is a bug, not a shortcut (FINAL-PLAN 9.2).
    /// </summary>
    internal static class WwiseId
    {
        internal static uint Hash(string name)
        {
            uint h = 2166136261;
            foreach (byte b in System.Text.Encoding.UTF8.GetBytes(name.ToLowerInvariant()))
            {
                h *= 16777619;
                h ^= b;
            }
            return h;
        }
    }
}
