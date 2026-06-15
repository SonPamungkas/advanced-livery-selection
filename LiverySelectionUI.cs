using System;
using System.Linq;
using UnityEngine;

namespace AdvancedLiverySelection
{
    public class LiverySelectionUI : MonoBehaviour
    {
        private bool _isVisible = false;
        private Rect _windowRect = new Rect(100, 100, 800, 600);
        private Vector2 _scrollPos;
        private int _selectedTab = 0;
        private readonly string[] _tabs = { "Faction & Visibility Mapping", "Options" };

        private const float NoVanillaSkinPollInterval = 2.5f;
        private float _noVanillaSkinTimer = 0f;

        private enum LiverySortMode { Name, Faction, Visibility }
        private LiverySortMode _currentSortMode = LiverySortMode.Name;

        private void Update()
        {
            if (AdvancedLiverySelectionPlugin.UIVisibilityHotkey.Value.IsDown())
                _isVisible = !_isVisible;

            if (AdvancedLiverySelectionPlugin.NoVanillaSkin.Value)
            {
                _noVanillaSkinTimer += Time.deltaTime;
                if (_noVanillaSkinTimer >= NoVanillaSkinPollInterval)
                {
                    _noVanillaSkinTimer = 0f;
                    LiveryManager.EnforceNoVanillaSkin();
                }
            }
            else
            {
                _noVanillaSkinTimer = 0f;
            }
        }

        private void OnGUI()
        {
            if (!_isVisible) return;
            GUI.backgroundColor = Color.grey;
            _windowRect = GUILayout.Window(8493, _windowRect, DrawWindow, "Advanced Livery Selection");
            GUI.backgroundColor = Color.white;
        }

        private void DrawWindow(int id)
        {
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabs);
            GUILayout.Space(10);

            if (_selectedTab == 0)
                DrawMainTab();
            else
                DrawOptionsTab();

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close UI")) _isVisible = false;
            
            GUI.DragWindow();
        }

        private void DrawMainTab()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Scan Custom Livery Folders")) LiveryManager.ScanCustomLiveries();
            if (GUILayout.Button("Scan All Base & Mod Skins")) LiveryManager.ScanGameLiveries();
            GUILayout.EndHorizontal();

            var noVanilla = AdvancedLiverySelectionPlugin.NoVanillaSkin;
            noVanilla.Value = GUILayout.Toggle(noVanilla.Value, " No Vanilla Skin (auto-switch spawned units off built-in liveries)");

            GUILayout.Label("Configure exclusivity or hide liveries from the game menus.\n" +
                            "Custom/workshop liveries are edited directly in their real meta.json — " +
                            "changes apply natively, exactly like editing the file by hand.", GUI.skin.label);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Sort by:", GUILayout.Width(60));
            if (GUILayout.Toggle(_currentSortMode == LiverySortMode.Name, "Name", GUI.skin.button)) _currentSortMode = LiverySortMode.Name;
            if (GUILayout.Toggle(_currentSortMode == LiverySortMode.Faction, "Faction", GUI.skin.button)) _currentSortMode = LiverySortMode.Faction;
            if (GUILayout.Toggle(_currentSortMode == LiverySortMode.Visibility, "Visibility", GUI.skin.button)) _currentSortMode = LiverySortMode.Visibility;
            GUILayout.EndHorizontal();
            GUILayout.Space(5);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            var groups = LiveryManager.LoadedLiveries
                .OrderBy(e => LiveryManager.GetAircraftUnitName(e.Meta.Aircraft))
                .GroupBy(e => LiveryManager.GetAircraftUnitName(e.Meta.Aircraft));

            foreach (var group in groups)
            {
                GUILayout.Label($"— {group.Key} —", GUI.skin.box);

                var sortedGroup = group.AsEnumerable();
                if (_currentSortMode == LiverySortMode.Faction)
                    sortedGroup = group.OrderBy(e => string.IsNullOrEmpty(e.Meta.Faction) ? "ZZZ" : e.Meta.Faction).ThenBy(e => e.Meta.DisplayName);
                else if (_currentSortMode == LiverySortMode.Visibility)
                    sortedGroup = group.OrderBy(e => LiveryManager.IsLiveryHidden(e) ? 1 : 0).ThenBy(e => e.Meta.DisplayName);
                else
                    sortedGroup = group.OrderBy(e => e.Meta.DisplayName);

                foreach (var entry in sortedGroup)
                {
                    LiveryMetadata livery = entry.Meta;
                    GUILayout.BeginHorizontal("box");

                    GUILayout.Label(livery.DisplayName + (entry.IsCustom ? "  [custom/workshop]" : ""), GUILayout.Width(250));

                    // Faction Toggles explicitly bound to (BDF) and (PALA)
                    bool isBoscali = livery.Faction == "Boscali";
                    GUI.backgroundColor = isBoscali ? Color.green : Color.white;
                    if (GUILayout.Button("BDF", GUILayout.Width(100)))
                    {
                        LiveryManager.SetLiveryFaction(entry, isBoscali ? "" : "Boscali");
                    }

                    bool isPrimeva = livery.Faction == "Primeva";
                    GUI.backgroundColor = isPrimeva ? Color.green : Color.white;
                    if (GUILayout.Button("PALA", GUILayout.Width(100)))
                    {
                        LiveryManager.SetLiveryFaction(entry, isPrimeva ? "" : "Primeva");
                    }

                    // Agnostic Toggle
                    bool isAgnostic = string.IsNullOrEmpty(livery.Faction);
                    GUI.backgroundColor = isAgnostic ? Color.green : Color.white;
                    if (GUILayout.Button("Agnostic", GUILayout.Width(75)))
                    {
                        LiveryManager.SetLiveryFaction(entry, "");
                    }

                    GUILayout.FlexibleSpace();

                    // Hide/show only applies to custom/workshop liveries — see documentation.md.
                    if (entry.IsCustom)
                    {
                        bool isHidden = LiveryManager.IsLiveryHidden(entry);
                        GUI.backgroundColor = isHidden ? new Color(0.9f, 0.2f, 0.2f) : Color.white;
                        if (GUILayout.Button(isHidden ? "Hidden" : "Visible", GUILayout.Width(75)))
                        {
                            LiveryManager.ToggleLiveryVisibility(entry);
                        }
                    }
                    else
                    {
                        GUILayout.Space(81);
                    }

                    // Preview Engine Button
                    GUI.backgroundColor = new Color(0.2f, 0.7f, 0.9f);
                    if (GUILayout.Button("Preview", GUILayout.Width(70)))
                    {
                        // Preview matches against the spawned unit's GameObject — needs the GoName, not the unitName
                        LiveryPreviewManager.PreviewLivery(livery.DisplayName, LiveryManager.GetAircraftGoName(livery.Aircraft));
                    }

                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();
                }
                GUILayout.Space(5);
            }

            GUILayout.EndScrollView();
        }

        private void DrawOptionsTab()
        {
            GUILayout.Label($"Scan Folders:\n{AdvancedLiverySelectionPlugin.ScanFolders.Value}");
            GUILayout.Space(20);
            GUILayout.Label("Tip: Modifying configurations here instantly saves to exactly the file it was loaded from inside your LiveriesConfig folder.");
        }
    }
}