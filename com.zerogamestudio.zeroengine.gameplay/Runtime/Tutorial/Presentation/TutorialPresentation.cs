using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Playables;
using UnityEngine.Video;

namespace ZeroEngine.Tutorial.Presentation
{
    public enum TutorialMediaKind { Text = 0, Dialogue = 1, CG = 2, Video = 3, Timeline = 4, Custom = 5 }
    public enum TutorialDialogueLayout { Corner = 0, Normal = 1 }
    public enum TutorialAdvanceMode { ByLayout = 0, Manual = 1, Timed = 2 }
    public enum TutorialPresentationResult { Pending = 0, Completed = 1, Skipped = 2, Cancelled = 3, Failed = 4 }

    [Serializable]
    public sealed class TutorialTimelineBinding
    {
        public string Stream;
        public string TargetId;
    }

    [Serializable]
    public sealed class TutorialMedia
    {
        public string Id;
        public TutorialMediaKind Kind;
        [Tooltip("Empty selects the built-in player. Projects register external dialogue/media backends by this key.")]
        public string Backend;
        public TutorialDialogueLayout DialogueLayout;
        public TutorialAdvanceMode Advance;
        public string Conversation;
        public string Speaker;
        public Sprite Portrait;
        [TextArea(2, 6)] public string Text;
        public string[] Choices = Array.Empty<string>();
        public Sprite Image;
        public VideoClip Video;
        public PlayableAsset Timeline;
        public List<TutorialTimelineBinding> Bindings = new List<TutorialTimelineBinding>();
        public AudioMixerGroup AudioMixer;
        [Range(0, 1)] public float Volume = .6f;
        [Min(.05f)] public float Seconds = 3;
        [Min(.1f)] public float PrepareTimeoutSeconds = 15;
        [Min(0)] public float TypewriterSpeed = 30;
        public bool CanSkip = true;
        public bool Placeholder;
        public string ReplacementNote;

        public string Validate()
        {
            if (string.IsNullOrWhiteSpace(Id)) return "A presentation needs an ID.";
            if (Placeholder && string.IsNullOrWhiteSpace(ReplacementNote)) return "Placeholder media needs a replacement note.";
            if (!string.IsNullOrEmpty(Backend)) return null;
            if (Kind == TutorialMediaKind.CG && !Image) return "CG media needs a sprite.";
            if (Kind == TutorialMediaKind.Video && !Video) return "Video media needs a clip.";
            if (Kind == TutorialMediaKind.Timeline && !Timeline) return "Timeline media needs a playable asset.";
            if (Kind == TutorialMediaKind.Custom || (Kind == TutorialMediaKind.Dialogue && !string.IsNullOrEmpty(Conversation)))
                return "External media needs a registered backend.";
            if ((Kind == TutorialMediaKind.Text || Kind == TutorialMediaKind.Dialogue) && string.IsNullOrWhiteSpace(Text))
                return "Text media needs nonempty text.";
            if (Choices != null && Choices.Length > 0 && DialogueLayout != TutorialDialogueLayout.Normal)
                return "Built-in choices require normal dialogue layout.";
            if (Seconds <= 0 || PrepareTimeoutSeconds <= 0) return "Presentation durations must be positive.";
            return null;
        }
    }

    public interface ITutorialPresentation : IDisposable
    {
        TutorialPresentationResult Result { get; }
        string Error { get; }
        bool IsReady { get; }
        IReadOnlyList<RectTransform> VisibleContent { get; }
        void Tick(bool holdFinalLine = false);
        void RequestSkip();
        void Continue();
    }

    public interface ITutorialPresentationPlayer
    {
        ITutorialPresentation Begin(TutorialMedia media, TutorialContext context, bool allowSkip);
    }

    public interface ITutorialChoicePresentation : ITutorialPresentation
    {
        int SelectedChoice { get; }
    }

    public sealed class TutorialPresentationRegistry
    {
        private readonly Dictionary<string, ITutorialPresentationPlayer> players = new Dictionary<string, ITutorialPresentationPlayer>(StringComparer.Ordinal);
        public ITutorialPresentationPlayer DefaultPlayer { get; set; }
        public IDisposable Register(string key, ITutorialPresentationPlayer player)
        {
            if (string.IsNullOrWhiteSpace(key) || player == null) throw new ArgumentException("A backend needs a key and player.");
            if (players.ContainsKey(key)) throw new InvalidOperationException("A presentation backend already owns: " + key);
            players.Add(key, player);
            return new Registration(this, key, player);
        }
        public ITutorialPresentation Begin(TutorialMedia media, TutorialContext context, bool allowSkip)
        {
            if (media == null) throw new ArgumentNullException(nameof(media));
            string error = media.Validate(); if (error != null) throw new ArgumentException(error);
            var player = string.IsNullOrEmpty(media.Backend) ? DefaultPlayer : players.TryGetValue(media.Backend, out var custom) ? custom : null;
            if (player == null) throw new InvalidOperationException("No presentation backend is registered for: " + media.Backend);
            return player.Begin(media, context, allowSkip && media.CanSkip) ?? throw new InvalidOperationException("The presentation backend returned no handle.");
        }
        private sealed class Registration : IDisposable
        {
            private TutorialPresentationRegistry owner;
            private readonly string key;
            private readonly ITutorialPresentationPlayer player;
            internal Registration(TutorialPresentationRegistry owner, string key, ITutorialPresentationPlayer player) { this.owner = owner; this.key = key; this.player = player; }
            public void Dispose()
            {
                if (owner != null && owner.players.TryGetValue(key, out var current) && ReferenceEquals(current, player)) owner.players.Remove(key);
                owner = null;
            }
        }
    }
}
