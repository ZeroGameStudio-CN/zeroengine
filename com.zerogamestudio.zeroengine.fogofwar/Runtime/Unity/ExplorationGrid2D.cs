using System;
using UnityEngine;

namespace ZeroEngine.FogOfWar.Unity
{
    /// <summary>Current sight, persistent exploration and last-observed terrain share one XY grid.</summary>
    public class ExplorationGrid2D : IVisibilityQuery2D
    {
        private readonly bool[] terrain, opaque;
        private readonly byte[] revealed, visible;
        // R = explored, G = last-observed terrain, B = remembered edge, A = current sight.
        private readonly Color32[] pixels;
        private readonly uint geometryHash;
        private SightPolygon2D currentSight;
        private string rememberedTerrainBase64;
        private RectInt visibilityRegion;
        public Rect Bounds { get; }
        public int Width { get; }
        public int Height { get; }
        public int Revision { get; private set; }
        public int ExploredCellCount { get; private set; }
        public Vector2 CellSize => new(Bounds.width / Width, Bounds.height / Height);

        public ExplorationGrid2D(Rect bounds, int width, int height, bool[] terrain, bool[] opaque)
        {
            if (width < 1 || height < 1 || bounds.width <= 0 || bounds.height <= 0
                || terrain == null || opaque == null || terrain.Length != width * height
                || opaque.Length != terrain.Length)
                throw new ArgumentException("Invalid fog grid geometry.");
            Bounds = bounds;
            Width = width;
            Height = height;
            this.terrain = (bool[])terrain.Clone();
            this.opaque = (bool[])opaque.Clone();
            revealed = new byte[terrain.Length];
            visible = new byte[terrain.Length];
            pixels = new Color32[terrain.Length];
            uint hash = 2166136261;
            for (int i = 0; i < terrain.Length; i++)
            {
                unchecked { hash = (hash ^ (uint)((terrain[i] ? 1 : 0) | (opaque[i] ? 2 : 0))) * 16777619; }
                pixels[i] = new Color32(0, terrain[i] ? (byte)255 : (byte)0, 0, 0);
            }
            geometryHash = hash;
            RebuildRememberedEdges();
        }

        public Vector2 WorldToUV(Vector2 position) =>
            new((position.x - Bounds.xMin) / Bounds.width, (position.y - Bounds.yMin) / Bounds.height);

        public Vector2 CellCenter(int x, int y) => new(
            Bounds.xMin + (x + 0.5f) * CellSize.x, Bounds.yMin + (y + 0.5f) * CellSize.y);

        private int Index(Vector2 position)
        {
            if (!Bounds.Contains(position)) return -1;
            var uv = WorldToUV(position);
            return Mathf.Clamp((int)(uv.y * Height), 0, Height - 1) * Width
                + Mathf.Clamp((int)(uv.x * Width), 0, Width - 1);
        }

        public bool IsExplored(Vector2 position) { int i = Index(position); return i >= 0 && revealed[i] >= 128; }
        public bool IsVisible(Vector2 position)
        { int i = Index(position); return i >= 0 && (currentSight != null ? currentSight.IsVisible(position) : visible[i] >= 128); }
        public bool HasRememberedTerrain(Vector2 position) { int i = Index(position); return i >= 0 && pixels[i].g != 0; }

        public void UpdateGeometry(bool[] currentTerrain, bool[] currentOpaque)
        {
            if (currentTerrain == null || currentOpaque == null || currentTerrain.Length != terrain.Length
                || currentOpaque.Length != opaque.Length) throw new ArgumentException("Map dimensions changed.");
            Array.Copy(currentTerrain, terrain, terrain.Length);
            Array.Copy(currentOpaque, opaque, opaque.Length);
            // Geometry changes become knowledge only when subsequently seen. They never erase exploration.
        }

