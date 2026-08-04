using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ZeroEngine.Feedback.Editor
{
    public static class FeedbackInstaller
    {
        private const string InstallRoot = "Assets/ZeroEngineGenerated/Feedback";
        private const string ThemePath = InstallRoot + "/FeedbackUiTheme.asset";
        private const string PrefabPath = InstallRoot + "/FeedbackPanel.prefab";

        [MenuItem("ZeroEngine/Feedback/Install Default UI")]
        public static void Install()
        {
            Directory.CreateDirectory(InstallRoot);
            FeedbackUiTheme theme = LoadOrCreateTheme();
            LoadOrCreatePrefab(theme);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                Debug.LogWarning(
                    "[ZeroEngine.Feedback] No EventSystem found. Add one with the input module used by this project.");
            }

            if (theme.Font == null)
            {
                Debug.LogWarning(
                    "[ZeroEngine.Feedback] Assign a TMP font with glyphs for every supported project language.");
            }

            if (!Application.isBatchMode)
            {
                EditorUtility.DisplayDialog(
                    "ZeroEngine Feedback",
                    $"Feedback assets installed under {InstallRoot}.",
                    "OK");
            }
        }

        private static FeedbackUiTheme LoadOrCreateTheme()
        {
            var theme = AssetDatabase.LoadAssetAtPath<FeedbackUiTheme>(ThemePath);
            if (theme != null)
                return theme;

            theme = ScriptableObject.CreateInstance<FeedbackUiTheme>();
            AssetDatabase.CreateAsset(theme, ThemePath);
            return theme;
        }

        private static void LoadOrCreatePrefab(FeedbackUiTheme theme)
        {
            if (AssetDatabase.LoadAssetAtPath<DefaultFeedbackPanelView>(PrefabPath) != null)
                return;

            var temporaryParent = new GameObject("FeedbackPrefabRoot", typeof(RectTransform));
            var configuration = new FeedbackUiConfiguration { Theme = theme };
            DefaultFeedbackPanelView view = DefaultFeedbackPanelView.Create(
                temporaryParent.GetComponent<RectTransform>(),
                configuration);
            PrefabUtility.SaveAsPrefabAsset(view.gameObject, PrefabPath);
            Object.DestroyImmediate(temporaryParent);
        }
    }
}
