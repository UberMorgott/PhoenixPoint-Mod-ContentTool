using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.IO;
using Morgott.ContentTool.Import;
using Morgott.ContentTool.Wwise;
using UnityEngine;

namespace Morgott.ContentTool.Project
{
    /// <summary>
    /// One shipped object this project replaces, declared in ppcontent.json. Three plain fields, no
    /// grammar to learn: which shipped bundle, which object inside it, which source file. Everything
    /// else - the patched copy, its compression, the catalog record - is derived.
    ///   "replace": [ { "bundle": "mutoid_assets_all.bundle", "asset": "Foo", "texture": "swatch" } ]
    /// `texture` names an imported Content\Textures\*.png by its file stem.
    ///
    /// Read by ParseReplace, not by JsonUtility - see the note there for why.
    /// </summary>
    [Serializable]
    public sealed class ShippedReplacement
    {
        public string bundle = null;
        public string asset = null;
        /// <summary>A Content\Textures\*.png stem - replaces a shipped Texture2D.</summary>
        public string texture = null;
        /// <summary>"_Glossiness=0.9" - sets one float on a shipped Material. Colors are the same
        /// shape in m_Colors and are a one-liner away when something needs them.</summary>
        public string material = null;
        /// <summary>
        /// A Content\Meshes\*.obj or *.glb stem - replaces a shipped Mesh's geometry. A .glb that
        /// carries an armature also replaces the SKIN: its own weights are kept and its joints are
        /// matched onto the shipped skeleton BY NAME, so joint order is free. An .obj has no skin at
        /// all and falls back to nearest-bone.
        /// </summary>
        public string mesh = null;
        /// <summary>
        /// "position*3" - scales one channel of a SHIPPED AnimationClip's curves. `position` and
        /// `scale` only: a rotation curve is a quaternion and multiplying one denormalises it, so
        /// "rotation*k" is refused by name rather than written.
        /// </summary>
        public string clip = null;
        /// <summary>
        /// A Content\Videos\ stem (.webm, .mp4 or .mov) - one row of the streamable Catalog.json.
        /// The ONLY kind with no "bundle": Phoenix Point's cutscenes are loose files on disk, in no
        /// bundle at all (docs\research-video-replacement.md), so "asset" names the shipped
        /// StreamingPath (or just its file name) instead.
        /// It is also the only kind where "asset" is OPTIONAL, and that is what picks REPLACE from
        /// ADD - the same "no mode for the author to choose" rule <see cref="PublishedKey"/> follows:
        /// naming a shipped clip repoints its row, naming none appends a brand-new row whose
        /// RuntimeKey is derived from the mod id and the clip name (VideoCatalog.KeyFor).
        /// </summary>
        public string video = null;
    }

    /// <summary>
    /// One catalog KEY this project serves out of its OWN bundle - route iii, gate C1
    /// (docs\design-one-bundle-mod.md). One concept, not two: whether this ADDS a key or REPOINTS a
    /// shipped one is decided by looking the key up in the game's catalog, never by a mode the
    /// author has to pick.
    ///   "publish": [ { "key": "morgott.sample/probe_tex", "asset": "textures/swatch", "type": "Texture2D" } ]
    /// `asset` is the path under the project's own bundle, exactly as Content\ spells it.
    /// </summary>
    [Serializable]
    public sealed class PublishedKey
    {
        public string key = null;
        public string asset = null;
        /// <summary>Required for an ADD (the catalog needs a resourceType); ignored for a REPOINT,
        /// which keeps the shipped one.</summary>
        public string type = null;
        /// <summary>';'-separated SHIPPED bundle files this asset's external PPtrs need mounted. A
        /// REPOINT inherits the shipped entry's own dependency set and normally needs none.</summary>
        public string deps = null;
    }

    /// <summary>A video the tool has understood: a file on disk, copied verbatim. There is nothing
    /// to decode - the game hands the file to Unity's VideoPlayer by path.</summary>
    internal sealed class ImportedVideo
    {
        internal string Name;
        internal string Path;
        /// <summary>Kept because the decoder is chosen by container: an .mp4 renamed .webm is refused.</summary>
        internal string Extension;
    }

    /// <summary>A mesh the tool has understood: already in the buffers a serialized Mesh wants.</summary>
    internal sealed class ImportedMesh
    {
        internal string Name;
        internal BakedMesh Baked;
        /// <summary>
        /// The .glb this came out of - null for an .obj. Kept because the BakedMesh buffers carry no
        /// skin: the joint names and WEIGHTS_0 a by-name rebind needs live only on the model.
        /// </summary>
        internal SkinnedModel Model;
        /// <summary>The alias sidecar that was applied to <see cref="Model"/>, or null. Carried so the
        /// bake log can name it - an author must never discover a rename by its effect.</summary>
        internal string SidecarPath;
        internal int AliasesApplied;
        /// <summary>Why a sidecar that EXISTS was not applied, or null. Carried for the same reason:
        /// the bake route has no other voice, and a stale sidecar the log never mentions is exactly
        /// the silence design §5 forbids.</summary>
        internal string SidecarRefusal;
        /// <summary>Sidecar keys this file has no bone for. Carried so the bake can name them exactly
        /// as the live preview does - a sidecar that matched NOTHING is otherwise invisible here.</summary>
        internal IList<string> UnusedAliasKeys;
    }

