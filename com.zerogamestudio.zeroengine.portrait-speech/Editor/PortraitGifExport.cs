using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object=UnityEngine.Object;

namespace ZeroEngine.PortraitSpeech.Editor
{
    internal sealed class PortraitPreviewPlayback
    {
        private const string Demo="你好，通讯已接通。a o e m";
        public readonly PortraitSpeechMotion Motion=new();
        private int character;
        private double next;
        public void Reset(){Motion.Reset();character=0;next=0;}

        public static float DemoLength
        {
            get
            {
                float time=.55f;
                foreach(char letter in Demo)time+=Delay(letter);
                return time;
            }
        }

        public void Advance(double now,float delta,PortraitSpeechLibrary style)
        {
            if(now>=next)
            {
                if(character<Demo.Length)
                {
                    char letter=Demo[character];
                    Motion.Reveal(letter,character++,(float)now,style.portraitSpeechFramesPerSecond,style.portraitSpeechHold);
                    next=now+Delay(letter);
                }
                else{Motion.Complete(false);character=0;next=now+.8;}
            }
            Motion.Advance((float)now,delta,style.portraitMouthOpenTime,style.portraitMouthCloseTime,style.portraitMouthTransition);
        }

        private static float Delay(char letter)=>char.IsPunctuation(letter)?.35f:char.IsWhiteSpace(letter)?.16f:.09f;
    }

    internal static class PortraitPreviewSurface
    {
        public static void Apply(Material material,Texture2D canvas,PortraitSpeechProfile draft,PortraitBlinkMaterial eyes,
            int frame,int previous,float openness,float blend,float blink)
        {
            material.SetFloat("_Layer",2);material.SetFloat("_EditorPreview",1);material.SetFloat("_Connection",1);
            material.SetColor("_Color",Color.white);material.SetFloat("_GlitchStrength",0);material.SetFloat("_SignalStrength",0);
            material.SetVector("_UvRect",new Vector4(0,0,1,1));material.SetVector("_SpritePadding",Vector4.zero);
            material.SetVector("_SourceSize",new Vector4(canvas.width,canvas.height,0,0));
            material.SetFloat("_MouthFrame",draft.HasMouth?frame:0);material.SetFloat("_MouthPrevious",previous);
            material.SetFloat("_MouthOpen",openness);material.SetFloat("_MouthBlend",blend);
            PortraitSpeechMaterial.ApplyProfile(material,draft);
            // Authoring still previews the calibration while in-game playback is paused.
            draft.blink.playbackDisabled=false;
            eyes.Apply(material,draft.blink,blink,new Vector2Int(canvas.width,canvas.height));
        }
    }

    internal static class PortraitGifExport
    {
        private const int MaxEdge=320,DelayCentiseconds=2;
        private const float Step=DelayCentiseconds*.01f;

