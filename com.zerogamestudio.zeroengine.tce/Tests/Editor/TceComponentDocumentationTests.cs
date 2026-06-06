using System;
using System.Linq;
using NUnit.Framework;

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
    }
}
