using System;
using System.Collections.Generic;
using HarmonyLib;
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

        // ─── Direct High-Performance Damage Hooks (0 Reflection) ─────────────
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

        // ─── Direct High-Performance Kill Hook (0 Reflection) ────────────────
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

        [HarmonyPatch(typeof(BuildingSpot), "Build")]
        [HarmonyPostfix]
        private static void BuildingSpot_Build_Postfix(BuildingSpot __instance)
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
