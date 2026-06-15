using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ZeroEngine.TCE.Editor;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceComponentCatalogTests
    {
        private const string CatalogPath = "Packages/com.zerogamestudio.zeroengine.tce/Documentation~/component-catalog.md";
        private const string CatalogWriterPath = "Packages/com.zerogamestudio.zeroengine.tce/Editor/Documentation/TceComponentCatalogWriter.cs";

        [Test]
        public void ComponentCatalog_IncludesEveryConcreteComponentDataType()
        {
            string[] concreteDataTypes = typeof(TceComponentData).Assembly
                .GetTypes()
                .Where(type => !type.IsAbstract && typeof(TceComponentData).IsAssignableFrom(type))
                .Select(type => type.FullName)
                .OrderBy(name => name)
                .ToArray();

            string[] catalogDataTypes = TceComponentCatalogBuilder.Build()
                .Select(entry => entry.DataTypeFullName)
                .OrderBy(name => name)
                .ToArray();

            CollectionAssert.AreEqual(concreteDataTypes, catalogDataTypes);
        }

        [Test]
        public void ComponentCatalog_OutputIsDeterministic()
        {
            string first = TceComponentCatalogWriter.WriteMarkdown(TceComponentCatalogBuilder.Build());
            string second = TceComponentCatalogWriter.WriteMarkdown(TceComponentCatalogBuilder.Build());

            Assert.AreEqual(first, second);
        }

        [Test]
        public void ComponentCatalog_ComponentIdsAreStableUniqueAndNamespaced()
        {
            string[] ids = TceComponentCatalogBuilder.Build()
                .Select(entry => entry.ComponentId)
                .ToArray();

            Assert.That(ids, Is.All.StartsWith("zeroengine.tce."));
            Assert.That(ids, Is.Unique);
            Assert.That(ids, Does.Contain("zeroengine.tce.trigger.on_install"));
            Assert.That(ids, Does.Contain("zeroengine.tce.condition.numeric_source"));
            Assert.That(ids, Does.Contain("zeroengine.tce.effect.debug_log"));
        }

        [Test]
        public void ComponentCatalog_IncludesSerializedFieldNamesAndDefaultValues()
        {
            TceComponentCatalogEntry cooldown = TceComponentCatalogBuilder.Build()
                .Single(entry => entry.DataType == typeof(CooldownConditionData));

            TceComponentCatalogField duration = cooldown.Fields.Single(field => field.Name == nameof(CooldownConditionData.Duration));

            Assert.AreEqual("System.Single", duration.TypeName);
            Assert.AreEqual("1", duration.DefaultValue);
        }

        [Test]
        public void ComponentCatalog_IncludesFieldDescriptions()
        {
            TceComponentCatalogEntry debugLog = TceComponentCatalogBuilder.Build()
                .Single(entry => entry.DataType == typeof(DebugLogEffectData));

            TceComponentCatalogField message = debugLog.Fields.Single(field => field.Name == nameof(DebugLogEffectData.Message));

            Assert.AreEqual("Log message emitted when the effect runs.", message.Description);
        }

        [Test]
        public void ComponentCatalog_MarkdownIncludesComponentIdsAndFieldDescriptions()
        {
            string markdown = TceComponentCatalogWriter.WriteMarkdown(TceComponentCatalogBuilder.Build());

            StringAssert.Contains("- Component ID: `zeroengine.tce.effect.debug_log`", markdown);
            StringAssert.Contains("- `Message` (`System.String`, default `\"\"`): Log message emitted when the effect runs.", markdown);
        }

        [Test]
        public void ComponentCatalog_CommittedMarkdownMatchesGeneratedOutput()
        {
            string expected = TceComponentCatalogWriter.WriteMarkdown(TceComponentCatalogBuilder.Build());
            string actual = File.ReadAllText(CatalogPath).Replace("\r\n", "\n");

            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void GraphSchema_IncludesExternalDocumentShapeAndComponentIds()
        {
            string schema = TceComponentCatalogWriter.WriteGraphJsonSchema(TceComponentCatalogBuilder.Build());

            StringAssert.Contains("\"format\"", schema);
            StringAssert.Contains($"\"format\": {{ \"const\": \"{TceGraphSchema.Format}\" }}", schema);
            StringAssert.Contains("\"schemaVersion\"", schema);
            StringAssert.Contains($"\"schemaVersion\": {{ \"const\": {TceGraphSchema.CurrentVersion} }}", schema);
            StringAssert.Contains("\"graphId\"", schema);
            StringAssert.Contains("\"componentId\"", schema);
            StringAssert.Contains("\"zeroengine.tce.trigger.on_install\"", schema);
            StringAssert.Contains("\"zeroengine.tce.effect.debug_log\"", schema);
        }

        [Test]
        public void GraphSchemaWriter_UsesGraphSchemaConstants()
        {
            string source = File.ReadAllText(CatalogWriterPath);

            StringAssert.DoesNotContain("\\\"format\\\": { \\\"const\\\": \\\"zeroengine-tce-graph\\\" }", source);
            StringAssert.DoesNotContain("\\\"schemaVersion\\\": { \\\"const\\\": 1 }", source);
        }

        [Test]
        public void GraphSchema_OutputIsDeterministic()
        {
            string first = TceComponentCatalogWriter.WriteGraphJsonSchema(TceComponentCatalogBuilder.Build());
            string second = TceComponentCatalogWriter.WriteGraphJsonSchema(TceComponentCatalogBuilder.Build());

            Assert.AreEqual(first, second);
        }

        [Test]
        public void GraphSchema_CommittedJsonMatchesGeneratedOutput()
        {
            string expected = TceComponentCatalogWriter.WriteGraphJsonSchema(TceComponentCatalogBuilder.Build());
            string actual = File.ReadAllText(TceComponentCatalogWriter.GraphSchemaPath).Replace("\r\n", "\n");

            Assert.AreEqual(expected, actual);
        }
    }
}
