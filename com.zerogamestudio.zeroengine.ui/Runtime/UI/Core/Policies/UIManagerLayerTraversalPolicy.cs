using System;
using System.Collections.Generic;

namespace ZeroEngine.UI
{
    public static class UIManagerLayerTraversalPolicy
    {
        private static readonly UILayer[] TopViewSearchOrder = CreateTopViewSearchOrder();

        public static IReadOnlyList<UILayer> GetTopViewSearchOrder()
        {
            return TopViewSearchOrder;
        }

        private static UILayer[] CreateTopViewSearchOrder()
        {
            var layers = (UILayer[])Enum.GetValues(typeof(UILayer));
            Array.Sort(layers, (a, b) => ((int)b).CompareTo((int)a));
            return layers;
        }
    }
}
