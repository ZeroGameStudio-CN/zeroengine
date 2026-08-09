namespace ZeroEngine.Formula
{
    public readonly struct FormulaDiagnosticSink
    {
        private readonly FormulaEvaluationReport report;

        public FormulaDiagnosticSink(FormulaEvaluationReport report)
        {
            this.report = report;
        }

        public void Add(FormulaDiagnosticSeverity severity, FormulaDiagnosticCode code, string message)
        {
            report?.AddDiagnostic(severity, code, message);
        }
    }
}
