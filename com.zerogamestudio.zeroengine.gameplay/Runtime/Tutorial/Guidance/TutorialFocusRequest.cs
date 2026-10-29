using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ZeroEngine.Tutorial.Guidance
{
    public enum TutorialTargetKind { Auto = 0, UI = 1, World = 2, ScreenRegion = 3 }
    public enum TutorialMaskShape { Rectangle = 0, Circle = 1, RoundedRectangle = 2 }
    public enum TutorialInteraction { Click = 0, Hold = 1, Drag = 2, EnterArea = 3, Signal = 4, Display = 5 }
    public enum TutorialCompletion { Any = 0, All = 1 }
    public enum TutorialFocusResult { Pending = 0, Completed = 1, Skipped = 2, Cancelled = 3, Failed = 4 }

    [Serializable]
    public sealed class TutorialTargetReference
    {
        public string Id;
        public TutorialTargetKind Kind;
        [Tooltip("Path relative to the context's ScopeRoot. Ambiguous names are rejected.")]
        public string Path;
        public TutorialMaskShape Shape = TutorialMaskShape.RoundedRectangle;
        public bool Interactive = true;
        [Tooltip("Normalized screen region; used only by ScreenRegion targets.")]
        public Rect ScreenRegion = new Rect(.4f, .4f, .2f, .2f);
        public Vector3 WorldSize = Vector3.one;
        [Min(0)] public float Padding = 10;
        [Min(0)] public float CornerRadius = 12;
        internal TutorialTargetReference RuntimeCopy() => (TutorialTargetReference)MemberwiseClone();
    }

    [Serializable]
    public sealed class TutorialFocusStyle
    {
        public Color Shade = new Color(.015f, .023f, .03f, .78f);
        public Color Border = new Color(.63f, .88f, .79f, 1);
        [Min(0)] public float BorderWidth = 3;
        [Min(0)] public float Feather = 1;
        public int SortingOrder = 300;
        public string SkipLabel = "Skip";
    }

    [Serializable]
    public sealed class TutorialFocusRequest
    {
        public List<TutorialTargetReference> Targets = new List<TutorialTargetReference>();
        public TutorialInteraction Interaction;
        public TutorialCompletion Completion;
        public bool BlockOtherInput = true;
        public bool CanSkip = true;
        [Min(.05f)] public float HoldSeconds = 1;
        [Min(.05f)] public float DisplaySeconds = 3;
        [Min(.1f)] public float TargetWaitSeconds = 10;
        [Min(0)] public float CompletionTimeoutSeconds;
        [Tooltip("Required for Drag. Must name one of Targets; it is the destination, not a source.")]
        public string DropTargetId;
        [Tooltip("Optional business confirmation after a gesture; required for Signal interactions.")]
        public string CompletionEvent;
        public string CompletionValue;
        public InputActionReference[] AllowedActions = Array.Empty<InputActionReference>();

        public TutorialFocusRequest RuntimeCopy(bool allowSkip)
        {
            var copy = (TutorialFocusRequest)MemberwiseClone();
            copy.Targets = Targets.Select(target => target == null ? null : target.RuntimeCopy()).ToList();
            copy.AllowedActions = (InputActionReference[])(AllowedActions ?? Array.Empty<InputActionReference>()).Clone();
            copy.CanSkip = CanSkip && allowSkip;
            return copy;
        }

        public List<string> Validate()
        {
            var errors = new List<string>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            int interactive = 0;
            if (Targets == null || Targets.Count == 0) errors.Add("At least one target is required.");
            if (Targets != null && Targets.Count > 15) errors.Add("At most fifteen targets are supported; one mask hole is reserved for dialogue.");
            foreach (var target in Targets ?? new List<TutorialTargetReference>())
            {
                if (target == null) { errors.Add("A target is null."); continue; }
                if (string.IsNullOrWhiteSpace(target.Id) || !ids.Add(target.Id)) errors.Add("Target IDs must be nonempty and unique.");
                if (target.Kind == TutorialTargetKind.ScreenRegion &&
                    (target.ScreenRegion.width <= 0 || target.ScreenRegion.height <= 0 || target.ScreenRegion.xMin < 0 ||
                    target.ScreenRegion.yMin < 0 || target.ScreenRegion.xMax > 1 || target.ScreenRegion.yMax > 1))
                    errors.Add("Screen regions must lie inside normalized screen bounds.");
                if (target.Kind == TutorialTargetKind.World &&
                    (target.WorldSize.x <= 0 || target.WorldSize.y <= 0 || target.WorldSize.z <= 0))
                    errors.Add("World target sizes must be positive.");
                if (target.Interactive && target.Id != DropTargetId) interactive++;
            }
            if (Interaction != TutorialInteraction.Display && Interaction != TutorialInteraction.Signal && interactive == 0)
                errors.Add("This interaction needs an interactive target.");
            if (Interaction == TutorialInteraction.Drag && (!ids.Contains(DropTargetId ?? string.Empty) || interactive == 0))
                errors.Add("Drag needs distinct source and destination targets.");
            if (Interaction == TutorialInteraction.Signal && string.IsNullOrWhiteSpace(CompletionEvent))
                errors.Add("Signal interactions require a completion event.");
            if (TargetWaitSeconds <= 0 || HoldSeconds <= 0 || DisplaySeconds <= 0 || CompletionTimeoutSeconds < 0)
                errors.Add("Durations must be positive; completion timeout may be zero.");
            return errors;
        }
    }

    public interface ITutorialFocusScope
    {
        void Release(TutorialFocusResult result);
    }

    public sealed class TutorialFocusContext
    {
        public TutorialTargetRegistry Targets = new TutorialTargetRegistry();
        public TutorialSignalBus Signals = new TutorialSignalBus();
        public Transform ScopeRoot;
        public Transform Actor;
        public Camera WorldCamera;
        public Func<bool> IsValid;
        public Func<double> Realtime = () => Time.realtimeSinceStartupAsDouble;
        public Func<IReadOnlyList<RectTransform>> VisibleExclusions;
        public Func<IEnumerable<InputActionMap>> InputMaps;
        public Func<InputActionMap, bool> CanRestoreInputMap;
        public Func<ITutorialFocusScope> AcquireScope;
        [Tooltip("A project may verify an authoritative operation result before accepting a gesture.")]
        public Func<string, bool> AcceptInteraction;
    }
}
