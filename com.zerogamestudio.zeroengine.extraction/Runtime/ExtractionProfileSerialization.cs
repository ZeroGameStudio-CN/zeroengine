using System;
using UnityEngine;

namespace POB.Extraction
{
    public static class ExtractionProfileSerialization
    {
        public static string ToJson(ExtractionProfileSaveData profile)
        {
            return JsonUtility.ToJson(profile);
        }

        public static ExtractionProfileSaveData FromJson(string json)
        {
            var profile = JsonUtility.FromJson<ExtractionProfileSaveData>(json);
            if (profile == null) return null;

            // JsonUtility 不会为普通 JSON 应用 FormerlySerializedAs。v0/v1 先用仅含旧字段的
            // envelope 读取 Loadout，再交给唯一 migrator；v2 只保存 CarryGrid，不保留双份网格。
            if (profile.SchemaVersion < 2)
            {
                var legacy = JsonUtility.FromJson<LegacyProfileV1Envelope>(json);
                if (legacy?.Loadout != null)
                    profile.CarryGrid = legacy.Loadout;
            }

            profile.EnsureInitialized();
            return profile;
        }

        [Serializable]
        private sealed class LegacyProfileV1Envelope
        {
            public ExtractionItemGrid Loadout;
        }
    }
}
