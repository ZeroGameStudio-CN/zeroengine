using System;
using System.Collections.Generic;

#nullable enable

namespace ZeroEngine.Dlc
{
    public sealed class LocalDlcEntitlementService : IDlcEntitlementService
    {
        private readonly Dictionary<string, DlcEntitlement> _entitlements = new(StringComparer.Ordinal);

        public DlcEntitlement GetEntitlement(string? dlcId)
        {
            if (string.IsNullOrWhiteSpace(dlcId))
            {
                return DlcEntitlement.Unavailable;
            }

            return _entitlements.TryGetValue(dlcId, out var entitlement)
                ? entitlement
                : DlcEntitlement.Unavailable;
        }

        public void SetEntitlement(string? dlcId, DlcEntitlement entitlement)
        {
            if (string.IsNullOrWhiteSpace(dlcId))
            {
                return;
            }

            _entitlements[dlcId] = entitlement;
        }

        public void Clear()
        {
            _entitlements.Clear();
        }
    }
}
