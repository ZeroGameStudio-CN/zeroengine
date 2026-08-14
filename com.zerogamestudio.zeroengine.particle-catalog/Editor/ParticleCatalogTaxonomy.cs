using System;
using System.Collections.Generic;
using System.Linq;

namespace ZeroEngine.ParticleCatalog
{
    public static class ParticleCatalogTaxonomy
    {
        public static readonly string[] Purposes = { "hit", "explosion", "projectile", "trail", "aura", "buff", "debuff", "environment", "ui", "spawn", "death", "warning", "pickup", "transition", "other" };
        public static readonly string[] Elements = { "fire", "ice", "lightning", "poison", "water", "wind", "earth", "nature", "blood", "smoke", "magic", "holy", "dark", "physical", "neutral" };
        public static readonly string[] Shapes = { "point", "circle", "ring", "cone", "line", "beam", "arc", "slash", "sphere", "cloud", "wave", "area", "other" };
        public static readonly string[] Motions = { "static", "burst", "expanding", "contracting", "directional", "orbit", "rising", "falling", "swirl", "follow", "random" };
        public static readonly string[] Colors = { "red", "orange", "yellow", "green", "cyan", "blue", "purple", "pink", "white", "black", "multicolor", "unknown" };
        public static readonly string[] Timings = { "instant", "short", "loop", "sustained", "delayed", "pulsing" };
        public static readonly string[] Styles = { "pixel", "cartoon", "stylized", "realistic", "glow", "soft", "sharp", "dark", "bright", "minimal" };
        public static readonly string[] Performance = { "light", "medium", "heavy" };

        private static readonly Dictionary<string, string> QueryAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "命中", "hit" }, { "受击", "hit" }, { "爆炸", "explosion" }, { "弹道", "projectile" },
            { "拖尾", "trail" }, { "光环", "aura" }, { "增益", "buff" }, { "减益", "debuff" },
            { "环境", "environment" }, { "界面", "ui" }, { "生成", "spawn" }, { "死亡", "death" },
            { "预警", "warning" }, { "拾取", "pickup" }, { "火", "fire" }, { "冰", "ice" },
            { "雷", "lightning" }, { "毒", "poison" }, { "水", "water" }, { "风", "wind" },
            { "土", "earth" }, { "自然", "nature" }, { "血", "blood" }, { "烟", "smoke" },
            { "魔法", "magic" }, { "神圣", "holy" }, { "黑暗", "dark" }, { "圆环", "ring" },
            { "射线", "beam" }, { "斩击", "slash" }, { "范围", "area" }, { "像素", "pixel" },
            { "卡通", "cartoon" }, { "发光", "glow" }, { "红色", "red" }, { "橙色", "orange" },
            { "黄色", "yellow" }, { "绿色", "green" }, { "青色", "cyan" }, { "蓝色", "blue" },
            { "紫色", "purple" }, { "粉色", "pink" }, { "白色", "white" }, { "黑色", "black" },
            { "雾", "smoke" }, { "移动端", "light" }, { "轻量", "light" }, { "重型", "heavy" }
        };

        public static string NormalizeQuery(string query)
        {
            string result = (query ?? string.Empty).Trim().ToLowerInvariant();
            foreach (KeyValuePair<string, string> alias in QueryAliases) result = result.Replace(alias.Key.ToLowerInvariant(), alias.Value);
            return result;
        }

        public static string[] Filter(string[] values, string[] allowed, string fallback = null)
        {
            HashSet<string> set = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
            string[] result = (values ?? Array.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value) && set.Contains(value.Trim()))
                .Select(value => value.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            return result.Length == 0 && fallback != null ? new[] { fallback } : result;
        }

        public static string DescribeAllowedValues()
        {
            return $"purposes=[{string.Join(",", Purposes)}]; elements=[{string.Join(",", Elements)}]; " +
                   $"shapes=[{string.Join(",", Shapes)}]; motions=[{string.Join(",", Motions)}]; " +
                   $"colors=[{string.Join(",", Colors)}]; timings=[{string.Join(",", Timings)}]; " +
                   $"styles=[{string.Join(",", Styles)}]; performance=[{string.Join(",", Performance)}]";
        }
    }
}
