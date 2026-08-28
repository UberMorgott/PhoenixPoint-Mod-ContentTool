using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Morgott.ContentTool.Wwise;

namespace Morgott.ContentTool.Bake
{
    /// <summary>
    /// The engine half of the sound route: at init, ContentTool loads every dependent mod's shipped
    /// replacement banks. A content mod ships `Dist\Sounds\&lt;mediaId&gt;.bnk` (media-only, built by
    /// `ct_sound bake`) and needs no code of its own; the file's PRESENCE is the declaration, exactly
    /// as `Content\Textures\*.png` is.
    ///
    /// NO GAME FILE IS TOUCHED. Measured 2026-08-13 (`ct_sound shapec`, build feb2d3b3): a media-only
    /// bank replaces a STREAMED shipped media (1200ms FILE -> 500ms MEMORY, same mediaID), a second
    /// bank wins without unloading the first, and the game re-loading its own bank afterwards does
    /// not take the sound back.
    ///
    /// LOADED ONCE, NEVER UNLOADED - also measured in that run and RE-measured 2026-08-27 in a full
    /// gate: after `UnloadBank` the event dies at ~18 ms with no duration callback rather than falling
    /// back to the shipped media, and the media stays dead for the rest of the session. So there is no
    /// tidy-up path here on purpose; a restart is the clean undo, and it IS clean because nothing was
    /// written to the install.
    ///
    /// ONE MEDIA, ONE OWNER, across mods: two mods shipping &lt;the same mediaId&gt;.bnk used to resolve
    /// by load order with no warning at all. They now go through the same deterministic policy the
    /// bundle and key routes use (<see cref="BundleClaims.MediaRefusal"/>): lowest mod id keeps the
    /// media, the later mod's bank is refused BY NAME and never loaded - refused rather than evicted,
    /// because of the paragraph above.
    /// </summary>
    internal static class SoundLoad
    {
        internal static string LoadAll(string modDir)
        {
            StringBuilder log = new StringBuilder();
            DirectoryInfo mods = modDir == null ? null : Directory.GetParent(modDir);
            if (mods == null || !mods.Exists) return "ct_sound: no mods folder above " + modDir;

            // A folder on disk is not a player's consent. Discovery and the gate are ONE thing
            // (Project.ContentMods), shared with the video route, so no route can grow its own idea
            // of which folders count.
            int failed = 0, skipped;
            List<string> enabled = Project.ContentMods.Enabled(
                modDir, SoundReplace.ShippedBanks, Project.ModRoster.Build(), log, out skipped);
            // LOWEST MOD ID FIRST. Two mods shipping a replacement for the SAME media must produce the
            // same winner on every machine, and the mod manager's order is not that - it changes
            // between launches. A bank cannot be unloaded once loaded (see UnloadMod), so the loser
            // cannot be evicted the way the bundle and key routes evict theirs; sorting the load order
            // is what makes the per-media refusal below deterministic instead of first-come.
            enabled.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string mod in enabled) LoadMod(mod, log, ref failed);
            // Counts including 0, always: a loader that found nothing must say so rather than print
            // an empty block that reads like success. The bank count is read from the LEDGER, not
            // from this loop - the runtime toggle loads banks through the same LoadMod, and a local
            // counter here reported 0 while nine banks were audibly playing (build 9b8cbf80).
            return "ct_sound: " + Project.ContentState.Items(Route) + " shipped replacement bank(s) " +
                   "loaded from " + mods.FullName +
                   ", " + failed + " failed, " + skipped + " skipped" +
                   (log.Length > 0 ? Environment.NewLine + log.ToString().TrimEnd() : "");
        }

        /// <summary>The route name this file owns in <see cref="Project.ContentState"/>.</summary>
        internal const string Route = "sound";

