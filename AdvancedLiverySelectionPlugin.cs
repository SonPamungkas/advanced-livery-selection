using System;
using System.Collections;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AdvancedLiverySelection
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class AdvancedLiverySelectionPlugin : BaseUnityPlugin
    {
        public const string PluginGuid    = "advance.livery.selection";
        public const string PluginName    = "Advanced Livery Selection";
        public const string PluginVersion = "1.1.0"; // Public release number 2

        public static ConfigEntry<KeyboardShortcut> UIVisibilityHotkey;
        public static ConfigEntry<string>           ScanFolders;
        public static ConfigEntry<bool>             NoVanillaSkin;
        public static new ManualLogSource           Logger;
        public static AdvancedLiverySelectionPlugin Instance;

        private void Awake()
        {
            Instance = this;
            Logger   = base.Logger;
            Logger.LogInfo($"Plugin {PluginName} v{PluginVersion} is loading...");

            // Migrate old config filename
            string targetCfg = Path.Combine(Paths.ConfigPath, "advance.livery.selection.cfg");
            try
            {
                foreach (string old in Directory.GetFiles(Paths.ConfigPath, "*advanceliveryselection.cfg"))
                {
                    if (string.Equals(old, targetCfg, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!File.Exists(targetCfg))
                    {
                        File.Copy(old, targetCfg);
                        base.Config.Reload();
                    }
                    File.Delete(old);
                    Logger.LogInfo($"Migrated old config: {Path.GetFileName(old)}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Config migration error: {ex.Message}");
            }

            // Config bindings
            UIVisibilityHotkey = Config.Bind("General", "UI Hotkey",
                new KeyboardShortcut(KeyCode.L, KeyCode.LeftControl),
                "Toggle the Livery Selection UI window.");

            string defaultScan = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NuclearOption", "Liveries");
            ScanFolders = Config.Bind("General", "Scan Folders", defaultScan,
                "Comma-separated folders to scan for custom livery JSON files.");

            NoVanillaSkin = Config.Bind("General", "No Vanilla Skin", false,
                "While enabled, spawned units wearing a built-in livery are automatically " +
                "switched to a custom/workshop one (if available for their aircraft); units " +
                "with no valid custom option are mechanically kept on/reverted to a built-in.");

            // Load saved livery configs
            LiveryManager.SyncAndLoadLiveries();

            // Attach UI
            gameObject.AddComponent<LiverySelectionUI>();

            // Apply Harmony patches
            Harmony harmony = new Harmony(PluginGuid);
            harmony.PatchAll(); // attribute-based patches (if any)
            LiveryFilterPatch.ApplyPatch(harmony); // manual patches (prototype style)

            // Auto-discover liveries when encyclopedia/gameplay loads
            SceneManager.sceneLoaded += OnSceneLoaded;

            Logger.LogInfo($"Plugin {PluginName} successfully loaded!");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "Encyclopedia" || scene.name == "GameWorld")
                StartCoroutine(DelayedLiveryDiscovery());
        }

        private IEnumerator DelayedLiveryDiscovery()
        {
            // Wait 3 s to let all mod aircraft definitions register before scanning
            yield return new WaitForSecondsRealtime(3f);
            Logger.LogInfo("Running delayed livery discovery...");
            LiveryManager.ScanGameLiveries();
        }
    }
}