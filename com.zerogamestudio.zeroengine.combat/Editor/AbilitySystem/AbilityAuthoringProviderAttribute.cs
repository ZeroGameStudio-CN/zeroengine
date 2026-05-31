using System;

namespace ZeroEngine.AbilitySystem.Editor
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class AbilityAuthoringProviderAttribute : Attribute
    {
        public AbilityAuthoringProviderAttribute(bool testOnly = false)
        {
            TestOnly = testOnly;
        }

        public bool TestOnly { get; }
    }
}
