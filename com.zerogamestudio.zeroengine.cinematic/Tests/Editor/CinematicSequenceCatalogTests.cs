using NUnit.Framework;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.Cinematic.Tests
{
    public sealed class CinematicSequenceCatalogTests
    {
        [Test]
        public void TryResolve_ReturnsSequenceByStableId()
        {
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test.intro");
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });

            var resolved = catalog.TryResolve("cinematic.test.intro", out var result);

            Assert.IsTrue(resolved);
            Assert.AreSame(sequence, result);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
        }

        [Test]
        public void TryResolve_NormalizesRequestedStableId()
        {
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(sequence, "_sequenceId", "cinematic.test.intro");
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });

            var resolved = catalog.TryResolve("  cinematic.test.intro  ", out var result);

            Assert.IsTrue(resolved);
            Assert.AreSame(sequence, result);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
        }

        [Test]
        public void Validate_DuplicateIds_ReportsValidationError()
        {
            var first = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            var second = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(first, "_sequenceId", "cinematic.duplicate");
            SetString(second, "_sequenceId", "cinematic.duplicate");
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { first, second });

            var issues = catalog.Validate();

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.DuplicateSequenceId &&
                    issue.ContextId == "cinematic.duplicate"));
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }

        [Test]
        public void Validate_NormalizedDuplicateIds_ReportsValidationError()
        {
            var first = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            var second = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(first, "_sequenceId", "cinematic.duplicate");
            SetString(second, "_sequenceId", "  cinematic.duplicate  ");
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { first, second });

            var issues = catalog.Validate();

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.DuplicateSequenceId &&
                    issue.ContextId == "cinematic.duplicate"));
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }

        [Test]
        public void TryResolve_DuplicateIds_ReturnsFalseWithoutSelectingAmbiguousSequence()
        {
            var first = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            var second = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            SetString(first, "_sequenceId", "cinematic.duplicate");
            SetString(second, "_sequenceId", "cinematic.duplicate");
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { first, second });

            var resolved = catalog.TryResolve("cinematic.duplicate", out var result);

            Assert.IsFalse(resolved);
            Assert.IsNull(result);
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(first);
            Object.DestroyImmediate(second);
        }

        [Test]
        public void Validate_EmptyId_ReportsValidationError()
        {
            var sequence = ScriptableObject.CreateInstance<CinematicSequenceDefinition>();
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { sequence });

            var issues = catalog.Validate();

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.EmptySequenceId));
            Object.DestroyImmediate(catalog);
            Object.DestroyImmediate(sequence);
        }

        [Test]
        public void Validate_NullSequenceReference_ReportsValidationError()
        {
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetObjectArray(catalog, "_sequences", new Object[] { null });

            var issues = catalog.Validate();

            Assert.That(issues, Has.Exactly(1)
                .Matches<CinematicValidationIssue>(issue =>
                    issue.Code == CinematicValidationCodes.MissingCatalogSequence));
            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void Validate_NullSequenceArray_TreatsAsEmpty()
        {
            var catalog = ScriptableObject.CreateInstance<CinematicSequenceCatalog>();
            SetPrivateField(catalog, "_sequences", null);

            var resolved = catalog.TryResolve("cinematic.missing", out var sequence);
            var issues = catalog.Validate();

            Assert.IsFalse(resolved);
            Assert.IsNull(sequence);
            Assert.That(catalog.Sequences, Is.Empty);
            Assert.That(issues, Is.Empty);
            Object.DestroyImmediate(catalog);
        }

        private static void SetString(Object target, string propertyName, string value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObjectArray(Object target, string propertyName, Object[] values)
        {
            var serialized = new SerializedObject(target);
            var array = serialized.FindProperty(propertyName);
            array.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            target.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }
    }
}
