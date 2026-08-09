using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    [Serializable]
    public class ExtractionMerchantState
    {
        public string RotationId;
        public int RotationSeed;
        public List<ExtractionMerchantOfferState> Offers = new();
        public string LastProcessedSettledRaidId;
        public int FreeRotationCount;
        public int PaidRefreshCount;
        public int NextPaidRefreshCost;

        internal void EnsureInitialized()
        {
            Offers ??= new List<ExtractionMerchantOfferState>();
        }
    }

    [Serializable]
    public class ExtractionMerchantOfferState
    {
        public string OfferId;
        public int RemainingStock;

        public ExtractionMerchantOfferState(string offerId, int remainingStock)
        {
            OfferId = offerId;
            RemainingStock = remainingStock;
        }
    }
}
