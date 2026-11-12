using NUnit.Framework;
using System.Reflection;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Timeline;

namespace ZeroEngine.Cinematic.Tests
{
    public sealed class CinematicValidationKernelTests
    {
        [Test]
        public void MissingTimeline_ReportsError()
        {
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.missing_timeline");
            var kernel = new CinematicValidationKernel();

            var issues = kernel.ValidateSequence(sequence);

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.MissingTimelineAsset));
            Object.DestroyImmediate(sequence);
        }

        [Test]
        public void ApplyNamedState_ReportsUnsupportedPolicy()
        {
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.unsupported_camera");
            SetEnum(sequence, "_cameraRestorePolicy", CinematicCameraRestorePolicy.ApplyNamedState);
            var kernel = new CinematicValidationKernel();

            var issues = kernel.ValidateSequence(sequence);

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.UnsupportedCameraRestorePolicy));
            Object.DestroyImmediate(sequence);
        }

        [Test]
        public void InvalidMaxPlaybackSeconds_ReportsError()
        {
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.invalid_timeout");
            SetFloat(sequence, "_minimumPlaybackSeconds", 2f);
            SetFloat(sequence, "_maxPlaybackSeconds", 1f);
            var kernel = new CinematicValidationKernel();

            var issues = kernel.ValidateSequence(sequence);

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.InvalidPlaybackTimeout));
            Object.DestroyImmediate(sequence);
        }

        [Test]
        public void InvalidSequenceStableId_ReportsError()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "Cinematic/Longleji Intro");
            SetObject(sequence, "_timelineAsset", timeline);
            var kernel = new CinematicValidationKernel();

            var issues = kernel.ValidateSequence(sequence);

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == "CINEMATIC_INVALID_STABLE_ID"));
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void InvalidBindingStableId_ReportsError()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var track = timeline.CreateTrack<ActivationTrack>(null, "actor.storyteller");
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.invalid_binding");
            SetObject(sequence, "_timelineAsset", timeline);
            SetBindingRequirements(sequence, new[] { new BindingRequirementSetup("actor/Storyteller", track) });
            var kernel = new CinematicValidationKernel();

            var issues = kernel.ValidateSequence(sequence);

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == "CINEMATIC_INVALID_STABLE_ID"));
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void InvalidCommandStableId_ReportsError()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.invalid_command");
            SetObject(sequence, "_timelineAsset", timeline);
            SetCommands(sequence, new[] { "Invalid.Command" });
            var kernel = new CinematicValidationKernel();

            var issues = kernel.ValidateSequence(sequence);

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == "CINEMATIC_INVALID_STABLE_ID"));
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void EmptyBindingRequirement_ReportsError()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.empty_binding");
            SetObject(sequence, "_timelineAsset", timeline);
            SetBindingRequirements(sequence, new[] { string.Empty });
            var kernel = new CinematicValidationKernel();

            var issues = kernel.ValidateSequence(sequence);

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.EmptyBindingKey));
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void BindingRequirementWithoutTrack_ReportsError()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.missing_binding_track");
            SetObject(sequence, "_timelineAsset", timeline);
            SetBindingRequirements(sequence, new[] { new BindingRequirementSetup("actor.storyteller", null) });
            var kernel = new CinematicValidationKernel();

            var issues = kernel.ValidateSequence(sequence);

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.MissingBindingTrack));
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void BindingRequirementTrackOutsideTimeline_ReportsError()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var otherTimeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var foreignTrack = otherTimeline.CreateTrack<ActivationTrack>(null, "actor.storyteller");
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.foreign_binding_track");
            SetObject(sequence, "_timelineAsset", timeline);
            SetBindingRequirements(sequence, new[] { new BindingRequirementSetup("actor.storyteller", foreignTrack) });
            var kernel = new CinematicValidationKernel();

            var issues = kernel.ValidateSequence(sequence);

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.BindingTrackNotInTimeline));
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
            Object.DestroyImmediate(otherTimeline);
        }

        [Test]
        public void TimelineOutputWithoutBindingRequirement_ReportsError()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.CreateTrack<ActivationTrack>(null, "actor.storyteller");
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.unmapped_output");
            SetObject(sequence, "_timelineAsset", timeline);
            var kernel = new CinematicValidationKernel();

            var issues = kernel.ValidateSequence(sequence);

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.MissingBindingRequirement));
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void NullBindingRequirements_TreatsAsEmpty()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.null_bindings");
            SetObject(sequence, "_timelineAsset", timeline);
            SetPrivateField(sequence, "_bindingRequirements", null);
            var kernel = new CinematicValidationKernel();

            var issues = kernel.ValidateSequence(sequence);

            Assert.That(sequence.BindingRequirements, Is.Empty);
            Assert.That(issues, Is.Empty);
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void DuplicateBindingRequirementKey_ReportsError()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var storytellerTrack = timeline.CreateTrack<ActivationTrack>(null, "actor.storyteller");
            var cameraTrack = timeline.CreateTrack<ActivationTrack>(null, "camera.main");
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.duplicate_binding_key");
            SetObject(sequence, "_timelineAsset", timeline);
            SetBindingRequirements(
                sequence,
                new[]
                {
                    new BindingRequirementSetup("actor.storyteller", storytellerTrack),
                    new BindingRequirementSetup("actor.storyteller", cameraTrack)
                });
            var kernel = new CinematicValidationKernel();

            var issues = kernel.ValidateSequence(sequence);

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.DuplicateBindingKey));
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void DuplicateBindingRequirementKey_AfterNormalization_ReportsError()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var storytellerTrack = timeline.CreateTrack<ActivationTrack>(null, "actor.storyteller");
            var cameraTrack = timeline.CreateTrack<ActivationTrack>(null, "camera.main");
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.normalized_duplicate_binding_key");
            SetObject(sequence, "_timelineAsset", timeline);
            SetBindingRequirements(
                sequence,
                new[]
                {
                    new BindingRequirementSetup("actor.storyteller", storytellerTrack),
                    new BindingRequirementSetup("  actor.storyteller  ", cameraTrack)
                });
            var kernel = new CinematicValidationKernel();

            var issues = kernel.ValidateSequence(sequence);

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.DuplicateBindingKey));
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void EmptyCommandId_ReportsError()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.empty_command");
            SetObject(sequence, "_timelineAsset", timeline);
            SetCommands(sequence, new[] { string.Empty });
            var kernel = new CinematicValidationKernel();

            var issues = kernel.ValidateSequence(sequence);

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.EmptyCommandId));
            Object.DestroyImmediate(sequence);
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void ForbiddenSignalReceiverTarget_ReportsError()
        {
            var gameObject = new GameObject("ForbiddenTarget");
            var target = gameObject.AddComponent<ExampleGame.Cinematic.Tests.ForbiddenBusinessReceiver>();
            var kernel = new CinematicValidationKernel(new[] { "ExampleGame." });

            var issues = kernel.ValidateSignalReceiverTarget(target);

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.ForbiddenSignalReceiverTarget));
            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void DefaultSignalReceiverRules_DoNotHardcodeProjectNamespace()
        {
            var gameObject = new GameObject("ProjectTarget");
            var target = gameObject.AddComponent<ExampleGame.Cinematic.Tests.ForbiddenBusinessReceiver>();
            var kernel = new CinematicValidationKernel();

            var issues = kernel.ValidateSignalReceiverTarget(target);

            Assert.IsEmpty(issues);
            Object.DestroyImmediate(gameObject);
        }

        [Test]
        public void SignalReceiverReactionTarget_ReportsForbiddenTarget()
        {
            var signal = ScriptableObject.CreateInstance<SignalAsset>();
            var gameObject = new GameObject("SignalReceiverHost");
            var receiver = gameObject.AddComponent<SignalReceiver>();
            var target = gameObject.AddComponent<ExampleGame.Cinematic.Tests.ForbiddenBusinessReceiver>();
            var reaction = new UnityEvent();
            UnityEventTools.AddPersistentListener(reaction, target.React);
            receiver.AddReaction(signal, reaction);
            var kernel = new CinematicValidationKernel(new[] { "ExampleGame." });

            var issues = kernel.ValidateSignalReceiver(receiver);

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.ForbiddenSignalReceiverTarget));
            Object.DestroyImmediate(gameObject);
            Object.DestroyImmediate(signal);
        }

        [Test]
        public void ValidateCatalog_IncludesCatalogAndSequenceIssues()
        {
            var firstSequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            var secondSequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetString(firstSequence, "_sequenceId", "cinematic.catalog.invalid");
            SetString(secondSequence, "_sequenceId", "cinematic.catalog.invalid");
            SetCatalogSequences(catalog, firstSequence, secondSequence);
            var kernel = new CinematicValidationKernel();

            var issues = kernel.ValidateCatalog(catalog);

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.DuplicateSequenceId));
            Assert.That(issues, Has.Exactly(2)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.MissingTimelineAsset));
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(firstSequence);
            Object.DestroyImmediate(secondSequence);
        }

        private static void SetString(Object target, string propertyName, string value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string propertyName, float value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObject(Object target, string propertyName, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnum(Object target, string propertyName, CinematicCameraRestorePolicy value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).enumValueIndex = (int)value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBindingRequirements(CinematicSequenceDefinition sequence, string[] bindingKeys)
        {
            var serialized = new SerializedObject(sequence);
            var array = serialized.FindProperty("_bindingRequirements");
            array.arraySize = bindingKeys.Length;
            for (var i = 0; i < bindingKeys.Length; i++)
            {
                array.GetArrayElementAtIndex(i).FindPropertyRelative("_bindingKey").stringValue = bindingKeys[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBindingRequirements(
            CinematicSequenceDefinition sequence,
            BindingRequirementSetup[] values)
        {
            var serialized = new SerializedObject(sequence);
            var array = serialized.FindProperty("_bindingRequirements");
            array.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                var element = array.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("_bindingKey").stringValue = values[i].BindingKey;
                element.FindPropertyRelative("_track").objectReferenceValue = values[i].Track;
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetCommands(CinematicSequenceDefinition sequence, string[] commandIds)
        {
            var serialized = new SerializedObject(sequence);
            var array = serialized.FindProperty("_commands");
            array.arraySize = commandIds.Length;
            for (var i = 0; i < commandIds.Length; i++)
            {
                array.GetArrayElementAtIndex(i).FindPropertyRelative("_commandId").stringValue = commandIds[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetCatalogSequences(
            CinematicSequenceCatalog catalog,
            params CinematicSequenceDefinition[] sequences)
        {
            var serialized = new SerializedObject(catalog);
            var array = serialized.FindProperty("_sequences");
            array.arraySize = sequences.Length;
            for (var i = 0; i < sequences.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = sequences[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            target.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }

        private readonly struct BindingRequirementSetup
        {
            public BindingRequirementSetup(string bindingKey, TrackAsset track)
            {
                BindingKey = bindingKey;
                Track = track;
            }

            public string BindingKey { get; }

            public TrackAsset Track { get; }
        }
    }
}

namespace ExampleGame.Cinematic.Tests
{
    public sealed class ForbiddenBusinessReceiver : MonoBehaviour
    {
        public void React()
        {
        }
    }
}
