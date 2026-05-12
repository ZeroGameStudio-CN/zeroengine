using System;
using System.IO;
using NUnit.Framework;

namespace ZeroEngine.UI.Tests.Editor.Toast
{
    public sealed class ToastSourceTests
    {
        private static readonly string PackageRoot = Path.GetFullPath("Packages/com.zerogamestudio.zeroengine.ui");

        [Test]
        public void ToastRuntime_DoesNotReferenceProjectOrDialoguePackages()
        {
            var runtimeRoot = Path.Combine(PackageRoot, "Runtime", "UI", "Toast");
            foreach (var file in Directory.GetFiles(runtimeRoot, "*.cs", SearchOption.AllDirectories))
            {
                var source = File.ReadAllText(file);
                StringAssert.DoesNotContain("namespace POB", source);
                StringAssert.DoesNotContain("using POB", source);
                StringAssert.DoesNotContain("PixelCrushers", source);
                StringAssert.DoesNotContain("DialogueManager", source);
                StringAssert.DoesNotContain("ES3", source);
            }
        }

        [Test]
        public void ToastRequest_ExposesPluginGradeOptions()
        {
            var source = File.ReadAllText(Path.Combine(PackageRoot, "Runtime", "UI", "Toast", "ToastRequest.cs"));
            StringAssert.Contains("public string Message", source);
            StringAssert.Contains("public string TextKey", source);
            StringAssert.Contains("public string DedupeKey", source);
            StringAssert.Contains("public string GroupKey", source);
            StringAssert.Contains("public ToastSeverity Severity", source);
            StringAssert.Contains("public ToastPriority Priority", source);
            StringAssert.Contains("public ToastAnchor Anchor", source);
            StringAssert.Contains("public Action<ToastHandle> OnClick", source);
            StringAssert.Contains("public Action<ToastHandle> OnDismissed", source);
        }

        [Test]
        public void ToastDefines_ContainsStackAndQueuePolicies()
        {
            var source = File.ReadAllText(Path.Combine(PackageRoot, "Runtime", "UI", "Toast", "ToastDefines.cs"));
            StringAssert.Contains("public enum ToastOverflowPolicy", source);
            StringAssert.Contains("DropOldest", source);
            StringAssert.Contains("Queue", source);
            StringAssert.Contains("ReplaceLowestPriority", source);
            StringAssert.Contains("public enum ToastDuplicatePolicy", source);
            StringAssert.Contains("IgnoreDuplicate", source);
            StringAssert.Contains("RefreshExisting", source);
            StringAssert.Contains("StackDuplicate", source);
        }
    }
}
