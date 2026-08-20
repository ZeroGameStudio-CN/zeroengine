using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Timeline;

namespace ZeroEngine.Cinematic
{
    public sealed class CinematicValidationKernel
    {
        private static readonly string[] EmptyForbiddenNamespacePrefixes = new string[0];

        private readonly string[] _forbiddenNamespacePrefixes;

        public CinematicValidationKernel()
            : this(EmptyForbiddenNamespacePrefixes)
        {
        }

        public CinematicValidationKernel(string[] forbiddenNamespacePrefixes)
        {
            _forbiddenNamespacePrefixes = forbiddenNamespacePrefixes == null || forbiddenNamespacePrefixes.Length == 0
                ? EmptyForbiddenNamespacePrefixes
                : (string[])forbiddenNamespacePrefixes.Clone();
        }

        public IReadOnlyList<CinematicValidationIssue> ValidateCatalog(CinematicSequenceCatalog catalog)
        {
            var issues = new List<CinematicValidationIssue>();
            if (catalog == null)
            {
                return issues;
            }

            issues.AddRange(catalog.Validate());
            var sequences = catalog.Sequences;
            for (var i = 0; i < sequences.Count; i++)
            {
                issues.AddRange(ValidateSequence(sequences[i]));
            }

            return issues;
        }

        public IReadOnlyList<CinematicValidationIssue> ValidateSequence(CinematicSequenceDefinition sequence)
        {
            var issues = new List<CinematicValidationIssue>();
            if (sequence == null)
            {
                return issues;
            }

            var contextId = sequence.SequenceId;
            if (string.IsNullOrWhiteSpace(contextId))
            {
                issues.Add(new CinematicValidationIssue(
                    CinematicValidationCodes.EmptySequenceId,
                    string.Empty,
                    "Cinematic sequence id is empty."));
            }
            else if (!CinematicStableId.IsValid(contextId))
            {
                issues.Add(new CinematicValidationIssue(
                    CinematicValidationCodes.InvalidStableId,
                    contextId,
                    $"Cinematic sequence id '{contextId}' is not a valid stable id."));
            }

            if (sequence.TimelineAsset == null)
            {
                issues.Add(new CinematicValidationIssue(
                    CinematicValidationCodes.MissingTimelineAsset,
                    contextId,
                    $"Cinematic sequence '{contextId}' has no TimelineAsset."));
            }

            if (sequence.CameraRestorePolicy == CinematicCameraRestorePolicy.ApplyNamedState)
            {
                issues.Add(new CinematicValidationIssue(
                    CinematicValidationCodes.UnsupportedCameraRestorePolicy,
                    contextId,
                    "ApplyNamedState camera restore is not supported by the generic cinematic kernel."));
            }

            if (sequence.MaxPlaybackSeconds <= 0f || sequence.MaxPlaybackSeconds <= sequence.MinimumPlaybackSeconds)
            {
                issues.Add(new CinematicValidationIssue(
                    CinematicValidationCodes.InvalidPlaybackTimeout,
                    contextId,
                    $"Cinematic sequence '{contextId}' max playback seconds must be greater than its minimum."));
            }

            var bindingRequirements = sequence.BindingRequirements;
            var requiredTracks = new HashSet<UnityEngine.Timeline.TrackAsset>();
            var seenBindingKeys = new HashSet<string>();
            var duplicateBindingKeys = new HashSet<string>();
            for (var i = 0; i < bindingRequirements.Count; i++)
            {
                var requirement = bindingRequirements[i];
                if (string.IsNullOrWhiteSpace(requirement.BindingKey))
                {
                    issues.Add(new CinematicValidationIssue(
                        CinematicValidationCodes.EmptyBindingKey,
                        contextId,
                        $"Cinematic sequence '{contextId}' contains an empty binding key."));
                }
                else if (!CinematicStableId.IsValid(requirement.BindingKey))
                {
                    issues.Add(new CinematicValidationIssue(
                        CinematicValidationCodes.InvalidStableId,
                        contextId,
                        $"Cinematic sequence '{contextId}' binding key '{requirement.BindingKey}' is not a valid stable id."));
                }
                else if (!seenBindingKeys.Add(requirement.BindingKey) &&
                    duplicateBindingKeys.Add(requirement.BindingKey))
                {
                    issues.Add(new CinematicValidationIssue(
                        CinematicValidationCodes.DuplicateBindingKey,
                        contextId,
                        $"Cinematic sequence '{contextId}' binding key '{requirement.BindingKey}' is duplicated."));
                }

                if (requirement.Track == null)
                {
                    issues.Add(new CinematicValidationIssue(
                        CinematicValidationCodes.MissingBindingTrack,
                        contextId,
                        $"Cinematic sequence '{contextId}' binding '{requirement.BindingKey}' has no Timeline track."));
                }
                else if (sequence.TimelineAsset != null && !TimelineContainsOutputTrack(sequence, requirement.Track))
                {
                    issues.Add(new CinematicValidationIssue(
                        CinematicValidationCodes.BindingTrackNotInTimeline,
                        contextId,
                        $"Cinematic sequence '{contextId}' binding '{requirement.BindingKey}' points to a track outside the Timeline asset."));
                }
                else
                {
                    requiredTracks.Add(requirement.Track);
                }
            }

            if (sequence.TimelineAsset != null)
            {
                foreach (var outputTrack in sequence.TimelineAsset.GetOutputTracks())
                {
                    if (!requiredTracks.Contains(outputTrack))
                    {
                        issues.Add(new CinematicValidationIssue(
                            CinematicValidationCodes.MissingBindingRequirement,
                            contextId,
                            $"Cinematic sequence '{contextId}' Timeline output track '{outputTrack.name}' has no binding requirement."));
                    }
                }
            }

            var commands = sequence.Commands;
            for (var i = 0; i < commands.Count; i++)
            {
                var commandId = commands[i].CommandId;
                if (string.IsNullOrWhiteSpace(commandId))
                {
                    issues.Add(new CinematicValidationIssue(
                        CinematicValidationCodes.EmptyCommandId,
                        contextId,
                        $"Cinematic sequence '{contextId}' contains an empty command id."));
                }
                else if (!CinematicStableId.IsValid(commandId))
                {
                    issues.Add(new CinematicValidationIssue(
                        CinematicValidationCodes.InvalidStableId,
                        contextId,
                        $"Cinematic sequence '{contextId}' command id '{commandId}' is not a valid stable id."));
                }
            }

            return issues;
        }

