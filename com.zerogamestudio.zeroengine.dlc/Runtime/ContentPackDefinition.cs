using System;
using UnityEngine;

namespace ZeroEngine.Dlc
{
    [Serializable]
    public sealed class ContentPackDefinition
    {
        [SerializeField] private string _contentPackId;
        [SerializeField] private bool _includedInBaseGame;
        [SerializeField] private string _requiredDlcId;
        [SerializeField] private string _displayName;

        public ContentPackDefinition()
        {
        }

        public ContentPackDefinition(string contentPackId, bool includedInBaseGame, string requiredDlcId, string displayName)
        {
            _contentPackId = contentPackId;
            _includedInBaseGame = includedInBaseGame;
            _requiredDlcId = requiredDlcId;
            _displayName = displayName;
        }

        public string ContentPackId => _contentPackId;
        public bool IncludedInBaseGame => _includedInBaseGame;
        public string RequiredDlcId => _requiredDlcId;
        public string DisplayName => _displayName;

        public bool RequiresDlc => !_includedInBaseGame && !string.IsNullOrWhiteSpace(_requiredDlcId);
    }
}
