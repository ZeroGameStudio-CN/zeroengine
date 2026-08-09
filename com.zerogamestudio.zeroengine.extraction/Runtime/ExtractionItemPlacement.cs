using System;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionItemPlacement
    {
        public string ItemInstanceId;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public bool Rotated;

        public ExtractionItemPlacement(string itemInstanceId, int x, int y, int width, int height, bool rotated)
        {
            ItemInstanceId = itemInstanceId;
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Rotated = rotated;
        }
    }
}
