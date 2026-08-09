using System;
using System.Collections.Generic;
using UnityEngine.Scripting.APIUpdating;

namespace POB.Extraction
{
    [Serializable]
    [MovedFrom(true, sourceAssembly: "POB.Runtime")]
    public class ExtractionItemGrid
    {
        public int Width;
        public int Height;
        public List<ExtractionItemPlacement> Placements = new();

        public ExtractionItemGrid(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public bool TryPlace(ExtractionItemInstance item, ExtractionItemDefinition definition, int x, int y, bool rotated)
        {
            if (item == null || definition == null) return false;
            int width = rotated ? definition.Height : definition.Width;
            int height = rotated ? definition.Width : definition.Height;
            if (rotated && !definition.CanRotate) return false;
            if (Contains(item.InstanceId)) return false;
            if (!Fits(x, y, width, height)) return false;
            if (Overlaps(x, y, width, height)) return false;

            Placements.Add(new ExtractionItemPlacement(item.InstanceId, x, y, width, height, rotated));
            return true;
        }

        // 只读探针，供 SG.2 拖拽预览用：不改动任何状态，纯粹复用既有的 Fits/Overlaps 组合。
        public bool CanPlace(int x, int y, int width, int height)
        {
            return Fits(x, y, width, height) && !Overlaps(x, y, width, height);
        }

        public bool TryRemove(string itemInstanceId)
        {
            if (string.IsNullOrEmpty(itemInstanceId)) return false;

            for (int i = 0; i < Placements.Count; i++)
            {
                if (Placements[i].ItemInstanceId != itemInstanceId) continue;
                Placements.RemoveAt(i);
                return true;
            }

            return false;
        }

        public bool TryFindFreeSlot(int width, int height, out int x, out int y)
        {
            x = 0;
            y = 0;
            if (width <= 0 || height <= 0) return false;

            for (int scanY = 0; scanY + height <= Height; scanY++)
            {
                for (int scanX = 0; scanX + width <= Width; scanX++)
                {
                    if (Overlaps(scanX, scanY, width, height)) continue;

                    x = scanX;
                    y = scanY;
                    return true;
                }
            }

            return false;
        }

        // 先按原始朝向找空位，失败且允许旋转时再试一次宽高互换。两个调用方(武器折回/loot 自动摆放)
        // 都需要这个"找不到就试试转 90 度"的兜底，抽到 grid 自己身上，避免各自维护一份重复逻辑。
        public bool TryFindFreeSlotWithRotation(int width, int height, bool canRotate, out int x, out int y, out bool rotated)
        {
            rotated = false;
            if (TryFindFreeSlot(width, height, out x, out y)) return true;
            if (!canRotate) return false;

            rotated = TryFindFreeSlot(height, width, out x, out y);
            return rotated;
        }

        public bool TryGetPlacement(string itemInstanceId, out ExtractionItemPlacement placement)
        {
            if (string.IsNullOrEmpty(itemInstanceId))
            {
                placement = null;
                return false;
            }

            foreach (var candidate in Placements)
            {
                if (candidate.ItemInstanceId != itemInstanceId) continue;
                placement = candidate;
                return true;
            }

            placement = null;
            return false;
        }

        // 供 SD.2 点选放置/交换用：给一个格子坐标，反查"这个格子当前落在哪个 placement 的
        // 占用范围内"(多格物品的任意一格都能查到同一个 placement，不要求正好是左上角)。
        public bool TryGetPlacementAt(int x, int y, out ExtractionItemPlacement placement)
        {
            foreach (var candidate in Placements)
            {
                if (x < candidate.X || x >= candidate.X + candidate.Width) continue;
                if (y < candidate.Y || y >= candidate.Y + candidate.Height) continue;

                placement = candidate;
                return true;
            }

            placement = null;
            return false;
        }

        private bool Contains(string itemInstanceId)
        {
            foreach (var placement in Placements)
            {
                if (placement.ItemInstanceId == itemInstanceId) return true;
            }

            return false;
        }

        private bool Fits(int x, int y, int width, int height)
        {
            return x >= 0 && y >= 0 && width > 0 && height > 0 && x + width <= Width && y + height <= Height;
        }

        private bool Overlaps(int x, int y, int width, int height)
        {
            foreach (var existing in Placements)
            {
                bool separated = x + width <= existing.X
                                 || existing.X + existing.Width <= x
                                 || y + height <= existing.Y
                                 || existing.Y + existing.Height <= y;
                if (!separated) return true;
            }

            return false;
        }
    }
}
