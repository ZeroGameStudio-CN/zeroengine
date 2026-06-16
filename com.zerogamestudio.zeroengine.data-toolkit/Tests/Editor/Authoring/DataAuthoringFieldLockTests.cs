using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ZGS.DataToolkit.Editor.Tests
{
    public sealed class DataAuthoringFieldLockTests
    {
        [Test]
        public void FieldLockUtility_BuildsAssignedValueDisableExpressions()
        {
            var utilityType = RequireType("ZGS.DataToolkit.Editor.DataAuthoringFieldLockUtility");
            var method = utilityType.GetMethod(
                "BuildAssignedValueDisableExpression",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(string), typeof(Type), typeof(bool) },
                modifiers: null);
            Assert.NotNull(method);

            Assert.AreEqual(
                "@!string.IsNullOrWhiteSpace(characterId)",
                method.Invoke(null, new object[] { "characterId", typeof(string), true }));
            Assert.AreEqual(
                "@portrait != null",
                method.Invoke(null, new object[] { "portrait", typeof(Sprite), true }));
            Assert.AreEqual(
                string.Empty,
                method.Invoke(null, new object[] { "level", typeof(int), true }));
            Assert.AreEqual(
                string.Empty,
                method.Invoke(null, new object[] { "characterId", typeof(string), false }));
        }

        [Test]
        public void FieldLockRegistry_ExposesProviderRegistrationForPackageHosts()
        {
            var registryType = RequireType("ZGS.DataToolkit.Editor.DataAuthoringFieldLockRegistry");
            var providerType = RequireType("ZGS.DataToolkit.Editor.IDataAuthoringFieldLockProvider");
            var lockedFieldType = RequireType("ZGS.DataToolkit.Editor.DataAuthoringLockedField");

            Assert.NotNull(registryType.GetMethod("Register", new[] { providerType }));
            Assert.NotNull(registryType.GetMethod("ClearForTests", BindingFlags.Public | BindingFlags.Static));
            Assert.NotNull(registryType.GetMethod(
                "TryGetLockedField",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { typeof(Type), typeof(string), lockedFieldType.MakeByRefType() },
                modifiers: null));
        }

        private static Type RequireType(string fullName)
        {
            var type = Type.GetType($"{fullName}, ZGS.DataToolkit.Editor");
            Assert.NotNull(type, fullName);
            return type;
        }
    }
}
