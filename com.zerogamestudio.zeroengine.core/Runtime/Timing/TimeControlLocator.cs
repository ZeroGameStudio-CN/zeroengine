using UnityEngine;

namespace ZeroEngine.Timing
{
    public static class TimeControlLocator
    {
        private static ITimeControlService _service = new TimeControlService();

        public static ITimeControlService Service
        {
            get => _service;
            set => _service = value ?? new TimeControlService();
        }

        public static void ResetForTests()
        {
            ResetService();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForSubsystemRegistration()
        {
            ResetService();
        }

        private static void ResetService()
        {
            _service = new TimeControlService();
        }
    }
}
