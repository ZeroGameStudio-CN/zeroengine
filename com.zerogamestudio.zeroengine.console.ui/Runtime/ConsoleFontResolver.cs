using System;
using UnityEngine;

namespace ZeroEngine.Console.UI
{
    internal sealed class ConsoleFontResolver : IDisposable
    {
        // OS references only: no font files, baked atlas or runtime text-library dependency.
        // Glyph availability does not provide complex-script shaping in legacy uGUI.
        private static readonly string[] Families = {
            "Segoe UI", "Roboto", "Noto Sans", "Arial",
            "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "PingFang TC",
            "Noto Sans CJK SC", "Noto Sans CJK TC", "Noto Sans CJK JP", "Noto Sans CJK KR",
            "Hiragino Sans", "Yu Gothic", "Meiryo", "Malgun Gothic", "Apple SD Gothic Neo",
            "Noto Sans Thai", "Leelawadee UI", "Nirmala UI", "Noto Sans Devanagari",
            "Noto Sans Arabic", "Noto Naskh Arabic", "Noto Sans Hebrew", "Geeza Pro",
            "Arial Unicode MS", "DejaVu Sans"
        };

        private Font source, resolved, owned;

        internal Font Resolve(Font primary, int size)
        {
            primary = primary ? primary : null;
            if (resolved && ReferenceEquals(primary, source)) return resolved;
            Dispose();
            source = primary;
            // Dynamic Font cloning is unsupported. Borrow the project's face, material
            // and existing Unity fallback rules; never rewrite a shared asset.
            if (primary) return resolved = primary;
            if (Application.platform != RuntimePlatform.WebGLPlayer)
            {
                try { owned = Font.CreateDynamicFontFromOSFont(Families, size); }
                catch (ArgumentException) { }
            }
            if (owned) owned.hideFlags = HideFlags.HideAndDontSave;
            return resolved = owned ? owned : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        public void Dispose()
        {
            if (owned) UnityEngine.Object.Destroy(owned);
            source = resolved = owned = null;
        }
    }
}
