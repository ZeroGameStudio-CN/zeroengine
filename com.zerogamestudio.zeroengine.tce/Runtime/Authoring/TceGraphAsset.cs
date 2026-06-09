using UnityEngine;

namespace ZeroEngine.TCE
{
    [CreateAssetMenu(menuName = "ZeroEngine/TCE/Graph Asset", fileName = "TceGraphAsset")]
    public sealed class TceGraphAsset : ScriptableObject
    {
        [SerializeField] private string displayName = string.Empty;
        [SerializeField] private string category = string.Empty;
        [SerializeField] private string description = string.Empty;
        [SerializeField] private TceGraph graph = new();

        public string DisplayName => displayName;
        public string Category => category;
        public string Description => description;
        public TceGraph Graph => graph;

        private void OnEnable()
        {
            graph ??= new TceGraph();
        }
    }
}
