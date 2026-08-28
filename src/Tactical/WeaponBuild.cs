using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Base.Core;
using Base.Defs;
using Base.UI;
using PhoenixPoint.Common.Core;
using PhoenixPoint.Common.Entities.Items;
using PhoenixPoint.Common.Entities.Items.SkinData;
using PhoenixPoint.Common.UI;
using PhoenixPoint.Tactical.Entities;
using PhoenixPoint.Tactical.Entities.Animations;
using PhoenixPoint.Tactical.Entities.DamageKeywords;
using PhoenixPoint.Tactical.Entities.Effects.DamageTypes;
using PhoenixPoint.Tactical.Entities.Equipments;
using PhoenixPoint.Tactical.Entities.Weapons;
using Morgott.ContentTool.Bake;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Morgott.ContentTool.Tactical
{
    /// <summary>
    /// ============ WEAPONS A CONTENT MOD ADDS, BUILT FROM A MANIFEST ============
    ///
    /// The <see cref="CreatureBuild"/> analogue, and it exists for the same reason: adding a SECOND
    /// weapon to the demo would have copy-pasted the first one's wiring, and wiring that gets
    /// copy-pasted is mechanism wearing a demo's clothes. Everything here is the same for every
    /// weapon anybody will ever add; what differs is data, and data lives in the mod's own
    /// <c>ppcontent.json</c> under a <c>"weapons"</c> array.
    ///
    /// CLONE, NEVER HAND-TYPE. Each entry names a SHIPPED weapon to clone, and the clone is what
    /// makes the weapon turn-key: equip slot, holster, abilities, ammunition, tags, Wwise switch,
    /// muzzle flash and tracer all arrive already correct. The entry then overrides the handful of
    /// things that are genuinely new.
    ///
    /// PICK THE CLONE SOURCE BY WEAPON CLASS, not by taste. Phoenix Point chooses a soldier's hold
    /// pose and firing animation set off the weapon's TAGS, so a pistol cloned from a rifle is held
    /// like a rifle. Match the silhouette to the shipped class and that problem cannot occur.
    ///
    /// A "model" is OPTIONAL. With one, the weapon wears the mod's own baked prefab and this class
    /// fits the four EXT_ sockets onto it. Without one, the clone keeps the SHIPPED weapon's
    /// SkinData and looks like the gun it was cloned from - which is the honest state for a weapon
    /// whose .glb has not been through Blender yet, and is strictly better than a weapon that holds
    /// nothing.
    /// </summary>
    public static class WeaponBuild
    {
        /// <summary>
        /// Reads the mod's manifest and builds every weapon it declares. NEVER THROWS: Phoenix Point
        /// answers a failed mod load by rewriting MOD_ACTIVATED empty, which silently disables every
        /// OTHER mod the player has. It reports and returns what it managed instead.
        /// </summary>
        public static List<WeaponDef> Build(string modDir, Action<string> log)
        {
            List<WeaponDef> built = new List<WeaponDef>();
            try
            {
                string meta = Path.Combine(modDir, "ppcontent.json");
                if (!File.Exists(meta)) { log("ct_weapon VOID no ppcontent.json in '" + modDir + "'"); return built; }

                List<Entry> entries = Parse(File.ReadAllText(meta));
                if (entries.Count == 0) { log("ct_weapon VOID ppcontent.json declares no \"weapons\" block"); return built; }

                DefRepository repo = GameUtl.GameComponent<DefRepository>();
                foreach (Entry e in entries)
                {
                    try
                    {
                        WeaponDef def = One(repo, modDir, e, log);
                        if (def != null) built.Add(def);
                    }
                    catch (Exception ex)
                    {
                        log("ct_weapon FAIL '" + e.id + "' threw " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
                Seed(repo, built, entries, log);
            }
            catch (Exception ex) { log("ct_weapon FAIL " + ex); }
            return built;
        }

        // ---------------------------------------------------------------- one weapon

        private static WeaponDef One(DefRepository repo, string modDir, Entry e, Action<string> log)
        {
            if (repo.GetDef(e.Guid(1)) is WeaponDef already)
            {
                log("ct_weapon PASS '" + e.id + "' already built this session");
                return already;
            }

            WeaponDef source = null;
            foreach (WeaponDef d in repo.GetAllDefs<WeaponDef>())
                if (d != null && d.name == e.clone) { source = d; break; }
            if (source == null)
            {
                log("ct_weapon FAIL '" + e.clone + "' is not in the def repository - nothing to clone from");
                return null;
            }

            // --- the view: name, blurb, inventory cell.
            ViewElementDef view = (ViewElementDef)repo.CreateDef(e.Guid(2), source.ViewElementDef, null);
            view.name = "E_View [" + e.id + "]";
            view.ResourcePath = "Morgott/ContentTool/" + e.id;
            // doNotLocalize: true (LocalizedTextBind.cs:25) - without it the UI looks the string up
            // as an I2 term and draws the raw key.
            view.DisplayName1 = new LocalizedTextBind(e.name, true);
            if (!string.IsNullOrEmpty(e.blurb)) view.Description = new LocalizedTextBind(e.blurb, true);

            string iconWhy = "(none declared)";
            if (!string.IsNullOrEmpty(e.icon))
            {
                Sprite s = Icon(Path.Combine(modDir, e.icon), out iconWhy);
                if (s != null)
                {
                    Held.Add(s);                       // Unity must not collect the texture behind it
                    view.InventoryIcon = s;
                    view.SmallIcon = s;
                    view.LargeIcon = s;
                    iconWhy = "ok";
                }
            }

            // --- the weapon.
            WeaponDef def = (WeaponDef)repo.CreateDef(e.Guid(1), source, null);
            def.name = e.id;
            def.ResourcePath = "Morgott/ContentTool/" + e.id;
            def.ViewElementDef = view;

            // --- the skin, ONLY when this mod published a prefab for it. Without a model the clone
            // keeps the shipped SkinData it already carries and wears the donor's art.
            string prefabWhy = "(no \"model\" - wears " + e.clone + "'s own art)";
            if (!string.IsNullOrEmpty(e.model))
            {
                SimpleSkinDataDef skin = (SimpleSkinDataDef)repo.CreateDef(e.Guid(3), source.SkinData, null);
                skin.name = "E_SkinData [" + e.id + "]";
                skin.ResourcePath = "Morgott/ContentTool/" + e.id;
                def.SkinData = skin;
                // Caught here and not at the top: if Addressables cannot be reached the weapon should
                // still exist with its stats, its name and its cell icon, holding nothing.
                try { prefabWhy = Point(skin, e, source); }
                catch (Exception ex) { prefabWhy = "FAILED " + ex.GetType().Name + ": " + ex.Message; }
            }

            // --- the numbers. The payload is DEEP-COPIED first: DamagePayload is a plain
            // [Serializable] CLASS (DamagePayload.cs:21-22), not a def and not a struct, so whether
            // the clone got its own instance depends on Unity's Instantiate deep-copying serialized
            // fields. Depending on that is how a mod permanently re-tunes the player's SHIPPED
            // weapon for the session; an own copy costs six lines and removes the question.
            def.DamagePayload = CopyOf(source.DamagePayload);
            if (e.damage > 0f)
                foreach (DamageKeywordPair pair in def.DamagePayload.DamageKeywords)
                    if (pair.DamageKeywordDef != null && pair.DamageKeywordDef.AppliesStandardDamage)
                        pair.Value = e.damage;
            if (e.spread > 0f) def.SpreadDegrees = e.spread;

            // --- what the shot DOES, beyond a number. Both of these are pure data in Phoenix Point,
            // which is why they are manifest keys and not code:
            //
            //   "damagetype"  DamagePayload.DamageType (DamagePayload.cs:35), a
            //                 DamageTypeBaseEffectDef. NJ_FlameThrower_WeaponDef sets
            //                 Fire_StandardDamageTypeEffectDef, and that is the whole of "this is
            //                 fire damage".
            //   "keywords"    extra DamageKeywordPairs. Setting the target ALIGHT is one of them:
            //                 the flamethrower carries Damage_DamageKeywordDataDef 80 AND
            //                 Burning_DamageKeywordEffectorDef 40, and the second pair is the burn.
            //
            // A named def that does not exist is REPORTED BY NAME rather than skipped: a typo here
            // is a weapon that quietly deals no fire, which is indistinguishable in play from a
            // weapon whose fire does not work.
            string keywordReport = Keywords(repo, def, e);
            string typeReport = "";
            if (!string.IsNullOrEmpty(e.damageType))
            {
                DamageTypeBaseEffectDef type = null;
                foreach (DamageTypeBaseEffectDef d in repo.GetAllDefs<DamageTypeBaseEffectDef>())
                    if (d != null && d.name == e.damageType) { type = d; break; }
                if (type == null)
                    typeReport = "; damagetype '" + e.damageType + "' NOT FOUND - the shot keeps " +
                                 (def.DamagePayload.DamageType == null ? "none" : def.DamagePayload.DamageType.name);
                else
                {
                    def.DamagePayload.DamageType = type;
                    typeReport = "; damagetype " + type.name;
                }
            }

            string animReport = Animate(repo, def, source);

            log("ct_weapon PASS '" + e.name + "' (" + e.id + ") cloned from " + e.clone +
                "; icon " + iconWhy + "; prefab " + prefabWhy +
                "; " + Tuning(def, source, e) + keywordReport + typeReport + animReport +
                "; " + Vfx(def));
            return def;
        }

        /// <summary>
        /// ============ THE CLONE HAS TO BE NAMED WHERE THE ORIGINAL IS NAMED ============
        ///
        /// A soldier holding a cloned weapon COULD NOT RUN, and this is why.
        ///
        /// Phoenix Point selects a soldier's animation for an action by filtering anim-action defs on
        /// the equipment in hand, and that filter is BY DEF IDENTITY - literally
        /// <c>GetInstanceID()</c>:
        ///
        ///   TacActorAnimActionEquipmentFilteredDef.Contains  (:79-91) builds a HashSet of
        ///   GetInstanceID() over its Equipments array and asks whether the held def is in it;
        ///   EquipmentListDef.Contains (:15-27) does the identical thing for a shared list.
        ///
        /// A clone is a DIFFERENT Unity object, so it has a different InstanceID, so it is in NO
        /// list, so NOTHING matches - including TacActorNavAnimActionDef, which is the run. The
        /// soldier is left with no locomotion clip for the thing in his hands.
        ///
        /// Cloning a weapon is a promise that it behaves like the weapon it cloned; being named
        /// everywhere the original is named is part of that promise, so the engine keeps it rather
        /// than every content mod rediscovering this. Same root as the creature line's melee weapon,
        /// which had to be added beside its donor in two equipment filters for the same reason.
        ///
        /// THE CACHE IS THE HALF THAT WOULD HAVE BITTEN NEXT: both Contains methods build
        /// _equipmentIds ONCE, lazily, and never rebuild it. Appending to the array after anything
        /// has queried that def leaves the addition invisible forever, so the cache is dropped here
        /// and rebuilt on next use.
        /// </summary>
        private static string Animate(DefRepository repo, WeaponDef def, WeaponDef source)
        {
            int filters = 0, lists = 0;
            List<EquipmentListDef> done = new List<EquipmentListDef>();
            foreach (TacActorAnimActionEquipmentFilteredDef action in
                     repo.GetAllDefs<TacActorAnimActionEquipmentFilteredDef>())
            {
                if (action == null) continue;
                if (Names(action.Equipments, source))
                {
                    action.Equipments = Append(action.Equipments, def);
                    Forget(action);
                    filters++;
                }
                EquipmentListDef list = action.EquipmentList;
                if (list != null && !done.Contains(list) && Names(list.Equipments, source))
                {
                    list.Equipments = Append(list.Equipments, def);
                    Forget(list);
                    done.Add(list);
                    lists++;
                }
            }
            // THE RUN SEQUENCE, PRINTED, because it is the seam that actually decides.
            // TacticalPathProcessor.GetRunForwardAnim:209-214 reads
            // actor.ActorAnimActions.ActiveNavigationClips.Run.Loop and REFUSES outright when it is
            // null - and ActiveNavigationClips is the equipment-filtered nav def this method just
            // named the clone into. So "which nav defs cover this weapon, and do their Run
            // sequences actually hold clips" is the whole question, and printing it beats reasoning
            // about it: if the soldier still cannot run, this line says whether the cause is a
            // missing MATCH (no nav def listed) or an empty SEQUENCE (matched, but Run.Loop null).
            List<string> nav = new List<string>();
            foreach (TacActorNavAnimActionDef run in repo.GetAllDefs<TacActorNavAnimActionDef>())
            {
                // MIRROR TacActorNavAnimActionDef.Match:78-108 EXACTLY, or this lies twice over.
                //
                // The first version counted only defs that NAME the clone and printed "NO nav action
                // covers this weapon" for all three - which is FALSE. Match uses identity ONLY when
                // the def actually carries an equipment filter (:100 HasEquipmentFilter). Without
                // one it matches on NumberOfHands (:102-106), and -1 means "any", so an unfiltered
                // nav def covers every weapon in the game including a brand-new clone. An instrument
                // that reports a catastrophe where there is none is worse than no instrument: it
                // sends the next reader hunting the wrong bug, which is exactly what it nearly did.
                if (run == null) continue;
                bool filtered = run.EquipmentList != null ||
                                (run.Equipments != null && run.Equipments.Length != 0);
                string how;
                if (filtered)
                {
                    if (!Names(run.Equipments, def) &&
                        !(run.EquipmentList != null && Names(run.EquipmentList.Equipments, def))) continue;
                    how = "by name";
                }
                else
                {
                    if (run.NumberOfHands != -1 && def.HandsToUse != run.NumberOfHands) continue;
                    how = run.NumberOfHands == -1 ? "any weapon" : "hands=" + run.NumberOfHands;
                }
                ClipSequence seq = run.Run;
                nav.Add(run.name + " (" + how + ", default=" + run.IsDefaultAnimatorClips + ") Run[" +
                        (seq == null ? "NO SEQUENCE"
                         : (seq.Start ? "start" : "-") + "/" +
                           (seq.Loop ? "LOOP" : "NO LOOP - the game refuses to run") + "/" +
                           (seq.Stop ? "stop" : "-")) + "]");
            }

            return "; anims " + (filters + lists == 0
                ? "NONE - " + source.name + " is named by no anim action, so this weapon has no " +
                  "animation of its own and the soldier may be unable to move with it"
                : "named beside " + source.name + " in " + filters + " filter(s) + " + lists +
                  " shared list(s)") +
                "; nav " + (nav.Count == 0
                    ? "NO nav action matches this weapon by EITHER rule (name or hands=" + def.HandsToUse + ") - GetRunForwardAnim:209 would refuse"
                    : string.Join(" | ", nav.ToArray()));
        }

        private static bool Names(EquipmentDef[] list, WeaponDef which)
        {
            if (list == null) return false;
            foreach (EquipmentDef d in list) if (d == which) return true;
            return false;
        }

        private static EquipmentDef[] Append(EquipmentDef[] list, WeaponDef add)
        {
            EquipmentDef[] grown = new EquipmentDef[(list == null ? 0 : list.Length) + 1];
            if (list != null) Array.Copy(list, grown, list.Length);
            grown[grown.Length - 1] = add;
            return grown;
        }

        /// <summary>Drops a lazily-built _equipmentIds cache so the appended def is actually seen.</summary>
        private static void Forget(object holder)
        {
            System.Reflection.FieldInfo cache = holder.GetType().GetField(
                "_equipmentIds",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.FlattenHierarchy);
            if (cache != null) cache.SetValue(holder, null);
        }

        /// <summary>
        /// The manifest's extra damage keywords, applied to the clone's own payload. An entry that
        /// names a keyword the weapon already carries OVERWRITES its value rather than adding a
        /// second pair for the same def, because two pairs of the same keyword is not a bigger
        /// number - it is a payload the game sums twice.
        /// </summary>
        private static string Keywords(DefRepository repo, WeaponDef def, Entry e)
        {
            if (e.keywords.Count == 0) return "";
            List<string> said = new List<string>();
            foreach (KeyValuePair<string, float> want in e.keywords)
            {
                DamageKeywordDef keyword = null;
                foreach (DamageKeywordDef d in repo.GetAllDefs<DamageKeywordDef>())
                    if (d != null && d.name == want.Key) { keyword = d; break; }
                if (keyword == null)
                {
                    said.Add(want.Key + " NOT FOUND");
                    continue;
                }
                DamageKeywordPair pair = null;
                foreach (DamageKeywordPair p in def.DamagePayload.DamageKeywords)
                    if (p.DamageKeywordDef == keyword) { pair = p; break; }
                if (pair == null)
                {
                    pair = new DamageKeywordPair { DamageKeywordDef = keyword };
                    def.DamagePayload.DamageKeywords.Add(pair);
                    said.Add("+" + want.Key + " " + want.Value);
                }
                else
                {
                    said.Add(want.Key + " " + pair.Value + "->" + want.Value);
                    pair.Value = want.Value;
                }
                pair.Value = want.Value;
            }
            return "; keywords [" + string.Join(", ", said.ToArray()) + "]";
        }

        // ---------------------------------------------------------------- starting storage

        /// <summary>
        /// Every built weapon, plus clips of the ammunition it actually eats, into the array a new
        /// campaign fills the Phoenix base from (GameDifficultyLevelDef.StartingStorage,
        /// GameDifficultyLevelDef.cs:43).
        ///
        /// The ammo def is never NAMED by a manifest. It is read off the clone's own
        /// ItemDef.CompatibleAmmunition[0] (ItemDef.cs:47), so whatever shipped class an entry
        /// cloned, the clips that arrive beside it are by construction the shipped clips that weapon
        /// reloads from - and a weapon whose class has no clip (a melee blade) simply gets none.
        /// </summary>
        private static void Seed(DefRepository repo, List<WeaponDef> built, List<Entry> entries, Action<string> log)
        {
            if (built.Count == 0) return;
            int stores = 0;
            List<string> what = new List<string>();
            foreach (GameDifficultyLevelDef diff in repo.GetAllDefs<GameDifficultyLevelDef>())
            {
                if (diff == null || diff.StartingStorage == null) continue;
                List<ItemUnit> grown = new List<ItemUnit>(diff.StartingStorage);
                what.Clear();
                for (int i = 0; i < built.Count; i++)
                {
                    Entry e = Find(entries, built[i].name);
                    grown.Add(new ItemUnit(built[i], e.count));
                    string ammo = "no clip";
                    if (built[i].CompatibleAmmunition != null && built[i].CompatibleAmmunition.Length > 0 && e.clips > 0)
                    {
                        grown.Add(new ItemUnit(built[i].CompatibleAmmunition[0], e.clips));
                        ammo = e.clips + "x " + built[i].CompatibleAmmunition[0].name;
                    }
                    what.Add(e.count + "x " + built[i].name + " + " + ammo);
                }
                diff.StartingStorage = grown.ToArray();
                stores++;
            }
            log("ct_weapon PASS StartingStorage of " + stores + " difficulty def(s) now carries [" +
                string.Join("; ", what.ToArray()) + "]");
        }

        private static Entry Find(List<Entry> entries, string id)
        {
            foreach (Entry e in entries) if (e.id == id) return e;
            return new Entry();
        }

        // ---------------------------------------------------------------- the prefab and its sockets

        /// <summary>
        /// Points a skin def at a catalog key this mod published, starts the load, and fits the four
        /// named empties onto the prefab when it arrives.
        ///
        /// The load is started HERE rather than left to the engine because
        /// AddonSkinDataBase.GetPrefabAsset (AddonSkinDataBase.cs:19-29) returns
        /// assetReference.Asset and never loads. The engine fills that in through
        /// AssetsManager.AcquireDependenciesAsync, which reflects over a def's AssetReference fields
        /// (AssetsManager.cs:82, :188), and whether a def created after boot is ever handed to that
        /// pass is not something a mod can promise.
        ///
        /// THE FOUR SOCKETS, all resolved BY NAME off the weapon's visual root, each breaking
        /// something specific when missing:
        ///   EXT_ShootPoint  DamagePayload.ProjectileOrigin - projectiles leave here AND the muzzle
        ///                   flash spawns here (Weapon.SpawnFlash, Weapon.cs:389-397).
        ///   EXT_AimPoint    EquipmentDef.AimPoint.
        ///   EXT_AimIKPoint  EquipmentDef.AimTransform - TacticalActor.cs:2028 otherwise hands the
        ///                   aim IK solver a null transform.
        ///   EXT_ShellPoint  EquipmentVisualEffectsDef.ShellEjectionPoint. Only matters when the
        ///                   clone source's effects def carries a Shell at all; an energy weapon
        ///                   ships none, and then this socket is simply unused.
        /// A prefab baked by ContentTool is a root plus one mesh child (PrefabFields.Build), so there
        /// is nowhere in the .glb to put them.
        /// </summary>
        private static string Point(SimpleSkinDataDef skin, Entry e, WeaponDef source)
        {
            AssetReferenceGameObject reference = new AssetReferenceGameObject(e.model);
            skin.DefaultPrefab = reference;
            if (!reference.RuntimeKeyIsValid())
                return "REFUSED key '" + e.model + "' is not a valid Addressables runtime key";

            AsyncOperationHandle<GameObject> handle = reference.LoadAssetAsync<GameObject>();
            handle.Completed += op =>
            {
                if (op.Status != AsyncOperationStatus.Succeeded || op.Result == null)
                {
                    Debug.LogError("[ContentTool] ct_weapon FAIL key '" + e.model + "' did not load (" +
                                   op.Status + ") - '" + e.id + "' exists but has no model. Keys are " +
                                   "published live when the mod is enabled, so there is nothing to " +
                                   "apply and nothing to restart: either the key is not declared in " +
                                   "this mod's \"publish\" block, or its bundle was never baked " +
                                   "('ct_project <mod>'). 'ct_catalog status' lists what IS published.");
                    return;
                }
                try
                {
                    if (e.fit != "auto") { Place(op.Result, e, e.shoot, e.aim, e.shell, "declared"); return; }

                    // SOCKETS FIRST, ALWAYS, from the model's own box. A nested asynchronous load is
                    // not allowed to be the only thing standing between this weapon and having a
                    // muzzle: MEASURED - the donor load's Completed never fired inside the gate's
                    // lifetime, so the previous version logged NOTHING for these two weapons and left
                    // them with no sockets at all. Placing now means the worst case is a slightly
                    // wrong muzzle instead of a missing one, and the donor merely improves it.
                    Vector3 s0, a0, h0;
                    Sockets(mineBounds(op.Result), Vector3.zero, 1f, out s0, out a0, out h0);
                    Place(op.Result, e, s0, a0, h0, "derived from its own box (donor not measured yet)");

                    // THE DONOR HAS TO BE LOADED, not hoped for. AddonSkinDataBase.GetPrefabAsset
                    // returns assetReference.Asset and never loads (AddonSkinDataBase.cs:19-29), and
                    // at mod-enable time the shipped weapon's prefab is not in memory - MEASURED: the
                    // first version of this fit logged "prefab is not loaded" for both weapons and
                    // silently left every socket at the origin. So load it the same way this class
                    // already loads its own, then fit inside that callback.
                    SimpleSkinDataDef donorSkin = source.SkinData as SimpleSkinDataDef;
                    if (donorSkin == null || donorSkin.DefaultPrefab == null ||
                        !donorSkin.DefaultPrefab.RuntimeKeyIsValid())
                    {
                        Debug.Log("[ContentTool] ct_weapon fit '" + e.id + "' keeps its own box: " +
                                  e.clone + " publishes no prefab to measure");
                        return;
                    }
                    // SYNCHRONOUS on purpose. Waiting on the callback made the fit a COIN FLIP -
                    // MEASURED across three gate runs: the donor's Completed fired in one and not in
                    // the next two, so the same build produced a fitted gun or a raw-file-units one
                    // depending on load timing. A weapon that is the right size only sometimes is
                    // worse than one that is reliably either. This is one small prefab, once, at
                    // mod-enable, where a brief hitch costs nothing anyone can see.
                    AsyncOperationHandle<GameObject> donorHandle =
                        donorSkin.DefaultPrefab.LoadAssetAsync<GameObject>();
                    GameObject donorGo = donorHandle.WaitForCompletion();
                    Vector3 s1, a1, h1;
                    bool fitted = Fit(op.Result, e, source,
                                      donorHandle.Status == AsyncOperationStatus.Succeeded ? donorGo : null,
                                      out s1, out a1, out h1);
                    Place(op.Result, e, s1, a1, h1,
                          fitted ? "derived from " + e.clone + "'s own box" : "derived from its own box");
                }
                catch (Exception ex) { Debug.LogError("[ContentTool] ct_weapon socket fit threw " + ex); }
            };
            return "load started for key " + e.model;
        }

        /// <summary>
        /// Scales and turns a downloaded model into the box the CLONED weapon already occupies.
        ///
        /// WHY THE DONOR IS THE SPECIFICATION. A weapon prefab is parented to a named attachment
        /// transform on the rig (Addon.cs:49-53 -> AddonsManager.cs:120), so it lands in the hand at
        /// whatever coordinates its mesh carries. The shipped weapon this entry cloned already sits
        /// correctly in that hand, so ITS mesh bounds are the target - measured, per clone source,
        /// with no magic numbers anywhere and nothing to re-tune when the clone source changes.
        ///
        /// STRICTLY ADDITIVE. Every failure path leaves the prefab exactly as it arrives today: a
        /// donor whose prefab is not loaded cannot be measured, and guessing a scale would be worse
        /// than the honest "unfitted" the demo already ships. It says which case it took, by name.
        ///
        /// ponytail: the fit is applied to the ROOT, so the EXT_ sockets - which Socket() places in
        /// root-local space right after this - ride along with it. That is why Fit runs FIRST.
        /// </summary>
        private static bool Fit(GameObject prefab, Entry e, WeaponDef source, GameObject donorGo,
                                out Vector3 shoot, out Vector3 aim, out Vector3 shell)
        {
            shoot = e.shoot; aim = e.aim; shell = e.shell;
            MeshFilter mine = prefab.GetComponentInChildren<MeshFilter>();
            if (mine == null || mine.sharedMesh == null)
            {
                Debug.Log("[ContentTool] ct_weapon fit SKIPPED '" + e.id + "': the baked prefab has no mesh");
                return false;
            }

            // THE BIGGEST MESH, not the first one. A shipped weapon prefab is many meshes - body,
            // magazine, scope glass, muzzle-flash quad - and GetComponentInChildren returns whichever
            // comes first in the hierarchy, which is not the gun. MEASURED: taking the first gave the
            // Sidearm a scale of 0.0078, i.e. a pistol about three millimetres long, because it had
            // measured itself against some tiny sub-part. The gun is the largest piece by a wide
            // margin, and that IS the box the game reserves.
            MeshFilter donor = Biggest(donorGo);
            if (donor == null || donor.sharedMesh == null)
            {
                // The fit cannot proceed, but the sockets still must not be the ORIGIN. Falling back
                // to a zeroed manifest value put the muzzle inside the grip and reported it as
                // "declared", which is the silent-wrong-value case a socket check exists to catch.
                // Deriving from the model's OWN box is at least the right end of the right gun.
                Sockets(mine.sharedMesh.bounds, Vector3.zero, 1f, out shoot, out aim, out shell);
                Debug.Log("[ContentTool] ct_weapon fit SKIPPED '" + e.id + "': " + e.clone +
                          "'s own prefab could not be measured. The model keeps the size its .glb " +
                          "carries, and the sockets are derived from its own box instead: shoot=" + shoot);
                return false;
            }

            Bounds src = mine.sharedMesh.bounds, dst = donor.sharedMesh.bounds;
            float[] se = { src.extents.x, src.extents.y, src.extents.z };
            int longAxis = FitBox.LongAxis(se);
            float[] euler = FitBox.RotationToZ(longAxis, e.flip);
            prefab.transform.localRotation = Quaternion.Euler(euler[0], euler[1], euler[2]);

            // The box AFTER the turn: rotating by whole right angles permutes the extents, so the
            // solve must see the extents the mesh will actually present once it is facing +Z.
            float[] turned = longAxis == 0 ? new[] { se[2], se[1], se[0] }
                           : longAxis == 1 ? new[] { se[0], se[2], se[1] }
                           : se;
            float scale; float[] offset; string why;
            if (!FitBox.Solve(new[] { src.center.x, src.center.y, src.center.z }, turned,
                              new[] { dst.center.x, dst.center.y, dst.center.z },
                              new[] { dst.extents.x, dst.extents.y, dst.extents.z },
                              out scale, out offset, out why))
            {
                Debug.Log("[ContentTool] ct_weapon fit SKIPPED '" + e.id + "': " + why);
                prefab.transform.localRotation = Quaternion.identity;
                return false;
            }

            prefab.transform.localScale = new Vector3(scale, scale, scale);
            prefab.transform.localPosition = new Vector3(offset[0], offset[1], offset[2]);
            Debug.Log("[ContentTool] ct_weapon fit '" + e.id + "' into " + e.clone + "'s own box: long axis " +
                      "XYZ"[longAxis] + " -> +Z (rotate " + euler[1] + (e.flip ? " incl. flip" : "") +
                      "), scale " + scale.ToString("0.0000", CultureInfo.InvariantCulture) +
                      ", offset " + offset[0].ToString("0.000", CultureInfo.InvariantCulture) + "," +
                      offset[1].ToString("0.000", CultureInfo.InvariantCulture) + "," +
                      offset[2].ToString("0.000", CultureInfo.InvariantCulture) +
                      "; donor mesh '" + donor.sharedMesh.name + "' centre " + dst.center + " extent " + dst.extents);

            // --- the three sockets, DERIVED from the box the gun now occupies, by the same rules the
            // demo's fit script uses offline: the muzzle at the front face, the sights 62% back along
            // the barrel, both on the barrel line at 70% of the box height (a rifle's barrel sits
            // above mid-height - the stock and magazine fill the lower half), and the ejection port on
            // the +X face beside the sights.
            //
            // THE DIVISION BY SCALE IS THE WHOLE TRICK. Sockets are children of the root, and the root
            // now carries the fit's own scale and offset, so a child's localPosition is expressed in
            // the SCALED frame. A coordinate q in the donor's space therefore has to be written as
            // (q - offset) / scale, or every socket lands at the wrong end of the gun by exactly the
            // factor the fit just applied.
            Sockets(dst, new Vector3(offset[0], offset[1], offset[2]), scale, out shoot, out aim, out shell);
            return true;
        }

        /// <summary>
        /// The three sockets, derived from the box the gun occupies, by the same rules the demo's fit
        /// script uses offline: the muzzle at the front face, the sights 62% back along the barrel,
        /// both on the barrel line at 70% of the box height (a rifle's barrel sits ABOVE mid-height -
        /// stock and magazine fill the lower half), and the ejection port on the +X face beside the
        /// sights.
        ///
        /// THE DIVISION BY SCALE IS THE WHOLE TRICK. Sockets are children of the root, and a fitted
        /// root carries the fit's own scale and offset, so a child's localPosition is expressed in
        /// the SCALED frame. A coordinate q in the target space is therefore written as
        /// (q - offset) / scale, or every socket lands off by exactly the factor the fit applied.
        /// An unfitted call passes offset zero and scale one, which reduces to q.
        /// </summary>
        private static void Sockets(Bounds box, Vector3 offset, float scale,
                                    out Vector3 shoot, out Vector3 aim, out Vector3 shell)
        {
            float barrelY = box.center.y - box.extents.y + 0.70f * (2f * box.extents.y);
            float aimZ = box.center.z - box.extents.z + 0.62f * (2f * box.extents.z);
            shoot = (new Vector3(box.center.x, barrelY, box.center.z + box.extents.z) - offset) / scale;
            aim = (new Vector3(box.center.x, barrelY, aimZ) - offset) / scale;
            shell = (new Vector3(box.center.x + box.extents.x, barrelY, aimZ) - offset) / scale;
        }

        /// <summary>
        /// The largest mesh under a prefab, by bounding-box diagonal. "Largest" rather than "first"
        /// is the whole point - see the note at the call site. Skinned meshes are ignored: a weapon
        /// prefab's body is a plain MeshFilter, and a skinned one under it would be an attachment.
        /// </summary>
        private static MeshFilter Biggest(GameObject root)
        {
            if (root == null) return null;
            MeshFilter best = null;
            float bestSize = -1f;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                float size = mf.sharedMesh.bounds.size.magnitude;
                if (size > bestSize) { bestSize = size; best = mf; }
            }
            return best;
        }

        /// <summary>The baked prefab's own mesh bounds, or an empty box when it somehow has none.</summary>
        private static Bounds mineBounds(GameObject prefab)
        {
            MeshFilter mf = prefab.GetComponentInChildren<MeshFilter>();
            return mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.zero);
        }

        /// <summary>The four sockets onto the prefab, and one line saying where they came from.</summary>
        private static void Place(GameObject prefab, Entry e, Vector3 shoot, Vector3 aim, Vector3 shell, string how)
        {
            Socket(prefab, "EXT_ShootPoint", shoot);
            Socket(prefab, "EXT_AimPoint", aim);
            Socket(prefab, "EXT_AimIKPoint", aim);
            Socket(prefab, "EXT_ShellPoint", shell);
            Debug.Log("[ContentTool] ct_weapon PASS '" + prefab.name + "' loaded from key " + e.model +
                      " for '" + e.id + "'; four EXT_ sockets " + how + " shoot=" + shoot + " aim=" + aim);
        }

        /// <summary>One named empty under the prefab root, idempotent.</summary>
        private static void Socket(GameObject root, string name, Vector3 local)
        {
            if (root.transform.Find(name) != null) return;
            GameObject go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = local;
            // The muzzle looks down +Z, which is the direction the shipped weapon meshes point and
            // the direction a projectile leaves along.
            go.transform.localRotation = Quaternion.identity;
        }

        // ---------------------------------------------------------------- reporting

        /// <summary>
        /// The changed numbers next to the shipped ones they came from - and the falsifiable half:
        /// <paramref name="src"/> is the player's own shipped def, so if the deep copy above ever
        /// stopped working this line would print the new numbers for IT too, in Player.log, instead
        /// of the mod quietly re-tuning a weapon it does not own.
        /// </summary>
        private static string Tuning(WeaponDef def, WeaponDef src, Entry e)
        {
            bool leaked = ReferenceEquals(src.DamagePayload, def.DamagePayload) ||
                          (e.damage > 0f && Math.Abs(Std(src) - e.damage) < 0.01f);
            return "tuning dmg " + Std(src) + "->" + Std(def) +
                   " spread " + src.SpreadDegrees + "->" + def.SpreadDegrees +
                   " range " + src.EffectiveRange + "->" + def.EffectiveRange +
                   (leaked ? " *** THE SHIPPED " + src.name + " WAS MUTATED ***" : " (source intact)");
        }

        /// <summary>The standard-damage keyword's value, which is the number a weapon really deals:
        /// DamagePayload.KeywordFlow (DamagePayload.cs:103) moves the whole damage flow onto the
        /// keyword list the moment it is non-empty, leaving DamageValue unread.</summary>
        private static float Std(WeaponDef def)
        {
            if (def == null || def.DamagePayload == null) return 0f;
            foreach (DamageKeywordPair pair in def.DamagePayload.DamageKeywords)
                if (pair.DamageKeywordDef != null && pair.DamageKeywordDef.AppliesStandardDamage)
                    return pair.Value;
            return def.DamagePayload.DamageValue;
        }

        /// <summary>
        /// What the weapon will actually SHOW when it fires, read back off the def rather than
        /// assumed. The binding is two fields: EquipmentDef.VisualEffects (EquipmentDef.cs:26) for
        /// flash/smoke/brass, and DamagePayload.ProjectileVisuals (DamagePayload.cs:89) for the
        /// tracer and impact. Both ride along with the clone at a cost of zero lines.
        /// </summary>
        private static string Vfx(WeaponDef def)
        {
            EquipmentVisualEffectsDef fx = def.VisualEffects;
            if (fx == null) return "vfx NONE - this weapon fires with no muzzle flash";
            return "vfx '" + fx.name + "' flash=" + (fx.Flash == null ? "none" : fx.Flash.name) +
                   " shell=" + (fx.Shell == null ? "none" : fx.Shell.name + "@" + fx.ShellEjectionPoint) +
                   " projectile=" + (def.DamagePayload.ProjectileVisuals == null
                       ? "none" : def.DamagePayload.ProjectileVisuals.name);
        }

        // ---------------------------------------------------------------- plumbing

        /// <summary>Sprites are held so Unity cannot collect the texture behind a live Sprite.</summary>
        private static readonly List<Sprite> Held = new List<Sprite>();

        /// <summary>
        /// An own copy of a DamagePayload, list and pairs included. MemberwiseClone rather than a
        /// field-by-field copy: DamagePayload carries ~30 serialized fields (DamagePayload.cs:30-101)
        /// and a copy that forgets one is a weapon subtly wrong in a way no test would catch.
        /// object.MemberwiseClone is protected, so it is reached by reflection - one MethodInfo
        /// serves both types.
        /// </summary>
        private static DamagePayload CopyOf(DamagePayload src)
        {
            System.Reflection.MethodInfo shallow = typeof(object).GetMethod(
                "MemberwiseClone",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            DamagePayload copy = (DamagePayload)shallow.Invoke(src, null);
            copy.DamageKeywords = new List<DamageKeywordPair>();
            foreach (DamageKeywordPair pair in src.DamageKeywords)
                copy.DamageKeywords.Add((DamageKeywordPair)shallow.Invoke(pair, null));
            return copy;
        }

        /// <summary>PNG -&gt; Sprite, TFTV's own recipe (refs\TFTV-src\TFTV\Helper.cs:167-177).</summary>
        private static Sprite Icon(string path, out string why)
        {
            why = null;
            if (!File.Exists(path)) { why = "MISSING no file at " + path; return null; }
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(File.ReadAllBytes(path)))
            { why = "MISSING " + path + " is not a PNG Unity can decode"; return null; }
            return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        // ---------------------------------------------------------------- the manifest

        /// <summary>One declared weapon. Every field is data; none of it is mechanism.</summary>
        private sealed class Entry
        {
            internal string id = "", name = "", clone = "", blurb = "", icon = "", model = "", guid = "", damageType = "", fit = "";
            /// <summary>The one bit a symmetric bounding box cannot supply: which end is the muzzle.</summary>
            internal bool flip;
            /// <summary>Whether the manifest actually wrote a "shoot" key. NOT "is it non-zero":
            /// the origin is a legal muzzle position, so absence has to be its own fact.</summary>
            internal bool declaresShoot;
            /// <summary>Extra DamageKeywordPairs by DEF NAME, e.g. Burning_DamageKeywordEffectorDef=40.</summary>
            internal readonly List<KeyValuePair<string, float>> keywords = new List<KeyValuePair<string, float>>();
            internal float damage, spread;
            internal int count, clips;
            internal Vector3 shoot, aim, shell;

            /// <summary>
            /// The weapon's, view's and skin's def identities. FIXED and derived from the manifest's
            /// own "guid", never Guid.NewGuid(): a save stores an item by its def, and a def whose
            /// identity changes every launch is a save that stops loading.
            ///
            /// THE WEAPON KEEPS THE DECLARED GUID VERBATIM, and the view and skin take it with the
            /// FIRST hex digit replaced by 'a' and 'b'. That is not decoration - the first version of
            /// this overwrote the guid's LAST TWO digits with 01/02/03, which are exactly the digits
            /// a manifest uses to tell its entries apart, so `...4b01`, `...4b11` and `...4b21` all
            /// collapsed onto `...4b01` and the second and third weapons silently resolved to the
            /// first. The in-game log said "already built this session" three times over and only one
            /// weapon reached storage. Derive from a position the author does NOT vary, and verify
            /// (see the distinctness check in Parse) rather than trusting the scheme.
            /// </summary>
            internal string Guid(int which)
            {
                if (string.IsNullOrEmpty(guid)) return guid;
                if (which == 1) return guid;
                return (which == 2 ? "a" : "b") + guid.Substring(1);
            }
        }

        /// <summary>
        /// Read out of the RAW TEXT, the same way ContentProject.ParsePublish reads its own array
        /// (ContentProject.cs:435-459) and for the reason recorded there: Unity's JsonUtility returns
        /// null for these nested shapes and gives no error at all, so a declared key would parse to
        /// silence.
        ///
        /// ponytail: the one-line Field regex is duplicated from ContentProject rather than shared -
        /// extracting a helper across two files would be more churn than the duplicate line saves.
        /// If a third parser wants it, lift it then.
        /// </summary>
        private static List<Entry> Parse(string json)
        {
            List<Entry> list = new List<Entry>();
            Match arr = Regex.Match(json, "\"weapons\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            if (!arr.Success) return list;

            foreach (Match o in Regex.Matches(arr.Groups[1].Value, "\\{[^{}]*\\}", RegexOptions.Singleline))
            {
                Entry e = new Entry
                {
                    id = Field(o.Value, "id"),
                    name = Field(o.Value, "name"),
                    clone = Field(o.Value, "clone"),
                    blurb = Field(o.Value, "blurb"),
                    icon = Field(o.Value, "icon"),
                    model = Field(o.Value, "model"),
                    guid = Field(o.Value, "guid"),
                    damageType = Field(o.Value, "damagetype"),
                    fit = Field(o.Value, "fit"),
                    flip = Field(o.Value, "flip") == "true",
                    declaresShoot = Field(o.Value, "shoot").Length > 0,
                    damage = Num(o.Value, "damage"),
                    spread = Num(o.Value, "spread"),
                    count = (int)Num(o.Value, "count"),
                    clips = (int)Num(o.Value, "clips"),
                    shoot = Vec(o.Value, "shoot"),
                    aim = Vec(o.Value, "aim"),
                    shell = Vec(o.Value, "shell")
                };
                // "keywords": "Burning_DamageKeywordEffectorDef=40; Shred_...=5" - def NAME to value,
                // semicolon separated, because a nested JSON object would break the flat {...} row
                // regex this parser and ContentProject both rely on.
                foreach (string clause in Field(o.Value, "keywords").Split(';'))
                {
                    string one = clause.Trim();
                    if (one.Length == 0) continue;
                    int eq = one.IndexOf('=');
                    if (eq <= 0)
                        throw new InvalidDataException(
                            "\"keywords\" entries are DEFNAME=VALUE separated by ';', e.g. " +
                            "\"Burning_DamageKeywordEffectorDef=40\"; got '" + one + "'");
                    float v;
                    if (!float.TryParse(one.Substring(eq + 1).Trim(), NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out v))
                        throw new InvalidDataException("\"keywords\" value for '" +
                            one.Substring(0, eq).Trim() + "' is not a number: '" + one + "'");
                    e.keywords.Add(new KeyValuePair<string, float>(one.Substring(0, eq).Trim(), v));
                }
                if (string.IsNullOrEmpty(e.id) || string.IsNullOrEmpty(e.clone) || string.IsNullOrEmpty(e.guid))
                    throw new InvalidDataException(
                        "every \"weapons\" entry needs \"id\" (the def name), \"clone\" (the SHIPPED " +
                        "weapon def it is cloned from) and \"guid\" (its fixed def identity); got " + o.Value);
                // A model needs sockets, and there are exactly two honest ways to get them: declare
                // them (a model pre-fitted offline, as the fit script derives them) or ask the
                // engine to derive them, which is what "fit": "auto" already means.
                //
                // NOT a zero check. "0,0,0" is a LEGAL socket position - a muzzle at the origin -
                // and using it as the "absent" sentinel made a placeholder indistinguishable from a
                // real value. It threw on all three weapons at once, because Parse refuses the whole
                // manifest rather than one entry, so a single bad row cost the player every gun.
                if (!string.IsNullOrEmpty(e.model) && e.fit != "auto" && !e.declaresShoot)
                    throw new InvalidDataException(
                        "\"" + e.id + "\" declares a \"model\" but no \"shoot\" socket, and does not " +
                        "ask for one. Projectiles leave from EXT_ShootPoint and the muzzle flash " +
                        "spawns there; without it TacticalLevelController.cs:1547-1549 logs \"Can't " +
                        "find ... projectile origin\" and Weapon.cs:425 indexes an empty array. " +
                        "Either add \"shoot\" (the demo's fit script prints all three), or set " +
                        "\"fit\": \"auto\" and the engine derives them from the box it fits into.");
                list.Add(e);
            }

            // THE DERIVED GUIDS MUST ALL BE DISTINCT, and this is checked rather than assumed. The
            // first derivation scheme collided silently: three entries produced one set of three
            // guids, DefRepository handed back the first weapon for all of them, and the only
            // symptom was a cheerful "already built this session" in the log while two weapons
            // quietly did not exist. A collision here is a weapon the player never receives, so it
            // is worth six lines to make it impossible to ship again.
            Dictionary<string, string> taken = new Dictionary<string, string>();
            foreach (Entry e in list)
                for (int which = 1; which <= 3; which++)
                {
                    string g = e.Guid(which);
                    string owner = e.id + "#" + which;
                    if (taken.ContainsKey(g))
                        throw new InvalidDataException(
                            "two \"weapons\" entries derive the SAME def guid " + g + " (" +
                            taken[g] + " and " + owner + "). Give them \"guid\" values that differ " +
                            "somewhere other than the first hex digit - the second and third weapons " +
                            "would otherwise resolve to the first and never reach the player.");
                    taken.Add(g, owner);
                }
            return list;
        }

        private static string Field(string obj, string name)
        {
            return Regex.Match(obj, "\"" + name + "\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value;
        }

        /// <summary>A number written either bare or quoted, invariant culture - a comma decimal
        /// separator on a Russian machine would otherwise read 3.0 as 30.</summary>
        private static float Num(string obj, string name)
        {
            Match m = Regex.Match(obj, "\"" + name + "\"\\s*:\\s*\"?(-?[0-9]*\\.?[0-9]+)\"?");
            if (!m.Success) return 0f;
            float v;
            return float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0f;
        }

        /// <summary>"x,y,z" -&gt; Vector3. Absent reads as zero, which Parse treats as "not declared".</summary>
        private static Vector3 Vec(string obj, string name)
        {
            string raw = Field(obj, name);
            if (string.IsNullOrEmpty(raw)) return Vector3.zero;
            string[] parts = raw.Split(',');
            if (parts.Length != 3) return Vector3.zero;
            float x, y, z;
            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x) ||
                !float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y) ||
                !float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                return Vector3.zero;
            return new Vector3(x, y, z);
        }
    }
}
