using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZeroEngine.Render.Weather;
using ZeroEngine.Render.Urp.Weather;

namespace ZeroEngine.Render.Tests
{
    public sealed class AutomaticSurfaceWeatherTests
    {
        private readonly List<Object> _owned = new();
        private GameObject _root;
        private AutomaticSurfaceWeather _weather;
        private Material _source;
        private static readonly Vector3 Origin = new(10000,0,10000);

        private GameObject Cube(string name,Vector3 point,Vector3 size)
        {
            var obj=GameObject.CreatePrimitive(PrimitiveType.Cube);obj.name=name;obj.layer=31;
            obj.transform.SetParent(_root.transform,false);obj.transform.localPosition=point;obj.transform.localScale=size;
            obj.GetComponent<Renderer>().sharedMaterial=_source;
            Object.Destroy(obj.GetComponent<Collider>());return obj;
        }

        private void Prepare()
        {
            _root=new GameObject("Synthetic automatic weather world");_root.transform.position=Origin;
            _source=new Material(Shader.Find("Universal Render Pipeline/Lit"));_owned.Add(_source);
            _source.SetTextureScale("_BaseMap",new Vector2(2.5f,3.5f));_source.SetFloat("_Smoothness",.17f);
            var profile=ScriptableObject.CreateInstance<SurfaceWeatherSceneProfileSO>();_owned.Add(profile);
            profile.Configure(Shader.Find("ZeroEngine/Render/SceneWeatherLit"),Shader.Find("ZeroEngine/Render/SceneWeatherTerrain"),
                Shader.Find("Hidden/ZeroEngine/SurfaceWeatherExposure"),1<<31);
            _weather=_root.AddComponent<AutomaticSurfaceWeather>();_weather.Configure(profile);
            _weather.Field.DefaultValue=new SurfaceWeatherValue(1,1);
        }

        private IEnumerator Wait(System.Func<bool> condition)
        {
            float end=Time.realtimeSinceStartup+12f;
            while(!condition()&&Time.realtimeSinceStartup<end)yield return null;
            Assert.That(condition(),Is.True,"The bounded automatic discovery/exposure update did not publish the expected state. "+ExposureDiagnostic());
        }

        [UnityTest]
        public IEnumerator MaterialReplacement_RebindsTrackedRendererImmediatelyWithoutGlobalDiscovery()
        {
            Prepare();
            var first = Cube("first", Vector3.zero, Vector3.one).GetComponent<Renderer>();
            var second = Cube("second", Vector3.right * 3f, Vector3.one).GetComponent<Renderer>();
            yield return Wait(() => _weather.IsReady);
            var untouched = second.sharedMaterial;
            var replacement = new Material(_source); _owned.Add(replacement);
            replacement.SetFloat("_Smoothness", .63f);
            Material recipe = null;
            for (int cycle = 0; cycle < 4; cycle++)
            {
                recipe = cycle % 2 == 0 ? replacement : _source;
                first.sharedMaterial = recipe;
                SurfaceWeatherMaterialSources.NotifyRendererMaterialsChanged(first);
                Assert.That(first.sharedMaterial.GetFloat("_ZeroSceneWeatherReceiver"), Is.EqualTo(1f));
                Assert.That(SurfaceWeatherMaterialSources.Resolve(first.sharedMaterial), Is.SameAs(recipe));
                Assert.That(second.sharedMaterial, Is.SameAs(untouched));
                Assert.That(_weather.RegisteredObjects, Is.EqualTo(2));
                Assert.That(_weather.OwnedMaterials, Is.LessThanOrEqualTo(2));
            }
            var late = Cube("not discovered yet", Vector3.left * 3f, Vector3.one).GetComponent<Renderer>();
            SurfaceWeatherMaterialSources.NotifyRendererMaterialsChanged(late);
            Assert.That(late.sharedMaterial, Is.SameAs(_source));
            Assert.That(_weather.RegisteredObjects, Is.EqualTo(2));
            _weather.DiscoverNow();
            Assert.That(_weather.RegisteredObjects, Is.EqualTo(3));
            _weather.enabled = false;
            Assert.That(first.sharedMaterial, Is.SameAs(recipe));
            Assert.That(second.sharedMaterial, Is.SameAs(_source));
            Assert.That(late.sharedMaterial, Is.SameAs(_source));
            first.sharedMaterial = replacement;
            SurfaceWeatherMaterialSources.NotifyRendererMaterialsChanged(first);
            Assert.That(first.sharedMaterial, Is.SameAs(replacement), "A disposed registry must not retain its callback.");
        }

