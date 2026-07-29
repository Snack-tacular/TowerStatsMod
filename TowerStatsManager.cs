using System;
using System.Collections.Generic;
using UnityEngine;

namespace TowerStatsMod
{
    public sealed class TowerStatsManager : MonoBehaviour
    {
        private static TowerStatsManager? _instance;
        public static TowerStatsManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("TowerStatsManagerObject");
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    _instance = go.AddComponent<TowerStatsManager>();
                }
                return _instance;
            }
        }

        private static readonly List<TowerStatsComponent> _activeTowers = new List<TowerStatsComponent>();
        public static IReadOnlyList<TowerStatsComponent> ActiveTowers => _activeTowers;

        // ─── Direct High-Performance IMGUI Styles & Textures (0 Allocations) ───
        private Texture2D? _bgTexture;
        private GUIStyle? _boxStyle;
        private GUIStyle? _killsStyle;
        private GUIStyle? _dpsStyle;
        private bool _stylesInitialized;

        private Camera? _cachedCam;
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

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
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

        private void EnsureStyles()
        {
            if (_stylesInitialized && _boxStyle != null && _killsStyle != null && _dpsStyle != null) return;

            // 1. Create dark background texture (RGBA: 10, 15, 25, 230)
            _bgTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _bgTexture.SetPixel(0, 0, new Color(0.04f, 0.06f, 0.10f, 0.90f));
            _bgTexture.Apply();

            // 2. Box style
            _boxStyle = new GUIStyle();
            _boxStyle.normal.background = _bgTexture;

            int fontSz = Plugin.FontSize.Value;

            // 3. Kills text style (White)
            _killsStyle = new GUIStyle();
            _killsStyle.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _killsStyle.fontSize = fontSz;
            _killsStyle.fontStyle = FontStyle.Bold;
            _killsStyle.alignment = TextAnchor.MiddleCenter;
            _killsStyle.normal.textColor = new Color(0.95f, 0.95f, 0.95f, 1f);

            // 4. DPS text style (Cyan glow)
            _dpsStyle = new GUIStyle();
            _dpsStyle.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _dpsStyle.fontSize = fontSz;
            _dpsStyle.fontStyle = FontStyle.Bold;
            _dpsStyle.alignment = TextAnchor.MiddleCenter;
            _dpsStyle.normal.textColor = new Color(0.35f, 0.85f, 1f, 1f);

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            // CRITICAL PERFORMANCE FIX: Only execute during Repaint event! Ignore Layout, MouseMove, etc.
            if (Event.current.type != EventType.Repaint) return;
            if (!Plugin.IsModEnabled || _activeTowers.Count == 0) return;

            Camera? cam = GetMainCamera();
            if (cam == null) return;

            EnsureStyles();

            Transform? playerT = LocalPlayerTransform;
            float showRadius = Plugin.ShowRadius.Value;
            float showRadiusSqr = showRadius * showRadius;
            float heightOffset = Plugin.HeightOffset.Value;
            Vector3 offsetVec = new Vector3(0f, heightOffset, 0f);

            int screenHeight = Screen.height;

            for (int i = 0; i < _activeTowers.Count; i++)
            {
                var tower = _activeTowers[i];
                if (tower == null || !tower.gameObject.activeInHierarchy) continue;

                // Distance culling check relative to local player hero
                if (playerT != null && showRadiusSqr > 0f)
                {
                    float sqrDist = (tower.transform.position - playerT.position).sqrMagnitude;
                    if (sqrDist > showRadiusSqr) continue;
                }

                Vector3 worldPos = tower.transform.position + offsetVec;
                Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

                // Behind camera check
                if (screenPos.z <= 0f) continue;

                // Convert Unity screen coordinates (y=0 at bottom) to IMGUI coordinates (y=0 at top)
                float guiX = screenPos.x;
                float guiY = screenHeight - screenPos.y;

                float width = tower.BadgeWidth;
                float height = 48f;
                Rect boxRect = new Rect(guiX - (width * 0.5f), guiY - (height * 0.5f), width, height);

                // Draw sleek dark background box (Repaint only = 0 extra calls!)
                GUI.Box(boxRect, GUIContent.none, _boxStyle!);

                // Draw 2-line text overlay
                Rect killsRect = new Rect(boxRect.x, boxRect.y + 2f, boxRect.width, 22f);
                Rect dpsRect = new Rect(boxRect.x, boxRect.y + 22f, boxRect.width, 22f);

                GUI.Label(killsRect, tower.KillsText, _killsStyle!);
                GUI.Label(dpsRect, tower.DpsText, _dpsStyle!);
            }
        }

        private Camera? GetMainCamera()
        {
            if (_cachedCam != null && _cachedCam.isActiveAndEnabled) return _cachedCam;
            _cachedCam = Camera.main;
            return _cachedCam;
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
