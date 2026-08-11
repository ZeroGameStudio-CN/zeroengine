using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroEngine.AbilitySystem;
using ZeroEngine.Inventory; // Future proofing

namespace ZeroEngine.Editor
{
    [ZeroEngine.EditorUI.EditorUiSurface]
    public class GlobalSearchWindow : EditorWindow
    {
        private Vector2 resultsScroll;
        private string[] componentTypes = Array.Empty<string>();

        internal static void OpenWindow()
        {
            var window = GetWindow<GlobalSearchWindow>();
            window.titleContent = new GUIContent("Global Search");
            window.Show();
        }

        public SearchMode Mode = SearchMode.AbilityByComponent;
        public string ComponentTypeSearch;
        public string NameSearchTerm;
        public List<UnityEngine.Object> SearchResults = new List<UnityEngine.Object>();

        public enum SearchMode
        {
            AbilityByComponent,
            ItemByName,
            QuestByName
        }

        private void OnEnable()
        {
            componentTypes = GetAllComponentTypes().ToArray();
            if (string.IsNullOrEmpty(ComponentTypeSearch) && componentTypes.Length > 0)
                ComponentTypeSearch = componentTypes[0];
        }

        private void OnGUI()
        {
            ZeroEngine.EditorUI.EditorUiGUILayout.Header(
                "Global Search",
                "Find abilities, inventory items, and quests across the project");

            ZeroEngine.EditorUI.EditorUiGUILayout.SectionHeader("Search Filter");
            Mode = (SearchMode)GUILayout.Toolbar((int)Mode, new[] { "Ability Component", "Item Name", "Quest Name" });

            if (Mode == SearchMode.AbilityByComponent)
            {
                if (componentTypes.Length == 0)
                    componentTypes = GetAllComponentTypes().ToArray();

                var selectedIndex = Math.Max(0, Array.IndexOf(componentTypes, ComponentTypeSearch));
                selectedIndex = EditorGUILayout.Popup("Component Type", selectedIndex, componentTypes);
                ComponentTypeSearch = componentTypes.Length == 0 ? string.Empty : componentTypes[selectedIndex];
            }
            else
            {
                NameSearchTerm = EditorGUILayout.TextField("Search Name", NameSearchTerm);
            }

            if (ZeroEngine.EditorUI.EditorUiGUILayout.PrimaryButton("Search"))
                PerformSearch();

            ZeroEngine.EditorUI.EditorUiGUILayout.SectionHeader($"Results ({SearchResults.Count})");
            if (SearchResults.Count == 0)
            {
                ZeroEngine.EditorUI.EditorUiGUILayout.EmptyState("No matching assets.");
                return;
            }

            resultsScroll = EditorGUILayout.BeginScrollView(resultsScroll);
            foreach (var result in SearchResults)
                EditorGUILayout.ObjectField(result, typeof(UnityEngine.Object), false);
            EditorGUILayout.EndScrollView();
        }

        private static IEnumerable<string> GetAllComponentTypes()
        {
            var baseType = typeof(ComponentData);
            return AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(s => s.GetTypes())
                .Where(p => baseType.IsAssignableFrom(p) && !p.IsAbstract)
                .Select(t => t.Name)
                .OrderBy(n => n);
        }

        public void PerformSearch()
        {
            SearchResults.Clear();

            if (Mode == SearchMode.AbilityByComponent)
            {
                if (string.IsNullOrEmpty(ComponentTypeSearch)) return;
                SearchAbilitiesByComponent();
            }
            else if (Mode == SearchMode.ItemByName)
            {
                SearchAssets<InventoryItemSO>("t:InventoryItemSO", NameSearchTerm);
            }
            else if (Mode == SearchMode.QuestByName)
            {
                SearchAssets<ZeroEngine.Quest.QuestConfigSO>("t:QuestConfigSO", NameSearchTerm);
            }
        }

        private void SearchAssets<T>(string filter, string nameFilter) where T : UnityEngine.Object
        {
            string[] guids = AssetDatabase.FindAssets(filter);
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset != null)
                {
                    if (string.IsNullOrEmpty(nameFilter) || asset.name.IndexOf(nameFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        SearchResults.Add(asset);
                    }
                }
            }
        }

        private void SearchAbilitiesByComponent()
        {
            string[] guids = AssetDatabase.FindAssets("t:AbilityDataSO");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var ability = AssetDatabase.LoadAssetAtPath<AbilityDataSO>(path);
                
                if (ability != null && AbilityHasComponent(ability, ComponentTypeSearch))
                {
                    SearchResults.Add(ability);
                }
            }
        }

        private bool AbilityHasComponent(AbilityDataSO ability, string typeName)
        {
            // Check Triggers, Conditions, Effects
            if (CheckList(ability.Triggers, typeName)) return true;
            if (CheckList(ability.Conditions, typeName)) return true;
            if (CheckList(ability.Effects, typeName)) return true;
            return false;
        }

        private bool CheckList<T>(List<T> list, string typeName)
        {
            if (list == null) return false;
            foreach (var item in list)
            {
                if (item != null && item.GetType().Name.Contains(typeName)) return true;
            }
            return false;
        }
    }
}
