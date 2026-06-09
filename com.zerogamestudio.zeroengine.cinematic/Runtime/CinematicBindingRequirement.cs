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

        public CinematicBindingRequirement(string bindingKey, TrackAsset track)
        {
            _bindingKey = bindingKey;
            _track = track;
        }

        public string BindingKey => string.IsNullOrWhiteSpace(_bindingKey)
            ? string.Empty
            : _bindingKey.Trim();

        public TrackAsset Track => _track;
    }
}
