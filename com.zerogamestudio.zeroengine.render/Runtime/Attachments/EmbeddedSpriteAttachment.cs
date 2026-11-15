using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Render.Attachments
{
    /// <summary>Presentation-only sprite cropping, depth and posed-surface contact. The host supplies art dimensions.</summary>
    public struct EmbeddedSpriteAttachment
    {
        private static readonly Dictionary<(Sprite, float, float), Sprite> ShaftSprites = new();
        private float hiddenTipLength, depthOffset;
        private Renderer body;
        private Collider2D region;
        private Sprite original, shaft;
        private Vector2 tip;
        private Vector3 flightPosition;
        private Vector3 displayAdjustment;
        private bool previousRenderOff;
        private Vector3 bodyContact;
        private bool hasContact;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearSprites()
        {
            foreach (var sprite in ShaftSprites.Values)
                if (sprite) Object.Destroy(sprite);
            ShaftSprites.Clear();
        }

        public void Begin(SpriteRenderer arrow, Renderer target, Vector2 localTip, float hiddenTipLength, float depthOffset, Collider2D hurtbox = null)
        {
            if (!arrow || !arrow.sprite || !target || !target.enabled ||
                !float.IsFinite(hiddenTipLength) || hiddenTipLength <= 0f || !float.IsFinite(depthOffset)) return;
            this.hiddenTipLength = hiddenTipLength;
            this.depthOffset = depthOffset;
            body = target;
            region = hurtbox;
            tip = localTip;
            original = arrow.sprite;
            previousRenderOff = arrow.forceRenderingOff;
            flightPosition = arrow.transform.localPosition;
            shaft = ShaftSprite(original, tip, hiddenTipLength);
            if (shaft) arrow.sprite = shaft;
            UpdateDepth(arrow);
        }

        public void UpdateDepth(SpriteRenderer arrow)
        {
            if (!body || !arrow || !arrow.transform.parent) return;
            var position = arrow.transform.localPosition - displayAdjustment;
            displayAdjustment = Vector3.zero;
            position.z = arrow.transform.parent.InverseTransformPoint(body.bounds.center).z + depthOffset;
            arrow.transform.localPosition = position;
        }

        public void SetContactVisibility(SpriteRenderer arrow, Vector3 contact, Transform projectile, Camera view)
        {
            if (!original || !arrow || !region) return;
            if (!body || !body.enabled || body.forceRenderingOff || !region.isActiveAndEnabled)
            {
                arrow.forceRenderingOff = true;
                return;
            }

            // Use the visible shaft's actual endpoint, including pivot, flip, local
            // offsets and camera perspective. The stored gameplay hit remains unchanged.
            Vector2 forward = projectile.right;
            var sprite = arrow.sprite;
            bool right = tip.x >= original.bounds.center.x;
            float edge = shaft ? (right ? sprite.bounds.max.x : sprite.bounds.min.x) : tip.x;
            var visibleEnd = new Vector3(arrow.flipX ? -edge : edge, arrow.flipY ? -tip.y : tip.y, 0f);
            var actualEndpoint = arrow.transform.TransformPoint(visibleEnd);
            float depth = body.bounds.center.z;
            if (!TryProjectDepth(view, actualEndpoint, depth, out var bodyEndpoint))
            {
                arrow.forceRenderingOff = true;
                return;
            }
            Vector2 preferred = hasContact
                ? (Vector2)(body.transform.TransformPoint(bodyContact) - bodyEndpoint) : default;
            float headSpan = ((Vector2)arrow.transform.TransformVector(Vector3.right * hiddenTipLength)).magnitude;
            float reach = headSpan * 4f;
            if (!SurfaceAttachmentPlacement.TryFindVisibleContact(body, region, bodyEndpoint, forward, out var shift,
                    preferred, (Vector2)contact - (Vector2)bodyEndpoint, reach, headSpan * 1.5f))
            {
                arrow.forceRenderingOff = true;
                return;
            }
            Vector3 chosen = bodyEndpoint + (Vector3)shift;
            if (!TryProjectDepth(view, chosen, actualEndpoint.z, out var shaftEndpoint))
            {
                arrow.forceRenderingOff = true;
                return;
            }

            Vector2 adjustment = shaftEndpoint - actualEndpoint;
            displayAdjustment = arrow.transform.parent.InverseTransformVector(adjustment);
            arrow.transform.localPosition += displayAdjustment;
            bodyContact = body.transform.InverseTransformPoint(chosen);
            hasContact = true;
            arrow.forceRenderingOff = previousRenderOff;
        }

        public static bool TryProjectDepth(Camera camera, Vector3 point, float depth, out Vector3 projected)
        {
            projected = point;
            if (!camera) { projected.z = depth; return true; }
            var screen = camera.WorldToScreenPoint(point);
            if (screen.z <= camera.nearClipPlane) return false;
            var ray = camera.ScreenPointToRay(screen);
            if (Mathf.Abs(ray.direction.z) < .0001f) return false;
            float distance = (depth - ray.origin.z) / ray.direction.z;
            if (distance < 0f) return false;
            projected = ray.GetPoint(distance);
            return true;
        }


        public void Restore(SpriteRenderer arrow)
        {
            if (original && arrow)
            {
                if (shaft && arrow.sprite == shaft) arrow.sprite = original;
                arrow.forceRenderingOff = previousRenderOff;
                arrow.transform.localPosition = flightPosition;
            }
            this = default;
        }

        private static Sprite ShaftSprite(Sprite sprite, Vector2 tip, float hiddenTipLength)
        {
            if (sprite.packed || !sprite.texture) return null;
            Rect rect = sprite.rect;
            var pivot = sprite.pivot;
            bool pointsRight = tip.x >= sprite.bounds.center.x;
            float head = (tip.x + (pointsRight ? -hiddenTipLength : hiddenTipLength)) * sprite.pixelsPerUnit + pivot.x;
            float left = pointsRight ? 0f : head, right = pointsRight ? head : rect.width;
            if (left < 0f || right > rect.width || right - left < 2f) return null;

            var key = (sprite, left, right);
            if (ShaftSprites.TryGetValue(key, out var existing) && existing) return existing;
            // Use the analytical rect crop, without changing the source or editing
            // geometry on an atlas sprite. Retain the original world-space pivot.
            var clipped = Sprite.Create(sprite.texture, new Rect(rect.x + left, rect.y, right - left, rect.height),
                new Vector2((pivot.x - left) / (right - left), pivot.y / rect.height), sprite.pixelsPerUnit,
                0, SpriteMeshType.FullRect);
            if (!clipped) return null;
            clipped.name = sprite.name + " (embedded shaft)";
            clipped.hideFlags = HideFlags.DontSave;
            ShaftSprites[key] = clipped;
            return clipped;
        }
    }
}