        public bool Reveal(Vector2 origin, Vector2 radius, float feather, int facing = 1, float forwardExtension = 0)
        {
            currentSight = null;
            var previousRegion = ClearVisibility();
            int originIndex = Index(origin);
            if (originIndex >= 0 && radius.x > 0 && radius.y > 0)
            {
                int ox = originIndex % Width, oy = originIndex / Width;
                float extension = Mathf.Max(0, forwardExtension);
                int rx = Mathf.CeilToInt((radius.x + extension) / CellSize.x) + 1;
                int ry = Mathf.CeilToInt(radius.y / CellSize.y) + 1;
                SetVisibilityRegion(ox - rx, oy - ry, ox + rx + 1, oy + ry + 1);
                for (int y = visibilityRegion.yMin; y < visibilityRegion.yMax; y++)
                for (int x = visibilityRegion.xMin; x < visibilityRegion.xMax; x++)
                {
                    var delta = CellCenter(x, y) - origin;
                    float horizontalRadius = radius.x + (delta.x * facing > 0 ? extension : 0);
                    float distance = new Vector2(delta.x / horizontalRadius, delta.y / radius.y).magnitude;
                    if (distance >= 1f || !HasLineOfSight(ox, oy, x, y)) continue;
                    visible[y * Width + x] = (byte)Mathf.RoundToInt(255f * Mathf.Clamp01(
                        (1f - distance) / Mathf.Clamp(feather, 0.01f, 1f)));
                }
            }

            return CommitVisibility(previousRegion);
        }

        public void SetCurrentSight(SightPolygon2D sight) => currentSight = sight;

        public bool Reveal(SightPolygon2D sight)
        {
            currentSight = sight;
            var previousRegion = ClearVisibility();
            var extent = new Vector2(sight.Radius.x + sight.Forward, sight.Radius.y);
            var min = WorldToUV(sight.Origin - extent); var max = WorldToUV(sight.Origin + extent);
            SetVisibilityRegion(Mathf.FloorToInt(min.x * Width), Mathf.FloorToInt(min.y * Height),
                Mathf.CeilToInt(max.x * Width), Mathf.CeilToInt(max.y * Height));
            for (int y = visibilityRegion.yMin; y < visibilityRegion.yMax; y++)
            for (int x = visibilityRegion.xMin; x < visibilityRegion.xMax; x++)
                if (sight.IsVisible(CellCenter(x, y))) visible[y * Width + x] = 255;
            // The visible surface of a solid belongs to map knowledge even when its cell centre is inside it.
            foreach (var point in sight.Points)
            {
                var observed = Vector2.MoveTowards(point, sight.Origin, .001f);
                int index = Index(point);
                if (index >= 0 && terrain[index] && sight.IsVisible(observed))
                {
                    visible[index] = 255;
                    visibilityRegion = Union(visibilityRegion, new RectInt(index % Width, index / Width, 1, 1));
                }
            }
            return CommitVisibility(previousRegion);
        }

        private RectInt ClearVisibility()
        {
            var previous = visibilityRegion;
            for (int y = previous.yMin; y < previous.yMax; y++)
                Array.Clear(visible, y * Width + previous.xMin, previous.width);
            visibilityRegion = default;
            return previous;
        }

        private void SetVisibilityRegion(int left, int bottom, int right, int top)
        {
            left = Mathf.Clamp(left, 0, Width); bottom = Mathf.Clamp(bottom, 0, Height);
            right = Mathf.Clamp(right, left, Width); top = Mathf.Clamp(top, bottom, Height);
            visibilityRegion = new RectInt(left, bottom, right - left, top - bottom);
        }

        private static RectInt Union(RectInt first, RectInt second)
        {
            if (first.width == 0 || first.height == 0) return second;
            if (second.width == 0 || second.height == 0) return first;
            int left = Math.Min(first.xMin, second.xMin), bottom = Math.Min(first.yMin, second.yMin);
            return new RectInt(left, bottom, Math.Max(first.xMax, second.xMax) - left,
                Math.Max(first.yMax, second.yMax) - bottom);
        }

