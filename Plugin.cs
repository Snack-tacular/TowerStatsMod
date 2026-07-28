using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace TowerStatsMod
{
    [BepInPlugin("com.antigravity.towerstatsmod", "Tower Stats Mod", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance { get; private set; } = null!;
        internal static ManualLogSource Log { get; private set; } = null!;

        // ─── Mod State ───────────────────────────────────────────────────────
        public static bool IsModEnabled { get; private set; } = true;

        // ─── Config Entries ──────────────────────────────────────────────────
        public static ConfigEntry<KeyboardShortcut> ToggleKey        { get; private set; } = null!;
        public static ConfigEntry<float>            DpsWindowSeconds { get; private set; } = null!;
        public static ConfigEntry<float>            ShowRadius       { get; private set; } = null!;
        public static ConfigEntry<float>            HeightOffset     { get; private set; } = null!;
        public static ConfigEntry<float>            UiScale          { get; private set; } = null!;
        public static ConfigEntry<int>              FontSize         { get; private set; } = null!;
        public static ConfigEntry<bool>             RenderThrough    { get; private set; } = null!;

        private Harmony _harmony = null!;

        private void Awake()
        {
            Instance = this;
            Log = base.Logger;

            BindConfig();

            _harmony = new Harmony("com.antigravity.towerstatsmod");
            _harmony.PatchAll(typeof(Patches));

            Log.LogInfo("🏰 Tower Stats Mod initialized! Press F6 to toggle stats display.");
        }

        private void Update()
        {
            if (ToggleKey.Value.IsDown())
            {
                IsModEnabled = !IsModEnabled;
                Log.LogInfo($"[TowerStatsMod] Tower stats display toggled {(IsModEnabled ? "ON" : "OFF")}");
            }
        }

        private void BindConfig()
        {
            const string sGeneral    = "1 - General";
            const string sAppearance = "2 - Appearance";

            ToggleKey        = Config.Bind(sGeneral, "ToggleKey",        new KeyboardShortcut(KeyCode.F6), "Key to toggle tower stats display (F6).");
            DpsWindowSeconds = Config.Bind(sGeneral, "DpsWindowSeconds", 5.0f,                           "Time window in seconds to calculate rolling current DPS.");
            ShowRadius       = Config.Bind(sGeneral, "ShowRadius",       32.0f,                          "Maximum distance (in units) from player to show tower stats.");

            HeightOffset     = Config.Bind(sAppearance, "HeightOffset",  4.0f,   "Height position offset above tower base.");
            UiScale          = Config.Bind(sAppearance, "UiScale",       0.019f, "Overall scale of the overhead badge.");
            FontSize         = Config.Bind(sAppearance, "FontSize",      16,     "Font size of the kills and DPS text.");
            RenderThrough    = Config.Bind(sAppearance, "RenderThrough", true,   "Render text through/on top of tower geometry (ZTest Always).");
        }

        private void OnDestroy()
        {
            _harmony.UnpatchSelf();
        }
    }
}
