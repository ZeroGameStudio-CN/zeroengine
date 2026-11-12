using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.Video;
using ZeroEngine.Tutorial.Guidance;
using ZeroEngine.Tutorial.Presentation;

namespace ZeroEngine.Tutorial.Tests.Runtime
{
    public sealed class TutorialRuntimeScenarios
    {
        public string EvidenceDirectory;
        public VideoClip VideoFixture;
        public Font FontFixture;
        private GameObject host;
        private Canvas canvas;
        private Camera camera;
        private TutorialFocusView view;
        private TutorialFocusContext context;
        private Keyboard keyboard;
        private Mouse mouse;
        private Gamepad gamepad;
        private Touchscreen touch;
        private InputSettings previousSettings, temporarySettings;
        private TMP_FontAsset font;
        private readonly List<IDisposable> registrations = new List<IDisposable>();
        private UnityEngine.UI.Image background;
        private bool setup;
        private TutorialRealtimeCapture capture;
        private ITutorialPresentation livePresentation;
        private readonly List<UnityEngine.Object> mediaAssets = new List<UnityEngine.Object>();
        private float previousTimeScale;
        private readonly List<string> outcomes = new List<string>();

        [UnityTest, Timeout(120000)]
        public IEnumerator UIWorldRegionsGesturesAndPresentation_AreScopedAndReusable()
        {
            SetUp();
            yield return new WaitForEndOfFrame();
            if (!string.IsNullOrEmpty(EvidenceDirectory))
            {
                capture = new TutorialRealtimeCapture(Path.Combine(EvidenceDirectory, "capture"));
                capture.Start(host.AddComponent<TutorialCaptureHost>());
            }
            int firstClicks = 0, secondClicks = 0, wrongClicks = 0;
            var first = Button("first", new Vector2(-260, 120), () => firstClicks++);
            var second = Button("second", new Vector2(260, 120), () => secondClicks++);
            var wrong = Button("wrong", new Vector2(0, -200), () => wrongClicks++);
            var options = Request(TutorialInteraction.Click, "first", "second"); options.Completion = TutorialCompletion.All; options.CanSkip = false; options.Targets[0].Shape = TutorialMaskShape.Circle;
            view.Begin(options, context, new TutorialFocusStyle());
            yield return Frames(3);
            yield return Click(Position(wrong)); Assert.That(wrongClicks, Is.Zero, "The mask must block unrelated native input.");
            yield return Press(Key.Enter); Assert.That(firstClicks, Is.EqualTo(1)); Assert.That(view.IsActive, Is.True);
            yield return Press(Key.RightArrow);
            yield return GamepadConfirm();
            Assert.That(secondClicks, Is.EqualTo(1)); Assert.That(view.Result, Is.EqualTo(TutorialFocusResult.Completed));
            outcomes.Add("multiple targets; blocked unrelated native click; keyboard and gamepad activation exactly once");
            var cancels = Button("cancel-owner", new Vector2(0, 230), () => view.Cancel());
            view.Begin(Request(TutorialInteraction.Click, "cancel-owner"), context, new TutorialFocusStyle());
            yield return Frames(2); yield return Press(Key.Enter);
            Assert.That(view.Result, Is.EqualTo(TutorialFocusResult.Cancelled));
            cancels.gameObject.SetActive(false);
            outcomes.Add("project cancellation inside the native action cannot complete or corrupt a released guide");

            var hold = Request(TutorialInteraction.Hold, "first"); hold.HoldSeconds = .35f;
            view.Begin(hold, context, new TutorialFocusStyle()); yield return Frames(2);
            yield return MouseStateAt(Position(first), true); yield return Realtime(.1);
            yield return MouseStateAt(Position(first), false); yield return Frames(2);
            Assert.That(firstClicks, Is.EqualTo(1)); Assert.That(view.IsActive, Is.True, "A short press must not count as a hold.");
            yield return MouseStateAt(Position(first), true); yield return Realtime(.45);
            yield return MouseStateAt(Position(first), false);
            Assert.That(firstClicks, Is.EqualTo(2)); Assert.That(view.Result, Is.EqualTo(TutorialFocusResult.Completed));
            outcomes.Add("hold threshold; early release does not invoke the original action");

            var touchRequest = Request(TutorialInteraction.Click, "second");
            view.Begin(touchRequest, context, new TutorialFocusStyle()); yield return Frames(2);
            InputSystem.QueueStateEvent(touch, new TouchState { touchId = 1, phase = UnityEngine.InputSystem.TouchPhase.Began, position = Position(second), pressure = 1 });
            yield return Frames(2);
            InputSystem.QueueStateEvent(touch, new TouchState { touchId = 1, phase = UnityEngine.InputSystem.TouchPhase.Ended, position = Position(second) });
            yield return Frames(3);
            Assert.That(secondClicks, Is.EqualTo(2)); Assert.That(view.Result, Is.EqualTo(TutorialFocusResult.Completed));
            outcomes.Add("touch uses the real target callback");

            var source = Button("source", new Vector2(-260, -25), null); var nativeSource = source.gameObject.AddComponent<TutorialNativeDragProbe>();
            var destination = Button("destination", new Vector2(260, -25), null); var nativeDrop = destination.gameObject.AddComponent<TutorialNativeDropProbe>();
            var drag = Request(TutorialInteraction.Drag, "source", "destination"); drag.DropTargetId = "destination";
            view.Begin(drag, context, new TutorialFocusStyle()); yield return Frames(2);
            yield return MouseStateAt(Position(source), true);
            yield return MouseStateAt(Position(destination), true);
            yield return MouseStateAt(Position(destination), false); yield return Frames(3);
            Assert.That(nativeSource.Begins, Is.EqualTo(1)); Assert.That(nativeSource.Ends, Is.EqualTo(1)); Assert.That(nativeDrop.Drops, Is.EqualTo(1));
            Assert.That(view.Result, Is.EqualTo(TutorialFocusResult.Completed));
            outcomes.Add("native drag/drop pipeline is forwarded once");

            view.Begin(drag, context, new TutorialFocusStyle()); yield return Frames(2);
            yield return Press(Key.Enter); yield return Press(Key.Enter); yield return Frames(2);
            Assert.That(nativeDrop.Drops, Is.EqualTo(2)); Assert.That(nativeSource.Ends, Is.EqualTo(2));
            Assert.That(view.Result, Is.EqualTo(TutorialFocusResult.Completed)); outcomes.Add("two-confirm keyboard drag/drop");

            var observed = Request(TutorialInteraction.Click, "first"); observed.BlockOtherInput = false;
            view.Begin(observed, context, new TutorialFocusStyle()); yield return Frames(2);
            yield return Click(Position(first));
            Assert.That(firstClicks, Is.EqualTo(3)); Assert.That(view.Result, Is.EqualTo(TutorialFocusResult.Completed));
            outcomes.Add("natural observation does not duplicate the native operation");

            var region = new TutorialFocusRequest { Interaction = TutorialInteraction.Click };
            region.Targets.Add(new TutorialTargetReference { Id = "region", Kind = TutorialTargetKind.ScreenRegion,
                ScreenRegion = new Rect(.4f, .1f, .2f, .2f), Shape = TutorialMaskShape.RoundedRectangle });
            view.Begin(region, context, new TutorialFocusStyle()); yield return Frames(2);
            int wrongBefore = wrongClicks; yield return Click(new Vector2(Screen.width * .5f, Screen.height * .2f));
            Assert.That(view.Result, Is.EqualTo(TutorialFocusResult.Completed)); Assert.That(wrongClicks, Is.EqualTo(wrongBefore));
            outcomes.Add("screen-region gesture does not activate an unrelated control underneath");

            background.enabled = false; first.gameObject.SetActive(false); second.gameObject.SetActive(false); wrong.gameObject.SetActive(false);
            source.gameObject.SetActive(false); destination.gameObject.SetActive(false);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.name = "world"; cube.transform.SetParent(host.transform);
            cube.transform.position = Vector3.zero; registrations.Add(context.Targets.Register("world", cube));
            int worldClicks = 0; cube.AddComponent<TutorialInteractionRelay>().OnActivated.AddListener(() => worldClicks++);
            var world = Request(TutorialInteraction.Click, "world"); world.Targets[0].Kind = TutorialTargetKind.World;
            view.Begin(world, context, new TutorialFocusStyle()); yield return Frames(3);
            yield return Click(camera.WorldToScreenPoint(cube.transform.position));
            Assert.That(worldClicks, Is.EqualTo(1)); Assert.That(view.Result, Is.EqualTo(TutorialFocusResult.Completed));
            outcomes.Add("world projection and nearest collider activation");

            var actor = new GameObject("actor"); actor.transform.SetParent(host.transform); actor.transform.position = new Vector3(4, 0, 0); context.Actor = actor.transform;
            var enter = Request(TutorialInteraction.EnterArea, "world"); enter.BlockOtherInput = false; enter.Targets[0].Kind = TutorialTargetKind.World;
            view.Begin(enter, context, new TutorialFocusStyle()); yield return Frames(2);
            Assert.That(view.IsActive, Is.True); actor.transform.position = Vector3.zero; yield return Frames(2);
            Assert.That(view.Result, Is.EqualTo(TutorialFocusResult.Completed)); outcomes.Add("area entry uses actor position");

            var signal = Request(TutorialInteraction.Signal, "world"); signal.BlockOtherInput = false; signal.CompletionEvent = "actual-result";
            view.Begin(signal, context, new TutorialFocusStyle()); yield return Frames(2);
            context.Signals.Publish("wrong-result"); yield return Frames(2); Assert.That(view.IsActive, Is.True);
            context.Signals.Publish("actual-result"); yield return Frames(2); Assert.That(view.Result, Is.EqualTo(TutorialFocusResult.Completed));
            outcomes.Add("context-local business confirmation");

            first.gameObject.SetActive(true);
            var missing = Request(TutorialInteraction.Click, "first"); missing.TargetWaitSeconds = .15f;
            view.Begin(missing, context, new TutorialFocusStyle()); yield return Frames(2); first.gameObject.SetActive(false); yield return Realtime(.25);
            Assert.That(view.Result, Is.EqualTo(TutorialFocusResult.Failed)); Assert.That(view.IsBlocking, Is.False);
            context.Signals.Publish("actual-result"); Assert.That(view.Result, Is.EqualTo(TutorialFocusResult.Failed));
            outcomes.Add("lost target releases input and late events do not complete a cancelled lesson");

            first.gameObject.SetActive(true);
            var failingFocusScope = new ThrowingScope(); context.AcquireScope = () => failingFocusScope;
            view.Begin(Request(TutorialInteraction.Click, "first"), context, new TutorialFocusStyle()); yield return Frames(2);
            Assert.Throws<InvalidOperationException>(() => view.Cancel());
            Assert.That(failingFocusScope.Releases, Is.EqualTo(1)); Assert.That(view.MaskRoot.gameObject.activeSelf, Is.False);
            Assert.That(view.IsActive, Is.False); Assert.That(((UnityEngine.InputSystem.UI.InputSystemUIInputModule)EventSystem.current.currentInputModule).submit.action.enabled, Is.True);
            context.AcquireScope = null; first.gameObject.SetActive(false); yield return Frames(2);
            outcomes.Add("a throwing project scope cannot retain the mask or its input gate");

            var presentation = host.AddComponent<TutorialPresentationView>(); presentation.Container = canvas.transform; presentation.Font = font;
            var tutorial = new TutorialContext(); tutorial.Focus.IsValid = () => host;
            using (var dialogue = presentation.Begin(new TutorialMedia { Id = "dialogue", Kind = TutorialMediaKind.Dialogue,
                DialogueLayout = TutorialDialogueLayout.Normal, Speaker = "Guide", Text = "The operation is complete.\nYou can continue normally.", TypewriterSpeed = 0 }, tutorial, true))
            {
                livePresentation = dialogue;
                yield return Realtime(.6); yield return Press(Key.Enter); yield return Frames(2); yield return Press(Key.Enter); yield return Frames(2);
                Assert.That(dialogue.Result, Is.EqualTo(TutorialPresentationResult.Completed));
            }
            livePresentation = null;
            using (var choices = presentation.Begin(new TutorialMedia { Id = "choice", Kind = TutorialMediaKind.Dialogue,
                DialogueLayout = TutorialDialogueLayout.Normal, Text = "Choose a route.", Choices = new[] { "First route", "Second route" }, TypewriterSpeed = 0 }, tutorial, false))
            {
                livePresentation = choices;
                yield return Press(Key.Enter); yield return Frames(2);
                Assert.That(((ITutorialChoicePresentation)choices).SelectedChoice, Is.Zero); Assert.That(choices.Result, Is.EqualTo(TutorialPresentationResult.Completed));
            }
            livePresentation = null;
            outcomes.Add("standalone normal dialogue and choice input");
            var failingMediaScope = new ThrowingScope(); tutorial.Focus.AcquireScope = () => failingMediaScope;
            var failedCleanup = presentation.Begin(new TutorialMedia { Id = "scope-failure", Kind = TutorialMediaKind.Dialogue,
                DialogueLayout = TutorialDialogueLayout.Normal, Text = "Synthetic scope failure.", TypewriterSpeed = 0 }, tutorial, true);
            Assert.Throws<InvalidOperationException>(() => failedCleanup.Dispose());
            Assert.That(failingMediaScope.Releases, Is.EqualTo(1)); Assert.That(failedCleanup.Result, Is.EqualTo(TutorialPresentationResult.Cancelled));
            tutorial.Focus.AcquireScope = null; yield return Frames(2);
            Assert.That(host.GetComponentsInChildren<Canvas>().Length, Is.EqualTo(1), "Presentation canvases are released even when a project callback throws.");
            outcomes.Add("a throwing project scope cannot retain presentation resources");
            Time.timeScale = 0;
            var image = new Texture2D(8, 8); image.SetPixels(System.Linq.Enumerable.Repeat(new Color(.15f, .4f, .65f), 64).ToArray()); image.Apply();
            var sprite = Sprite.Create(image, new Rect(0, 0, 8, 8), new Vector2(.5f, .5f)); mediaAssets.Add(image); mediaAssets.Add(sprite);
            using (var cg = presentation.Begin(new TutorialMedia { Id = "cg", Kind = TutorialMediaKind.CG, Image = sprite, Seconds = .15f }, tutorial, true))
            {
                livePresentation = cg; yield return Realtime(.25); Assert.That(cg.Result, Is.EqualTo(TutorialPresentationResult.Completed));
            }
            livePresentation = null;
            var timeline = ScriptableObject.CreateInstance<TutorialProbePlayable>(); mediaAssets.Add(timeline);
            using (var playback = presentation.Begin(new TutorialMedia { Id = "timeline", Kind = TutorialMediaKind.Timeline, Timeline = timeline }, tutorial, true))
            {
                livePresentation = playback; yield return Realtime(.4); Assert.That(playback.Result, Is.EqualTo(TutorialPresentationResult.Completed));
            }
            livePresentation = null;
            if (VideoFixture)
            {
                using (var playback = presentation.Begin(new TutorialMedia { Id = "video", Kind = TutorialMediaKind.Video, Video = VideoFixture }, tutorial, true))
                {
                    livePresentation = playback; double deadline = Time.realtimeSinceStartupAsDouble + 20;
                    bool firstFrame = false;
                    while (playback.Result == TutorialPresentationResult.Pending && Time.realtimeSinceStartupAsDouble < deadline)
                    { playback.Tick(); firstFrame |= playback.IsReady; yield return null; }
                    Assert.That(firstFrame, Is.True); Assert.That(playback.Result, Is.EqualTo(TutorialPresentationResult.Completed), playback.Error);
                }
                livePresentation = null;
            }
            Time.timeScale = previousTimeScale;
            outcomes.Add("CG, Timeline and supplied video run on unscaled time and release their resources");
            if (capture != null)
            {
                capture.Stop(); while (!capture.Stopped) yield return null; capture.Complete();
                File.WriteAllText(Path.Combine(EvidenceDirectory, "scenario.json"), JsonUtility.ToJson(new Evidence { passed = true, outcomes = outcomes.ToArray() }, true));
            }
        }

