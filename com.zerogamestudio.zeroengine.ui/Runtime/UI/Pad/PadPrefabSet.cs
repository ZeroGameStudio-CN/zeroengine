using System;
using UnityEngine;
using UnityEngine.UI;

namespace ZeroEngine.UI.Pad
{
    /// <summary>One set of authored templates for all PAD pages that use the same design.</summary>
    [CreateAssetMenu(menuName = "ZeroEngine/UI and Presentation/Views/PAD Prefab Set", fileName = "PadPrefabSet")]
    public sealed class PadPrefabSet : ScriptableObject
    {
        [SerializeField] private PadPanelView _panel;
        [SerializeField] private Button _navigationRow;
        [SerializeField] private Button _primaryAction;
        [SerializeField] private PadMetricView _metric;

        public void ConfigureForRuntime(PadPanelView panel, Button navigationRow, Button primaryAction, PadMetricView metric)
        {
            if (!panel || !panel.HasRequiredReferences || !navigationRow || !primaryAction || !metric || !metric.HasRequiredReferences)
                throw new ArgumentException("Assign a configured panel, navigation row, primary action and metric template.");

            _panel = panel;
            _navigationRow = navigationRow;
            _primaryAction = primaryAction;
            _metric = metric;
        }

        public PadPanelView CreatePanel(Transform parent)
        {
            if (!_panel || !_panel.HasRequiredReferences)
                throw new InvalidOperationException("Assign a configured PAD panel prefab.");

            return UIRuntimeObjectFactory.CreateChild(_panel, parent);
        }

        public Button CreateNavigationRow(Transform parent) => CreateButton(_navigationRow, parent);

        public Button CreatePrimaryAction(Transform parent) => CreateButton(_primaryAction, parent);

        public PadMetricView CreateMetric(Transform parent)
        {
            if (!_metric || !_metric.HasRequiredReferences)
                throw new InvalidOperationException("Assign a configured PAD metric prefab.");

            return UIRuntimeObjectFactory.CreateChild(_metric, parent);
        }

        private static Button CreateButton(Button template, Transform parent)
        {
            if (!template)
                throw new InvalidOperationException("Assign the PAD button prefab before creating an instance.");

            return UIRuntimeObjectFactory.CreateChild(template, parent);
        }
    }
}
