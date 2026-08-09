using System;
using System.Collections.Generic;

namespace ZeroEngine.Formula
{
    public enum FormulaDiagnosticSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2,
    }

    public enum FormulaDiagnosticCode
    {
        None = 0,
        NullFormula = 1,
        CircularReference = 2,
        DivideByZero = 3,
        MissingContext = 4,
        InvalidProvider = 5,
        InvalidOperation = 6,
        NonFiniteResult = 7,
        ProviderException = 8,
        InvalidParameter = 9,
        MissingRandomSource = 10,
        InvalidRandomRange = 11,
        RandomSourceException = 12,
    }

    [Serializable]
    public sealed class FormulaDiagnostic
    {
        public FormulaDiagnostic(FormulaDiagnosticSeverity severity, FormulaDiagnosticCode code, string message)
        {
            Severity = severity;
            Code = code;
            Message = message;
        }

        public FormulaDiagnosticSeverity Severity { get; }
        public FormulaDiagnosticCode Code { get; }
        public string Message { get; }

        public override string ToString() => $"[{Severity}] {Code}: {Message}";
    }

    [Serializable]
    public sealed class FormulaStepEvaluation
    {
        public FormulaStepEvaluation(
            int stepIndex,
            FormulaOperationType operation,
            FormulaValueSourceType sourceType,
            string sourceLabel,
            float inputValue,
            float stepValue,
            float outputValue)
        {
            StepIndex = stepIndex;
            Operation = operation;
            SourceType = sourceType;
            SourceLabel = sourceLabel;
            InputValue = inputValue;
            StepValue = stepValue;
            OutputValue = outputValue;
        }

        public int StepIndex { get; }
        public FormulaOperationType Operation { get; }
        public FormulaValueSourceType SourceType { get; }
        public string SourceLabel { get; }
        public float InputValue { get; }
        public float StepValue { get; }
        public float OutputValue { get; }
    }

    [Serializable]
    public sealed class FormulaEvaluationReport
    {
        private readonly List<FormulaDiagnostic> diagnostics = new();
        private readonly List<FormulaStepEvaluation> steps = new();
        private readonly List<FormulaEvaluationReport> childReports = new();

        public FormulaEvaluationReport(UnityEngine.Object formulaObject, string formulaName)
        {
            FormulaObject = formulaObject;
            FormulaName = string.IsNullOrEmpty(formulaName) ? "<formula>" : formulaName;
        }

        public UnityEngine.Object FormulaObject { get; }
        public string FormulaName { get; }
        public float Result { get; private set; }
        public bool Succeeded { get; private set; } = true;
        public IReadOnlyList<FormulaDiagnostic> Diagnostics => diagnostics;
        public IReadOnlyList<FormulaStepEvaluation> Steps => steps;
        public IReadOnlyList<FormulaEvaluationReport> ChildReports => childReports;
        public bool HasErrors => diagnostics.Exists(d => d.Severity == FormulaDiagnosticSeverity.Error);
        public bool HasWarnings => diagnostics.Exists(d => d.Severity == FormulaDiagnosticSeverity.Warning);

        public void SetResult(float result, bool succeeded)
        {
            Result = result;
            Succeeded = succeeded;
        }

        public void AddDiagnostic(FormulaDiagnosticSeverity severity, FormulaDiagnosticCode code, string message)
        {
            diagnostics.Add(new FormulaDiagnostic(severity, code, message));
        }

        public void AddStep(FormulaStepEvaluation step)
        {
            if (step != null)
                steps.Add(step);
        }

        public void AddChildReport(FormulaEvaluationReport report)
        {
            if (report != null)
                childReports.Add(report);
        }
    }
}
