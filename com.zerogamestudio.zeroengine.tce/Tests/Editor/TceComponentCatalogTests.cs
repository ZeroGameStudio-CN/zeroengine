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
        public void ComponentCatalog_IncludesSerializedFieldNamesAndDefaultValues()
        {
            TceComponentCatalogEntry cooldown = TceComponentCatalogBuilder.Build()
                .Single(entry => entry.DataType == typeof(CooldownConditionData));

            TceComponentCatalogField duration = cooldown.Fields.Single(field => field.Name == nameof(CooldownConditionData.Duration));

            Assert.AreEqual("System.Single", duration.TypeName);
            Assert.AreEqual("1", duration.DefaultValue);
        }

        [Test]
        public void ComponentCatalog_CommittedMarkdownMatchesGeneratedOutput()
        {
            string expected = TceComponentCatalogWriter.WriteMarkdown(TceComponentCatalogBuilder.Build());
            string actual = File.ReadAllText(CatalogPath).Replace("\r\n", "\n");

            Assert.AreEqual(expected, actual);
        }
    }
}
