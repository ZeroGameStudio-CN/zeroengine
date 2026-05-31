using System;

namespace ZeroEngine.Currency
{
    public enum CurrencyEventType
    {
        Added,
        Consumed,
        Set,
        BalanceChanged
    }

    [Serializable]
    public struct CurrencyChangedEventArgs
    {
        public string CurrencyId;
        public int PreviousBalance;
        public int NewBalance;
        public int Delta;
        public CurrencyEventType EventType;
    }
}
