namespace ZeroEngine.Dlc
{
    public readonly struct ContentAvailabilityResult
    {
        public ContentAvailabilityResult(
            string contentPackId,
            string requiredDlcId,
            ContentAvailabilityStatus status)
        {
            ContentPackId = contentPackId;
            RequiredDlcId = requiredDlcId;
            Status = status;
        }

        public string ContentPackId { get; }
        public string RequiredDlcId { get; }
        public ContentAvailabilityStatus Status { get; }
        public bool Available => Status == ContentAvailabilityStatus.Available;
    }
}
