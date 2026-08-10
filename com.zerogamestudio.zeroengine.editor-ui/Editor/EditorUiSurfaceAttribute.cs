using System;

namespace ZeroEngine.EditorUI
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class EditorUiSurfaceAttribute : Attribute
    {
        public EditorUiSurfaceAttribute(int contractVersion = 1)
        {
            ContractVersion = contractVersion;
        }

        public int ContractVersion { get; }
    }
}
