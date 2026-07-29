using System;
using System.Collections.Generic;

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
    }
}
