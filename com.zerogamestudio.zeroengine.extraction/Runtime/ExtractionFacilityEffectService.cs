using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionFacilityEffectService
    {
        public static int GetIntEffect(
            ExtractionProfileSaveData profile,
            List<ExtractionFacilityDefinition> definitions,
            string effectId)
        {
            if (profile == null || definitions == null || string.IsNullOrEmpty(effectId)) return 0;
            profile.EnsureInitialized();

            int total = 0;
            foreach (var definition in definitions)
            {
                if (definition == null || !definition.IsValid) continue;
                if (profile.Facilities.GetLevel(definition.FacilityType) < definition.Level) continue;

                foreach (var effect in definition.Effects)
                {
                    if (effect == null || effect.EffectId != effectId) continue;
                    total += effect.IntValue;
                }
            }

            return total;
        }
    }
}
