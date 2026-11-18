#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using ZeroEngine.Render.Urp;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ZeroEngine.Render.Tests.Runtime
{
    /// <summary>Core contract: the actual Render Graph route works on an unchanged opaque shader and restores the original image.</summary>
    public sealed class SpriteRimLightingRenderTests
    {
        [UnityTest]
        public IEnumerator MultiLight_KeepsOpposedContoursStableAndRestoresOriginalMaterials() => VerifyComposite();

        [UnityTest]
        public IEnumerator InnerWrap_FadesAcrossArtCellsWithoutChangingTheOuterRim() => VerifyComposite(true);

        [UnityTest]
        public IEnumerator SpatialVisibility_PreservesPivotsWorldDirectionAndTransparentSources() => VerifyComposite(true, true);

        [UnityTest]
        public IEnumerator SceneLights_IgnoreSkyDirectional_KeepLocalPointSpotAndCullingMasks() => VerifyComposite(false, false, true);

        [UnityTest]
        public IEnumerator NativeContourCore_LightsInnerGapsAndIslandsWithoutBodyFillOrExtraHdr() => VerifyComposite(false, false, true, true);

        [UnityTest]
        public IEnumerator ReferenceEdges_PreserveSceneBody_KeepOpposedSides_AndFadeOnlyNearTheContour() => VerifyComposite(true, true, false, false, true);

        private IEnumerator VerifyComposite(bool verifyWrap = false, bool verifySpatial = false, bool verifyNative = false, bool verifyContour = false, bool verifyReference = false)
        {
            Assert.That(Application.isPlaying, Is.True);
            RenderPipelineAsset previous = QualitySettings.renderPipeline;
            UniversalRenderPipelineAsset pipeline = null; ScriptableRendererData renderer = null; SpriteRimRendererFeature feature = null;
            Scene preview = default;
            GameObject cameraHost = null, actorHost = null, lampHost = null, occluder = null;
            Texture2D texture = null, baseline = null, lit = null, upper = null, restored = null;
            Sprite sprite = null; Material material = null, occluderMaterial = null; SpriteRimLightingProfile profile = null;
            try
            {
                var original = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                var rendererField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
                if (original)
                {
                    var sourceData = (ScriptableRendererData[])rendererField.GetValue(original);
                    Assert.That(sourceData, Is.Not.Null.And.Not.Empty);
                    pipeline = Object.Instantiate(original);
                    renderer = Object.Instantiate(sourceData[0]);
                }
                else
                {
                    renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                    pipeline = UniversalRenderPipelineAsset.Create(renderer);
                }
                renderer.rendererFeatures.Clear();
                feature = ScriptableObject.CreateInstance<SpriteRimRendererFeature>();
                feature.Create();
                renderer.rendererFeatures.Add(feature);
                rendererField.SetValue(pipeline, new[] { renderer });
                pipeline.supportsCameraDepthTexture = true;
                pipeline.supportsCameraOpaqueTexture = false;
                pipeline.supportsHDR = false;
                pipeline.msaaSampleCount = 1;
                pipeline.renderScale = 1f;
                if (verifyNative)
                {
                    typeof(UniversalRenderPipelineAsset).GetProperty(nameof(UniversalRenderPipelineAsset.supportsAdditionalLightShadows))
                        .SetValue(pipeline, true);
                    pipeline.shadowDistance = 10f;
                }
                QualitySettings.renderPipeline = pipeline;
                preview = SceneManager.CreateScene("Synthetic rim runtime " + Guid.NewGuid().ToString("N"));
                cameraHost = new GameObject("Synthetic reference camera"); SceneManager.MoveGameObjectToScene(cameraHost, preview); var camera = cameraHost.AddComponent<Camera>();
                camera.enabled = false; camera.scene = preview; camera.orthographic = true; camera.orthographicSize = .5f; camera.aspect = 1;
                camera.nearClipPlane = .1f; camera.farClipPlane = 10; camera.cullingMask = 1 << 30;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f,.05f,.03f,1);
                camera.transform.position = new Vector3(0,0,-2); camera.transform.rotation = Quaternion.identity;
                var additionalType = Type.GetType("UnityEngine.Rendering.Universal.UniversalAdditionalCameraData, Unity.RenderPipelines.Universal.Runtime", true);
                var additional = cameraHost.AddComponent(additionalType);
                additionalType.GetProperty("renderPostProcessing").SetValue(additional, false);
                var depthOption=additionalType.GetProperty("requiresDepthOption"); if(depthOption!=null) depthOption.SetValue(additional, Enum.Parse(depthOption.PropertyType,"On"));
                texture = new Texture2D(16,16,TextureFormat.RGBA32,false,true) {filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                var colors = new Color[256];
                for(int y=0;y<16;y++)for(int x=0;x<16;x++) colors[y*16+x]=(x>=3&&x<=10&&y>=2&&y<=11)?Color.white:Color.clear;
                texture.SetPixels(colors);texture.Apply();
                sprite=Sprite.Create(texture,new Rect(0,0,16,16),Vector2.one*.5f,16,0,SpriteMeshType.FullRect);
                actorHost=new GameObject("Synthetic reference actor");SceneManager.MoveGameObjectToScene(actorHost, preview);actorHost.layer=30;
                var actor=actorHost.AddComponent<SpriteRenderer>();actor.sprite=sprite;
                material=new Material(AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.zerogamestudio.zeroengine.render.urp/Tests/Runtime/SpriteRimOpaqueTest.shader"));
                material.SetVector("_BaseColor",new Vector4(.2f,.2f,.2f,1));material.mainTexture=texture;actor.sharedMaterial=material;
                Assert.That(material.HasProperty("_RimPreviewMask"),Is.False,"The test shader must have no old tint hooks.");
                occluder=GameObject.CreatePrimitive(PrimitiveType.Quad);SceneManager.MoveGameObjectToScene(occluder, preview);occluder.name="Synthetic foreground occluder";occluder.layer=30;
                occluder.transform.position=new Vector3(-.3f,-.25f,-.3f);occluder.transform.localScale=new Vector3(.16f,.18f,1);
                occluderMaterial=new Material(material);occluderMaterial.mainTexture=Texture2D.whiteTexture;
                occluderMaterial.SetVector("_BaseColor",new Vector4(.4f,.1f,.1f,1));occluder.GetComponent<Renderer>().sharedMaterial=occluderMaterial;

                var properties=new MaterialPropertyBlock();int otherId=Shader.PropertyToID("_OtherVisualLease");properties.SetFloat(otherId,7);actor.SetPropertyBlock(properties);
                lampHost=new GameObject("Synthetic 2D field light");SceneManager.MoveGameObjectToScene(lampHost, preview);var light=lampHost.AddComponent<SpriteRimLight>();
                light.Radius=Vector2.one*2;light.LightColor=new Color(.15f,.85f,.25f,1);light.Intensity=1;lampHost.transform.position=new Vector3(-.5f,0,0);
                profile=ScriptableObject.CreateInstance<SpriteRimLightingProfile>();profile.InnerWrapStrength=0;profile.Strength=1;
                profile.WeakDistance=4;profile.StrongDistance=1.5f;profile.ResponsePower=.5f;
                profile.StrongScale=-.05f;profile.MinimumExposure=.9f;profile.MaximumLight=3;
                profile.GammaBlend=true;profile.LimitAddedHdr=true;
                profile.ReferenceEdgeResponse=verifyReference;
                var lighting=actorHost.AddComponent<SpriteRimLighting>();lighting.enabled=false;
                lighting.Configure(new Renderer[]{actor},new[]{light},profile);
                yield return null;
                baseline=Render(camera);
                lighting.enabled=true;lighting.UpdateLighting();
                Assert.That(lighting.ActiveTargetCount,Is.EqualTo(1));
                lit=Render(camera);
                Color edge=lit.GetPixel(13,28),inside=lit.GetPixel(29,28);
                var expectedEdge = ExpectedReferenceEdge(baseline.GetPixel(13,28),13,28,light,profile);
                Assert.That(edge.r,Is.EqualTo(expectedEdge.r).Within(.004f));
                Assert.That(edge.g,Is.EqualTo(expectedEdge.g).Within(.004f),"The rendered contour must match the recovered light-field and Gamma blend formula.");
                Assert.That(edge.g,Is.GreaterThan(edge.r+.02f),"The chosen green light must visibly tint the contour.");
                if(verifyReference)
                {
                    var body=baseline.GetPixel(29,28);
                    Assert.That(inside.r,Is.EqualTo(body.r).Within(.0001f));
                    Assert.That(inside.g,Is.EqualTo(body.g).Within(.0001f));
                    Assert.That(inside.b,Is.EqualTo(body.b).Within(.0001f),
                        "White source art on a dark material must retain its shaded body, even when a rim is active.");
                    VerifyReferenceDarkContours(camera,actor,material,lighting,light,profile);
                }
                else Assert.That(inside.g,Is.LessThan(baseline.GetPixel(29,28).g),"The legacy local exposure remains compatible for custom profiles.");
                Assert.That(lit.GetPixel(5,5).r,Is.EqualTo(baseline.GetPixel(5,5).r).Within(.001f),"Background pixels must not be recolored.");
                Assert.That(lit.GetPixel(13,16).g,Is.EqualTo(baseline.GetPixel(13,16).g).Within(.001f),"A foreground occluder must not receive the hidden actor's rim.");
                lampHost.transform.position=new Vector3(0,.5f,0);lighting.UpdateLighting();upper=Render(camera);
                var expectedUpper = ExpectedReferenceEdge(baseline.GetPixel(29,45),29,45,light,profile);
                Assert.That(upper.GetPixel(29,45).g,Is.EqualTo(expectedUpper.g).Within(.004f),"Upper-world field/depth UVs must stay aligned.");
                Assert.That(upper.GetPixel(29,45).g,Is.GreaterThan(upper.GetPixel(29,9).g+.02f),"Upper-world light must reach the upper contour.");
                if (verifyWrap)
                {
                    profile.WeakDistance=profile.StrongDistance=1;profile.StrongScale=0;
                    profile.InnerWrapWidth=2;profile.InnerWrapStrength=0;
                    lighting.UpdateLighting();var sharp=Render(camera);Texture2D wrapped=null,offAgain=null;
                    try
                    {
                        profile.InnerWrapStrength=.15f;lighting.UpdateLighting();wrapped=Render(camera);
                        Assert.That(wrapped.GetPixel(29,45).g,Is.EqualTo(sharp.GetPixel(29,45).g).Within(.001f),
                            "The bright outer rim must retain its original color and width.");
                        float near=wrapped.GetPixel(29,41).g-sharp.GetPixel(29,41).g;
                        float next=wrapped.GetPixel(29,37).g-sharp.GetPixel(29,37).g;
                        Assert.That(near,Is.GreaterThan(next+.0001f),"Light wrap must fade inward across art cells.");
                        Assert.That(next,Is.GreaterThan(.0001f),"The second art cell must receive a weak transition.");
                        Assert.That(wrapped.GetPixel(29,33).g,Is.EqualTo(sharp.GetPixel(29,33).g).Within(.001f),
                            "The deep interior must retain its original artwork shading.");
                        profile.InnerWrapStrength=0;lighting.UpdateLighting();offAgain=Render(camera);
                        AssertSameImage(sharp,offAgain,"Disabling only inner wrap must restore the sharp-rim image in the same frame.");
                    }
                    finally {Object.DestroyImmediate(sharp);if(wrapped)Object.DestroyImmediate(wrapped);if(offAgain)Object.DestroyImmediate(offAgain);}
                    profile.InnerWrapStrength=.15f;lighting.UpdateLighting();
                    Object.DestroyImmediate(upper);upper=Render(camera);
                }
                if(verifySpatial) VerifySpatialAndTransparentSources(camera,actor,occluder,lighting,light,profile,texture,material);
                var scaledSprite=Sprite.Create(texture,new Rect(0,0,16,16),Vector2.one*.5f,32,0,SpriteMeshType.FullRect);
                var enlarged=new Texture2D(32,32,TextureFormat.RGBA32,false,true){filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                var enlargedColors=new Color[1024];for(int y=0;y<32;y++)for(int x=0;x<32;x++)enlargedColors[y*32+x]=colors[(y/2)*16+x/2];enlarged.SetPixels(enlargedColors);enlarged.Apply();
                var enlargedSprite=Sprite.Create(enlarged,new Rect(0,0,32,32),Vector2.one*.5f,32,0,SpriteMeshType.FullRect);
                Texture2D scaledImage=null,enlargedImage=null;SpriteRimPixelGrid artGrid=null;
                try {
                    actor.sprite=scaledSprite;actorHost.transform.localScale=Vector3.one*2;lighting.UpdateLighting();scaledImage=Render(camera);
                    AssertSameImage(upper,scaledImage,"Changing Sprite PPU and compensating object scale must preserve the art-pixel rim.");
                    actorHost.transform.localScale=Vector3.one;actor.sprite=enlargedSprite;material.mainTexture=enlarged;
                    artGrid=actorHost.AddComponent<SpriteRimPixelGrid>();artGrid.TextureTexelsPerArtPixel=Vector2Int.one*2;lighting.UpdateLighting();enlargedImage=Render(camera);
                    AssertSameImage(upper,enlargedImage,"A 2x enlarged source texture must preserve one art-pixel sampling with explicit grid metadata.");
                } finally {
                    actor.sprite=sprite;material.mainTexture=texture;actorHost.transform.localScale=Vector3.one;
                    if(artGrid)Object.DestroyImmediate(artGrid);if(scaledImage)Object.DestroyImmediate(scaledImage);if(enlargedImage)Object.DestroyImmediate(enlargedImage);
                    Object.DestroyImmediate(scaledSprite);Object.DestroyImmediate(enlargedSprite);Object.DestroyImmediate(enlarged);lighting.UpdateLighting();
                }

                {
                    // Regression: a tiny strongest-light change must not move all tint to the opposite contour.
                    var secondLamp=new GameObject("Synthetic opposing blue light");SceneManager.MoveGameObjectToScene(secondLamp,preview);
                    Texture2D balanced=null,crossed=null,directional=null,spot=null,saturated=null;
                    try
                    {
                        var blue=secondLamp.AddComponent<SpriteRimLight>();blue.Radius=Vector2.one*2;
                        blue.LightColor=new Color(.05f,.05f,.85f,1);secondLamp.transform.position=new Vector3(.501f,0,0);
                        light.LightColor=new Color(.85f,.05f,.05f,1);lampHost.transform.position=new Vector3(-.499f,0,0);
                        profile.WeakDistance=profile.StrongDistance=1;profile.StrongScale=0;
                        lighting.Configure(new Renderer[]{actor},new[]{light,blue},profile);lighting.UpdateLighting();
                        balanced=Render(camera);
                        var left=balanced.GetPixel(13,28);var right=balanced.GetPixel(42,28);
                        Assert.That(left.r,Is.GreaterThan(left.b+.02f),"The left contour must receive the red light rather than the opposing blue field.");
                        Assert.That(right.b,Is.GreaterThan(right.r+.02f),"Both opposed contours must remain lit at the same time.");
                        lampHost.transform.position=new Vector3(-.501f,0,0);secondLamp.transform.position=new Vector3(.499f,0,0);
                        lighting.UpdateLighting();crossed=Render(camera);
                        AssertImageDelta(balanced,crossed,.01f,"Crossing the old strongest-light boundary must not flip either contour.");
                        blue.enabled=false;light.Shape=LightType.Directional;light.LightColor=new Color(.15f,.85f,.25f,1);
                        lampHost.transform.position=new Vector3(0,.5f,0);lighting.UpdateLighting();directional=Render(camera);
                        var top=directional.GetPixel(29,45);
                        Assert.That(top.g,Is.GreaterThan(top.r+.02f),"The directional half-field must reach the upper art contour.");
                        light.Shape=LightType.Spot;light.Intensity=1; // Keep the cone-direction check below color saturation.
                        lampHost.transform.position=new Vector3(0,.5f,0);
                        Assert.That(light.TryGetInfluence(Vector3.zero,out _,out _),Is.True,
                            "Selection must use the same bright cone center as the rendered light.");
                        lighting.UpdateLighting();spot=Render(camera);top=spot.GetPixel(31,45);
                        Assert.That(top.g,Is.GreaterThan(top.r+.02f),"The bright spot center axis must receive light on the enhanced route.");
                        Assert.That(float.IsFinite(top.r)&&float.IsFinite(top.g)&&float.IsFinite(top.b),Is.True);
                        light.Shape=LightType.Point;light.Intensity=4;light.LightColor=Color.blue;
                        lampHost.transform.position=new Vector3(-.5f,0,0);lighting.UpdateLighting();saturated=Render(camera);
                        var blueEdge=saturated.GetPixel(13,28);
                        Assert.That(blueEdge.b,Is.GreaterThan(blueEdge.r+.1f),"A saturated blue contour must use its blue channel instead of the reference shader's red-channel substitution.");
                    }
                    finally
                    {
                        if(balanced)Object.DestroyImmediate(balanced);if(crossed)Object.DestroyImmediate(crossed);
                        if(directional)Object.DestroyImmediate(directional);if(spot)Object.DestroyImmediate(spot);
                        if(saturated)Object.DestroyImmediate(saturated);
                        Object.DestroyImmediate(secondLamp);
                    }
                }

                if (verifyNative)
                {
                    lampHost.SetActive(false);
                    lighting.Configure(new Renderer[] { actor }, profile);
                    profile.MinimumExposure=1;profile.InnerWrapStrength=0;
                    var nativeHost=new GameObject("Synthetic native scene light");SceneManager.MoveGameObjectToScene(nativeHost,preview);
                    var native=nativeHost.AddComponent<Light>();native.type=LightType.Point;native.color=Color.green;
                    native.range=4;native.intensity=1;native.cullingMask=1<<30;native.renderMode=LightRenderMode.ForcePixel;
                    nativeHost.transform.position=new Vector3(-.5f,0,-.4f);
                    Texture2D nativeImage=null,maskedImage=null,offImage=null,directionalImage=null,spotImage=null,unmarkedImage=null,unselectedImage=null,hdrImage=null;
                    try
                    {
                        unmarkedImage=Render(camera);
                        Assert.That(lighting.ActiveTargetCount,Is.Zero,"An ordinary native light must not create an implicit actor rim.");
                        AssertSameImage(baseline,unmarkedImage,"Unmarked scene lighting must retain the original image.");
                        var participation=nativeHost.AddComponent<SpriteRimLightSource>();
                        nativeImage=Render(camera);
                        Assert.That(lighting.ActiveTargetCount,Is.EqualTo(1),"Native URP visible lights enroll the actor without a 2D-light component.");
                        Assert.That(nativeImage.GetPixel(13,28).g,Is.GreaterThan(nativeImage.GetPixel(13,28).r+.01f),"A native point light must tint the near contour.");
                        VerifyDarkEdgeFill(camera, material, native, participation, profile);
                        VerifyNativeOverlap(camera, material, native, participation, profile);
                        yield return VerifyReferenceKeyFill(camera, actor, material, native, participation, profile);
                        VerifyNativeBacklight(camera, actor, native, participation, profile);
                        VerifyRearContourWrap(camera, actor, material, native, participation, profile, pipeline);
                        VerifyLocalShadows(camera, material, native, participation, profile, pipeline);
                        VerifyDarkContourStyle(camera, material, native, participation, profile);
                        if (verifyContour) VerifyContourCore(camera, actor, texture, material, native, participation, profile, pipeline);
                        participation.enabled=false;unselectedImage=Render(camera);
                        Assert.That(lighting.ActiveTargetCount,Is.Zero,"The source component checkbox must remove participation immediately.");
                        AssertSameImage(baseline,unselectedImage,"Disabling rim participation must leave native source lighting alone.");
                        participation.enabled=true;
                        native.cullingMask=0;maskedImage=Render(camera);
                        Assert.That(lighting.ActiveTargetCount,Is.Zero,"The native light's culling mask must be honored.");
                        AssertSameImage(baseline,maskedImage,"Masked scene lights must leave the source image unchanged.");
                        native.cullingMask=1<<30;native.type=LightType.Directional;
                        native.color=Color.white;native.intensity=4;
                        // Regression: global daylight must never create a persistent actor rim.
                        foreach(var direction in new[]{new Vector3(-1,0,1),new Vector3(1,1,1)})
                        {
                            nativeHost.transform.rotation=Quaternion.LookRotation(direction);
                            directionalImage=Render(camera);
                            Assert.That(lighting.ActiveTargetCount,Is.Zero,"Directional sky light is excluded from local rim lighting.");
                            AssertSameImage(baseline,directionalImage,"Even strong white daylight must preserve the source image without adding a rim.");
                            Object.DestroyImmediate(directionalImage);directionalImage=null;
                        }
                        native.type=LightType.Spot;native.color=Color.green;native.intensity=1;native.spotAngle=70;native.innerSpotAngle=50;
                        nativeHost.transform.position=new Vector3(0,.7f,-.7f);
                        nativeHost.transform.rotation=Quaternion.LookRotation(-nativeHost.transform.position);
                        spotImage=Render(camera);Assert.That(lighting.ActiveTargetCount,Is.EqualTo(1));
                        Assert.That(spotImage.GetPixel(31,45).g,Is.GreaterThan(spotImage.GetPixel(31,45).r+.01f),"A native spot cone must light its center axis.");
                        native.type=LightType.Point;native.color=new Color(.1f,.4f,1);native.intensity=40;
                        nativeHost.transform.position=new Vector3(-.5f,0,-.4f);
                        hdrImage=Render(camera);var hdrEdge=hdrImage.GetPixel(13,28);
                        Assert.That(lighting.ActiveTargetCount,Is.EqualTo(1));
                        Assert.That(hdrEdge.b,Is.GreaterThan(hdrEdge.r+.05f),"Strong blue light must remain blue instead of saturating every channel to white.");
                        Object.DestroyImmediate(participation);participation=null;
                        var removed=Render(camera);
                        try{Assert.That(lighting.ActiveTargetCount,Is.Zero);AssertSameImage(baseline,removed,"Removing the marker must restore ordinary scene lighting.");}
                        finally{Object.DestroyImmediate(removed);}
                        native.enabled=false;offImage=Render(camera);
                        Assert.That(lighting.ActiveTargetCount,Is.Zero);AssertSameImage(baseline,offImage,"Disabling a scene light removes its rim in the same frame.");
                    }
                    finally
                    {
                        if(nativeImage)Object.DestroyImmediate(nativeImage);if(maskedImage)Object.DestroyImmediate(maskedImage);
                        if(unmarkedImage)Object.DestroyImmediate(unmarkedImage);if(unselectedImage)Object.DestroyImmediate(unselectedImage);if(hdrImage)Object.DestroyImmediate(hdrImage);
                        if(directionalImage)Object.DestroyImmediate(directionalImage);if(spotImage)Object.DestroyImmediate(spotImage);if(offImage)Object.DestroyImmediate(offImage);
                        Object.DestroyImmediate(nativeHost);
                    }
                }

                actor.GetPropertyBlock(properties);Assert.That(properties.GetFloat(otherId),Is.EqualTo(7));
                Assert.That(properties.HasFloat(Shader.PropertyToID("_RimPreviewEnabled")),Is.False,"An unrelated shader must not receive old tint properties.");
                lighting.enabled=false;restored=Render(camera);
                var before=baseline.GetPixels();var after=restored.GetPixels();
                for(int i=0;i<before.Length;i++)
                {
                    Assert.That(after[i].r,Is.EqualTo(before[i].r).Within(.001f));Assert.That(after[i].g,Is.EqualTo(before[i].g).Within(.001f));
                    Assert.That(after[i].b,Is.EqualTo(before[i].b).Within(.001f));
                }
                Assert.That(actor.sharedMaterial,Is.SameAs(material));
            }
            finally
            {
                if(occluder)Object.DestroyImmediate(occluder);if(actorHost)Object.DestroyImmediate(actorHost);if(lampHost)Object.DestroyImmediate(lampHost);if(cameraHost)Object.DestroyImmediate(cameraHost);
                if (preview.IsValid() && preview.isLoaded) SceneManager.UnloadSceneAsync(preview);
                QualitySettings.renderPipeline=previous;
                if(occluderMaterial)Object.Destroy(occluderMaterial);if(profile)Object.Destroy(profile);if(material)Object.Destroy(material);if(sprite)Object.Destroy(sprite);if(texture)Object.Destroy(texture);
                if(feature)Object.DestroyImmediate(feature);if(renderer)Object.DestroyImmediate(renderer);if(pipeline)Object.DestroyImmediate(pipeline);
                if(baseline)Object.Destroy(baseline);if(lit)Object.Destroy(lit);if(upper)Object.Destroy(upper);if(restored)Object.Destroy(restored);
            }
            yield return null;
        }
        private static void VerifyLocalShadows(Camera camera, Material material, Light light,
            SpriteRimLightSource source, SpriteRimLightingProfile profile, UniversalRenderPipelineAsset pipeline)
        {
            var position = light.transform.position;
            var rotation = light.transform.rotation;
            var oldColor = material.GetVector("_BaseColor");
            float fill = source.DarkEdgeFill, exposure = profile.MinimumExposure;
            var caster = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(caster, camera.scene);
            caster.name = "Opaque door between light and actor";
            caster.layer = 30;
            caster.transform.position = new Vector3(-.28f, -.02f, .5f);
            caster.transform.localScale = new Vector3(.85f, 1.1f, .08f);
            var mesh = caster.GetComponent<MeshRenderer>();
            var shadowMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mesh.sharedMaterial = shadowMaterial;
            mesh.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            Texture2D unmarked = null;
            var realtimeShadows = typeof(UniversalRenderPipelineAsset).GetProperty(nameof(UniversalRenderPipelineAsset.supportsAdditionalLightShadows));
            try
            {
                // Keep the full synthetic receiver on one point-shadow face.
                light.transform.position = new Vector3(-.55f, .06f, 1f);
                light.transform.rotation = Quaternion.LookRotation(-light.transform.position);
                light.shadows = LightShadows.Hard;
                light.shadowStrength = 1;
                light.shadowBias = .02f;
                light.shadowNormalBias = .02f;
                light.shadowNearPlane = .05f;
                light.spotAngle = 90;
                light.innerSpotAngle = 76;
                source.DarkEdgeFill = .18f;
                profile.MinimumExposure = .85f;
                material.SetVector("_BaseColor", new Vector4(.06f, .06f, .06f, 1));

                source.enabled = false;
                unmarked = Render(camera);
                source.enabled = true;
                foreach (bool useRealtime in new[] { true, false })
                {
                    realtimeShadows.SetValue(pipeline, useRealtime);
                    source.FallbackOccluder = useRealtime ? null : mesh;
                    foreach (var type in new[] { LightType.Point, LightType.Spot })
                    {
                        light.type = type;
                        Texture2D open = null, closed = null, opened = null;
                        try
                        {
                            caster.SetActive(false);
                            open = Render(camera);
                            Assert.That(open.GetPixel(13, 28).g, Is.GreaterThan(unmarked.GetPixel(13, 28).g + .01f),
                                "A clear path from the " + type + " light must add visible dark-edge fill.");
                            caster.SetActive(true);
                            closed = Render(camera);
                            float clearGain = open.GetPixel(13, 28).g - unmarked.GetPixel(13, 28).g;
                            float blockedGain = Mathf.Abs(closed.GetPixel(13, 28).g - unmarked.GetPixel(13, 28).g);
                            Assert.That(blockedGain, Is.LessThan(clearGain * .15f),
                                $"The covered {type} contour must return close to its unmarked color (clear {clearGain}, covered {blockedGain}).");
                            float clearChange = MeanColorChange(unmarked, open);
                            float blockedChange = MeanColorChange(unmarked, closed);
                            Assert.That(blockedChange, Is.LessThan(clearChange * .2f),
                                $"The {type} wall must remove most of the rim across the sprite (clear {clearChange}, covered {blockedChange}).");
                            caster.SetActive(false);
                            opened = Render(camera);
                            AssertSameImage(open, opened, "Opening the light path must restore the same rim immediately.");
                        }
                        finally
                        {
                            if (open) Object.DestroyImmediate(open);
                            if (closed) Object.DestroyImmediate(closed);
                            if (opened) Object.DestroyImmediate(opened);
                        }
                    }
                }
            }
            finally
            {
                realtimeShadows.SetValue(pipeline, true);
                source.FallbackOccluder = null;
                if (unmarked) Object.DestroyImmediate(unmarked);
                Object.DestroyImmediate(caster);
                Object.DestroyImmediate(shadowMaterial);
                light.type = LightType.Point;
                light.shadows = LightShadows.None;
                light.transform.SetPositionAndRotation(position, rotation);
                source.enabled = true;
                source.DarkEdgeFill = fill;
                profile.MinimumExposure = exposure;
                material.SetVector("_BaseColor", oldColor);
            }
        }

        private static void VerifyDarkContourStyle(Camera camera, Material material, Light light,
            SpriteRimLightSource source, SpriteRimLightingProfile profile)
        {
            var oldColor = material.GetVector("_BaseColor");
            var oldLightColor = light.color;
            float intensity = light.intensity, fill = source.DarkEdgeFill;
            float cap = profile.DarkEdgeBrightness, wrap = profile.InnerWrapStrength;
            float weak = profile.WeakDistance, strong = profile.StrongDistance;
            float scale = profile.StrongScale, exposure = profile.MinimumExposure;
            Texture2D old = null, brighter = null, wrapped = null, brightBase = null, brightLit = null;
            try
            {
                material.SetVector("_BaseColor", new Vector4(.025f, .025f, .025f, 1));
                light.color = new Color(1f, .45f, .1f);
                light.intensity = 5f;
                source.DarkEdgeFill = .18f;
                profile.WeakDistance = profile.StrongDistance = 1;
                profile.StrongScale = 0;
                profile.MinimumExposure = 1;
                profile.InnerWrapStrength = 0;
                profile.DarkEdgeBrightness = .25f;
                old = Render(camera);
                profile.DarkEdgeBrightness = .42f;
                brighter = Render(camera);
                Assert.That(brighter.GetPixel(13, 28).r, Is.GreaterThan(old.GetPixel(13, 28).r + .05f),
                    "The configurable dark-art cap must visibly brighten the same native-light contour.");
                profile.InnerWrapStrength = .22f;
                wrapped = Render(camera);
                float near = wrapped.GetPixel(17, 28).r - brighter.GetPixel(17, 28).r;
                float next = wrapped.GetPixel(21, 28).r - brighter.GetPixel(21, 28).r;
                Assert.That(near, Is.GreaterThan(next + .0001f), "Warm light must fade inward from the first art cell.");
                Assert.That(next, Is.GreaterThan(.0001f), "The second art cell must receive a visible inward contribution.");
                Assert.That(wrapped.GetPixel(29, 28).r, Is.EqualTo(brighter.GetPixel(29, 28).r).Within(.001f),
                    "The deeper interior must retain its original shading.");

                material.SetVector("_BaseColor", new Vector4(.92f, .92f, .92f, 1));
                source.enabled = false;
                brightBase = Render(camera);
                source.enabled = true;
                brightLit = Render(camera);
                Color before = brightBase.GetPixel(13, 28), after = brightLit.GetPixel(13, 28);
                Assert.That(Mathf.Max(after.r, Mathf.Max(after.g, after.b)),
                    Is.LessThanOrEqualTo(Mathf.Max(before.r, Mathf.Max(before.g, before.b)) + .004f),
                    "The brighter dark contour must retain the original peak of bright hair or clothing.");
            }
            finally
            {
                if (old) Object.DestroyImmediate(old);
                if (brighter) Object.DestroyImmediate(brighter);
                if (wrapped) Object.DestroyImmediate(wrapped);
                if (brightBase) Object.DestroyImmediate(brightBase);
                if (brightLit) Object.DestroyImmediate(brightLit);
                material.SetVector("_BaseColor", oldColor);
                light.color = oldLightColor;
                light.intensity = intensity;
                source.DarkEdgeFill = fill;
                source.enabled = true;
                profile.DarkEdgeBrightness = cap;
                profile.InnerWrapStrength = wrap;
                profile.WeakDistance = weak;
                profile.StrongDistance = strong;
                profile.StrongScale = scale;
                profile.MinimumExposure = exposure;
            }
        }

        private static void VerifyContourCore(Camera camera, Renderer actor, Texture2D texture, Material material,
            Light light, SpriteRimLightSource source, SpriteRimLightingProfile profile, UniversalRenderPipelineAsset pipeline)
        {
            var pixels = texture.GetPixels(); var oldColor = material.GetVector("_BaseColor");
            var position = light.transform.position; var color = light.color;
            float intensity = light.intensity, fill = source.DarkEdgeFill;
            float coreStrength = profile.ContourStrength, wrap = profile.InnerWrapStrength;
            float weak = profile.WeakDistance, strong = profile.StrongDistance, scale = profile.StrongScale;
            bool hdr = pipeline.supportsHDR;
            Texture2D soft = null, crisp = null, restored = null, hdrBase = null, hdrLit = null;
            try
            {
                pipeline.supportsHDR = true;
                material.SetVector("_BaseColor", new Vector4(.1f, .1f, .1f, 1));
                light.transform.position = actor.bounds.center + new Vector3(-.4f, 0, .3f);
                light.color = Color.green; light.intensity = 1.3f; source.DarkEdgeFill = 0;
                profile.WeakDistance = profile.StrongDistance = 1; profile.StrongScale = 0;
                profile.InnerWrapStrength = 0; profile.ContourStrength = 0;
                var detail = (Color[])pixels.Clone();
                for (int y = 2; y <= 6; y++) detail[y * 16 + 7] = Color.clear;
                detail[9 * 16 + 13] = Color.white;
                texture.SetPixels(detail); texture.Apply();
                soft = Render(camera);
                profile.ContourStrength = .85f; crisp = Render(camera);
                Assert.That(crisp.GetPixel(34, 18).g, Is.GreaterThan(soft.GetPixel(34, 18).g + .05f),
                    "The inner edge of a separated leg must receive a clear art-cell contour.");
                Assert.That(crisp.GetPixel(54, 38).g, Is.GreaterThan(soft.GetPixel(54, 38).g + .05f),
                    "An isolated art cell must not disappear into the filtered weak-light response.");
                Assert.That(crisp.GetPixel(26, 34).g, Is.EqualTo(soft.GetPixel(26, 34).g).Within(.003f),
                    "The clear contour must not spread into the opaque body interior.");
                Assert.That(crisp.GetPixel(30, 18).g, Is.EqualTo(soft.GetPixel(30, 18).g).Within(.003f),
                    "The transparent gap itself must retain the background.");
                foreach (var pixel in crisp.GetPixels())
                    Assert.That(Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)), Is.LessThanOrEqualTo(1.001f),
                        "A clear contour adds no new HDR energy to LDR art.");
                profile.ContourStrength = 0; restored = Render(camera);
                AssertSameImage(soft, restored, "Zero contour strength must restore the previous soft response.");

                profile.ContourStrength = .85f;
                VerifyContourMaterialValues(camera, material, light);
                VerifyNativeContourEnergy(camera, material, light, source);
                VerifyNativeContourColorHandoff(camera, material, light, source);
                material.SetVector("_BaseColor", new Vector4(2.4f, .6f, .1f, 1));
                source.enabled = false; hdrBase = Render(camera); source.enabled = true; hdrLit = Render(camera);
                Assert.That(hdrBase.GetPixel(34, 18).r, Is.GreaterThan(1.5f), "The test must contain actual incoming HDR.");
                Assert.That(hdrLit.GetPixel(34, 18).r, Is.EqualTo(hdrBase.GetPixel(34, 18).r).Within(.015f),
                    "The contour must preserve the existing HDR source instead of adding emission to it.");

                texture.SetPixels(pixels); texture.Apply();
                material.SetVector("_BaseColor", new Vector4(.04f, .04f, .04f, 1));
                VerifyNativeBacklight(camera, actor, light, source, profile);
                VerifyLocalShadows(camera, material, light, source, profile, pipeline);
            }
            finally
            {
                texture.SetPixels(pixels); texture.Apply(); material.SetVector("_BaseColor", oldColor);
                light.transform.position = position; light.color = color; light.intensity = intensity;
                source.enabled = true; source.DarkEdgeFill = fill;
                profile.ContourStrength = coreStrength; profile.InnerWrapStrength = wrap;
                profile.WeakDistance = weak; profile.StrongDistance = strong; profile.StrongScale = scale;
                pipeline.supportsHDR = hdr;
                foreach (var image in new[] { soft, crisp, restored, hdrBase, hdrLit }) if (image) Object.DestroyImmediate(image);
            }
        }

        private static void VerifyNativeContourEnergy(Camera camera, Material material, Light light, SpriteRimLightSource source)
        {
            var savedColor = material.GetVector("_BaseColor"); var savedLight = light.color;
            float savedIntensity = light.intensity;
            Texture2D baseline = null, weak = null, duplicate = null, opposed = null;
            GameObject duplicateHost = null;
            try
            {
                // The shader Color property is converted from sRGB: .35 reads back
                // near .1 linear. Verify that this is a midtone, not the dark-art case.
                material.SetVector("_BaseColor", new Vector4(.35f, .35f, .35f, 1));
                light.color = Color.green; light.intensity = .35f;
                source.enabled = false; baseline = Render(camera); source.enabled = true; weak = Render(camera);
                Assert.That(baseline.GetPixel(34, 18).g, Is.InRange(.08f, .15f), "Calibrate the weak-core fixture to a linear midtone.");
                Assert.That(weak.GetPixel(34, 18).g, Is.GreaterThan(baseline.GetPixel(34, 18).g + .025f),
                    "Weak native light must retain a readable core without repeated energy attenuation.");
                Assert.That(weak.GetPixel(26, 34).g, Is.EqualTo(baseline.GetPixel(26, 34).g).Within(.003f),
                    "Core brightness must not raise the opaque body interior.");

                duplicateHost = new GameObject("Duplicate native contour light");
                SceneManager.MoveGameObjectToScene(duplicateHost, light.gameObject.scene);
                var second = duplicateHost.AddComponent<Light>(); second.type = light.type;
                second.transform.SetPositionAndRotation(light.transform.position, light.transform.rotation);
                second.range = light.range; second.color = light.color; second.intensity = light.intensity;
                second.cullingMask = light.cullingMask; second.shadows = LightShadows.None;
                duplicateHost.AddComponent<SpriteRimLightSource>(); duplicate = Render(camera);
                AssertSameImage(weak, duplicate, "Duplicating a native light must not double the core or wash its color out.");

                light.intensity = second.intensity = 1.3f; second.color = Color.blue;
                second.transform.position += Vector3.right * .8f; opposed = Render(camera);
                int colored = 0;
                for (int y = 0; y < opposed.height; y++) for (int x = 0; x < opposed.width; x++)
                {
                    var value = opposed.GetPixel(x, y); var original = baseline.GetPixel(x, y);
                    Assert.That(Mathf.Max(value.r, Mathf.Max(value.g, value.b)), Is.LessThanOrEqualTo(1.001f),
                        "Opposing colored cores must remain bounded for LDR art.");
                    Assert.That(value.r, Is.LessThanOrEqualTo(original.r + .006f),
                        "Green and blue lights must not introduce a white red-channel contribution.");
                    if (value.g + value.b > original.g + original.b + .05f) colored++;
                }
                Assert.That(colored, Is.GreaterThan(3), "The opposing-light check must contain actual illuminated contour pixels.");
            }
            finally
            {
                if (duplicateHost) Object.DestroyImmediate(duplicateHost);
                material.SetVector("_BaseColor", savedColor); light.color = savedLight; light.intensity = savedIntensity;
                source.enabled = true;
                foreach (var image in new[] { baseline, weak, duplicate, opposed }) if (image) Object.DestroyImmediate(image);
            }
        }

        private static void VerifyNativeContourColorHandoff(Camera camera, Material material, Light light,
            SpriteRimLightSource source)
        {
            var savedColor = material.GetVector("_BaseColor"); var savedLight = light.color;
            float savedIntensity = light.intensity, savedFill = source.DarkEdgeFill;
            var hosts = new List<GameObject>(); var extraLights = new List<Light>();
            var extraSources = new List<SpriteRimLightSource>(); var images = new List<Texture2D>();
            Texture2D Capture() { var image = Render(camera); images.Add(image); return image; }
            try
            {
                material.SetVector("_BaseColor", new Vector4(.35f, .35f, .35f, 1));
                light.color = Color.green; light.intensity = 1.3f; source.DarkEdgeFill = 0;
                for (int i = 0; i < 3; i++)
                {
                    var host = new GameObject("Native color handoff " + i); hosts.Add(host);
                    SceneManager.MoveGameObjectToScene(host, light.gameObject.scene);
                    var extra = host.AddComponent<Light>(); extra.type = light.type;
                    extra.transform.SetPositionAndRotation(light.transform.position, light.transform.rotation);
                    extra.range = light.range; extra.cullingMask = light.cullingMask; extra.shadows = LightShadows.None;
                    var participation = host.AddComponent<SpriteRimLightSource>(); participation.enabled = false;
                    extraLights.Add(extra); extraSources.Add(participation);
                }
                var green = Capture();
                extraLights[0].color = Color.blue; extraSources[0].enabled = true;
                float previousBlue = green.GetPixel(34, 18).b;
                for (int i = 0; i <= 5; i++)
                {
                    extraLights[0].intensity = .065f * i;
                    var mixed = Capture(); var cell = mixed.GetPixel(34, 18);
                    Assert.That(cell.b, Is.GreaterThanOrEqualTo(previousBlue - .001f),
                        "A weaker colored lamp must enter continuously without a reverse color step.");
                    Assert.That(cell.b - previousBlue, Is.LessThan(.02f),
                        "Adjacent light strengths must not cause an abrupt color handoff.");
                    Assert.That(cell.g, Is.GreaterThanOrEqualTo(green.GetPixel(34, 18).g - .003f),
                        "A weaker blue lamp must not dim the dominant green core.");
                    previousBlue = cell.b;
                }
                Assert.That(previousBlue, Is.GreaterThan(green.GetPixel(34, 18).b + .018f),
                    "A quarter-strength secondary color must remain visible on the same contour cell.");

                extraLights[1].color = Color.red; extraLights[1].intensity = .39f;
                extraLights[2].color = new Color(.25f, .05f, 1f); extraLights[2].intensity = .2f;
                extraSources[1].enabled = extraSources[2].enabled = true;
                var four = Capture();
                var firstColor = light.color; float firstIntensity = light.intensity;
                light.color = extraLights[2].color; light.intensity = extraLights[2].intensity;
                extraLights[2].color = firstColor; extraLights[2].intensity = firstIntensity;
                var secondColor = extraLights[0].color; float secondIntensity = extraLights[0].intensity;
                extraLights[0].color = extraLights[1].color; extraLights[0].intensity = extraLights[1].intensity;
                extraLights[1].color = secondColor; extraLights[1].intensity = secondIntensity;
                var reordered = Capture();
                AssertSameImage(four, reordered, "The same four colored lamps must keep their result when light order changes.");
                Assert.That(four.GetPixel(26, 34).b, Is.EqualTo(green.GetPixel(26, 34).b).Within(.003f),
                    "The multi-light color handoff must leave the deeper body interior unchanged.");
                foreach (var pixel in four.GetPixels())
                    Assert.That(Mathf.Max(pixel.r, Mathf.Max(pixel.g, pixel.b)), Is.LessThanOrEqualTo(1.001f),
                        "Four colored cores must not add HDR emission to LDR art.");
            }
            finally
            {
                foreach (var host in hosts) if (host) Object.DestroyImmediate(host);
                foreach (var image in images) if (image) Object.DestroyImmediate(image);
                material.SetVector("_BaseColor", savedColor); light.color = savedLight;
                light.intensity = savedIntensity; source.DarkEdgeFill = savedFill;
            }
        }

        private static void VerifyContourMaterialValues(Camera camera, Material material, Light light)
        {
            var savedColor = material.GetVector("_BaseColor"); var savedLight = light.color;
            float savedIntensity = light.intensity;
            Texture2D black = null, dark = null, pale = null, painted = null;
            try
            {
                light.color = Color.white;
                material.SetVector("_BaseColor", new Vector4(0, 0, 0, 1)); black = Render(camera);
                material.SetVector("_BaseColor", new Vector4(.003f, .003f, .003f, 1)); dark = Render(camera);
                material.SetVector("_BaseColor", new Vector4(.12f, .12f, .12f, 1)); pale = Render(camera);
                light.intensity = .65f;
                material.SetVector("_BaseColor", new Vector4(.2f, .03f, .003f, 1)); painted = Render(camera);
                var zero = black.GetPixel(34, 18); var low = dark.GetPixel(34, 18);
                var high = pale.GetPixel(34, 18); var hue = painted.GetPixel(34, 18);
                Assert.That(zero.g, Is.InRange(.02f, .035f), "Black art needs a readable bounded floor, distinct from pale material.");
                Assert.That(low.g, Is.LessThan(high.g * .35f), "Dark and pale cloth must retain visibly different contour values under identical light.");
                Assert.That(hue.r, Is.GreaterThan(hue.g * 1.5f), "Neutral light must retain the painted red channel.");
                Assert.That(hue.g, Is.GreaterThan(hue.b * 1.5f), "A dark blue channel must not become a uniform bright white contour.");
            }
            finally
            {
                material.SetVector("_BaseColor", savedColor); light.color = savedLight; light.intensity = savedIntensity;
                foreach (var image in new[] { black, dark, pale, painted }) if (image) Object.DestroyImmediate(image);
            }
        }

        private static float MeanColorChange(Texture2D before, Texture2D after)
        {
            var source = before.GetPixels();
            var result = after.GetPixels();
            float sum = 0;
            for (int i = 0; i < source.Length; i++)
                sum += Mathf.Abs(result[i].r - source[i].r) + Mathf.Abs(result[i].g - source[i].g) +
                    Mathf.Abs(result[i].b - source[i].b);
            return sum / source.Length;
        }

        private static IEnumerator VerifyReferenceKeyFill(Camera camera, Renderer actor, Material material,
            Light light, SpriteRimLightSource source, SpriteRimLightingProfile profile)
        {
            var position=light.transform.position;var color=light.color;var baseColor=material.GetVector("_BaseColor");
            float intensity=light.intensity,range=light.range,fill=source.DarkEdgeFill;
            var shadows=light.shadows;bool reference=profile.ReferenceEdgeResponse;
            float weak=profile.WeakDistance,strong=profile.StrongDistance,scale=profile.StrongScale;
            float wrap=profile.InnerWrapStrength,strength=profile.Strength,maximum=profile.MaximumLight,exposure=profile.MinimumExposure;
            GameObject otherHost=null;var images=new List<Texture2D>();
            Texture2D Capture(){var image=Render(camera);images.Add(image);return image;}
            try
            {
                profile.ReferenceEdgeResponse=true;profile.WeakDistance=profile.StrongDistance=1;
                profile.StrongScale=0;profile.InnerWrapStrength=0;profile.Strength=1;profile.MaximumLight=profile.MinimumExposure=1;
                light.color=Color.white;light.intensity=.9f;light.range=4;light.shadows=LightShadows.None;source.DarkEdgeFill=0;
                light.transform.position=actor.bounds.center+new Vector3(-.5f,0,2);
                material.SetVector("_BaseColor",new Vector4(.2f,.2f,.2f,1));
                source.enabled=false;var baseline=Capture();source.enabled=true;var single=Capture();
                Assert.That(single.GetPixel(29,9).r,Is.EqualTo(baseline.GetPixel(29,9).r).Within(.0001f),
                    "A horizontal rear light must not paint the underside of an opaque foot.");
                otherHost=new GameObject("Equal reference fill light");SceneManager.MoveGameObjectToScene(otherHost,light.gameObject.scene);
                var other=otherHost.AddComponent<Light>();other.type=LightType.Point;other.color=Color.white;
                other.intensity=light.intensity;other.range=light.range;other.cullingMask=light.cullingMask;other.renderMode=LightRenderMode.ForcePixel;
                otherHost.transform.position=actor.bounds.center+new Vector3(.5f,0,2);otherHost.AddComponent<SpriteRimLightSource>();
                var pair=Capture();
                float left=pair.GetPixel(13,28).r-baseline.GetPixel(13,28).r;
                float right=pair.GetPixel(42,28).r-baseline.GetPixel(42,28).r;
                Assert.That(left,Is.GreaterThan(right*1.5f),"Equal opposed lamps must retain a visibly stronger key side.");
                Assert.That(right,Is.GreaterThan(left*.2f),"The opposite contour remains a readable fill.");
                Assert.That(pair.GetPixel(29,9).r,Is.EqualTo(baseline.GetPixel(29,9).r).Within(.0001f));
                foreach(float multiplier in new[]{1.04f,.96f,1.03f})
                {
                    other.intensity=light.intensity*multiplier;yield return null;var stable=Capture();
                    float l=stable.GetPixel(13,28).r-baseline.GetPixel(13,28).r;
                    float r=stable.GetPixel(42,28).r-baseline.GetPixel(42,28).r;
                    Assert.That(l,Is.GreaterThan(r*1.3f),"Small intensity crossings must not exchange the key and fill sides.");
                }
                other.intensity=light.intensity*1.5f;Capture();yield return new WaitForSecondsRealtime(.2f);var promoted=Capture();
                Assert.That(promoted.GetPixel(42,28).r-baseline.GetPixel(42,28).r,
                    Is.GreaterThan((promoted.GetPixel(13,28).r-baseline.GetPixel(13,28).r)*1.5f),
                    "A clearly stronger source must take over after the short key-light handoff.");
                other.enabled=false;var restored=Capture();
                AssertSameImage(single,restored,"Removing the key immediately restores the remaining single-lamp response.");
                light.transform.position=actor.bounds.center+Vector3.forward*2;var centered=Capture();
                left=centered.GetPixel(13,28).r-baseline.GetPixel(13,28).r;
                right=centered.GetPixel(42,28).r-baseline.GetPixel(42,28).r;
                Assert.That(left,Is.GreaterThan(right*1.5f));Assert.That(right,Is.GreaterThan(.0001f));
                Assert.That(centered.GetPixel(29,9).r,Is.EqualTo(baseline.GetPixel(29,9).r).Within(.0001f));
                foreach(float x in new[]{-.005f,.005f})
                {
                    light.transform.position=actor.bounds.center+new Vector3(x,0,2);var nearby=Capture();
                    Assert.That(nearby.GetPixel(13,28).r,Is.EqualTo(centered.GetPixel(13,28).r).Within(.015f));
                    Assert.That(nearby.GetPixel(42,28).r,Is.EqualTo(centered.GetPixel(42,28).r).Within(.015f));
                }
                light.transform.position=actor.bounds.center+new Vector3(0,-.5f,2);var below=Capture();
                Assert.That(below.GetPixel(29,9).r,Is.GreaterThan(baseline.GetPixel(29,9).r+.001f),
                    "A source genuinely below the actor may illuminate the underside; this must not be a height-based foot exclusion.");
            }
            finally
            {
                if(otherHost)Object.DestroyImmediate(otherHost);foreach(var image in images)if(image)Object.DestroyImmediate(image);
                light.transform.position=position;light.color=color;light.intensity=intensity;light.range=range;light.shadows=shadows;
                source.enabled=true;source.DarkEdgeFill=fill;material.SetVector("_BaseColor",baseColor);
                profile.ReferenceEdgeResponse=reference;profile.WeakDistance=weak;profile.StrongDistance=strong;profile.StrongScale=scale;
                profile.InnerWrapStrength=wrap;profile.Strength=strength;profile.MaximumLight=maximum;profile.MinimumExposure=exposure;
            }
        }

        private static void VerifyNativeBacklight(Camera camera, Renderer actor, Light light, SpriteRimLightSource source, SpriteRimLightingProfile profile)
        {
            var position = light.transform.position;
            var color = light.color;
            var fill = source.DarkEdgeFill;
            var exposure = profile.MinimumExposure;
            var brightness = profile.DarkEdgeBrightness;
            Texture2D baseline = null, back = null;
            try
            {
                light.color = Color.green;
                profile.DarkEdgeBrightness = .25f;
                profile.MinimumExposure = 1;
                source.DarkEdgeFill = .22f;
                source.enabled = false;
                baseline = Render(camera);
                source.enabled = true;
                light.transform.position = actor.bounds.center + Vector3.forward * .7f;
                back = Render(camera);
                float left = back.GetPixel(13, 28).g - baseline.GetPixel(13, 28).g;
                float right = back.GetPixel(42, 28).g - baseline.GetPixel(42, 28).g;
                Assert.That(left, Is.GreaterThan(.01f), "A projected-center rear light keeps the left contour.");
                Assert.That(right, Is.GreaterThan(.01f), "A projected-center rear light keeps the right contour.");
                Assert.That(Mathf.Abs(left - right), Is.LessThan(.01f), "Backlight must not choose an arbitrary side at XY=0.");
                Assert.That(back.GetPixel(29, 28).g, Is.EqualTo(baseline.GetPixel(29, 28).g).Within(.004f), "The opaque body remains unchanged behind its contour.");
                foreach (float x in new[] { -.005f, .005f })
                {
                    light.transform.position = actor.bounds.center + new Vector3(x, 0, .7f);
                    var image = Render(camera);
                    try
                    {
                        Assert.That(image.GetPixel(13, 28).g, Is.EqualTo(back.GetPixel(13, 28).g).Within(.015f));
                        Assert.That(image.GetPixel(42, 28).g, Is.EqualTo(back.GetPixel(42, 28).g).Within(.015f));
                    }
                    finally { Object.DestroyImmediate(image); }
                }
                light.transform.position = actor.bounds.center + Vector3.back * .7f;
                var front = Render(camera);
                try
                {
                    Assert.That(front.GetPixel(13, 28).g - baseline.GetPixel(13, 28).g, Is.LessThan(left * .25f), "A centered front light is not a rear silhouette light.");
                    Assert.That(front.GetPixel(42, 28).g - baseline.GetPixel(42, 28).g, Is.LessThan(right * .25f));
                }
                finally { Object.DestroyImmediate(front); }
            }
            finally
            {
                source.enabled = true;
                source.DarkEdgeFill = fill;
                profile.MinimumExposure = exposure;
                profile.DarkEdgeBrightness = brightness;
                light.color = color;
                light.transform.position = position;
                if (baseline) Object.DestroyImmediate(baseline);
                if (back) Object.DestroyImmediate(back);
            }
        }

        private static void VerifyRearContourWrap(Camera camera, Renderer actor, Material material, Light light, SpriteRimLightSource source, SpriteRimLightingProfile profile, UniversalRenderPipelineAsset pipeline)
        {
            var position = light.transform.position; var color = light.color;
            float intensity = light.intensity, fill = source.DarkEdgeFill;
            var baseColor = material.GetVector("_BaseColor");
            float wrap = profile.InnerWrapStrength, cap = profile.DarkEdgeBrightness, exposure = profile.MinimumExposure;
            int width = profile.InnerWrapWidth;
            bool additionalShadows = pipeline.supportsAdditionalLightShadows;
            var realtimeShadows = typeof(UniversalRenderPipelineAsset).GetProperty(nameof(UniversalRenderPipelineAsset.supportsAdditionalLightShadows));
            Texture2D baseline = null, core = null, wrapped = null, singleSide = null, opposed = null, blocked = null, blockedOnly = null;
            GameObject opposedHost = null, caster = null;
            try
            {
                light.transform.position = actor.bounds.center + Vector3.forward * .7f;
                light.color = Color.green; light.intensity = 2.6f; source.DarkEdgeFill = .22f;
                material.SetVector("_BaseColor", new Vector4(.04f, .04f, .04f, 1));
                profile.MinimumExposure = 1; profile.DarkEdgeBrightness = .56f;
                profile.InnerWrapWidth = 2; profile.InnerWrapStrength = 0;
                source.enabled = false; baseline = Render(camera); source.enabled = true;
                core = Render(camera);
                profile.InnerWrapStrength = .32f; wrapped = Render(camera);
                float edgeGain = wrapped.GetPixel(13, 28).g - baseline.GetPixel(13, 28).g;
                Assert.That(edgeGain, Is.GreaterThan(.1f));
                float corePeak = Mathf.Max(core.GetPixel(13, 28).g, core.GetPixel(42, 28).g);
                float wrappedPeak = Mathf.Max(wrapped.GetPixel(13, 28).g, wrapped.GetPixel(42, 28).g);
                Assert.That(wrappedPeak, Is.EqualTo(corePeak).Within(.004f), "Reducing rear wrap must preserve the bright contour peak.");
                Assert.That(wrapped.GetPixel(13, 28).g, Is.GreaterThanOrEqualTo(core.GetPixel(13, 28).g - .004f), "A partially covered boundary cell may receive wrap, but must not be dimmed.");
                Assert.That(wrapped.GetPixel(17, 28).g - baseline.GetPixel(17, 28).g, Is.LessThan(edgeGain * .1f), "The symmetric rear transition is faint beside the one-cell core.");
                Assert.That(wrapped.GetPixel(21, 28).g, Is.EqualTo(baseline.GetPixel(21, 28).g).Within(.004f), "Rear wrap must not reach the third art cell inside the body.");
                // Several lights on opposite sides must not reconstruct a broad body fill
                // from individually valid directional transitions.
                light.transform.position = actor.bounds.center + new Vector3(-.6f, 0, -.4f);
                singleSide = Render(camera);
                Assert.That(singleSide.GetPixel(17, 28).g - baseline.GetPixel(17, 28).g, Is.GreaterThan(edgeGain * .1f), "Keep the existing one-sided transition.");
                opposedHost = new GameObject("Synthetic opposed light");
                SceneManager.MoveGameObjectToScene(opposedHost, light.gameObject.scene);
                opposedHost.transform.position = actor.bounds.center + new Vector3(.6f, 0, -.4f);
                var other = opposedHost.AddComponent<Light>(); other.type = LightType.Point;
                other.color = light.color; other.intensity = light.intensity; other.range = light.range;
                other.cullingMask = light.cullingMask; other.renderMode = LightRenderMode.ForcePixel;
                var otherSource = opposedHost.AddComponent<SpriteRimLightSource>();
                otherSource.DarkEdgeFill = source.DarkEdgeFill;
                opposed = Render(camera);
                Assert.That(opposed.GetPixel(13, 28).g - baseline.GetPixel(13, 28).g, Is.GreaterThan(.1f));
                Assert.That(opposed.GetPixel(42, 28).g - baseline.GetPixel(42, 28).g, Is.GreaterThan(.1f));
                Assert.That(opposed.GetPixel(17, 28).g - baseline.GetPixel(17, 28).g, Is.LessThan(edgeGain * .1f), "Opposed lights keep the inward transition faint.");
                Assert.That(opposed.GetPixel(21, 28).g, Is.EqualTo(baseline.GetPixel(21, 28).g).Within(.004f), "Opposed wrap stays out of the body interior.");
                // Shadowing must affect the style mix as well as the light color. Otherwise an
                // invisible opposing source still removes the visible side's inward transition.
                realtimeShadows.SetValue(pipeline, false);
                caster = GameObject.CreatePrimitive(PrimitiveType.Cube);
                SceneManager.MoveGameObjectToScene(caster, camera.scene);
                caster.layer = 29;
                caster.transform.position = opposedHost.transform.position;
                caster.transform.localScale = Vector3.one * .15f;
                other.shadows = LightShadows.Hard; other.shadowStrength = 1;
                otherSource.FallbackOccluder = caster.GetComponent<MeshRenderer>();
                blocked = Render(camera);
                AssertSameImage(singleSide, blocked, "A fully blocked opposing light must preserve the visible side's contour style.");
                source.enabled = false;
                blockedOnly = Render(camera);
                AssertSameImage(baseline, blockedOnly, "The style comparison must use a completely occluded opposing source.");
            }
            finally
            {
                source.enabled = true; source.DarkEdgeFill = fill; light.transform.position = position;
                light.color = color; light.intensity = intensity; material.SetVector("_BaseColor", baseColor);
                profile.InnerWrapStrength = wrap; profile.InnerWrapWidth = width;
                profile.DarkEdgeBrightness = cap; profile.MinimumExposure = exposure;
                realtimeShadows.SetValue(pipeline, additionalShadows);
                if (caster) Object.DestroyImmediate(caster);
                if (opposedHost) Object.DestroyImmediate(opposedHost);
                foreach (var image in new[] { baseline, core, wrapped, singleSide, opposed, blocked, blockedOnly }) if (image) Object.DestroyImmediate(image);
            }
        }

        private static void VerifyNativeOverlap(Camera camera, Material material, Light primary, SpriteRimLightSource source,
            SpriteRimLightingProfile profile)
        {
            var baseColor = material.GetVector("_BaseColor");
            var originalColor = primary.color;
            float originalIntensity = primary.intensity;
            float originalCap = profile.DarkEdgeBrightness;
            var host = new GameObject("Synthetic same-side local light");
            SceneManager.MoveGameObjectToScene(host, primary.gameObject.scene);
            host.transform.SetPositionAndRotation(primary.transform.position, primary.transform.rotation);
            var secondary = host.AddComponent<Light>();
            secondary.type = LightType.Point;
            secondary.range = primary.range;
            secondary.cullingMask = primary.cullingMask;
            secondary.renderMode = primary.renderMode;
            secondary.enabled = false;
            host.AddComponent<SpriteRimLightSource>().DarkEdgeFill = .18f;
            Texture2D single = null, duplicate = null;
            try
            {
                profile.DarkEdgeBrightness = .25f;
                material.SetVector("_BaseColor", new Vector4(.035f, .035f, .035f, 1f));
                source.DarkEdgeFill = .18f;
                primary.color = new Color(1f, .5f, .12f);
                primary.intensity = .7f;
                single = Render(camera);
                secondary.color = primary.color;
                secondary.intensity = primary.intensity;
                secondary.enabled = true;
                duplicate = Render(camera);
                AssertSameImage(single, duplicate, "Equal same-side lights must share the single-light brightness budget.");

                secondary.color = new Color(.08f, .66f, 1f);
                float lastWarmth = float.PositiveInfinity;
                for (int i = 0; i <= 4; i++)
                {
                    float t = i / 4f;
                    primary.intensity = Mathf.Lerp(1.35f, .25f, t);
                    secondary.intensity = Mathf.Lerp(.25f, 1.35f, t);
                    var image = Render(camera);
                    try
                    {
                        var edge = image.GetPixel(13, 28);
                        float warmth = edge.r - edge.b;
                        Assert.That(warmth, Is.LessThan(lastWarmth), "Changing the stronger light must smoothly transfer its hue.");
                        if (i == 0) Assert.That(warmth, Is.GreaterThan(.06f));
                        if (i == 4) Assert.That(warmth, Is.LessThan(-.06f));
                        lastWarmth = warmth;
                        Assert.That(Mathf.Max(edge.r, Mathf.Max(edge.g, edge.b)), Is.LessThanOrEqualTo(.251f));
                    }
                    finally { Object.DestroyImmediate(image); }
                }
            }
            finally
            {
                primary.intensity = originalIntensity;
                profile.DarkEdgeBrightness = originalCap;
                primary.color = originalColor;
                source.DarkEdgeFill = 0f;
                material.SetVector("_BaseColor", baseColor);
                if (single) Object.DestroyImmediate(single);
                if (duplicate) Object.DestroyImmediate(duplicate);
                Object.DestroyImmediate(host);
            }
        }

        private static void VerifyDarkEdgeFill(Camera camera, Material material, Light light, SpriteRimLightSource source,
            SpriteRimLightingProfile profile)
        {
            var baseColor = material.GetVector("_BaseColor");
            var lightColor = light.color;
            var position = light.transform.position;
            float intensity = light.intensity;
            float originalCap = profile.DarkEdgeBrightness;
            Texture2D legacy = null, near = null, far = null, whiteLegacy = null, whiteFilled = null;
            try
            {
                profile.DarkEdgeBrightness = .25f;
                light.color = new Color(1f, .5f, .12f);
                light.intensity = .65f;
                material.SetVector("_BaseColor", new Vector4(0f, 0f, 0f, 1f));
                Assert.That(source.DarkEdgeFill, Is.Zero, "Existing marked lights keep their current response.");
                legacy = Render(camera);
                source.DarkEdgeFill = .18f;
                near = Render(camera);
                var edge = near.GetPixel(13, 28);
                Assert.That(edge.r, Is.GreaterThan(legacy.GetPixel(13, 28).r + .05f));
                Assert.That(edge.r, Is.GreaterThan(edge.g).And.LessThanOrEqualTo(.251f), "Dark contour fill is colored and bounded.");
                Assert.That(near.GetPixel(29, 28).r, Is.EqualTo(legacy.GetPixel(29, 28).r).Within(.004f), "Dark fill stays out of the interior.");
                Assert.That(near.GetPixel(13, 16).r, Is.EqualTo(legacy.GetPixel(13, 16).r).Within(.004f), "Occluded art does not get colored.");
                light.transform.position = new Vector3(-2.6f, position.y, position.z);
                far = Render(camera);
                Assert.That(far.GetPixel(13, 28).r, Is.InRange(.01f, edge.r - .01f), "The added color fades with distance.");

                light.transform.position = position;
                material.SetVector("_BaseColor", Vector4.one);
                source.DarkEdgeFill = 0f;
                whiteLegacy = Render(camera);
                source.DarkEdgeFill = .18f;
                whiteFilled = Render(camera);
                AssertSameImage(whiteLegacy, whiteFilled, "Dark fill must leave bright source pixels unchanged.");
            }
            finally
            {
                source.DarkEdgeFill = 0f;
                light.color = lightColor;
                light.intensity = intensity;
                profile.DarkEdgeBrightness = originalCap;
                light.transform.position = position;
                material.SetVector("_BaseColor", baseColor);
                foreach (var image in new[] { legacy, near, far, whiteLegacy, whiteFilled })
                    if (image) Object.DestroyImmediate(image);
            }
        }

        private static void VerifySpatialAndTransparentSources(Camera camera, SpriteRenderer actor, GameObject occluder,
            SpriteRimLighting lighting, SpriteRimLight light, SpriteRimLightingProfile profile, Texture2D texture, Material material)
        {
            var originalSprite=actor.sprite;var originalPosition=actor.transform.position;
            var originalRotation=actor.transform.rotation;var originalScale=actor.transform.localScale;
            bool originalFlip=actor.flipX,occluderEnabled=occluder.activeSelf;
            var occluderPosition=occluder.transform.position;var occluderScale=occluder.transform.localScale;
            var lampPosition=light.transform.position;var lampColor=light.LightColor;var radius=light.Radius;
            float weak=profile.WeakDistance,strong=profile.StrongDistance,scale=profile.StrongScale,exposure=profile.MinimumExposure;
            int queue=material.renderQueue;var baseColor=material.GetVector("_BaseColor");
            Sprite pivotSprite=null;Texture2D originImage=null,pivotImage=null;
            try
            {
                occluder.SetActive(false);light.Shape=LightType.Point;light.LightColor=Color.green;light.Radius=Vector2.one*4;
                profile.WeakDistance=profile.StrongDistance=1;profile.StrongScale=0;profile.MinimumExposure=1;
                light.transform.position=actor.bounds.center+Vector3.left;
                lighting.UpdateLighting();originImage=Render(camera);
                pivotSprite=Sprite.Create(texture,new Rect(0,0,16,16),new Vector2(.15f,.8f),16,0,SpriteMeshType.FullRect);
                actor.sprite=pivotSprite;actor.transform.position=originalPosition+new Vector3(-.35f,.3f,0);
                lighting.UpdateLighting();pivotImage=Render(camera);
                AssertSameImage(originImage,pivotImage,"Changing only the pivot, with the visible sprite fixed in world space, must not change the selected light or rim.");
                actor.sprite=originalSprite;actor.transform.position=originalPosition;
                AssertWorldSideLighting(camera,actor,lighting,light,"center pivot");
                actor.flipX=true;AssertWorldSideLighting(camera,actor,lighting,light,"sprite flip");actor.flipX=false;
                actor.transform.rotation=Quaternion.Euler(0,0,90);AssertWorldSideLighting(camera,actor,lighting,light,"90-degree rotation");
                actor.transform.rotation=Quaternion.Euler(0,0,37);actor.transform.localScale=new Vector3(1.25f,.75f,1);
                AssertWorldSideLighting(camera,actor,lighting,light,"rotation with non-uniform scale");
                actor.transform.rotation=Quaternion.identity;actor.transform.localScale=new Vector3(-1.25f,.75f,1);
                AssertWorldSideLighting(camera,actor,lighting,light,"mirrored non-uniform scale");
                actor.transform.rotation=originalRotation;actor.transform.localScale=originalScale;

                // A transparent source over a coplanar opaque surface must never recolor that surface.
                // Queue-based filtering keeps the source's own alpha blend; it does not replace its material.
                occluder.SetActive(true);occluder.transform.position=originalPosition;occluder.transform.localScale=Vector3.one;
                material.renderQueue=(int)RenderQueue.Transparent;material.SetInt("_SrcBlend",(int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend",(int)BlendMode.OneMinusSrcAlpha);material.SetInt("_ZWrite",0);
                foreach(float alpha in new[]{.5f,0f})
                {
                    material.SetVector("_BaseColor",new Vector4(.2f,.2f,.2f,alpha));
                    Texture2D unlit=null,enabled=null;
                    try
                    {
                        lighting.enabled=false;unlit=Render(camera);lighting.enabled=true;lighting.UpdateLighting();enabled=Render(camera);
                        Assert.That(lighting.ActiveTargetCount,Is.Zero,"Transparent source materials are preserved rather than post-composited.");
                        AssertSameImage(unlit,enabled,"A translucent or fully faded source must not paint a rim onto a coplanar background.");
                        Assert.That(actor.sharedMaterial,Is.SameAs(material));
                    }
                    finally{if(unlit)Object.DestroyImmediate(unlit);if(enabled)Object.DestroyImmediate(enabled);}
                }
            }
            finally
            {
                actor.sprite=originalSprite;actor.transform.SetPositionAndRotation(originalPosition,originalRotation);
                actor.transform.localScale=originalScale;actor.flipX=originalFlip;
                material.renderQueue=queue;material.SetVector("_BaseColor",baseColor);material.SetInt("_SrcBlend",1);material.SetInt("_DstBlend",0);material.SetInt("_ZWrite",1);
                light.transform.position=lampPosition;light.LightColor=lampColor;light.Radius=radius;
                profile.WeakDistance=weak;profile.StrongDistance=strong;profile.StrongScale=scale;profile.MinimumExposure=exposure;
                occluder.transform.position=occluderPosition;occluder.transform.localScale=occluderScale;occluder.SetActive(occluderEnabled);
                lighting.enabled=true;lighting.UpdateLighting();
                if(pivotSprite)Object.DestroyImmediate(pivotSprite);if(originImage)Object.DestroyImmediate(originImage);if(pivotImage)Object.DestroyImmediate(pivotImage);
            }
        }

        private static void AssertWorldSideLighting(Camera camera, SpriteRenderer actor, SpriteRimLighting lighting, SpriteRimLight light, string scenario)
        {
            light.transform.position=actor.bounds.center+Vector3.left;
            Texture2D baseline=null,lit=null;
            try
            {
                lighting.enabled=false;baseline=Render(camera);lighting.enabled=true;lighting.UpdateLighting();lit=Render(camera);
                Assert.That(lighting.ActiveTargetCount,Is.EqualTo(1),scenario);
                float background=baseline.GetPixel(0,0).r,total=0,moment=0,bodyMoment=0;int bodyPixels=0;
                for(int y=0;y<64;y++)for(int x=0;x<64;x++)
                {
                    var source=baseline.GetPixel(x,y);
                    if(Mathf.Abs(source.r-background)>.02f){bodyMoment+=x;bodyPixels++;}
                    float gain=Mathf.Max(0,lit.GetPixel(x,y).g-source.g);
                    total+=gain;moment+=gain*x;
                }
                Assert.That(bodyPixels,Is.GreaterThan(10),scenario+": source visible");
                Assert.That(total,Is.GreaterThan(.05f),scenario+": light visible");
                Assert.That(moment/total,Is.LessThan(bodyMoment/bodyPixels-.5f),scenario+": a world-left lamp must continue to illuminate the world-left contour");
            }
            finally{if(baseline)Object.DestroyImmediate(baseline);if(lit)Object.DestroyImmediate(lit);}
        }

        private static void VerifyReferenceDarkContours(Camera camera, Renderer actor, Material material,
            SpriteRimLighting lighting, SpriteRimLight light, SpriteRimLightingProfile profile)
        {
            var color=material.GetVector("_BaseColor");var lightColor=light.LightColor;
            var position=light.transform.position;var shape=light.Shape;float intensity=light.Intensity;
            float weak=profile.WeakDistance,strong=profile.StrongDistance,scale=profile.StrongScale;
            float wrap=profile.InnerWrapStrength,strength=profile.Strength,exposure=profile.MinimumExposure,maximum=profile.MaximumLight;
            var edgeTint=profile.EdgeTint;
            var images=new List<Texture2D>();GameObject secondHost=null;
            var extraHosts=new List<GameObject>();
            Texture2D Capture(){var image=Render(camera);images.Add(image);return image;}
            try
            {
                profile.WeakDistance=profile.StrongDistance=1;profile.StrongScale=0;
                profile.InnerWrapStrength=0;profile.Strength=1;profile.MinimumExposure=1;profile.MaximumLight=1;
                material.SetVector("_BaseColor",new Vector4(0,0,0,1));
                light.Shape=LightType.Point;
                light.transform.position=new Vector3(-.5f,0,0);

                light.LightColor = new Color(.08f, .66f, 1, 1);
                light.Intensity = 0;
                lighting.UpdateLighting();
                var blackUnlit = Capture();
                light.Intensity = .6f;
                lighting.UpdateLighting();
                var weakBlue = Capture();
                light.Intensity = .8f;
                lighting.UpdateLighting();
                var risingBlue = Capture();
                light.Intensity = 1;
                lighting.UpdateLighting();
                var ordinaryBlue = Capture();
                var blueContour = ordinaryBlue.GetPixel(13, 28);
                Assert.That(blackUnlit.GetPixel(13, 28).maxColorComponent, Is.LessThan(.0001f));
                Assert.That(weakBlue.GetPixel(13, 28).b, Is.LessThan(.002f));
                Assert.That(risingBlue.GetPixel(13, 28).b, Is.InRange(.002f, blueContour.b * .6f),
                    "Blue reflection should become visible before ordinary intensity and rise with the light.");
                Assert.That(blueContour.b, Is.InRange(.015f, .07f),
                    "Black cloth must show a readable edge under a single ordinary blue lamp.");
                Assert.That(blueContour.r, Is.LessThan(blueContour.b * .1f));
                Assert.That(ordinaryBlue.GetPixel(29, 28).maxColorComponent, Is.LessThan(.0001f),
                    "Light on the silhouette must leave the black body interior dark.");

                light.LightColor = new Color(1, .3f, .1f, 1);
                lighting.UpdateLighting();
                var amberContour = Capture().GetPixel(13, 28);
                Assert.That(amberContour.r, Is.InRange(.015f, .07f),
                    "A warm lamp at the same ordinary intensity must also light the dark edge.");
                Assert.That(amberContour.b, Is.LessThan(amberContour.r * .1f));
                light.LightColor = Color.white;
                lighting.UpdateLighting();
                var single = Capture();

                secondHost=new GameObject("Reference-field opposing light");
                SceneManager.MoveGameObjectToScene(secondHost,actor.gameObject.scene);
                var second=secondHost.AddComponent<SpriteRimLight>();second.Radius=light.Radius;
                second.LightColor=Color.white;second.Intensity=1;secondHost.transform.position=new Vector3(.5f,0,0);
                lighting.Configure(new[]{actor},new[]{light,second},profile);lighting.UpdateLighting();var black=Capture();
                float ordinaryKey = black.GetPixel(13, 28).b;
                Assert.That(ordinaryKey, Is.InRange(single.GetPixel(13, 28).b + .005f, .11f),
                    "Opposed ordinary lights must preserve a readable key edge with bounded brightness.");
                Assert.That(black.GetPixel(42, 28).b, Is.InRange(.008f, ordinaryKey * .7f));
                light.LightColor=second.LightColor=new Color(.02f,.3f,1,1);
                light.Intensity=second.Intensity=2;
                lighting.UpdateLighting();var mixedBlack=Capture();var mixedEdge=mixedBlack.GetPixel(13,28);
                Assert.That(mixedEdge.b,Is.GreaterThan(.002f),
                    "A saturated blue field with a weaker red field must reveal a colored black contour.");
                Assert.That(mixedEdge.b,Is.LessThan(.16f),
                    "The recovered mixed-field branch must keep black detail below a white/neon contour.");
                Assert.That(mixedEdge.r,Is.LessThan(mixedEdge.b*.08f),
                    "A trace of red must not turn a cool reflected edge white.");
                light.LightColor=second.LightColor=Color.white;
                light.Intensity=second.Intensity=1;
                material.SetVector("_BaseColor",new Vector4(.002f,.004f,.012f,1));
                profile.Strength=0;lighting.UpdateLighting();var bodyBase=Capture();profile.Strength=1;
                second.enabled=false;lighting.UpdateLighting();var darkSingle=Capture();
                second.enabled=true;lighting.UpdateLighting();var combined=Capture();
                float keyGain=combined.GetPixel(13,28).b-bodyBase.GetPixel(13,28).b;
                float fillGain=combined.GetPixel(42,28).b-bodyBase.GetPixel(42,28).b;
                Assert.That(keyGain,Is.GreaterThan(darkSingle.GetPixel(13,28).b-bodyBase.GetPixel(13,28).b+.0005f),
                    "Opposed light fields retain the reference's readable response on dark key-side material.");
                Assert.That(fillGain,Is.InRange(.00005f,keyGain*.7f),
                    "The colored dark edge must remain visible on the opposed contour.");
                var body=combined.GetPixel(29,28);var unlitBody=bodyBase.GetPixel(29,28);
                Assert.That(body.r,Is.EqualTo(unlitBody.r).Within(.0001f));
                Assert.That(body.g,Is.EqualTo(unlitBody.g).Within(.0001f));
                Assert.That(body.b,Is.EqualTo(unlitBody.b).Within(.0001f),
                    "The dark body between the contours must retain its scene shading.");
                second.enabled=false;lighting.UpdateLighting();var removed=Capture();
                AssertSameImage(darkSingle,removed,"Disabling the extra field must immediately restore the single-light image.");

                second.enabled=true;secondHost.transform.position=light.transform.position;
                lighting.UpdateLighting();var overlap=Capture();
                float singleGain=darkSingle.GetPixel(13,28).b-bodyBase.GetPixel(13,28).b;
                float overlapGain=overlap.GetPixel(13,28).b-bodyBase.GetPixel(13,28).b;
                Assert.That(singleGain,Is.GreaterThan(.00005f));
                Assert.That(overlapGain,Is.InRange(singleGain,singleGain*1.08f),
                    "A duplicate lamp may gently increase the edge response without triggering a shared Color Dodge singularity.");
                var overlappingLights=new List<SpriteRimLight>{light,second};
                for(int i=0;i<2;i++)
                {
                    var host=new GameObject("Additional overlapping reference light");extraHosts.Add(host);
                    SceneManager.MoveGameObjectToScene(host,actor.gameObject.scene);host.transform.position=light.transform.position;
                    var extra=host.AddComponent<SpriteRimLight>();extra.Radius=light.Radius;
                    extra.LightColor=Color.white;extra.Intensity=light.Intensity;overlappingLights.Add(extra);
                }
                lighting.Configure(new[]{actor},overlappingLights.ToArray(),profile);lighting.UpdateLighting();var four=Capture();
                float fourGain=four.GetPixel(13,28).b-bodyBase.GetPixel(13,28).b;
                Assert.That(fourGain,Is.InRange(overlapGain,singleGain*1.1f),
                    "One key and three fills must remain below 1.1 times the single-lamp added response.");
                Assert.That(four.GetPixel(29,28).b,Is.EqualTo(unlitBody.b).Within(.0001f));
                foreach(var host in extraHosts)Object.DestroyImmediate(host);extraHosts.Clear();
                secondHost.transform.position=new Vector3(.5f,0,0);
                lighting.Configure(new[]{actor},new[]{light,second},profile);

                second.enabled=false;profile.EdgeTint=new Color(2,2,2,1);
                lighting.UpdateLighting();var tinted=Capture();
                Assert.That(tinted.GetPixel(13,28).b,Is.InRange(darkSingle.GetPixel(13,28).b,.1f),
                    "HDR tint must retain the single-light mask bound instead of saturating a dark material to white.");
                profile.EdgeTint=edgeTint;

                second.enabled=true;secondHost.transform.position=light.transform.position;
                light.Intensity=second.Intensity=2;
                light.LightColor=new Color(1,.3f,.1f,1);second.LightColor=new Color(.08f,.66f,1,1);
                material.SetVector("_BaseColor",new Vector4(.12f,.2f,.3f,1));
                profile.Strength=0;lighting.UpdateLighting();var colorBase=Capture();
                profile.Strength=1;lighting.UpdateLighting();var colorMix=Capture();
                var colorEdge=colorMix.GetPixel(13,28);var baseEdge=colorBase.GetPixel(13,28);
                Assert.That(colorEdge.r-baseEdge.r,Is.GreaterThan(colorEdge.g-baseEdge.g),
                    "Strong warm/cool overlap must retain the dominant warm hue instead of the reference saturation branch's green cast.");
                Assert.That(colorEdge.b,Is.GreaterThan(baseEdge.b+.005f),
                    "The cool lamp must still contribute blue when its reference saturated branch would borrow a darker red channel.");
                var colorBody=colorMix.GetPixel(29,28);var baseBody=colorBase.GetPixel(29,28);
                Assert.That(colorBody.r,Is.EqualTo(baseBody.r).Within(.0001f));
                Assert.That(colorBody.g,Is.EqualTo(baseBody.g).Within(.0001f));
                Assert.That(colorBody.b,Is.EqualTo(baseBody.b).Within(.0001f));
                light.LightColor=second.LightColor=Color.white;secondHost.transform.position=new Vector3(.5f,0,0);

                material.SetVector("_BaseColor",new Vector4(0,0,0,1));
                second.enabled=true;light.Intensity=second.Intensity=2;
                lighting.UpdateLighting();var intense=Capture();
                float blackKey=intense.GetPixel(13,28).b;
                float blackFill=intense.GetPixel(42,28).b;
                Assert.That(blackKey,Is.InRange(.07f,.15f),
                    "Strong white light must produce a readable dark contour without flattening it into a white rim.");
                Assert.That(blackKey, Is.GreaterThan(ordinaryKey));
                Assert.That(blackFill,Is.InRange(.035f,blackKey*.7f),"Keep the strong-light fill below the key.");
                body=intense.GetPixel(29,28);
                Assert.That(Mathf.Max(body.r,Mathf.Max(body.g,body.b)),Is.LessThan(.0001f));

                second.enabled=false;
                light.Intensity=1.30f;lighting.UpdateLighting();var nearSaturation=Capture();
                light.Intensity=1.36f;lighting.UpdateLighting();var saturated=Capture();
                Assert.That(saturated.GetPixel(13,28).b,Is.EqualTo(nearSaturation.GetPixel(13,28).b).Within(.006f),
                    "Near-black art must not flash when the quantized field reaches saturation.");
                material.SetVector("_BaseColor",new Vector4(.15f,.3f,.5f,1));
                lighting.UpdateLighting();var lightPaint=Capture();
                Assert.That(lightPaint.GetPixel(13,28).b,Is.GreaterThan(blackKey*3),
                    "Dark and bright painted edges must remain distinct under the same strong field.");

                second.enabled=false;material.SetVector("_BaseColor",new Vector4(.002f,.004f,.012f,1));
                light.LightColor=Color.blue;light.Intensity=1.2f;
                lighting.UpdateLighting();var blue=Capture();var blueEdge=blue.GetPixel(13,28);
                Assert.That(blueEdge.b,Is.GreaterThan(.01f),"Bright blue light must reveal dark blue material detail.");
                Assert.That(blueEdge.b,Is.GreaterThan(blueEdge.r*20),"The bright-dark response must preserve the incident color.");

                light.Intensity=.65f;material.SetVector("_BaseColor",new Vector4(.002f,.004f,.012f,1));
                profile.Strength=0;lighting.UpdateLighting();var darkBase=Capture();
                profile.Strength=1;lighting.UpdateLighting();var dark=Capture();
                float darkGain=dark.GetPixel(13,28).b-darkBase.GetPixel(13,28).b;
                Assert.That(darkGain,Is.GreaterThan(darkBase.GetPixel(13,28).b*.03f+.000001f),
                    "Unsaturated dark blue must still receive a color-dependent edge response.");
                var originalBody=darkBase.GetPixel(29,28);var litBody=dark.GetPixel(29,28);
                Assert.That(litBody.r,Is.EqualTo(originalBody.r).Within(.0001f));
                Assert.That(litBody.g,Is.EqualTo(originalBody.g).Within(.0001f));
                Assert.That(litBody.b,Is.EqualTo(originalBody.b).Within(.0001f));
                material.SetVector("_BaseColor",new Vector4(.08f,.1f,.12f,1));
                profile.Strength=0;lighting.UpdateLighting();var paleBase=Capture();
                profile.Strength=1;lighting.UpdateLighting();var pale=Capture();
                float paleEdge = pale.GetPixel(13, 28).b;
                float paleGain = paleEdge - paleBase.GetPixel(13, 28).b;
                Assert.That(paleGain, Is.GreaterThan(darkGain),
                    "A pale material should retain a stronger rim response under the same weak lamp.");
                Assert.That(paleEdge, Is.GreaterThan(dark.GetPixel(13, 28).b * 4),
                    "The shaded edge must retain clear brightness separation between pale and dark artwork.");
            }
            finally
            {
                if(secondHost)Object.DestroyImmediate(secondHost);
                foreach(var host in extraHosts)if(host)Object.DestroyImmediate(host);
                foreach(var image in images)Object.DestroyImmediate(image);
                material.SetVector("_BaseColor",color);light.LightColor=lightColor;light.Intensity=intensity;
                light.Shape=shape;light.transform.position=position;
                profile.WeakDistance=weak;profile.StrongDistance=strong;profile.StrongScale=scale;
                profile.InnerWrapStrength=wrap;profile.Strength=strength;profile.MinimumExposure=exposure;profile.MaximumLight=maximum;
                profile.EdgeTint=edgeTint;
                lighting.Configure(new[]{actor},new[]{light},profile);lighting.UpdateLighting();
            }
        }

        private static Color ExpectedReferenceEdge(Color source,int x,int y,SpriteRimLight light,SpriteRimLightingProfile profile)
        {
            var position=new Vector3((x+.5f)/64f-.5f,(y+.5f)/64f-.5f,0);
            var q=light.transform.InverseTransformPoint(position);q.x/=light.Radius.x;q.y/=light.Radius.y;
            float falloff=Mathf.Pow(Mathf.Max(0,1-new Vector2(q.x,q.y).magnitude),light.LightPower);
            float alpha=falloff*light.LightColor.a*light.Intensity;
            float Quantize(float value)=>Mathf.Round(Mathf.Clamp01(value)*255)/255;
            float fieldAlpha=profile.ReferenceEdgeResponse?Quantize(Quantize(alpha)*Quantize(alpha)):Mathf.Clamp01(alpha*alpha);
            Assert.That(light.TryGetInfluence(Vector3.zero,out _,out float intensity),Is.True);
            float response=Mathf.Pow(intensity,profile.ResponsePower);
            float exposure=Mathf.Lerp(1,profile.MinimumExposure*light.MinimumGlobalExposure,intensity);
            float blend=fieldAlpha*profile.Strength;
            Color input=profile.GammaBlend && QualitySettings.activeColorSpace==ColorSpace.Linear?source.gamma:source;
            float[] b={input.r,input.g,input.b},c={light.LightColor.r,light.LightColor.g,light.LightColor.b};
            float[] value=new float[3];
            for(int i=0;i<3;i++){
                float incident=profile.ReferenceEdgeResponse?Quantize(Quantize(c[i]*light.Intensity*alpha)*Quantize(alpha)):Mathf.Clamp01(c[i]*light.Intensity*fieldAlpha);
                float f=incident*response;
                float red=Quantize(Quantize(c[0]*light.Intensity*alpha)*Quantize(alpha))*response;
                float saturated=profile.ReferenceEdgeResponse&&i>0&&red>0?red:f;
                float dodge=f>=1?saturated:b[i]/(1-f);
                value[i]=Mathf.Min(Mathf.Lerp(b[i],dodge,blend),profile.MaximumLight)*(profile.ReferenceEdgeResponse?1:exposure);
            }
            var result=new Color(value[0],value[1],value[2],source.a);
            if(profile.ReferenceEdgeResponse&&profile.LimitAddedHdr)result=new Color(Mathf.Clamp01(result.r),Mathf.Clamp01(result.g),Mathf.Clamp01(result.b),source.a);
            if(profile.GammaBlend && QualitySettings.activeColorSpace==ColorSpace.Linear)result=result.linear;
            if(!profile.ReferenceEdgeResponse)return result;
            return new Color(source.r+Mathf.Max(0,result.r-source.r)*exposure,
                source.g+Mathf.Max(0,result.g-source.g)*exposure,source.b+Mathf.Max(0,result.b-source.b)*exposure,source.a);
        }
        private static void AssertSameImage(Texture2D expected,Texture2D actual,string reason) => AssertImageDelta(expected,actual,.004f,reason);
        private static void AssertImageDelta(Texture2D expected,Texture2D actual,float tolerance,string reason)
        {
            var a=expected.GetPixels();var b=actual.GetPixels();float maximum=0;
            for(int i=0;i<a.Length;i++)maximum=Mathf.Max(maximum,Mathf.Abs(a[i].r-b[i].r),Mathf.Abs(a[i].g-b[i].g),Mathf.Abs(a[i].b-b[i].b));
            Assert.That(maximum,Is.LessThanOrEqualTo(tolerance),reason);
        }
        private static Texture2D Render(Camera camera)
        {
            var target=new RenderTexture(64,64,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);target.Create();var previous=RenderTexture.active;
            try
            {
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active=target;var image=new Texture2D(64,64,TextureFormat.RGBAFloat,false,true);image.ReadPixels(new Rect(0,0,64,64),0,0);image.Apply();return image;
            }
            finally {RenderTexture.active=previous;target.Release();Object.DestroyImmediate(target);}
        }
    }
}
#endif