        [UnityTest]
        public IEnumerator MergedNonReadableRoof_MovesAndCopiesWithoutAuthoredWeatherComponents()
        {
            Prepare();Cube("ground",new Vector3(0,-.1f,0),new Vector3(24,.2f,12));
            GameObject house=Cube("merged roof and walls",new Vector3(0,1.5f,0),new Vector3(4,3,4));
            var mesh=Object.Instantiate(house.GetComponent<MeshFilter>().sharedMesh);_owned.Add(mesh);
            mesh.UploadMeshData(true);house.GetComponent<MeshFilter>().sharedMesh=mesh;
            yield return Wait(()=>_weather.IsReady);
            Assert.That(_weather.Sample(Origin+new Vector3(0,.05f,0)).Snow,Is.LessThan(.1f),ExposureDiagnostic());
            Assert.That(_weather.Sample(Origin+new Vector3(0,3.05f,0)).Snow,Is.GreaterThan(.9f));
            Assert.That(_weather.Sample(Origin+new Vector3(-5,.05f,0)).Wetness,Is.GreaterThan(.9f));
            Material borrowed=house.GetComponent<Renderer>().sharedMaterial;
            Assert.That(borrowed.shader.name,Is.EqualTo("ZeroEngine/Render/SceneWeatherLit"));
            Assert.That(borrowed.GetTextureScale("_BaseMap"),Is.EqualTo(new Vector2(2.5f,3.5f)));
            house.transform.localPosition+=Vector3.right*6;
            yield return Wait(()=>_weather.Sample(Origin+new Vector3(0,.05f,0)).Snow>.9f
                &&_weather.Sample(Origin+new Vector3(6,.05f,0)).Snow<.1f);
            house.GetComponent<Renderer>().enabled=false;
            Assert.That(_weather.Sample(Origin+new Vector3(6,.05f,0)).Snow,Is.LessThan(.1f),"Visual cutaway must not remove the physical roof.");
            GameObject copy=Object.Instantiate(house,_root.transform);copy.transform.localPosition=new Vector3(-6,1.5f,0);copy.GetComponent<Renderer>().enabled=true;
            yield return Wait(()=>_weather.Sample(Origin+new Vector3(-6,.05f,0)).Snow<.1f);
            _weather.enabled=false;
            Assert.That(house.GetComponent<Renderer>().sharedMaterial,Is.SameAs(_source));
            Assert.That(copy.GetComponent<Renderer>().sharedMaterial,Is.SameAs(_source));
            Assert.That(_source.GetFloat("_Smoothness"),Is.EqualTo(.17f));
            Assert.That(SurfaceWeatherExposure.Current,Is.Null);
        }

