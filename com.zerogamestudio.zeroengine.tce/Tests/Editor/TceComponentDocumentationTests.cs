using System;
using System.Linq;
using NUnit.Framework;
using ZeroEngine.TCE.Editor;

namespace ZeroEngine.TCE.Tests.Editor
{
    [TestFixture]
    public sealed class TceComponentDocumentationTests
    {
        [Test]
        public void ConcreteRuntimeComponentDataTypes_DeclareDocMetadata()
        {
            Type dataBaseType = typeof(TceComponentData);
            Type attributeType = typeof(TceComponentDocAttribute);

            string[] missing = typeof(TceComponentData).Assembly
                .GetTypes()
                .Where(type => !type.IsAbstract && dataBaseType.IsAssignableFrom(type))
                .Where(type => type.GetCustomAttributes(attributeType, false).Length == 0)
                .Select(type => type.FullName)
                .OrderBy(name => name)
                .ToArray();

            Assert.That(missing, Is.Empty, "Every concrete ZeroEngine TCE component data type must declare TceComponentDocAttribute.");
        }

        [Test]
        public void ConcreteRuntimeComponentDataTypes_DeclareStableComponentId()
        {
            string[] missing = typeof(TceComponentData).Assembly
                .GetTypes()
                .Where(type => !type.IsAbstract && typeof(TceComponentData).IsAssignableFrom(type))
                .Where(type =>
                {
                    var doc = (TceComponentDocAttribute)type.GetCustomAttributes(typeof(TceComponentDocAttribute), false).SingleOrDefault();
                    return doc == null || string.IsNullOrWhiteSpace(doc.ComponentId);
                })
                .Select(type => type.FullName)
                .OrderBy(name => name)
                .ToArray();

            Assert.That(missing, Is.Empty, "Every concrete TCE component data type must declare a stable component ID.");
        }

        [Test]
        public void CatalogFields_DeclareFieldDocMetadata()
        {
            string[] missing = TceComponentCatalogBuilder.Build()
                .SelectMany(entry => entry.Fields
                    .Where(field => string.IsNullOrWhiteSpace(field.Description))
                    .Select(field => $"{entry.DataTypeFullName}.{field.Name}"))
                .OrderBy(name => name)
                .ToArray();

            Assert.That(missing, Is.Empty, "Every catalog-visible TCE component field must declare TceFieldDocAttribute.");
        }
    }
}
