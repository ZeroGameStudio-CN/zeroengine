using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace ZeroEngine.Tutorial.Guidance
{
    /// <summary>A scoped interaction surface. It does not own quests, saves, or game state.</summary>
    [DefaultExecutionOrder(-5000)]
    public sealed class TutorialFocusView : MonoBehaviour
    {
        private static readonly Dictionary<EventSystem, TutorialFocusView> blockers = new Dictionary<EventSystem, TutorialFocusView>();
        private sealed class Target
        {
            internal TutorialTargetReference Reference;
            internal GameObject Object;
            internal Rect ScreenRect;
            internal TutorialPointerProxy Proxy;
            internal bool Completed;
        }
        private readonly List<Target> targets = new List<Target>();
        private readonly List<MonoBehaviour> observers = new List<MonoBehaviour>();
        private readonly List<TutorialMaskHole> holes = new List<TutorialMaskHole>();
        private RectTransform root;
        private TutorialMaskGraphic shade;
        private UnityEngine.UI.Button skip;
        private GameObject previousSelection;
        private EventSystem eventSystem;
        private InputSystemUIInputModule module;
        private TutorialFocusInputScope inputs;
        private ITutorialFocusScope scope;
        private TutorialFocusRequest request;
        private TutorialFocusContext context;
        private TutorialFocusStyle style;
        private Action<TutorialFocusResult> completion;
        private IDisposable signals;
        private bool showing, ending, signalReceived, waitingSignal;
        private int runVersion;
        private double beganAt, targetDeadline, completionDeadline;
        private int pressed = -1;
        private bool dragging, keyboardDrag, keyboardHold, observedPress, pointerUpSent;
        private RaycastResult latestHit;
        private double pressedAt;
        private GameObject nativePress, nativeDrag;
        private PointerEventData forwarded;

        public bool IsActive => Result == TutorialFocusResult.Pending && request != null;
        public bool IsBlocking => IsActive && showing && request.BlockOtherInput;
        public TutorialFocusResult Result { get; private set; } = TutorialFocusResult.Cancelled;
        public string Error { get; private set; }
        public float HoldProgress => pressed >= 0 && request != null ? Mathf.Clamp01((float)((Now - pressedAt) / request.HoldSeconds)) : 0;
        public IReadOnlyList<TutorialMaskHole> VisibleHoles => holes;
        public RectTransform MaskRoot => root;
        private double Now => context?.Realtime?.Invoke() ?? Time.realtimeSinceStartupAsDouble;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOwners() => blockers.Clear();

        public void Initialize(Transform parent, TMP_FontAsset font = null)
        {
            if (root) return;
            if (!font) font = TMP_Settings.defaultFontAsset;
            if (!font) throw new InvalidOperationException("Configure a tutorial font or import TextMeshPro Essential Resources before initializing guidance.");
            root = Rect("TutorialFocus", parent);
            root.anchorMin = Vector2.zero; root.anchorMax = Vector2.one; root.offsetMin = root.offsetMax = Vector2.zero;
            var canvas = root.gameObject.AddComponent<Canvas>(); canvas.overrideSorting = true;
            root.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var background = Rect("Mask", root);
            background.anchorMin = Vector2.zero; background.anchorMax = Vector2.one;
            background.offsetMin = background.offsetMax = Vector2.zero;
            shade = background.gameObject.AddComponent<TutorialMaskGraphic>();
            var button = Rect("Skip", root); button.anchorMin = button.anchorMax = new Vector2(1, 0);
            button.pivot = new Vector2(1, 0); button.anchoredPosition = new Vector2(-30, 30); button.sizeDelta = new Vector2(200, 52);
            button.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.12f, .2f, .2f, .98f);
            skip = button.gameObject.AddComponent<UnityEngine.UI.Button>(); skip.onClick.AddListener(() => TrySkip());
            var label = Rect("Label", button); label.anchorMin = Vector2.zero; label.anchorMax = Vector2.one;
            label.offsetMin = label.offsetMax = Vector2.zero;
            var text = label.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = 24; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
            root.gameObject.SetActive(false);
        }

        public void Begin(TutorialFocusRequest options, TutorialFocusContext environment, TutorialFocusStyle appearance,
            Action<TutorialFocusResult> onCompleted = null)
        {
            if (IsActive) throw new InvalidOperationException("This tutorial view already has an owner.");
            if (!root) throw new InvalidOperationException("Initialize the tutorial view before beginning.");
            if (options == null || environment == null || appearance == null) throw new ArgumentNullException();
            var errors = options.Validate();
            if (errors.Count > 0) throw new ArgumentException(string.Join("\n", errors));
            var activeEvents = EventSystem.current;
            var inputModule = activeEvents ? activeEvents.currentInputModule as InputSystemUIInputModule : null;
            if (!inputModule && activeEvents && !activeEvents.currentInputModule)
                inputModule = activeEvents.GetComponent<InputSystemUIInputModule>();
            if (!activeEvents || !inputModule || !inputModule.isActiveAndEnabled)
                throw new InvalidOperationException("Tutorial interactions require an InputSystemUIInputModule.");
            runVersion++;
            request = options; context = environment; style = appearance; completion = onCompleted;
            eventSystem = EventSystem.current; module = inputModule; Error = null; Result = TutorialFocusResult.Pending;
            showing = signalReceived = waitingSignal = ending = false;
            beganAt = Now; targetDeadline = Now + request.TargetWaitSeconds;
            completionDeadline = request.CompletionTimeoutSeconds > 0 ? Now + request.CompletionTimeoutSeconds : double.PositiveInfinity;
            previousSelection = eventSystem.currentSelectedGameObject;
            root.GetComponent<Canvas>().sortingOrder = style.SortingOrder;
            skip.GetComponentInChildren<TextMeshProUGUI>().text = style.SkipLabel;
            skip.gameObject.SetActive(request.CanSkip);
            try
            {
                foreach (var reference in request.Targets)
                {
                    var rect = Rect(reference.Id, root);
                    var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = Color.clear;
                    image.canvasRenderer.cullTransparentMesh = false; image.raycastTarget = request.BlockOtherInput;
                    var proxy = rect.gameObject.AddComponent<TutorialPointerProxy>(); proxy.Owner = this; proxy.TargetIndex = targets.Count;
                    targets.Add(new Target { Reference = reference, Proxy = proxy });
                }
                skip.transform.SetAsLastSibling();
                signals = context.Signals.Subscribe((key, value) =>
                {
                    if (IsActive && key == request.CompletionEvent &&
                        (string.IsNullOrEmpty(request.CompletionValue) || request.CompletionValue == value)) signalReceived = true;
                });
                Canvas.willRenderCanvases += RefreshGeometry;
                Tick();
            }
            catch { End(TutorialFocusResult.Failed); throw; }
        }

        private bool ContextValid() => context != null && (context.IsValid == null || context.IsValid()) &&
            eventSystem && eventSystem.isActiveAndEnabled && EventSystem.current == eventSystem && module && module.isActiveAndEnabled &&
            (!eventSystem.currentInputModule || eventSystem.currentInputModule == module);

        public void Tick()
        {
            if (!IsActive || ending) return;
            try
            {
                if (!ContextValid()) { End(TutorialFocusResult.Cancelled); return; }
                if (Now >= completionDeadline) { Fail("The tutorial operation timed out."); return; }
                if (!eventSystem.currentInputModule)
                {
                    if (Now >= targetDeadline) Fail("The tutorial input module did not become ready.");
                    return;
                }
                if (waitingSignal)
                {
                    if (signalReceived) End(TutorialFocusResult.Completed);
                    return;
                }
                bool ready = ResolveTargets();
                if (!ready)
                {
                    if (showing) { HideSurface(TutorialFocusResult.Cancelled); targetDeadline = Now + request.TargetWaitSeconds; }
                    if (Now >= targetDeadline) Fail("A tutorial target is unavailable or ambiguous.");
                    return;
                }
                if (!showing) ShowSurface();
                RefreshGeometry();
                if (request.Interaction == TutorialInteraction.Signal && signalReceived) End(TutorialFocusResult.Completed);
                else if (request.Interaction == TutorialInteraction.Display && Now - beganAt >= request.DisplaySeconds) End(TutorialFocusResult.Completed);
                else if (request.Interaction == TutorialInteraction.EnterArea)
                {
                    if (!context.Actor) { Fail("EnterArea needs an actor transform."); return; }
                    for (int i = 0; i < targets.Count && IsActive; i++)
                        if (!targets[i].Completed && targets[i].Reference.Interactive && targets[i].Object &&
                            TutorialTargetProjection.WorldBounds(targets[i].Reference, targets[i].Object).Contains(context.Actor.position)) Gesture(i, null);
                }
                if (!IsActive) return;
                if (keyboardHold && pressed >= 0 && !module.submit.action.IsPressed()) { ClearPointer(); return; }
                if (pressed >= 0 && request.Interaction == TutorialInteraction.Hold && Now - pressedAt >= request.HoldSeconds)
                {
                    int index = pressed;
                    var target = nativePress;
                    var data = forwarded;
                    bool observe = observedPress;
                    bool submit = keyboardHold;
                    ClearPointer();
                    Gesture(index, observe ? null : () => DispatchClick(target, data, index, submit));
                }
                if (IsBlocking && eventSystem.currentSelectedGameObject != skip.gameObject &&
                    !targets.Any(target => target.Proxy.gameObject == eventSystem.currentSelectedGameObject)) SelectFirst();
            }
            catch (Exception exception) { Fail(exception.Message); Debug.LogException(exception, this); }
        }

        private void Update() => Tick();

        private bool ResolveTargets()
        {
            bool ready = true;
            foreach (var target in targets)
            {
                target.Object = context.Targets.Resolve(target.Reference, context.ScopeRoot);
                bool visible = TutorialTargetProjection.TryScreenRect(target.Reference, target.Object, context.WorldCamera, out target.ScreenRect);
                var selectable = target.Object ? target.Object.GetComponent<UnityEngine.UI.Selectable>() : null;
                bool interactable = !selectable || selectable.IsInteractable();
                if (!target.Completed && (!visible || (target.Reference.Interactive && !interactable))) ready = false;
            }
            return ready;
        }

        private void ShowSurface()
        {
            if (request.BlockOtherInput)
            {
                if (blockers.TryGetValue(eventSystem, out var owner) && owner && owner != this && owner.IsBlocking)
                    throw new InvalidOperationException("Another tutorial already owns blocking input.");
                blockers[eventSystem] = this;
                scope = context.AcquireScope?.Invoke();
                inputs = new TutorialFocusInputScope(module, request, context);
            }
            showing = true; root.gameObject.SetActive(true); shade.raycastTarget = request.BlockOtherInput;
            if (!request.BlockOtherInput) InstallObservers();
            BuildNavigation();
            Canvas.ForceUpdateCanvases();
            RefreshGeometry();
            if (request.BlockOtherInput) SelectFirst();
        }

        private void BuildNavigation()
        {
            var selectable = targets.Where(target => target.Reference.Interactive &&
                (!keyboardDrag || target.Reference.Id == request.DropTargetId)).Select(target => (UnityEngine.UI.Selectable)target.Proxy).ToList();
            if (request.CanSkip) selectable.Add(skip);
            for (int i = 0; i < selectable.Count; i++)
            {
                var previous = selectable[(i + selectable.Count - 1) % selectable.Count];
                var next = selectable[(i + 1) % selectable.Count];
                selectable[i].navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.Explicit,
                    selectOnUp = previous, selectOnLeft = previous, selectOnDown = next, selectOnRight = next };
            }
        }

        private void SelectFirst()
        {
            var target = targets.FirstOrDefault(value => value.Reference.Interactive && !value.Completed && value.Reference.Id != request.DropTargetId);
            if (target != null && eventSystem) eventSystem.SetSelectedGameObject(target.Proxy.gameObject);
        }

        private void RefreshGeometry()
        {
            if (!showing || !root || !IsActive) return;
            holes.Clear();
            foreach (var target in targets)
            {
                var rect = ToLocal(target.ScreenRect);
                Place((RectTransform)target.Proxy.transform, rect);
                var padded = new Rect(rect.x - target.Reference.Padding, rect.y - target.Reference.Padding,
                    rect.width + target.Reference.Padding * 2, rect.height + target.Reference.Padding * 2);
                holes.Add(new TutorialMaskHole(padded, target.Reference.Shape, target.Reference.CornerRadius));
            }
            bool any = false; Rect exclusions = default;
            foreach (var element in context.VisibleExclusions?.Invoke() ?? Array.Empty<RectTransform>())
            {
                if (!element || !element.gameObject.activeInHierarchy) continue;
                var reference = new TutorialTargetReference { Kind = TutorialTargetKind.UI };
                if (!TutorialTargetProjection.TryScreenRect(reference, element.gameObject, null, out var screen)) continue;
                var bounds = ToLocal(screen);
                exclusions = any ? UnityEngine.Rect.MinMaxRect(Mathf.Min(exclusions.xMin, bounds.xMin), Mathf.Min(exclusions.yMin, bounds.yMin),
                    Mathf.Max(exclusions.xMax, bounds.xMax), Mathf.Max(exclusions.yMax, bounds.yMax)) : bounds;
                any = true;
            }
            if (any) holes.Add(new TutorialMaskHole(new Rect(exclusions.x - 10, exclusions.y - 10, exclusions.width + 20, exclusions.height + 20), TutorialMaskShape.Rectangle, 0, false));
            shade.Configure(holes, style);
        }

        private Rect ToLocal(Rect screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen.min, null, out var min);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen.max, null, out var max);
            return UnityEngine.Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private bool CanUse(int index) => IsActive && showing && index >= 0 && index < targets.Count &&
            targets[index].Reference.Interactive && !targets[index].Completed && ContextValid();

        private GameObject Underlying(PointerEventData data)
        {
            latestHit = default;
            var hits = new List<RaycastResult>(); eventSystem.RaycastAll(data, hits);
            foreach (var hit in hits)
            {
                if (!hit.gameObject || hit.gameObject.transform.IsChildOf(root)) continue;
                latestHit = hit;
                return hit.gameObject;
            }
            var camera = context.WorldCamera ? context.WorldCamera : Camera.main;
            if (!camera) return null;
            var ray = camera.ScreenPointToRay(data.position);
            bool has3D = Physics.Raycast(ray, out var hit3D);
            var hit2D = Physics2D.GetRayIntersection(ray);
            return hit2D.collider && (!has3D || hit2D.distance < hit3D.distance) ? hit2D.collider.gameObject : has3D ? hit3D.collider.gameObject : null;
        }

        private bool Belongs(int index, GameObject hit) => targets[index].Reference.Kind == TutorialTargetKind.ScreenRegion ||
            (hit && targets[index].Object && (hit == targets[index].Object || hit.transform.IsChildOf(targets[index].Object.transform)));

        private PointerEventData Copy(PointerEventData data)
        {
            return new PointerEventData(eventSystem) { position = data.position, pressPosition = data.pressPosition,
                delta = data.delta, button = data.button, pointerId = data.pointerId, clickCount = data.clickCount,
                clickTime = data.clickTime, eligibleForClick = true, useDragThreshold = data.useDragThreshold,
                pointerPressRaycast = latestHit, pointerCurrentRaycast = latestHit };
        }

        internal void PointerDown(int index, PointerEventData data)
        {
            if (!CanUse(index) || index == DropIndex || data.button != PointerEventData.InputButton.Left) return;
            var hit = Underlying(data);
            if (!Belongs(index, hit)) return;
            ClearPointer(); pressed = index; pressedAt = Now; forwarded = Copy(data);
            if (targets[index].Reference.Kind == TutorialTargetKind.ScreenRegion) return;
            nativePress = hit ? ExecuteEvents.GetEventHandler<IPointerDownHandler>(hit) : null;
            if (!nativePress && hit) nativePress = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit);
            forwarded.pointerPress = nativePress;
            forwarded.rawPointerPress = hit;
            if (nativePress) ExecuteEvents.Execute(nativePress, forwarded, ExecuteEvents.pointerDownHandler);
        }

        internal void PointerUp(int index, PointerEventData data)
        {
            if (pressed != index || forwarded == null) return;
            forwarded.position = data.position;
            if (nativePress && !pointerUpSent) ExecuteEvents.Execute(nativePress, forwarded, ExecuteEvents.pointerUpHandler);
            pointerUpSent = true;
            if (request.Interaction == TutorialInteraction.Hold && HoldProgress < 1) ClearPointer();
        }

        internal void PointerExit(int index, PointerEventData data)
        { if (pressed == index && !keyboardHold && request.Interaction == TutorialInteraction.Hold) ClearPointer(); }

        internal void PointerClick(int index, PointerEventData data)
        {
            if (!CanUse(index) || pressed != index || dragging || request.Interaction != TutorialInteraction.Click) return;
            var hit = Underlying(data);
            if (!Belongs(index, hit)) { ClearPointer(); return; }
            var target = hit; var payload = Copy(data);
            ClearPointer();
            Gesture(index, () => DispatchClick(target, payload, index, false));
        }

        private void DispatchClick(GameObject target, PointerEventData data, int index, bool submit)
        {
            if (targets[index].Reference.Kind == TutorialTargetKind.ScreenRegion) return;
            if (!target) throw new InvalidOperationException("The original target was removed before activation.");
            if (submit && ExecuteEvents.GetEventHandler<ISubmitHandler>(target))
                ExecuteEvents.ExecuteHierarchy(target, new BaseEventData(eventSystem), ExecuteEvents.submitHandler);
            else if (!ExecuteEvents.ExecuteHierarchy(target, data ?? new PointerEventData(eventSystem), ExecuteEvents.pointerClickHandler))
                throw new InvalidOperationException("The target has no click handler; use a project signal or interaction relay.");
        }

        internal void Submit(int index)
        {
            if (!CanUse(index)) return;
            if (request.Interaction == TutorialInteraction.Drag)
            {
                if (keyboardDrag && index == DropIndex) { FinishKeyboardDrag(index); return; }
                if (index == DropIndex) return;
                var data = new PointerEventData(eventSystem) { position = targets[index].ScreenRect.center, pressPosition = targets[index].ScreenRect.center, pointerId = -10001 };
                PointerDown(index, data); InitializeDrag(index, data); BeginDrag(index, data);
                if (!dragging) return;
                keyboardDrag = true; BuildNavigation(); eventSystem.SetSelectedGameObject(targets[DropIndex].Proxy.gameObject);
                return;
            }
            if (request.Interaction == TutorialInteraction.Hold)
            {
                PointerDown(index, new PointerEventData(eventSystem) { position = targets[index].ScreenRect.center, pointerId = -10001 });
                keyboardHold = pressed == index;
                return;
            }
            if (request.Interaction != TutorialInteraction.Click) return;
            var payload = new PointerEventData(eventSystem) { position = targets[index].ScreenRect.center };
            var target = Underlying(payload);
            if (Belongs(index, target)) Gesture(index, () => DispatchClick(target, payload, index, true));
        }

        private int DropIndex => request == null ? -1 : targets.FindIndex(target => target.Reference.Id == request.DropTargetId);

        internal void InitializeDrag(int index, PointerEventData data)
        {
            if (pressed != index || request.Interaction != TutorialInteraction.Drag) return;
            var hit = Underlying(data);
            nativeDrag = hit ? ExecuteEvents.GetEventHandler<IDragHandler>(hit) : null;
            if (nativeDrag) { forwarded.pointerDrag = nativeDrag; ExecuteEvents.Execute(nativeDrag, forwarded, ExecuteEvents.initializePotentialDrag); }
        }

        internal void BeginDrag(int index, PointerEventData data)
        {
            if (pressed != index || request.Interaction != TutorialInteraction.Drag ||
                (!nativeDrag && targets[index].Reference.Kind != TutorialTargetKind.ScreenRegion)) return;
            dragging = true; forwarded.dragging = true;
            if (nativeDrag) ExecuteEvents.Execute(nativeDrag, forwarded, ExecuteEvents.beginDragHandler);
        }

        internal void Drag(int index, PointerEventData data)
        {
            if (pressed != index || !dragging) return;
            forwarded.position = data.position; forwarded.delta = data.delta;
            Underlying(data); forwarded.pointerCurrentRaycast = latestHit;
            if (nativeDrag) ExecuteEvents.Execute(nativeDrag, forwarded, ExecuteEvents.dragHandler);
        }

        internal void EndDrag(int index, PointerEventData data)
        {
            if (pressed != index || !dragging || request.Interaction != TutorialInteraction.Drag) return;
            int destination = DropIndex; var hit = Underlying(data);
            if (destination < 0 || !targets[destination].ScreenRect.Contains(data.position) || !Belongs(destination, hit)) { ClearPointer(); return; }
            var source = nativeDrag; var payload = forwarded; payload.position = data.position;
            if (nativePress && !pointerUpSent) ExecuteEvents.Execute(nativePress, payload, ExecuteEvents.pointerUpHandler);
            // Detach ownership before dispatch: callbacks may close or destroy the source panel.
            pressed = -1; nativeDrag = nativePress = null; dragging = keyboardDrag = false; forwarded = null;
            Gesture(index, () =>
            {
                try
                {
                    if (targets[destination].Reference.Kind != TutorialTargetKind.ScreenRegion &&
                        (!hit || !ExecuteEvents.ExecuteHierarchy(hit, payload, ExecuteEvents.dropHandler)))
                        throw new InvalidOperationException("The drop target has no native drop handler.");
                }
                finally { if (source) ExecuteEvents.Execute(source, payload, ExecuteEvents.endDragHandler); }
            });
        }

        private void FinishKeyboardDrag(int index)
        {
            var data = new PointerEventData(eventSystem) { position = targets[index].ScreenRect.center, pointerId = -10001 };
            Drag(pressed, data); EndDrag(pressed, data);
        }

        private void Gesture(int index, Action nativeAction)
        {
            if (!CanUse(index)) return;
            int version = runVersion;
            bool last = request.Completion == TutorialCompletion.Any || targets.Where(target => target.Reference.Interactive && target.Reference.Id != request.DropTargetId)
                .All(target => target == targets[index] || target.Completed);
            try
            {
                if (last) HideSurface(TutorialFocusResult.Completed);
                if (!IsActive || version != runVersion) return;
                nativeAction?.Invoke();
                if (!IsActive || version != runVersion) return;
                if (context.AcceptInteraction != null && !context.AcceptInteraction(targets[index].Reference.Id))
                { Fail("The project rejected the interaction result."); return; }
                targets[index].Completed = true;
                if (!last) return;
                if (string.IsNullOrEmpty(request.CompletionEvent) || signalReceived) End(TutorialFocusResult.Completed);
                else { waitingSignal = true; completionDeadline = Now + (request.CompletionTimeoutSeconds > 0 ? request.CompletionTimeoutSeconds : request.TargetWaitSeconds); }
            }
            catch (Exception exception) { Fail(exception.Message); Debug.LogException(exception, this); }
        }

        private void InstallObservers()
        {
            for (int index = 0; index < targets.Count; index++)
            {
                var target = targets[index];
                if (!target.Object) continue;
                var components = target.Object.GetComponentsInChildren<MonoBehaviour>();
                var objects = components.Where(component => request.Interaction == TutorialInteraction.Drag ?
                    index == DropIndex ? component is IDropHandler : component is IDragHandler : component is IPointerClickHandler)
                    .Select(component => component.gameObject).Distinct().ToArray();
                foreach (var item in objects)
                {
                    if (request.Interaction == TutorialInteraction.Drag && index == DropIndex)
                    { var observer = item.AddComponent<TutorialDropObserver>(); observer.Owner = this; observer.TargetIndex = index; observers.Add(observer); }
                    else if (request.Interaction == TutorialInteraction.Drag)
                    { var observer = item.AddComponent<TutorialDragObserver>(); observer.Owner = this; observer.TargetIndex = index; observers.Add(observer); }
                    else
                    { var observer = item.AddComponent<TutorialPointerObserver>(); observer.Owner = this; observer.TargetIndex = index; observers.Add(observer); }
                }
            }
        }

        internal void ObserveClick(int index) { if (CanUse(index) && request.Interaction == TutorialInteraction.Click) Gesture(index, null); }
        internal void ObserveDown(int index, PointerEventData data)
        { if (CanUse(index) && request.Interaction == TutorialInteraction.Hold) { pressed = index; pressedAt = Now; observedPress = true; } }
        internal void ObserveUp(int index, PointerEventData data) { if (pressed == index && observedPress) ClearPointer(); }
        internal void ObserveBeginDrag(int index, PointerEventData data)
        { if (CanUse(index) && request.Interaction == TutorialInteraction.Drag && index != DropIndex) { pressed = index; observedPress = dragging = true; } }
        internal void ObserveEndDrag(int index) { if (pressed == index && observedPress) ClearPointer(); }
        internal void ObserveDrop(int index, PointerEventData data)
        { if (IsActive && index == DropIndex && pressed >= 0 && observedPress && dragging) { int source = pressed; ClearPointer(); Gesture(source, null); } }

        public bool TrySkip()
        {
            if (!IsActive) return false;
            if (request.CanSkip) End(TutorialFocusResult.Skipped);
            return true;
        }

        public void Cancel() => End(TutorialFocusResult.Cancelled);

        private void Fail(string message) { Error = message; End(TutorialFocusResult.Failed); }

        private void ClearPointer()
        {
            var press = nativePress; var drag = nativeDrag; var data = forwarded;
            bool sendUp = press && data != null && !pointerUpSent;
            bool sendEnd = drag && dragging && data != null;
            pressed = -1; nativePress = nativeDrag = null; forwarded = null;
            dragging = keyboardDrag = keyboardHold = observedPress = pointerUpSent = false;
            try { if (sendUp) ExecuteEvents.Execute(press, data, ExecuteEvents.pointerUpHandler); }
            finally { if (sendEnd) ExecuteEvents.Execute(drag, data, ExecuteEvents.endDragHandler); }
        }

        private void HideSurface(TutorialFocusResult result)
        {
            showing = false;
            try { ClearPointer(); }
            finally
            {
                foreach (var observer in observers) if (observer)
                {
                    if (observer is TutorialPointerObserver pointer) pointer.Owner = null;
                    else if (observer is TutorialDragObserver drag) drag.Owner = null;
                    else if (observer is TutorialDropObserver drop) drop.Owner = null;
                    TutorialObjectLifetime.Destroy(observer);
                }
                observers.Clear();
                var acquiredInputs = inputs; inputs = null;
                var acquiredScope = scope; scope = null;
                try { acquiredInputs?.Dispose(); }
                finally
                {
                    try { acquiredScope?.Release(result); }
                    finally
                    {
                        if (root) root.gameObject.SetActive(false);
                        if (eventSystem && blockers.TryGetValue(eventSystem, out var owner) && owner == this) blockers.Remove(eventSystem);
                        if (eventSystem && previousSelection && previousSelection.activeInHierarchy &&
                            targets.Any(target => target.Proxy && eventSystem.currentSelectedGameObject == target.Proxy.gameObject))
                            eventSystem.SetSelectedGameObject(previousSelection);
                    }
                }
            }
        }

        private void End(TutorialFocusResult result)
        {
            if (!IsActive || ending || result == TutorialFocusResult.Pending) return;
            ending = true;
            var done = completion; completion = null;
            try
            {
                Result = result;
                Canvas.willRenderCanvases -= RefreshGeometry;
                signals?.Dispose(); signals = null;
                HideSurface(result);
            }
            finally
            {
                foreach (var target in targets) if (target.Proxy) { target.Proxy.Owner = null; TutorialObjectLifetime.Destroy(target.Proxy.gameObject); }
                targets.Clear(); holes.Clear(); request = null;
                ending = false; done?.Invoke(result);
            }
        }

        private void OnDisable() => Cancel();
        private void OnDestroy()
        {
            Cancel();
            if (root) TutorialObjectLifetime.Destroy(root.gameObject);

        }

        private static RectTransform Rect(string name, Transform parent)
        { var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); return rect; }
        private static void Place(RectTransform rect, Rect bounds)
        { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = bounds.center; rect.sizeDelta = bounds.size; }
    }
}