        [UnityTest]
        public IEnumerator NewTerrainAndPooledMesh_AutomaticallyAdaptAndReleaseTheirBorrowedState()
        {
            Prepare();
            var data=new TerrainData{heightmapResolution=33,size=new Vector3(8,2,8)};_owned.Add(data);
            GameObject ground=Terrain.CreateTerrainGameObject(data);ground.layer=31;ground.transform.SetParent(_root.transform,false);
            Terrain terrain=ground.GetComponent<Terrain>();Material original=terrain.materialTemplate;
            yield return Wait(()=>_weather.IsReady&&terrain.materialTemplate!=original);
            Assert.That(terrain.materialTemplate.shader.name,Is.EqualTo("ZeroEngine/Render/SceneWeatherTerrain"));
            Assert.That(_weather.Sample(Origin+new Vector3(2,.05f,2)).Snow,Is.GreaterThan(.9f));
            GameObject pooled=Cube("pooled receiver",new Vector3(3,3,3),new Vector3(4,.3f,4));
            yield return Wait(()=>_weather.Sample(Origin+new Vector3(3,.05f,3)).Snow<.1f);
            pooled.SetActive(false);
            yield return Wait(()=>pooled.GetComponent<Renderer>().sharedMaterial==_source
                &&_weather.Sample(Origin+new Vector3(3,.05f,3)).Snow>.9f);
            pooled.SetActive(true);
            yield return Wait(()=>_weather.Sample(Origin+new Vector3(3,.05f,3)).Snow<.1f);
            int before=_weather.OwnedMaterials;
            var transient=new Material(_source);_owned.Add(transient);
            GameObject disposable=Cube("transient source",new Vector3(12,0,0),Vector3.one);
            disposable.GetComponent<Renderer>().sharedMaterial=transient;
            yield return Wait(()=>_weather.OwnedMaterials>before);
            Object.Destroy(disposable);Object.Destroy(transient);
            yield return Wait(()=>_weather.OwnedMaterials==before);
            var unfamiliar=new Material(Shader.Find("Universal Render Pipeline/Unlit")){name="unsupported terrain source"};_owned.Add(unfamiliar);
            GameObject customGround=Terrain.CreateTerrainGameObject(data);customGround.layer=31;customGround.transform.SetParent(_root.transform,false);
            customGround.transform.localPosition=new Vector3(20,0,0);Terrain customTerrain=customGround.GetComponent<Terrain>();customTerrain.materialTemplate=unfamiliar;
            int registered=_weather.RegisteredObjects;
            yield return Wait(()=>_weather.RegisteredObjects>registered);
            Assert.That(customTerrain.materialTemplate,Is.SameAs(unfamiliar));
            Assert.That(_weather.UnsupportedShaders,Has.Some.Contains("unsupported terrain source"),"An unknown Terrain shader must remain intact and report its missing family adapter.");
            _weather.enabled=false;
            Assert.That(terrain.materialTemplate,Is.SameAs(original));
            Assert.That(pooled.GetComponent<Renderer>().sharedMaterial,Is.SameAs(_source));
        }

