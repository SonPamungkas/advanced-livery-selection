using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NuclearOption.AddressableScripts;
using UnityEngine;

namespace AdvancedLiverySelection
{
    public static class LiveryFilterPatch
    {
        private const string WorkshopSuffix = " (workshop)";
        private const string AppDataSuffix  = " (app data)";

        public static object CurrentAirbase { get; set; }

        // Applies patches manually (avoids AmbiguousMatchException). Call after harmony.PatchAll().
        public static void ApplyPatch(Harmony harmony)
        {
            try
            {
                Type type = typeof(LoadoutSelector);

                // Target the non-static overload to avoid AmbiguousMatchException
                MethodInfo refreshMethod = type
                    .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy)
                    .FirstOrDefault(m => m.Name == "RefreshLiveryOptions" && !m.IsStatic);

                if (refreshMethod != null)
                {
                    MethodInfo prefix  = AccessTools.Method(typeof(LiveryFilterPatch), nameof(RefreshPrefix));
                    MethodInfo postfix = AccessTools.Method(typeof(LiveryFilterPatch), nameof(RefreshPostfix));
                    harmony.Patch(refreshMethod, new HarmonyMethod(prefix), new HarmonyMethod(postfix));
                    AdvancedLiverySelectionPlugin.Logger.LogInfo($"Patched RefreshLiveryOptions: {refreshMethod}");
                }
                else
                {
                    AdvancedLiverySelectionPlugin.Logger.LogError("Could not find RefreshLiveryOptions on LoadoutSelector!");
                }

                // Use GetDeclaredMethods to avoid pulling in inherited overloads
                MethodInfo getOptionsMethod = AccessTools.GetDeclaredMethods(type)
                    .FirstOrDefault(m => m.Name == "GetLiveryOptions");

                if (getOptionsMethod != null)
                {
                    MethodInfo optionsPrefix  = AccessTools.Method(typeof(LiveryFilterPatch), nameof(GetOptionsPrefix));
                    MethodInfo optionsPostfix = AccessTools.Method(typeof(LiveryFilterPatch), nameof(GetOptionsPostfix));
                    harmony.Patch(getOptionsMethod, new HarmonyMethod(optionsPrefix), new HarmonyMethod(optionsPostfix));
                    AdvancedLiverySelectionPlugin.Logger.LogInfo($"Patched GetLiveryOptions: {getOptionsMethod}");
                }
                else
                {
                    AdvancedLiverySelectionPlugin.Logger.LogError("Could not find GetLiveryOptions on LoadoutSelector!");
                }
            }
            catch (Exception ex)
            {
                AdvancedLiverySelectionPlugin.Logger.LogError($"Error applying patches: {ex}");
            }
        }

        // Harmony Patches

        public static void RefreshPrefix(object __instance, out object __state)
        {
            __state = null;
            try
            {
                FieldInfo airbaseField = AccessTools.Field(__instance.GetType(), "airbase");
                CurrentAirbase = airbaseField != null ? airbaseField.GetValue(__instance) : null;
            }
            catch (Exception ex)
            {
                AdvancedLiverySelectionPlugin.Logger.LogError($"Error in RefreshPrefix: {ex}");
                CurrentAirbase = null;
            }
        }

        public static void RefreshPostfix(object __instance, object __state)
        {
            CurrentAirbase = null;
        }

        // Forces the native faction filter to pass everything through; the postfix then applies our own complete decision.
        public static void GetOptionsPrefix(object[] __args)
        {
            if (__args != null && __args.Length >= 4)
            {
                __args[2] = "";   // aircraftFaction -> neutral, so ValidFaction passes everything
                __args[3] = true; // allowFactionLivery
            }
        }

