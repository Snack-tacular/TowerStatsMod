using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

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

        // ─── Rolling window buffer for DPS ───────────────────────────────────
        private readonly Queue<DamageSample> _rollingSamples = new Queue<DamageSample>(128);

        // ─── UI Badge (Single ScreenSpace Root Canvas Child) ─────────────────
        public RectTransform? BadgeRectTransform { get; private set; }
        private Text? _killsText;
        private Text? _dpsText;
        private Image? _bgImage;

        // ─── State & Optimization ───────────────────────────────────────────
        private bool _initialized;
        private float _nextUIUpdate;
        private float _lastRenderedDPS = -1f;
        private int _lastRenderedKills = -1;

        public void Init(Unit unit)
        {
            _unit = unit;
            TowerStatsManager.RegisterTower(this);
        }

        public void ResetStats()
        {
            TotalDamage = 0f;
            Kills = 0;
            CurrentDPS = 0f;
            _rollingSamples.Clear();
            _lastRenderedDPS = -1f;
            _lastRenderedKills = -1;
            UpdateUIContent();
        }

        private void Start()
        {
            BuildUI();
            _initialized = true;
        }

        private void OnEnable()
        {
            TowerStatsManager.RegisterTower(this);
            if (_initialized && BadgeRectTransform == null)
            {
                BuildUI();
            }
        }

        private void OnDisable()
        {
            TowerStatsManager.UnregisterTower(this);
            if (BadgeRectTransform != null)
            {
                Destroy(BadgeRectTransform.gameObject);
                BadgeRectTransform = null;
            }
        }

        private void OnDestroy()
        {
            TowerStatsManager.UnregisterTower(this);
            if (BadgeRectTransform != null)
            {
                Destroy(BadgeRectTransform.gameObject);
                BadgeRectTransform = null;
            }
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
            if (!Plugin.IsModEnabled || !_initialized) return;

            // Self-heal UI badge if missing
            if (BadgeRectTransform == null || _killsText == null || _dpsText == null)
            {
                BuildUI();
            }

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

            // Refresh UI content at 10Hz
            if (Time.time >= _nextUIUpdate)
            {
                _nextUIUpdate = Time.time + 0.1f;
                UpdateUIContent();
            }
        }

        private void BuildUI()
        {
            if (BadgeRectTransform != null)
            {
                DestroyImmediate(BadgeRectTransform.gameObject);
            }

            // Create badge container inside single global root Canvas (0 extra Canvases!)
            BadgeRectTransform = TowerStatsManager.Instance.CreateBadgeContainer($"TowerBadge_{gameObject.GetInstanceID()}");
            BadgeRectTransform.sizeDelta = new Vector2(100f, 52f);

            // Sleek dark background panel
            _bgImage = BadgeRectTransform.gameObject.AddComponent<Image>();
            _bgImage.color = new Color(0.04f, 0.06f, 0.10f, 0.90f);

            Font mainFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            int fontSz = Plugin.FontSize.Value;

            // 1. Kills text (Top line)
            var killsGo = new GameObject("KillsLabel");
            killsGo.transform.SetParent(BadgeRectTransform, false);
            _killsText = killsGo.AddComponent<Text>();
            _killsText.font = mainFont;
            _killsText.fontSize = fontSz;
            _killsText.fontStyle = FontStyle.Bold;
            _killsText.alignment = TextAnchor.MiddleCenter;
            _killsText.color = new Color(0.95f, 0.95f, 0.95f, 1f);

            var killsRT = killsGo.GetComponent<RectTransform>();
            killsRT.anchorMin = new Vector2(0f, 0.50f);
            killsRT.anchorMax = new Vector2(1f, 1.00f);
            killsRT.offsetMin = new Vector2(2f, 0f);
            killsRT.offsetMax = new Vector2(-2f, 0f);

            // 2. DPS text (Bottom line)
            var dpsGo = new GameObject("DPSLabel");
            dpsGo.transform.SetParent(BadgeRectTransform, false);
            _dpsText = dpsGo.AddComponent<Text>();
            _dpsText.font = mainFont;
            _dpsText.fontSize = fontSz;
            _dpsText.fontStyle = FontStyle.Bold;
            _dpsText.alignment = TextAnchor.MiddleCenter;
            _dpsText.color = new Color(0.35f, 0.85f, 1f, 1f); // Cyan glow for DPS

            var dpsRT = dpsGo.GetComponent<RectTransform>();
            dpsRT.anchorMin = new Vector2(0f, 0.00f);
            dpsRT.anchorMax = new Vector2(1f, 0.50f);
            dpsRT.offsetMin = new Vector2(2f, 0f);
            dpsRT.offsetMax = new Vector2(-2f, 0f);

            UpdateUIContent();
        }

        private void UpdateUIContent()
        {
            if (_killsText == null || _dpsText == null || BadgeRectTransform == null) return;

            // Avoid redundant string & UI preferredWidth recalculations
            if (Mathf.Abs(CurrentDPS - _lastRenderedDPS) < 0.1f && Kills == _lastRenderedKills)
            {
                return;
            }

            _lastRenderedDPS = CurrentDPS;
            _lastRenderedKills = Kills;

            int fontSz = Plugin.FontSize.Value;
            if (_killsText.fontSize != fontSz) _killsText.fontSize = fontSz;
            if (_dpsText.fontSize != fontSz) _dpsText.fontSize = fontSz;

            string dpsFormatted = FormatNumber(CurrentDPS);
            string killsFormatted = Kills.ToString(CultureInfo.InvariantCulture);

            _killsText.text = $"⚔ Kills: {killsFormatted}";
            _dpsText.text = $"⚡ DPS: {dpsFormatted}";

            // Auto-expand background box width
            float preferredWidth = Mathf.Max(_killsText.preferredWidth, _dpsText.preferredWidth);
            float targetWidth = Mathf.Clamp(preferredWidth + 20f, 75f, 320f);

            if (Mathf.Abs(BadgeRectTransform.sizeDelta.x - targetWidth) > 1f)
            {
                BadgeRectTransform.sizeDelta = new Vector2(targetWidth, 52f);
            }
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
