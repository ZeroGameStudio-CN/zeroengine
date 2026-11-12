using System;

namespace ZeroEngine.EditorTools
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class EditorToolProjectProviderAttribute : Attribute
    {
        public EditorToolProjectProviderAttribute(bool testOnly = false)
        {
            TestOnly = testOnly;
        }

        public bool TestOnly { get; }
    }
}
