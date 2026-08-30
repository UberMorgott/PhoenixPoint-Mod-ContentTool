using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace Morgott.ContentTool.Tactical
{
    /// <summary>
    /// KEEPS A FOREIGN MODEL'S OWN PROPORTIONS WHILE IT PLAYS THE GAME'S OWN ANIMATIONS.
    ///
    /// A PP rig is Unity GENERIC. A generic clip binds a curve to the CRC-32 of a bone's relative
    /// transform PATH, so once tools/ppskel.py has renamed a foreign rig onto PP's exact paths the
    /// game's installed clips drive it with NOTHING shipped - which is what
    /// <c>"useGameAnimations": true</c> buys, and it is worth 89,281,672 B of animation on the
    /// humanoid demo alone (measured: 300 clips, 29,082 sampler accessors, of a 104,511,576 B file).
    ///
    /// What it costs, and what this component pays: Unity's generic bake writes a POSITION curve on
    /// every bound bone whose value is PP's OWN rest offset, held constant for the whole clip. Play
    /// that raw and the clip does not merely animate the model, it PINS PP's segment lengths onto
    /// it. On the humanoid demo tools/ppretarget-report.json measures her segments at 0.487x to
    /// 2.143x PP's over 56 segments (Head 2.143x, Spine_2 1.985x, gun_point_hand 0.487x), so raw
    /// vanilla clips would serve her with a head at 47% of its size. That is the one thing the
    /// author refused to give up, so it is corrected rather than accepted.
    ///
    /// THE CORRECTION IS ONE VECTOR ADD PER BONE:
    ///
    ///     localPosition := animatedPosition + (herRest - ppRest)
    ///
    /// and it is exactly right on both kinds of curve, which is why there is no list of special
    /// bones anywhere in this file. On a PINNING curve - the constant kind, 95.3% of the values
    /// ClipCensus measured - animatedPosition IS ppRest, so the sum is herRest and the bone sits
    /// where her own rig puts it. On a curve that genuinely MOVES (root travel, the pelvis rising
    /// through a crouch) the delta is a CONSTANT, so it cancels out of every displacement the clip
    /// expresses: end - start is preserved to the bit, and the motion is simply re-expressed in her
    /// frame. Scaling the amplitude instead - herRest + ratio * (pos - ppRest) - is the tempting
    /// wrong answer: it would stretch the travel of a clip that was authored in metres.
    ///
    /// WHERE IT RUNS, AND WHY NOT LATER. <see cref="Base.Utils.Animations.RootMotion"/> is on the
    /// Animator's own GameObject on every actor - TacticalActorBase.cs:589-593 adds it if the prefab
    /// lacks one, the same lines that add the AnimEventReceiver at :596 - and its OnAnimatorMove is
    /// an ENGINE-DEFINED phase that runs after Mecanim has written the bones, before OnAnimatorIK,
    /// and before every LateUpdate in the scene. So the corrected pose is the only one anything else
    /// ever sees: the aim solver feed (Base.Utils/AimIKCharacterAiming.cs:29-44) and the camera rig
    /// (com.ootii.Cameras/BaseCameraRig.cs:286-320) both read in LateUpdate, after this.
    ///
    /// A LateUpdate of our own was the obvious seam and it is the wrong one twice over. Hooking
    /// FinalIK's SolverManager.LateUpdate would never fire at all: TacticalActor.InitIK:2042-2050
    /// only FINDS an existing AimIK, a .glb creature has none, so no SolverManager instance exists -
    /// the correction would silently never apply. And an ordinary LateUpdate component sits in the
    /// same undefined execution-order bucket as the two readers above.
    /// </summary>
    internal sealed class NativeBones : MonoBehaviour
    {
        /// <summary>Bones whose rest differs from PP's. A bone that agrees is not listed: the clip
        /// already writes the right value for it, so correcting it would be a write for nothing.</summary>
        internal Transform[] Bones;

        /// <summary><c>herRest - ppRest</c>, parallel to <see cref="Bones"/>.</summary>
        internal Vector3[] Delta;

        /// <summary>What this component last WROTE, per bone. Not bookkeeping - see Apply.</summary>
        private Vector3[] written;

        /// <summary>
        /// Correct one frame's pose. Called from the OnAnimatorMove postfix below.
        ///
        /// The guard is load-bearing and not an optimisation. TacticalActorBase.cs:594 sets
        /// <c>Animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms</c>, so an off-screen
        /// actor keeps evaluating its state machine and applying root motion - OnAnimatorMove still
        /// fires - while writing NO bone transforms. Adding the delta to a value this component
        /// itself wrote last frame would walk the bone away from the rig a frame at a time, and the
        /// symptom would be a model that slowly comes apart while the camera is not looking at it.
        /// Comparing against what we wrote is the whole fix.
        ///
        /// The array is seeded with NaN, not left at Vector3.zero, and that is not tidiness. A great
        /// many of these bones sit at the origin in PP's rest pose, so their animated localPosition is
        /// EXACTLY (0,0,0) every frame; against a zero-filled array the very first comparison would
        /// match, the bone would be skipped forever and it would keep PP's proportions - the one
        /// failure this whole component exists to prevent, on precisely the bones most likely to
        /// differ. NaN equals nothing, including itself, so the first frame always writes.
        ///
        /// ponytail: Vector3's == is an epsilon compare, so a frame in which the Animator happens to
        /// write EXACTLY the previous corrected value is skipped and that bone lags by delta for one
        /// frame before healing. It needs the animator to land on a value it can only reach by
        /// coincidence; a per-bone "last animated value" array would close it for a second array's
        /// worth of memory, and is the upgrade if it is ever seen.
        /// </summary>
        internal void Apply()
        {
            Transform[] bones = Bones;
            Vector3[] delta = Delta;
            if (bones == null || delta == null) return;
            if (written == null || written.Length != bones.Length)
            {
                written = new Vector3[bones.Length];
                for (int i = 0; i < written.Length; i++)
                    written[i] = new Vector3(float.NaN, float.NaN, float.NaN);
            }
            for (int i = 0; i < bones.Length; i++)
            {
                Transform bone = bones[i];
                if (bone == null) continue;
                Vector3 animated = bone.localPosition;
                if (animated == written[i]) continue;
                Vector3 corrected = animated + delta[i];
                written[i] = corrected;
                bone.localPosition = corrected;
            }
        }

        /// <summary>
        /// Measure <c>herRest - ppRest</c> off the two rigs and hang the result on
        /// <paramref name="ours"/>, which is the inactive TEMPLATE every actor is instantiated from -
        /// so the arrays are built ONCE and Unity's Instantiate remaps the Transform references into
        /// each clone's own hierarchy for free.
        ///
        /// Both rests are read LIVE, off the game's own donor rig prefab and off her imported
        /// skeleton, and nothing about either is shipped or written down. That is deliberate: a
        /// table of PP's rest offsets in the manifest would be a copy of game data that goes stale
        /// the first time the game is patched, and this cannot.
        /// </summary>
        internal static void Attach(GameObject ours, GameObject theirs, System.Action<string> say)
        {
            if (ours == null) return;
            if (theirs == null)
            {
                say("ct_creature FAIL \"useGameAnimations\" is on but the donor has no rig to measure " +
                    "PP's rest pose against, so the game's clips would pin PP's proportions onto this " +
                    "model uncorrected. Name a donor whose chassis has a rig.");
                return;
            }

            Dictionary<string, Transform> mine = Paths(ours.transform);
            Dictionary<string, Transform> pp = Paths(theirs.transform);

            var bones = new List<Transform>();
            var delta = new List<Vector3>();
            var absent = new List<string>();
            int agreed = 0;
            foreach (KeyValuePair<string, Transform> theirBone in pp)
            {
                Transform ourBone;
                if (!mine.TryGetValue(theirBone.Key, out ourBone)) { absent.Add(theirBone.Key); continue; }
                // BY IDENTITY, not by name. The Animator's own transform is the EMPTY relative path
                // and root motion binds to CRC32("") - tools/ppskel.py:190-193 says so and keeps the
                // row for exactly that reason. The engine owns that transform: RootMotion.cs:22-25
                // writes it itself when applyOffset is set, and TacticalActor drives the actor's
                // world position through it. Correcting it would fight both.
                if (ourBone == ours.transform) continue;
                Vector3 d = ourBone.localPosition - theirBone.Value.localPosition;
                // A bone that already agrees with PP needs no write: the clip's own value is hers.
                if (d.sqrMagnitude <= 1e-12f) { agreed++; continue; }
                bones.Add(ourBone);
                delta.Add(d);
            }

            if (bones.Count > 0)
            {
                NativeBones fix = ours.AddComponent<NativeBones>();
                fix.Bones = bones.ToArray();
                fix.Delta = delta.ToArray();
            }

            string n(int v) { return v.ToString(CultureInfo.InvariantCulture); }
            say("ct_creature " + (absent.Count == 0 ? "PASS" : "WARN") + " \"useGameAnimations\": this " +
                "model plays the GAME'S clips and ships none. Of " + n(pp.Count) + " transform path(s) on " +
                "the donor rig, " + n(bones.Count) + " differ from hers and are corrected every frame, " +
                n(agreed) + " already agree" +
                (absent.Count == 0
                    ? " and every one has a counterpart here."
                    : " and " + n(absent.Count) + " have NO counterpart in this model, so any curve bound " +
                      "to them drives nothing and that part of the body will not move: " +
                      string.Join(", ", absent.GetRange(0, absent.Count < 12 ? absent.Count : 12).ToArray()) +
                      (absent.Count > 12 ? ", ..." : "") + ". Re-run tools/ppskel.py against this donor."));
        }

        /// <summary>Every transform under <paramref name="root"/> by its path RELATIVE to it - the
        /// same string a generic clip takes the CRC-32 of, and the root itself as the empty path.
        /// A duplicate path is the file's own ambiguity; the first wins, as a clip binding would.</summary>
        private static Dictionary<string, Transform> Paths(Transform root)
        {
            var found = new Dictionary<string, Transform>();
            Walk(root, "", found);
            return found;
        }

        private static void Walk(Transform at, string path, Dictionary<string, Transform> into)
        {
            if (!into.ContainsKey(path)) into.Add(path, at);
            for (int i = 0; i < at.childCount; i++)
            {
                Transform child = at.GetChild(i);
                Walk(child, path.Length == 0 ? child.name : path + "/" + child.name, into);
            }
        }
    }

    /// <summary>
    /// The tick. Postfixing the game's own OnAnimatorMove rather than adding a LateUpdate is what
    /// puts the correction in the engine's animation phase - see the note on <see cref="NativeBones"/>.
    /// The component is on the Animator's GameObject on EVERY actor in the game, ours and vanilla
    /// alike, so the lookup here has to be cheap and has to answer "not ours" for almost every call:
    /// one GetComponent against a rig that never has the component is a few hundred nanoseconds
    /// against the ~19 us the correction itself costs on a 214-bone rig.
    /// </summary>
    [HarmonyPatch(typeof(Base.Utils.Animations.RootMotion), "OnAnimatorMove")]
    internal static class NativeBonesTick
    {
        private static void Postfix(Base.Utils.Animations.RootMotion __instance)
        {
            NativeBones fix = __instance.GetComponent<NativeBones>();
            if (fix != null) fix.Apply();
        }
    }
}
