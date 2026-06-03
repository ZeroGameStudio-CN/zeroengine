using ZeroEngine.Formula;

namespace ZeroEngine.Formula.Editor
{
    public static class FormulaEditorLabels
    {
        public const string Formula = "公式";
        public const string InitialValue = "初始值";
        public const string Steps = "步骤";
        public const string Operation = "操作";
        public const string Source = "来源";
        public const string ConstantValue = "常量值";
        public const string Provider = "上下文变量";
        public const string NestedFormula = "嵌套公式";
        public const string Result = "结果";
        public const string PreviewResult = "预览结果";
        public const string Workbench = "公式工作台";
        public const string Diagnostics = "诊断";
        public const string StepTrace = "步骤追踪";
        public const string Evaluate = "预览计算";
        public const string PreviewInputs = "预览输入";
        public const string PreviewCases = "预览样例";
        public const string AddPreviewCase = "添加样例";
        public const string EvaluatePreviewCases = "批量预览";
        public const string PreviewReportJson = "JSON 报告";
        public const string PreviewReportMarkdown = "Markdown 报告";
        public const string CurvePreview = "曲线预览";
        public const string CurveInput = "曲线输入";
        public const string CurveRange = "曲线范围";
        public const string CurveSamples = "采样数";
        public const string BuildCurve = "生成曲线";
        public const string ResetPreviewInputs = "重置预览输入";
        public const string AddStep = "添加步骤";
        public const string RemoveStep = "删除步骤";
        public const string FormulaRoot = "公式根目录";
        public const string Catalog = "目录资产";
        public const string Refresh = "刷新";
        public const string Scan = "扫描";
        public const string GenerateMissingCatalogEntries = "生成缺失目录项";
        public const string Search = "搜索";
        public const string Filter = "筛选";
        public const string ScanSummary = "扫描摘要";
        public const string FormulaList = "公式列表";
        public const string References = "引用";
        public const string MissingCatalog = "缺目录";
        public const string NoCatalogPath = "<未配置>";
        public const string NoRows = "没有符合当前筛选条件的公式。";
        public const string NoDiagnostics = "无诊断。";
        public const string NoStepTrace = "无步骤追踪。";
        public const string PreviewNotRun = "尚未预览。";
        public const string PreviewSucceeded = "预览通过";
        public const string PreviewFailed = "预览失败";
        public const string Ping = "定位";
        public const string OpenWorkbench = "工作台";
        public const string All = "全部";

        public static string OperationName(FormulaOperationType operation)
        {
            switch (operation)
            {
                case FormulaOperationType.Add:
                    return "加";
                case FormulaOperationType.Subtract:
                    return "减";
                case FormulaOperationType.Multiply:
                    return "乘";
                case FormulaOperationType.Divide:
                    return "除";
                case FormulaOperationType.MultiplyFactor:
                    return "乘以系数";
                default:
                    return operation.ToString();
            }
        }

        public static string SourceTypeName(FormulaValueSourceType sourceType)
        {
            switch (sourceType)
            {
                case FormulaValueSourceType.Constant:
                    return "常量";
                case FormulaValueSourceType.Provider:
                    return "上下文变量";
                case FormulaValueSourceType.NestedFormula:
                    return "嵌套公式";
                default:
                    return sourceType.ToString();
            }
        }

        public static string FilterName(FormulaCatalogWindowFilter filter)
        {
            switch (filter)
            {
                case FormulaCatalogWindowFilter.Errors:
                    return "错误";
                case FormulaCatalogWindowFilter.Warnings:
                    return "警告";
                case FormulaCatalogWindowFilter.MissingCatalog:
                    return MissingCatalog;
                case FormulaCatalogWindowFilter.Unreferenced:
                    return "未引用";
                case FormulaCatalogWindowFilter.All:
                default:
                    return All;
            }
        }

        public static string CatalogStatusName(FormulaCatalogStatus status)
        {
            switch (status)
            {
                case FormulaCatalogStatus.Active:
                    return "生效";
                case FormulaCatalogStatus.Deprecated:
                    return "废弃";
                case FormulaCatalogStatus.Draft:
                default:
                    return "草稿";
            }
        }

        public static string ScanSeverityName(FormulaAssetScanSeverity severity)
        {
            switch (severity)
            {
                case FormulaAssetScanSeverity.Error:
                    return "错误";
                case FormulaAssetScanSeverity.Warning:
                    return "警告";
                case FormulaAssetScanSeverity.Info:
                default:
                    return "信息";
            }
        }

        public static string DiagnosticSeverityName(FormulaDiagnosticSeverity severity)
        {
            switch (severity)
            {
                case FormulaDiagnosticSeverity.Error:
                    return "错误";
                case FormulaDiagnosticSeverity.Warning:
                    return "警告";
                case FormulaDiagnosticSeverity.Info:
                default:
                    return "信息";
            }
        }

        public static string IssueSummary(int errorCount, int warningCount, int infoCount)
        {
            if (errorCount <= 0 && warningCount <= 0 && infoCount <= 0)
                return "无问题";

            var parts = new System.Collections.Generic.List<string>(3);
            if (errorCount > 0)
                parts.Add($"错误 {errorCount}");
            if (warningCount > 0)
                parts.Add($"警告 {warningCount}");
            if (infoCount > 0)
                parts.Add($"信息 {infoCount}");
            return string.Join(" / ", parts);
        }

        public static string EvaluationStatusName(FormulaEvaluationReport report)
        {
            if (report == null)
                return PreviewNotRun;

            return report.Succeeded && !report.HasErrors
                ? PreviewSucceeded
                : PreviewFailed;
        }
    }
}
