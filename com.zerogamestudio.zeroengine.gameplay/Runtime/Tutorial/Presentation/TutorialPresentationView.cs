using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Playables;
using UnityEngine.Video;
using ZeroEngine.Tutorial.Guidance;

namespace ZeroEngine.Tutorial.Presentation
{
    public sealed class TutorialPresentationView : MonoBehaviour, ITutorialPresentationPlayer
    {
        public Transform Container { get; set; }
        public TMP_FontAsset Font { get; set; }
        public int SortingOrder { get; set; } = 250;
        private Handle active;

        public ITutorialPresentation Begin(TutorialMedia media, TutorialContext context, bool allowSkip)
        {
            if (media == null || context == null) throw new ArgumentNullException();
            string error = media.Validate();
            if (error != null) throw new ArgumentException(error);
            if (active != null && active.Result == TutorialPresentationResult.Pending)
                throw new InvalidOperationException("This presentation view is occupied.");
            active?.Dispose();
            var handle = new Handle(this, media, context, allowSkip && media.CanSkip);
            try { handle.Begin(); active = handle; return handle; }
            catch { handle.Dispose(); throw; }
        }

        private void OnDisable() => active?.Dispose();
        private void OnDestroy() => active?.Dispose();
        public void CancelCurrent() => active?.Dispose();

        private sealed class Handle : ITutorialChoicePresentation
        {
            private readonly TutorialPresentationView owner;
            private readonly TutorialMedia media;
            private readonly TutorialContext context;
            private readonly bool allowSkip;
            private RectTransform root, content;
            private TextMeshProUGUI text;
            private UnityEngine.UI.Button continueButton;
            private UnityEngine.UI.RawImage videoImage;
            private VideoPlayer video;
            private PlayableDirector director;
            private RenderTexture texture;
            private IDisposable input;
            private ITutorialFocusScope scope;
            private GameObject previousSelection;
            private readonly List<RectTransform> visible = new List<RectTransform>();
            private readonly List<UnityEngine.UI.Button> choices = new List<UnityEngine.UI.Button>();
            private string[] lines;
            private int line;
            private double began, lineAt;
            private bool videoReady, firstFrame, disposed;
            private int continueFrame = -1;
            private int LastCharacter => media.TypewriterSpeed <= 0 ? int.MaxValue : (int)((Now - lineAt) * media.TypewriterSpeed);
            private double Now => context.Focus.Realtime();
            public TutorialPresentationResult Result { get; private set; }
            public string Error { get; private set; }
            public int SelectedChoice { get; private set; } = -1;
            public bool IsReady => !disposed && (media.Kind != TutorialMediaKind.Video || firstFrame);
            public IReadOnlyList<RectTransform> VisibleContent => visible;

            internal Handle(TutorialPresentationView owner, TutorialMedia media, TutorialContext context, bool allowSkip)
            { this.owner = owner; this.media = media; this.context = context; this.allowSkip = allowSkip; }

