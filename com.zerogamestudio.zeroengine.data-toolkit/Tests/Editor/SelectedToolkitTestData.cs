using UnityEngine;

namespace ZGS.DataToolkit.Editor.Tests
{
    [ManageableData]
    public sealed class SelectedToolkitTestData : ScriptableObject
    {
    }

    [ZGS.DataToolkit.ManageableData]
    public sealed class ToolkitAttributedTestData : ScriptableObject
    {
    }

    public abstract class AbstractToolkitTestData : ScriptableObject
    {
    }

    public sealed class GenericToolkitTestData<T> : ScriptableObject
    {
    }

    public sealed class ToolkitHostTestData : ScriptableObject
    {
    }
}
