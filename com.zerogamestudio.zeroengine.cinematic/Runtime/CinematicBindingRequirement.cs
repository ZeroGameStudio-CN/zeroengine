using System;
using UnityEngine;
using UnityEngine.Timeline;

namespace ZeroEngine.Cinematic
{
    [Serializable]
    public struct CinematicBindingRequirement
    {
        [SerializeField] private string _bindingKey;
        [SerializeField] private TrackAsset _track;
        [SerializeField] private bool _managedByProjectService;

        public CinematicBindingRequirement(string bindingKey, TrackAsset track)
            : this(bindingKey, track, false)
        {
        }

        public CinematicBindingRequirement(string bindingKey, TrackAsset track, bool managedByProjectService)
        {
            _bindingKey = bindingKey;
            _track = track;
            _managedByProjectService = managedByProjectService;
        }

        public string BindingKey => string.IsNullOrWhiteSpace(_bindingKey)
            ? string.Empty
            : _bindingKey.Trim();

        public TrackAsset Track => _track;

        /// <summary>The project service binds and releases this native track before playback starts.</summary>
        public bool ManagedByProjectService => _managedByProjectService;
    }
}
