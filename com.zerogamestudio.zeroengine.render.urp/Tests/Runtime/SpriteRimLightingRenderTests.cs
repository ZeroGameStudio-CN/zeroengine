#if UNITY_EDITOR
using System;
using System.Collections;
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

        private IEnumerator VerifyComposite(bool verifyWrap = false, bool verifySpatial = false, bool verifyNative = false)
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
                Assert.That(original, Is.Not.Null);
                var rendererField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
                var sourceData = (ScriptableRendererData[])rendererField.GetValue(original);
                Assert.That(sourceData, Is.Not.Null.And.Not.Empty);
                pipeline = Object.Instantiate(original);
                renderer = Object.Instantiate(sourceData[0]);
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
                Assert.That(inside.g,Is.LessThan(baseline.GetPixel(29,28).g),"The reference local exposure must operate on the covered interior.");
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

        private static Color ExpectedReferenceEdge(Color source,int x,int y,SpriteRimLight light,SpriteRimLightingProfile profile)
        {
            var position=new Vector3((x+.5f)/64f-.5f,(y+.5f)/64f-.5f,0);
            var q=light.transform.InverseTransformPoint(position);q.x/=light.Radius.x;q.y/=light.Radius.y;
            float falloff=Mathf.Pow(Mathf.Max(0,1-new Vector2(q.x,q.y).magnitude),light.LightPower);
            float fieldAlpha=Mathf.Clamp01(Mathf.Pow(falloff*light.LightColor.a*light.Intensity,2));
            Assert.That(light.TryGetInfluence(Vector3.zero,out _,out float intensity),Is.True);
            float response=Mathf.Pow(intensity,profile.ResponsePower);
            float exposure=Mathf.Lerp(1,profile.MinimumExposure*light.MinimumGlobalExposure,intensity);
            float blend=fieldAlpha*profile.Strength;
            Color input=profile.GammaBlend && QualitySettings.activeColorSpace==ColorSpace.Linear?source.gamma:source;
            float[] b={input.r,input.g,input.b},c={light.LightColor.r,light.LightColor.g,light.LightColor.b};
            float[] value=new float[3];
            for(int i=0;i<3;i++){
                float f=Mathf.Clamp01(c[i]*light.Intensity*fieldAlpha)*response;
                float dodge=f>=1?f:b[i]/(1-f);
                value[i]=Mathf.Min(Mathf.Lerp(b[i],dodge,blend),profile.MaximumLight)*exposure;
            }
            var result=new Color(value[0],value[1],value[2],source.a);
            return profile.GammaBlend && QualitySettings.activeColorSpace==ColorSpace.Linear?result.linear:result;
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
