using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace TowerStatsMod
{
    internal static class Patches
    {
        private static readonly Dictionary<int, Unit> _lastTowerAttacker = new Dictionary<int, Unit>();
        private static readonly HashSet<int> _creditedKills = new HashSet<int>();

        public static void ResetVictimState(int victimId)
        {
            _creditedKills.Remove(victimId);
            _lastTowerAttacker.Remove(victimId);
        }

        // ─── Unified Victim GameObject Instance ID (Prevents +2 Double Counting) ───
        public static int GetVictimEntityId(Component? comp)
        {
            if (comp == null) return 0;
            if (comp is Unit uDirect) return uDirect.gameObject.GetInstanceID();

            try
            {
                var u = comp.GetComponent<Unit>() ?? comp.GetComponentInParent<Unit>();
                if (u != null) return u.gameObject.GetInstanceID();
            }
            catch { }

            return comp.gameObject.GetInstanceID();
        }

        // ─── Filter: Player Hero Check ───────────────────────────────────────
        public static bool IsPlayerHero(Unit? unit)
        {
            if (unit == null) return false;
            if (unit.isBuilding) return false;
            if (unit.IsPlayerControlled) return true;
            if (unit.GetComponent<PlayerInteract>() != null) return true;
            return false;
        }

        public static bool IsHeroDamage(IDamageSource? source)
        {
            if (source == null) return false;

            try
            {
                Unit u = source.SourceUnit;
                if (u != null && IsPlayerHero(u)) return true;
            }
            catch { }

            if (source is Component comp)
            {
                try
                {
                    var pi = comp.GetComponentInParent<PlayerInteract>();
                    if (pi != null) return true;

                    var parentUnit = comp.GetComponentInParent<Unit>();
                    if (parentUnit != null && IsPlayerHero(parentUnit)) return true;
                }
                catch { }
            }

            return false;
        }

        // ─── Filter: Only Towers (Strictly exclude Barracks, Abode, House, Keep, Forge, etc.) ───
        public static bool IsTower(Unit? unit)
        {
            if (unit == null || !unit.isBuilding) return false;

            // 1. Check PlayerBuilding / HouseType / BuildingSize
            if (unit.playerBuilding != null)
            {
                var pb = unit.playerBuilding;

                // Explicit rejection of non-tower house types
                if (pb.HouseType == HouseType.Barracks || 
                    pb.HouseType == HouseType.Abode || 
                    pb.HouseType == HouseType.House || 
                    pb.HouseType == HouseType.Forge || 
                    pb.HouseType == HouseType.Tavern || 
                    pb.HouseType == HouseType.MarketPlace || 
                    pb.HouseType == HouseType.Arsenal || 
                    pb.HouseType == HouseType.Keep || 
                    pb.HouseType == HouseType.Workshop || 
                    pb.HouseType == HouseType.Builders_guild)
                {
                    return false;
                }

                string houseStr = pb.HouseType.ToString();
                if (houseStr.IndexOf("Barracks", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    houseStr.IndexOf("Abode", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    houseStr.IndexOf("House", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }

                // Explicit inclusion of Tower & Catapult house types
                if (houseStr.IndexOf("Tower", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    houseStr.IndexOf("Catapult", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                if (pb.BuildingSpot != null && pb.BuildingSpot.IsTowerSpot)
                    return true;

                if (pb.StatsEntry != null && pb.StatsEntry.buildingSize == BuildingSize.Tower)
                    return true;

                return false; // Reject any other building types
            }

            // 2. Check for AutoTower component
            try
            {
                if (unit.GetComponent<AutoTower>() != null || unit.GetComponentInParent<AutoTower>() != null)
                    return true;
            }
            catch { }

            // 3. Name check fallback
            if (!string.IsNullOrEmpty(unit.UnitName))
            {
                string name = unit.UnitName.ToLowerInvariant();
                if (name.Contains("barrack") || name.Contains("abode") || name.Contains("house") || name.Contains("keep"))
                    return false;

                if (name.Contains("tower") || name.Contains("catapult") || name.Contains("turret") || name.Contains("ballista"))
                    return true;
            }

            return false;
        }

        // ─── Deep Resolution: Tower Unit from IDamageSource / Projectile ─────
        public static Unit? ResolveTowerUnit(IDamageSource? source)
        {
            if (source == null) return null;

            // Reject hero damage early
            if (IsHeroDamage(source)) return null;

            // 1. Direct SourceUnit property check
            try
            {
                Unit u = source.SourceUnit;
                if (u != null && IsTower(u)) return u;
            }
            catch { }

            // 2. Deep Reflection check for projectiles & weapons (sourceWeapon, _owner, owner, Owner, etc.)
            try
            {
                Type type = source.GetType();

                // Check 'Owner' or 'SourceUnit' properties
                var ownerProp = type.GetProperty("Owner") ?? type.GetProperty("SourceUnit");
                if (ownerProp != null)
                {
                    var u = ownerProp.GetValue(source) as Unit;
                    if (u != null && IsTower(u)) return u;
                }

                // Check '_owner', 'owner', '_sourceUnit' fields
                var ownerField = type.GetField("_owner", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                              ?? type.GetField("owner", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                              ?? type.GetField("_sourceUnit", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (ownerField != null)
                {
                    var u = ownerField.GetValue(source) as Unit;
                    if (u != null && IsTower(u)) return u;
                }

                // Check 'sourceWeapon' field (for projectiles like ImpactProjectile, WeaponProjectile, etc.)
                var swField = type.GetField("sourceWeapon", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                           ?? type.GetField("_sourceWeapon", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (swField != null)
                {
                    var sw = swField.GetValue(source) as IDamageSource;
                    if (sw != null && sw != source)
                    {
                        var resolvedFromSw = ResolveTowerUnit(sw);
                        if (resolvedFromSw != null) return resolvedFromSw;
                    }
                }
            }
            catch { }

            // 3. Component hierarchy check (for attached attack modules)
            if (source is Component comp)
            {
                try
                {
                    var autoTower = comp.GetComponentInParent<AutoTower>();
                    if (autoTower != null)
                    {
                        if (autoTower.Owner != null && IsTower(autoTower.Owner)) return autoTower.Owner;
                        var uFromAuto = autoTower.GetComponent<Unit>();
                        if (uFromAuto != null && IsTower(uFromAuto)) return uFromAuto;
                    }

                    var pb = comp.GetComponentInParent<PlayerBuilding>();
                    if (pb != null)
                    {
                        if (pb.Owner != null && IsTower(pb.Owner)) return pb.Owner;
                        var uFromPb = pb.GetComponent<Unit>();
                        if (uFromPb != null && IsTower(uFromPb)) return uFromPb;
                    }

                    var parentUnit = comp.GetComponentInParent<Unit>();
                    if (parentUnit != null && IsTower(parentUnit))
                    {
                        return parentUnit;
                    }
                }
                catch { }
            }

            return null;
        }

        public static TowerStatsComponent? GetOrCreateTowerStats(Unit unit, bool resetStats = false)
        {
            if (unit == null || !IsTower(unit)) return null;

            var existing = unit.GetComponent<TowerStatsComponent>();
            if (existing != null)
            {
                if (resetStats) existing.ResetStats();
                return existing;
            }

            try
            {
                var comp = unit.gameObject.AddComponent<TowerStatsComponent>();
                comp.Init(unit);
                if (resetStats) comp.ResetStats();
                return comp;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[TowerStatsMod] Failed to attach TowerStatsComponent: {ex.Message}");
                return null;
            }
        }

        public static void EnsureComponentAttachedToBuilding(PlayerBuilding? building, bool resetStats = false)
        {
            if (building == null) return;

            Unit? u = building.Owner;
            if (u == null) u = building.GetComponent<Unit>();
            if (u == null) u = building.GetComponentInParent<Unit>();

            if (u != null && IsTower(u))
            {
                GetOrCreateTowerStats(u, resetStats);
            }
        }

        public static void EnsureComponentAttachedToSpot(BuildingSpot? spot, bool resetStats = false)
        {
            if (spot == null) return;
            if (spot.PlayerBuilding != null)
            {
                EnsureComponentAttachedToBuilding(spot.PlayerBuilding, resetStats);
            }
        }

        private static void TryCreditKill(int victimEntityId, Unit? towerUnit)
        {
            if (victimEntityId == 0 || towerUnit == null || !IsTower(towerUnit)) return;
            if (_creditedKills.Contains(victimEntityId)) return;

            _creditedKills.Add(victimEntityId);
            var stats = GetOrCreateTowerStats(towerUnit);
            if (stats != null)
            {
                stats.RecordKill(1);
            }
        }

        // ─── Non-Host Client Multiplayer Hit Receiver Hook ────────────────────
        [HarmonyPatch(typeof(DamageBatchManager), "ApplyHitLocally")]
        [HarmonyPostfix]
        private static void DamageBatchManager_ApplyHitLocally_Postfix(ulong targetNetId, float damage, HitResultType type, bool isBuilding)
        {
            if (damage <= 0f) return;

            try
            {
                if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
                {
                    if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetId, out var netObj) && netObj != null)
                    {
                        int victimId = GetVictimEntityId(netObj);

                        _lastTowerAttacker.TryGetValue(victimId, out var tower);
                        if (tower != null && IsTower(tower))
                        {
                            var stats = GetOrCreateTowerStats(tower);
                            if (stats != null)
                            {
                                stats.RecordDamage(damage);
                            }

                            var victimUnit = netObj.GetComponent<Unit>();
                            if (victimUnit != null && (victimUnit.IsDead || (victimUnit.Damageable != null && victimUnit.Damageable.CurrentHealth <= 0f)))
                            {
                                TryCreditKill(victimId, tower);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        // ─── Game Statistics Engine Kill Hook (Server / Host) ────────────────
        [HarmonyPatch(typeof(PlayerStatisticsManager), "OnUnitKilled")]
        [HarmonyPostfix]
        private static void PlayerStatisticsManager_OnUnitKilled_Postfix(Unit victim, Unit killer)
        {
            if (victim == null) return;
            int victimId = GetVictimEntityId(victim);

            if (killer != null && IsPlayerHero(killer))
            {
                // Hero dealt the killing blow -> Clear tower attacker record so hero kills are never stolen
                _lastTowerAttacker.Remove(victimId);
                return;
            }

            Unit? tower = killer;
            if (tower == null || !IsTower(tower))
            {
                _lastTowerAttacker.TryGetValue(victimId, out tower);
            }

            if (tower != null && IsTower(tower))
            {
                TryCreditKill(victimId, tower);
            }
        }

        // ─── Damage Hooks ────────────────────────────────────────────────────
        [HarmonyPatch(typeof(SimpleDamageable), nameof(SimpleDamageable.TakeDamage))]
        [HarmonyPostfix]
        private static void SimpleDamageable_TakeDamage_Postfix(SimpleDamageable __instance, float amount, IDamageSource source)
        {
            RecordTowerDamageAndTrackAttacker(__instance, source, amount);
        }

        [HarmonyPatch(typeof(SimpleDamageable), nameof(SimpleDamageable.TakeDirectDamage))]
        [HarmonyPostfix]
        private static void SimpleDamageable_TakeDirectDamage_Postfix(SimpleDamageable __instance, float amount, IDamageSource source)
        {
            RecordTowerDamageAndTrackAttacker(__instance, source, amount);
        }

        [HarmonyPatch(typeof(BuildingDamageable), nameof(BuildingDamageable.TakeDamage))]
        [HarmonyPostfix]
        private static void BuildingDamageable_TakeDamage_Postfix(BuildingDamageable __instance, float amount, IDamageSource source)
        {
            RecordTowerDamageAndTrackAttacker(__instance, source, amount);
        }

        [HarmonyPatch(typeof(BuildingDamageable), nameof(BuildingDamageable.TakeDirectDamage))]
        [HarmonyPostfix]
        private static void BuildingDamageable_TakeDirectDamage_Postfix(BuildingDamageable __instance, float amount, IDamageSource source)
        {
            RecordTowerDamageAndTrackAttacker(__instance, source, amount);
        }

        private static void RecordTowerDamageAndTrackAttacker(Component victimComponent, IDamageSource source, float amount)
        {
            if (victimComponent == null || amount <= 0f) return;

            int victimId = GetVictimEntityId(victimComponent);

            // If damage is from a Player Hero -> Clear tower attacker tracking so player kills are never miscredited to towers
            if (IsHeroDamage(source))
            {
                _lastTowerAttacker.Remove(victimId);
                return;
            }

            Unit? tower = ResolveTowerUnit(source);
            if (tower != null && IsTower(tower))
            {
                _lastTowerAttacker[victimId] = tower;

                var stats = GetOrCreateTowerStats(tower);
                if (stats != null)
                {
                    stats.RecordDamage(amount);
                }

                // Check if victim died from this tower's damage
                if (victimComponent is SimpleDamageable sd)
                {
                    if (sd.CurrentHealth <= 0f || sd.IsDead)
                    {
                        TryCreditKill(victimId, tower);
                    }
                }
            }
        }

        // ─── Kill Hooks ──────────────────────────────────────────────────────
        [HarmonyPatch(typeof(SimpleDamageable), "TryMarkDead")]
        [HarmonyPostfix]
        private static void SimpleDamageable_TryMarkDead_Postfix(SimpleDamageable __instance, IDamageSource source)
        {
            if (__instance == null) return;
            int victimId = GetVictimEntityId(__instance);

            if (IsHeroDamage(source))
            {
                _lastTowerAttacker.Remove(victimId);
                return;
            }

            Unit? tower = ResolveTowerUnit(source);
            if (tower == null)
            {
                _lastTowerAttacker.TryGetValue(victimId, out tower);
            }

            if (tower != null && IsTower(tower))
            {
                TryCreditKill(victimId, tower);
            }
        }

        // ─── Building & Unit Spawning / Rebuilding Hooks ─────────────────────
        [HarmonyPatch(typeof(PlayerBuilding), nameof(PlayerBuilding.OnNetworkSpawn))]
        [HarmonyPostfix]
        private static void PlayerBuilding_OnNetworkSpawn_Postfix(PlayerBuilding __instance)
        {
            EnsureComponentAttachedToBuilding(__instance, resetStats: true);
        }

        [HarmonyPatch(typeof(BuildingSpot), "OnEnable")]
        [HarmonyPostfix]
        private static void BuildingSpot_OnEnable_Postfix(BuildingSpot __instance)
        {
            EnsureComponentAttachedToSpot(__instance, resetStats: true);
        }

        [HarmonyPatch(typeof(Unit), "OnEnable")]
        [HarmonyPostfix]
        private static void Unit_OnEnable_Postfix(Unit __instance)
        {
            if (__instance != null)
            {
                int id = GetVictimEntityId(__instance);
                ResetVictimState(id);

                if (IsTower(__instance))
                {
                    GetOrCreateTowerStats(__instance, resetStats: true);
                }
            }
        }

        [HarmonyPatch(typeof(UnitManager), nameof(UnitManager.RegisterUnit))]
        [HarmonyPostfix]
        private static void UnitManager_RegisterUnit_Postfix(Unit unit)
        {
            if (unit != null)
            {
                int id = GetVictimEntityId(unit);
                ResetVictimState(id);

                if (IsTower(unit))
                {
                    GetOrCreateTowerStats(unit);
                }
            }
        }

        [HarmonyPatch(typeof(Unit), nameof(Unit.OnNetworkSpawn))]
        [HarmonyPostfix]
        private static void Unit_OnNetworkSpawn_Postfix(Unit __instance)
        {
            if (__instance != null)
            {
                int id = GetVictimEntityId(__instance);
                ResetVictimState(id);

                if (IsTower(__instance))
                {
                    GetOrCreateTowerStats(__instance);
                }
            }
        }

        [HarmonyPatch(typeof(BuildingDamageable), nameof(BuildingDamageable.Initialize))]
        [HarmonyPostfix]
        private static void BuildingDamageable_Initialize_Postfix(BuildingDamageable __instance, Unit owner)
        {
            if (owner != null && IsTower(owner))
            {
                GetOrCreateTowerStats(owner, resetStats: true);
            }
        }

        // ─── Match Reset Hook ────────────────────────────────────────────────
        [HarmonyPatch(typeof(GameFlowManager), "DoRematch")]
        [HarmonyPostfix]
        private static void GameFlowManager_DoRematch_Postfix()
        {
            TowerStatsManager.Clear();
            _lastTowerAttacker.Clear();
            _creditedKills.Clear();
        }
    }
}
