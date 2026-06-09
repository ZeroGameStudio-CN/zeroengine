using UnityEngine;

namespace ZeroEngine.Cinematic
{
    [System.Serializable]
    public struct CinematicCommandDefinition
    {
        [SerializeField] private CinematicCommandPhase _phase;
        [SerializeField] private string _commandId;
        [SerializeField] private string _payload;

        public CinematicCommandDefinition(
            CinematicCommandPhase phase,
            string commandId,
            string payload)
        {
            _phase = phase;
            _commandId = commandId ?? string.Empty;
            _payload = payload ?? string.Empty;
        }

        public CinematicCommandPhase Phase => _phase;

        public string CommandId => string.IsNullOrWhiteSpace(_commandId)
            ? string.Empty
            : _commandId.Trim();

        public string Payload => _payload;
    }
}
