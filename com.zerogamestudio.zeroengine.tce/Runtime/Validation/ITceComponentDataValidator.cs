using System.Collections.Generic;

namespace ZeroEngine.TCE
{
    public interface ITceComponentDataValidator
    {
        void Validate(TceComponentValidationContext context, List<TceValidationIssue> issues);
    }

    public readonly struct TceComponentValidationContext
    {
        public TceComponentValidationContext(TceComponentData data, string path)
        {
            Data = data;
            Path = path ?? string.Empty;
        }

        public TceComponentData Data { get; }
        public string Path { get; }
    }
}
