using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    public interface IDataAuthoringAssetAdapter
    {
        string GroupId { get; }
        string DisplayName { get; }
        int Order { get; }
        IReadOnlyList<DataAuthoringAssetRecord> GetAssets();
        Object CreateAsset();
        Object DuplicateAsset(Object source);
        void DrawInspector(Object asset);
        IReadOnlyList<DataAuthoringIssue> Validate(Object asset);
        void AddExportSheets(TabularWorkbook workbook);
    }
}
