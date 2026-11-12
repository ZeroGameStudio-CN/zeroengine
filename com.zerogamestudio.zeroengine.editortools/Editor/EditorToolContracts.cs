using UnityEditor.TestTools.TestRunner.Api;

namespace ZeroEngine.EditorTools
{
    public interface IEditorToolCommand
    {
        string Id { get; }

        string DisplayName { get; }

        string Tooltip { get; }

        string Group { get; }

        string GroupDisplayName { get; }

        int Order { get; }

        EditorToolExecutionResult Execute();
    }

    public interface IEditorToolPanel
    {
        string Id { get; }

        string DisplayName { get; }

        string Tooltip { get; }

        string Group { get; }

        string GroupDisplayName { get; }

        int Order { get; }

        void Draw();
    }

    public interface IAssetGenerationTask : IEditorToolCommand
    {
    }

    public interface IValidationTask : IEditorToolCommand
    {
    }

    public interface ITestRunnerTask
    {
        string Id { get; }

        string DisplayName { get; }

        string Tooltip { get; }

        string Group { get; }

        string GroupDisplayName { get; }

        int Order { get; }

        Filter CreateFilter();
    }
}
