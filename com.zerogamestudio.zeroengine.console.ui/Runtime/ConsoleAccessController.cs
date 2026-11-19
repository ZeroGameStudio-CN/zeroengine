using System;
using UnityEngine;
using ZeroEngine.Console.Unity;

namespace ZeroEngine.Console.UI
{
    /// <summary>Shared Editor access and session-only hidden release activation.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(ConsoleHost), typeof(NativeConsole))]
    [AddComponentMenu("ZeroEngine/ZE Console Access")]
    public sealed class ConsoleAccessController : MonoBehaviour
    {
        [SerializeField] private string unlockSequence = string.Empty;
        [SerializeField, Range(0, 12)] private int hiddenCornerTaps = 7;
        [SerializeField, Range(.03f, .15f)] private float hiddenCornerSize = .08f;
        private ConsoleHost host;
        private NativeConsole view;
        private int sequencePosition, cornerTaps;
        private int[] sequencePrefixes = Array.Empty<int>();
        private float lastInput;
        private bool previousShortcut;
        public bool IsReleaseUnlocked { get; private set; }
        public bool CanOpen => Application.isEditor || Debug.isDebugBuild || IsReleaseUnlocked;
        public event Action<bool> AccessChanged;

        private void OnEnable()
        {
            host = GetComponent<ConsoleHost>(); view = GetComponent<NativeConsole>();
            previousShortcut = view.KeyboardShortcutEnabled;
            view.KeyboardShortcutEnabled = false;
            PrepareSequence();
        }

        public void ConfigureHiddenPath(string sequence, int cornerTapCount = 7)
        {
            if ((sequence?.Length ?? 0) > 128 || cornerTapCount < 0 || cornerTapCount > 12)
                throw new ArgumentOutOfRangeException(nameof(sequence));
            unlockSequence = sequence ?? string.Empty; hiddenCornerTaps = cornerTapCount;
            sequencePosition = cornerTaps = 0;
            PrepareSequence();
        }

        private void PrepareSequence()
        {
            if (unlockSequence.Length > 128) throw new InvalidOperationException("Unlock sequence exceeds its bound.");
            sequencePrefixes = new int[unlockSequence.Length];
            for (int i = 1; i < unlockSequence.Length; i++)
            {
                int prefix = sequencePrefixes[i - 1];
                while (prefix > 0 && unlockSequence[i] != unlockSequence[prefix]) prefix = sequencePrefixes[prefix - 1];
                if (unlockSequence[i] == unlockSequence[prefix]) prefix++;
                sequencePrefixes[i] = prefix;
            }
        }

        public bool FeedHiddenInput(string text)
        {
            if (string.IsNullOrEmpty(unlockSequence) || string.IsNullOrEmpty(text)) return false;
            foreach (char character in text)
            {
                while (sequencePosition > 0 && character != unlockSequence[sequencePosition]) sequencePosition = sequencePrefixes[sequencePosition - 1];
                if (character == unlockSequence[sequencePosition]) sequencePosition++;
                if (sequencePosition != unlockSequence.Length) continue;
                sequencePosition = 0; Unlock(); return true;
            }
            return false;
        }

        public bool TryUnlock(string sequence)
        {
            if (string.IsNullOrEmpty(unlockSequence) || !string.Equals(sequence, unlockSequence, StringComparison.Ordinal)) return false;
            Unlock(); return true;
        }

        // Bind this to a project's existing hidden path; no project-specific controller is needed.
        public void Unlock()
        {
            if (!IsReleaseUnlocked)
            {
                IsReleaseUnlocked = true; host.SetReleaseAccessForSession(true); AccessChanged?.Invoke(true);
            }
        }

        public void Toggle()
        {
            if (!CanOpen) return;
            view.Toggle();
        }

        public void Lock()
        {
            view.Close(); IsReleaseUnlocked = false; sequencePosition = cornerTaps = 0;
            host.SetReleaseAccessForSession(false); AccessChanged?.Invoke(false);
        }

        private void Update()
        {
            if (CanOpen)
            {
                if (!view.IsComposing && ConsoleKeyboard.Pressed(KeyCode.BackQuote)) Toggle();
                ReadHiddenCorner();
                return;
            }
            if (Time.unscaledTime - lastInput > 3f) sequencePosition = cornerTaps = 0;
            if (!string.IsNullOrEmpty(unlockSequence))
            {
                string text = ConsoleInputBackend.ReadActivationText();
                if (!string.IsNullOrEmpty(text))
                {
                    lastInput = Time.unscaledTime;
                    if (FeedHiddenInput(text)) { Toggle(); return; }
                }
            }
            ReadHiddenCorner();
        }

        private void ReadHiddenCorner()
        {
            if (Time.unscaledTime - lastInput > 3f) cornerTaps = 0;
            if (hiddenCornerTaps == 0 || !ConsoleInputBackend.TryReadActivationPointer(out Vector2 point)) return;
            if (point.x > Screen.width * hiddenCornerSize || point.y < Screen.height * (1 - hiddenCornerSize)) { cornerTaps = 0; return; }
            lastInput = Time.unscaledTime;
            if (++cornerTaps >= hiddenCornerTaps) { cornerTaps = 0; Unlock(); Toggle(); }
        }

        private void OnDisable()
        {
            if (view) { view.Close(); view.KeyboardShortcutEnabled = previousShortcut; }
            if (host) host.SetReleaseAccessForSession(false);
            IsReleaseUnlocked = false; sequencePosition = cornerTaps = 0;
        }
    }
}
