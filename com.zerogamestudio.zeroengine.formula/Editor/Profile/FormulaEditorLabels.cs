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
        public const string Succeeded = "是否成功";
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
    }
}
