using System.Collections.Generic;
using UnityEngine;
using ZeroEngine.TCE;

namespace ZeroEngine.TCE.Presentation
{
    public sealed class TcePresentationRunner : MonoBehaviour
    {
        private readonly List<Entry> entries = new();

        public TcePresentationHandle Play(TceVisualSnapshot snapshot, TcePresentationPlaybackSettings settings, ITceClock clock)
        {
            if (snapshot == null)
            {
                var disposed = new TcePresentationHandle(null);
                disposed.Dispose();
                return disposed;
            }

            settings ??= new TcePresentationPlaybackSettings();
            var entry = new Entry(snapshot, settings, clock);
            entries.Add(entry);
            return new TcePresentationHandle(() =>
            {
                entries.Remove(entry);
                entry.Dispose();
            });
        }

        private void LateUpdate()
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].Tick())
                {
                    entries[i].Dispose();
                    entries.RemoveAt(i);
                }
            }
        }

        private sealed class Entry
        {
            private static Material fallbackMaterial;

            private readonly TceVisualSnapshot snapshot;
            private readonly TcePresentationPlaybackSettings settings;
            private readonly ITceClock clock;
            private readonly float startTime;
            private readonly int tintPropertyId;
            private readonly int mainTexturePropertyId;
            private readonly MaterialPropertyBlock block = new();
            private readonly MaterialPropertyBlock spriteBlock = new();
            private readonly List<SpriteRenderer> spriteRenderers = new();
            private GameObject spriteRoot;
            private Vector3 spriteStartPosition;

            public Entry(TceVisualSnapshot snapshot, TcePresentationPlaybackSettings settings, ITceClock clock)
            {
                this.snapshot = snapshot;
                this.settings = settings;
                this.clock = clock;
                startTime = clock?.Now ?? Time.time;
                tintPropertyId = GetShaderPropertyId(settings.TintPropertyName, "_Color");
                mainTexturePropertyId = GetShaderPropertyId(settings.MainTexturePropertyName, "_MainTex");

                if (snapshot is TceSpriteSnapshot spriteSnapshot)
                    CreateSpriteSnapshot(spriteSnapshot);
                else if (snapshot is TceSpriteLayerSnapshot layerSnapshot)
                    CreateSpriteLayerSnapshot(layerSnapshot);
            }

            public bool Tick()
            {
                float now = clock?.Now ?? Time.time;
                float duration = Mathf.Max(0.01f, settings.Duration);
                float progress = Mathf.Clamp01((now - startTime) / duration);
                Draw(progress);
                return progress >= 1f;
            }

            public void Dispose()
            {
                snapshot.Dispose();
                block.Clear();
                spriteBlock.Clear();

                if (spriteRoot)
                    DestroySnapshotObject(spriteRoot);
                spriteRoot = null;
                spriteRenderers.Clear();
            }

            private void Draw(float progress)
            {
                if (snapshot is TceMeshSnapshot meshSnapshot)
                {
                    DrawMesh(meshSnapshot, progress);
                    return;
                }

                UpdateSpriteMotion(progress);
                UpdateSpriteAlpha(progress);
            }

            private void DrawMesh(TceMeshSnapshot meshSnapshot, float progress)
            {
                if (!meshSnapshot.Mesh)
                    return;

                block.Clear();
                Color tint = settings.Tint;
                tint.a *= ResolveAlphaMultiplier(progress);
                block.SetColor(tintPropertyId, tint);
                if (meshSnapshot.MainTexture)
                    block.SetTexture(mainTexturePropertyId, meshSnapshot.MainTexture);

                Graphics.DrawMesh(
                    meshSnapshot.Mesh,
                    GetPlaybackMatrix(meshSnapshot.Matrix, progress),
                    settings.MaterialOverride ? settings.MaterialOverride : GetFallbackMaterial(),
                    meshSnapshot.Layer,
                    null,
                    0,
                    block,
                    false,
                    false,
                    false);
            }

            private void CreateSpriteSnapshot(TceSpriteSnapshot spriteSnapshot)
            {
                spriteRoot = CreateRoot("Tce Sprite Snapshot", spriteSnapshot.Matrix, spriteSnapshot.Layer);
                spriteStartPosition = spriteRoot.transform.position;
                var renderer = spriteRoot.AddComponent<SpriteRenderer>();
                renderer.sprite = spriteSnapshot.Sprite;
                renderer.sortingLayerID = spriteSnapshot.SortingLayerId;
                renderer.sortingOrder = spriteSnapshot.SortingOrder;
                ApplySpriteTint(renderer, settings.Tint);
                ApplySpriteOverrides(renderer);
                spriteRenderers.Add(renderer);
            }

            private void CreateSpriteLayerSnapshot(TceSpriteLayerSnapshot layerSnapshot)
            {
                spriteRoot = CreateRoot("Tce Sprite Layer Snapshot", layerSnapshot.Matrix, layerSnapshot.Layer);
                spriteStartPosition = spriteRoot.transform.position;
                foreach (TceSpriteLayerFrame layer in layerSnapshot.Layers)
                {
                    var child = new GameObject("Layer");
                    child.transform.SetParent(spriteRoot.transform, false);
                    child.transform.localPosition = layer.LocalPosition;
                    child.transform.localRotation = layer.LocalRotation;
                    child.transform.localScale = layer.LocalScale;

                    var renderer = child.AddComponent<SpriteRenderer>();
                    renderer.sprite = layer.Sprite;
                    renderer.enabled = layer.Enabled;
                    renderer.sortingLayerID = layer.SortingLayerId;
                    renderer.sortingOrder = layer.SortingOrder;
                    ApplySpriteTint(renderer, settings.Tint);
                    ApplySpriteOverrides(renderer);
                    spriteRenderers.Add(renderer);
                }
            }

            private static GameObject CreateRoot(string name, Matrix4x4 matrix, int layer)
            {
                var root = new GameObject(name) { layer = layer };
                Vector4 column = matrix.GetColumn(3);
                root.transform.SetPositionAndRotation(new Vector3(column.x, column.y, column.z), matrix.rotation);
                root.transform.localScale = matrix.lossyScale;
                return root;
            }

            private Matrix4x4 GetPlaybackMatrix(Matrix4x4 matrix, float progress)
            {
                if (settings.Style != TcePresentationStyle.SoulGhost || settings.Direction == Vector3.zero)
                    return matrix;

                Vector3 offset = settings.Direction * progress;
                Vector4 column = matrix.GetColumn(3);
                column.x += offset.x;
                column.y += offset.y;
                column.z += offset.z;
                matrix.SetColumn(3, column);
                return matrix;
            }

            private void UpdateSpriteMotion(float progress)
            {
                if (!spriteRoot || settings.Style != TcePresentationStyle.SoulGhost || settings.Direction == Vector3.zero)
                    return;

                spriteRoot.transform.position = spriteStartPosition + settings.Direction * progress;
            }

            private void UpdateSpriteAlpha(float progress)
            {
                Color tint = settings.Tint;
                tint.a *= ResolveAlphaMultiplier(progress);
                foreach (SpriteRenderer renderer in spriteRenderers)
                {
                    if (renderer)
                        ApplySpriteTint(renderer, tint);
                }
            }

            private void ApplySpriteTint(SpriteRenderer renderer, Color tint)
            {
                renderer.color = tint;
                spriteBlock.Clear();
                spriteBlock.SetColor(tintPropertyId, tint);
                renderer.SetPropertyBlock(spriteBlock);
            }

            private float ResolveAlphaMultiplier(float progress)
            {
                float duration = Mathf.Max(0.01f, settings.Duration);
                float elapsed = Mathf.Clamp01(progress) * duration;
                float fadeDelay = Mathf.Max(0f, settings.FadeDelay);
                if (elapsed <= fadeDelay)
                    return 1f;

                float fadeDuration = settings.FadeDuration > 0f
                    ? settings.FadeDuration
                    : Mathf.Max(0.01f, duration - fadeDelay);
                float fadeProgress = Mathf.Clamp01((elapsed - fadeDelay) / fadeDuration);
                if (settings.AlphaEase != null && settings.AlphaEase.length > 0)
                    return Mathf.Clamp01(settings.AlphaEase.Evaluate(fadeProgress));

                return 1f - fadeProgress;
            }

            private void ApplySpriteOverrides(SpriteRenderer renderer)
            {
                if (!renderer)
                    return;

                if (settings.OverrideSorting)
                {
                    renderer.sortingLayerID = settings.SortingLayerId;
                    renderer.sortingOrder = settings.SortingOrder;
                }

                if (settings.MaterialOverride)
                    renderer.sharedMaterial = settings.MaterialOverride;
            }

            private static int GetShaderPropertyId(string propertyName, string fallbackName)
            {
                return Shader.PropertyToID(string.IsNullOrWhiteSpace(propertyName) ? fallbackName : propertyName);
            }

            private static Material GetFallbackMaterial()
            {
                if (fallbackMaterial)
                    return fallbackMaterial;

                Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
                fallbackMaterial = new Material(shader) { name = "TcePresentationFallbackMaterial" };
                return fallbackMaterial;
            }

            private static void DestroySnapshotObject(Object target)
            {
                if (!target)
                    return;

                if (Application.isPlaying)
                    Destroy(target);
                else
                    DestroyImmediate(target);
            }
        }
    }
}
