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

        // ─── UI References ───────────────────────────────────────────────────
        private Canvas? _canvas;
        private RectTransform? _canvasRT;
        private Text? _killsText;
        private Text? _dpsText;
        private Image? _bgImage;
        private Camera? _cachedCam;

        // ─── Shared Overlay Materials (Created ONCE for 0 Allocation Overhead) ──
        private static Material? _textOverlayMat;
        public static Material TextOverlayMaterial
        {
            get
            {
                if (_textOverlayMat == null)
                {
                    try
                    {
                        Shader s = Shader.Find("GUI/Text Shader") ?? Shader.Find("UI/Default");
                        if (s != null)
                        {
                            _textOverlayMat = new Material(s);
                            _textOverlayMat.name = "TowerStatsTextOverlayMaterial";
                            _textOverlayMat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                            _textOverlayMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                            _textOverlayMat.renderQueue = 3000;
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log?.LogWarning($"[TowerStatsMod] Failed to create TextOverlayMaterial: {ex.Message}");
                    }
                }
                return _textOverlayMat!;
            }
        }

        private static Material? _imageOverlayMat;
        public static Material ImageOverlayMaterial
        {
            get
            {
                if (_imageOverlayMat == null)
                {
                    try
                    {
                        Shader s = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
                        if (s != null)
                        {
                            _imageOverlayMat = new Material(s);
                            _imageOverlayMat.name = "TowerStatsImageOverlayMaterial";
                            _imageOverlayMat.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                            _imageOverlayMat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                            _imageOverlayMat.renderQueue = 3000;
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log?.LogWarning($"[TowerStatsMod] Failed to create ImageOverlayMaterial: {ex.Message}");
                    }
                }
                return _imageOverlayMat!;
            }
        }

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
            if (_initialized && (_canvas == null || _killsText == null))
            {
                BuildUI();
            }
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
            if (!Plugin.IsModEnabled || !_initialized) return;

            // Self-heal UI if destroyed or missing
            if (_canvas == null || _canvasRT == null || _killsText == null || _dpsText == null)
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

            // Refresh UI at 10Hz to save CPU
            if (Time.time >= _nextUIUpdate)
            {
                _nextUIUpdate = Time.time + 0.1f;
                UpdateUIContent();
            }
        }

        private void LateUpdate()
        {
            if (!Plugin.IsModEnabled || !_initialized) return;

            if (_canvas == null || _canvasRT == null)
            {
                BuildUI();
                if (_canvas == null || _canvasRT == null) return;
            }

            Camera? cam = GetMainCamera();

            // Distance check using pre-cached local player transform (0 GC Allocations!)
            float showRadius = Plugin.ShowRadius.Value;
            Transform? playerT = TowerStatsManager.LocalPlayerTransform;
            if (playerT != null && showRadius > 0f)
            {
                float dist = Vector3.Distance(transform.position, playerT.position);
                if (dist > showRadius)
                {
                    if (_canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(false);
                    return;
                }
            }

            // If player is dead or respawning, playerT is null -> stay active and visible!
            if (!_canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(true);

            // Dynamic scale & position updates from config
            float scaleVal = Plugin.UiScale.Value;
            _canvasRT.localScale = Vector3.one * scaleVal;

            float heightOffset = GetHeightOffset();
            _canvasRT.localPosition = new Vector3(0f, heightOffset, 0f);

            // Rotate canvas to face camera cleanly
            if (cam != null)
            {
                Vector3 dir = _canvas.transform.position - cam.transform.position;
                if (dir != Vector3.zero)
                {
                    _canvas.transform.rotation = Quaternion.LookRotation(dir);
                }
            }
        }

        private void BuildUI()
        {
            // Destroy existing child canvas if any
            var existingCanvas = transform.Find("TowerStatsCanvas");
            if (existingCanvas != null)
            {
                DestroyImmediate(existingCanvas.gameObject);
            }

            // Create Canvas GameObject
            var canvasGo = new GameObject("TowerStatsCanvas");
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 30000; // Highest sorting order

            _canvasRT = canvasGo.GetComponent<RectTransform>();
            _canvasRT.sizeDelta = new Vector2(100f, 52f); // Default initial size (will auto-expand)
            
            float scaleVal = Plugin.UiScale.Value;
            _canvasRT.localScale = Vector3.one * scaleVal;

            float heightOffset = GetHeightOffset();
            _canvasRT.localPosition = new Vector3(0f, heightOffset, 0f);

            // Sleek dark background panel
            _bgImage = canvasGo.AddComponent<Image>();
            _bgImage.color = new Color(0.04f, 0.06f, 0.10f, 0.90f);

            Font mainFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            int fontSz = Plugin.FontSize.Value;

            // 1. Kills text (Top line)
            var killsGo = new GameObject("KillsLabel");
            killsGo.transform.SetParent(canvasGo.transform, false);
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

            // 2. DPS text (Bottom line, below Kills)
            var dpsGo = new GameObject("DPSLabel");
            dpsGo.transform.SetParent(canvasGo.transform, false);
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

            // Assign static shared overlay materials ONCE during BuildUI (0 allocations during update!)
            if (Plugin.RenderThrough.Value)
            {
                if (_bgImage != null) _bgImage.material = ImageOverlayMaterial;
                if (_killsText != null) _killsText.material = TextOverlayMaterial;
                if (_dpsText != null) _dpsText.material = TextOverlayMaterial;
            }

            UpdateUIContent();
        }

        private float GetHeightOffset()
        {
            return Plugin.HeightOffset.Value;
        }

        private void UpdateUIContent()
        {
            if (_killsText == null || _dpsText == null || _canvasRT == null) return;

            // Avoid redundant string & UI preferredWidth recalculations
            if (Mathf.Abs(CurrentDPS - _lastRenderedDPS) < 0.1f && Kills == _lastRenderedKills)
            {
                return;
            }

            _lastRenderedDPS = CurrentDPS;
            _lastRenderedKills = Kills;

            // Update font size dynamically if config changed
            int fontSz = Plugin.FontSize.Value;
            if (_killsText.fontSize != fontSz) _killsText.fontSize = fontSz;
            if (_dpsText.fontSize != fontSz) _dpsText.fontSize = fontSz;

            string dpsFormatted = FormatNumber(CurrentDPS);
            string killsFormatted = Kills.ToString(CultureInfo.InvariantCulture);

            // Matched single-symbol unicode kerning
            _killsText.text = $"⚔ Kills: {killsFormatted}";
            _dpsText.text = $"⚡ DPS: {dpsFormatted}";

            // Calculate exact preferred width for dynamic background box expansion
            float preferredWidth = Mathf.Max(_killsText.preferredWidth, _dpsText.preferredWidth);
            float targetWidth = Mathf.Clamp(preferredWidth + 20f, 75f, 320f);

            if (Mathf.Abs(_canvasRT.sizeDelta.x - targetWidth) > 1f)
            {
                _canvasRT.sizeDelta = new Vector2(targetWidth, 52f);
            }
        }

        public static string FormatNumber(float val)
        {
            if (val < 0.1f) return "0";
            if (val < 1000f) return val.ToString("F0", CultureInfo.InvariantCulture);
            if (val < 1000000f) return (val / 1000f).ToString("F1", CultureInfo.InvariantCulture) + "k";
            return (val / 1000000f).ToString("F2", CultureInfo.InvariantCulture) + "M";
        }

        private Camera? GetMainCamera()
        {
            if (_cachedCam != null && _cachedCam.isActiveAndEnabled) return _cachedCam;
            _cachedCam = Camera.main;
            return _cachedCam;
        }
    }
}
