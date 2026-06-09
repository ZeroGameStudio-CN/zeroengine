namespace ZeroEngine.TCE.EditorTesting
{
    public interface ITceAdapterContractFixture
    {
        ITceActor CreateAliveActor();
        ITceActor CreateDeadActor();
        ITceClock CreateClock(float initialTime);
        void SetClockTime(ITceClock clock, float time);
    }
}
