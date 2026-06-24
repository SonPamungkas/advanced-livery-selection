using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NuclearOption.AddressableScripts;
using UnityEngine;

namespace AdvancedLiverySelection
{
    public static class LiveryManager
    {
        public static List<LiveryEntry> LoadedLiveries = new List<LiveryEntry>();
        public static List<string> KnownFactions = new List<string> { "Boscali", "Primeva" };

        // Shadow faction-override store for built-ins (no on-disk meta.json exists for them).
        // See documentation.md → "Architecture: native vs. shadow storage".
        private static Dictionary<string, string> LiveryFactionOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> BuiltinConfigPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Custom liveries have no on-disk "hidden" concept eithe
        private static Dictionary<string, bool> CustomHiddenOverrides = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static string CustomHiddenSidecarPath => Path.Combine(PluginDir, "_hidden_customs.json");

        [Serializable]
        private class HiddenSidecar { public List<string> HiddenKeys = new List<string>(); }

        private static void LoadCustomHiddenOverrides()
        {
            CustomHiddenOverrides.Clear();
            try
            {
                if (!File.Exists(CustomHiddenSidecarPath)) return;
                var sidecar = JsonUtility.FromJson<HiddenSidecar>(File.ReadAllText(CustomHiddenSidecarPath));
                if (sidecar?.HiddenKeys == null) return;
                foreach (string key in sidecar.HiddenKeys)
                    CustomHiddenOverrides[key] = true;
            }
            catch (Exception ex)
            {
                AdvancedLiverySelectionPlugin.Logger.LogError($"Failed to load {CustomHiddenSidecarPath}: {ex.Message}");
            }
        }

        private static void SaveCustomHiddenOverrides()
        {
            try
            {
                Directory.CreateDirectory(PluginDir);
                var sidecar = new HiddenSidecar { HiddenKeys = CustomHiddenOverrides.Where(kv => kv.Value).Select(kv => kv.Key).ToList() };
                File.WriteAllText(CustomHiddenSidecarPath, JsonUtility.ToJson(sidecar));
            }
            catch (Exception ex)
            {
                AdvancedLiverySelectionPlugin.Logger.LogError($"Failed to save {CustomHiddenSidecarPath}: {ex.Message}");
            }
        }

        // Used by GetOptionsPostfix to filter custom liveries by our shadow hidden-override sidecar.
        public static bool IsCustomLiveryHidden(string unitName, string displayName) =>
            CustomHiddenOverrides.TryGetValue(MakeLiveryKey(unitName, displayName), out bool hidden) && hidden;

        private static string MakeLiveryKey(string unitName, string displayName) =>
            StripHiddenSuffix(unitName) + "::" + displayName;

        // Resolves GoName/UnitName/Aliases for each aircraft. See documentation.md →
        // "Architecture: native vs. shadow storage" → "Aircraft identity catalog".
        private class AircraftIdentity
        {
            public string GoName;
            public string UnitName;
            public string[] Aliases;
        }