        // __args[0] = list of (LiveryKey, string) tuples, __args[1] = AircraftDefinition.
        // Applies our authoritative faction + hidden decisions to every entry
        public static void GetOptionsPostfix(object[] __args)
        {
            if (LiveryPreviewManager.IsPreviewing) return;

            try
            {
                if (__args == null || __args.Length < 2 || __args[0] == null || __args[1] == null)
                    return;

                AircraftDefinition aircraftDef = __args[1] as AircraftDefinition;
                if (aircraftDef == null || aircraftDef.aircraftParameters == null) return;

                string unitName = aircraftDef.unitName;

                string currentFaction = ResolveCurrentFaction();

                object listObj = __args[0];
                Type listType = listObj.GetType();
                PropertyInfo countProp = listType.GetProperty("Count");
                PropertyInfo itemProp  = listType.GetProperty("Item");
                MethodInfo   removeAt  = listType.GetMethod("RemoveAt");
                if (countProp == null || itemProp == null || removeAt == null) return;

                // ── Pass 1: discover newly-seen built-in liveries (customs discovered elsewhere) ──
                var builtinNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var livery in aircraftDef.aircraftParameters.liveries)
                    if (!string.IsNullOrEmpty(livery.name))
                        builtinNames.Add(livery.name);

                int count = (int)countProp.GetValue(listObj);
                for (int i = 0; i < count; i++)
                {
                    string label = GetTupleLabel(itemProp.GetValue(listObj, new object[] { i }));
                    if (!string.IsNullOrEmpty(label) && builtinNames.Contains(label))
                        LiveryManager.DiscoverLivery(label, unitName);
                }

                // ── Pass 2: remove user-hidden custom liveries (built-ins can't be hidden — see documentation.md) ──
                List<int> toRemove = new List<int>();
                count = (int)countProp.GetValue(listObj);

                for (int i = 0; i < count; i++)
                {
                    object tuple = itemProp.GetValue(listObj, new object[] { i });
                    string label = GetTupleLabel(tuple);
                    if (string.IsNullOrEmpty(label)) continue;

                    if (TryStripCustomSuffix(label, out string cleanName) &&
                        LiveryManager.IsCustomLiveryHidden(unitName, cleanName))
                    {
                        toRemove.Add(i);
                    }
                }

                for (int i = toRemove.Count - 1; i >= 0; i--)
                    removeAt.Invoke(listObj, new object[] { toRemove[i] });

                if (string.IsNullOrEmpty(currentFaction)) return; // no airbase context -> nothing more to filter against

                // Pass 3: apply our complete faction decision to every remaining entry
                toRemove.Clear();
                count = (int)countProp.GetValue(listObj); // re-read after hidden-removal

                for (int i = 0; i < count; i++)
                {
                    object tuple = itemProp.GetValue(listObj, new object[] { i });
                    string label = GetTupleLabel(tuple);
                    if (string.IsNullOrEmpty(label)) continue;

                    string liveryFaction = TryStripCustomSuffix(label, out string cleanName)
                        ? FindCustomLiveryFaction(cleanName, aircraftDef)
                        : LiveryManager.GetLiveryFaction(unitName, label);

                    if (!string.IsNullOrEmpty(liveryFaction) &&
                        !string.Equals(liveryFaction, currentFaction, StringComparison.OrdinalIgnoreCase))
                    {
                        toRemove.Add(i);
                    }
                }

                for (int i = toRemove.Count - 1; i >= 0; i--)
                    removeAt.Invoke(listObj, new object[] { toRemove[i] });
            }
            catch (Exception ex)
            {
                AdvancedLiverySelectionPlugin.Logger.LogError($"Error in GetOptionsPostfix: {ex}");
            }
        }

        // Helpers
        private static string GetTupleLabel(object tuple)
        {
            FieldInfo labelField = tuple.GetType().GetField("Item2", BindingFlags.Instance | BindingFlags.Public);
            return labelField?.GetValue(tuple) as string;
        }

        // Strips the display-only " (workshop)"/" (app data)" suffix to recover the real DisplayName for ModLoadCache.SkinMetaData lookups.
        private static bool TryStripCustomSuffix(string label, out string cleanName)
        {
            if (label.EndsWith(WorkshopSuffix, StringComparison.OrdinalIgnoreCase))
            {
                cleanName = label.Substring(0, label.Length - WorkshopSuffix.Length);
                return true;
            }
            if (label.EndsWith(AppDataSuffix, StringComparison.OrdinalIgnoreCase))
            {
                cleanName = label.Substring(0, label.Length - AppDataSuffix.Length);
                return true;
            }
            cleanName = label;
            return false;
        }

        private static string FindCustomLiveryFaction(string cleanDisplayName, AircraftDefinition aircraftDef)
        {
            foreach (LiveryMetaData meta in ModLoadCache.SkinMetaData)
            {
                if (string.Equals(meta.DisplayName, cleanDisplayName, StringComparison.OrdinalIgnoreCase) &&
                    meta.CheckAircraft(aircraftDef))
                    return meta.Faction ?? "";
            }
            return "";
        }

        private static string ResolveCurrentFaction()
        {
            Airbase airbase = CurrentAirbase as Airbase;
            return airbase != null ? airbase.CurrentHQ?.faction?.factionName : null;
        }
    }
}
