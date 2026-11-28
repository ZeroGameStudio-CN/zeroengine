using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ZeroEngine.UI
{
    /// <summary>Finds directional neighbors for one uGUI hierarchy.</summary>
    public sealed class DirectionalFocusNavigation
    {
        private readonly Transform root;
        private readonly Predicate<Selectable> include;
        private readonly bool wrapAround;
        private readonly List<Selectable> selectables = new();
        private readonly Dictionary<ScrollRect, Selectable> scrollFocus = new();
        private readonly Vector3[] corners = new Vector3[4];

        public DirectionalFocusNavigation(Transform root, bool wrapAround = false, Predicate<Selectable> include = null)
        {
            this.root = root ? root : throw new ArgumentNullException(nameof(root));
            this.include = include;
            this.wrapAround = wrapAround;
        }

        public bool CanFocus(Selectable candidate) =>
            root && candidate && candidate.isActiveAndEnabled && candidate.IsInteractable() &&
            (candidate.transform == root || candidate.transform.IsChildOf(root)) &&
            (include?.Invoke(candidate) ?? (candidate.navigation.mode != Navigation.Mode.None));

        public Selectable FindNext(Selectable current, MoveDirection direction, Selectable preferred = null)
        {
            if (!CanFocus(current) || direction == MoveDirection.None)
                return null;
            if (preferred != current && CanFocus(preferred))
                return preferred;

            bool horizontal = direction is MoveDirection.Left or MoveDirection.Right;
            float sign = direction is MoveDirection.Left or MoveDirection.Down ? -1f : 1f;
            Matrix4x4 toLocal = root.worldToLocalMatrix;
            Rect origin = GetBounds(current, toLocal);
            var originScroll = FindScroll(current);
            var aligned = default(Candidate);
            var diagonal = default(Candidate);
            var wrapped = default(Candidate);
            root.GetComponentsInChildren(false, selectables);

            try
            {
                foreach (var target in selectables)
                {
                    if (target == current || !CanFocus(target))
                        continue;

                    Rect bounds = GetBounds(target, toLocal);
                    Vector2 difference = bounds.center - origin.center;
                    float forward = (horizontal ? difference.x : difference.y) * sign;
                    float across = Mathf.Abs(horizontal ? difference.y : difference.x);
                    float size = horizontal ? origin.height : origin.width;
                    bool sameLine = across <= Mathf.Max(1f, size * .4f) &&
                        OverlapsAcrossAxis(origin, bounds, horizontal);
                    var candidate = new Candidate(target, Mathf.Abs(forward), across);

                    if (forward > .001f)
                    {
                        float distanceSquared = difference.sqrMagnitude;
                        if (sameLine && forward * forward >= .49f * distanceSquared)
                            KeepNearest(ref aligned, candidate, primaryFirst: horizontal);
                        else if (forward * forward >= .25f * distanceSquared)
                            KeepNearest(ref diagonal, candidate, primaryFirst: true);
                    }
                    else if (wrapAround && forward < -.001f && sameLine && FindScroll(target) == originScroll)
                    {
                        if (!wrapped.Target || candidate.Forward > wrapped.Forward ||
                            (candidate.Forward == wrapped.Forward && candidate.Across < wrapped.Across))
                            wrapped = candidate;
                    }
                }
            }
            finally
            {
                selectables.Clear();
            }

            var result = aligned.Target ? aligned.Target : diagonal.Target ? diagonal.Target : wrapped.Target;
            if (!result)
                return null;

            var destination = FindScroll(result);
            if (destination && destination != originScroll && scrollFocus.TryGetValue(destination, out var remembered))
            {
                if (CanFocus(remembered) && FindScroll(remembered) == destination)
                    return remembered;
                scrollFocus.Remove(destination);
            }

            return result;
        }

        /// <summary>Remembers the last selection when the host moves out of a scroll area.</summary>
        public void RecordMove(Selectable from, Selectable to)
        {
            if (from == to || !CanFocus(from) || !CanFocus(to))
                return;

            var scroll = FindScroll(from);
            if (scroll && scroll != FindScroll(to))
                scrollFocus[scroll] = from;
        }

        public void ClearScrollFocus() => scrollFocus.Clear();

        private ScrollRect FindScroll(Selectable selectable)
        {
            for (var parent = selectable.transform; parent; parent = parent.parent)
            {
                if (parent.TryGetComponent<ScrollRect>(out var scroll) && scroll.isActiveAndEnabled &&
                    scroll.content && selectable.transform.IsChildOf(scroll.content))
                    return scroll;
                if (parent == root)
                    break;
            }
            return null;
        }

        private Rect GetBounds(Selectable selectable, Matrix4x4 toLocal)
        {
            if (selectable.transform is not RectTransform rect)
                return new Rect((Vector2)toLocal.MultiplyPoint3x4(selectable.transform.position), Vector2.zero);

            rect.GetWorldCorners(corners);
            Vector2 min = toLocal.MultiplyPoint3x4(corners[0]);
            Vector2 max = min;
            for (int i = 1; i < corners.Length; i++)
            {
                Vector2 point = toLocal.MultiplyPoint3x4(corners[i]);
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static bool OverlapsAcrossAxis(Rect a, Rect b, bool horizontal)
        {
            float size = horizontal ? Mathf.Min(a.height, b.height) : Mathf.Min(a.width, b.width);
            float overlap = horizontal
                ? Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin)
                : Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            return overlap >= size * .1f;
        }

        private static void KeepNearest(ref Candidate best, Candidate candidate, bool primaryFirst)
        {
            float primary = primaryFirst ? candidate.Forward : candidate.Across;
            float secondary = primaryFirst ? candidate.Across : candidate.Forward;
            float bestPrimary = primaryFirst ? best.Forward : best.Across;
            float bestSecondary = primaryFirst ? best.Across : best.Forward;
            if (!best.Target || primary < bestPrimary || (primary == bestPrimary && secondary < bestSecondary))
                best = candidate;
        }

        private readonly struct Candidate
        {
            public readonly Selectable Target;
            public readonly float Forward;
            public readonly float Across;

            public Candidate(Selectable target, float forward, float across)
            {
                Target = target;
                Forward = forward;
                Across = across;
            }
        }
    }
}
