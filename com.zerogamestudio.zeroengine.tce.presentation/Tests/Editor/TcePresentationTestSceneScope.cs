using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
namespace ZeroEngine.TCE.Presentation.Tests
{
    [SetUpFixture] public sealed class TcePresentationTestSceneScope
    {
        private Scene original,owned;
        [OneTimeSetUp] public void OpenOwnedVisualScene()
        {
            original=SceneManager.GetActiveScene();
            // Test Runner already owns an unsaved scratch scene for synchronous batches.
            if(string.IsNullOrEmpty(original.path))return;
            owned=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            SceneManager.SetActiveScene(owned);
        }
        [OneTimeTearDown] public void CloseOwnedVisualScene()
        {
            if(original.IsValid()&&original.isLoaded)SceneManager.SetActiveScene(original);
            if(owned.IsValid()&&owned.isLoaded)EditorSceneManager.CloseScene(owned,true);
        }
    }
}
