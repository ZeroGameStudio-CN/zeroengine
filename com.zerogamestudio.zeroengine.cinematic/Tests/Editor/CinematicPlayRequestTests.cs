using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Cinematic.Tests
{
    public sealed class CinematicPlayRequestTests
    {
        [Test]
        public void ForSequence_TrimsSequenceIdAndUsesDefaultPolicies()
        {
            var request = CinematicPlayRequest.ForSequence("  cinematic.longleji.storyteller.intro  ");

            Assert.AreEqual("cinematic.longleji.storyteller.intro", request.SequenceId);
            Assert.AreEqual(string.Empty, request.SourceId);
            Assert.IsFalse(request.AllowInterrupt);
            Assert.AreEqual(CinematicSkipPolicy.AllowAfterMinimumPlayback, request.SkipPolicy);
            Assert.AreEqual(CinematicInputLockPolicy.GameplayOnly, request.InputLockPolicy);
            Assert.AreEqual(CinematicCameraRestorePolicy.RestorePrevious, request.CameraRestorePolicy);
            Assert.AreEqual(
                CinematicPlaybackTimeoutPolicy.DefaultMaxPlaybackSeconds,
                request.TimeoutPolicy.MaxPlaybackSeconds);
        }

        [Test]
        public void ForSequence_NormalizesRequestMetadata()
        {
            var request = CinematicPlayRequest.ForSequence(
                "cinematic.test",
                "  npc.storyteller  ",
                allowInterrupt: true);

            Assert.AreEqual("cinematic.test", request.SequenceId);
            Assert.AreEqual("npc.storyteller", request.SourceId);
            Assert.IsTrue(request.AllowInterrupt);
        }

        [Test]
        public void FromSequence_CopiesAuthoringPolicies()
        {
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");
            SetEnum(sequence, "_skipPolicy", (int)CinematicSkipPolicy.Disabled);
            SetEnum(sequence, "_inputLockPolicy", (int)CinematicInputLockPolicy.GameplayAndMenu);
            SetEnum(sequence, "_cameraRestorePolicy", (int)CinematicCameraRestorePolicy.None);
            SetFloat(sequence, "_maxPlaybackSeconds", 4.5f);

            var request = CinematicPlayRequest.FromSequence(sequence);

            Assert.AreEqual("cinematic.test", request.SequenceId);
            Assert.AreEqual(CinematicSkipPolicy.Disabled, request.SkipPolicy);
            Assert.AreEqual(CinematicInputLockPolicy.GameplayAndMenu, request.InputLockPolicy);
            Assert.AreEqual(CinematicCameraRestorePolicy.None, request.CameraRestorePolicy);
            Assert.AreEqual(4.5f, request.TimeoutPolicy.MaxPlaybackSeconds);
            Object.DestroyImmediate(sequence);
        }

        [Test]
        public void FromSequence_CopiesLeaveTimelineCameraPolicy()
        {
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.camera_leave");
            SetEnum(sequence, "_cameraRestorePolicy", (int)CinematicCameraRestorePolicy.LeaveTimelineCamera);

            var request = CinematicPlayRequest.FromSequence(sequence);

            Assert.AreEqual(CinematicCameraRestorePolicy.LeaveTimelineCamera, request.CameraRestorePolicy);
            Object.DestroyImmediate(sequence);
        }

        [Test]
        public void FromSequence_PreservesRequestMetadata()
        {
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test");

            var request = CinematicPlayRequest.FromSequence(
                sequence,
                "  demo.storyteller  ",
                allowInterrupt: true);

            Assert.AreEqual("cinematic.test", request.SequenceId);
            Assert.AreEqual("demo.storyteller", request.SourceId);
            Assert.IsTrue(request.AllowInterrupt);
            Object.DestroyImmediate(sequence);
        }

        private static void SetString(Object target, string propertyName, string value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnum(Object target, string propertyName, int value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).enumValueIndex = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string propertyName, float value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
