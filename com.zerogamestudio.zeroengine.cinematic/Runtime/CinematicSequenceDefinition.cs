using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Timeline;

namespace ZeroEngine.Cinematic
{
    [CreateAssetMenu(fileName = "CinematicSequence", menuName = "ZeroEngine/Cinematic/Sequence Definition")]
    public sealed class CinematicSequenceDefinition : ScriptableObject
    {
        private static readonly CinematicCommandDefinition[] EmptyCommands =
            new CinematicCommandDefinition[0];
        private static readonly CinematicBindingRequirement[] EmptyBindingRequirements =
            new CinematicBindingRequirement[0];

        [SerializeField] private string _sequenceId;
        [SerializeField] private TimelineAsset _timelineAsset;
        [SerializeField] private CinematicBindingRequirement[] _bindingRequirements = new CinematicBindingRequirement[0];
        [SerializeField] private CinematicCommandDefinition[] _commands = new CinematicCommandDefinition[0];
        [SerializeField] private CinematicSkipPolicy _skipPolicy = CinematicSkipPolicy.AllowAfterMinimumPlayback;
        [SerializeField] private CinematicInputLockPolicy _inputLockPolicy = CinematicInputLockPolicy.GameplayOnly;
        [SerializeField] private CinematicCameraRestorePolicy _cameraRestorePolicy = CinematicCameraRestorePolicy.RestorePrevious;
        [SerializeField] private float _minimumPlaybackSeconds = 0.05f;
        [SerializeField] private float _maxPlaybackSeconds = CinematicPlaybackTimeoutPolicy.DefaultMaxPlaybackSeconds;

        public string SequenceId => string.IsNullOrWhiteSpace(_sequenceId)
            ? string.Empty
            : _sequenceId.Trim();

        public TimelineAsset TimelineAsset => _timelineAsset;

        public IReadOnlyList<CinematicBindingRequirement> BindingRequirements =>
            _bindingRequirements ?? EmptyBindingRequirements;

        public IReadOnlyList<CinematicCommandDefinition> Commands => _commands ?? EmptyCommands;

        public CinematicSkipPolicy SkipPolicy => _skipPolicy;

        public CinematicInputLockPolicy InputLockPolicy => _inputLockPolicy;

        public CinematicCameraRestorePolicy CameraRestorePolicy => _cameraRestorePolicy;

        public float MinimumPlaybackSeconds => Mathf.Max(0.01f, _minimumPlaybackSeconds);

        public float MaxPlaybackSeconds => _maxPlaybackSeconds;

        public CinematicPlaybackTimeoutPolicy TimeoutPolicy => new(_maxPlaybackSeconds);
    }
}