        private static bool TimelineContainsOutputTrack(
            CinematicSequenceDefinition sequence,
            UnityEngine.Timeline.TrackAsset track)
        {
            foreach (var outputTrack in sequence.TimelineAsset.GetOutputTracks())
            {
                if (outputTrack == track)
                {
                    return true;
                }
            }

            return false;
        }

        public IReadOnlyList<CinematicValidationIssue> ValidateSignalReceiver(SignalReceiver receiver)
        {
            var issues = new List<CinematicValidationIssue>();
            if (receiver == null)
            {
                return issues;
            }

            for (var reactionIndex = 0; reactionIndex < receiver.Count(); reactionIndex++)
            {
                var reaction = receiver.GetReactionAtIndex(reactionIndex);
                if (reaction == null)
                {
                    continue;
                }

                for (var targetIndex = 0; targetIndex < reaction.GetPersistentEventCount(); targetIndex++)
                {
                    issues.AddRange(ValidateSignalReceiverTarget(
                        reaction.GetPersistentTarget(targetIndex)));
                }
            }

            return issues;
        }

        public IReadOnlyList<CinematicValidationIssue> ValidateSignalReceiverTarget(Object target)
        {
            var issues = new List<CinematicValidationIssue>();
            if (target == null)
            {
                return issues;
            }

            var targetType = target.GetType();
            var targetNamespace = targetType.Namespace ?? string.Empty;
            for (var i = 0; i < _forbiddenNamespacePrefixes.Length; i++)
            {
                var prefix = _forbiddenNamespacePrefixes[i];
                if (string.IsNullOrWhiteSpace(prefix))
                {
                    continue;
                }

                if (targetNamespace.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    issues.Add(new CinematicValidationIssue(
                        CinematicValidationCodes.ForbiddenSignalReceiverTarget,
                        target.name,
                        $"Signal receiver target '{targetType.FullName}' belongs to a forbidden namespace."));
                    break;
                }
            }

            return issues;
        }
    }
}
