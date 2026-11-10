using NUnit.Framework;
using UnityEngine;

namespace ZeroEngine.Render.Tests
{
    public sealed class RendererOpacityBindingTests
    {
        [Test]
        public void PropertyBlockAuthoredBetweenFrames_KeepsItsOwnAlphaAfterFade()
        {
            var root = new GameObject("Property-block animation contract");
            var renderer = root.AddComponent<MeshRenderer>();
            var material = new Material(Shader.Find("Sprites/Default"));
            material.color = new Color(.2f, .4f, .6f, .8f); renderer.sharedMaterial = material;
            var binding = new RendererOpacityBinding(renderer);
            var block = new MaterialPropertyBlock(); int color = Shader.PropertyToID("_Color");
            try
            {
                binding.Apply(.5f); binding.Restore();
                block.SetColor(color, new Color(.7f, .2f, .3f, .6f)); renderer.SetPropertyBlock(block);
                binding.Apply(.5f); renderer.GetPropertyBlock(block);
                Assert.That(block.GetColor(color).a, Is.EqualTo(.3f).Within(.001f));
                binding.Restore(); material.color = new Color(.2f, .4f, .6f, .1f);
                binding.Apply(1f); renderer.GetPropertyBlock(block);
                Assert.That(block.GetColor(color).a, Is.EqualTo(.6f).Within(.001f));
            }
            finally { binding.Dispose(); Object.DestroyImmediate(root); Object.DestroyImmediate(material); }
        }

        [Test]
        public void MaterialAssignedWhileHidden_IsDiscoveredOnFirstPartialApply()
        {
            var root = new GameObject("Lazy opacity contract");
            var renderer = root.AddComponent<MeshRenderer>();
            var material = new Material(Shader.Find("Sprites/Default"));
            var binding = new RendererOpacityBinding(renderer);
            var block = new MaterialPropertyBlock();
            int color = Shader.PropertyToID("_Color");
            try
            {
                binding.Apply(1f);
                binding.Apply(0f);
                material.SetColor(color, new Color(.2f, .4f, .6f, .6f));
                renderer.sharedMaterial = material;
                binding.Apply(.5f); renderer.GetPropertyBlock(block);
                Assert.That(renderer.forceRenderingOff, Is.False);
                Assert.That(block.GetColor(color).a, Is.EqualTo(.3f).Within(.001f));
                binding.Apply(1f); renderer.GetPropertyBlock(block);
                Assert.That(block.GetColor(color).a, Is.EqualTo(.6f).Within(.001f));
            }
            finally { binding.Dispose(); Object.DestroyImmediate(root); Object.DestroyImmediate(material); }
        }

        [Test]
        public void PartialValuesAndZero_RestoreAuthoredInputsWithoutMutatingSharedMaterial()
        {
            var root = new GameObject("Opacity contract");
            var renderer = root.AddComponent<ParticleSystem>().GetComponent<Renderer>();
            var material = new Material(Shader.Find("Sprites/Default"));
            int color = Shader.PropertyToID("_Color");
            material.SetColor(color, new Color(.2f, .4f, .6f, .8f));
            renderer.sharedMaterial = material;
            var binding = new RendererOpacityBinding(renderer);
            var block = new MaterialPropertyBlock();
            try
            {
                binding.Apply(.5f); renderer.GetPropertyBlock(block);
                Assert.That(block.GetColor(color).a, Is.EqualTo(.4f).Within(.001f));
                binding.Apply(.25f); renderer.GetPropertyBlock(block);
                Assert.That(block.GetColor(color).a, Is.EqualTo(.2f).Within(.001f));
                binding.Apply(0f);
                Assert.That(renderer.forceRenderingOff, Is.True);
                binding.Apply(1f); renderer.GetPropertyBlock(block);
                Assert.That(renderer.forceRenderingOff, Is.False);
                Assert.That(block.GetColor(color).a, Is.EqualTo(.8f).Within(.001f));
                Assert.That(material.GetColor(color).a, Is.EqualTo(.8f).Within(.001f));
            }
            finally { binding.Dispose(); Object.DestroyImmediate(root); Object.DestroyImmediate(material); }
        }

        [Test]
        public void AnimatedSpriteAndForeignVisibility_KeepTheirOwnAuthoredState()
        {
            var root = new GameObject("Sprite opacity contract");
            var sprite = root.AddComponent<SpriteRenderer>();
            sprite.color = new Color(.2f, .4f, .6f, .8f);
            var binding = new RendererOpacityBinding(sprite);
            try
            {
                binding.Apply(.5f);
                Assert.That(sprite.color.a, Is.EqualTo(.4f).Within(.001f));
                sprite.color = new Color(.3f, .5f, .7f, .6f);
                binding.Apply(.25f);
                Assert.That(sprite.color.a, Is.EqualTo(.15f).Within(.001f));
                binding.Restore();
                Assert.That(sprite.color.a, Is.EqualTo(.6f).Within(.001f));
                sprite.forceRenderingOff = true;
                binding.Apply(0f); binding.Restore();
                Assert.That(sprite.forceRenderingOff, Is.True);
            }
            finally { binding.Dispose(); Object.DestroyImmediate(root); }
        }

        [Test]
        public void MaterialAnimationAndReplacement_RemainLiveDuringReduction()
        {
            var root = new GameObject("Material opacity contract");
            var renderer = root.AddComponent<ParticleSystem>().GetComponent<Renderer>();
            int color = Shader.PropertyToID("_Color");
            var first = new Material(Shader.Find("Sprites/Default"));
            var second = new Material(first);
            first.SetColor(color, new Color(1, 1, 1, .8f));
            second.SetColor(color, new Color(1, 1, 1, .6f));
            renderer.sharedMaterial = first;
            var binding = new RendererOpacityBinding(renderer);
            var block = new MaterialPropertyBlock();
            try
            {
                binding.Apply(.5f);
                first.SetColor(color, new Color(1, 1, 1, .4f));
                binding.Apply(.5f); renderer.GetPropertyBlock(block);
                Assert.That(block.GetColor(color).a, Is.EqualTo(.2f).Within(.001f));
                renderer.sharedMaterial = second;
                binding.Apply(.5f); renderer.GetPropertyBlock(block);
                Assert.That(block.GetColor(color).a, Is.EqualTo(.3f).Within(.001f));
                binding.Restore(); renderer.GetPropertyBlock(block);
                Assert.That(block.GetColor(color).a, Is.EqualTo(.6f).Within(.001f));
            }
            finally { binding.Dispose(); Object.DestroyImmediate(root); Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
        }
    }
}
