using System;
using System.Collections.Generic;

namespace POB.Extraction
{
    public static class ExtractionHostileExplorerSelectionService
    {
        public static bool TryGetCandidates(
            List<ExtractionHostileExplorerDefinition> encounters,
            string mapId,
            int threatLevel,
            List<ExtractionHostileExplorerDefinition> results)
        {
            if (results == null) return false;
            results.Clear();
            if (encounters == null || string.IsNullOrEmpty(mapId)) return false;

            foreach (var candidate in encounters)
            {
                if (candidate == null || !candidate.IsValid) continue;
                if (candidate.MapId != mapId) continue;
                if (threatLevel < candidate.MinThreatLevel) continue;
                results.Add(candidate);
            }

            return results.Count > 0;
        }

        public static bool TrySelectCandidate(
            List<ExtractionHostileExplorerDefinition> candidates,
            int rollValue,
            out ExtractionHostileExplorerDefinition encounter)
        {
            encounter = null;
            int totalWeight = GetTotalValidWeight(candidates);
            if (totalWeight <= 0) return false;

            int target = NormalizeRoll(rollValue, totalWeight);
            int cumulative = 0;
            foreach (var candidate in candidates)
            {
                if (candidate == null || !candidate.IsValid) continue;
                cumulative += candidate.Weight;
                if (target >= cumulative) continue;

                encounter = candidate;
                return true;
            }

            return false;
        }

        public static bool TrySelectCandidateBySeed(
            List<ExtractionHostileExplorerDefinition> candidates,
            int seed,
            out ExtractionHostileExplorerDefinition encounter)
        {
            encounter = null;
            int totalWeight = GetTotalValidWeight(candidates);
            if (totalWeight <= 0) return false;

            var random = new Random(seed);
            return TrySelectCandidate(candidates, random.Next(totalWeight), out encounter);
        }

        private static int GetTotalValidWeight(List<ExtractionHostileExplorerDefinition> candidates)
        {
            if (candidates == null) return 0;

            int totalWeight = 0;
            foreach (var candidate in candidates)
            {
                if (candidate == null || !candidate.IsValid) continue;
                totalWeight += candidate.Weight;
            }

            return totalWeight;
        }

        private static int NormalizeRoll(int rollValue, int totalWeight)
        {
            int result = rollValue % totalWeight;
            return result < 0 ? result + totalWeight : result;
        }
    }
}
