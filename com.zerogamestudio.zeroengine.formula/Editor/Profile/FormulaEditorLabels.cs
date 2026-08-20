using ZeroEngine.Formula;

namespace ZeroEngine.Formula.Editor
{
    public static class FormulaEditorLabels
    {
        public const string Formula = "公式";
        public const string FormulaTooltip = "选择要预览计算的公式资源。";
        public const string InitialValue = "初始值";
        public const string Steps = "步骤";
        public const string Operation = "操作";
        public const string Source = "来源";
        public const string ConstantValue = "常量值";
        public const string Provider = "上下文变量";
        public const string NestedFormula = "嵌套公式";
        public const string RandomMinInclusive = "随机最小值（含）";
        public const string RandomMaxInclusive = "随机最大值（含）";
        public const string Result = "结果";
        public const string PreviewResult = "预览结果";
        public const string Workbench = "公式工作台";
        public const string WorkbenchTooltip = "配置一个场景，并在每张公式卡片中计算和查看步骤。";
        public const string Studio = "公式中心";
        public const string StudioTooltip = "在同一个窗口中浏览公式目录并进行预览计算。";
        public const string CatalogPage = "公式目录";
        public const string CatalogPageTooltip = "浏览、筛选并检查已登记的公式资源。";
        public const string Diagnostics = "诊断";
        public const string StepTrace = "步骤追踪";
        public const string Evaluate = "预览计算";
        public const string EvaluateTooltip = "使用当前 Profile 和预览输入计算所选公式。";
        public const string PreviewInputs = "当前场景输入";
        public const string Preview = "预览";
        public const string PreviewWorkspace = "公式预览";
        public const string PreviewWorkspaceTooltip = "先配置场景，再逐个计算公式；结果和步骤保留在公式卡片中。";
        public const string PreviewFormulas = "公式";
        public const string PreviewFormulasTooltip = "每张卡片独立选择、计算并显示结果。";
        public const string PreviewSetup = "输入与运行";
        public const string PreviewCases = "场景";
        public const string PreviewCaseTooltip = "选择一个批量预览样例资源。";
        public const string AddPreviewCase = "添加样例";
        public const string AddPreviewCaseTooltip = "添加一个批量预览样例资源槽。";
        public const string RemovePreviewCaseTooltip = "移除当前批量预览样例资源槽。";
        public const string EvaluatePreviewCases = "计算此公式";
        public const string EvaluatePreviewCasesTooltip = "使用当前选中的场景计算这一条公式。";
        public const string AddFormula = "添加公式";
        public const string AddFormulaTooltip = "添加一个公式资源，用同一组输入进行并列预览。";
        public const string RemoveFormulaTooltip = "移除当前公式。";
        public const string PreviewResults = "预览结果";
        public const string PreviewCaseResults = "场景结果";
        public const string PreviewResultSummary = "按公式显示当前输入和场景的可读结果。";
        public const string PreviewResultDetails = "当前公式详情";
        public const string PreviewReportJson = "JSON 报告";
        public const string PreviewReportMarkdown = "Markdown 报告";
        public const string Scenarios = "场景";
        public const string Scenario = "场景";
        public const string ScenarioTooltip = "选择本次计算使用的当前输入、项目内置场景或本机保存场景。";
        public const string CurrentScenario = "当前输入（可编辑）";
        public const string BuiltInScenarioPrefix = "内置 · ";
        public const string SavedScenarioPrefix = "已保存 · ";
        public const string ScenarioName = "场景名称";
        public const string ScenarioNameTooltip = "为当前输入命名后保存到本机工作台；不会创建项目资源。";
        public const string SaveScenario = "保存当前场景";
        public const string SaveScenarioTooltip = "保存当前输入，供以后和内置场景一起预览。";
        public const string DeleteScenario = "删除本地场景";
        public const string DeleteScenarioTooltip = "仅删除本机工作台中保存的这个场景。";
        public const string RelatedFields = "相关字段";
        public const string RelatedFieldsTooltip = "只显示当前所选公式实际读取的场景字段。";
        public const string AllFields = "全部字段";
        public const string AllFieldsTooltip = "显示 Profile 的全部普通输入，以及当前公式引用的带参数输入。";
        public const string NoRelatedFields = "当前公式只使用常量、随机值或尚未选择公式，无需配置场景字段。";
        public const string GeneralCategory = "通用";
        public const string PendingCalculation = "尚未计算。点击“计算此公式”后，这里会显示结果和计算过程。";
        public const string CalculationResult = "计算结果";
        public const string CalculationSteps = "计算过程";
        public const string StepInput = "输入";
        public const string StepOutput = "输出";
        public const string TrendAnalysis = "趋势分析（按需）";
        public const string TrendAnalysisTooltip = "沿一个输入区间采样公式结果；默认折叠，只有展开并生成时才计算。";
        public const string CurvePreview = "曲线预览";
        public const string CurveInput = "曲线输入";
        public const string CurveInputTooltip = "选择用于横轴采样的预览输入。";
        public const string CurveRange = "曲线范围";
        public const string CurveRangeTooltip = "设置曲线预览的最小值和最大值。";
        public const string CurveSamples = "采样数";
        public const string CurveSamplesTooltip = "设置曲线范围内的采样点数量。";
        public const string BuildCurve = "生成曲线";
        public const string BuildCurveTooltip = "按当前输入、范围和采样数生成公式结果曲线。";
        public const string ResetPreviewInputs = "重置当前场景";
        public const string ResetPreviewInputsTooltip = "将当前场景输入恢复为项目默认值。";
        public const string AddStep = "添加步骤";
        public const string RemoveStep = "删除步骤";
        public const string FormulaRoot = "公式根目录";
        public const string Catalog = "目录资产";
        public const string Search = "搜索";
        public const string SearchTooltip = "按公式名称、路径、用途、单位、标签或诊断内容筛选。";
        public const string Filter = "筛选";
        public const string FilterTooltip = "按错误、警告、缺目录或未引用状态筛选公式。";
        public const string ScanSummary = "目录概况";
        public const string FormulaList = "公式列表";
        public const string References = "引用";
        public const string MissingCatalog = "资料待补";
        public const string NoCatalogPath = "<未配置>";
        public const string NoRows = "没有符合当前筛选条件的公式。";
        public const string NoDiagnostics = "无诊断。";
        public const string NoStepTrace = "无步骤追踪。";
        public const string PreviewNotRun = "尚未预览。";
        public const string PreviewSucceeded = "预览通过";
        public const string PreviewFailed = "预览失败";
        public const string FormulaCardTooltip = "单击在 Project 中选中公式；双击进入公式工作台。";
        public const string OpenWorkbench = "工作台";
        public const string OpenWorkbenchTooltip = "在当前公式中心切换到工作台并选中该公式。";
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
                case FormulaValueSourceType.RandomInteger:
                    return "随机整数";
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
                    return "提醒";
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
                    return "提醒";
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
                parts.Add($"提醒 {warningCount}");
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
