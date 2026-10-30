using System;
using UnityEditor;
using UnityEngine;
using ZeroEngine.EditorUI;

namespace ZeroEngine.Feedback.Editor
{
    [EditorWorkspacePanelProvider("zeroengine.feedback.help")]
    public sealed class FeedbackTroubleshootingProvider : IEditorWorkspacePanelProvider
    {
        public IEditorWorkspacePanel CreatePanel(string panelId) =>
            panelId == "failure-codes" ? new FeedbackTroubleshootingPanel() : null;
    }

    internal sealed class FeedbackTroubleshootingPanel : IEditorWorkspacePanel
    {
        private string _search = string.Empty;

        private static readonly string[][] Entries =
        {
            new[] { "F001", "请求无效", "填写反馈描述；接入方检查请求是否为空。" },
            new[] { "F002", "上传配置未就绪", "接入方检查上传地址与认证配置。" },
            new[] { "F101", "反馈 ZIP 打包失败", "检查临时目录权限、剩余空间和打包日志。" },
            new[] { "F102", "上传队列保存失败", "检查持久目录权限、空间和队列日志；不能视为已安全入队。" },
            new[] { "F103", "ZIP 缺失、读取或校验失败", "检查文件是否被清理、占用或损坏；原文件缺失时需重新提交。" },
            new[] { "F201", "网络连接失败", "检查联网、DNS、代理和上传服务可达性；已入队项会后台重试。" },
            new[] { "F202", "上传超时", "检查网络质量与服务负载，等待后台重试。" },
            new[] { "F203", "证书 / TLS 连接错误", "先检查玩家设备日期与时间，再换用可信网络对比，检查代理或安全软件是否拦截 HTTPS。维护人员检查上传域名的证书有效期、域名匹配、完整证书链及 TLS 握手。不要关闭证书校验。仅凭 F203 不能确定具体根因；已入队项保留并进入 6 小时冷却后复查。\n\n收集信息：报错时间与时区、平台及系统版本、实际包版本或构建编号、来源分支，以及 Player.log 中 F203 前后的脱敏日志。分支当前代码不等于玩家手里的包。\n\n判断方向：\n• 同一包换网络后正常：优先查网络、代理或 HTTPS 拦截。\n• 同一设备、同一网络及相同构建设置下，仅某分支包可重复失败：对比上传配置、SDK 与构建差异。\n• 多人、多网络都失败：优先查服务端证书链及 TLS，再排查客户端共有问题。\n\n拉到最新不代表 F203 已修复。本机访问正常也不能替代玩家环境验证。先定位原因：服务端问题修服务；确认客户端兼容问题后再修 SDK，并用原失败环境回归。" },
            new[] { "F204", "响应数据处理失败", "检查客户端处理错误、服务端响应和中间代理；已入队项会后台重试。" },
            new[] { "F299", "未分类错误", "保留同一提交编号的诊断日志，结合原因类别定位；已入队项会后台重试。" },
            new[] { "F400", "请求格式错误", "接入方检查请求格式和接口约定。" },
            new[] { "F401", "未授权", "维护人员检查上传认证配置。" },
            new[] { "F403", "访问被拒绝", "维护人员结合请求编号检查鉴权、权限和网关策略。" },
            new[] { "F404", "上传接口不存在", "检查上传地址和路由。" },
            new[] { "F408", "服务端请求超时", "等待后台重试；维护人员检查服务端耗时。" },
            new[] { "F413", "上传内容过大", "检查 ZIP 大小、附件预算与服务端限制。" },
            new[] { "F425", "服务端要求稍后重试", "等待后台重试。" },
            new[] { "F429", "请求过多 / 限流", "等待退避重试，避免连续重复提交。" },
            new[] { "F500–F599（如 F502 / F503 / F504）", "服务端或网关错误", "等待后台重试；持续发生时由维护人员排查服务端。" }
        };

        public float RefreshInterval => 0f;
        public void Activate(EditorWorkspacePanelContext context) { }
        public void Deactivate() { }
        public void Tick(EditorWorkspacePanelContext context, double timeSinceStartup) { }
        public void Dispose() { }

        public void OnGUI(EditorWorkspacePanelContext context)
        {
            EditorGUILayout.HelpBox("这里解释反馈上传报错，不是游戏 Bug 分类。失败提示表示一次尝试失败；本地入队也不等于服务器收到。", MessageType.Info);
            _search = EditorGUILayout.TextField(new GUIContent("查报错码", "输入 F203、203 或关键词；留空查看全部。"), _search);
            string query = _search.Trim();
            bool found = false;
            foreach (string[] entry in Entries)
            {
                bool serverError = TryGetHttpStatus(query, out int status) && status >= 500 && status <= 599;
                if (query.Length > 0 && !Array.Exists(entry, value => value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) &&
                    !(serverError && entry[0].StartsWith("F500", StringComparison.Ordinal)))
                    continue;
                found = true;
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(entry[0] + "  " + entry[1], EditorStyles.wordWrappedLabel);
                    GUILayout.Label(entry[2], EditorStyles.wordWrappedLabel);
                }
            }
            if (!found)
                EditorGUILayout.HelpBox("没有匹配项。HTTP 400–599 显示为 F 加实际状态码；请结合服务端响应和原因类别排查。", MessageType.Info);
            EditorGUILayout.Space();
            GUILayout.Label("重试与送达", EditorStyles.boldLabel);
            GUILayout.Label("F201 / F202 / F204 / F299、HTTP 408 / 425 / 429 和全部 5xx 会退避重试。其他 HTTP 4xx、F203 / F002 / F103 的已入队项进入 6 小时冷却；ZIP 缺失时移除失效记录。提交阶段的 F001 / F002 / F101 / F102 需修正原因后重新提交。", EditorStyles.wordWrappedLabel);
            GUILayout.Label("排查日志时按 submission 对应同一条反馈，结合 code、http、request、reason。queued 表示本地入队；confirmed 表示收到上传成功响应。请提供脱敏日志，不要提供密钥或联系方式。", EditorStyles.wordWrappedLabel);
        }

        private static bool TryGetHttpStatus(string query, out int status) =>
            int.TryParse(query.TrimStart('F', 'f'), out status);
    }
}
