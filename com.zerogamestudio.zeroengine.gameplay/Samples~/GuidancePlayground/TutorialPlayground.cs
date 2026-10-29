using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using ZeroEngine.Tutorial.Guidance;
using ZeroEngine.Tutorial.Presentation;

namespace ZeroEngine.Tutorial.Samples
{
    /// <summary>Add this component to an empty scene and enter Play. No project save or quest service is used.</summary>
    public sealed class TutorialPlayground : MonoBehaviour
    {
        public TMP_FontAsset Font;
        private readonly List<IDisposable> bindings = new List<IDisposable>();
        private GameObject owned;
        private Canvas canvas;
        private TextMeshProUGUI title;
        private TutorialFocusView focus;
        private TutorialPresentationView presentations;
        private TutorialContext context;
        private ITutorialPresentation coaching;

        private IEnumerator Start()
        {
            owned = new GameObject("Tutorial Playground"); owned.transform.SetParent(transform);
            if (!EventSystem.current) new GameObject("Events", typeof(EventSystem), typeof(InputSystemUIInputModule)).transform.SetParent(owned.transform);
            if (!EventSystem.current.GetComponent<InputSystemUIInputModule>() ||
                (EventSystem.current.currentInputModule && EventSystem.current.currentInputModule is not InputSystemUIInputModule))
                throw new InvalidOperationException("The sample needs an InputSystemUIInputModule. Use an empty scene.");
            var view = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            view.transform.SetParent(owned.transform); canvas = view.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = view.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800); scaler.matchWidthOrHeight = .5f;
            if (!Font) Font = TMP_Settings.defaultFontAsset;
            if (!Font) throw new InvalidOperationException("Assign Font or import TextMeshPro Essential Resources before running the sample.");
            title = Label("Instructions", canvas.transform, "Tutorial playground", new Vector2(0, 310), new Vector2(1100, 100));
            var cameraObject = new GameObject("Camera", typeof(Camera)); cameraObject.transform.SetParent(owned.transform);
            var camera = cameraObject.GetComponent<Camera>(); camera.transform.position = new Vector3(0, 0, -8);
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.06f, .09f, .12f);
            camera.orthographic = true; camera.orthographicSize = 4;
            context = new TutorialContext(); context.Focus.ScopeRoot = canvas.transform; context.Focus.WorldCamera = camera;
            context.Focus.IsValid = () => this && isActiveAndEnabled;
            focus = owned.AddComponent<TutorialFocusView>(); focus.Initialize(canvas.transform, Font);
            presentations = owned.AddComponent<TutorialPresentationView>(); presentations.Container = canvas.transform; presentations.Font = Font;
            context.Presentations.DefaultPlayer = presentations;
            var first = Button("first", "First action", new Vector2(-260, 120));
            var second = Button("second", "Second action", new Vector2(260, 120));
            first.onClick.AddListener(() => first.GetComponent<UnityEngine.UI.Image>().color = Color.green);
            second.onClick.AddListener(() => second.GetComponent<UnityEngine.UI.Image>().color = Color.green);
            var clicks = Request(TutorialInteraction.Click, "first", "second"); clicks.Completion = TutorialCompletion.All;
            clicks.Targets[0].Shape = TutorialMaskShape.Circle;
            yield return Guide(clicks, "Activate both targets. Keyboard/gamepad navigation and touch work too.");
            var hold = Request(TutorialInteraction.Hold, "first"); hold.HoldSeconds = 1;
            yield return Guide(hold, "Hold the first action for one second. A short press does not complete the lesson.");
            var source = Button("source", "Drag this card", new Vector2(-260, -40)); source.gameObject.AddComponent<PlaygroundDragSource>();
            var destination = Button("destination", "Drop here", new Vector2(260, -40)); destination.gameObject.AddComponent<PlaygroundDropTarget>();
            var drag = Request(TutorialInteraction.Drag, "source", "destination"); drag.DropTargetId = "destination";
            yield return Guide(drag, "Drag the card to the destination, or confirm the source then the destination.");
            first.gameObject.SetActive(false); second.gameObject.SetActive(false); source.gameObject.SetActive(false); destination.gameObject.SetActive(false);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = "World target"; cube.transform.SetParent(owned.transform);
            cube.transform.position = Vector3.zero;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader)
            {
                var material = new Material(shader); material.color = new Color(.3f, .8f, .65f); cube.GetComponent<Renderer>().material = material;
                cube.AddComponent<PlaygroundMaterialOwner>().Owned = material;
            }
            bindings.Add(context.Focus.Targets.Register("world", cube));
            cube.AddComponent<TutorialInteractionRelay>().OnActivated.AddListener(() => cube.transform.localScale *= .9f);
            var world = Request(TutorialInteraction.Click, "world"); world.Targets[0].Kind = TutorialTargetKind.World;
            yield return Guide(world, "This is a scene object. The real collider and its action are used.");
            var signal = Request(TutorialInteraction.Signal, "world"); signal.CompletionEvent = "sample.ready"; signal.BlockOtherInput = false;
            var done = Button("result", "Perform project action", new Vector2(0, -230));
            done.onClick.AddListener(() => context.Signal("sample.ready"));
            yield return Guide(signal, "This lesson waits for a project event. Perform the action below.");
            done.gameObject.SetActive(false);
            using (var dialogue = presentations.Begin(new TutorialMedia { Id = "normal", Kind = TutorialMediaKind.Dialogue,
                DialogueLayout = TutorialDialogueLayout.Normal, Speaker = "Guide", Text = "The guided actions are finished.\nNormal dialogue can be placed between any two lessons." }, context, true))
                while (dialogue.Result == TutorialPresentationResult.Pending) { dialogue.Tick(); yield return null; }
            title.text = "Finished. Use the sequence Inspector for content authoring; bind project operations through targets and signals.";
        }

        private IEnumerator Guide(TutorialFocusRequest request, string message)
        {
            title.text = request.Interaction.ToString();
            coaching = presentations.Begin(new TutorialMedia { Id = "coaching", Kind = TutorialMediaKind.Dialogue,
                DialogueLayout = TutorialDialogueLayout.Corner, Speaker = "Guide", Text = message }, context, request.CanSkip);
            context.Focus.VisibleExclusions = () => coaching.VisibleContent;
            focus.Begin(request, context.Focus, new TutorialFocusStyle());
            while (focus.IsActive) { coaching.Tick(true); yield return null; }
            coaching.Dispose(); coaching = null; context.Focus.VisibleExclusions = null;
            if (focus.Result == TutorialFocusResult.Failed || focus.Result == TutorialFocusResult.Cancelled)
                throw new InvalidOperationException(focus.Error ?? "The sample was cancelled.");
        }
        private static TutorialFocusRequest Request(TutorialInteraction interaction, params string[] ids)
        { var request = new TutorialFocusRequest { Interaction = interaction }; foreach (var id in ids) request.Targets.Add(new TutorialTargetReference { Id = id }); return request; }
        private UnityEngine.UI.Button Button(string id, string text, Vector2 position)
        {
            var root = new GameObject(id, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button)); root.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)root.transform; rect.sizeDelta = new Vector2(220, 80); rect.anchoredPosition = position;
            root.GetComponent<UnityEngine.UI.Image>().color = new Color(.22f, .48f, .44f);
            Label("Label", root.transform, text, Vector2.zero, rect.sizeDelta);
            bindings.Add(context.Focus.Targets.Register(id, root)); return root.GetComponent<UnityEngine.UI.Button>();
        }
        private TextMeshProUGUI Label(string name, Transform parent, string text, Vector2 position, Vector2 size)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); root.transform.SetParent(parent, false);
            var rect = (RectTransform)root.transform; rect.sizeDelta = size; rect.anchoredPosition = position;
            var label = root.GetComponent<TextMeshProUGUI>(); label.font = Font; label.fontSize = 25; label.alignment = TextAlignmentOptions.Center;
            label.raycastTarget = false; label.text = text; return label;
        }
        private void OnDisable() { StopAllCoroutines(); if (focus) focus.Cancel(); coaching?.Dispose(); }
        private void OnDestroy()
        {
            if (focus) focus.Cancel(); coaching?.Dispose(); foreach (var binding in bindings) binding.Dispose();
            if (owned) Destroy(owned);
        }
    }
}
