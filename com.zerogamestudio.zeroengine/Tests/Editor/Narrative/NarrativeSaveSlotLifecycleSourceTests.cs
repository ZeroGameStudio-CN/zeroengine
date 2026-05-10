using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Tests.Narrative
{
    [TestFixture]
    public class NarrativeSaveSlotLifecycleSourceTests
    {
        [TestCase("Runtime/Quest/QuestManager.cs", "OnDestroy")]
        [TestCase("Runtime/Dialog/DialogSaveAdapter.cs", "OnDestroy")]
        [TestCase("Runtime/Achievement/AchievementManager.cs", "Unregister")]
        public void TeardownDoesNotResolveSaveSlotManagerSingleton(string relativePath, string methodName)
        {
            var source = File.ReadAllText(FindNarrativeSourcePath(relativePath));
            var body = ExtractMethodBody(source, methodName);

            StringAssert.DoesNotContain("SaveSlotManager.Instance", body);
        }

        private static string FindNarrativeSourcePath(string relativePath)
        {
            var candidates = new[]
            {
                Path.Combine(Application.dataPath, "..", "Packages", "com.zerogamestudio.zeroengine.narrative", relativePath),
                Path.Combine(Application.dataPath, "..", "..", "com.zerogamestudio.zeroengine.narrative", relativePath),
                Path.Combine(Directory.GetCurrentDirectory(), "com.zerogamestudio.zeroengine.narrative", relativePath)
            };

            foreach (var candidate in candidates)
            {
                var fullPath = Path.GetFullPath(candidate);
                if (File.Exists(fullPath))
                    return fullPath;
            }

            Assert.Fail($"Could not locate narrative source file: {relativePath}");
            return null;
        }

        private static string ExtractMethodBody(string source, string methodName)
        {
            var match = Regex.Match(source, $@"\b{Regex.Escape(methodName)}\s*\([^)]*\)\s*\{{");
            Assert.IsTrue(match.Success, $"Could not locate method: {methodName}");

            var start = match.Index + match.Length - 1;
            var depth = 0;
            for (var i = start; i < source.Length; i++)
            {
                if (source[i] == '{')
                {
                    depth++;
                }
                else if (source[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return source.Substring(start, i - start + 1);
                }
            }

            Assert.Fail($"Could not parse method body: {methodName}");
            return null;
        }
    }
}
