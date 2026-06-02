using System;

namespace ZeroEngine.Timing
{
    public static class TimeDomainIds
    {
        public const string GlobalValue = "ze.global";
        public const string GameplayValue = "ze.gameplay";
        public const string PresentationValue = "ze.presentation";
        public const string UIValue = "ze.ui";
        public const string CinematicValue = "ze.cinematic";
        public const string WorldClockValue = "ze.world-clock";

        public static readonly TimeDomainId Global = new(GlobalValue);
        public static readonly TimeDomainId Gameplay = new(GameplayValue);
        public static readonly TimeDomainId Presentation = new(PresentationValue);
        public static readonly TimeDomainId UI = new(UIValue);
        public static readonly TimeDomainId Cinematic = new(CinematicValue);
        public static readonly TimeDomainId WorldClock = new(WorldClockValue);

        public static TimeDomainId Project(string projectCode, string domainName)
        {
            string project = Normalize(projectCode);
            string domain = Normalize(domainName);
            if (string.IsNullOrEmpty(project) || string.IsNullOrEmpty(domain))
            {
                return default;
            }

            return new TimeDomainId($"{project}.{domain}");
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToLowerInvariant();
        }
    }
}
