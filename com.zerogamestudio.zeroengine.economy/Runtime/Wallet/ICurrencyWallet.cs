namespace ZeroEngine.Wallet
{
    public interface ICurrencyWallet
    {
        int GetBalance(string currencyId);
        bool CanSpend(string currencyId, int amount);
        bool TrySpend(string currencyId, int amount);
        void Add(string currencyId, int amount);
    }
}
