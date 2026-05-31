using System;

namespace ZGS.DataToolkit.Editor
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class DataAuthoringProviderAttribute : Attribute
    {
        public DataAuthoringProviderAttribute(bool testOnly = false)
        {
            TestOnly = testOnly;
        }

        public bool TestOnly { get; }
    }
}
