using System;
using UnityEngine;

namespace ZeroEngine.PortraitSpeech
{
    /// <summary>Owns the small protection texture shared by the portrait material and its stencil copy.</summary>
    public sealed class PortraitBlinkMaterial : IDisposable
    {
        private Vector2Int[] pixels;
        private Texture2D mask;
        private Vector4 maskRect;

        public static Vector2Int CanvasSize(Sprite sprite) => sprite?Vector2Int.RoundToInt(sprite.rect.size):Vector2Int.zero;

        public void Apply(Material target,PortraitBlinkProfile profile,float amount,Vector2Int size)
        {
            if(!target)return;
            if(!profile.CanRender(size)||size.x<=0||size.y<=0)
            {
                target.SetFloat("_BlinkAmount",0);
                if(mask)target.SetTexture("_BlinkMask",null);
                Dispose();
                return;
            }

            // The authoring brush replaces the array when edited. No texture upload per frame.
            if(!ReferenceEquals(pixels,profile.protectedPixels))BuildMask(profile.protectedPixels,size);
            target.SetFloat("_BlinkAmount",Mathf.Clamp01(amount));
            if(amount<=.001f)return;
            ApplyEye(target,"_BlinkEyeA","_BlinkPoseA","_BlinkSkinA",profile.leftEye);
            ApplyEye(target,"_BlinkEyeB","_BlinkPoseB","_BlinkSkinB",profile.rightEye);
            target.SetColor("_BlinkLidColor",profile.lidColor);
            target.SetTexture("_BlinkMask",mask);
            target.SetVector("_BlinkMaskRect",maskRect);
        }

        private static void ApplyEye(Material target,string area,string pose,string skin,PortraitBlinkEye eye)
        {
            Rect box=eye.area;
            target.SetVector(area,eye.IsConfigured?new Vector4(box.x,box.y,box.width,box.height):Vector4.zero);
            target.SetVector(pose,new Vector4(Mathf.Clamp(eye.closedHeight,.15f,.85f),eye.tilt,eye.curve,0));
            target.SetVector(skin,new Vector4(eye.skinSample.x,eye.skinSample.y,0,0));
        }

        private void BuildMask(Vector2Int[] points,Vector2Int size)
        {
            Dispose();
            pixels=points;
            if(points==null||points.Length==0)return;

            bool InCanvas(Vector2Int point)=>point.x>=0&&point.y>=0&&point.x<size.x&&point.y<size.y;
            var min=size;var max=new Vector2Int(-1,-1);
            foreach(var point in points)if(InCanvas(point)){min=Vector2Int.Min(min,point);max=Vector2Int.Max(max,point);}
            var extent=max-min+Vector2Int.one;
            if(extent.x<=0||extent.y<=0||extent.x>SystemInfo.maxTextureSize||extent.y>SystemInfo.maxTextureSize||
                (long)extent.x*extent.y>4*1024*1024)return;

            var colors=new Color32[extent.x*extent.y];
            foreach(var point in points)if(InCanvas(point))colors[(point.y-min.y)*extent.x+point.x-min.x]=new Color32(255,255,255,255);
            mask=new Texture2D(extent.x,extent.y,TextureFormat.RGBA32,false,true)
            {name="Portrait eye protection",hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            mask.SetPixels32(colors);mask.Apply(false,true);
            maskRect=new Vector4(min.x,min.y,extent.x,extent.y);
        }

        public void Dispose()
        {
            if(mask)
            {
                if(Application.isPlaying)UnityEngine.Object.Destroy(mask);
                else UnityEngine.Object.DestroyImmediate(mask);
            }
            mask=null;pixels=null;maskRect=Vector4.zero;
        }
    }
}