        [UnityTest]
        public IEnumerator DiscoveryBudget_PreservesUnclaimedMaterialsAndReportsIncompleteCoverage()
        {
            Prepare();
            var profile=(SurfaceWeatherSceneProfileSO)typeof(AutomaticSurfaceWeather).GetField("_profile",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(_weather);
            typeof(SurfaceWeatherSceneProfileSO).GetField("_maximumRegisteredObjects",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(profile,2);
            Cube("first",Vector3.zero,Vector3.one);
            Cube("second",Vector3.right*2,Vector3.one);
            GameObject third=Cube("third",Vector3.right*4,Vector3.one);
            yield return Wait(()=>_weather.LastFailure!=null);
            Assert.That(_weather.RegisteredObjects,Is.EqualTo(2));
            Assert.That(_weather.IsReady,Is.False);
            Assert.That(third.GetComponent<Renderer>().sharedMaterial,Is.SameAs(_source));
            _weather.enabled=false;
            Assert.That(_weather.OwnedMaterials,Is.Zero);
        }
        [UnityTest]
        public IEnumerator TransparentRoof_OccludesWeather_WhileActorShadowProxyDoesNot()
        {
            Prepare();Cube("ground",new Vector3(0,-.1f,0),new Vector3(20,.2f,12));
            GameObject roof=Cube("transparent roof",new Vector3(0,3,0),new Vector3(4,.2f,4));
            var glass=new Material(_source){renderQueue=3000};_owned.Add(glass);
            glass.SetFloat("_Surface",1f);glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            roof.GetComponent<Renderer>().sharedMaterial=glass;
            GameObject proxy=Cube("actor shadow",new Vector3(6,3,0),new Vector3(2,.2f,2));
            proxy.GetComponent<Renderer>().shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            yield return Wait(()=>_weather.IsReady);
            Assert.That(_weather.Sample(Origin+new Vector3(0,.05f,0)).Snow,Is.LessThan(.1f));
            Assert.That(_weather.Sample(Origin+new Vector3(6,.05f,0)).Snow,Is.GreaterThan(.9f));
            Assert.That(roof.GetComponent<Renderer>().sharedMaterial.renderQueue,Is.EqualTo(3000));
            Assert.That(roof.GetComponent<Renderer>().sharedMaterial.GetFloat("_Surface"),Is.EqualTo(1f));
            Assert.That(roof.GetComponent<Renderer>().sharedMaterial.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"),Is.True);
            Assert.That(_weather.TryGetSceneBounds(_root.scene,out Bounds before),Is.True);
            proxy.transform.localPosition+=Vector3.right*1000;
            yield return null;
            Assert.That(_weather.TryGetSceneBounds(_root.scene,out Bounds after),Is.True);
            Assert.That(after.max.x,Is.EqualTo(before.max.x),"Moving an actor proxy must not move the climate domain.");
            var profile=(SurfaceWeatherSceneProfileSO)typeof(AutomaticSurfaceWeather).GetField("_profile",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(_weather);
            var mist=new Material(Shader.Find("Universal Render Pipeline/Unlit"));_owned.Add(mist);
            typeof(SurfaceWeatherSceneProfileSO).GetField("_nonReceiverMaterials",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).SetValue(profile,new[]{mist});
            GameObject backdrop=Cube("nonphysical backdrop",new Vector3(2000,0,0),Vector3.one*100);
            backdrop.GetComponent<Renderer>().sharedMaterial=mist;
            yield return Wait(()=>_weather.RegisteredObjects>=4);
            Assert.That(_weather.TryGetSceneBounds(_root.scene,out Bounds withoutBackdrop),Is.True);
            Assert.That(withoutBackdrop.max.x,Is.EqualTo(before.max.x),"An atmosphere backdrop must not stretch the climate domain.");
            Assert.That(backdrop.GetComponent<Renderer>().sharedMaterial,Is.SameAs(mist));
            Assert.That(_weather.UnsupportedShaders,Is.Empty);
            _weather.enabled=false;
            Assert.That(roof.GetComponent<Renderer>().sharedMaterial,Is.SameAs(glass));
        }
        [UnityTest]
        public IEnumerator StaticBatch_SamplesOnlyItsRendererSlice_AndKeepsHiddenRoofGeometry()
        {
            Prepare();GameObject ground=Cube("static ground",new Vector3(0,-.1f,0),new Vector3(12,.2f,12));
            GameObject roof=Cube("static roof",new Vector3(0,3,0),new Vector3(4,.2f,4));
            Mesh original=ground.GetComponent<MeshFilter>().sharedMesh;
            StaticBatchingUtility.Combine(_root);
            foreach(GameObject obj in new[]{ground,roof}){Mesh mesh=obj.GetComponent<MeshFilter>().sharedMesh;if(mesh!=original&&!_owned.Contains(mesh))_owned.Add(mesh);}
            Assert.That(roof.GetComponent<MeshRenderer>().isPartOfStaticBatch,Is.True);
            yield return Wait(()=>_weather.IsReady);
            Assert.That(_weather.Sample(Origin+new Vector3(0,.05f,0)).Snow,Is.LessThan(.1f),ExposureDiagnostic());
            roof.GetComponent<Renderer>().enabled=false;
            _root.transform.position+=Vector3.forward*8;
            yield return Wait(()=>_weather.Sample(Origin+new Vector3(0,.05f,8)).Snow<.1f
                &&_weather.Sample(Origin+new Vector3(0,.05f,0)).Snow>.9f);
        }

        [UnityTest]
        public IEnumerator LitAndFiveLayerTerrain_RenderAccumulationFromTheAutomaticField()
        {
            Prepare();_weather.Field.DefaultValue=new SurfaceWeatherValue(0,0);
            _source.SetColor("_BaseColor",new Color(.18f,.045f,.015f));
            Cube("visible lit surface",new Vector3(-3,.1f,0),new Vector3(4,.2f,4));
            var data=new TerrainData{heightmapResolution=33,alphamapResolution=16,size=new Vector3(4,.3f,4)};_owned.Add(data);
            var layers=new TerrainLayer[5];
            for(int i=0;i<layers.Length;i++)
            {
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);_owned.Add(texture);
                texture.SetPixels(new[]{new Color(.18f,.07f,.02f),new Color(.18f,.07f,.02f),new Color(.18f,.07f,.02f),new Color(.18f,.07f,.02f)});texture.Apply();
                layers[i]=new TerrainLayer{diffuseTexture=texture,tileSize=Vector2.one};_owned.Add(layers[i]);
            }
            data.terrainLayers=layers;var alpha=new float[16,16,5];
            for(int y=0;y<16;y++)for(int x=0;x<16;x++)for(int i=0;i<5;i++)alpha[y,x,i]=.2f;
            data.SetAlphamaps(0,0,alpha);
            GameObject terrain=Terrain.CreateTerrainGameObject(data);terrain.layer=31;terrain.transform.SetParent(_root.transform,false);terrain.transform.localPosition=new Vector3(0,0,-2);
            var lightObject=new GameObject("owned material rendering light");lightObject.transform.SetParent(_root.transform,false);
            var light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=2;light.cullingMask=1<<31;light.shadows=LightShadows.None;
            lightObject.transform.rotation=Quaternion.Euler(50,30,0);
            var cameraObject=new GameObject("owned material rendering camera");cameraObject.transform.SetParent(_root.transform,false);
            var camera=cameraObject.AddComponent<Camera>();camera.transform.localPosition=new Vector3(-.5f,10,0);camera.transform.rotation=Quaternion.Euler(90,0,0);
            camera.orthographic=true;camera.orthographicSize=5.5f;camera.aspect=1;camera.nearClipPlane=.01f;camera.farClipPlane=30;
            camera.cullingMask=1<<31;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.allowHDR=false;camera.allowMSAA=false;
            var target=new RenderTexture(128,128,24,RenderTextureFormat.ARGB32);_owned.Add(target);target.Create();camera.targetTexture=target;
            var readback=new Texture2D(128,128,TextureFormat.RGBA32,false);_owned.Add(readback);
            yield return Wait(()=>_weather.IsReady);
            yield return new WaitForEndOfFrame();
            Vector3 litPoint=Origin+new Vector3(-3,.2f,0),terrainPoint=Origin+new Vector3(2,.02f,0);
            float litDry=RenderedLuminance(camera,target,readback,litPoint),terrainDry=RenderedLuminance(camera,target,readback,terrainPoint);
            _weather.Field.DefaultValue=new SurfaceWeatherValue(0,1);
            yield return null;yield return new WaitForEndOfFrame();
            float litSnow=RenderedLuminance(camera,target,readback,litPoint),terrainSnow=RenderedLuminance(camera,target,readback,terrainPoint);
            Assert.That(litSnow,Is.GreaterThan(litDry+.15f),$"The Lit colour pass must visibly apply weather, dry={litDry}, snow={litSnow}.");
            Assert.That(terrainSnow,Is.GreaterThan(terrainDry+.15f),$"All five Terrain layers must visibly apply weather, dry={terrainDry}, snow={terrainSnow}.");
            camera.targetTexture=null;target.Release();
        }

        private static float RenderedLuminance(Camera camera,RenderTexture target,Texture2D readback,Vector3 point)
        {
            RenderTexture previous=RenderTexture.active;
            try
            {
                RenderTexture.active=target;readback.ReadPixels(new Rect(0,0,128,128),0,0);readback.Apply();
                Vector3 uv=camera.WorldToViewportPoint(point);int x=Mathf.RoundToInt(uv.x*127),y=Mathf.RoundToInt(uv.y*127);float sum=0;
                for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++){Color c=readback.GetPixel(x+dx,y+dy);sum+=c.r*.2126f+c.g*.7152f+c.b*.0722f;}
                return sum/25f;
            }
            finally{RenderTexture.active=previous;}
        }

        private string ExposureDiagnostic()
        {
            object capture=typeof(AutomaticSurfaceWeather).GetField("_exposure",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(_weather);
            if(capture==null)return "No capture owner.";
            var maps=(System.Array)capture.GetType().GetField("_maps",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(capture);
            object map=maps.GetValue(1);var type=map.GetType();
            var projection=(SurfaceWeatherProjection)type.GetField("Projection").GetValue(map);
            var pixels=(float[])type.GetField("PublishedPixels").GetValue(map);
            float min=float.PositiveInfinity,max=float.NegativeInfinity;foreach(float value in pixels){min=Mathf.Min(min,value);max=Mathf.Max(max,value);}
            var material=(Material)capture.GetType().GetField("_capture",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(capture);
            return $"objects={_weather.RegisteredObjects}; generation={_weather.ExposureGeneration}; failure={_weather.LastFailure}; uv={projection.UV(Origin)}; U={projection.U}; V={projection.V}; range={min}..{max}; shader={material.shader.name}/{material.shader.isSupported}; depthTest={material.GetFloat("_ZeroDepthTest")}; device={SystemInfo.graphicsDeviceType}";
        }
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if(_weather!=null)_weather.enabled=false;
            if(_root!=null)Object.Destroy(_root);
            foreach(Object obj in _owned)if(obj!=null)Object.Destroy(obj);
            _owned.Clear();yield return null;
        }
    }
}