            internal void Begin()
            {
                began = lineAt = Now;
                root = Rect("TutorialPresentation", owner.Container ? owner.Container : owner.transform);
                Stretch(root);
                var canvas = root.gameObject.AddComponent<Canvas>(); canvas.overrideSorting = true; canvas.sortingOrder = owner.SortingOrder;
                root.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                bool corner = (media.Kind == TutorialMediaKind.Text || media.Kind == TutorialMediaKind.Dialogue) && media.DialogueLayout == TutorialDialogueLayout.Corner;
                content = corner ? Rect("Corner", root) : root;
                if (corner)
                {
                    content.anchorMin = content.anchorMax = content.pivot = new Vector2(0, 1);
                    content.anchoredPosition = new Vector2(32, -100); content.sizeDelta = new Vector2(560, 210);
                }
                var background = content.gameObject.AddComponent<UnityEngine.UI.Image>(); background.color = new Color(.025f, .035f, .045f, corner ? .94f : .98f);
                background.raycastTarget = !corner;
                visible.Add(content);
                var font = owner.Font ? owner.Font : TMP_Settings.defaultFontAsset;
                if (!font) throw new InvalidOperationException("Configure a tutorial font or import TextMeshPro Essential Resources before presenting media.");
                if (media.Kind == TutorialMediaKind.Text || media.Kind == TutorialMediaKind.Dialogue)
                {
                    var name = Text("Speaker", content, font); name.text = media.Speaker; name.fontSize = corner ? 27 : 34;
                    name.rectTransform.anchorMin = new Vector2(.15f, .7f); name.rectTransform.anchorMax = new Vector2(.85f, .9f);
                    text = Text("Text", content, font); text.fontSize = corner ? 24 : 31;
                    text.rectTransform.anchorMin = new Vector2(.15f, corner ? .1f : .25f);
                    text.rectTransform.anchorMax = new Vector2(.85f, corner ? .68f : .65f);
                    text.alignment = TextAlignmentOptions.TopLeft;
                    lines = media.Text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    text.text = lines[0]; text.maxVisibleCharacters = 0;
                    if (media.Portrait)
                    {
                        var portrait = Rect("Portrait", content); portrait.anchorMin = new Vector2(.015f, .2f); portrait.anchorMax = new Vector2(.14f, .85f);
                        portrait.offsetMin = portrait.offsetMax = Vector2.zero;
                        var image = portrait.gameObject.AddComponent<UnityEngine.UI.Image>(); image.sprite = media.Portrait; image.preserveAspect = true; image.raycastTarget = false;
                    }
                }
                else if (media.Kind == TutorialMediaKind.CG)
                {
                    var image = Rect("CG", root); Stretch(image);
                    var graphic = image.gameObject.AddComponent<UnityEngine.UI.Image>(); graphic.sprite = media.Image; graphic.preserveAspect = true; graphic.raycastTarget = false;
                }
                else if (media.Kind == TutorialMediaKind.Video) PrepareVideo();
                else if (media.Kind == TutorialMediaKind.Timeline) PrepareTimeline();
                if (!corner)
                {
                    scope = context.Focus.AcquireScope?.Invoke();
                    if (EventSystem.current && EventSystem.current.currentInputModule is UnityEngine.InputSystem.UI.InputSystemUIInputModule module)
                        input = new TutorialFocusInputScope(module, new TutorialFocusRequest(), context.Focus);
                    previousSelection = EventSystem.current ? EventSystem.current.currentSelectedGameObject : null;
                    if (text)
                    {
                        if (media.Choices != null && media.Choices.Length > 0)
                        {
                            for (int i = 0; i < media.Choices.Length; i++)
                            {
                                int index = i;
                                var choice = Button("Choice" + i, root, font, media.Choices[i], -100 - 64 * i, () =>
                                {
                                    if (disposed || Result != TutorialPresentationResult.Pending) return;
                                    SelectedChoice = index; Result = TutorialPresentationResult.Completed;
                                    context.Signal("tutorial.choice", index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                                });
                                choice.gameObject.AddComponent<TutorialPresentationCancel>().Cancel = RequestSkip;
                                choices.Add(choice);
                            }
                            EventSystem.current?.SetSelectedGameObject(choices[0].gameObject);
                        }
                        else
                        {
                            continueButton = Button("Continue", root, font, "Continue", -100, () => Continue());
                            continueButton.gameObject.AddComponent<TutorialPresentationCancel>().Cancel = RequestSkip;
                            EventSystem.current?.SetSelectedGameObject(continueButton.gameObject);
                        }
                    }
                }
                if (allowSkip)
                {
                    var skip = Button("Skip", content, font, "Skip", -30, () => RequestSkip());
                    skip.gameObject.AddComponent<TutorialPresentationCancel>().Cancel = RequestSkip;
                    if (!continueButton && choices.Count == 0) EventSystem.current?.SetSelectedGameObject(skip.gameObject);
                }
            }

            private void PrepareVideo()
            {
                var rect = Rect("Video", root); Stretch(rect); videoImage = rect.gameObject.AddComponent<UnityEngine.UI.RawImage>();
                videoImage.raycastTarget = false; videoImage.enabled = false;
                video = root.gameObject.AddComponent<VideoPlayer>(); video.playOnAwake = false; video.clip = media.Video;
                video.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
                video.renderMode = VideoRenderMode.RenderTexture; video.aspectRatio = VideoAspectRatio.FitInside; video.waitForFirstFrame = true;
                if (media.Video.audioTrackCount > 0)
                {
                    var audio = root.gameObject.AddComponent<AudioSource>(); audio.playOnAwake = false; audio.volume = media.Volume; audio.outputAudioMixerGroup = media.AudioMixer;
                    video.audioOutputMode = VideoAudioOutputMode.AudioSource; video.controlledAudioTrackCount = 1; video.SetTargetAudioSource(0, audio);
                }
                else video.audioOutputMode = VideoAudioOutputMode.None;
                video.prepareCompleted += Prepared; video.errorReceived += VideoError; video.loopPointReached += Ended;
                video.sendFrameReadyEvents = true; video.frameReady += Frame;
                video.Prepare();
            }

            private void Prepared(VideoPlayer source)
            {
                if (disposed || Result != TutorialPresentationResult.Pending || source != video) return;
                texture = new RenderTexture(Mathf.Max(1, (int)source.width), Mathf.Max(1, (int)source.height), 0) { hideFlags = HideFlags.HideAndDontSave };
                texture.Create(); source.targetTexture = texture; videoImage.texture = texture; videoReady = true; source.Play();
            }
            private void Frame(VideoPlayer source, long frame) { if (!disposed && source == video) { firstFrame = true; videoImage.enabled = true; } }
            private void VideoError(VideoPlayer source, string message) { if (!disposed && source == video) Fail(message); }
            private void Ended(VideoPlayer source) { if (!disposed && source == video && Result == TutorialPresentationResult.Pending) Result = TutorialPresentationResult.Completed; }

            private void PrepareTimeline()
            {
                director = root.gameObject.AddComponent<PlayableDirector>(); director.playOnAwake = false;
                director.playableAsset = media.Timeline; director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
                director.extrapolationMode = DirectorWrapMode.None;
                foreach (var binding in media.Bindings)
                {
                    var output = media.Timeline.outputs.FirstOrDefault(value => value.streamName == binding.Stream);
                    var target = context.Focus.Targets.Find(binding.TargetId);
                    if (output.sourceObject == null || !target) throw new InvalidOperationException("A Timeline binding is unresolved: " + binding.Stream);
                    UnityEngine.Object value = output.outputTargetType == null || output.outputTargetType.IsInstanceOfType(target) ? target : target.GetComponent(output.outputTargetType);
                    if (!value) throw new InvalidOperationException("A Timeline target has the wrong component type: " + binding.TargetId);
                    director.SetGenericBinding(output.sourceObject, value);
                }
                director.stopped += TimelineEnded; director.Play();
            }
            private void TimelineEnded(PlayableDirector source) { if (!disposed && source == director && Result == TutorialPresentationResult.Pending) Result = TutorialPresentationResult.Completed; }

            public void Tick(bool holdFinalLine = false)
            {
                if (disposed || Result != TutorialPresentationResult.Pending) return;
                if (context.Focus.IsValid != null && !context.Focus.IsValid()) { Result = TutorialPresentationResult.Cancelled; return; }
                if (media.Kind == TutorialMediaKind.Video)
                {
                    if ((!videoReady || !firstFrame) && Now - began > media.PrepareTimeoutSeconds) Fail("Video preparation timed out.");
                    return;
                }
                if (media.Kind == TutorialMediaKind.Timeline)
                {
                    double duration = media.Timeline.duration;
                    if (!double.IsInfinity(duration) && Now - began > duration + media.PrepareTimeoutSeconds) Fail("Timeline playback timed out.");
                    return;
                }
                if (text)
                {
                    text.maxVisibleCharacters = LastCharacter;
                    bool automatic = media.Advance == TutorialAdvanceMode.Timed ||
                        (media.Advance == TutorialAdvanceMode.ByLayout && media.DialogueLayout == TutorialDialogueLayout.Corner);
                    if (automatic && choices.Count == 0 && Now - lineAt >= media.Seconds &&
                        !(holdFinalLine && line == lines.Length - 1)) Continue();
                }
                else if (Now - began >= media.Seconds) Result = TutorialPresentationResult.Completed;
            }

            public void Continue()
            {
                if (disposed || Result != TutorialPresentationResult.Pending || text == null) return;
                if (continueFrame == Time.frameCount) return;
                continueFrame = Time.frameCount;
                if (text.maxVisibleCharacters < text.text.Length) { text.maxVisibleCharacters = int.MaxValue; lineAt = Now - text.text.Length / Math.Max(1, media.TypewriterSpeed); return; }
                if (++line >= lines.Length) { Result = TutorialPresentationResult.Completed; return; }
                lineAt = Now; text.text = lines[line]; text.maxVisibleCharacters = 0;
            }

            public void RequestSkip()
            {
                if (disposed || !allowSkip || Result != TutorialPresentationResult.Pending) return;
                Result = TutorialPresentationResult.Skipped;
                if (video) { video.Pause(); root.GetComponent<AudioSource>()?.Stop(); }
                if (director) director.Pause();
            }
            private void Fail(string error) { if (Result == TutorialPresentationResult.Pending) { Error = error; Result = TutorialPresentationResult.Failed; } }

            public void Dispose()
            {
                if (disposed) return; disposed = true;
                if (Result == TutorialPresentationResult.Pending) Result = TutorialPresentationResult.Cancelled;
                if (video)
                {
                    video.prepareCompleted -= Prepared; video.errorReceived -= VideoError; video.loopPointReached -= Ended; video.frameReady -= Frame;
                    video.Stop(); video.targetTexture = null;
                }
                if (director)
                {
                    director.stopped -= TimelineEnded; director.Stop();
                    foreach (var output in media.Timeline.outputs) director.ClearGenericBinding(output.sourceObject);
                }
                var acquiredInput = input; input = null;
                var acquiredScope = scope; scope = null;
                try { acquiredInput?.Dispose(); }
                finally
                {
                    try
                    {
                        acquiredScope?.Release(Result switch { TutorialPresentationResult.Completed => TutorialFocusResult.Completed,
                            TutorialPresentationResult.Skipped => TutorialFocusResult.Skipped, TutorialPresentationResult.Failed => TutorialFocusResult.Failed,
                            _ => TutorialFocusResult.Cancelled });
                    }
                    finally
                    {
                        if (EventSystem.current && previousSelection && previousSelection.activeInHierarchy &&
                            EventSystem.current.currentSelectedGameObject && root && EventSystem.current.currentSelectedGameObject.transform.IsChildOf(root))
                            EventSystem.current.SetSelectedGameObject(previousSelection);
                        if (texture) { texture.Release(); TutorialObjectLifetime.Destroy(texture); texture = null; }
                        if (root) { root.gameObject.SetActive(false); TutorialObjectLifetime.Destroy(root.gameObject); }
                        visible.Clear();
                    }
                }
            }
        }

        private static RectTransform Rect(string name, Transform parent)
        { var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); return rect; }
        private static void Stretch(RectTransform rect)
        { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
        private static TextMeshProUGUI Text(string name, Transform parent, TMP_FontAsset font)
        {
            var rect = Rect(name, parent); Stretch(rect); var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font; text.fontSize = 25; text.raycastTarget = false; text.text = string.Empty; return text;
        }
        private static UnityEngine.UI.Button Button(string name, Transform parent, TMP_FontAsset font, string label, float y, Action clicked)
        {
            var rect = Rect(name, parent); rect.anchorMin = rect.anchorMax = new Vector2(.9f, 0); rect.pivot = new Vector2(1, 0);
            rect.anchoredPosition = new Vector2(0, -y); rect.sizeDelta = new Vector2(180, 50);
            rect.gameObject.AddComponent<UnityEngine.UI.Image>().color = new Color(.18f, .3f, .3f, 1);
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>(); button.onClick.AddListener(() => clicked());
            var text = Text("Label", rect, font); text.text = label; text.alignment = TextAlignmentOptions.Center; return button;
        }
    }
}
