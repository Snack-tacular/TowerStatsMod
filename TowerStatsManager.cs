using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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

        // ─── Single Global Root Canvas (1 Canvas for ALL Towers = 0 FPS Impact) ───
        private Canvas? _rootCanvas;
        private RectTransform? _rootCanvasRT;
        private CanvasScaler? _canvasScaler;
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
            EnsureRootCanvas();
        }

        public void EnsureRootCanvas()
        {
            if (_rootCanvas != null && _rootCanvasRT != null) return;

            _rootCanvas = gameObject.GetComponent<Canvas>();
            if (_rootCanvas == null) _rootCanvas = gameObject.AddComponent<Canvas>();

            _rootCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _rootCanvas.sortingOrder = 30000; // Render on top of game UI

            _canvasScaler = gameObject.GetComponent<CanvasScaler>();
            if (_canvasScaler == null) _canvasScaler = gameObject.AddComponent<CanvasScaler>();
            _canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _canvasScaler.referenceResolution = new Vector2(1920f, 1080f);

            _rootCanvasRT = _rootCanvas.GetComponent<RectTransform>();
        }

        public RectTransform CreateBadgeContainer(string name)
        {
            EnsureRootCanvas();
            var badgeGo = new GameObject(name);
            badgeGo.transform.SetParent(_rootCanvasRT, false);
            var rt = badgeGo.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
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

        private void LateUpdate()
        {
            if (!Plugin.IsModEnabled || _activeTowers.Count == 0) return;

            Camera? cam = GetMainCamera();
            if (cam == null) return;

            Transform? playerT = LocalPlayerTransform;
            float showRadius = Plugin.ShowRadius.Value;
            float showRadiusSqr = showRadius * showRadius;
            float heightOffset = Plugin.HeightOffset.Value;
            Vector3 offsetVec = new Vector3(0f, heightOffset, 0f);

            for (int i = 0; i < _activeTowers.Count; i++)
            {
                var tower = _activeTowers[i];
                if (tower == null || !tower.gameObject.activeInHierarchy) continue;

                var badgeRT = tower.BadgeRectTransform;
                if (badgeRT == null) continue;

                // Distance culling check relative to local player hero
                if (playerT != null && showRadius > 0f)
                {
                    float sqrDist = (tower.transform.position - playerT.position).sqrMagnitude;
                    if (sqrDist > showRadiusSqr)
                    {
                        if (badgeRT.gameObject.activeSelf) badgeRT.gameObject.SetActive(false);
                        continue;
                    }
                }

                // Screen position calculation
                Vector3 worldPos = tower.transform.position + offsetVec;
                Vector3 screenPos = cam.WorldToScreenPoint(worldPos);

                // Check if tower is in front of camera
                if (screenPos.z > 0f)
                {
                    if (!badgeRT.gameObject.activeSelf) badgeRT.gameObject.SetActive(true);

                    // Convert screen position to canvas anchoredPosition
                    badgeRT.anchoredPosition = new Vector2(
                        screenPos.x - (Screen.width * 0.5f),
                        screenPos.y - (Screen.height * 0.5f)
                    );
                }
                else
                {
                    if (badgeRT.gameObject.activeSelf) badgeRT.gameObject.SetActive(false);
                }
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
