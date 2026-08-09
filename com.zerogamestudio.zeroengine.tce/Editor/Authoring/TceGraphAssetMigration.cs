using UnityEditor;

namespace ZeroEngine.TCE.Editor
{
    public static class TceGraphAssetMigration
    {
        public static bool MigrateToCurrent(TceGraphAsset asset)
        {
            if (asset == null || asset.GraphSchemaVersion >= TceGraphSchema.CurrentVersion)
                return false;

            var serializedObject = new SerializedObject(asset);
            SerializedProperty schemaVersion = serializedObject.FindProperty(TceGraphSerializedAccess.GraphSchemaVersionProperty);
            if (schemaVersion == null)
                return false;

            schemaVersion.intValue = TceGraphSchema.CurrentVersion;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return true;
        }
    }
}
