using System.Collections.Generic;

namespace ZeroEngine.Formula
{
    public sealed class FormulaProviderRegistry
    {
        private readonly Dictionary<string, IFormulaValueProvider> providers = new();

        public static FormulaProviderRegistry Empty => new();

        public void Register(IFormulaValueProvider provider)
        {
            if (provider == null || string.IsNullOrWhiteSpace(provider.Id))
                return;

            providers[provider.Id] = provider;
        }

        public bool TryGetProvider(string providerId, out IFormulaValueProvider provider)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                provider = null;
                return false;
            }

            return providers.TryGetValue(providerId, out provider);
        }
    }
}
