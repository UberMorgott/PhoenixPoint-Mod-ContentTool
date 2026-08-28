using System.Collections.Generic;
using Morgott.ContentTool.Tactical;
using PhoenixPoint.Modding;
using PhoenixPoint.Tactical.Entities.Weapons;

namespace Morgott.WeaponAdd
{
    /// <summary>
    /// ============ THREE WEAPONS PHOENIX POINT DOES NOT SHIP, IN THE BASE ON DAY ONE ============
    ///
    /// THIS FILE IS THE WHOLE MOD, and it is one call. Everything a new weapon needs - cloning a
    /// shipped def, its view and blurb and inventory icon, pointing a skin at a prefab this mod
    /// published, fitting the four EXT_ sockets, deep-copying the damage payload so the player's own
    /// weapon is not re-tuned, and putting the result in the base's starting storage - is MECHANISM,
    /// and mechanism lives in the ContentTool engine (<see cref="WeaponBuild"/>) where every weapon
    /// mod shares it.
    ///
    /// What is left is data, and the data is in ppcontent.json. Read that next; it is the
    /// interesting file.
    ///
    /// THE WORKFLOW:
    ///
    ///   1. Pick the SHIPPED weapon to clone.  Not by taste - by CLASS. Phoenix Point picks a
    ///                                         soldier's hold pose and firing animation set off the
    ///                                         weapon's tags, so a pistol cloned from a rifle is held
    ///                                         like a rifle. Match the silhouette to the class and
    ///                                         that problem cannot happen.
    ///   2. Add an entry to "weapons".         id, name, clone, guid. That is the minimum.
    ///   3. Change the numbers you care about. "damage" and "spread" override the clone's; leave
    ///                                         either out and the shipped value stands.
    ///   4. OPTIONAL: give it your own model.  Publish a prefab key in "publish", name it in
    ///                                         "model", and give the three socket positions the
    ///                                         demo's fit script DERIVES for you. Leave "model" out
    ///                                         and the weapon wears the art of the gun it cloned -
    ///                                         which is the honest state for a model that has not
    ///                                         been through Blender yet.
    ///
    /// WHY THEY EXIST AT CAMPAIGN START: a new campaign fills the base from
    /// GameDifficultyLevelDef.StartingStorage (GameDifficultyLevelDef.cs:43), so appending to it puts
    /// these guns in the player's hands before they have done anything. That is a decision about the
    /// campaign rather than about any one weapon, so the engine does it for every declared entry and
    /// reads back what it wrote.
    /// </summary>
    public class WeaponAddMain : ModMain
    {
        public override bool CanSafelyDisable => true;

        /// <summary>What the engine built, kept only so a failure is visible in the log line.</summary>
        internal static List<WeaponDef> Weapons;

        /// <summary>
        /// OnModEnabled, not ModMain.ApplyDefRepoPatches: the running game has that hook
        /// (ModManager.cs:673) but the shipped ModSDK\Assembly-CSharp.dll stub does not declare it,
        /// so the override does not compile. Mods start from PhoenixGame.FirstRunCrt -> InitMods()
        /// (PhoenixGame.cs:758), well after the defs are loaded and long before a campaign is made.
        /// </summary>
        public override void OnModEnabled()
        {
            Weapons = WeaponBuild.Build(Instance.Entry.Directory, m => Logger.LogInfo(m));
        }
    }
}
