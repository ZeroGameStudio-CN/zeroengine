using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.PortraitSpeech.Editor
{
    public static class DialoguePortraitAuthoringUtility
    {
        public sealed class Avatar
        {
            public int ActorId;
            public string ActorName;
            public Sprite Sprite;
            public string Label=>ActorName+" · "+Sprite.name;
        }

        public static List<Avatar> Collect(PortraitSpeechLibrary style,ScriptableObject database)
        {
            var result=new List<Avatar>();var keys=new HashSet<string>(StringComparer.Ordinal);
            void Add(int id,string name,Sprite sprite)
            {
                if(style)sprite=style.ResolveAuthoringPortrait(id,sprite);
                if(sprite&&keys.Add(id+":"+sprite.name))result.Add(new Avatar{ActorId=id,ActorName=name,Sprite=sprite});
            }
            if(database)
            {
                using(var data=new SerializedObject(database))
                {
                    var actors=data.FindProperty("actors");
                    if(actors!=null&&actors.isArray)for(int i=0;i<actors.arraySize;i++)
                    {
                        var actor=actors.GetArrayElementAtIndex(i);var idProperty=actor.FindPropertyRelative("id");if(idProperty==null)continue;int id=idProperty.intValue;string name=id.ToString();
                        var fields=actor.FindPropertyRelative("fields");
                        if(fields!=null)for(int f=0;f<fields.arraySize;f++)
                        {
                            var field=fields.GetArrayElementAtIndex(f);string title=field.FindPropertyRelative("title").stringValue;
                            if(title=="Name"||title=="Display Name")name=field.FindPropertyRelative("value").stringValue;
                        }
                        Add(id,name,actor.FindPropertyRelative("spritePortrait")?.objectReferenceValue as Sprite);
                        var alternates=actor.FindPropertyRelative("spritePortraits");
                        if(alternates!=null)for(int a=0;a<alternates.arraySize;a++)Add(id,name,alternates.GetArrayElementAtIndex(a).objectReferenceValue as Sprite);
                    }
                }
            }
            if(style&&style.portraitSpeech!=null)foreach(var profile in style.portraitSpeech)
            {
                if(result.Any(a=>a.ActorId==profile.actorId&&a.Sprite.name==profile.spriteName))continue;
                foreach(string guid in AssetDatabase.FindAssets(profile.spriteName+" t:Sprite",new[]{"Assets"}))
                {
                    var sprite=AssetDatabase.LoadAssetAtPath<Sprite>(AssetDatabase.GUIDToAssetPath(guid));
                    if(sprite&&sprite.name==profile.spriteName){Add(profile.actorId,profile.actorId.ToString(),sprite);break;}
                }
            }
            return result;
        }

        public static bool TryFind(PortraitSpeechLibrary style,int actor,string sprite,out PortraitSpeechProfile entry)
        {
            if(style)return style.TryGetPortraitProfile(actor,sprite,out entry);
            entry=default;return false;
        }

        public static Texture2D ReadCanvas(Sprite sprite)
        {
            if(!sprite)throw new ArgumentNullException(nameof(sprite));
            string asset=AssetDatabase.GetAssetPath(sprite);string path=Path.Combine(Directory.GetParent(Application.dataPath).FullName,asset);
            if(!File.Exists(path)||new FileInfo(path).Length>64*1024*1024)throw new InvalidOperationException("请选择可读取的 PNG/JPG 头像源图。");
            var decoded=new Texture2D(2,2,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave};
            try
            {
                if(!ImageConversion.LoadImage(decoded,File.ReadAllBytes(path),false))throw new InvalidOperationException("此源图不能直接采样，请使用 PNG/JPG 头像。");
                int w=Mathf.RoundToInt(sprite.rect.width),h=Mathf.RoundToInt(sprite.rect.height);
                if(w<=0||h<=0||(long)w*h>4*1024*1024)throw new InvalidOperationException("请选择独立头像裁片。");
                var imported=AssetDatabase.LoadAssetAtPath<Texture2D>(asset);
                if(!imported)throw new InvalidOperationException("找不到头像源贴图。");
                float sx=(float)decoded.width/imported.width,sy=(float)decoded.height/imported.height;
                var pixels=decoded.GetPixels32();var output=new Color32[w*h];
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                {
                    int px=Mathf.Clamp(Mathf.FloorToInt((sprite.rect.x+x+.5f)*sx),0,decoded.width-1);
                    int py=Mathf.Clamp(Mathf.FloorToInt((sprite.rect.y+y+.5f)*sy),0,decoded.height-1);
                    output[y*w+x]=pixels[py*decoded.width+px];
                }
                var canvas=new Texture2D(w,h,TextureFormat.RGBA32,false){hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                canvas.SetPixels32(output);canvas.Apply(false,false);return canvas;
            }
            finally{UnityEngine.Object.DestroyImmediate(decoded);}
        }

        internal static bool MouthPoseSheetMatches(PortraitSpeechProfile profile,int width,int height)
        {
            var sheet=profile.mouthPoseSheet;
            return !sheet||RegionOnCanvas(profile.mouth)&&sheet.width==Mathf.RoundToInt(profile.mouth.width*width)
                &&sheet.height==2*Mathf.RoundToInt(profile.mouth.height*height);
        }

        public static Rect Snap(Rect value,int width,int height)
        {
            int x=Mathf.Clamp(Mathf.RoundToInt(value.x*width),0,width-1),y=Mathf.Clamp(Mathf.RoundToInt(value.y*height),0,height-1);
            int w=Mathf.Clamp(Mathf.RoundToInt(value.width*width),1,width-x),h=Mathf.Clamp(Mathf.RoundToInt(value.height*height),1,height-y);
            return new Rect((float)x/width,(float)y/height,(float)w/width,(float)h/height);
        }
        public static Rect ToGui(Rect mouth,Rect canvas)=>new Rect(canvas.x+mouth.x*canvas.width,canvas.yMax-mouth.yMax*canvas.height,mouth.width*canvas.width,mouth.height*canvas.height);
        public static Rect FromGui(Rect mouth,Rect canvas,int width,int height)=>Snap(new Rect((mouth.x-canvas.x)/canvas.width,(canvas.yMax-mouth.yMax)/canvas.height,mouth.width/canvas.width,mouth.height/canvas.height),width,height);
        internal static bool RegionOnCanvas(Rect rect)=>rect.width>0&&rect.height>0&&
            rect.xMin>=0&&rect.yMin>=0&&rect.xMax<=1.00001f&&rect.yMax<=1.00001f;

        public static PortraitSpeechProfile Propose(PortraitSpeechLibrary style,int actor,Sprite sprite,Texture2D canvas,out string origin)
        {
            if(TryFind(style,actor,sprite.name,out var existing)&&existing.HasMouth){origin="已有配置";return existing;}
            var blink=existing.blink;
            Rect face=new Rect(.25f,.4f,.5f,.4f);
            if(style.TryGetFaceBounds(actor,sprite.name,out var hint))face=hint;
            Vector2 center=new Vector2(face.center.x,face.y+face.height*.23f);
            var search=new Rect(face.x+face.width*.2f,face.y+face.height*.08f,face.width*.6f,face.height*.42f);
            var pixels=canvas.GetPixels32();float best=.045f;Vector2 point=center;Color lip=new Color(.6f,.2f,.28f);bool found=false;
            int xmin=Mathf.Clamp(Mathf.FloorToInt(search.xMin*canvas.width),0,canvas.width-1),xmax=Mathf.Clamp(Mathf.CeilToInt(search.xMax*canvas.width),0,canvas.width);
            int ymin=Mathf.Clamp(Mathf.FloorToInt(search.yMin*canvas.height),0,canvas.height-1),ymax=Mathf.Clamp(Mathf.CeilToInt(search.yMax*canvas.height),0,canvas.height);
            for(int y=ymin;y<ymax;y++)for(int x=xmin;x<xmax;x++)
            {
                Color c=pixels[y*canvas.width+x];if(c.a<.8f)continue;
                var uv=new Vector2((x+.5f)/canvas.width,(y+.5f)/canvas.height);
                float red=Mathf.Max(0,c.r-Mathf.Max(c.g,c.b))/Mathf.Max(.01f,c.r);
                float dark=1-(c.r*.299f+c.g*.587f+c.b*.114f);
                float distance=Vector2.Scale(uv-center,new Vector2(1/Mathf.Max(.01f,face.width),1/Mathf.Max(.01f,face.height))).sqrMagnitude;
                float score=red*dark/(1+distance*5);
                if(score>best){best=score;point=uv;lip=c;found=true;}
            }
            existing=new PortraitSpeechProfile{actorId=actor,spriteName=sprite.name,blink=blink,
                mouth=Snap(new Rect(point.x-2.5f/canvas.width,point.y-2.5f/canvas.height,6f/canvas.width,5f/canvas.height),canvas.width,canvas.height),
                skinSample=new Vector2(Mathf.Clamp01(point.x-4f/canvas.width),point.y),mouthColor=Color.Lerp(lip,Color.black,.25f),tongueColor=new Color(.9f,.48f,.55f),teethColor=new Color(.98f,.94f,.9f,1)};
            existing.mouthColor.a=1;origin=found?"颜色候选草稿，请预览确认":"脸部比例初值，请框选嘴部";return existing;
        }

        public static int Write(PortraitSpeechLibrary style,IEnumerable<PortraitSpeechProfile> entries,bool replace)
            => WritePart(style,entries,replace,false);

        public static int WriteBlink(PortraitSpeechLibrary style,IEnumerable<PortraitSpeechProfile> entries,bool replace)
            => WritePart(style,entries,replace,true);

        private static int WritePart(PortraitSpeechLibrary style,IEnumerable<PortraitSpeechProfile> entries,bool replace,bool blink)
        {
            if(!style)throw new ArgumentNullException(nameof(style));
            if(EditorApplication.isPlaying)throw new InvalidOperationException("请退出运行模式后保存头像配置。");
            var list=new List<PortraitSpeechProfile>(style.portraitSpeech??Array.Empty<PortraitSpeechProfile>());int changed=0;
            foreach(var entry in entries)
            {
                if(string.IsNullOrEmpty(entry.spriteName)||(!blink&&!entry.HasMouth))continue;
                if(!blink&&entry.mouthPoseSheet&&!AssetDatabase.Contains(entry.mouthPoseSheet))
                    throw new InvalidOperationException("贴片仍是窗口草稿，请先使用“保存新贴片并应用”。");
                int index=list.FindIndex(p=>p.actorId==entry.actorId&&p.spriteName==entry.spriteName);
                var saved=index>=0?list[index]:default;
                if(!replace&&(blink?saved.blink.HasEyes:saved.HasMouth))continue;
                if(blink&&!entry.blink.HasEyes&&index<0)continue;
                var updated=entry;
                if(blink)
                {
                    updated=saved;updated.actorId=entry.actorId;updated.spriteName=entry.spriteName;updated.blink=entry.blink;
                    updated.blink.playbackDisabled=saved.blink.playbackDisabled;
                }
                else{updated.blink=saved.blink;updated.speechDisabled=saved.speechDisabled;}
                if(index>=0)list[index]=updated;else list.Add(updated);
                changed++;
            }
            if(changed==0)return 0;
            SaveProfiles(style,list.ToArray(),blink?"配置对话头像眨眼":"配置对话头像口型");
            return changed;
        }

        /// <summary>Pause or resume authored channels without touching geometry, masks, or disabled expressions.</summary>
        public static int SetPlayback(PortraitSpeechLibrary style,int? actor,bool speech,bool blink,bool play)
        {
            if(!style)throw new ArgumentNullException(nameof(style));
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("请在编辑模式管理头像动画。");
            if((!speech&&!blink)||style.portraitSpeech==null)return 0;
            var profiles=(PortraitSpeechProfile[])style.portraitSpeech.Clone();int changed=0;
            for(int i=0;i<profiles.Length;i++)
            {
                var entry=profiles[i];
                if(actor.HasValue&&entry.actorId!=actor.Value)continue;
                bool mouthChanged=speech&&entry.HasMouth&&entry.speechDisabled==play;
                bool blinkChanged=blink&&entry.blink.enabled&&entry.blink.HasEyes&&entry.blink.playbackDisabled==play;
                if(!mouthChanged&&!blinkChanged)continue;
                if(mouthChanged)entry.speechDisabled=!play;
                if(blinkChanged)entry.blink.playbackDisabled=!play;
                profiles[i]=entry;changed++;
            }
            if(changed>0)SaveProfiles(style,profiles,play?"批量恢复头像动画":"批量停用头像动画");
            return changed;
        }

        private static void SaveProfiles(PortraitSpeechLibrary style,PortraitSpeechProfile[] profiles,string operation)
        {
            Undo.RecordObject(style,operation);style.portraitSpeech=profiles;EditorUtility.SetDirty(style);
            if(AssetDatabase.Contains(style))AssetDatabase.SaveAssetIfDirty(style);
        }
    }
}
