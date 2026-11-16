#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using ZeroEngine.Render.Urp;
using Object = UnityEngine.Object;

namespace ZeroEngine.Render.Tests.Runtime
{
    public sealed class SpriteRimOriginalRenderTests
    {
        [UnityTest]
        public IEnumerator OriginalSelector_PreservesEulerDirection_AndDistinctSpotSelection()
        {
            var parent = new GameObject("Original selector transformed parent");
            var child = new GameObject("Original selector light");
            try
            {
                child.transform.SetParent(parent.transform,false);
                parent.transform.localScale=new Vector3(2,.7f,1); parent.transform.rotation=Quaternion.Euler(0,0,32);
                child.transform.localRotation=Quaternion.Euler(0,0,-20);
                var light=child.AddComponent<SpriteRimLight>(); light.Shape=LightType.Directional;
                var type=typeof(SpriteRimLighting).Assembly.GetType(typeof(SpriteRimLighting).Namespace+".SpriteRimOriginalGroup+Source",true);
                object Source()=>Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{light},null);
                var influence=type.GetMethod("Influence",BindingFlags.Instance|BindingFlags.NonPublic);
                object[] args={child.transform.position+new Vector3(0,.1f,0),Vector2.zero,0f};
                Assert.That((bool)influence.Invoke(Source(),args),Is.True,"The original directional selector covers both sides of its box.");
                float angle=child.transform.eulerAngles.z*Mathf.Deg2Rad;
                Assert.That(Vector2.Distance((Vector2)args[1],new Vector2(Mathf.Sin(angle),-Mathf.Cos(angle))),Is.LessThan(.00001f));
                child.transform.SetParent(null,false);child.transform.SetPositionAndRotation(Vector3.zero,Quaternion.identity);child.transform.localScale=Vector3.one;
                light.Shape=LightType.Spot;light.Radius=Vector2.one*2;light.Intensity=.8f;light.RangePower=1;light.LightPower=1;
                args=new object[]{new Vector3(0,-1,0),Vector2.zero,0f};
                Assert.That((bool)influence.Invoke(Source(),args),Is.False,"The original CPU selector has zero centre-line score; its field shader does not.");
                args=new object[]{new Vector3(.5f,-1,0),Vector2.zero,0f};
                Assert.That((bool)influence.Invoke(Source(),args),Is.True);
                float expected=Mathf.Atan2(.25f,.5f)*Mathf.Rad2Deg/45*(1-Mathf.Sqrt(.25f*.25f+.5f*.5f))*.8f;
                Assert.That((float)args[2],Is.EqualTo(expected).Within(.00001f));
            }
            finally {Object.DestroyImmediate(child);Object.DestroyImmediate(parent);}
            yield return null;
        }

