using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ZeroEngine.Render.Tests.Editor
{
    public sealed class FlowPatternTests
    {
        private const string Root = "Packages/com.zerogamestudio.zeroengine.render/Tests/Editor/";
        [Serializable] private sealed class Vectors { public string schema = ""; public float tolerance = 0f; public Sample[] cases = Array.Empty<Sample>(); }
        [Serializable] private sealed class Sample { public int kind = 0; public float[] args = Array.Empty<float>(); public float expected = 0f; }

        [Test]
        public void SharedGpuVectorsPreserveLoopSeamsPhaseAndPatternValues()
        {
            var fixture = AssetDatabase.LoadAssetAtPath<TextAsset>(Root + "render_flow_vectors.json");
            Assert.That(fixture, Is.Not.Null);
            var vectors = JsonUtility.FromJson<Vectors>(fixture.text);
            Assert.That(vectors.schema, Is.EqualTo("zero.render.flow.v1"));
            Assert.That(vectors.cases.Length, Is.GreaterThan(0));
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "FlowPatternFixture.shader");
            Assert.That(shader && shader.isSupported && !ShaderUtil.ShaderHasError(shader), Is.True);
            var material = new Material(shader);
            var target = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(16, 16, TextureFormat.RGBAFloat, false, true);
            var previous = RenderTexture.active;
            try
            {
                Assert.That(target.Create(), Is.True);
                for (int i = 0; i < vectors.cases.Length; i++)
                {
                    var sample = vectors.cases[i];
                    material.SetInt("_Kind", sample.kind);
                    material.SetVector("_Args", new Vector4(sample.args[0], sample.args[1], sample.args[2], sample.args[3]));
                    material.SetFloat("_Extra", sample.args[4]);
                    Graphics.Blit(Texture2D.whiteTexture, target, material);
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, 16, 16), 0, 0);
                    pixels.Apply();
                    float scale = sample.kind <= 1 ? 10f : 1f;
                    float actual = pixels.GetPixel(8, 8).r * scale;
                    Assert.That(actual, Is.EqualTo(sample.expected).Within(vectors.tolerance * scale), "GPU vector " + i);
                }
                Assert.That(ShaderUtil.ShaderHasError(shader), Is.False);
            }
            finally
            {
                RenderTexture.active = previous;
                target.Release();
                Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void RenderLeafDoesNotDependOnProductOrTceAssemblies()
        {
            foreach (var reference in typeof(SpritePoseHistory).Assembly.GetReferencedAssemblies())
            {
                Assert.That(reference.Name.StartsWith("POB", StringComparison.Ordinal), Is.False);
                Assert.That(reference.Name.StartsWith("ZeroEngine.TCE", StringComparison.Ordinal), Is.False);
            }
        }
    }
}
