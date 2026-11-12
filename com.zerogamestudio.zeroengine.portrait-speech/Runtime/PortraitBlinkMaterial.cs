using System;
using UnityEngine;

namespace ZeroEngine.PortraitSpeech
{
    /// <summary>Owns the small protection texture shared by the portrait material and its stencil copy.</summary>
    public sealed class PortraitBlinkMaterial : IDisposable
    {
        private Vector2Int[] pixels;
        private Texture2D mask;
        private Vector2Int canvasSize;
        private Rect leftArea,rightArea;
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

            // The authoring brush replaces its array; the two eye bounds can also be edited.
            // The protection texture is built once per calibration, never per frame.
            if(!ReferenceEquals(pixels,profile.protectedPixels)||canvasSize!=size||
                leftArea!=profile.leftEye.area||rightArea!=profile.rightEye.area)
                BuildMask(profile,size);
            if(profile.protectedPixels is {Length:>0}&&!mask)
            {
                target.SetFloat("_BlinkAmount",0);
                target.SetTexture("_BlinkMask",null);
                target.SetVector("_BlinkMaskRect",Vector4.zero);
                return;
            }
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

        private void BuildMask(PortraitBlinkProfile profile,Vector2Int size)
        {
            Dispose();
            pixels=profile.protectedPixels;
            canvasSize=size;leftArea=profile.leftEye.area;rightArea=profile.rightEye.area;
            if(pixels==null||pixels.Length==0)return;

            bool InCanvas(Vector2Int point)=>point.x>=0&&point.y>=0&&point.x<size.x&&point.y<size.y;
            var min=size;var max=new Vector2Int(-1,-1);
            foreach(var point in pixels)if(InCanvas(point)){min=Vector2Int.Min(min,point);max=Vector2Int.Max(max,point);}
            if(max.x<min.x||max.y<min.y||size.y>32768)return;

            // Only columns with painted pixels need a span. Extend their rows to the eye bounds
            // so samples below or above a foreground edge still know where to stop.
            void ExtendRows(PortraitBlinkEye eye)
            {
                if(!eye.IsConfigured)return;
                min.y=Mathf.Min(min.y,Mathf.Clamp(Mathf.FloorToInt(eye.area.yMin*size.y),0,size.y-1));
                max.y=Mathf.Max(max.y,Mathf.Clamp(Mathf.CeilToInt(eye.area.yMax*size.y)-1,0,size.y-1));
            }
            ExtendRows(profile.leftEye);ExtendRows(profile.rightEye);
            var extent=max-min+Vector2Int.one;
            if(extent.x>SystemInfo.maxTextureSize||extent.y>SystemInfo.maxTextureSize||
                (long)extent.x*extent.y>4*1024*1024)return;

            var colors=new Color32[extent.x*extent.y];
            foreach(var point in pixels)
                if(InCanvas(point))colors[(point.y-min.y)*extent.x+point.x-min.x]=new Color32(255,0,0,0);
            for(int x=0;x<extent.x;x++)
            {
                int previous=-1;
                for(int y=0;y<=extent.y;y++)
                {
                    if(y<extent.y&&colors[y*extent.x+x].r!=255)continue;
                    if(y==extent.y&&previous<0)break;
                    int bottom=min.y+previous+1,top=min.y+y;
                    // R >= 128 marks a protected pixel. The other values encode the absolute
                    // lower (inclusive) and upper (exclusive) rows of this unbroken span.
                    var span=new Color32((byte)(bottom&127),(byte)(bottom>>7),(byte)(top&255),(byte)(top>>8));
                    for(int row=previous+1;row<y;row++)colors[row*extent.x+x]=span;
                    previous=y;
                }
            }
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
            mask=null;pixels=null;maskRect=Vector4.zero;canvasSize=Vector2Int.zero;leftArea=rightArea=default;
        }
    }
}