        [UnityTest]
        public IEnumerator OriginalBlend_MatchesShippedShaderGoldenSamples()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.zerogamestudio.zeroengine.render.urp/Tests/Runtime/SpriteRimOriginalGoldenTest.shader");
            Assert.That(shader && shader.isSupported,Is.True);
            var material = new Material(shader);
            var output = new RenderTexture(1,1,0,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var readback = new Texture2D(1,1,TextureFormat.RGBAFloat,false,true);
            var previous = RenderTexture.active;
            try
            {
                output.Create(); material.SetVector("_Original",new Vector4(.02f,.04f,.08f,1));
                foreach (var sample in SpriteRimOriginalGoldenSamples.Values)
                {
                    material.SetFloat("_Mode",sample.Mode); material.SetVector("_Light",sample.Light);
                    Graphics.Blit(Texture2D.whiteTexture,output,material);
                    RenderTexture.active=output; readback.ReadPixels(new Rect(0,0,1,1),0,0); readback.Apply();
                    var actual=readback.GetPixel(0,0);
                    Assert.That(actual.r,Is.EqualTo(sample.Result.x).Within(.00005f),"Original red channel, mode "+sample.Mode);
                    Assert.That(actual.g,Is.EqualTo(sample.Result.y).Within(.00005f),"Original green channel, mode "+sample.Mode);
                    Assert.That(actual.b,Is.EqualTo(sample.Result.z).Within(.00005f),"Original blue channel, mode "+sample.Mode);
                }
            }
            finally
            {
                RenderTexture.active=previous; output.Release();
                Object.DestroyImmediate(material); Object.DestroyImmediate(output); Object.DestroyImmediate(readback);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator OriginalMechanism_KeepsDarkBlueDistinct_AccumulatesAllLights_AndRestoresSources()
        {
            var previous = QualitySettings.renderPipeline;
            UniversalRenderPipelineAsset pipeline = null; ScriptableRendererData data = null;
            SpriteRimRendererFeature feature = null; Scene scene = default;
            Texture2D texture = null; Sprite sprite = null; Material material = null;
            Texture2D linearTexture = null, linearImage = null; Sprite linearSprite = null;
            SpriteRimLightingProfile profile = null;
            RenderTexture output = null; Texture2D first = null, many = null, noRim = null, ignored = null, limited = null;
            try
            {
                var original = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                Assert.That(original, Is.Not.Null);
                var rendererField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
                pipeline = Object.Instantiate(original);
                data = Object.Instantiate(((ScriptableRendererData[])rendererField.GetValue(original))[0]);
                data.rendererFeatures.Clear();
                feature = ScriptableObject.CreateInstance<SpriteRimRendererFeature>(); feature.Create(); data.rendererFeatures.Add(feature);
                rendererField.SetValue(pipeline, new[] { data });
                pipeline.supportsCameraDepthTexture = true; pipeline.supportsHDR = false;
                pipeline.msaaSampleCount = 1; pipeline.renderScale = 1;
                QualitySettings.renderPipeline = pipeline;
                scene = SceneManager.CreateScene("Original rim synthetic " + Guid.NewGuid().ToString("N"));
                GameObject Host(string name)
                { var go = new GameObject(name) { layer = 30 }; SceneManager.MoveGameObjectToScene(go, scene); return go; }
                var camera = Host("Original rim camera").AddComponent<Camera>();
                camera.enabled = false; camera.scene = scene; camera.cullingMask = 1 << 30;
                camera.orthographic = true; camera.orthographicSize = .5f; camera.aspect = 1;
                camera.transform.position = new Vector3(0,0,-2); camera.nearClipPlane = .1f; camera.farClipPlane = 10;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f,.05f,.03f,1);
                var extra = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                extra.renderPostProcessing = false; extra.requiresDepthOption = CameraOverrideOption.On;
                texture = new Texture2D(16,16,TextureFormat.RGBA32,false,false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color[256];
                for (int y=0;y<16;y++) for (int x=0;x<16;x++)
                    pixels[y*16+x] = x>=3 && x<=10 && y>=2 && y<=11 ? new Color(.02f,.04f,.08f,1) : Color.clear;
                texture.SetPixels(pixels); texture.Apply();
                sprite = Sprite.Create(texture,new Rect(0,0,16,16),Vector2.one*.5f,16,0,SpriteMeshType.FullRect);
                var actor = Host("Original rim actor").AddComponent<SpriteRenderer>(); actor.sprite = sprite;
                material = new Material(AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.zerogamestudio.zeroengine.render.urp/Tests/Runtime/SpriteRimOpaqueTest.shader"));
                material.mainTexture = texture; material.SetColor("_BaseColor",Color.white); actor.sharedMaterial = material;
                var properties = new MaterialPropertyBlock(); properties.SetFloat("_UnrelatedOwner",7); actor.SetPropertyBlock(properties);
                profile = ScriptableObject.CreateInstance<SpriteRimLightingProfile>(); profile.UseOriginalMechanism = true;
                Assert.That(profile.Original.MinimumDistance,Is.EqualTo(.04f)); Assert.That(profile.Original.MaximumDistance,Is.EqualTo(.015f));
                Assert.That(profile.Original.LightBlend,Is.EqualTo(1)); Assert.That(profile.Original.MaximumLight,Is.EqualTo(3));
                var lamps = new SpriteRimLight[6];
                for (int i=0;i<lamps.Length;i++)
                {
                    lamps[i] = Host("Original point " + i).AddComponent<SpriteRimLight>();
                    lamps[i].transform.position = new Vector3(-.5f,0,3+i);
                    lamps[i].Radius = Vector2.one*2; lamps[i].LightColor = new Color(.08f,.3f,.7f,1);
                    lamps[i].Intensity = i==0 ? 1 : 0;
                }
                var lighting = actor.gameObject.AddComponent<SpriteRimLighting>();
                lighting.Configure(new Renderer[] { actor },lamps,profile);
                yield return null;
                output = new RenderTexture(64,64,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
                output.Create(); camera.targetTexture = output;
                Texture2D Read()
                {
                    camera.Render(); var active = RenderTexture.active;
                    try
                    {
                        RenderTexture.active = output; var image = new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);
                        image.ReadPixels(new Rect(0,0,64,64),0,0); image.Apply(); return image;
                    }
                    finally { RenderTexture.active = active; }
                }
                first = Read(); Assert.That(lighting.ActiveTargetCount,Is.EqualTo(1));
                Color edge = first.GetPixel(13,28), inside = first.GetPixel(29,28);
                Assert.That(edge.b,Is.GreaterThan(inside.b*1.5f),"Dark blue keeps its hue and responds to the original source-colour dodge.");
                Assert.That(inside.b,Is.GreaterThan(inside.g*1.5f));
                for (int i=1;i<lamps.Length;i++) lamps[i].Intensity=.2f;
                many = Read();
                Assert.That(many.GetPixel(13,28).b,Is.GreaterThan(edge.b+.0001f),"The original field includes lamps beyond ZE's four-light contour selection.");
                lamps[1].LightColor=new Color(1,0,0,1);lamps[1].Intensity=1;lamps[1].transform.position=new Vector3(.5f,0,4);
                for(int i=2;i<lamps.Length;i++)lamps[i].Intensity=0;
                lighting.OriginalIgnoredLights=new Object[]{lamps[0]};
                ignored=Read();
                Assert.That(ignored.GetPixel(42,28).b,Is.GreaterThan(ignored.GetPixel(29,28).b+.001f),
                    "Ignored blue still colors the right contour selected by red; it is excluded only from direction selection.");
                lighting.OriginalIgnoredLights=Array.Empty<Object>();
                profile.Original.MaximumSelectionDistance=.01f;limited=Read();
                Assert.That(limited.GetPixel(13,28).b,Is.EqualTo(limited.GetPixel(29,28).b).Within(.0001f));
                profile.Original.MaximumSelectionDistance=4;
                foreach (var lamp in lamps) lamp.AffectsRim=false;
                noRim = Read();
                Assert.That(noRim.GetPixel(13,28).b,Is.EqualTo(noRim.GetPixel(29,28).b).Within(.0001f),"Field-only lights do not invent a rim direction.");
                linearTexture=new Texture2D(16,16,TextureFormat.RGBA32,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                linearTexture.SetPixels(texture.GetPixels());linearTexture.Apply();
                linearSprite=Sprite.Create(linearTexture,new Rect(0,0,16,16),Vector2.one*.5f,16,0,SpriteMeshType.FullRect);
                actor.sprite=linearSprite;material.mainTexture=linearTexture;linearImage=Read();
                Assert.That(linearImage.GetPixel(29,28).b,Is.EqualTo(noRim.GetPixel(29,28).b).Within(.0001f),
                    "Reconstruct raw source color for both sRGB and linear texture views, without double-decoding the latter.");
                properties.Clear(); actor.GetPropertyBlock(properties);
                Assert.That(properties.GetFloat("_UnrelatedOwner"),Is.EqualTo(7)); Assert.That(actor.sharedMaterial,Is.SameAs(material));
                lighting.enabled=false; Assert.That(lighting.ActiveTargetCount,Is.Zero);
                Assert.That(actor.sharedMaterial,Is.SameAs(material));
            }
            finally
            {
                QualitySettings.renderPipeline=previous;
                if(scene.IsValid()) { var operation=SceneManager.UnloadSceneAsync(scene); }
                if(output) { output.Release(); Object.DestroyImmediate(output); }
                foreach(var value in new Object[] {first,many,noRim,ignored,limited,linearImage,linearSprite,linearTexture,texture,sprite,material,profile,pipeline,data,feature})
                    if(value) Object.DestroyImmediate(value);
            }
            yield return null;
        }
    }
}
#endif