        private bool CommitVisibility(RectInt previousRegion)
        {
            bool changed = false, memoryChanged = false, terrainChanged = false;
            // Only these cells can have a new alpha or newly observed knowledge. Include
            // the old region so leaving, turning, shrinking or teleporting clears old sight.
            var affected = Union(previousRegion, visibilityRegion);
            for (int y = affected.yMin; y < affected.yMax; y++)
            for (int x = affected.xMin; x < affected.xMax; x++)
            {
                int i = y * Width + x;
                byte amount = visible[i];
                if (pixels[i].a != amount) { pixels[i].a = amount; changed = true; }
                if (revealed[i] < amount)
                {
                    if (revealed[i] < 128 && amount >= 128) ExploredCellCount++;
                    pixels[i].r = revealed[i] = amount;
                    memoryChanged = true;
                }
                byte shape = terrain[i] ? (byte)255 : (byte)0;
                if (amount >= 128 && pixels[i].g != shape)
                {
                    pixels[i].g = shape;
                    terrainChanged = true;
                }
            }
            if (terrainChanged)
            {
                rememberedTerrainBase64 = null;
                RebuildRememberedEdges();
            }
            if (memoryChanged || terrainChanged) Revision++;
            return changed || memoryChanged || terrainChanged;
        }

        // Supercover traversal closes diagonal gaps while allowing the visible face of a wall.
        private bool HasLineOfSight(int x, int y, int targetX, int targetY)
        {
            int dx = targetX - x, dy = targetY - y;
            int nx = Math.Abs(dx), ny = Math.Abs(dy);
            int sx = Math.Sign(dx), sy = Math.Sign(dy);
            int ix = 0, iy = 0;
            while (ix < nx || iy < ny)
            {
                int decision = (1 + 2 * ix) * ny - (1 + 2 * iy) * nx;
                if (decision == 0)
                {
                    if (Blocks(x + sx, y) || Blocks(x, y + sy)) return false;
                    x += sx; y += sy; ix++; iy++;
                }
                else if (decision < 0) { x += sx; ix++; }
                else { y += sy; iy++; }
                if (x == targetX && y == targetY) return true;
                if (Blocks(x, y)) return false;
            }
            return true;
        }

        private bool RememberedTerrain(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height
            && pixels[y * Width + x].g != 0;
        private bool Blocks(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height
            && opaque[y * Width + x];

        private void RebuildRememberedEdges()
        {
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
                pixels[y * Width + x].b = RememberedTerrain(x, y)
                    && (!RememberedTerrain(x - 1, y) || !RememberedTerrain(x + 1, y)
                        || !RememberedTerrain(x, y - 1) || !RememberedTerrain(x, y + 1)) ? (byte)255 : (byte)0;
        }

        public void Upload(Texture2D texture)
        {
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
        }

        public uint GeometryHash => geometryHash;
        public string ExportRevealed() => Convert.ToBase64String(revealed);
        public string ExportRememberedTerrain()
        {
            if (rememberedTerrainBase64 == null)
            {
                var remembered = new byte[pixels.Length];
                for (int i = 0; i < remembered.Length; i++) remembered[i] = pixels[i].g;
                rememberedTerrainBase64 = Convert.ToBase64String(remembered);
            }
            return rememberedTerrainBase64;
        }

        // Validate all payloads before changing memory. Null terrain preserves legacy memory.
        public bool TryRestoreMemory(byte[] explored, byte[] remembered = null)
        {
            if (explored == null || explored.Length != revealed.Length
                || remembered != null && remembered.Length != terrain.Length) return false;
            if (remembered != null)
                foreach (byte value in remembered) if (value != 0 && value != 255) return false;
            ExploredCellCount = 0;
            currentSight = null;
            Array.Clear(visible, 0, visible.Length);
            visibilityRegion = default;
            for (int i = 0; i < explored.Length; i++)
            {
                revealed[i] = pixels[i].r = explored[i];
                pixels[i].a = 0;
                if (remembered != null) pixels[i].g = remembered[i];
                if (explored[i] >= 128) ExploredCellCount++;
            }
            rememberedTerrainBase64 = null;
            RebuildRememberedEdges();
            Revision++;
            return true;
        }
    }
}