    /// <summary>
    /// A whole MODEL the tool has understood - geometry plus, when the file carries one, the armature.
    /// Unlike every other imported record this one is an ADD, not a replacement: it becomes a new
    /// prefab in the mod's own bundle and overwrites nothing the game ships.
    /// </summary>
    internal sealed class ImportedModel
    {
        internal string Name;
        internal BakedSkin Baked;
        /// <summary>
        /// The clips the .glb itself carries, in the file's own order (U8). Empty for a file with no
        /// animations, which is every model this tool baked before U9.
        /// </summary>
        internal readonly List<SampledClip> Clips = new List<SampledClip>();
    }

    /// <summary>A texture the tool has understood, whatever file it came from.</summary>
    internal sealed class ImportedTexture
    {
        internal string Name;
        internal int Width, Height;
        internal byte[] Rgba32;
    }

    /// <summary>
    /// A sound the tool has understood: already PCM .wem, because that is the only codec the tool
    /// emits. <see cref="MediaId"/> is ALLOCATED at import (never hashed from the name).
    /// </summary>
    internal sealed class ImportedAudio
    {
        internal string Name;
        internal byte[] Wem;
        internal bool Stream;
        internal uint MediaId;

        // --- the identity of what actually came out of the reader/decoder, and what the SOURCE
        //     declared it should be. The bake arm compares them; "a bank appeared" is not a verdict.
        internal int Channels, SampleRate, Frames;
        internal float Peak;
        internal string SourceFile, Extension;
        /// <summary>Null when the container declares nothing this tool can read independently.</summary>
        internal SourceAudio.Info Declared;
        internal string DeclaredWhy;
    }

    /// <summary>
    /// A content project on disk and everything imported out of it - the ONE imported
    /// representation both the live path and the bake path consume (FINAL-PLAN 0.3). Nothing here
    /// knows about AssetsTools.NET or serialized files; that stays in Bake.
    ///
    /// Layout (FINAL-PLAN 13):
    ///   ppcontent.json          { "id": "author.mod", "bundle": "MyMod.bundle" }
    ///   Content\Textures\*.png *.jpg *.jpeg
    ///   Content\Meshes\*.obj
    ///   Content\Models\*.glb                a whole model this project ADDS (see ImportModel)
    ///   Content\Videos\*.webm *.mp4 *.mov
    ///   Content\Audio\*.wav *.ogg *.mp3   name.stream.* -> streamed, anything else -> embedded
    ///
    /// The accepted set is exactly what gate F1 MEASURED this engine decoding on its own
    /// (docs\research-format-coverage.md): Texture2D.LoadImage takes PNG and JPEG and nothing else;
    /// VideoPlayer takes VP8-WebM, H.264 in MP4/MOV, and MPEG-4 in AVI. AUDIO is the exception, and
    /// deliberately so: this tool decodes .wav, .ogg and .mp3 ITSELF (WwisePcm.ReadAudio, NVorbis +
    /// NLayer merged in), because the engine's own decoders are unreachable here - Phoenix Point
    /// drives all sound through Wwise and ships Unity's audio subsystem shut (m_DisableAudio), so
    /// using them would mean editing a file in the player's install. .flac, .m4a/.aac, .wma and
    /// .opus stay refused by name: no decoder for them is going in.
    /// </summary>
    internal sealed class ContentProject
    {
        /// <summary>First ID of the tool's allocation range; ids are checked for collisions anyway.</summary>
        private const uint MediaIdBase = 0xC7000100;

        [Serializable]
        private sealed class Meta
        {
            public string id = null;
            public string bundle = null;
            /// <summary>Flat strings, so JsonUtility reads them the way it reads the two above - the
            /// custom-class ARRAYS are what it returns null for (see ParseReplace).</summary>
            public string loop = null;
            public string play = null;
            public float scale = 0f;
        }

