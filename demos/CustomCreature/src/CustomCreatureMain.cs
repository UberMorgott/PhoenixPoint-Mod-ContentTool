using System;
using HarmonyLib;
using Morgott.ContentTool.Tactical;
using PhoenixPoint.Geoscape.Levels;
using PhoenixPoint.Geoscape.Levels.Factions;
using PhoenixPoint.Modding;
using PhoenixPoint.Tactical.Entities;

namespace Morgott.CustomCreature
{
    /// <summary>
    /// ============ A CREATURE OFF THE INTERNET JOINS YOUR SQUAD ============
    ///
    /// THIS FILE IS THE WHOLE MOD. Everything a creature needs in order to walk, turn, attack, take a
    /// hit and die is MECHANISM, and mechanism lives in the ContentTool engine
    /// (<see cref="CreatureBuild"/>) where every creature mod shares it. What is left here is the two
    /// things that are genuinely THIS mod's: where the creature comes from, and what happens to it.
    ///
    /// THE WORKFLOW, which is the point of the demo:
    ///
    ///   1. Drop the model in.       Content\Models\spider.glb - any .glb with a skeleton.
    ///   2. Declare a creature.      Put "creature": {} in ppcontent.json. That is the opt-in.
    ///   3. Bake once.               `ct_project CustomCreature` in the console. The tool reads the
    ///                               file, WRITES every animation it found into your ppcontent.json,
    ///                               prints what it MEASURED (span, the scale that makes it one tile,
    ///                               bone count, where its feet are) and then REFUSES the bake while a
    ///                               required role is still unmapped. It will not guess which clip is
    ///                               the walk: glTF has no such flag, and a wrong guess puts an
    ///                               event-less clip in the attack state, which is a ten-second stall
    ///                               per swing that reads like the GAME hanging.
    ///   4. Fill in the roles.       Beside each clip name, write walk / idle / attack / death / jump.
    ///                               While you are in there, set the numbers you care about - scale,
    ///                               lift, health, speed, and the frames your attack and death
    ///                               animations actually connect on. ONE file, all of it.
    ///   5. Bake again, and play.
    ///
    /// So the interesting file in this mod is not this one - it is ppcontent.json. Read that next.
    ///
    /// WHAT IS STILL A CHOICE HERE, and why it cannot be a manifest key: a creature has to get into the
    /// game SOMEHOW, and that is a decision about the campaign, not about the model. This demo puts it
    /// in the player's starting squad; a different mod would hand it to a faction's deployment list or
    /// spawn it from an ability. So this file holds exactly that: two triggers and one call.
    ///
    /// WHY BOTH TRIGGERS. The game has TWO squad builders and which one runs depends on whether the
    /// player took the tutorial. GeoPhoenixFaction.CreateInitialSquad:1964-1976 is the obvious one;
    /// GeoscapeTutorial.InitSquad:289-330 is the one that quietly replaces it, and it reads
    /// StartingSquadTemplate for its LENGTH only (:313) before filling the gap with a FIXED human
    /// template - which is why appending to that array produces an extra soldier and never a spider.
    /// <see cref="CreatureBuild.JoinPlayerVehicle"/> instead adds the unit to the aircraft AFTER
    /// whichever builder ran, and then reads the roster back to prove it is there.
    /// </summary>
    public class CustomCreatureMain : ModMain
    {
        public override bool CanSafelyDisable => true;

        /// <summary>The creature the engine built, handed to the two squad triggers below.</summary>
        internal static TacCharacterDef Spider;

        /// <summary>
        /// All the def work happens once, at mod load: the defs are already in the repository by then
        /// (this is where TFTV does its own def injection too) and a campaign is only started later
        /// from the main menu, so this is always in time.
        ///
        /// ModMain also declares ApplyDefRepoPatches(DefRepository), which reads like the tidier seam -
        /// but it does NOT exist in the shipped ModSDK\Assembly-CSharp.dll a mod compiles against, only
        /// in the live game assembly. OnModEnabled is the portable one.
        ///
        /// Build never throws: Phoenix Point answers a failed mod load by rewriting MOD_ACTIVATED
        /// empty, which silently disables every OTHER mod too. It logs why and returns null instead.
        /// </summary>
        public override void OnModEnabled()
        {
            Spider = CreatureBuild.Build(Instance.Entry.Directory, m => Logger.LogInfo(m));
            // The two squad triggers below. The engine patches its OWN assembly for the creature's
            // animation and pose seams; these two are this mod's, so this mod applies them.
            ((Harmony)HarmonyInstance).PatchAll(System.Reflection.Assembly.GetExecutingAssembly());
        }
    }

    [HarmonyPatch(typeof(GeoPhoenixFaction), nameof(GeoPhoenixFaction.CreateInitialSquad))]
    internal static class SpiderJoinsNewCampaign
    {
        private static void Postfix()
        {
            CreatureBuild.JoinPlayerVehicle(CustomCreatureMain.Spider, "CreateInitialSquad");
        }
    }

    /// <summary>Private method, patched by name - GeoscapeTutorial.InitSquad:289-330.</summary>
    [HarmonyPatch(typeof(GeoscapeTutorial), "InitSquad")]
    internal static class SpiderJoinsTutorialSquad
    {
        private static void Postfix()
        {
            CreatureBuild.JoinPlayerVehicle(CustomCreatureMain.Spider, "Tutorial.InitSquad");
        }
    }
}
