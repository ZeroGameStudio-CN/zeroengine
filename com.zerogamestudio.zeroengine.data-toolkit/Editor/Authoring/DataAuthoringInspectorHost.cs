using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZGS.DataToolkit.Editor
{
    public sealed class DataAuthoringInspectorHost : IDisposable
    {
        private readonly CompositeAssetInspector _assetInspector = new();

        public bool UsesModernPipeline(DataAuthoringProfile profile, Object asset)
        {
            return asset != null
                && profile != null
                && (profile.PreviewProviders.Any(provider => provider.CanPreview(asset))
                    || profile.DetailSections.Any(section => section.CanDraw(asset)));
        }

        public void Draw(
            DataAuthoringProfile profile,
            IDataAuthoringAssetAdapter adapter,
            DataAuthoringAssetRecord record,
            IReadOnlyList<DataAuthoringIssue> issues)
        {
            if (profile == null || adapter == null || record?.Asset == null)
            {
                return;
            }

            var context = new DataAuthoringPreviewContext(
                profile,
                adapter,
                record,
                issues ?? Array.Empty<DataAuthoringIssue>());
            DrawPreviewProviders(profile, record.Asset, context);
            DrawDetailSections(profile, record.Asset, context);
            DrawDefaultInspector(record.Asset);
        }

        public void Dispose()
        {
            _assetInspector.Dispose();
        }

        private static void DrawPreviewProviders(
            DataAuthoringProfile profile,
            Object asset,
            DataAuthoringPreviewContext context)
        {
            foreach (var provider in profile.PreviewProviders)
            {
                if (!provider.CanPreview(asset))
                {
                    continue;
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    provider.DrawPreview(context);
                }
            }
        }

        private static void DrawDetailSections(
            DataAuthoringProfile profile,
            Object asset,
            DataAuthoringPreviewContext context)
        {
            foreach (var section in profile.DetailSections)
            {
                if (!section.CanDraw(asset))
                {
                    continue;
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(section.Title, EditorStyles.boldLabel);
                    section.DrawSection(context);
                }
            }
        }

        private void DrawDefaultInspector(Object asset)
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Inspector", EditorStyles.boldLabel);
            _assetInspector.SetTarget(asset);
            _assetInspector.Draw();
        }
    }
}
