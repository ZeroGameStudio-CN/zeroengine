using System.Collections.Generic;

namespace ZGS.DataToolkit.Editor
{
    public interface IDataToolkitValidationProvider
    {
        int Order { get; }
        IEnumerable<DataValidationIssue> Validate(DataToolkitValidationContext context);
    }
}
