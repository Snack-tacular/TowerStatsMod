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

        // ─── UI References (Child WorldSpace Canvas like BuildingLevelDisplay) ──
        private Canvas? _canvas;
        private RectTransform? _canvasRT;
        private Text? _killsText;
        private Text? _dpsText;
        private Image? _bgImage;
        private Camera? _cam;

        // ─── Shared Render Through Overlay Materials ─────────────────────────
        private static Material? _alwaysOnTopTextMaterial;
        public static Material AlwaysOnTopTextMaterial
        {
            get
            {
                if (_alwaysOnTopTextMaterial == null)
                {
                    try
                    {
                        Shader s = Shader.Find("GUI/Text Shader") ?? Shader.Find("UI/Default");
                        if (s != null)
                        {
                            _alwaysOnTopTextMaterial = new Material(s);
                            _alwaysOnTopTextMaterial.name = "TowerStatsTextOverlayMaterial";
                            _alwaysOnTopTextMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                            _alwaysOnTopTextMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                            _alwaysOnTopTextMaterial.renderQueue = 3000;
                        }
                    }
                    catch { }
                }
                return _alwaysOnTopTextMaterial!;
            }
        }

        private static Material? _alwaysOnTopImageMaterial;
        public static Material AlwaysOnTopImageMaterial
        {
            get
            {
                if (_alwaysOnTopImageMaterial == null)
                {
                    try
                    {
                        Shader s = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
                        if (s != null)
                        {
                            _alwaysOnTopImageMaterial = new Material(s);
                            _alwaysOnTopImageMaterial.name = "TowerStatsImageOverlayMaterial";
                            _alwaysOnTopImageMaterial.SetInt("unity_GUIZTestMode", (int)UnityEngine.Rendering.CompareFunction.Always);
                            _alwaysOnTopImageMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                            _alwaysOnTopImageMaterial.renderQueue = 3000;
                        }
                    }
                    catch { }
                }
                return _alwaysOnTopImageMaterial!;
            }
        }

        // ─── Cached Local Player Resolution (0.5s rate-limited like BuildingLevelDisplay) ──
        private static PlayerInteract? _staticLocalPlayer;
        private static float _staticLastPlayerCheckTime;

        // ─── State & Optimization ───────────────────────────────────────────
        private bool _uiInitialized;
        private float _nextUIUpdate;
        private float _lastRenderedDPS = -1f;
        private int _lastRenderedKills = -1;

        public void Init(Unit unit)
        {
            _unit = unit;
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
            _uiInitialized = true;
        }

        private void OnEnable()
        {
            if (_uiInitialized && (_canvas == null || _killsText == null))
            {
                BuildUI();
            }
        }

        private void OnDestroy()
        {
            if (_canvas != null && _canvas.gameObject != null)
            {
                Destroy(_canvas.gameObject);
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
            if (!Plugin.IsModEnabled) return;

            if (_canvas == null || _canvasRT == null || _killsText == null || _dpsText == null)
            {
                BuildUI();
                if (_canvas == null || _canvasRT == null) return;
            }

            // 1. Distance Culling Check (Rate-limited Local Player resolution like BuildingLevelDisplay)
            PlayerInteract? player = GetLocalPlayer();
            float showRadius = Plugin.ShowRadius.Value;
            if (player != null && player.gameObject != null && player.gameObject.activeInHierarchy && showRadius > 0f)
            {
                float dist = Vector3.Distance(transform.position, player.transform.position);
                if (dist > showRadius)
                {
                    if (_canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(false);
                    return;
                }
            }

            // Stay active if player is dead/respawning or within radius
            if (!_canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(true);

            // 2. Camera Facing Rotation
            if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
            if (_cam != null)
            {
                Vector3 dir = _canvas.transform.position - _cam.transform.position;
                if (dir != Vector3.zero)
                {
                    _canvas.transform.rotation = Quaternion.LookRotation(dir);
                }
            }

            // 3. DPS Rolling Window Calculations
            float window = Plugin.DpsWindowSeconds.Value;
            float cutoff = Time.time - window;
            while (_rollingSamples.Count > 0 && _rollingSamples.Peek().Time < cutoff)
            {
                _rollingSamples.Dequeue();
            }

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
            var existingCanvas = transform.Find("TowerStatsCanvas");
            if (existingCanvas != null)
            {
                DestroyImmediate(existingCanvas.gameObject);
            }

            var canvasGo = new GameObject("TowerStatsCanvas");
            canvasGo.transform.SetParent(transform, false);

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = 30000;

            _canvasRT = canvasGo.GetComponent<RectTransform>();
            _canvasRT.sizeDelta = new Vector2(100f, 52f);
            _canvasRT.localScale = Vector3.one * Plugin.UiScale.Value;
            _canvasRT.localPosition = new Vector3(0f, Plugin.HeightOffset.Value, 0f);

            _bgImage = canvasGo.AddComponent<Image>();
            _bgImage.color = new Color(0.04f, 0.06f, 0.10f, 0.90f);

            Font mainFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            int fontSz = Plugin.FontSize.Value;

            // Top line: Kills
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

            // Bottom line: DPS
            var dpsGo = new GameObject("DPSLabel");
            dpsGo.transform.SetParent(canvasGo.transform, false);
            _dpsText = dpsGo.AddComponent<Text>();
            _dpsText.font = mainFont;
            _dpsText.fontSize = fontSz;
            _dpsText.fontStyle = FontStyle.Bold;
            _dpsText.alignment = TextAnchor.MiddleCenter;
            _dpsText.color = new Color(0.35f, 0.85f, 1f, 1f);

            var dpsRT = dpsGo.GetComponent<RectTransform>();
            dpsRT.anchorMin = new Vector2(0f, 0.00f);
            dpsRT.anchorMax = new Vector2(1f, 0.50f);
            dpsRT.offsetMin = new Vector2(2f, 0f);
            dpsRT.offsetMax = new Vector2(-2f, 0f);

            ApplyOverlayMaterials();
            UpdateUIContent();
        }

        private void ApplyOverlayMaterials()
        {
            if (!Plugin.RenderThrough.Value) return;

            Material imgMat = AlwaysOnTopImageMaterial;
            if (imgMat != null && _bgImage != null) _bgImage.material = imgMat;

            Material txtMat = AlwaysOnTopTextMaterial;
            if (txtMat != null)
            {
                if (_killsText != null) _killsText.material = txtMat;
                if (_dpsText != null) _dpsText.material = txtMat;
            }
        }

        private void UpdateUIContent()
        {
            if (_killsText == null || _dpsText == null || _canvasRT == null) return;

            if (Mathf.Abs(CurrentDPS - _lastRenderedDPS) < 0.1f && Kills == _lastRenderedKills)
            {
                return;
            }

            _lastRenderedDPS = CurrentDPS;
            _lastRenderedKills = Kills;

            int fontSz = Plugin.FontSize.Value;
            if (_killsText.fontSize != fontSz) _killsText.fontSize = fontSz;
            if (_dpsText.fontSize != fontSz) _dpsText.fontSize = fontSz;

            ApplyOverlayMaterials();

            string dpsFormatted = FormatNumber(CurrentDPS);
            string killsFormatted = Kills.ToString(CultureInfo.InvariantCulture);

            _killsText.text = $"⚔ Kills: {killsFormatted}";
            _dpsText.text = $"⚡ DPS: {dpsFormatted}";

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

        // ─── Local Player Resolution matching BuildingLevelDisplay EXACTLY ───
        private static PlayerInteract? GetLocalPlayer()
        {
            if (_staticLocalPlayer != null && _staticLocalPlayer.gameObject != null && _staticLocalPlayer.gameObject.activeInHierarchy)
            {
                return _staticLocalPlayer;
            }

            if (Time.time - _staticLastPlayerCheckTime < 0.5f)
            {
                return _staticLocalPlayer;
            }

            _staticLastPlayerCheckTime = Time.time;
            try
            {
                var players = UnityEngine.Object.FindObjectsOfType<PlayerInteract>();
                if (players != null)
                {
                    foreach (var p in players)
                    {
                        if (p != null && p.gameObject.activeInHierarchy && p.IsOwner)
                        {
                            _staticLocalPlayer = p;
                            return _staticLocalPlayer;
                        }
                    }
                }
            }
            catch { }

            return _staticLocalPlayer;
        }
    }
}
