using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ZeroEngine.Render.Urp;
using ZeroEngine.Render.Urp.Editor;

namespace ZeroEngine.Render.Tests.Editor
{
    public sealed class SpriteRimLightMenuTests
    {
        [TestCase(LightType.Point)]
        [TestCase(LightType.Spot)]
        public void DedicatedLightCreation_IsExplicitAndUndoable(LightType type)
        {
            var previousSelection = Selection.activeObject;
            var parent = new GameObject("Synthetic rim authoring parent");
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            GameObject light = null;
            try
            {
                light = SpriteRimLightMenu.Create(type, parent);
                Assert.That(light.transform.parent, Is.EqualTo(parent.transform));
                Assert.That(light.GetComponent<Light>().type, Is.EqualTo(type));
                Assert.That(light.GetComponent<SpriteRimLightSource>().enabled, Is.True);
                Assert.That(light.GetComponent<Light>().color.b, Is.GreaterThan(light.GetComponent<Light>().color.r));
                Undo.RevertAllDownToGroup(group);
                Assert.That(!light, Is.True, "Undo must remove the authored object and its marker together.");
            }
            finally
            {
                if (light) Object.DestroyImmediate(light);
                Object.DestroyImmediate(parent);
                Selection.activeObject = previousSelection;
            }
        }

        [Test]
        public void AuthoringAssembly_IsEditorOnlyAndHasNoConsumerDependency()
        {
            var assembly = typeof(SpriteRimLightMenu).Assembly;
            Assert.That(assembly.GetName().Name, Is.EqualTo("ZeroEngine.Render.Urp.Editor"));
            foreach (var reference in assembly.GetReferencedAssemblies())
                Assert.That(reference.Name.StartsWith("POB"), Is.False, reference.FullName);
            foreach (var reference in typeof(SpriteRimLighting).Assembly.GetReferencedAssemblies())
                Assert.That(reference.Name, Is.Not.EqualTo(assembly.GetName().Name));
        }
    }
}
