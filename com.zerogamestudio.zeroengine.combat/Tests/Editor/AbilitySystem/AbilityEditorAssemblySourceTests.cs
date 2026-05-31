using System.IO;
using NUnit.Framework;

namespace ZeroEngine.AbilitySystem.Editor.Tests
{
    public sealed class AbilityEditorAssemblySourceTests
    {
        private const string AssemblyPath =
            "Editor/AbilitySystem/ZeroEngine.Combat.Editor.asmdef";

        [Test]
        public void CombatEditorAssembly_IsEditorOnlyAndReferencesRuntimeCombat()
        {
            var source = File.ReadAllText(AbilityEditorTestPaths.PackageFile(AssemblyPath));

            StringAssert.Contains("\"name\": \"ZeroEngine.Combat.Editor\"", source);
            StringAssert.Contains("\"includePlatforms\"", source);
            StringAssert.Contains("\"Editor\"", source);
            StringAssert.Contains("\"ZeroEngine.Combat\"", source);
        }
    }
}
