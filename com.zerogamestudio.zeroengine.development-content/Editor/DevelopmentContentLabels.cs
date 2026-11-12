using System.Linq;

namespace ZeroEngine.DevelopmentContent.Editor
{
    internal static class DevelopmentContentLabels
    {
        public static readonly string[] Stages = { "待确认", "开发中", "可试用", "就绪", "暂缓", "废弃" };
        public static readonly string[] Availability = { "待核对", "仅开发环境", "正式默认关闭", "仅测试范围", "用户已开放", "内部工具" };
        public static string Stage(DevelopmentContentRecord record) => record == null ? "待登记" : Stages[(int)record.Stage];
        public static string Scope(DevelopmentContentRecord record) => record == null ? "待核对" : Availability[(int)record.Availability];
        public static string Group(DevelopmentContentRow row) => row.Source?.Group ??
            (row.Record.Availability == ContentAvailability.InternalOnly ? "内部工具" : "独立登记");
        public static string Target(DevelopmentContentPlan plan, DevelopmentContentRecord record)
        {
            if (record == null) return "尚未登记";
            if (record.Availability == ContentAvailability.InternalOnly) return "内部使用";
            if (string.IsNullOrEmpty(record.TargetReleaseId)) return "待排期";
            if (record.TargetReleaseId == DevelopmentContentPlan.LaterReleaseId) return "后续版本（未定）";
            return plan.Releases.First(release => release.Id == record.TargetReleaseId).Name;
        }
    }
}
