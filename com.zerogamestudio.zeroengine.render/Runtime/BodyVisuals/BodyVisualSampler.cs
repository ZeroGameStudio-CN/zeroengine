using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ZeroEngine.Render
{
    public struct BodyVisualSample
    {
        public Vector3 point, size;
        public Color color;
        public float height, phase;
    }

    /// <summary>Copied opaque surface cells from the current sprite, including packed/rotated atlas geometry.</summary>
    public static class BodyVisualSampler
    {
        private static float Cross(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        private static bool TryUv(Vector2 p,Vector2[] vertices,Vector2[] uv,ushort[] triangles,out Vector2 result)
        {
            for(int i=0;i<triangles.Length;i+=3)
            {
                int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
                float d=Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]);
                if(Mathf.Abs(d)<.0000001f)continue;
                float u=Cross(p-vertices[a],vertices[c]-vertices[a])/d;
                float v=Cross(vertices[b]-vertices[a],p-vertices[a])/d;
                if(u<-.00001f||v<-.00001f||u+v>1.00001f)continue;
                result=uv[a]+u*(uv[b]-uv[a])+v*(uv[c]-uv[a]);return true;
            }
            result=default;return false;
        }
        public static BodyVisualSample[] ReadSprite(Sprite sprite) => ReadSprite(sprite, value => value);
        public static T[] ReadSprite<T>(Sprite sprite, Func<BodyVisualSample,T> convert)
        {
            if(!sprite||!sprite.texture)return Array.Empty<T>();
            var texture=sprite.texture;
            var vertices=sprite.vertices;var uvs=sprite.uv;var indices=sprite.triangles;
            // Read only this packed sprite's UV bounds, never its entire atlas.
            // Barycentric sampling below also preserves rotated and tight packing.
            int cropX=Mathf.Clamp(Mathf.FloorToInt(uvs.Min(v=>v.x)*texture.width),0,texture.width-1);
            int cropY=Mathf.Clamp(Mathf.FloorToInt(uvs.Min(v=>v.y)*texture.height),0,texture.height-1);
            int cropWidth=Mathf.Clamp(Mathf.CeilToInt(uvs.Max(v=>v.x)*texture.width)-cropX,1,texture.width-cropX);
            int cropHeight=Mathf.Clamp(Mathf.CeilToInt(uvs.Max(v=>v.y)*texture.height)-cropY,1,texture.height-cropY);
            var previous=RenderTexture.active;
            var temporary=texture.isReadable?null:RenderTexture.GetTemporary(cropWidth,cropHeight,0,RenderTextureFormat.ARGB32);
            var readable=texture.isReadable?null:new Texture2D(cropWidth,cropHeight,TextureFormat.RGBA32,false);
            if(readable)readable.hideFlags=HideFlags.HideAndDontSave;
            try
            {
                Color[] pixels;
                if(texture.isReadable)pixels=texture.GetPixels(cropX,cropY,cropWidth,cropHeight);
                else
                {
                    Graphics.Blit(texture,temporary,new Vector2(cropWidth/(float)texture.width,cropHeight/(float)texture.height),new Vector2(cropX/(float)texture.width,cropY/(float)texture.height));
                    RenderTexture.active=temporary;
                    readable.ReadPixels(new Rect(0,0,cropWidth,cropHeight),0,0);readable.Apply(false,false);
                    pixels=readable.GetPixels();
                }
                int width=Mathf.CeilToInt(sprite.rect.width),height=Mathf.CeilToInt(sprite.rect.height),stride=1;
                while(Mathf.CeilToInt(width/(float)stride)*Mathf.CeilToInt(height/(float)stride)>65536)stride++;
                float ppu=sprite.pixelsPerUnit;
                float left=-sprite.pivot.x/ppu,bottom=-sprite.pivot.y/ppu;
                var list=new List<T>();
                for(int y=0;y<height;y+=stride)for(int x=0;x<width;x+=stride)
                {
                    float sx=Mathf.Min(stride,width-x),sy=Mathf.Min(stride,height-y);
                    Vector2 point=new(left+(x+sx*.5f)/ppu,bottom+(y+sy*.5f)/ppu);
                    if(!TryUv(point,vertices,uvs,indices,out var uv))continue;
                    int tx=Mathf.Clamp(Mathf.FloorToInt(uv.x*texture.width)-cropX,0,cropWidth-1);
                    int ty=Mathf.Clamp(Mathf.FloorToInt(uv.y*texture.height)-cropY,0,cropHeight-1);
                    Color color=pixels[ty*cropWidth+tx];
                    if(color.a<.15f)continue;
                    list.Add(convert(new BodyVisualSample {point=new Vector3(point.x,point.y,0),
                        size=new Vector3(sx/ppu,sy/ppu,1),
                        color=color,height=y/(float)height,phase=(x*17+y*31)*.017f}));
                }
                return list.ToArray();
            }
            finally{RenderTexture.active=previous;if(temporary)RenderTexture.ReleaseTemporary(temporary);Release(readable);}
        }
        private static void Release(UnityEngine.Object value)
        {
            if (!value) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
