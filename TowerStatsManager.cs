using System;
using System.Collections.Generic;
using UnityEngine;

namespace TowerStatsMod
{
    public static class TowerStatsManager
    {
        private static readonly List<TowerStatsComponent> _activeTowers = new List<TowerStatsComponent>();

        public static IReadOnlyList<TowerStatsComponent> ActiveTowers => _activeTowers;

        public static void RegisterTower(TowerStatsComponent tower)
        {
            if (tower != null && !_activeTowers.Contains(tower))
            {
                _activeTowers.Add(tower);
            }
        }

        public static void UnregisterTower(TowerStatsComponent tower)
        {
            if (tower != null)
            {
                _activeTowers.Remove(tower);
            }
        }

        public static void Clear()
        {
            _activeTowers.Clear();
        }

        public static int GetTotalTowerKills()
        {
            int total = 0;
            for (int i = 0; i < _activeTowers.Count; i++)
            {
                if (_activeTowers[i] != null)
                {
                    total += _activeTowers[i].Kills;
                }
            }
            return total;
        }

        public static float GetTotalTowerCurrentDPS()
        {
            float total = 0f;
            for (int i = 0; i < _activeTowers.Count; i++)
            {
                if (_activeTowers[i] != null)
                {
                    total += _activeTowers[i].CurrentDPS;
                }
            }
            return total;
        }

        public static float GetTotalTowerDamage()
        {
            float total = 0f;
            for (int i = 0; i < _activeTowers.Count; i++)
            {
                if (_activeTowers[i] != null)
                {
                    total += _activeTowers[i].TotalDamage;
                }
            }
            return total;
        }
    }
}
