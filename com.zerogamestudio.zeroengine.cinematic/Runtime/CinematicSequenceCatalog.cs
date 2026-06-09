using System.Collections.Generic;
using UnityEngine;

namespace ZeroEngine.Cinematic
{
    [CreateAssetMenu(fileName = "CinematicSequenceCatalog", menuName = "ZeroEngine/Cinematic/Sequence Catalog")]
    public sealed class CinematicSequenceCatalog : ScriptableObject, ICinematicSequenceResolver
    {
        private static readonly CinematicSequenceDefinition[] EmptySequences =
            new CinematicSequenceDefinition[0];

        [SerializeField] private CinematicSequenceDefinition[] _sequences = new CinematicSequenceDefinition[0];

        public IReadOnlyList<CinematicSequenceDefinition> Sequences => _sequences ?? EmptySequences;

        public bool TryResolve(string sequenceId, out CinematicSequenceDefinition sequence)
        {
            var normalizedSequenceId = string.IsNullOrWhiteSpace(sequenceId)
                ? string.Empty
                : sequenceId.Trim();

            if (!string.IsNullOrEmpty(normalizedSequenceId))
            {
                var sequences = Sequences;
                var matchCount = 0;
                CinematicSequenceDefinition matchedSequence = null;
                for (var i = 0; i < sequences.Count; i++)
                {
                    var candidate = sequences[i];
                    if (candidate != null && candidate.SequenceId == normalizedSequenceId)
                    {
                        matchedSequence = candidate;
                        matchCount++;
                    }
                }

                if (matchCount == 1)
                {
                    sequence = matchedSequence;
                    return true;
                }
            }

            sequence = null;
            return false;
        }

        public IReadOnlyList<CinematicValidationIssue> Validate()
        {
            var issues = new List<CinematicValidationIssue>();
            var seenIds = new HashSet<string>();
            var duplicateIds = new HashSet<string>();
            var sequences = Sequences;

            for (var i = 0; i < sequences.Count; i++)
            {
                var sequence = sequences[i];
                if (sequence == null)
                {
                    issues.Add(new CinematicValidationIssue(
                        CinematicValidationCodes.MissingCatalogSequence,
                        i.ToString(),
                        $"Cinematic sequence catalog slot {i} has no sequence reference."));
                    continue;
                }

                var sequenceId = sequence.SequenceId;
                if (string.IsNullOrWhiteSpace(sequenceId))
                {
                    issues.Add(new CinematicValidationIssue(
                        CinematicValidationCodes.EmptySequenceId,
                        string.Empty,
                        "Cinematic sequence id is empty."));
                    continue;
                }

                if (!seenIds.Add(sequenceId) && duplicateIds.Add(sequenceId))
                {
                    issues.Add(new CinematicValidationIssue(
                        CinematicValidationCodes.DuplicateSequenceId,
                        sequenceId,
                        $"Cinematic sequence id '{sequenceId}' is duplicated."));
                }
            }

            return issues;
        }
    }
}
