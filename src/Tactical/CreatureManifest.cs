using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Morgott.ContentTool.Tactical
{
    /// <summary>
    /// ============ THE ONE PLACE A MODDER CONFIGURES A DOWNLOADED CREATURE ============
    ///
    /// Everything a content mod gets to DECIDE about a creature lives in the <c>"creature"</c> block
    /// of the <c>ppcontent.json</c> it already ships. There is no second file and no second loader -
    /// this reads the same text <see cref="Project.ContentProject"/> reads, and the bake WRITES the
    /// discovered clip list back into it, so the file the author edits is the file the tool filled in.
    ///
    ///   "scale": 0.005,                       &lt;- TOP LEVEL, shared with the bake (see below)
    ///   "creature": {
    ///     "clips":  { "Spider_Walk": "walk", "Spider_Idle": "idle",
    ///                 "Spider_Attack": "attack", "Spider_Death": "death" },
    ///     "events": { "attack": "ActionDo 0.25, ShootShot 0.55, ActionEnd 0.90",
    ///                 "death":  "Ragdoll 0.90" },
    ///     "name": "Spider", "donor": "Swarmer_TacCharacterDef",
    ///     "up": "0,1,0", "lift": 32.8288,
    ///     "health": 40, "will": 10, "speed": 16, "volume": 1, "pace": 5.4284,
    ///     "colliders": "", "hitBones": "", "hitRadius": "", "aim": "",
    ///     "ranged": "Crabman_Head_Spitter_WeaponDef", "aiAction": "", "shootBone": ""
    ///   }
    ///
    /// THE RANGED KEYS, all optional, all off by default:
    ///   "ranged"    - the shipped WeaponDef to clone as a SECOND, ranged bodypart attack (a spit).
    ///                 Empty/absent = the creature is melee-only and NONE of the ranged wiring runs,
    ///                 which is the standing default for every creature built before this key existed.
    ///   "aiAction"  - the AI action def appended to the cloned actions template so the stock AI can
    ///                 CHOOSE to shoot. Empty = "MoveAndShoot_AIActionDef", the one-tile sibling of
    ///                 MoveAndStrike (same AIActionMoveAndAttackDef class; MoveAndShoot3x3 is the
    ///                 Mutog's multi-tile variant and is deliberately NOT the default).
    ///   "shootBone" - the rig bone the synthesised muzzle hangs on. Empty = MEASURED: the bone whose
    ///                 rest position sits furthest along the model's forward axis - the mouth end.
    ///
    /// WHY "clips" IS A MAP AND NOT A CONVENTION. The clip names inside a downloaded .glb are whatever
    /// its author typed - "Spider_Walk", "Armature|run", "Take 001". The ENGINE knows the ROLES the
    /// game drives (a controller has a locomotion state, an action state, a die state); it cannot know
    /// which of the file's clips is which, and a keyword guess would silently bind a death animation to
    /// walking. So the bake DISCOVERS the names and writes them out as a scaffold with empty roles, and
    /// the author fills them in. <see cref="Missing"/> is what refuses a bake that still has holes.
    ///
    /// WHY THE SCALE IS NOT IN THIS BLOCK. It is already a top-level key, because the BAKE needs it
    /// too: the root-motion ramp is measured in the GAME's units, not the file's
    /// (<c>ContentProject.Scale</c>). Repeating it here would be two numbers that must agree, which is
    /// one number too many - so the rig-root scale the mod applies IS the bake's own <c>"scale"</c>.
    ///
    /// ponytail: one flat block, one nested map for clips and one for events, and every other key a
    /// scalar with a measured or shipped default. No per-subsystem tri-states; anything absent is
    /// either measured or left alone, and anything that CANNOT be defaulted honestly is refused by name.
    /// </summary>
    internal sealed class CreatureManifest
    {
        /// <summary>
        /// The roles the engine knows how to drive, and which of them a creature cannot do without.
        ///
        /// These are not a taxonomy anyone invented - each is a state the shipped controllers reach and
        /// the game blocks on: locomotion (TacticalNavigationComponent waits for the nav clip), the
        /// Action state (BashAbility/TacticalAbility wait for ActionDo/ShootShot/ActionEnd), the Die
        /// state (RagdollDieAbility waits for Ragdoll), and the idle everything falls back to.
        /// <c>jump</c> is optional because a creature that cannot jump simply never reaches that state.
        ///
        /// <c>reaction</c> is the flinch when something hits the creature, and it exists because a model
        /// finally arrived carrying one. The game drives it exactly like the others and has all along:
        /// TacticalActor.cs:1627-1633 asks GetReactionAnimation, writes the answer over
        /// <c>TacActorAnimActionsDef.DefaultReactionClip</c> in the override controller and then fires
        /// <c>SetTrigger("Reaction")</c>; the clip itself comes off a
        /// <c>TacActorSimpleReactionAnimActionDef</c> (TacticalActor.cs:1597-1601). Optional, because a
        /// creature with no flinch clip simply never has one substituted and the trigger finds the
        /// donor's own state - which is what every creature built before this did.
        ///
        /// <c>ranged</c> is the spit's own clip, and it exists because a creature can now carry a
        /// second, ranged attack (the manifest's "ranged" weapon key). Optional twice over: with no
        /// ranged weapon the role is never read, and with a ranged weapon but no clip the general
        /// shoot anim action - already rewritten to the creature's own clips - plays the attack clip
        /// for the shot instead. Mapping it clones that action for ONLY the ranged weapon, so the
        /// spit and the bash stop sharing an animation.
        /// </summary>
        /// <summary>"climb" is the one traversal role: a single clip may legally fill a whole
        /// ClipSequence (Start/Loop/Stop are three fields, and ClipSequence.cs:16-25 only tests them for
        /// non-null), so ONE role covers what the slot map actually consumes. Optional - unmapped, the
        /// bake synthesises the three parts out of the walk cycle instead.</summary>
        internal static readonly string[] Roles = { "walk", "idle", "attack", "death", "jump", "reaction",
                                                    "ranged", "climb" };
        internal static readonly string[] RequiredRoles = { "walk", "idle", "attack", "death" };

        /// <summary>Clip name AS SPELLED IN THE MODEL FILE -&gt; role. Insertion-ordered, so the
        /// scaffold the bake rewrites keeps the file's own clip order.</summary>
        internal readonly List<KeyValuePair<string, string>> Clips = new List<KeyValuePair<string, string>>();
        /// <summary>role -&gt; the blocking animation events that clip must fire, in the order the
        /// ability waits for them. See <see cref="Event"/>.</summary>
        internal readonly List<KeyValuePair<string, Event[]>> Events = new List<KeyValuePair<string, Event[]>>();

        /// <summary>What the unit is called in the roster. Empty leaves the donor's own name.</summary>
        internal string Name = "";
        /// <summary>Which <c>Content\Models\*.glb</c> is the creature, by file stem. Only needed when a
        /// project ships more than one model - with a single .glb the engine uses that one and this
        /// stays empty.</summary>
        internal string Model = "";
        /// <summary>
        /// The shipped unit whose COMPONENT STRUCTURE is cloned - a TacCharacterDef name, or one of the
        /// two SharedGameTags tag fields that exist (see the remark at CreatureBuild's donor lookup for
        /// why a tag cannot name most units).
        ///
        /// THE DEFAULT IS A ONE-TILE CREATURE AND NOT THE BIGGEST NON-HUMANOID, which is the whole
        /// lesson of the first version: this said "MutogTag", and a Mutog is a 3x3 vehicle-class unit,
        /// so a demo about a small spider silently inherited a MedMonster nav agent (a multi-tile
        /// footprint and path preview), Move3x3_AbilityDef, and Mutog_DemolitionComponentDef - which is
        /// why a tiny spider smashed every wall it walked past. Swarmer is the smallest shipped unit
        /// that carries everything the clone REQUIRES: AgentType "Humanoid", an AddonsManagerDef with a
        /// SkeletonChassisAddonDef, a TacActorAnimActionsDef, and a bodypart that is a melee WeaponDef
        /// with a BashAbilityDef on it (Swarmer_Torso_BodyPartDef -> BashStrike_AbilityDef), which is
        /// what <see cref="CreatureBuild"/>'s Melee needs and what a Facehugger, with no bodyparts at
        /// all, cannot give.
        /// </summary>
        internal string Donor = "Swarmer_TacCharacterDef";
        /// <summary>The model's up axis AS IMPORTED. The rig rotation is DERIVED from it by
        /// Quaternion.FromToRotation, so the only thing that can be wrong is the measurement.</summary>
        internal float[] Up = { 0f, 1f, 0f };
        /// <summary>Distance from the model's origin DOWN to its lowest vertex, in FILE units. A model
        /// whose origin is its geometric centre stands in a hole exactly this deep without it.</summary>
        internal float Lift;
        /// <summary>Health the unit should enter play with. Zero leaves the donor's own Strength -
        /// which for a bodypart-free template is BORN DEAD, and CreatureFit says so out loud.</summary>
        internal float Health;
        internal int Will, Speed, Volume;
        /// <summary>
        /// TILES PER SECOND the creature TRAVERSES at - a different quantity from <see cref="Speed"/>,
        /// which is why it could not be folded into that key however much one number would be nicer.
        /// "speed" is ActionPoints (CharacterStats.cs:301-302), i.e. how FAR this unit gets in a turn;
        /// this is how FAST it crosses a tile, which in this game is one number for every unit because
        /// no def carries a movement speed at all. Absent = the shipped pace, measured off the soldier's
        /// own run loop (<see cref="Import.Treadmill.ShippedPace"/>); 0 = leave the downloaded clip at
        /// whatever pace its author animated it at.
        /// </summary>
        internal float Pace = Import.Treadmill.ShippedPace;

        /// <summary>
        /// DEGREES OF NOSE-UP PITCH the creature holds while it climbs, and the whole reason a walk
        /// cycle can stand in for a climb at all: a spider on a wall really does face up it, so at 90
        /// the ordinary gait reads as climbing rather than as a body sliding upright up a face.
        ///
        /// 0 - the default - is the HONEST answer for a biped, whose walk would look wrong tipped over.
        /// The start clip tips into it, the loop holds it, the stop takes it back to level at the top.
        /// </summary>
        internal float ClimbPitch;

        /// <summary>The shipped WeaponDef cloned as the SECOND, ranged attack. Empty = melee-only,
        /// and none of <see cref="CreatureRanged"/> runs - the default for every existing creature.</summary>
        internal string Ranged = "";
        /// <summary>The AI action def appended so the stock AI can choose to shoot. The default is
        /// the ONE-TILE MoveAndShoot, never the Mutog's 3x3 variant.</summary>
        internal string AiAction = "MoveAndShoot_AIActionDef";
        /// <summary>Bone-name override for the synthesised shoot point. Empty = measured (the bone
        /// furthest along the model's forward axis - the mouth end).</summary>
        internal string ShootBone = "";
        /// <summary>
        /// Accuracy percentage for the RANGED attack, written onto the ranged bodypart's aspect. 0
        /// leaves the donor's, which for both shipped donors is literally zero.
        ///
        /// It needs its own key and cannot ride on the base stats: CharacterStats.cs:26 holds Accuracy
        /// as a BaseStat starting at 0, and the only base values the engine can set are
        /// Endurance/Willpower/Speed (BaseCharacterStats declares no accuracy at all). A bodypart
        /// ASPECT is the one route in. A melee creature never needs it - a bash does not roll against
        /// accuracy - which is why this only appeared when the first ranged creature fired.
        /// </summary>
        internal float Accuracy;

        // ---- what CreatureFit already honoured, unchanged ---------------------------------------
        internal bool Off;
        internal string Aim = "";
        internal float HitRadius;
        internal string[] HitBones = new string[0];

        /// <summary>One blocking animation event and WHERE in its clip it fires, as a fraction of the
        /// clip's length. A fraction rather than a second so the number survives a re-export at a
        /// different frame rate.</summary>
        internal struct Event
        {
            internal string Name;
            internal float At;
        }

        /// <summary>The engine's defaults, for a project that declares no "creature" block at all.</summary>
        internal static readonly CreatureManifest None = new CreatureManifest();

        /// <summary>The clip name the author mapped to <paramref name="role"/>, or null.</summary>
        internal string ClipFor(string role)
        {
            foreach (KeyValuePair<string, string> e in Clips)
                if (string.Equals(e.Value, role, StringComparison.OrdinalIgnoreCase)) return e.Key;
            return null;
        }

        internal Event[] EventsFor(string role)
        {
            foreach (KeyValuePair<string, Event[]> e in Events)
                if (string.Equals(e.Key, role, StringComparison.OrdinalIgnoreCase)) return e.Value;
            return new Event[0];
        }

        /// <summary>
        /// The required roles this manifest does NOT map, BY NAME - the sentence a bake refuses with.
        /// Null when every one is mapped.
        ///
        /// It is deliberately a refusal and not a fallback. A role bound to the wrong clip is not a
        /// cosmetic defect: an idle in the Action state carries no ShootShot, so every attack eats
        /// three ten-second timeouts and reads to the player as the GAME hanging, not the mod.
        /// </summary>
        internal string Missing(IEnumerable<string> discovered)
        {
            string[] holes = RequiredRoles.Where(r => ClipFor(r) == null).ToArray();
            if (holes.Length == 0) return null;
            string[] free = discovered
                .Where(c => !Clips.Any(e => string.Equals(e.Key, c, StringComparison.OrdinalIgnoreCase) &&
                                            e.Value.Length > 0))
                .ToArray();
            return "ppcontent.json \"creature\": \"clips\" leaves " + holes.Length +
                   " REQUIRED role(s) unmapped: " + string.Join(", ", holes) +
                   ". The tool has written every clip it found in your model into that block; put one " +
                   "of these role names beside the clip that plays it" +
                   (free.Length == 0 ? "" : " - still unassigned: " + string.Join(", ", free)) +
                   ". The engine will NOT guess: a walk cycle and a death animation are the same shape " +
                   "of data in a .glb, and a wrong guess puts an event-less clip in the Action state, " +
                   "which is a 10s stall per attack (AnimEventReceiver.cs:100,126). Roles: " +
                   string.Join(", ", Roles) + " (" + string.Join(", ", RequiredRoles) + " required).";
        }

        // ------------------------------------------------------------------ reading

        /// <summary>The manifest of the mod at <paramref name="modDir"/>, or the defaults.</summary>
        internal static CreatureManifest Load(string modDir)
        {
            try
            {
                string meta = Path.Combine(modDir, Project.ContentMods.Manifest);
                return File.Exists(meta) ? Parse(File.ReadAllText(meta)) : None;
            }
            catch (Exception) { return None; }
        }

        /// <summary>
        /// Read out of the raw text, not through JsonUtility, for the reason ContentProject records at
        /// its own ParseReplace: Unity's reader returns null for the nested shapes here and gives no
        /// error at all. A declared key can never parse to silence.
        /// </summary>
        internal static CreatureManifest Parse(string json)
        {
            string block = Block(json, "creature");
            if (block == null) return None;
            CreatureManifest m = new CreatureManifest();

            foreach (KeyValuePair<string, string> e in Pairs(Block(block, "clips")))
                m.Clips.Add(new KeyValuePair<string, string>(e.Key, e.Value.Trim().ToLowerInvariant()));
            foreach (KeyValuePair<string, string> e in Pairs(Block(block, "events")))
                m.Events.Add(new KeyValuePair<string, Event[]>(e.Key.Trim().ToLowerInvariant(),
                                                               ParseEvents(e.Value)));

            // Scalars are read off the block with its NESTED objects removed, so a key inside "clips"
            // can never be mistaken for a key of the creature itself.
            string flat = Flat(block);
            m.Name = Field(flat, "name");
            m.Model = Field(flat, "model");
            string donor = Field(flat, "donor");
            if (donor.Length > 0) m.Donor = donor;
            float[] up = Vector(Field(flat, "up"));
            if (up != null) m.Up = up;
            m.Lift = Number(flat, "lift");
            m.Health = Number(flat, "health");
            m.Will = (int)Number(flat, "will");
            m.Speed = (int)Number(flat, "speed");
            m.Volume = (int)Number(flat, "volume");
            // ABSENT and ZERO are different answers here - absent takes the shipped pace, zero is the
            // author saying "my clip is already animated at the speed I want". Number() cannot tell
            // them apart, so the presence of the key is tested and not its value.
            if (Field(flat, "pace").Length > 0) m.Pace = Number(flat, "pace");
            m.ClimbPitch = Number(flat, "climbPitch");
            m.Ranged = Field(flat, "ranged");
            string aiAction = Field(flat, "aiAction");
            if (aiAction.Length > 0) m.AiAction = aiAction;
            m.ShootBone = Field(flat, "shootBone");
            m.Accuracy = Number(flat, "accuracy");
            m.Off = Field(flat, "colliders").Equals("off", StringComparison.OrdinalIgnoreCase);
            m.Aim = Field(flat, "aim");
            m.HitRadius = Number(flat, "hitRadius");
            m.HitBones = Field(flat, "hitBones").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                                .Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            return m;
        }

        /// <summary>"ActionDo 0.25, ShootShot 0.55" -&gt; two events, in that order. The ORDER is
        /// load-bearing: each wait is registered only after the previous one returned, so two events
        /// sharing a timestamp are not two events - the second fires while nothing is listening.</summary>
        private static Event[] ParseEvents(string spec)
        {
            List<Event> list = new List<Event>();
            foreach (string part in spec.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] bits = part.Trim().Split(new[] { ' ', '\t', ':', '@' },
                                                  StringSplitOptions.RemoveEmptyEntries);
                if (bits.Length == 0) continue;
                float at;
                if (bits.Length < 2 ||
                    !float.TryParse(bits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out at))
                    throw new InvalidDataException(
                        "ppcontent.json \"creature\": \"events\" entry '" + part.Trim() + "' has no time. " +
                        "Write \"<EventName> <fraction of the clip>\", e.g. \"ShootShot 0.55\" - the frame " +
                        "the animation actually connects. The engine will not guess it: a shot that " +
                        "fires before the leg lands reads as a bug in the GAME.");
                list.Add(new Event { Name = bits[0], At = Mathf01(at) });
            }
            return list.ToArray();
        }

        private static float Mathf01(float v) { return v < 0f ? 0f : v > 1f ? 1f : v; }

        /// <summary>
        /// The inner text of <c>"name": { ... }</c>, brace-matched so a NESTED object does not end the
        /// block early. Null when the key is absent. (The regex ContentProject uses for its flat arrays
        /// cannot do this - <c>[^{}]*</c> stops at the first inner brace.)
        /// </summary>
        internal static string Block(string json, string name)
        {
            if (json == null) return null;
            Match at = Regex.Match(json, "\"" + name + "\"\\s*:\\s*\\{");
            if (!at.Success) return null;
            int start = at.Index + at.Length, depth = 1;
            for (int i = start; i < json.Length; i++)
            {
                if (json[i] == '{') depth++;
                else if (json[i] == '}' && --depth == 0) return json.Substring(start, i - start);
            }
            throw new InvalidDataException("ppcontent.json \"" + name + "\" block is never closed");
        }

        /// <summary>Every <c>"key": value</c> of a block, in file order, values unquoted.</summary>
        private static List<KeyValuePair<string, string>> Pairs(string block)
        {
            List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
            if (block == null) return list;
            foreach (Match m in Regex.Matches(block, "\"([^\"]+)\"\\s*:\\s*(?:\"([^\"]*)\"|([^,}\\s]+))"))
                list.Add(new KeyValuePair<string, string>(
                    m.Groups[1].Value,
                    m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value));
            return list;
        }

        /// <summary>The block with every nested object removed, so scalar reads cannot reach into one.</summary>
        private static string Flat(string block)
        {
            StringBuilder sb = new StringBuilder();
            int depth = 0;
            foreach (char c in block)
            {
                if (c == '{') depth++;
                else if (c == '}') { if (depth > 0) depth--; }
                else if (depth == 0) sb.Append(c);
            }
            return sb.ToString();
        }

        private static string Field(string obj, string name)
        {
            return Regex.Match(obj, "\"" + name + "\"\\s*:\\s*\"?([^\",}]*)\"?").Groups[1].Value.Trim();
        }

        private static float Number(string obj, string name)
        {
            float v;
            float.TryParse(Field(obj, name), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            return v;
        }

        /// <summary>"0,1,0" -&gt; three floats, or null when the key is absent or malformed.</summary>
        private static float[] Vector(string text)
        {
            string[] bits = text.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (bits.Length != 3) return null;
            float[] v = new float[3];
            for (int i = 0; i < 3; i++)
                if (!float.TryParse(bits[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]))
                    return null;
            return v;
        }

        // ------------------------------------------------------------------ writing the scaffold

        /// <summary>
        /// WRITE THE MODEL'S OWN CLIP LIST INTO THE AUTHOR'S ppcontent.json, so the thing they have to
        /// map is sitting in front of them instead of in a log line they have to transcribe.
        ///
        /// Additive and idempotent: a clip already carrying a role keeps it, a clip the file has lost
        /// is dropped, and a NEW clip arrives with an empty role. Nothing else in the file is touched -
        /// the block is spliced back in at its own offsets, so comments, key order and formatting
        /// everywhere else survive a re-bake.
        ///
        /// Returns the new file text, or null when the project declares no "creature" block at all -
        /// which is how a texture-only or sound-only project never grows one it did not ask for.
        /// ponytail: string splice, not a JSON round trip. A round trip would need a writer this tool
        /// does not have and would reformat a file the author hand-edits.
        /// </summary>
        internal static string Scaffold(string json, IEnumerable<string> discovered)
        {
            Match at = Regex.Match(json, "\"creature\"\\s*:\\s*\\{");
            if (!at.Success) return null;
            string block = Block(json, "creature");
            CreatureManifest had = Parse(json);

            // THE FILE'S OWN LINE ENDING, not this platform's and not a hardcoded one.
            //
            // This method REWRITES a file the author maintains by hand, so flipping its line endings
            // is not cosmetic: on Windows it turns every line of their ppcontent.json into a diff and
            // buries the one line that actually changed. It also broke the idempotence this method
            // promises - re-scaffolding a finished CRLF manifest rewrote it as LF and the SCAFFOLD-ID
            // arm went red, which is exactly the "a scaffold silently ate my file" failure that arm
            // exists to catch. Measured: the demo's own manifest is 32 CRLF / 0 bare LF.
            //
            // ponytail: first newline wins. A file with mixed endings is already inconsistent and this
            // makes the block it touches match the majority case rather than trying to preserve the
            // mess line by line.
            string nl = json.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";

            // The file's own clip order, each keeping whatever role it already had.
            List<string> rows = new List<string>();
            foreach (string clip in discovered)
            {
                string role = "";
                foreach (KeyValuePair<string, string> e in had.Clips)
                    if (string.Equals(e.Key, clip, StringComparison.OrdinalIgnoreCase)) role = e.Value;
                rows.Add("      \"" + clip + "\": \"" + role + "\"");
            }
            string clips = rows.Count == 0 ? "{}"
                : "{" + nl + string.Join("," + nl, rows.ToArray()) + nl + "    }";

            string rest = StripKey(block, "clips").TrimEnd();
            rest = rest.TrimEnd(',', ' ', '\t', '\n', '\r');
            string inner = nl + "    \"clips\": " + clips +
                           (rest.Trim().Length == 0 ? nl + "  " : "," + rest + nl + "  ");

            int start = at.Index + at.Length;
            int end = start + block.Length;
            return json.Substring(0, start) + inner + json.Substring(end);
        }

        /// <summary>The block minus one key and its value, nested object included.</summary>
        private static string StripKey(string block, string name)
        {
            Match at = Regex.Match(block, "\\s*\"" + name + "\"\\s*:\\s*");
            if (!at.Success) return block;
            int i = at.Index + at.Length, depth = 0;
            bool inString = false;
            for (; i < block.Length; i++)
            {
                char c = block[i];
                if (inString) { if (c == '"') inString = false; continue; }
                if (c == '"') inString = true;
                else if (c == '{' || c == '[') depth++;
                else if (c == '}' || c == ']') depth--;
                else if (c == ',' && depth == 0) { i++; break; }
                if (depth < 0) break;
            }
            return block.Substring(0, at.Index) + block.Substring(Math.Min(i, block.Length));
        }
    }
}
