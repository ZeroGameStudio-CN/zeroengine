using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ZeroEngine.UI.Toast;

namespace ZeroEngine.UI.Editor.Toast
{
    public static class ToastInstaller
    {
        private const string InstallRoot = "Assets/ZeroEngineGenerated/Toast";
        private const string SettingsPath = InstallRoot + "/ToastSettings.asset";
        private const string ItemPrefabPath = InstallRoot + "/ToastItemView.prefab";
        private const string RootPrefabPath = InstallRoot + "/ToastRootPresenter.prefab";

        [MenuItem("ZeroEngine/UI/Install Toast System")]
        public static void Install()
        {
            Directory.CreateDirectory(InstallRoot);

            var settings = LoadOrCreateSettings();
            var itemPrefab = LoadOrCreateItemPrefab();
            LoadOrCreateRootPrefab(settings, itemPrefab);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (!Application.isBatchMode)
                EditorUtility.DisplayDialog("ZeroEngine Toast", $"Toast assets installed under {InstallRoot}. Add ToastRootPresenter.prefab under your UI Canvas.", "OK");
        }

        private static ToastSettings LoadOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<ToastSettings>(SettingsPath);
            if (settings != null) return settings;

            settings = ScriptableObject.CreateInstance<ToastSettings>();
            settings.ResetToDefaults();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            return settings;
        }

        private static ToastItemView LoadOrCreateItemPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<ToastItemView>(ItemPrefabPath);
            if (existing != null) return existing;

            var root = new GameObject("ToastItemView", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Button), typeof(ToastItemView));
            var rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(300f, 100f);

            var background = root.GetComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.88235295f);
            var canvasGroup = root.GetComponent<CanvasGroup>();
            var button = root.GetComponent<Button>();
            button.targetGraphic = background;

            var accent = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            accent.transform.SetParent(root.transform, false);
            var accentRect = (RectTransform)accent.transform;
            accentRect.anchorMin = new Vector2(0f, 0f);
            accentRect.anchorMax = new Vector2(0f, 1f);
            accentRect.sizeDelta = new Vector2(10f, 0f);
            accent.GetComponent<Image>().color = new Color(1f, 0f, 0.01f, 1f);

            var label = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI));
            label.transform.SetParent(root.transform, false);
            var labelRect = (RectTransform)label.transform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(18f, 8f);
            labelRect.offsetMax = new Vector2(-12f, -8f);

            var text = label.GetComponent<TextMeshProUGUI>();
            text.fontSize = 34f;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.raycastTarget = false;

            var view = root.GetComponent<ToastItemView>();
            var serialized = new SerializedObject(view);
            serialized.FindProperty("canvasGroup").objectReferenceValue = canvasGroup;
            serialized.FindProperty("background").objectReferenceValue = background;
            serialized.FindProperty("accent").objectReferenceValue = accent.GetComponent<Image>();
            serialized.FindProperty("messageText").objectReferenceValue = text;
            serialized.FindProperty("button").objectReferenceValue = button;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, ItemPrefabPath).GetComponent<ToastItemView>();
            Object.DestroyImmediate(root);
            return prefab;
        }

        private static void LoadOrCreateRootPrefab(ToastSettings settings, ToastItemView itemPrefab)
        {
            if (AssetDatabase.LoadAssetAtPath<ToastRootPresenter>(RootPrefabPath) != null) return;

            var root = new GameObject("ToastRootPresenter", typeof(RectTransform), typeof(ToastRootPresenter));
            var rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            var topCenter = CreateContainer(root.transform, "TopCenter", ToastAnchor.TopCenter, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -200f), itemPrefab);
            var topRight = CreateContainer(root.transform, "TopRight", ToastAnchor.TopRight, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -24f), itemPrefab);
            var bottomCenter = CreateContainer(root.transform, "BottomCenter", ToastAnchor.BottomCenter, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 96f), itemPrefab);

            var presenter = root.GetComponent<ToastRootPresenter>();
            var presenterSo = new SerializedObject(presenter);
            presenterSo.FindProperty("settings").objectReferenceValue = settings;
            var containers = presenterSo.FindProperty("containers");
            containers.arraySize = 3;
            containers.GetArrayElementAtIndex(0).objectReferenceValue = topCenter;
            containers.GetArrayElementAtIndex(1).objectReferenceValue = topRight;
            containers.GetArrayElementAtIndex(2).objectReferenceValue = bottomCenter;
            presenterSo.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, RootPrefabPath);
            Object.DestroyImmediate(root);
        }

        private static ToastContainer CreateContainer(Transform parent, string name, ToastAnchor anchor, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 position, ToastItemView itemPrefab)
        {
            var container = new GameObject(name, typeof(RectTransform), typeof(ToastContainer));
            container.transform.SetParent(parent, false);
            var rect = (RectTransform)container.transform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = position;

            var toastContainer = container.GetComponent<ToastContainer>();
            var serialized = new SerializedObject(toastContainer);
            serialized.FindProperty("anchor").enumValueIndex = (int)anchor;
            serialized.FindProperty("itemRoot").objectReferenceValue = rect;
            serialized.FindProperty("itemPrefab").objectReferenceValue = itemPrefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return toastContainer;
        }
    }
}
