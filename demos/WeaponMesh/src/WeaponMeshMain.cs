using System;
using System.IO;
using System.Reflection;
using Base.Core;
using Base.Defs;
using PhoenixPoint.Common.UI;
using PhoenixPoint.Modding;
using UnityEngine;

namespace Morgott.WeaponMesh
{
    /// <summary>
    /// The demo's SECOND half. The first half - the 3D model and its five textures - is pure
    /// content: six "replace" rows in ppcontent.json, installed by ContentTool's route vii, with no
    /// code running while the player plays.
    ///
    /// This file exists because the INVENTORY CELL is not the model. Phoenix Point draws a
    /// PRE-RENDERED Sprite there, taken off the item's def:
    ///
    ///   UIInventorySlot.cs:445        ImageNode.sprite = _item.ItemDef.ViewElementDef.InventoryIcon
    ///   UIInventoryItemDragIcon.cs:68 SetIcon(itemDef.ViewElementDef.InventoryIcon)
    ///   ItemDef.cs:228-236            GetSmallIcon()/GetLargeIcon() -> ViewElementDef?.InventoryIcon
    ///   ViewElementDef.cs:27/33       public Sprite SmallIcon; public Sprite InventoryIcon;
    ///
    /// so a mesh swap alone leaves the cell showing the old gun's picture. There is no content route
    /// to that Sprite either: `UI_PX_WeaponIcon_AssaultRifle_INV` lives in
    /// PhoenixPointWin64_Data\sharedassets0.assets, which is a Unity serialized file and NOT an
    /// Addressables bundle - it has no row in catalog.json, so route vii (which repoints an
    /// internalId) has nothing to aim at. The def field is the seam the game itself uses, so that is
    /// the seam this takes.
    ///
    /// Consequence, and it is a GOOD one: unlike the mesh half, this is undone by switching the mod
    /// off. Nothing is written into the install.
    /// </summary>
    public class WeaponMeshMain : ModMain
    {
        /// <summary>
        /// The Ares AR-1's view def. Its name is the def's own, verbatim - `PX_AssaultRifle_WeaponDef`
        /// points at `E_View [PX_AssaultRifle_WeaponDef]`
        /// (extracted\GameData\defs\WeaponDef\PX_AssaultRifle_WeaponDef.json, field ViewElementDef),
        /// and that def carries InventoryIcon and SmallIcon as the SAME shipped sprite guid
        /// 819abb6b55732ee4ba6d1c3cb907bcca, which is why both are written below.
        /// </summary>
        private const string ViewDefName = "E_View [PX_AssaultRifle_WeaponDef]";
        private const string IconFile = "Icons\\rifle_inv.png";

        public override bool CanSafelyDisable => true;

        /// <summary>Held so Unity cannot collect the texture behind a live Sprite.</summary>
        private static Sprite icon;

        /// <summary>
        /// OnModEnabled, and not ModMain.ApplyDefRepoPatches(DefRepository) - which is the hook the
        /// RUNNING game has (ModManager.cs:673 calls it for every mod) but the SHIPPED ModSDK stub
        /// does not: `ModMain` in ModSDK\Assembly-CSharp.dll declares eight virtuals and that is not
        /// one of them, so overriding it does not compile. Measured, not assumed.
        ///
        /// The repository is fully populated here: mods are started from PhoenixGame.FirstRunCrt ->
        /// InitMods() (PhoenixGame.cs:758), long after the def assets are loaded, and this is the same
        /// point at which TFTV rewrites hundreds of defs.
        /// </summary>
        public override void OnModEnabled()
        {
            try { Logger.LogInfo("W1-icon " + Swap(GameUtl.GameComponent<DefRepository>())); }
            catch (Exception ex) { Logger.LogError("W1-icon FAIL threw " + ex); }
        }

        private string Swap(DefRepository repo)
        {
            string path = Path.Combine(Instance.Entry.Directory, IconFile);
            if (!File.Exists(path))
                return "FAIL no icon at " + path + " - the mesh still swaps, the cell keeps the Ares' picture";

            ViewElementDef view = null;
            foreach (ViewElementDef d in repo.GetAllDefs<ViewElementDef>())
                if (d != null && d.name == ViewDefName) { view = d; break; }
            if (view == null)
                return "FAIL '" + ViewDefName + "' is not in the def repository - the game changed the def's name";

            string why;
            Sprite s = Load(path, out why);
            if (s == null) return "FAIL " + why;

            icon = s;
            view.InventoryIcon = s;
            view.SmallIcon = s;
            return "PASS " + ViewDefName + ".InventoryIcon and .SmallIcon now draw " + IconFile +
                   " (" + s.texture.width + "x" + s.texture.height + ")";
        }

        /// <summary>
        /// PNG -> Sprite, the same two calls TFTV's Helper.CreateSpriteFromImageFile makes
        /// (refs\TFTV-src\TFTV\Helper.cs:167-177): Texture2D + ImageConversion.LoadImage, then
        /// Sprite.Create over the whole texture.
        ///
        /// LoadImage is reached BY REFLECTION, not by referencing UnityEngine.ImageConversionModule.
        /// See the note in WeaponMesh.csproj: a compile-time reference to a Managed\ module outside
        /// ModSDK is what made a sibling demo fail to load, and a failed load disables every mod.
        /// </summary>
        private static Sprite Load(string path, out string why)
        {
            why = null;
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Type conv = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
            MethodInfo load = conv == null
                ? null
                : conv.GetMethod("LoadImage", BindingFlags.Public | BindingFlags.Static, null,
                                 new[] { typeof(Texture2D), typeof(byte[]) }, null);
            if (load == null)
            {
                why = "UnityEngine.ImageConversion.LoadImage was not found - this install's Unity differs";
                return null;
            }
            if (!(bool)load.Invoke(null, new object[] { tex, File.ReadAllBytes(path) }))
            {
                why = path + " is not a PNG Unity can decode";
                return null;
            }
            // Pivot centred and the full rect: the shipped weapon icons are whole-sheet sprites
            // (measured: UI_PX_WeaponIcon_AssaultRifle_INV is Rect 450x450 at 0,0), and a UI Image
            // draws the rect, so this matches what the cell already expects.
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }
    }
}