        private static readonly AircraftIdentity[] AircraftCatalog =
        {
            new AircraftIdentity { GoName = "AttackHelo1",   UnitName = "SAH-46 Chicane",  Aliases = new[] { "AttackHelo1", "SAH-46 Chicane", "SAH-46L" } },
            new AircraftIdentity { GoName = "SmallFighter1", UnitName = "FS-20 Vortex",    Aliases = new[] { "SmallFighter1", "FS-20 Vortex", "FS-20B" } },
            new AircraftIdentity { GoName = "FastBomber1",   UnitName = "AB-4 Alkyon",     Aliases = new[] { "FastBomber1", "AB-4 Alkyon", "AB-4M" } },
            new AircraftIdentity { GoName = "UFO",           UnitName = "UF-0",             Aliases = new[] { "UFO" } },
            new AircraftIdentity { GoName = "COIN",          UnitName = "CI-22 Cricket",    Aliases = new[] { "CI-22", "COIN" } },
            new AircraftIdentity { GoName = "EW1",           UnitName = "EW-25 Medusa",    Aliases = new[] { "EW1", "EW-25 Medusa", "EA-25 Medusa", "EA-25B" } },
            new AircraftIdentity { GoName = "QuadVTOL1",     UnitName = "VL-49 Tarantula", Aliases = new[] { "QuadVTOL1", "VL-49 Tarantula", "VL-49D" } },
            new AircraftIdentity { GoName = "Darkreach",     UnitName = "SFB-81 Darkreach", Aliases = new[] { "SFB", "SFB-81" } },
            new AircraftIdentity { GoName = "Trainer",       UnitName = "TA-30 Compass",  Aliases = new[] { "TA-30 Compass", "TA-30YH" } },
            new AircraftIdentity { GoName = "UtilityHelo1",  UnitName = "UH-90 Ibis",      Aliases = new[] { "UH-90 Ibis", "UH-90K" } },
            new AircraftIdentity { GoName = "CAS1",          UnitName = "A-19 Brawler",    Aliases = new[] { "A-19 Brawler", "A-19C" } },
            new AircraftIdentity { GoName = "FS-12",         UnitName = "FS-12 Revoker",   Aliases = new[] { "FS-12 Revoker", "FS-12V" } },
            new AircraftIdentity { GoName = "Multirole1",    UnitName = "KR-67 Ifrit",     Aliases = new[] { "KR-67 Ifrit", "KR-67A" } },

            new AircraftIdentity { GoName = "Aryx_F16M_KingViper",   UnitName = "F-16M King Viper", Aliases = new[] { "Aryx_F16M_KingViper_AircraftDefinition" } },
            new AircraftIdentity { GoName = "Aryx_LightHelicopter1", UnitName = "RAH-72 Knockout",  Aliases = new[] { "Aryx_LightHelicopter1_Definition" } },
            new AircraftIdentity { GoName = "Aryx_LightFighter1",    UnitName = "F-99 Shrike",      Aliases = new[] { "Aryx_LightFighter1_Definition" } },
            new AircraftIdentity { GoName = "Aryx_MiG-15",           UnitName = "MiG-15",           Aliases = new[] { "Aryx_MiG-15_AircraftDefinition" } },
            new AircraftIdentity { GoName = "P_Trisurface1",         UnitName = "FS-3 Ternion",    Aliases = new[] { "P_Trisurface1_definition", "FS-3 Ternion", "FS-3E" } },
            new AircraftIdentity { GoName = "Aryx_MC260_Chimera",    UnitName = "MC-260 Chimera",   Aliases = new[] { "Aryx_MC260_Chimera_Definition" } },
            new AircraftIdentity { GoName = "Kestrel",               UnitName = "FQ-106 Kestrel",   Aliases = new[] { "kestrel_definition" } },
        };

        public static string PluginDir => Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "LiveriesConfig");

        // Name resolution
        private static string StripHiddenSuffix(string name) =>
            !string.IsNullOrEmpty(name) && name.EndsWith("X") ? name.Substring(0, name.Length - 1) : name;

        private static AircraftIdentity FindIdentity(string anyName)
        {
            if (string.IsNullOrEmpty(anyName)) return null;
            string search = StripHiddenSuffix(anyName);

            foreach (var id in AircraftCatalog)
            {
                if (string.Equals(id.GoName, search, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(id.UnitName, search, StringComparison.OrdinalIgnoreCase) ||
                    (id.Aliases != null && Array.Exists(id.Aliases, a => string.Equals(a, search, StringComparison.OrdinalIgnoreCase))))
                    return id;
            }
            return null;
        }

        // Resolves any known alias to the unitPrefab GameObject name — use this to match the spawned preview unit.
        public static string GetAircraftGoName(string anyName)
        {
            if (string.IsNullOrEmpty(anyName)) return "Unknown Aircraft";
            return FindIdentity(anyName)?.GoName ?? StripHiddenSuffix(anyName);
        }

        public static string GetAircraftUnitName(string anyName)
        {
            if (string.IsNullOrEmpty(anyName)) return "Unknown Aircraft";
            return FindIdentity(anyName)?.UnitName ?? StripHiddenSuffix(anyName);
        }

        // Built-in livery storage (LiveriesConfig shadow files; customs use their own real meta.json)

        private static string SafeFileName(string name) =>
            string.Join("_", (name ?? "").Split(Path.GetInvalidFileNameChars()));

        // Per-aircraft subfolder that owns a built-in livery's config (e.g. LiveriesConfig/CI-22 Cricket/).
        private static string AircraftDir(string unitName) =>
            Path.Combine(PluginDir, SafeFileName(StripHiddenSuffix(unitName)));

        // Reads all JSON files in LiveriesConfig (recursively) and rebuilds the runtime state for built-in liveries.
        public static void SyncAndLoadLiveries()
        {
            LoadedLiveries.RemoveAll(e => !e.IsCustom);
            LiveryFactionOverrides.Clear();
            BuiltinConfigPaths.Clear();

            if (!Directory.Exists(PluginDir))
                Directory.CreateDirectory(PluginDir);

            var factionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in Directory.GetFiles(PluginDir, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    string json = File.ReadAllText(file);
                    LiveryMetadata meta = JsonUtility.FromJson<LiveryMetadata>(json);

                    // Delete broken entries with no aircraft/display name
                    if (meta == null || string.IsNullOrEmpty(meta.Aircraft) || string.IsNullOrEmpty(meta.DisplayName))
                    {
                        try { File.Delete(file); } catch { }
                        continue;
                    }

                    string filePath = file;

                    // Migrate flat-layout configs (saved directly in LiveriesConfig/) into per-aircraft subfolder so same-named liveries on different aircraft stop colliding.
                    string expectedDir = AircraftDir(meta.Aircraft);
                    if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(file)), Path.GetFullPath(expectedDir), StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            Directory.CreateDirectory(expectedDir);
                            string movedPath = Path.Combine(expectedDir, Path.GetFileName(file));
                            int moveCounter = 2;
                            while (File.Exists(movedPath))
                            {
                                movedPath = Path.Combine(expectedDir, $"{Path.GetFileNameWithoutExtension(file)}_{moveCounter}.json");
                                moveCounter++;
                            }
                            File.Move(file, movedPath);
                            filePath = movedPath;
                        }
                        catch (Exception ex)
                        {
                            AdvancedLiverySelectionPlugin.Logger.LogError($"Failed to migrate {file} into per-aircraft folder: {ex.Message}");
                        }
                    }

