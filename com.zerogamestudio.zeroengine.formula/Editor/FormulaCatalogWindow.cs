using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Formula.Editor
{
    // Compatibility facade. Normal routes open the Catalog page in FormulaWorkbenchWindow.
    [ZeroEngine.EditorUI.EditorUiSurface]
    public sealed class FormulaCatalogWindow : EditorWindow
    {
        [MenuItem("ZeroEngine/Formula/Formula Catalog", priority = 129)]
        private static void Open()
        {
            OpenWithProfile(FormulaEditorProfileRegistry.ActiveProfile);
        }

        public static void OpenWithProfile(FormulaEditorProfile profile)
        {
            FormulaWorkbenchWindow.OpenCatalogWithProfile(profile);
        }

        private void OnEnable()
        {
            EditorApplication.delayCall += RedirectToStudio;
        }

        private void OnDisable()
        {
            EditorApplication.delayCall -= RedirectToStudio;
        }

        private void RedirectToStudio()
        {
            if (!this)
                return;
            FormulaEditorProfile profile = FormulaEditorProfileRegistry.ActiveProfile;
            Close();
            FormulaWorkbenchWindow.OpenCatalogWithProfile(profile);
        }
    }
}
