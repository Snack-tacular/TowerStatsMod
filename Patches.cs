using System;
using System.Collections.Generic;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace TowerStatsMod
{
    internal static class Patches
    {
        // ─── Filter: Only Towers (Matching BuildingLevelDisplay Enum Filter) ───
        public static bool IsTower(Unit? unit)
        {
            if (unit == null || !unit.isBuilding) return false;

            if (unit.playerBuilding != null)
            {
                var ht = unit.playerBuilding.HouseType;
                if (ht == HouseType.Tower || 
                    ht == HouseType.Tower_level2 || 
                    ht == HouseType.Mage_Tower || 
                    ht == HouseType.Catapult)
                {
                    return true;
                }
                return false;
            }

            try
            {
                if (unit.GetComponent<AutoTower>() != null)
                    return true;
            }
            catch { }

            return false;
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
            catch
            {
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

        // ─── Host / Server Direct Damage Hooks (0 Reflection) ─────────────────
        [HarmonyPatch(typeof(SimpleDamageable), nameof(SimpleDamageable.TakeDamage))]
        [HarmonyPostfix]
        private static void SimpleDamageable_TakeDamage_Postfix(SimpleDamageable __instance, float amount, IDamageSource source)
        {
            if (amount <= 0f || source == null) return;
            try
            {
                Unit? tower = source.SourceUnit;
                if (tower != null && IsTower(tower))
                {
                    var stats = GetOrCreateTowerStats(tower);
                    if (stats != null) stats.RecordDamage(amount);
                }
            }
            catch { }
        }

        [HarmonyPatch(typeof(BuildingDamageable), nameof(BuildingDamageable.TakeDamage))]
        [HarmonyPostfix]
        private static void BuildingDamageable_TakeDamage_Postfix(BuildingDamageable __instance, float amount, IDamageSource source)
        {
            if (amount <= 0f || source == null) return;
            try
            {
                Unit? tower = source.SourceUnit;
                if (tower != null && IsTower(tower))
                {
                    var stats = GetOrCreateTowerStats(tower);
                    if (stats != null) stats.RecordDamage(amount);
                }
            }
            catch { }
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
                        Unit? tower = FindNearestTower(netObj.transform.position);
                        if (tower != null)
                        {
                            var stats = GetOrCreateTowerStats(tower);
                            if (stats != null)
                            {
                                stats.RecordDamage(damage);
                            }

                            var victimUnit = netObj.GetComponent<Unit>();
                            if (victimUnit != null && (victimUnit.IsDead || (victimUnit.Damageable != null && victimUnit.Damageable.CurrentHealth <= 0f)))
                            {
                                stats?.RecordKill(1);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private static Unit? FindNearestTower(Vector3 targetPos)
        {
            float minSqrDist = 1600f; // 40 units max range
            Unit? bestTower = null;

            var towers = TowerStatsManager.ActiveTowers;
            for (int i = 0; i < towers.Count; i++)
            {
                var stats = towers[i];
                if (stats == null) continue;
                Unit u = stats.GetComponent<Unit>();
                if (u != null && u.gameObject.activeInHierarchy)
                {
                    float sqrD = (u.transform.position - targetPos).sqrMagnitude;
                    if (sqrD < minSqrDist)
                    {
                        minSqrDist = sqrD;
                        bestTower = u;
                    }
                }
            }

            return bestTower;
        }

        // ─── Host / Server Direct Kill Hook (0 Reflection) ───────────────────
        [HarmonyPatch(typeof(PlayerStatisticsManager), "OnUnitKilled")]
        [HarmonyPostfix]
        private static void PlayerStatisticsManager_OnUnitKilled_Postfix(Unit victim, Unit killer)
        {
            if (killer == null) return;
            try
            {
                if (IsTower(killer))
                {
                    var stats = GetOrCreateTowerStats(killer);
                    if (stats != null) stats.RecordKill(1);
                }
            }
            catch { }
        }

        // ─── Building & Spot Lifecycle Hooks (Matching BuildingLevelDisplay) ──
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

        [HarmonyPatch(typeof(BuildingDamageable), nameof(BuildingDamageable.Initialize))]
        [HarmonyPostfix]
        private static void BuildingDamageable_Initialize_Postfix(BuildingDamageable __instance, Unit owner)
        {
            if (owner != null && IsTower(owner))
            {
                GetOrCreateTowerStats(owner, resetStats: true);
            }
        }
    }
}