        internal string Root { get; private set; }
        internal string Id { get; private set; }
        internal string BundleName { get; private set; }
        /// <summary>
        /// ppcontent.json <c>"loop": "Spider_Idle, Spider_Walk"</c> - which of the model's OWN clips
        /// cycle instead of holding their last frame. Kept as the author WROTE it: the names are parsed
        /// and matched in <see cref="Bake.ClipFields.Names"/>, which is where they can be proven offline
        /// (nothing in this file can be - it needs a Unity runtime for the texture decode).
        ///
        /// A declaration and not an inference because there is nothing to infer FROM: glTF has no loop
        /// flag, so a walk cycle and a death animation are the same shape of data in the file.
        /// </summary>
        internal string LoopDeclaration { get; private set; }
        /// <summary>
        /// ppcontent.json <c>"play": "Spider_Walk"</c> - which ONE of the model's clips its Animator
        /// plays. Empty means the first bakeable one, which is what every bake did before it existed.
        /// Resolved by <see cref="Bake.ClipFields.Chosen"/>.
        /// </summary>
        internal string PlayDeclaration { get; private set; }
        /// <summary>
        /// ppcontent.json <c>"scale": 0.005</c> - the uniform scale the MOD puts on this model's rig root
        /// in game, i.e. the factor between the file's units and the game's. 1 when the key is absent,
        /// which is every project that has never needed one.
        ///
        /// It exists because one baked number is measured by the engine in the game's units rather than
        /// the file's: the root-motion ramp. <c>AnimationInfos.cs:105/108</c> reads the motion node
        /// through <c>animatedObj.transform.InverseTransformPoint</c> - the animator object's own LOCAL
        /// space, so the rig scale divides straight back out - and <c>:123</c> hands that magnitude to
        /// <c>TacticalNavigationComponent.cs:376</c> as WORLD units per second, over a map whose tile is
        /// 1 unit (<c>TacticalMap.cs:67</c>). A rig imported at 200x game size therefore reported a
        /// walking speed 200x too high and teleported. See <c>ClipFields.Ramp</c>.
        ///
        /// It is a DECLARATION and not a measurement for the same reason <see cref="LoopDeclaration"/>
        /// is: how big the author wants their creature is not in the file. Nothing else in the bake
        /// reads it - the vertices still arrive at the file's own size, which is what the demo's own
        /// rig-root scale then corrects.
        /// ponytail: the ONE number the bake and the mod must agree on, so the demo's SpiderAxisCheck
        /// asserts the two are equal rather than trusting an author to keep them in step.
        /// </summary>
        internal float Scale { get; private set; }
        internal readonly List<ImportedTexture> Textures = new List<ImportedTexture>();
        internal readonly List<ImportedMesh> Meshes = new List<ImportedMesh>();
        /// <summary>Content\Models\*.glb - whole models this project ADDS to the game.</summary>
        internal readonly List<ImportedModel> Models = new List<ImportedModel>();
        internal readonly List<ImportedVideo> Videos = new List<ImportedVideo>();
        internal readonly List<ImportedAudio> Audio = new List<ImportedAudio>();
        /// <summary>ppcontent.json "replace" entries - shipped objects this project overwrites (route vii).</summary>
        internal readonly List<ShippedReplacement> Replace = new List<ShippedReplacement>();
        /// <summary>ppcontent.json "publish" entries - catalog keys served from this mod's own bundle (route iii).</summary>
        internal readonly List<PublishedKey> Publish = new List<PublishedKey>();
        /// <summary>replacements.json, already validated; empty for an add-only project (FINAL-PLAN 39.2).</summary>
        internal readonly List<ReplacementRule> Replacements = new List<ReplacementRule>();
        /// <summary>One sentence per record that did NOT load. Never empty silently.</summary>
        internal readonly List<string> ReplacementRefusals = new List<string>();
        /// <summary>
        /// One sentence per SOURCE FILE that could not be imported. It is a list and not an exception
        /// for the reason the clip path learned first (9a3747b): one unreadable sound used to abort
        /// <see cref="Load"/>, and with it the bake of every texture, mesh and model in the project.
        /// The bake reports each line out loud - a skipped source is never a silent one.
        /// </summary>
        internal readonly List<string> SourceRefusals = new List<string>();
        /// <summary>How many of those refusals there are - ALL of them, not only the ones an importer
        /// THREW: a format the tool never accepts, a stem two files answer to and a half-typed row of
        /// ppcontent.json are declared work that did not happen just as much as a broken .glb is.
        /// The bake adds it to its failure count, so skipping a source stays non-fatal to the OTHER
        /// sources without the run reporting ALL PASS over a model that never made it in.</summary>
        internal int ImportFailures;

        /// <summary>What ppcontent.json DECLARES, with nothing imported.</summary>
        internal sealed class Declared
        {
            internal string Id, BundleName;
            internal readonly List<PublishedKey> Publish = new List<PublishedKey>();
            /// <summary>The "replace" array, unimported - all a video install needs.</summary>
            internal readonly List<ShippedReplacement> Replace = new List<ShippedReplacement>();
            /// <summary>Content\Videos\ - "importing" a video is reading its name off the file
            /// system, so the declaration-only load carries the real thing, not a stub.</summary>
            internal readonly List<ImportedVideo> Videos = new List<ImportedVideo>();
        }