        public static float Write(string path,Texture2D canvas,PortraitSpeechLibrary style,PortraitSpeechProfile draft,bool speech,bool blink)
        {
            if(!style)throw new InvalidOperationException("请先选择统一样式。");
            Validate(canvas,draft,speech,blink);
            if(!string.Equals(Path.GetExtension(path),".gif",StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("请选择 .gif 文件。",nameof(path));
            path=Path.GetFullPath(path);
            var destination=new FileInfo(path);
            bool replace=destination.Exists;
            DateTime previousWrite=replace?destination.LastWriteTimeUtc:default;
            long previousSize=replace?destination.Length:0;
            var shader=Shader.Find(PortraitSpeechMaterial.ShaderName);
            if(!shader||!shader.isSupported)throw new InvalidOperationException("头像预览着色器不可用。");

            float scale=Mathf.Min(2f,(float)MaxEdge/Mathf.Max(canvas.width,canvas.height));
            int width=Mathf.Max(1,Mathf.RoundToInt(canvas.width*scale)),height=Mathf.Max(1,Mathf.RoundToInt(canvas.height*scale));
            float lead=speech?.7f:.3f;
            float duration=speech?PortraitPreviewPlayback.DemoLength+.14f:lead+Mathf.Clamp(style.blinkTiming.duration,.06f,.5f)+.55f;
            int count=Mathf.CeilToInt(duration/Step);
            var frames=Render(canvas,style,draft,shader,width,height,count,speech,blink,lead);
            if(speech&&!HasMotion(frames,width,height,draft.mouth))
                throw new InvalidOperationException("未检测到口型动作。请检查嘴部框、像素口型和肤色采样。");
            if(blink&&!HasMotion(frames,width,height,draft.blink.leftEye.area)&&!HasMotion(frames,width,height,draft.blink.rightEye.area))
                throw new InvalidOperationException("未检测到眨眼动作。请检查眼框和保护像素。");

            string temporary=Path.Combine(Path.GetDirectoryName(path),".portrait-gif-"+Guid.NewGuid().ToString("N")+".tmp");
            try
            {
                using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write))
                {
                    if(PortraitGifEncoder.Write(stream,width,height,frames,DelayCentiseconds)<2)
                        throw new InvalidOperationException("GIF 压缩后只剩静止画面。请增大口型或眼睑变化后重试。");
                }
                if(replace)
                {
                    destination.Refresh();
                    if(!destination.Exists||destination.LastWriteTimeUtc!=previousWrite||destination.Length!=previousSize)
                        throw new IOException("目标 GIF 在导出期间已被修改，请重新选择。");
                    File.Replace(temporary,path,null);
                }
                else File.Move(temporary,path);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
            return count*Step;
        }

        internal static void Validate(Texture2D canvas,PortraitSpeechProfile draft,bool speech,bool blink)
        {
            if(!canvas||(!speech&&!blink))throw new InvalidOperationException("请先标定要导出的动作。");
            if(speech&&!DialoguePortraitAuthoringUtility.RegionOnCanvas(draft.mouth))
                throw new InvalidOperationException("嘴部框需要位于原图内，请调整后再导出。");
            if(speech&&!DialoguePortraitAuthoringUtility.MouthPoseSheetMatches(draft,canvas.width,canvas.height))
                throw new InvalidOperationException("口型贴片尺寸需要等于嘴部宽度 × 两倍嘴部高度。");
            draft.blink.playbackDisabled=false;
            if(blink&&!draft.blink.CanRender(new Vector2Int(canvas.width,canvas.height)))
                throw new InvalidOperationException("请启用眨眼、框选眼区，并检查保护像素是否与当前尺寸一致。");
        }

        private static bool HasMotion(IReadOnlyList<Color32[]> frames,int width,int height,Rect region)
        {
            if(!DialoguePortraitAuthoringUtility.RegionOnCanvas(region))return false;
            int x0=Mathf.Clamp(Mathf.FloorToInt(region.xMin*width),0,width-1),x1=Mathf.Min(width,Mathf.CeilToInt(region.xMax*width));
            int y0=Mathf.Clamp(Mathf.FloorToInt(region.yMin*height),0,height-1),y1=Mathf.Min(height,Mathf.CeilToInt(region.yMax*height));
            var rest=frames[0];Color32[] previous=rest;
            for(int i=1;i<frames.Count;i++)
            {
                var frame=frames[i];
                if(ReferenceEquals(frame,previous))continue;
                previous=frame;
                for(int y=y0;y<y1;y++)
                for(int x=x0;x<x1;x++)
                {
                    int index=y*width+x;
                    if(!frame[index].Equals(rest[index]))return true;
                }
            }
            return false;
        }

        private static List<Color32[]> Render(Texture2D canvas,PortraitSpeechLibrary style,PortraitSpeechProfile draft,Shader shader,
            int width,int height,int count,bool speech,bool blink,float lead)
        {
            var target=RenderTexture.GetTemporary(width,height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear);
            var material=new Material(shader){hideFlags=HideFlags.HideAndDontSave,mainTexture=canvas};
            var pixels=new Texture2D(width,height,TextureFormat.RGBA32,false,true){hideFlags=HideFlags.HideAndDontSave};
            var prior=RenderTexture.active;bool srgb=GL.sRGBWrite;
            var playback=new PortraitPreviewPlayback();
            var frames=new List<Color32[]>(count);
            using var eyes=new PortraitBlinkMaterial();
            (int,int,float,float,float) previous=default;
            try
            {
                for(int i=0;i<count;i++)
                {
                    float time=i*Step;
                    if(speech&&time>=.14f)playback.Advance(time-.14f,Step,style);
                    var motion=playback.Motion;
                    int frame=motion.Frame;
                    float amount=blink&&time>=lead?PortraitBlinkMotion.Evaluate(Mathf.Max(0,style.blinkTiming.firstDelay)+time-lead,style.blinkTiming):0;
                    var state=(frame,frame==0?0:motion.PreviousFrame,frame==0?0:motion.Openness,frame==0?1:motion.Blend,amount);
                    if(frames.Count>0&&state.Equals(previous)){frames.Add(frames[frames.Count-1]);continue;}
                    previous=state;
                    PortraitPreviewSurface.Apply(material,canvas,draft,eyes,state.Item1,state.Item2,state.Item3,state.Item4,state.Item5);
                    RenderTexture.active=target;GL.sRGBWrite=false;GL.Clear(true,true,new Color(.09f,.11f,.15f,1));GL.PushMatrix();
                    try
                    {
                        GL.LoadPixelMatrix(0,width,0,height);
                        if(!material.SetPass(0))throw new InvalidOperationException("头像预览渲染失败。");
                        GL.Begin(GL.QUADS);GL.Color(Color.white);
                        GL.TexCoord2(0,0);GL.Vertex3(0,0,0);GL.TexCoord2(1,0);GL.Vertex3(width,0,0);
                        GL.TexCoord2(1,1);GL.Vertex3(width,height,0);GL.TexCoord2(0,1);GL.Vertex3(0,height,0);GL.End();
                    }
                    finally{GL.PopMatrix();}
                    pixels.ReadPixels(new Rect(0,0,width,height),0,0);
                    frames.Add(pixels.GetPixels32());
                }
            }
            finally
            {
                RenderTexture.active=prior;GL.sRGBWrite=srgb;RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(material);Object.DestroyImmediate(pixels);
            }
            return frames;
        }
    }
}
