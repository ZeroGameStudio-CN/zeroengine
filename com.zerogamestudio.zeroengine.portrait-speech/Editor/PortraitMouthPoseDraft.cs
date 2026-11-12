using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace ZeroEngine.PortraitSpeech.Editor
{
    /// <summary>Native-pixel candidates and brush edits. No source image or library is written by a draft.</summary>
    public sealed class PortraitMouthPoseDraft : IDisposable
    {
        private readonly Color32[] source;
        private Color32[] patches;
        private readonly Stack<Color32[]> undo=new(),redo=new();
        private Color32[] stroke;
        public Vector2Int CanvasSize{get;}
        public RectInt Region{get;}
        public Texture2D Texture{get;}
        public bool CanUndo=>undo.Count>0;
        public bool CanRedo=>redo.Count>0;
        public Color32[] Palette{get;}
        public int Revision{get;private set;}

        public PortraitMouthPoseDraft(Texture2D canvas,Rect mouth)
        {
            if(!canvas||!DialoguePortraitAuthoringUtility.RegionOnCanvas(mouth))
                throw new ArgumentException("请先框选画布内的嘴部。");
            CanvasSize=new Vector2Int(canvas.width,canvas.height);
            Region=new RectInt(Mathf.RoundToInt(mouth.x*canvas.width),Mathf.RoundToInt(mouth.y*canvas.height),
                Mathf.RoundToInt(mouth.width*canvas.width),Mathf.RoundToInt(mouth.height*canvas.height));
            if(Region.width<2||Region.height<2||Region.width>128||Region.height>128||
                Region.width*Region.height>4096||Region.xMin<0||Region.yMin<0||Region.xMax>canvas.width||Region.yMax>canvas.height)
                throw new ArgumentException("嘴部区域需要至少 2×2 像素，单边不超过 128 像素，总面积不超过 4096 像素。");
            source=canvas.GetPixels32();patches=new Color32[Region.width*Region.height*2];
            Palette=Enumerable.Range(0,Region.width*Region.height).Select(i=>Original(i%Region.width,i/Region.width))
                .Where(c=>c.a==255).GroupBy(c=>c).OrderByDescending(g=>g.Count()).Take(14).Select(g=>g.Key).ToArray();
            Texture=new Texture2D(Region.width,Region.height*2,TextureFormat.RGBA32,false,false)
            {name="Mouth pose draft",hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            Upload();
        }

        public Color32 Original(int x,int y)=>source[(Region.y+y)*CanvasSize.x+Region.x+x];
        public Color32 Composed(int pose,int x,int y)
        {
            var p=patches[Index(pose,x,y)];return p.a==0?Original(x,y):p;
        }
        private int Index(int pose,int x,int y)
        {
            if(pose<0||pose>1||x<0||x>=Region.width||y<0||y>=Region.height)throw new ArgumentOutOfRangeException();
            return (pose*Region.height+y)*Region.width+x;
        }

        public void BeginStroke(){if(stroke==null)stroke=(Color32[])patches.Clone();}
        public void Paint(int pose,int x,int y,Color32? color)
        {
            int i=Index(pose,x,y);var c=color??default;c.a=color.HasValue?(byte)255:(byte)0;
            if(c.a!=0&&c.Equals(Original(x,y)))c=default;
            if(patches[i].Equals(c))return;
            BeginStroke();patches[i]=c;Revision++;Upload();
        }
        public void EndStroke()
        {
            if(stroke==null)return;
            if(!stroke.SequenceEqual(patches)){undo.Push(stroke);redo.Clear();TrimUndo();}
            stroke=null;
        }
        public bool Undo(){EndStroke();if(!CanUndo)return false;redo.Push(patches);patches=undo.Pop();Revision++;Upload();return true;}
        public bool Redo(){EndStroke();if(!CanRedo)return false;undo.Push(patches);patches=redo.Pop();Revision++;Upload();return true;}
        private void TrimUndo()
        {
            if(undo.Count<=32)return;
            var keep=undo.Take(32).Reverse().ToArray();undo.Clear();foreach(var state in keep)undo.Push(state);
        }
        private void Upload(){Texture.SetPixels32(patches);Texture.Apply(false,false);}

        public void LoadSheet(Texture2D sheet)
        {
            if(!sheet||sheet.width!=Region.width||sheet.height!=Region.height*2)
                throw new ArgumentException("贴片尺寸与嘴部区域不一致。");
            // Decode a separate copy; imported PNGs remain non-readable on the runtime side.
            var path=AssetDatabase.GetAssetPath(sheet);Texture2D decoded=null;
            try
            {
                if(!string.IsNullOrEmpty(path))
                {
                    decoded=new Texture2D(2,2,TextureFormat.RGBA32,false,false);
                    if(!ImageConversion.LoadImage(decoded,File.ReadAllBytes(path),false))throw new IOException("无法读取已配置贴片。");
                }
                var data=(decoded?decoded:sheet).GetPixels32();
                if(data.Length!=patches.Length||data.Any(c=>c.a!=0&&c.a!=255))throw new ArgumentException("请使用原像素尺寸、无半透明边缘的贴片。");
                BeginStroke();patches=data;Revision++;Upload();EndStroke();
            }
            finally{if(decoded)Object.DestroyImmediate(decoded);}
        }

        public void Generate(Vector2 skinSample,bool sameRow)
        {
            int w=Region.width,h=Region.height;
            int sx=Mathf.Clamp(Mathf.FloorToInt(skinSample.x*CanvasSize.x),0,CanvasSize.x-1);
            int sy=Mathf.Clamp(Mathf.FloorToInt(skinSample.y*CanvasSize.y),0,CanvasSize.y-1);
            Color32 Sample(int y)=>source[(sameRow?Region.y+y:sy)*CanvasSize.x+sx];
            bool Ink(int x,int y)
            {
                var c=Original(x,y);var skin=Sample(y);
                return c.a==255&&skin.a==255&&skin.g-c.g>35&&
                    (c.r>c.g*1.12f||c.r+c.g+c.b<(skin.r+skin.g+skin.b)*.58f);
            }
            // The explicitly calibrated mouth box may contain a deliberately broken
            // pixel outline. All lip pixels inside it belong to the authored expression.
            var best=new List<int>();
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(Ink(x,y))best.Add(y*w+x);
            if(best.Count<4)throw new InvalidOperationException("未找到清晰的张嘴轮廓。请调整嘴框和肤色采样，或先编辑已有贴片。");
            int left=best.Min(i=>i%w),right=best.Max(i=>i%w),bottom=best.Min(i=>i/w),top=best.Max(i=>i/w);
            if(right-left<2||top-bottom<1)throw new InvalidOperationException("当前嘴部接近闭合，适合继续使用通用口型。");
            var mask=new HashSet<int>(best);
            // Restore the bounded original mouth interior, including disconnected
            // highlights/teeth. Region-exterior face pixels are never touched.
            var erase=new HashSet<int>();
            for(int y=bottom;y<=top;y++)for(int x=left;x<=right;x++)
                if(Original(x,y).a==255)erase.Add(y*w+x);
            var tones=best.Select(i=>Original(i%w,i/w)).GroupBy(c=>c).OrderByDescending(g=>g.Count()).ToArray();
            var interior=tones[0].Key;
            var edge=tones.OrderBy(g=>g.Key.r+g.Key.g+g.Key.b).First().Key;
            var lip=Color32.Lerp(edge,interior,.55f);
            float cx=(left+right)*.5f,cy=(float)best.Average(i=>i/w),xx=0,xy=0;
            foreach(int p in best){float x=p%w-cx;xx+=x*x;xy+=x*(p/w-cy);}
            float slope=Mathf.Clamp(xy/Mathf.Max(1,xx),-.8f,.8f);
            Color32 Restore(int x,int y)
            {
                int l=x-1,r=x+1;
                while(l>=0&&erase.Contains(y*w+l))l--;
                while(r<w&&erase.Contains(y*w+r))r++;
                var a=l>=0?Original(l,y):Sample(y);var b=r<w?Original(r,y):Sample(y);
                if(a.a!=255||b.a!=255)throw new InvalidOperationException("嘴部接触透明边缘，请缩小嘴框。");
                return Color32.Lerp(a,b,(float)(x-l)/(r-l));
            }
            var next=new Color32[patches.Length];
            for(int pose=0;pose<2;pose++)
            {
                var composed=new Color32[w*h];
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)composed[y*w+x]=erase.Contains(y*w+x)?Restore(x,y):Original(x,y);
                int breadth=Mathf.Clamp(Mathf.RoundToInt((right-left+1)*(pose==0?.82f:.68f)),3,w);
                int height=pose==0?Mathf.Max(2,Mathf.RoundToInt((top-bottom+1)*.5f)):1;
                int x0=Mathf.Clamp(Mathf.RoundToInt(cx-(breadth-1)*.5f),0,w-breadth),priorBottom=-1,priorTop=-1;
                for(int x=x0;x<x0+breadth;x++)
                {
                    float q=(x-x0+.5f)/breadth*2-1;
                    int column=Mathf.Max(1,Mathf.RoundToInt(height*Mathf.Sqrt(Mathf.Max(0,1-q*q))));
                    float center=cy+slope*(x-cx);
                    int y0=Mathf.Clamp(Mathf.RoundToInt(center-(column-1)*.5f),bottom,Mathf.Max(bottom,top-column+1));
                    int y1=y0+column-1;
                    // Stair steps connect the one-pixel seam instead of leaving diagonal-only dots.
                    if(priorTop>=0){if(y0>priorTop)y0=priorTop;if(y1<priorBottom)y1=priorBottom;}
                    for(int y=y0;y<=y1;y++)
                    {
                        if(Original(x,y).a!=255)throw new InvalidOperationException("候选超出了不透明脸部，请调整嘴框。");
                        composed[y*w+x]=pose==1?lip:(x==x0?edge:interior);
                    }
                    priorBottom=y0;priorTop=y1;
                }
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                {
                    var c=composed[y*w+x];if(!c.Equals(Original(x,y)))next[(pose*h+y)*w+x]=c;
                }
            }
            BeginStroke();patches=next;Revision++;Upload();EndStroke();
        }

        public bool Matches(Texture2D other,Rect mouth)
        {
            if(!other||other.width!=CanvasSize.x||other.height!=CanvasSize.y)return false;
            var r=new RectInt(Mathf.RoundToInt(mouth.x*other.width),Mathf.RoundToInt(mouth.y*other.height),
                Mathf.RoundToInt(mouth.width*other.width),Mathf.RoundToInt(mouth.height*other.height));
            if(r!=Region)return false;
            var pixels=other.GetPixels32();
            for(int y=Mathf.Max(0,r.y-2);y<Mathf.Min(other.height,r.yMax+2);y++)
            for(int x=Mathf.Max(0,r.x-2);x<Mathf.Min(other.width,r.xMax+2);x++)
                if(!source[y*CanvasSize.x+x].Equals(pixels[y*CanvasSize.x+x]))return false;
            return true;
        }

        public Texture2D SaveNewAsset(string path)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("请在编辑模式保存贴片。");
            path=path?.Replace('\\','/');
            if(string.IsNullOrWhiteSpace(path)||!path.StartsWith("Assets/",StringComparison.Ordinal)||
                !path.EndsWith(".png",StringComparison.OrdinalIgnoreCase)||path.Split('/').Any(p=>p==".."||p=="."))
                throw new ArgumentException("请选择 Assets 内的新 PNG 文件。");
            if(!Directory.Exists(Path.GetDirectoryName(path))||File.Exists(path)||File.Exists(path+".meta")||AssetDatabase.LoadMainAssetAtPath(path))
                throw new IOException("目标已存在或目录不可用。请使用新文件名，已有素材不会被覆盖。");
            EndStroke();
            using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            {var png=Texture.EncodeToPNG();stream.Write(png,0,png.Length);}
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
            var importer=AssetImporter.GetAtPath(path) as TextureImporter;
            if(!importer)throw new IOException("贴片未能导入，PNG 已保留："+path);
            importer.textureType=TextureImporterType.Default;importer.filterMode=FilterMode.Point;importer.wrapMode=TextureWrapMode.Clamp;
            importer.mipmapEnabled=false;importer.npotScale=TextureImporterNPOTScale.None;importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=false;importer.sRGBTexture=true;importer.isReadable=false;
            importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public void Dispose(){if(Texture)Object.DestroyImmediate(Texture);}
    }
}
