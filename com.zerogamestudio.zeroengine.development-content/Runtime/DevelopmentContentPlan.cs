using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace ZeroEngine.DevelopmentContent
{
    // Development progress, target version and actual user availability are independent facts.
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ContentDevelopmentStage { Unknown, InProgress, [EnumMember(Value = "Playable")] Previewable, Ready, Paused, Abandoned }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ContentAvailability { Unknown, [EnumMember(Value = "EditorOnly")] DevelopmentOnly, DisabledByDefault, TestOnly, Released, InternalOnly }

    public enum ContentPlanView { Current, OtherVersions, Unplanned, All }

    public sealed class DevelopmentContentRelease
    {
        public string Id = string.Empty;
        public string Name = string.Empty;

        [JsonExtensionData] private IDictionary<string, JToken> Extensions { get; set; }
    }

    public sealed class DevelopmentContentRecord
    {
        public string Id = string.Empty;
        public string Title = string.Empty;
        public string SourceKey = string.Empty;
        public ContentDevelopmentStage Stage;
        public string TargetReleaseId = string.Empty;
        public ContentAvailability Availability;
        public string ResponsibleChat = string.Empty;
        public string ChatUrl = string.Empty;
        public string NextAction = string.Empty;
        public string Entrance = string.Empty;
        public string ResourcePath = string.Empty;
        public string Revision = string.Empty;
        public string ReleaseEvidence = string.Empty;

        [JsonExtensionData] internal IDictionary<string, JToken> Extensions { get; set; }

        [JsonIgnore]
        public bool ReadyUnreleased => Stage == ContentDevelopmentStage.Ready &&
            Availability is ContentAvailability.DevelopmentOnly or ContentAvailability.DisabledByDefault or ContentAvailability.TestOnly;

        public DevelopmentContentRecord Copy() => (DevelopmentContentRecord)MemberwiseClone();
    }

    public sealed class DevelopmentContentPlan
    {
        public const string LaterReleaseId = "later";
        [JsonProperty(Required = Required.Always)] public int SchemaVersion = 1;
        [JsonProperty(Required = Required.Always)] public string ProjectId = string.Empty;
        public string CurrentReleaseId = string.Empty;
        [JsonProperty(Required = Required.Always)] public List<DevelopmentContentRelease> Releases = new();
        [JsonProperty(Required = Required.Always)] public List<DevelopmentContentRecord> Records = new();

        [JsonExtensionData] private IDictionary<string, JToken> Extensions { get; set; }

        // A draft edits shared fields. The current record owns its project metadata.
        public void ApplyRecordEdit(DevelopmentContentRecord draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            var edited = draft.Copy();
            int index = Records.FindIndex(record => record.Id == edited.Id);
            if (index < 0)
            {
                Records.Add(edited);
                return;
            }

            edited.Extensions = Records[index].Extensions;
            Records[index] = edited;
        }

        public bool InView(DevelopmentContentRecord record, ContentPlanView view) => view switch
        {
            ContentPlanView.All => true,
            _ when record.Stage == ContentDevelopmentStage.Abandoned => false,
            _ when record.Availability is ContentAvailability.InternalOnly or ContentAvailability.Released => false,
            ContentPlanView.Unplanned => string.IsNullOrEmpty(record.TargetReleaseId),
            ContentPlanView.Current => !string.IsNullOrEmpty(CurrentReleaseId) && record.TargetReleaseId == CurrentReleaseId,
            ContentPlanView.OtherVersions => !string.IsNullOrEmpty(record.TargetReleaseId) && record.TargetReleaseId != CurrentReleaseId,
            _ => false
        };

        public void Validate(string projectId)
        {
            if (SchemaVersion != 1 || ProjectId != projectId || Releases == null || Records == null)
                throw new FormatException("计划格式或项目不匹配；未覆盖原文件。");
            var releaseIds = new HashSet<string>(StringComparer.Ordinal);
            var releaseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var release in Releases)
                if (release == null || string.IsNullOrWhiteSpace(release.Id) || release.Id == LaterReleaseId ||
                    string.IsNullOrWhiteSpace(release.Name) || !releaseIds.Add(release.Id) || !releaseNames.Add(release.Name.Trim()))
                    throw new FormatException("版本需要唯一 ID 和名称。");
            if (!string.IsNullOrEmpty(CurrentReleaseId) && !releaseIds.Contains(CurrentReleaseId))
                throw new FormatException("本版必须引用已登记版本。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var sources = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in Records)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.Id) || string.IsNullOrWhiteSpace(record.Title) || !ids.Add(record.Id))
                    throw new FormatException("内容需要唯一 ID 和名称。");
                if (!string.IsNullOrEmpty(record.SourceKey) && !sources.Add(record.SourceKey))
                    throw new FormatException("同一清单入口已有关联登记，请编辑原记录。");
                if (!Enum.IsDefined(typeof(ContentDevelopmentStage), record.Stage) ||
                    !Enum.IsDefined(typeof(ContentAvailability), record.Availability))
                    throw new FormatException("开发进度或用户开放范围无效。");
                if (!string.IsNullOrEmpty(record.TargetReleaseId) && record.TargetReleaseId != LaterReleaseId && !releaseIds.Contains(record.TargetReleaseId))
                    throw new FormatException("目标版本不存在。");
                if (record.Availability == ContentAvailability.InternalOnly && !string.IsNullOrEmpty(record.TargetReleaseId))
                    throw new FormatException("内部工具不安排用户版本，请先清空目标版本。");
                if (record.Availability == ContentAvailability.Released && string.IsNullOrWhiteSpace(record.ReleaseEvidence))
                    throw new FormatException("标记用户已开放时，需填写实际发布版本或证据；提交号和可试用不代替上线。");
            }
        }
    }

    public sealed class DevelopmentContentPlanStore
    {
        private static readonly JsonSerializerSettings Settings = new()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            DateParseHandling = DateParseHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
            Formatting = Formatting.Indented
        };
        private static readonly UTF8Encoding Utf8 = new(false, true);
        private readonly string path;
        private readonly string projectId;
        public string Revision { get; private set; }

        public DevelopmentContentPlanStore(string path, string projectId)
        {
            this.path = path;
            this.projectId = projectId;
        }

        public DevelopmentContentPlan Load()
        {
            byte[] bytes = File.Exists(path) ? File.ReadAllBytes(path) : null;
            var plan = bytes == null ? new DevelopmentContentPlan { ProjectId = projectId } :
                JsonConvert.DeserializeObject<DevelopmentContentPlan>(Utf8.GetString(bytes), Settings);
            if (plan == null) throw new FormatException("计划为空，未覆盖原文件。");
            plan.Validate(projectId);
            Revision = Hash(bytes);
            return plan;
        }

        // Refuse stale edits. Atomic replacement keeps a partial write from becoming a valid empty plan.
        public void Save(DevelopmentContentPlan plan)
        {
            plan.Validate(projectId);
            string json = JsonConvert.SerializeObject(plan, Settings) + "\n";
            string lockPath = Path.GetFullPath(path);
            if (Path.DirectorySeparatorChar == '\\') lockPath = lockPath.ToUpperInvariant();
            using var mutex = new Mutex(false, "DevelopmentContentPlan." + Hash(Utf8.GetBytes(lockPath)));
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("计划正在保存，草稿保留，请稍后重试。");
            try { Write(json); }
            finally { mutex.ReleaseMutex(); }
        }

        private void Write(string json)
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, json, Utf8);
                string current = Hash(File.Exists(path) ? File.ReadAllBytes(path) : null);
                if (current != Revision) throw new IOException("计划已被其他编辑修改。当前草稿已保留，请重新载入后合并。");
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                Revision = Hash(Utf8.GetBytes(json));
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        public static DevelopmentContentPlan Copy(DevelopmentContentPlan plan) =>
            JsonConvert.DeserializeObject<DevelopmentContentPlan>(JsonConvert.SerializeObject(plan, Settings), Settings);

        private static string Hash(byte[] bytes)
        {
            if (bytes == null) return null;
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty);
        }
    }
}
