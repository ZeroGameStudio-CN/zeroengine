namespace ZeroEngine.World.Presentation
{
    public readonly struct WorldVisibilitySnapshot
    {
        private readonly string _targetId;

        internal WorldVisibilitySnapshot(string targetId, float visibility)
        {
            _targetId = targetId;
            Visibility = visibility;
        }

        public string TargetId => _targetId ?? string.Empty;
        public float Visibility { get; }
    }
}