        /// <summary>
        /// The declaration only - no texture decode, no .glb read, no audio decoded. Installing a
        /// catalog KEY does not need the author's sources turned into anything: the bundle is
        /// already baked.
        ///
        /// A VIDEO install is the same case and takes the same route: it copies a file and writes a
        /// catalog row, so decoding the project's sounds to reach that is work nobody asked for.
        /// </summary>
        internal static Declared LoadDeclared(string root)
        {
            string metaPath = Path.Combine(root, "ppcontent.json");
            if (!File.Exists(metaPath)) throw new FileNotFoundException("no ppcontent.json in " + root, metaPath);
            string text = File.ReadAllText(metaPath);
            Meta m = JsonUtility.FromJson<Meta>(text);
            if (m == null || string.IsNullOrEmpty(m.id) || string.IsNullOrEmpty(m.bundle))
                throw new InvalidDataException("ppcontent.json needs both \"id\" and \"bundle\"");
            Declared d = new Declared { Id = m.id, BundleName = m.bundle };
            d.Publish.AddRange(ParsePublish(text));
            d.Replace.AddRange(ParseReplace(text));
            d.Videos.AddRange(ImportVideos(root));
            return d;
        }

        /// <summary>Reads ppcontent.json and imports every source file under Content\.</summary>
        internal static ContentProject Load(string root)
        {
            string metaPath = Path.Combine(root, "ppcontent.json");
            if (!File.Exists(metaPath)) throw new FileNotFoundException("no ppcontent.json in " + root, metaPath);
            // JsonUtility: Unity's own reader, so no JSON dependency enters the tool.
            Meta m = JsonUtility.FromJson<Meta>(File.ReadAllText(metaPath));
            if (m == null || string.IsNullOrEmpty(m.id) || string.IsNullOrEmpty(m.bundle))
                throw new InvalidDataException("ppcontent.json needs both \"id\" and \"bundle\"");

            ContentProject p = new ContentProject
            {
                Root = root, Id = m.id, BundleName = m.bundle,
                LoopDeclaration = m.loop, PlayDeclaration = m.play
            };
            p.Scale = ScaleOrRefuse(m.scale, p.SourceRefusals);
            // ONE SOURCE FILE CANNOT TAKE THE PROJECT DOWN WITH IT: every importer below throws by
            // design - that is how a .glb states what is wrong with it - and an escaping throw used to
            // abort this whole method, so a single unusable mesh produced no bundle at all and the
            // author saw only "the mod does not activate". SourceImport.Each is the audio path's
            // arrangement (9a3747b) applied to the other three folders.
            // The return of Each is NOT added here any more: every refusal below - thrown or not - is
            // counted once, in ONE place, at the end of this method. Adding it twice would report two
            // failures for one unreadable .glb.
            SourceImport.Each(Sources(root, "Textures", p.SourceRefusals, "*.png", "*.jpg", "*.jpeg"),
                              p.Textures, p.SourceRefusals, ImportTexture);
            SourceImport.Each(Sources(root, "Meshes", p.SourceRefusals, "*.obj", "*.glb"),
                              p.Meshes, p.SourceRefusals, ImportMesh);
            SourceImport.Each(Sources(root, "Models", p.SourceRefusals, "*.glb"),
                              p.Models, p.SourceRefusals, ImportModel);
            p.Videos.AddRange(ImportVideos(root));
            p.Replace.AddRange(ParseReplace(File.ReadAllText(metaPath), p.SourceRefusals));
            p.Publish.AddRange(ParsePublish(File.ReadAllText(metaPath), p.SourceRefusals));

            string refused = RefuseUnsupported(Path.Combine(Path.Combine(root, "Content"), "Audio"));
            if (refused != null) p.SourceRefusals.Add(refused);

            uint next = MediaIdBase;
            foreach (string f in Sources(root, "Audio", p.SourceRefusals, "*.wav", "*.ogg", "*.mp3"))
            {
                string why;
                ImportedAudio a = p.ImportAudio(f, ref next, out why);
                if (a == null) p.SourceRefusals.Add(Path.GetFileName(f) + " " + why + " - SKIPPED, the " +
                                                    "project's other sources are unaffected");
                else p.Audio.Add(a);
            }

            p.Replacements.AddRange(ReplacementFile.Load(root, p.ReplacementRefusals));
            // EVERY REFUSAL IS A FAILURE OF THIS RUN, not only the ones an importer THREW. An
            // unreadable .ogg, a .flac the tool never accepts, two files sharing a stem, a half-typed
            // "replace" row and a negative "scale" are all declared work that did not happen, and each
            // of them used to be printed and then reported under ALL PASS - which Route7.cs:249 reads
            // as permission to stamp the patch cache current. Counted ONCE, here, so an arm that adds
            // a line can never forget to add its count.
            p.ImportFailures = p.SourceRefusals.Count;
            return p;
        }

        /// <summary>Content\Videos\, verbatim: a video is copied, never decoded, so the whole
        /// "import" is what the file system already says. Shared by both loads.</summary>
        private static List<ImportedVideo> ImportVideos(string root)
        {
            List<ImportedVideo> list = new List<ImportedVideo>();
            // null: shared with LoadDeclared, which carries no refusal list at all - a video stem
            // collision keeps the throw it has always had rather than being dropped silently.
            foreach (string f in Sources(root, "Videos", null, "*.webm", "*.mp4", "*.mov"))
                list.Add(new ImportedVideo
                {
                    Name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant(),
                    Path = f,
                    Extension = Path.GetExtension(f).ToLowerInvariant()
                });
            return list;
        }

