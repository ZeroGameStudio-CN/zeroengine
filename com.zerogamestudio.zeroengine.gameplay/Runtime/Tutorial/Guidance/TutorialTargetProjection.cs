using UnityEngine;

namespace ZeroEngine.Tutorial.Guidance
{
    public static class TutorialTargetProjection
    {
        public static Camera UICamera(RectTransform element)
        {
            var canvas = element.GetComponentInParent<Canvas>()?.rootCanvas;
            if (!canvas || canvas.renderMode == RenderMode.ScreenSpaceOverlay) return null;
            return canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>()?.eventCamera ?? canvas.worldCamera ??
                (canvas.renderMode == RenderMode.WorldSpace ? Camera.main : null);
        }

        public static bool TryScreenRect(TutorialTargetReference reference, GameObject target, Camera worldCamera, out Rect rect)
        {
            rect = default;
            if (reference.Kind == TutorialTargetKind.ScreenRegion)
            {
                var region = reference.ScreenRegion;
                rect = new Rect(region.x * Screen.width, region.y * Screen.height, region.width * Screen.width, region.height * Screen.height);
                return rect.width > 0 && rect.height > 0;
            }
            if (!target || !target.activeInHierarchy) return false;
            var ui = target.transform as RectTransform;
            if (reference.Kind != TutorialTargetKind.World && ui)
            {
                foreach (var group in ui.GetComponentsInParent<CanvasGroup>())
                    if (group.alpha < .95f) return false;
                var corners = new Vector3[4];
                ui.GetWorldCorners(corners);
                var camera = UICamera(ui);
                Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
                foreach (var point in corners)
                {
                    var screen = RectTransformUtility.WorldToScreenPoint(camera, point);
                    min = Vector2.Min(min, screen); max = Vector2.Max(max, screen);
                }
                rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
            else
            {
                if (reference.Kind == TutorialTargetKind.UI) return false;
                var camera = worldCamera ? worldCamera : Camera.main;
                if (!camera) return false;
                var bounds = WorldBounds(reference, target);
                Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
                for (int corner = 0; corner < 8; corner++)
                {
                    var offset = Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    var screen = camera.WorldToScreenPoint(bounds.center + offset);
                    if (screen.z <= camera.nearClipPlane) return false;
                    min = Vector2.Min(min, screen); max = Vector2.Max(max, screen);
                }
                rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
            return rect.width > 0 && rect.height > 0 && rect.Overlaps(new Rect(0, 0, Screen.width, Screen.height));
        }

        public static Bounds WorldBounds(TutorialTargetReference reference, GameObject target)
        {
            var collider = target.GetComponent<Collider>();
            if (collider) return collider.bounds;
            var collider2D = target.GetComponent<Collider2D>();
            if (collider2D) return collider2D.bounds;
            var renderer = target.GetComponent<Renderer>();
            return renderer ? renderer.bounds : new Bounds(target.transform.position, reference.WorldSize);
        }

        public static bool Contains(Rect rectangle, TutorialMaskShape shape, float radius, Vector2 point)
        {
            var offset = point - rectangle.center;
            var half = rectangle.size * .5f;
            if (shape == TutorialMaskShape.Circle) return offset.sqrMagnitude <= half.sqrMagnitude;
            if (shape == TutorialMaskShape.Rectangle) return rectangle.Contains(point);
            radius = Mathf.Clamp(radius, 0, Mathf.Min(half.x, half.y));
            var q = new Vector2(Mathf.Abs(offset.x), Mathf.Abs(offset.y)) - half + Vector2.one * radius;
            return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0) <= radius;
        }
    }
}
