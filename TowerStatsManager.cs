using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace TowerStatsMod
{
    public static class TowerStatsManager
    {
        private static readonly List<TowerStatsComponent> _activeTowers = new List<TowerStatsComponent>();
        public static IReadOnlyList<TowerStatsComponent> ActiveTowers => _activeTowers;

        private static Transform? _cachedLocalPlayerTransform;
        private static float _nextPlayerSearchTime;

        public static Transform? LocalPlayerTransform
        {
            get
            {
                if (Time.time < _nextPlayerSearchTime && _cachedLocalPlayerTransform != null)
                {
                    if (_cachedLocalPlayerTransform.gameObject != null && _cachedLocalPlayerTransform.gameObject.activeInHierarchy)
                    {
                        return _cachedLocalPlayerTransform;
                    }
                }

                _nextPlayerSearchTime = Time.time + 1.0f; // Refresh at most once per second
                _cachedLocalPlayerTransform = ResolveLocalPlayerTransform();
                return _cachedLocalPlayerTransform;
            }
        }

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
            _cachedLocalPlayerTransform = null;
            _nextPlayerSearchTime = 0f;
        }

        private static Transform? ResolveLocalPlayerTransform()
        {
            try
            {
                var units = UnityEngine.Object.FindObjectsByType<Unit>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                if (units != null)
                {
                    foreach (var u in units)
                    {
                        if (u != null && u.gameObject.activeInHierarchy && !u.IsDead && u.IsLocalPlayer)
                        {
                            return u.transform;
                        }
                    }
                }
            }
            catch { }

            try
            {
                var interacts = UnityEngine.Object.FindObjectsByType<PlayerInteract>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                if (interacts != null)
                {
                    foreach (var pi in interacts)
                    {
                        if (pi != null && pi.gameObject.activeInHierarchy && pi.IsLocalPlayer)
                        {
                            var u = pi.GetComponent<Unit>();
                            if (u == null || !u.IsDead)
                            {
                                return pi.transform;
                            }
                        }
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