        /// <summary>
        /// Reads the "replace" array out of the raw ppcontent.json text.
        ///
        /// NOT JsonUtility. It parses "id" and "bundle" from the same file happily but returns null
        /// for an array of custom classes here - measured twice, with the class nested+internal and
        /// then top-level+public exactly as Unity's own docs show (the deployed DLL was confirmed to
        /// contain the top-level type, so it was not a stale build). Rather than keep guessing which
        /// of nesting, accessibility or the private container it objects to, this reads the three
        /// flat string fields directly. A declared entry can no longer parse to silence.
        /// </summary>
        /// <param name="refusals">Where an INCOMPLETE entry goes instead of ending the run. Null keeps
        /// the old throw, for the declaration-only load that has no refusal channel of its own: one
        /// half-typed row must not stop the project's other rows from baking (Load's own note).</param>
        private static List<ShippedReplacement> ParseReplace(string json, List<string> refusals = null)
        {
            List<ShippedReplacement> list = new List<ShippedReplacement>();
            Match arr = Regex.Match(json, "\"replace\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            if (!arr.Success) return list;
            int marked = refusals == null ? 0 : refusals.Count;

            foreach (Match o in Regex.Matches(arr.Groups[1].Value, "\\{[^{}]*\\}", RegexOptions.Singleline))
            {
                ShippedReplacement r = new ShippedReplacement
                {
                    bundle = Field(o.Value, "bundle"),
                    asset = Field(o.Value, "asset"),
                    texture = Field(o.Value, "texture"),
                    material = Field(o.Value, "material"),
                    mesh = Field(o.Value, "mesh"),
                    clip = Field(o.Value, "clip"),
                    video = Field(o.Value, "video")
                };
                int kinds = (string.IsNullOrEmpty(r.texture) ? 0 : 1) +
                            (string.IsNullOrEmpty(r.material) ? 0 : 1) +
                            (string.IsNullOrEmpty(r.mesh) ? 0 : 1) +
                            (string.IsNullOrEmpty(r.clip) ? 0 : 1) +
                            (string.IsNullOrEmpty(r.video) ? 0 : 1);
                // "bundle" is required for every kind that LIVES in a bundle. Video does not: the
                // cutscenes are loose files behind a side catalog, so a video entry that named one
                // would be naming something that does not exist.
                // ...and "asset" is required for everything but "video" too: a video entry with no
                // "asset" names no shipped clip because it ADDS one (see ShippedReplacement.video).
                bool needsBundle = string.IsNullOrEmpty(r.video);
                if ((needsBundle && string.IsNullOrEmpty(r.bundle)) ||
                    (needsBundle && string.IsNullOrEmpty(r.asset)) || kinds != 1)
                {
                    string why =
                        "\"replace\" row REFUSED: every entry needs exactly one of \"texture\", \"material\", " +
                        "\"mesh\", \"clip\" or \"video\", plus \"bundle\" and \"asset\" for everything but " +
                        "\"video\" (a \"video\" entry with no \"asset\" ADDS a new clip); got " + o.Value +
                        " - SKIPPED, this project's other rows still bake";
                    if (refusals == null) throw new InvalidDataException(why);
                    refusals.Add(why); continue;
                }
                list.Add(r);
            }
            if (list.Count == 0 && (refusals == null || refusals.Count == marked))
            {
                string why = "ppcontent.json declares \"replace\" but no complete entry was read from it";
                if (refusals == null) throw new InvalidDataException(why);
                refusals.Add(why);
            }
            return list;
        }

        /// <summary>One optional `"sounds"` entry: which shipped media, and which file of the
        /// author's own replaces it.</summary>
        internal sealed class SoundEntry
        {
            internal uint Media;
            internal string File;
        }

        /// <summary>
        /// The optional "sounds" array. The filename convention
        /// (<c>Content\Audio\Replace\&lt;mediaId&gt;.mp3</c>) still works and is still the lazy way to
        /// do ONE file; this exists because that convention spends its only slot on the target ID, so
        /// it destroys the author's own filename and leaves the project file unable to say what a
        /// track is. Declared entries keep both.
        ///
        /// Read the way "replace" and "publish" are, for the same measured reason (JsonUtility returns
        /// null for an array of custom classes here). "media" is a NUMBER, so it is read with its own
        /// pattern rather than through <see cref="Field"/>, and a quoted number is accepted too.
        /// </summary>
        /// <param name="refusals">Where an INCOMPLETE entry goes instead of ending the run - one
        /// half-typed row must not stop the project's other sounds from being replaced. Null keeps the
        /// old throw for a caller that has no refusal channel to write into.</param>
        internal static List<SoundEntry> ParseSounds(string json, List<string> refusals = null)
        {
            List<SoundEntry> list = new List<SoundEntry>();
            Match arr = Regex.Match(json, "\"sounds\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            if (!arr.Success) return list;
            int marked = refusals == null ? 0 : refusals.Count;

            foreach (Match o in Regex.Matches(arr.Groups[1].Value, "\\{[^{}]*\\}", RegexOptions.Singleline))
            {
                Match media = Regex.Match(o.Value, "\"media\"\\s*:\\s*\"?(\\d+)\"?");
                string file = Field(o.Value, "file");
                if (!media.Success || string.IsNullOrEmpty(file))
                {
                    string why =
                        "\"sounds\" row REFUSED: every entry needs \"media\" (the shipped media ID it " +
                        "replaces) and \"file\" (the name of your own file in Content\\Audio\\Replace\\); " +
                        "got " + o.Value + " - SKIPPED, this project's other sounds are unaffected";
                    if (refusals == null) throw new InvalidDataException(why);
                    refusals.Add(why); continue;
                }
                list.Add(new SoundEntry { Media = uint.Parse(media.Groups[1].Value), File = file });
            }
            if (list.Count == 0 && (refusals == null || refusals.Count == marked))
            {
                string why = "ppcontent.json declares \"sounds\" but no complete entry was read from it";
                if (refusals == null) throw new InvalidDataException(why);
                refusals.Add(why);
            }
            return list;
        }

        /// <summary>
        /// The "publish" array, read the same way "replace" is and for the same reason (JsonUtility
        /// returns null for an array of custom classes here). A declared entry can never parse to
        /// silence: an incomplete one is refused by name.
        /// </summary>
        /// <param name="refusals">Where an INCOMPLETE entry goes instead of ending the run. A "publish"
        /// row is irrelevant to a texture bake, and a half-typed one used to stop that bake dead.</param>
        private static List<PublishedKey> ParsePublish(string json, List<string> refusals = null)
        {
            List<PublishedKey> list = new List<PublishedKey>();
            Match arr = Regex.Match(json, "\"publish\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            if (!arr.Success) return list;
            int marked = refusals == null ? 0 : refusals.Count;

            foreach (Match o in Regex.Matches(arr.Groups[1].Value, "\\{[^{}]*\\}", RegexOptions.Singleline))
            {
                PublishedKey k = new PublishedKey
                {
                    key = Field(o.Value, "key"),
                    asset = Field(o.Value, "asset"),
                    type = Field(o.Value, "type"),
                    deps = Field(o.Value, "deps")
                };
                if (string.IsNullOrEmpty(k.key) || string.IsNullOrEmpty(k.asset))
                {
                    string why =
                        "\"publish\" row REFUSED: every entry needs \"key\" (the address the game will " +
                        "ask for) and \"asset\" (the path inside this mod's own bundle); got " + o.Value +
                        " - SKIPPED, this project's other keys and its sources still bake";
                    if (refusals == null) throw new InvalidDataException(why);
                    refusals.Add(why); continue;
                }
                list.Add(k);
            }
            if (list.Count == 0 && (refusals == null || refusals.Count == marked))
            {
                string why = "ppcontent.json declares \"publish\" but no complete entry was read from it";
                if (refusals == null) throw new InvalidDataException(why);
                refusals.Add(why);
            }
            return list;
        }

        /// <summary>
        /// ponytail: the value is taken VERBATIM, so a JSON <c>\uXXXX</c> escape arrives as those six
        /// characters rather than the character it denotes. Authors write ppcontent.json in UTF-8 and
        /// type the name itself ("Ублюдок, мать твою.mp3" works, spaces and commas and all); add a
        /// decoder here if an editor that emits escapes ever shows up.
        /// </summary>
        private static string Field(string obj, string name)
        {
            return Regex.Match(obj, "\"" + name + "\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
        }

        /// <summary>
        /// Sorted so an ID allocated for a file does not move when a sibling is added.
        ///
        /// A record is named by its file STEM, so two files that differ only in extension
        /// (swatch.png next to swatch.jpg) would both answer to "swatch" and the first one found
        /// would win silently. That is refused, by name, rather than resolved by a rule nobody can
        /// remember - and it is refused as a pair of SOURCES, not as the project: the collision used to
        /// throw out of Load before <see cref="SourceImport.Each"/> could contain it, so swatch.jpg
        /// beside swatch.png cost the author every mesh, model and sound in the project too. Both
        /// colliding files are left out (choosing one is exactly what this refuses to do) and the other
        /// kinds are untouched. A null <paramref name="refusals"/> keeps the old throw, for a caller
        /// with no refusal channel to write into.
        /// </summary>
        private static string[] Sources(string root, string folder, List<string> refusals,
                                        params string[] patterns)
        {
            string dir = Path.Combine(Path.Combine(root, "Content"), folder);
            if (!Directory.Exists(dir)) return new string[0];
            List<string> files = new List<string>();
            foreach (string pattern in patterns) files.AddRange(Directory.GetFiles(dir, pattern));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            int i = files.Count - 1;
            while (i > 0)
            {
                if (!string.Equals(Path.GetFileNameWithoutExtension(files[i]),
                                   Path.GetFileNameWithoutExtension(files[i - 1]),
                                   StringComparison.OrdinalIgnoreCase))
                { i--; continue; }
                string why = "Content\\" + folder + "\\ holds two files with the same name: " +
                             Path.GetFileName(files[i - 1]) + " and " + Path.GetFileName(files[i]) +
                             " - a replacement names the stem, so one of them has to go; BOTH were " +
                             "SKIPPED, the project's other sources are unaffected";
                if (refusals == null) throw new InvalidDataException(why);
                refusals.Add(why);
                files.RemoveAt(i);
                files.RemoveAt(i - 1);
                i -= 2;
            }
            return files.ToArray();
        }

        /// <summary>
        /// PNG through Unity's own decoder, then straight to RGBA32 - the live materializer and the
        /// bake writer both want exactly this, so there is no second decode path.
        /// Main thread only (Texture2D), which is where the console dispatches.
        /// </summary>
        private static ImportedTexture ImportTexture(string path)
        {
            Texture2D t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!t.LoadImage(File.ReadAllBytes(path)))
                    throw new InvalidDataException("Unity could not decode " + path + " as an image");
                Color32[] px = t.GetPixels32();
                byte[] rgba = new byte[px.Length * 4];
                for (int i = 0; i < px.Length; i++)
                {
                    rgba[i * 4] = px[i].r; rgba[i * 4 + 1] = px[i].g;
                    rgba[i * 4 + 2] = px[i].b; rgba[i * 4 + 3] = px[i].a;
                }
                return new ImportedTexture
                {
                    Name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant(),
                    Width = t.width, Height = t.height, Rgba32 = rgba
                };
            }
            finally { UnityEngine.Object.Destroy(t); }
        }

        /// <summary>
        /// .obj through the tool's own parser, straight to the buffers a serialized Mesh wants -
        /// no UnityEngine type takes part, which is what lets the same conversion be proven offline.
        ///
        /// A .glb comes through GlbSource.ReadReplacement - the ONE replacement read, so the alias
        /// sidecar that a preview applied applies here too (the ADD path, ImportModel, deliberately
        /// does NOT go through it: its published bone-path hashes must not depend on a file sitting
        /// next to the .glb). It additionally
        /// KEEPS the model: an .obj holds no skin, while a .glb holds the joint names, the bind poses
        /// and WEIGHTS_0 that let a rigged replacement carry the author's own weights instead of
        /// synthesised ones. Which of the two a replacement got is reported by the bake, never assumed.
        /// </summary>
        private static ImportedMesh ImportMesh(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if (!string.Equals(Path.GetExtension(path), ".glb", StringComparison.OrdinalIgnoreCase))
                return new ImportedMesh { Name = name, Baked = MeshBuild.From(ObjCodec.Parse(File.ReadAllText(path))) };

            ReplacementSource source = GlbSource.ReadReplacement(File.ReadAllBytes(path), path);
            return new ImportedMesh
            {
                Name = name,
                Baked = ModelBuild.From(source.Model, name).Mesh,
                Model = source.Model,
                SidecarPath = source.SidecarPath,
                AliasesApplied = source.AliasesApplied,
                SidecarRefusal = source.SidecarRefusal,
                UnusedAliasKeys = source.UnusedAliasKeys
            };
        }

        /// <summary>
        /// .glb through the tool's own reader - the same one the extractor's round trip already
        /// exercises, so an author's loop is: extract a shipped model as .glb, edit it in Blender,
        /// drop it back in. A .glb is the one interchange file that carries the node hierarchy, the
        /// mesh, the skin weights AND the bind poses, which is exactly what a model this bake ADDS
        /// needs and what an .obj has none of.
        ///
        /// It also carries the model's own ANIMATION CLIPS (U8/U9), which is why the reader is handed a
        /// list to fill: a file with no "animations" leaves it empty and the model bakes exactly as it
        /// did before.
        /// </summary>
        private static ImportedModel ImportModel(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            ImportedModel imported = new ImportedModel { Name = name };
            imported.Baked = ModelBuild.From(GlbReader.Read(File.ReadAllBytes(path), imported.Clips), name);
            return imported;
        }

        /// <summary>
        /// The scale a project DECLARES, or 1 with a refusal saying why. A negative one threw
        /// InvalidDataException straight out of <see cref="Load"/>, so ONE mistyped number - a row of
        /// the manifest, nothing more - cost the author every texture, mesh, model and sound in the
        /// project, and the run ended in "ct_project THREW" with no per-row line and no summary.
        /// It is refused by name and the bake goes on at 1, which is what every project that never
        /// declared one already uses.
        ///
        /// Carries no UnityEngine type on purpose - the arrangement <see cref="SourceImport"/> uses -
        /// so the clamp is proven in ObjCodecTests rather than by watching a bake die.
        /// </summary>
        internal static float ScaleOrRefuse(float declared, List<string> refusals)
        {
            if (declared < 0f)
            {
                refusals.Add("ppcontent.json \"scale\": " + declared + " is negative - it is the uniform " +
                             "scale the mod puts on the rig root, so it has to be a positive number; " +
                             "SKIPPED, this bake used scale 1 (the model arrives at its file's own size)");
                return 1f;
            }
            return declared > 0f ? declared : 1f;
        }

        /// <summary>
        /// The accepted set is a WHITELIST - .wav, .ogg, .mp3 - and everything else in Content\Audio\
        /// is refused BY NAME, with the cause, before anything is decoded. Never a silent empty bank.
        ///
        /// A whitelist rather than a list of known-bad extensions: .opus/.wma/.aac would have joined
        /// .flac and .m4a one forgotten line at a time. The three accepted formats cost 70.5 KB of
        /// merged NLayer between them (NVorbis was already here for .wem extraction); a fourth codec
        /// would be another vendored library for a format every editor can already export out of.
        ///
        /// The refusal is a REPORT, not an abort: Load collects it into SourceRefusals, so a stray
        /// .flac in the folder is named out loud and the project's models still bake.
        /// </summary>
        internal static string RefuseUnsupported(string audioDir)
        {
            if (!Directory.Exists(audioDir)) return null;
            List<string> bad = new List<string>();
            foreach (string f in Directory.GetFiles(audioDir))
            {
                string e = Path.GetExtension(f).ToLowerInvariant();
                if (e != ".wav" && e != ".ogg" && e != ".mp3") bad.Add(Path.GetFileName(f));
            }
            if (bad.Count == 0) return null;
            bad.Sort(StringComparer.OrdinalIgnoreCase);
            return "Content\\Audio\\ holds " + bad.Count + " file(s) this tool does not import: " +
                   string.Join(", ", bad.ToArray()) + " - the accepted set is .wav, .ogg and .mp3, " +
                   "which this tool decodes itself at bake time. Export the rest to .ogg (small) or " +
                   ".wav (lossless); no decoder for .flac, .m4a/.aac, .wma or .opus is going into this " +
                   "tool (docs\\research-format-coverage.md 2.1).";
        }

        /// <summary>
        /// ONE pipeline and one decoder, whatever the container: <see cref="WwisePcm.ReadAudio"/>
        /// reads .wav, .ogg and .mp3 in-house, and all three go into the same
        /// <see cref="WwisePcm.BuildWem"/>. The imported record carries what came out AND what the
        /// source declared, so the bake can assert an identity rather than a presence.
        ///
        /// Returns null with <paramref name="why"/> instead of throwing: one unreadable file is a
        /// skipped source, not a dead bake.
        /// </summary>
        private ImportedAudio ImportAudio(string path, ref uint nextId, out string why)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            WwisePcm.Wav w = WwisePcm.ReadAudio(path, out why);
            if (w == null) return null;
            int channels = w.Channels, rate = w.SampleRate;
            byte[] pcm16 = w.Pcm16;
            // BuildWem REFUSES a layout it cannot honestly name (3+ channels) rather than placing the
            // source wrongly. That refusal is about ONE file, so it is caught here and reported as a
            // skip - a 5.1 .ogg in the folder must not take the project's models down with it.
            byte[] wem;
            try { wem = WwisePcm.BuildWem(pcm16, channels, rate); }
            catch (ArgumentException ex) { why = "cannot be packaged: " + ex.Message; return null; }

            string name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            bool stream = name.EndsWith(".stream", StringComparison.Ordinal);
            if (stream) name = name.Substring(0, name.Length - ".stream".Length);

            string declaredWhy;
            return new ImportedAudio
            {
                Name = Id.ToLowerInvariant().Replace('.', '_') + "_" + name,
                Wem = wem,
                Stream = stream,
                MediaId = Allocate(ref nextId),
                Channels = channels,
                SampleRate = rate,
                Frames = pcm16.Length / (2 * channels),
                Peak = Peak(pcm16),
                SourceFile = Path.GetFileName(path),
                Extension = ext,
                Declared = SourceAudio.Declare(File.ReadAllBytes(path), ext, out declaredWhy),
                DeclaredWhy = declaredWhy
            };
        }

        /// <summary>Loudest sample, 0..1 - a correctly sized buffer of silence must not pass for audio.</summary>
        private static float Peak(byte[] pcm16)
        {
            int peak = 0;
            for (int i = 0; i + 1 < pcm16.Length; i += 2)
            {
                int s = (short)(pcm16[i] | pcm16[i + 1] << 8);
                if (s < 0) s = -s;
                if (s > peak) peak = s;
            }
            return peak / 32768f;
        }

        /// <summary>
        /// Media IDs are allocated, so the only correctness question is whether the number is free:
        /// skip anything Phoenix Point owns, and anything this project already took. The bake-time
        /// validator re-checks the whole matrix (FINAL-PLAN 9.4) - this just never hands it a
        /// number that is already known to be taken.
        /// </summary>
        private uint Allocate(ref uint next)
        {
            while (true)
            {
                uint id = next++;
                if (IdIndex.IsPpMedia(id)) continue;
                bool mine = false;
                foreach (ImportedAudio a in Audio) if (a.MediaId == id) { mine = true; break; }
                if (!mine) return id;
            }
        }
    }
}
