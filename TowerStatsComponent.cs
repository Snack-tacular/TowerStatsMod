using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace TowerStatsMod
{
    public sealed class TowerStatsComponent : MonoBehaviour
    {
        private struct DamageSample
        {
            public float Time;
            public float Amount;
        }

        // ─── Ref ─────────────────────────────────────────────────────────────
        private Unit? _unit;

        // ─── Stats ───────────────────────────────────────────────────────────
        public float TotalDamage { get; private set; }
        public int Kills { get; private set; }
        public float CurrentDPS { get; private set; }

        // ─── Formatted IMGUI Text & Dimensions (0 GameObjects, 0 Canvases) ───
        public string KillsText { get; private set; } = "⚔ Kills: 0";
        public string DpsText { get; private set; } = "⚡ DPS: 0";
        public float BadgeWidth { get; private set; } = 110f;

        // ─── Rolling window buffer for DPS ───────────────────────────────────
        private readonly Queue<DamageSample> _rollingSamples = new Queue<DamageSample>(128);

        // ─── State & Optimization ───────────────────────────────────────────
        private float _nextUIUpdate;
        private float _lastRenderedDPS = -1f;
        private int _lastRenderedKills = -1;

        public void Init(Unit unit)
        {
            _unit = unit;
            TowerStatsManager.RegisterTower(this);
            // Ensure manager instance exists
            _ = TowerStatsManager.Instance;
        }

        public void ResetStats()
        {
            TotalDamage = 0f;
            Kills = 0;
            CurrentDPS = 0f;
            _rollingSamples.Clear();
            _lastRenderedDPS = -1f;
            _lastRenderedKills = -1;
            UpdateTextData();
        }

        private void OnEnable()
        {
            TowerStatsManager.RegisterTower(this);
            _ = TowerStatsManager.Instance;
        }

        private void OnDisable()
        {
            TowerStatsManager.UnregisterTower(this);
        }

        private void OnDestroy()
        {
            TowerStatsManager.UnregisterTower(this);
        }

        public void RecordDamage(float amount)
        {
            if (amount <= 0f) return;
            TotalDamage += amount;
            _rollingSamples.Enqueue(new DamageSample { Time = Time.time, Amount = amount });
        }

        public void RecordKill(int count = 1)
        {
            if (count <= 0) return;
            Kills += count;
        }

        private void Update()
        {
            if (!Plugin.IsModEnabled) return;

            // Prune rolling window samples
            float window = Plugin.DpsWindowSeconds.Value;
            float cutoff = Time.time - window;
            while (_rollingSamples.Count > 0 && _rollingSamples.Peek().Time < cutoff)
            {
                _rollingSamples.Dequeue();
            }

            // Calculate current DPS over rolling window
            float windowDmg = 0f;
            foreach (var sample in _rollingSamples)
            {
                windowDmg += sample.Amount;
            }
            CurrentDPS = window > 0.1f ? windowDmg / window : 0f;

            // Refresh text data at 10Hz
            if (Time.time >= _nextUIUpdate)
            {
                _nextUIUpdate = Time.time + 0.1f;
                UpdateTextData();
            }
        }

        private void UpdateTextData()
        {
            // Avoid redundant string formatting
            if (Mathf.Abs(CurrentDPS - _lastRenderedDPS) < 0.1f && Kills == _lastRenderedKills)
            {
                return;
            }

            _lastRenderedDPS = CurrentDPS;
            _lastRenderedKills = Kills;

            string dpsFormatted = FormatNumber(CurrentDPS);
            string killsFormatted = Kills.ToString(CultureInfo.InvariantCulture);

            KillsText = $"⚔ Kills: {killsFormatted}";
            DpsText = $"⚡ DPS: {dpsFormatted}";

            // Estimate width for dynamic box sizing without UI layout engine calls
            int maxChars = Mathf.Max(KillsText.Length, DpsText.Length);
            BadgeWidth = Mathf.Clamp(maxChars * 10f + 16f, 100f, 320f);
        }

        public static string FormatNumber(float val)
        {
            if (val < 0.1f) return "0";
            if (val < 1000f) return val.ToString("F0", CultureInfo.InvariantCulture);
            if (val < 1000000f) return (val / 1000f).ToString("F1", CultureInfo.InvariantCulture) + "k";
            return (val / 1000000f).ToString("F2", CultureInfo.InvariantCulture) + "M";
        }
    }
}
