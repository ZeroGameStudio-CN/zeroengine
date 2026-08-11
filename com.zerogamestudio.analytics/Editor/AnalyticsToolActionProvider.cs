using ZeroEngine.EditorUI;

namespace ZGS.Analytics.Editor
{
    [EditorToolActionProvider("zeroengine.analytics")]
    public sealed class AnalyticsToolActionProvider : IEditorToolActionProvider
    {
        public IEditorToolAction CreateAction(string actionId)
        {
            if (actionId != "analytics-dashboard")
                return null;
            return new DelegateEditorToolAction(context =>
            {
                AnalyticsDashboardWindow.ShowWindow();
                return new EditorToolActionResult(EditorToolActionStatus.Succeeded, "已打开数据分析仪表盘。");
            });
        }
    }
}