        /// <summary>
        /// ONE mod's shipped banks. The caller has already decided the player wants this mod - the
        /// startup scan by asking the roster, the runtime hook by being the enable itself.
        /// Claimed, so whichever of the two gets here first loads the banks and the other is a no-op:
        /// these files run to 24 MB and loading one twice is pure waste.
        /// </summary>
        internal static void LoadMod(string modDir, StringBuilder log, ref int failed)
        {
            string dir = Path.Combine(modDir, SoundReplace.ShippedBanks);
            // The runtime toggle reaches here for ANY content mod, including a video-only one.
            if (!Directory.Exists(dir) || !Project.ContentState.Claim(modDir, Route)) return;
            string name = new DirectoryInfo(modDir).Name;
            foreach (string path in Directory.GetFiles(dir, "*.bnk"))
            {
                // The generated file name IS the media id (`ct_sound bake` writes <mediaId>.bnk), so
                // the ownership question needs no parser: whoever is already serving that media keeps
                // it, and this mod's bank is not loaded at all. Without this two mods shipping the
                // same media resolved by load order with no warning anywhere - measured in game,
                // bank B over bank A simply won.
                string media = Path.GetFileNameWithoutExtension(path);
                string owner = Project.ContentState.Owner(Route, media);
                string refusal = BundleClaims.MediaRefusal(
                    owner == null ? null : Path.GetFileName(owner), name, media, Path.GetFileName(path));
                if (refusal != null) { log.AppendLine("  " + refusal); continue; }

                byte[] bytes;
                try { bytes = File.ReadAllBytes(path); }
                catch (IOException ex) { log.AppendLine("  " + path + ": unreadable, " + ex.Message); failed++; continue; }

                uint loaded;
                // The bank's OWN id, out of the BKHD prologue already in hand (BankPrune reads the
                // same four bytes off disk). Passing 0 made AudioProbe's pre-unload answer
                // AK_UnknownBankID every time, so the "never read AK_BankAlreadyLoaded as a failure"
                // guard it exists for never actually ran: a mod switched off and on again came back
                // as a FAILED load rather than a reload.
                uint bankId = bytes.Length >= 16 && Encoding.ASCII.GetString(bytes, 0, 4) == "BKHD"
                            ? BitConverter.ToUInt32(bytes, 12) : 0;
                string r = AudioProbe.LoadBank(bytes, bankId, out loaded);
                if (!r.Contains("AK_Success")) failed++;
                // The MEDIA, not the bank id: it is what ownership is decided on above, and the
                // ledger has to be the same list the refusal reads or the two drift apart.
                else Project.ContentState.Served(modDir, Route, media);
                log.AppendLine("  " + name + "\\" + Path.GetFileName(path) + " " + bytes.Length +
                               " B (bankId " + bankId + ", media " + media + ") -> " + r);
            }
        }

        /// <summary>
        /// THE CEILING, and it is a hard one: a replacement bank CANNOT be taken back in-session.
        /// Measured 2026-08-13 (`ct_sound shapec`, build feb2d3b3) - after UnloadBank the event dies
        /// at 17 ms with no duration callback instead of falling back to the shipped media. So the
        /// two available answers to "the player just switched this mod off" are the mod's sound, or
        /// NO sound. Unloading would be the second, which is a broken game rather than a restored
        /// one, so the bank is left alone and the log says exactly that.
        ///
        /// Nothing was written to the install, so the restart IS the clean undo - it just is not
        /// instant, and pretending otherwise is the lie this line exists to avoid.
        /// ponytail: no unload until the engine can be made to fall back to the shipped media.
        /// </summary>
        internal static string UnloadMod(string modDir)
        {
            List<string> banks = Project.ContentState.Release(modDir, Route);
            if (banks.Count == 0) return null;
            return "  " + new DirectoryInfo(modDir).Name + ": " + banks.Count + " replacement bank(s) " +
                   "(media " + string.Join(", ", banks.ToArray()) + ") stay loaded until you RESTART - " +
                   "Wwise does not fall back to the shipped media after an unload, it goes silent " +
                   "(measured). Nothing was written to your game, so the restart is a clean undo.";
        }
    }
}
