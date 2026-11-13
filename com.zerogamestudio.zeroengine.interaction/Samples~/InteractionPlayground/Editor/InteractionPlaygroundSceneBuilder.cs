using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ZeroEngine.Interaction.Samples.Editor
{
    public static class InteractionPlaygroundSceneBuilder
    {
        [MenuItem("Tools/ZeroEngine/Interaction/Create Playground Scene")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CreateAt("Assets/InteractionPlayground.unity");
        }

        public static void CreateAt(string path)
        {
            if (!path.StartsWith("Assets/") || Path.GetExtension(path) != ".unity")
                throw new System.ArgumentException("The playground scene must be inside Assets.", nameof(path));
            if (File.Exists(path)) throw new System.IO.IOException("Refusing to overwrite an existing scene: " + path);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Interaction Playground"); root.AddComponent<InteractionPlayground>();
            var cameraObject = new GameObject("Main Camera"); var camera = cameraObject.AddComponent<Camera>();
            camera.backgroundColor = new Color(.035f, .045f, .07f); camera.clearFlags = CameraClearFlags.SolidColor;
            EditorSceneManager.SaveScene(scene, path); AssetDatabase.Refresh();
        }
    }
}
