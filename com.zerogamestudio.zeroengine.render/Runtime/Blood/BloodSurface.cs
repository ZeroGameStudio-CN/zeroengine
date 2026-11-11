using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Profiling;

namespace ZeroEngine.Render
{
    public sealed class BloodSurface : System.IDisposable
    {
        private sealed class Layer
        {
            public Renderer Source,Overlay;
            public MeshFilter Filter;
            public Mesh Baked;
            public readonly List<Material> SourceMaterials=new();
            public readonly List<Material> OverlayMaterials=new();
        }
        private readonly BloodVisualResources resources;
        private readonly List<Layer> layers=new();
        private MaterialPropertyBlock block;
        private Material material;
        private static readonly ProfilerMarker UpdateMarker=new("ZeroEngine.BloodSurface.Update");
        private readonly BloodStainMask mask;
        private Texture2D bloodMask;
        public float Intensity=>mask.Intensity;
        public float Coverage=>Intensity*.72f;
        public int Tier=>mask.Tier;
        public int Deposits=>mask.Deposits;
        public int MaskUploads { get; private set; }
        public int LastUpdatedColours=>mask.LastUpdatedColours;
        public int OverlayCount=>layers.Count;
        public BloodSurface(BloodVisualResources resources,uint seed)
        {this.resources=resources??throw new System.ArgumentNullException(nameof(resources));mask=new BloodStainMask(seed);block=new MaterialPropertyBlock();}
        public void Bind(){material=resources.SurfaceMaterial;bloodMask??=resources.TakeLookup();}
        public void Add(float amount)
        {
            mask.Deposit(amount);
        }
        public void Advance(float delta)
        {
            mask.Advance(delta);
        }
        public uint Generation {get;private set;}
        public bool UploadQueued {get;set;}
        public bool CanUpload=>bloodMask && Intensity>0;
        public void FlushPending()
        {
            if(bloodMask && mask.Flush()){bloodMask.SetPixels32(mask.Pixels);bloodMask.Apply(false,false);MaskUploads++;}
        }
        public void ResetForPool()
        {
            Generation++;UploadQueued=false;mask.ResetForPool();MaskUploads=0;
            foreach(var layer in layers)if(layer.Overlay)layer.Overlay.enabled=false;
            if(bloodMask)resources.ReturnLookup(bloodMask);bloodMask=null;material=null;
        }
        public void Render(IReadOnlyList<Renderer> sources,Color bloodColor)
        {
            using var sample=UpdateMarker.Auto();
            for(int i=layers.Count-1;i>=0;i--)
                if(!layers[i].Source)
                {
                    if(layers[i].Overlay)BloodVisualResources.DestroyOwned(layers[i].Overlay.gameObject);
                    if(layers[i].Baked)BloodVisualResources.DestroyOwned(layers[i].Baked);
                    layers.RemoveAt(i);
                }
            foreach(var layer in layers)if(layer.Overlay)layer.Overlay.enabled=false;
            if(Intensity<=0 || sources==null || MaskUploads==0)return;
            for(int layerIndex=0;layerIndex<sources.Count;layerIndex++)
            {
                var renderer=sources[layerIndex];
                if(!renderer || !renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy)continue;
                Layer layer=null;bool isOverlay=false;
                foreach(var existing in layers){if(existing.Overlay==renderer)isOverlay=true;if(existing.Source==renderer)layer=existing;}
                if(isOverlay)continue;
                if(layer==null)
                {
                    if(layers.Count>=16)continue;
                    var go=new GameObject("Player pixel blood stains"){hideFlags=HideFlags.DontSave,layer=renderer.gameObject.layer};
                    go.transform.SetParent(renderer.transform.parent,false);
                    layer=new Layer{Source=renderer};
                    if(renderer is SpriteRenderer)layer.Overlay=go.AddComponent<SpriteRenderer>();
                    else{layer.Filter=go.AddComponent<MeshFilter>();layer.Overlay=go.AddComponent<MeshRenderer>();}
                    layer.Overlay.shadowCastingMode=ShadowCastingMode.Off;layer.Overlay.receiveShadows=false;
                    layers.Add(layer);
                }
                var pose=layer.Overlay.transform;
                // Copy the exact local transform under the same parent, including signed scale.
                // Reconstructing world scale from lossyScale drifts under rotated/mirrored parents.
                if(pose.parent!=renderer.transform.parent)pose.SetParent(renderer.transform.parent,false);
                pose.SetLocalPositionAndRotation(renderer.transform.localPosition,renderer.transform.localRotation);
                pose.localScale=renderer.transform.localScale;
                Texture texture=null;
                if(renderer is SpriteRenderer sprite)
                {
                    var overlay=(SpriteRenderer)layer.Overlay;overlay.sprite=sprite.sprite;
                    if(!sprite.sprite)continue;
                    overlay.flipX=sprite.flipX;overlay.flipY=sprite.flipY;overlay.color=sprite.color;
                    overlay.drawMode=sprite.drawMode;overlay.size=sprite.size;
                    texture=sprite.sprite.texture;
                    overlay.maskInteraction=sprite.maskInteraction;overlay.spriteSortPoint=sprite.spriteSortPoint;
                }
                else
                {
                    Mesh mesh=null;
                    if(renderer is SkinnedMeshRenderer skinned)
                    {layer.Baked??=new Mesh();skinned.BakeMesh(layer.Baked);mesh=layer.Baked;}
                    else if(renderer.TryGetComponent<MeshFilter>(out var filter))mesh=filter.sharedMesh;
                    if(!mesh)continue;
                    layer.Filter.sharedMesh=mesh;
                }
                renderer.GetSharedMaterials(layer.SourceMaterials);
                var materials=layer.SourceMaterials;
                int slots=Mathf.Max(1,materials.Count);
                if(layer.OverlayMaterials.Count!=slots || layer.OverlayMaterials[0]!=material)
                {
                    layer.OverlayMaterials.Clear();
                    for(int i=0;i<slots;i++)layer.OverlayMaterials.Add(material);
                    layer.Overlay.SetSharedMaterials(layer.OverlayMaterials);
                }
                layer.Overlay.sortingLayerID=renderer.sortingLayerID;layer.Overlay.sortingOrder=renderer.sortingOrder+1;
                for(int i=0;i<slots;i++)
                {
                    block.Clear();
                    var original=i<materials.Count?materials[i]:null;
                    var tex=texture?texture:original?(original.HasProperty("_MainTex")?original.GetTexture("_MainTex"):original.HasProperty("_BaseMap")?original.GetTexture("_BaseMap"):null):null;
                    if(!tex)continue;
                    block.SetTexture("_MainTex",tex);
                    block.SetVector("_BloodSourceTexel",new Vector4(1f/tex.width,1f/tex.height,tex.width,tex.height));
                    block.SetTexture("_BloodMask",bloodMask);
                    block.SetFloat("_StainOpacity",mask.DisplayFade);
                    block.SetVector("_StainFlip",renderer is SpriteRenderer sr?new Vector4(sr.flipX?-1:1,sr.flipY?-1:1,0,0):Vector4.one);
                    block.SetColor("_BloodColor",bloodColor);
                    layer.Overlay.SetPropertyBlock(block,i);
                }
                layer.Overlay.enabled=true;
            }
        }
        public void Dispose()
        {
            foreach(var layer in layers){if(layer.Overlay)BloodVisualResources.DestroyOwned(layer.Overlay.gameObject);if(layer.Baked)BloodVisualResources.DestroyOwned(layer.Baked);}
            layers.Clear();ResetForPool();
        }
    }
}
