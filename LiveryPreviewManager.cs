using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AdvancedLiverySelection
{
    public static class LiveryPreviewManager
    {
        public static bool IsPreviewing = false;

        // Internal aircraft resolver 
        private static (string goName, object defValue) GetAircraftInfoIndependently(string inputName)
        {
            if (string.IsNullOrEmpty(inputName))
                return (inputName, null);

            // Strip the hidden-X suffix before searching
            string search = inputName.EndsWith("X")
                ? inputName.Substring(0, inputName.Length - 1)
                : inputName;

            Type defType = Type.GetType("AircraftDefinition, Assembly-CSharp");
            if (defType == null) return (search, null);

            UnityEngine.Object[] defs = Resources.FindObjectsOfTypeAll(defType);
            FieldInfo prefabField  = defType.GetField("unitPrefab", BindingFlags.Instance | BindingFlags.Public);
            FieldInfo unitNameField = defType.GetField("unitName",   BindingFlags.Instance | BindingFlags.Public);
            PropertyInfo nameProp  = defType.GetProperty("name",     BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);

            foreach (UnityEngine.Object def in defs)
            {
                // Try definition asset name (e.g. "CI-22 Cricket")
                string assetName = nameProp?.GetValue(def) as string ?? "";
                if (string.Equals(assetName, search, StringComparison.OrdinalIgnoreCase))
                {
                    GameObject prefabGo = prefabField?.GetValue(def) as GameObject;
                    return (prefabGo != null ? prefabGo.name : search, def);
                }

                // Try unitName field
                string unitName = unitNameField?.GetValue(def) as string ?? "";
                if (string.Equals(unitName, search, StringComparison.OrdinalIgnoreCase))
                {
                    GameObject prefabGo = prefabField?.GetValue(def) as GameObject;
                    return (prefabGo != null ? prefabGo.name : search, def);
                }

                // Try unitPrefab GameObject name (e.g. "COIN", "AttackHelo1")
                GameObject go = prefabField?.GetValue(def) as GameObject;
                if (go != null && string.Equals(go.name, search, StringComparison.OrdinalIgnoreCase))
                    return (go.name, def);
            }

            return (search, null);
        }

        // Public preview entry point
        public static void PreviewLivery(string liveryName, string aircraftInternal)
        {
            Type browserType = Type.GetType("EncyclopediaBrowser, Assembly-CSharp");
            if (browserType == null)
            {
                AdvancedLiverySelectionPlugin.Logger.LogError("PreviewLivery: Could not find EncyclopediaBrowser type.");
                return;
            }

            UnityEngine.Object browser = UnityEngine.Object.FindObjectOfType(browserType);
            if (browser == null)
            {
                AdvancedLiverySelectionPlugin.Logger.LogWarning("PreviewLivery: EncyclopediaBrowser not active in scene.");
                return;
            }

            FieldInfo spawnedField = browser.GetType().GetField("spawnedUnit",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (spawnedField == null)
            {
                AdvancedLiverySelectionPlugin.Logger.LogError("PreviewLivery: spawnedUnitField is null.");
                return;
            }

            object spawnedUnit = spawnedField.GetValue(browser);
            if (spawnedUnit == null)
            {
                AdvancedLiverySelectionPlugin.Logger.LogError("PreviewLivery: spawnedUnit is null.");
                return;
            }

            // Get the name of the currently displayed unit (strip "(Clone)" suffix)
            string currentGoName = "";
            PropertyInfo unitNameProp = spawnedUnit.GetType().GetProperty("name",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.FlattenHierarchy);
            if (unitNameProp != null)
            {
                currentGoName = unitNameProp.GetValue(spawnedUnit) as string ?? "";
                if (currentGoName.EndsWith("(Clone)"))
                    currentGoName = currentGoName.Substring(0, currentGoName.Length - 7).TrimEnd();
            }

            // Resolve the target AircraftDefinition independently
            (string targetGoName, object targetDef) = GetAircraftInfoIndependently(aircraftInternal);

            // Also fetch the prefab name from the resolved def (most reliable)
            string targetPrefabName = "";
            if (targetDef != null)
            {
                FieldInfo pf = targetDef.GetType().GetField("unitPrefab", BindingFlags.Instance | BindingFlags.Public);
                GameObject pg = pf?.GetValue(targetDef) as GameObject;
                if (pg != null) targetPrefabName = pg.name;
            }

            bool aircraftMatches =
                string.Equals(targetGoName,     currentGoName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targetPrefabName, currentGoName, StringComparison.OrdinalIgnoreCase);

            AdvancedLiverySelectionPlugin.Logger.LogInfo(
                $"PreviewLivery: target={aircraftInternal} mapped={targetGoName} " +
                $"prefab={targetPrefabName} current={currentGoName} matches={aircraftMatches}");

            if (!aircraftMatches)
            {
                AdvancedLiverySelectionPlugin.Logger.LogWarning(
                    $"PreviewLivery: Aircraft mismatch. Target '{targetGoName}' vs displayed '{currentGoName}'. " +
                    "Open the encyclopedia entry for this aircraft first.");
                return;
            }

            if (targetDef == null)
            {
                AdvancedLiverySelectionPlugin.Logger.LogError($"PreviewLivery: Could not resolve AircraftDefinition for '{aircraftInternal}'.");
                return;
            }

            // Fetch livery options from game (bypass our own filter)
            Type loadoutType = Type.GetType("LoadoutSelector, Assembly-CSharp");
            if (loadoutType == null)
            {
                AdvancedLiverySelectionPlugin.Logger.LogError("PreviewLivery: LoadoutSelector type not found.");
                return;
            }

            MethodInfo getOptionsMethod = loadoutType.GetMethod("GetLiveryOptions",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (getOptionsMethod == null)
            {
                AdvancedLiverySelectionPlugin.Logger.LogError("PreviewLivery: GetLiveryOptions method not found.");
                return;
            }

            Type liveryKeyType = Type.GetType("LiveryKey, Assembly-CSharp");
            Type tupleType     = typeof(ValueTuple<,>).MakeGenericType(liveryKeyType, typeof(string));
            Type listType      = typeof(List<>).MakeGenericType(tupleType);
            IList listObj      = (IList)Activator.CreateInstance(listType);

            IsPreviewing = true; // suspend our own faction filter patch
            try
            {
                getOptionsMethod.Invoke(null, new object[] { listObj, targetDef, null, true });

                int count = listObj.Count;
                AdvancedLiverySelectionPlugin.Logger.LogInfo($"PreviewLivery: {count} liveries available for '{aircraftInternal}'.");

                object targetLiveryKey = null;
                FieldInfo item1Field   = tupleType.GetField("Item1", BindingFlags.Instance | BindingFlags.Public);
                FieldInfo item2Field   = tupleType.GetField("Item2", BindingFlags.Instance | BindingFlags.Public);

                for (int i = 0; i < count; i++)
                {
                    object item  = listObj[i];
                    string label = item2Field?.GetValue(item) as string ?? "";

                    AdvancedLiverySelectionPlugin.Logger.LogInfo($"  - Option: '{label}'");

                    // Match: strip (workshop) noise for comparison
                    string cleanLabel  = label.Replace(" (workshop)", "").Replace(" (Workshop)", "").Trim();
                    string cleanTarget = liveryName.Replace(" (workshop)", "").Replace(" (Workshop)", "").Trim();

                    if (cleanLabel.Equals(cleanTarget, StringComparison.OrdinalIgnoreCase) ||
                        cleanLabel.StartsWith(cleanTarget, StringComparison.OrdinalIgnoreCase) ||
                        cleanTarget.StartsWith(cleanLabel, StringComparison.OrdinalIgnoreCase))
                    {
                        targetLiveryKey = item1Field?.GetValue(item);
                        AdvancedLiverySelectionPlugin.Logger.LogInfo($"PreviewLivery: Matched '{label}'!");
                        break;
                    }
                }

                if (targetLiveryKey != null)
                {
                    // Try 2-arg overload first, then 1-arg fallback
                    MethodInfo setMethod =
                        spawnedUnit.GetType().GetMethod("SetLiveryKey",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            new Type[] { targetLiveryKey.GetType(), typeof(bool) },
                            null) ??
                        spawnedUnit.GetType().GetMethod("SetLiveryKey",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            new Type[] { targetLiveryKey.GetType() },
                            null);

                    if (setMethod != null)
                    {
                        if (setMethod.GetParameters().Length == 2)
                            setMethod.Invoke(spawnedUnit, new object[] { targetLiveryKey, false });
                        else
                            setMethod.Invoke(spawnedUnit, new object[] { targetLiveryKey });

                        AdvancedLiverySelectionPlugin.Logger.LogInfo("PreviewLivery: Successfully called SetLiveryKey!");
                    }
                    else
                    {
                        AdvancedLiverySelectionPlugin.Logger.LogError(
                            $"PreviewLivery: Could not find SetLiveryKey on {spawnedUnit.GetType().Name}.");
                    }
                }
                else
                {
                    AdvancedLiverySelectionPlugin.Logger.LogError(
                        $"PreviewLivery: Could not find livery key for '{liveryName}' on '{aircraftInternal}'.");
                }
            }
            finally
            {
                IsPreviewing = false;
            }
        }
    }
}