using System;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    public readonly struct DataAuthoringPreviewContext
    {
        public DataAuthoringPreviewContext(
            DataAuthoringProfile profile,
            IDataAuthoringAssetAdapter adapter,
            DataAuthoringAssetRecord record,
            IReadOnlyList<DataAuthoringIssue> issues)
        {
            Profile = profile;
            Adapter = adapter;
            Record = record;
            Issues = issues ?? Array.Empty<DataAuthoringIssue>();
        }

        public DataAuthoringProfile Profile { get; }
        public IDataAuthoringAssetAdapter Adapter { get; }
        public DataAuthoringAssetRecord Record { get; }
        public IReadOnlyList<DataAuthoringIssue> Issues { get; }
        public Object Asset => Record?.Asset;
    }
}
