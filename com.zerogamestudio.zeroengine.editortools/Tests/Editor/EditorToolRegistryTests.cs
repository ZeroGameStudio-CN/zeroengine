using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor.TestTools.TestRunner.Api;

namespace ZeroEngine.EditorTools.Tests
{
    [TestFixture]
    public sealed class EditorToolRegistryTests
    {
        [SetUp]
        public void SetUp()
        {
            EditorToolProjectRegistry.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            EditorToolProjectRegistry.ClearForTests();
        }

        [Test]
        public void Register_Profile_ReturnsSortedCommandsAndTasks()
        {
            var slowCommand = new EditorToolCommand("slow", "Slow", "Generators", 20, () => EditorToolExecutionResult.Success("slow"), "Slow tooltip", "生成器");
            var fastCommand = new EditorToolCommand("fast", "Fast", "Generators", 10, () => EditorToolExecutionResult.Success("fast"), "Fast tooltip", "生成器");
            var validationTask = new EditorToolCommandValidationTask("validate", "Validate", "Validation", 5, () => EditorToolExecutionResult.Warning("warn"), "Validate tooltip", "校验");
            var profile = new EditorToolProjectProfile(
                "P5",
                "P5 Editor Tools",
                "ZGS/Editor Tools",
                "Profile tooltip",
                commands: new IEditorToolCommand[] { slowCommand, fastCommand },
                validationTasks: new IValidationTask[] { validationTask });

            EditorToolProjectRegistry.Register(profile);

            Assert.That(EditorToolProjectRegistry.GetProfile("P5"), Is.SameAs(profile));
            Assert.That(profile.Description, Is.EqualTo("Profile tooltip"));
            Assert.That(EditorToolProjectRegistry.GetCommands("P5").Select(command => command.Id), Is.EqualTo(new[] { "fast", "slow" }));
            Assert.That(EditorToolProjectRegistry.GetValidationTasks("P5").Select(task => task.Id), Is.EqualTo(new[] { "validate" }));
            Assert.That(EditorToolProjectRegistry.GetCommands("P5").First().Tooltip, Is.EqualTo("Fast tooltip"));
            Assert.That(EditorToolProjectRegistry.GetCommands("P5").First().GroupDisplayName, Is.EqualTo("生成器"));
        }

        [Test]
        public void Register_DuplicateProfileId_Throws()
        {
            EditorToolProjectRegistry.Register(new EditorToolProjectProfile("P5", "P5 Editor Tools"));

            Assert.Throws<InvalidOperationException>(() => EditorToolProjectRegistry.Register(new EditorToolProjectProfile("P5", "Other")));
        }

        [Test]
        public void Register_DuplicateCommandIdInProfile_Throws()
        {
            var profile = new EditorToolProjectProfile(
                "P5",
                "P5 Editor Tools",
                commands: new IEditorToolCommand[]
                {
                    new EditorToolCommand("duplicate", "One", "Group", 0, () => EditorToolExecutionResult.Success()),
                    new EditorToolCommand("duplicate", "Two", "Group", 1, () => EditorToolExecutionResult.Success())
                });

            Assert.Throws<InvalidOperationException>(() => EditorToolProjectRegistry.Register(profile));
        }

        [Test]
        public void RefreshFromProviders_CanIncludeTestProvidersWhenRequested()
        {
            EditorToolProjectRegistry.ClearForTests();

            EditorToolProjectRegistry.RefreshFromProviders(includeTestProviders: true);

            var profile = EditorToolProjectRegistry.GetProfile("ZE_TEST");
            Assert.NotNull(profile);
            Assert.That(profile.Title, Is.EqualTo("测试工具"));
            Assert.That(profile.Commands.Select(command => command.Id), Does.Contain("ze-test.command"));
        }

        [Test]
        public void GetProfiles_WhenEmpty_DoesNotExposeTestOnlyProviders()
        {
            EditorToolProjectRegistry.ClearForTests();

            var profiles = EditorToolProjectRegistry.GetProfiles();

            Assert.That(profiles.Select(profile => profile.ProjectId), Does.Not.Contain("ZE_TEST"));
        }

        [Test]
        public void ExecutionResult_ExpressesSuccessWarningAndError()
        {
            Assert.That(EditorToolExecutionResult.Success("ok").Status, Is.EqualTo(EditorToolExecutionStatus.Success));
            Assert.That(EditorToolExecutionResult.Warning("warn").Status, Is.EqualTo(EditorToolExecutionStatus.Warning));
            Assert.That(EditorToolExecutionResult.Error("err").Status, Is.EqualTo(EditorToolExecutionStatus.Error));
            Assert.That(EditorToolExecutionResult.Error("err").Succeeded, Is.False);
        }

        [Test]
        public void TestRunnerTask_CreatesUnityFilterWithoutProjectSpecificAssemblyNames()
        {
            var task = new EditorToolTestRunnerTask(
                "fast-editmode",
                "Fast EditMode",
                EditorToolTestMode.EditMode,
                tooltip: "运行快速测试",
                groupDisplayName: "测试运行器",
                assemblyNames: new[] { "Example.Tests.EditMode" },
                categoryNames: new[] { "Unit", "Boundary" },
                groupNames: new[] { "^Example\\.Tests\\." });

            Filter filter = task.CreateFilter();

            Assert.That(task.Tooltip, Is.EqualTo("运行快速测试"));
            Assert.That(task.GroupDisplayName, Is.EqualTo("测试运行器"));
            Assert.That(filter.testMode, Is.EqualTo(TestMode.EditMode));
            Assert.That(filter.assemblyNames, Is.EqualTo(new[] { "Example.Tests.EditMode" }));
            Assert.That(filter.categoryNames, Is.EqualTo(new[] { "Unit", "Boundary" }));
            Assert.That(filter.groupNames, Is.EqualTo(new[] { "^Example\\.Tests\\." }));
        }

        private static class TestEditorToolProvider
        {
            [EditorToolProjectProvider(testOnly: true)]
            public static EditorToolProjectProfile CreateProfile()
            {
                return new EditorToolProjectProfile(
                    "ZE_TEST",
                    "测试工具",
                    "ZGS/Editor Tools",
                    "用于验证 provider 自恢复。",
                    commands: new[]
                    {
                        new EditorToolCommand("ze-test.command", "测试命令", "Test", 0, () => EditorToolExecutionResult.Success(), "测试 tooltip", "测试")
                    });
            }
        }
    }
}
