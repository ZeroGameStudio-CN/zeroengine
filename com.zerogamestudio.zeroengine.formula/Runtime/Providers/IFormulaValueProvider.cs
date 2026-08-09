namespace ZeroEngine.Formula
{
    public interface IFormulaValueProvider
    {
        string Id { get; }

        bool TryGetValue(
            FormulaProviderRequest request,
            IFormulaEvaluationContext context,
            out float value,
            FormulaDiagnosticSink diagnostics);
    }
}
