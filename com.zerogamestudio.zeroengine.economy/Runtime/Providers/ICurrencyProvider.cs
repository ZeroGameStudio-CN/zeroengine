namespace ZeroEngine.Economy
{
    public interface ICurrencyProvider
    {
        bool HasCurrency(string currencyId, int amount);
        bool ConsumeCurrency(string currencyId, int amount);
        void AddCurrency(string currencyId, int amount);
        int GetCurrencyBalance(string currencyId);
    }
}