                    string key = MakeLiveryKey(meta.Aircraft, meta.DisplayName);
                    LoadedLiveries.Add(new LiveryEntry { Meta = meta, IsCustom = false });
                    BuiltinConfigPaths[key] = filePath;
                    LiveryFactionOverrides[key] = meta.Faction ?? "";

                    if (!string.IsNullOrEmpty(meta.Faction))
                        factionSet.Add(meta.Faction);
                }
                catch (Exception ex)
                {
                    AdvancedLiverySelectionPlugin.Logger.LogError($"Failed to parse {file}: {ex.Message}");
                }
            }

            if (factionSet.Count > 0)
            {
                KnownFactions = new List<string>(factionSet);
                KnownFactions.Sort();
            }

            AdvancedLiverySelectionPlugin.Logger.LogInfo($"Loaded {BuiltinConfigPaths.Count} built-in livery configs from LiveriesConfig.");
        }

        // Dispatches to the storage the game actually reads
        private static void SaveLivery(LiveryEntry entry)
        {
            if (entry?.Meta == null) return;
            LiveryMetadata meta = entry.Meta;

            try
            {
                if (entry.IsCustom)
                {
                    if (string.IsNullOrEmpty(entry.StoragePath)) return;

                    var native = new LiveryMetaData
                    {
                        DisplayName = meta.DisplayName,
                        Faction     = meta.Faction ?? "",
                        Aircraft    = meta.Aircraft
                    };
                    ModLoader.WriteMetaData(entry.StoragePath, native);

                    // Bust the session skin-metadata cache so the edit applies without a restart.
                    ModLoadCache.HasSkinMetaData = false;

                    AdvancedLiverySelectionPlugin.Logger.LogInfo($"Wrote native meta.json for '{meta.DisplayName}' [{meta.Aircraft}] -> {entry.StoragePath}");
                }
                else
                {
                    string key = MakeLiveryKey(meta.Aircraft, meta.DisplayName);
                    if (!BuiltinConfigPaths.TryGetValue(key, out string filePath)) return;

                    if (LiveryFactionOverrides.TryGetValue(key, out string faction))
                        meta.Faction = faction;

                    string json = JsonUtility.ToJson(meta);
                    File.WriteAllText(filePath, json);
                    AdvancedLiverySelectionPlugin.Logger.LogInfo($"Saved livery config: {meta.DisplayName} [{meta.Aircraft}]");
                }
            }
            catch (Exception ex)
            {
                AdvancedLiverySelectionPlugin.Logger.LogError($"Failed to save livery '{meta.DisplayName}': {ex.Message}");
            }
        }

        // Public API used by UISee documentation.md → "Hide/show & No-Vanilla-Skin".
        public static bool IsLiveryHidden(LiveryEntry entry) =>
            entry?.IsCustom == true && entry.Meta != null && IsCustomLiveryHidden(entry.Meta.Aircraft, entry.Meta.DisplayName);

        public static void ToggleLiveryVisibility(LiveryEntry entry)
        {
            if (entry?.IsCustom != true || entry.Meta == null) return;

            string key = MakeLiveryKey(entry.Meta.Aircraft, entry.Meta.DisplayName);
            bool nowHidden = !(CustomHiddenOverrides.TryGetValue(key, out bool hidden) && hidden);
            CustomHiddenOverrides[key] = nowHidden;
            SaveCustomHiddenOverrides();
            // No cache bust needed — GetOptionsPostfix re-evaluates CustomHiddenOverrides on every call, unlike faction (which is read from the cached on-disk meta.json).
        }

        public static string GetLiveryFaction(string anyAircraftName, string displayName)
        {
            string unitName = GetAircraftUnitName(anyAircraftName);
            if (LiveryFactionOverrides.TryGetValue(MakeLiveryKey(unitName, displayName), out string faction))
                return faction;
            return "";
        }

        public static void SetLiveryFaction(LiveryEntry entry, string faction)
        {
            if (entry?.Meta == null) return;
            entry.Meta.Faction = faction;

            if (!entry.IsCustom)
                LiveryFactionOverrides[MakeLiveryKey(entry.Meta.Aircraft, entry.Meta.DisplayName)] = faction;

            SaveLivery(entry);
        }

        // Discovery (built-in liveries only — customs are discovered via ScanCustomLiveries)
        public static void DiscoverLivery(string displayName, string anyAircraftName)
        {
            if (string.IsNullOrEmpty(displayName)) return;

            string unitName = GetAircraftUnitName(anyAircraftName);
            string key = MakeLiveryKey(unitName, displayName);

            if (BuiltinConfigPaths.ContainsKey(key))
                return;

            var meta = new LiveryMetadata
            {
                DisplayName = displayName,
                Aircraft    = unitName,
                Faction     = ""
            };

            string aircraftDir = AircraftDir(unitName);
            Directory.CreateDirectory(aircraftDir);

            string safeName = SafeFileName(displayName);
            string filePath = Path.Combine(aircraftDir, safeName + ".json");

            int fileCounter = 2;
            while (File.Exists(filePath))
            {
                filePath = Path.Combine(aircraftDir, $"{safeName}_{fileCounter}.json");
                fileCounter++;
            }

            var entry = new LiveryEntry { Meta = meta, IsCustom = false };
            LoadedLiveries.Add(entry);
            BuiltinConfigPaths[key] = filePath;
            LiveryFactionOverrides[key] = "";

            SaveLivery(entry);
            AdvancedLiverySelectionPlugin.Logger.LogInfo($"Discovered new built-in livery: '{displayName}' on {unitName}");
        }

        // Scanning

        // Discovers custom/workshop liveries straight from the game's own mod-folder index
        public static void ScanCustomLiveries()
        {
            LoadedLiveries.RemoveAll(e => e.IsCustom);
            LoadCustomHiddenOverrides();

            int found = 0;
            try { found += DiscoverNativeSkins(ModFolders.AppDataSkins.ListMetaData()); }
            catch (Exception ex) { AdvancedLiverySelectionPlugin.Logger.LogError($"Error reading AppData skins: {ex}"); }

            try { found += DiscoverNativeSkins(ModFolders.WorkshopSkins.ListMetaData()); }
            catch (Exception ex) { AdvancedLiverySelectionPlugin.Logger.LogError($"Error reading Workshop skins: {ex}"); }

            AdvancedLiverySelectionPlugin.Logger.LogInfo($"ScanCustomLiveries: found {found} custom/workshop liveries (read directly from their real meta.json).");
        }

        private static int DiscoverNativeSkins(IEnumerable<LiveryMetaData> metas)
        {
            int count = 0;
            foreach (LiveryMetaData native in metas)
            {
                if (string.IsNullOrEmpty(native.FolderFullPath) || string.IsNullOrEmpty(native.DisplayName))
                    continue;

                LoadedLiveries.Add(new LiveryEntry
                {
                    Meta = new LiveryMetadata
                    {
                        DisplayName = native.DisplayName,
                        Faction     = native.Faction ?? "",
                        Aircraft    = native.Aircraft ?? ""
                    },
                    IsCustom    = true,
                    StoragePath = native.FolderFullPath
                });
                count++;
            }
            return count;
        }

        // Discovers built-in liveries via GetLiveryOptions on every loaded AircraftDefinition.
        public static void ScanGameLiveries()
        {
            try
            {
                var resultsList = new List<(LiveryKey key, string label)>();
                int discovered = 0;

                foreach (AircraftDefinition def in Resources.FindObjectsOfTypeAll<AircraftDefinition>())
                {
                    if (def == null || def.unitPrefab == null) continue;

                    resultsList.Clear();
                    try { LoadoutSelector.GetLiveryOptions(resultsList, def, null, true); }
                    catch { continue; }

                    foreach (var (key, label) in resultsList)
                    {
                        if (string.IsNullOrEmpty(label) || key.Type != LiveryKey.KeyType.Builtin)
                            continue; // custom/workshop liveries come from their real meta.json, not here

                        int before = BuiltinConfigPaths.Count;
                        DiscoverLivery(label, def.unitName);
                        if (BuiltinConfigPaths.Count > before) discovered++;
                    }
                }

                if (discovered > 0)
                    AdvancedLiverySelectionPlugin.Logger.LogInfo($"ScanGameLiveries: discovered {discovered} new built-in liveries.");
            }
            catch (Exception ex)
            {
                AdvancedLiverySelectionPlugin.Logger.LogError($"ScanGameLiveries error: {ex}");
            }
        }

        // Clears generated built-in configs and re-scans from scratch (customs untouched).
        public static void ResetAndRescan()
        {
            if (Directory.Exists(PluginDir))
            {
                foreach (string file in Directory.GetFiles(PluginDir, "*.json", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); }
                    catch (Exception ex)
                    {
                        AdvancedLiverySelectionPlugin.Logger.LogError($"Failed to delete {file}: {ex.Message}");
                    }
                }

                foreach (string dir in Directory.GetDirectories(PluginDir))
                {
                    try { if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir); }
                    catch (Exception ex)
                    {
                        AdvancedLiverySelectionPlugin.Logger.LogError($"Failed to remove empty folder {dir}: {ex.Message}");
                    }
                }
            }

            LoadedLiveries.RemoveAll(e => !e.IsCustom);
            BuiltinConfigPaths.Clear();
            LiveryFactionOverrides.Clear();

            ScanCustomLiveries();
            ScanGameLiveries();
        }

        // Reconciliation pass for the "No Vanilla Skin" auto-toggle
        public static void EnforceNoVanillaSkin()
        {
            try
            {
                var options = new List<(LiveryKey key, string label)>();
                foreach (Aircraft aircraft in Resources.FindObjectsOfTypeAll<Aircraft>())
                {
                    try
                    {
                        if (aircraft == null || aircraft.definition == null) continue;
                        if (!aircraft.gameObject.scene.IsValid()) continue; // skip prefabs/encyclopedia ghosts

                        options.Clear();
                        try { LoadoutSelector.GetLiveryOptions(options, aircraft.definition, null, true); }
                        catch { continue; }

                        bool isVanilla = aircraft.NetworkLiveryKey.Type == LiveryKey.KeyType.Builtin;
                        var customs = options.Where(o => o.key.Type != LiveryKey.KeyType.Builtin).ToList();

                        if (isVanilla && customs.Count > 0)
                        {
                            var pick = customs[UnityEngine.Random.Range(0, customs.Count)];
                            aircraft.SetLiveryKey(pick.key, loadIfUnspawned: true);
                        }
                        else if (!isVanilla && customs.Count == 0)
                        {
                            var builtins = options.Where(o => o.key.Type == LiveryKey.KeyType.Builtin).ToList();
                            if (builtins.Count > 0)
                                aircraft.SetLiveryKey(builtins[UnityEngine.Random.Range(0, builtins.Count)].key, loadIfUnspawned: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        AdvancedLiverySelectionPlugin.Logger.LogError($"EnforceNoVanillaSkin aircraft error: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                AdvancedLiverySelectionPlugin.Logger.LogError($"EnforceNoVanillaSkin error: {ex}");
            }
        }
    }

    // Runtime wrapper distinguishing how a livery is persisted
    public class LiveryEntry
    {
        public LiveryMetadata Meta;
        public bool IsCustom;
        public string StoragePath;
    }

    [Serializable]
    public class LiveryMetadata
    {
        public string DisplayName;
        public string Faction;
        public string Aircraft;
    }
}