        private void SetUp()
        {
            Assert.That(UnityEngine.Object.FindObjectOfType<EventSystem>(), Is.Null, "Do not replace another EventSystem owner.");
            setup = true;
            previousTimeScale = Time.timeScale;
            var settings = InputSystem.settings;
            previousSettings = settings.hideFlags == HideFlags.HideAndDontSave ? UnityEngine.Object.Instantiate(settings) : settings;
            temporarySettings = UnityEngine.Object.Instantiate(settings); temporarySettings.hideFlags = HideFlags.HideAndDontSave;
            temporarySettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
#if UNITY_EDITOR
            temporarySettings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
#endif
            InputSystem.settings = temporarySettings;
            keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>(); gamepad = InputSystem.AddDevice<Gamepad>(); touch = InputSystem.AddDevice<Touchscreen>();
            host = new GameObject("TutorialStandaloneFixture");
            var events = new GameObject("Events", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule)); events.transform.SetParent(host.transform);
            var cameraObject = new GameObject("Camera", typeof(Camera)); cameraObject.transform.SetParent(host.transform);
            camera = cameraObject.GetComponent<Camera>(); camera.transform.position = new Vector3(0, 0, -8); camera.orthographic = true; camera.orthographicSize = 4;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .1f, .13f);
            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            canvasObject.transform.SetParent(host.transform); canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>(); scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800); scaler.matchWidthOrHeight = .5f;
            var bg = new GameObject("Background", typeof(RectTransform), typeof(UnityEngine.UI.Image)); bg.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)bg.transform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            background = bg.GetComponent<UnityEngine.UI.Image>(); background.color = Color.white; background.raycastTarget = false;
            var sourceFont = FontFixture ? FontFixture : TMP_Settings.defaultFontAsset ? TMP_Settings.defaultFontAsset.sourceFontFile : null;
            Assert.That(sourceFont, Is.Not.Null, "Provide FontFixture or a default TMP font with readable source font data.");
            font = TMP_FontAsset.CreateFontAsset(sourceFont);
            Assert.That(font, Is.Not.Null, "The fixture font must include its source font data.");
            font.hideFlags = HideFlags.HideAndDontSave;
            view = host.AddComponent<TutorialFocusView>(); view.Initialize(canvas.transform, font);
            context = new TutorialFocusContext { ScopeRoot = canvas.transform, WorldCamera = camera, IsValid = () => host };
        }

        [UnityTearDown] public IEnumerator TearDown()
        {
            capture?.Stop(); capture?.Dispose();
            Time.timeScale = previousTimeScale;
            livePresentation?.Dispose(); livePresentation = null;
            if (view) view.Cancel(); foreach (var registration in registrations) registration.Dispose(); registrations.Clear();
            if (host) UnityEngine.Object.Destroy(host);
            foreach (var asset in mediaAssets) if (asset) UnityEngine.Object.Destroy(asset); mediaAssets.Clear();
            if (font) { if (font.material) UnityEngine.Object.Destroy(font.material); foreach (var atlas in font.atlasTextures) if (atlas) UnityEngine.Object.Destroy(atlas); UnityEngine.Object.Destroy(font); }
            foreach (var device in new InputDevice[] { keyboard, mouse, gamepad, touch }) if (device != null && device.added) InputSystem.RemoveDevice(device);
            if (setup) { InputSystem.settings = previousSettings; if (temporarySettings) UnityEngine.Object.Destroy(temporarySettings); }
            setup = false; yield return null; yield return null;
        }

        private RectTransform Button(string id, Vector2 position, Action clicked)
        {
            var root = new GameObject(id, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button)); root.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)root.transform; rect.sizeDelta = new Vector2(200, 80); rect.anchoredPosition = position;
            root.GetComponent<UnityEngine.UI.Image>().color = new Color(.3f, .7f, .65f);
            root.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(() => clicked?.Invoke());
            var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI)); label.transform.SetParent(root.transform, false);
            var labelRect = (RectTransform)label.transform; labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one; labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            var text = label.GetComponent<TextMeshProUGUI>(); text.font = font; text.fontSize = 24; text.text = id; text.raycastTarget = false; text.alignment = TextAlignmentOptions.Center;
            registrations.Add(context.Targets.Register(id, root)); return rect;
        }
        private static TutorialFocusRequest Request(TutorialInteraction interaction, params string[] ids)
        { var request = new TutorialFocusRequest { Interaction = interaction }; foreach (var id in ids) request.Targets.Add(new TutorialTargetReference { Id = id }); return request; }
        private static Vector2 Position(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(TutorialTargetProjection.UICamera(rect), rect.position);
        private IEnumerator Press(Key key)
        { InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return Frames(2); InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return Frames(2); }
        private IEnumerator GamepadConfirm()
        { InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.South)); yield return Frames(2); InputSystem.QueueStateEvent(gamepad, new GamepadState()); yield return Frames(2); }
        private IEnumerator Click(Vector2 point) { yield return MouseStateAt(point, true); yield return MouseStateAt(point, false); yield return Frames(2); }
        private IEnumerator MouseStateAt(Vector2 point, bool down)
        { InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = (ushort)(down ? 1 : 0) }); yield return Frames(3); }
        private IEnumerator Frames(int count) { while (count-- > 0) { livePresentation?.Tick(); yield return null; } }
        private IEnumerator Realtime(double duration) { double end = Time.realtimeSinceStartupAsDouble + duration; while (Time.realtimeSinceStartupAsDouble < end) { livePresentation?.Tick(); yield return null; } }
        private sealed class ThrowingScope : ITutorialFocusScope
        {
            internal int Releases;
            public void Release(TutorialFocusResult result) { Releases++; throw new InvalidOperationException("synthetic scope failure"); }
        }
        [Serializable] private sealed class Evidence { public bool passed; public string[] outcomes; }
    }
}
