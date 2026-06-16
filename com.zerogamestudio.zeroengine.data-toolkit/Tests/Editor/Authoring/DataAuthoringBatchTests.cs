using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;

namespace ZGS.DataToolkit.Editor.Tests
{
    public sealed class DataAuthoringBatchTests
    {
        [Test]
        public void PreviewResult_BlocksApplyWhenBlockingIssuesExist()
        {
            var changeType = RequireType("ZGS.DataToolkit.Editor.DataAuthoringBatchChange");
            var previewType = RequireType("ZGS.DataToolkit.Editor.DataAuthoringBatchPreviewResult");
            var change = CreateChange(changeType, "name\tfield", "old\r\nvalue", "new");
            var issue = DataAuthoringIssue.Error("Assets/Hero.asset", "Character", "char.hero", "name", "blocked");

            var preview = Activator.CreateInstance(
                previewType,
                CreateTypedArray(changeType, change),
                new[] { issue });

            Assert.False(GetProperty<bool>(preview, "CanApply"));
        }

        [Test]
        public void ReportExporter_WritesPreviewAndApplyRowsWithSanitizedCells()
        {
            var changeType = RequireType("ZGS.DataToolkit.Editor.DataAuthoringBatchChange");
            var previewType = RequireType("ZGS.DataToolkit.Editor.DataAuthoringBatchPreviewResult");
            var applyType = RequireType("ZGS.DataToolkit.Editor.DataAuthoringBatchApplyResult");
            var labelsType = RequireType("ZGS.DataToolkit.Editor.DataAuthoringBatchReportLabels");
            var exporterType = RequireType("ZGS.DataToolkit.Editor.DataAuthoringBatchReportExporter");
            var change = CreateChange(changeType, "name\tfield", "old\r\nvalue", "new\tvalue");
            var issue = DataAuthoringIssue.Error("Assets/Hero.asset", "Character", "char.hero", "name", "blocked\nissue");
            var labels = Activator.CreateInstance(labelsType, "预览", "阻断", "已应用", "已跳过", "跳过原因");
            var preview = Activator.CreateInstance(
                previewType,
                CreateTypedArray(changeType, change),
                new[] { issue });
            var apply = Activator.CreateInstance(
                applyType,
                CreateTypedArray(changeType, change),
                CreateTypedArray(changeType),
                Array.Empty<DataAuthoringIssue>());

            var previewReport = InvokeReport(exporterType, "CreateTsvReport", previewType, preview, labelsType, labels);
            var applyReport = InvokeReport(exporterType, "CreateTsvReport", applyType, apply, labelsType, labels);

            StringAssert.Contains("rowType\tgroup\tassetPath\tstableId\tfieldPath\toldValue\tnewValue\tstatus\tmessage", previewReport);
            StringAssert.Contains("change\t角色\tAssets/Hero.asset\tchar.hero\tname field\told  value\tnew value\t预览\t", previewReport);
            StringAssert.Contains("blockingIssue\tCharacter\tAssets/Hero.asset\tchar.hero\tname\t\t\t阻断\tblocked issue", previewReport);
            StringAssert.Contains("appliedChange\t角色\tAssets/Hero.asset\tchar.hero\tname field\told  value\tnew value\t已应用\t", applyReport);
        }

        [Test]
        public void BatchWindowHost_RefreshesAppliesAndCreatesReports()
        {
            var change = new DataAuthoringBatchChange(
                null,
                "Test.FillName",
                "角色",
                "Assets/Hero.asset",
                "char.hero",
                "name",
                string.Empty,
                "char.hero");
            var preview = new DataAuthoringBatchPreviewResult(
                new[] { change },
                Array.Empty<DataAuthoringIssue>());
            var options = DataAuthoringBatchWindowOptions.CreateDefault(
                "批处理预览",
                "应用显示名补齐");
            var host = new DataAuthoringBatchWindowHost(
                options,
                () => preview,
                currentPreview => new DataAuthoringBatchApplyResult(
                    currentPreview.Changes,
                    Array.Empty<DataAuthoringBatchChange>(),
                    Array.Empty<DataAuthoringIssue>()));

            host.RefreshPreview();
            var applyResult = host.ApplyPreview();

            Assert.True(host.Preview.CanApply);
            Assert.AreEqual(1, applyResult.AppliedChanges.Count);
            StringAssert.Contains("change\t角色\tAssets/Hero.asset", host.CreatePreviewReport());
            StringAssert.Contains("appliedChange\t角色\tAssets/Hero.asset", host.CreateApplyReport());
        }

        private static Type RequireType(string fullName)
        {
            var type = Type.GetType($"{fullName}, ZGS.DataToolkit.Editor");
            Assert.NotNull(type, fullName);
            return type;
        }

        private static object CreateChange(Type changeType, string fieldPath, string oldValue, string newValue)
        {
            return Activator.CreateInstance(
                changeType,
                null,
                "Test.FillName",
                "角色",
                "Assets/Hero.asset",
                "char.hero",
                fieldPath,
                oldValue,
                newValue);
        }

        private static Array CreateTypedArray(Type elementType, params object[] values)
        {
            var array = Array.CreateInstance(elementType, values.Length);
            for (var i = 0; i < values.Length; i++)
            {
                array.SetValue(values[i], i);
            }

            return array;
        }

        private static T GetProperty<T>(object instance, string propertyName)
        {
            var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(property, propertyName);
            return (T)property.GetValue(instance);
        }

        private static string InvokeReport(
            Type exporterType,
            string methodName,
            Type resultType,
            object result,
            Type labelsType,
            object labels)
        {
            var method = exporterType.GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: new[] { resultType, labelsType },
                modifiers: null);
            Assert.NotNull(method, methodName);
            return (string)method.Invoke(null, new[] { result, labels });
        }
    }
}
