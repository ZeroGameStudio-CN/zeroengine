using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace ZeroEngine.PortraitSpeech.Editor
{
    /// <summary>Persistent source stamps and reviewed bulk updates. Playback never reads this state.</summary>
    public sealed class PortraitLibraryMaintenance : IDisposable
    {
        private const string StateField="portraitAuthoringStateJson";
        [Serializable] private sealed class State
        { public int schema=1;public List<Stamp> entries=new(); }
        [Serializable] private sealed class Stamp
        {
            public int actor,width,height,kind;
            public string sprite,guid,source,calibration,sheetGuid,sheetHash,recipe;
        }
        public enum Change { Current,Untracked,NeedsSheet,NewPortrait,SourceChanged,CalibrationChanged,ToolChanged,SheetEdited,CanvasChanged,Inactive,MissingSource }
        public sealed class Entry
        {
            public DialoguePortraitAuthoringUtility.Avatar Avatar;
            public PortraitSpeechProfile Before,Proposed;
            public string SpriteName,SourceGuid,SourceHash,BeforeCalibration,BeforeSheetHash,BeforeStamp;
            public bool HadProfile,Protected,Complex,Confirmed,Applied;
            public Change Status;
            public PortraitMouthPoseDraft Draft;
            public int GeneratedRevision,ConfirmedRevision;
            public string Error;
            public string Key=>Before.actorId+":"+SpriteName;
            public string Label=>Avatar?.Sprite?Avatar.Label:Before.actorId+" · "+SpriteName;
            public bool NeedsWork=>!Applied&&Status!=Change.Current&&(Status!=Change.Inactive||string.IsNullOrEmpty(BeforeStamp));
            public bool CanGenerate=>Avatar?.Sprite&&Before.CanSpeak&&!Protected&&Complex&&Status!=Change.Current&&Status!=Change.CanvasChanged;
        }
        private readonly PortraitSpeechLibrary library;
        public readonly List<Entry> Entries=new();
        public readonly List<string> CreatedAssets=new();
        public static string Recipe
        {
            get
            {
                var method=typeof(PortraitMouthPoseDraft).GetMethod(nameof(PortraitMouthPoseDraft.Generate));
                var flags=System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic;
                byte[] lip=typeof(PortraitLibraryMaintenance).GetMethod(nameof(CompleteLip),flags).GetMethodBody().GetILAsByteArray();
                return "mouth-candidate-1/"+Digest(method.GetMethodBody().GetILAsByteArray().Concat(lip).ToArray());
            }
        }
        private PortraitLibraryMaintenance(PortraitSpeechLibrary library){this.library=library;}
        private static string Digest(byte[] value)
        {using var sha=SHA256.Create();return BitConverter.ToString(sha.ComputeHash(value)).Replace("-","").ToLowerInvariant();}
        private static string HashAsset(Object asset)
        {
            if(!asset)return "";
            string path=AssetDatabase.GetAssetPath(asset),hash=AssetDatabase.GetAssetDependencyHash(path).ToString();
            // A paused auto-refresh must not hide bytes edited on disk.
            return File.Exists(path)?hash+"/"+Digest(File.ReadAllBytes(path)):hash;
        }
        private static string Guid(Object asset)=>asset?AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset)):"";
        private static string Calibration(PortraitSpeechProfile p)
        {
            // Session playback switches do not invalidate the artist's calibration.
            p.speechDisabled=false;p.blink.playbackDisabled=false;p.mouthPoseSheet=null;
            return Digest(Encoding.UTF8.GetBytes(JsonUtility.ToJson(p)));
        }
        private static State Read(PortraitSpeechLibrary library)
        {
            using var serialized=new SerializedObject(library);
            var field=serialized.FindProperty(StateField)??throw new InvalidOperationException("头像包需要重新编译，尚未载入全库版本记录字段。");
            if(string.IsNullOrEmpty(field.stringValue))return new State();
            var state=JsonUtility.FromJson<State>(field.stringValue);
            if(state==null||state.schema!=1||state.entries==null||state.entries.Any(s=>s==null)||
                state.entries.Select(s=>s.actor+":"+s.sprite).Distinct().Count()!=state.entries.Count)
                throw new InvalidOperationException("全库版本记录无效，原记录已保留。请先检查配置。");
            return state;
        }
        private static string StampJson(Stamp stamp)=>stamp==null?"":JsonUtility.ToJson(stamp);
        private static Stamp Find(State state,int actor,string name)=>state.entries.Find(s=>s.actor==actor&&s.sprite==name);
        private static RectInt Region(PortraitSpeechProfile p,int w,int h)=>new(Mathf.RoundToInt(p.mouth.x*w),Mathf.RoundToInt(p.mouth.y*h),
            Mathf.RoundToInt(p.mouth.width*w),Mathf.RoundToInt(p.mouth.height*h));
        private static bool ComplexMouth(Texture2D canvas,PortraitSpeechProfile p)
        {
            if(!p.CanSpeak||!DialoguePortraitAuthoringUtility.RegionOnCanvas(p.mouth))return false;
            var r=Region(p,canvas.width,canvas.height);if(r.width<5||r.height<3)return false;
            var pixels=canvas.GetPixels32();int sx=Mathf.Clamp((int)(p.skinSample.x*canvas.width),0,canvas.width-1);
            int sy=Mathf.Clamp((int)(p.skinSample.y*canvas.height),0,canvas.height-1),n=0;
            var min=new Vector2Int(int.MaxValue,int.MaxValue);var max=new Vector2Int(-1,-1);
            for(int y=r.y;y<r.yMax;y++)for(int x=r.x;x<r.xMax;x++)
            {
                if(x<0||y<0||x>=canvas.width||y>=canvas.height)return false;
                var c=pixels[y*canvas.width+x];var skin=pixels[(p.sampleSkinByRow?y:sy)*canvas.width+sx];
                if(c.a!=255||skin.a!=255||skin.g-c.g<=35||!(c.r>c.g*1.12f||c.r+c.g+c.b<(skin.r+skin.g+skin.b)*.58f))continue;
                n++;min=Vector2Int.Min(min,new(x,y));max=Vector2Int.Max(max,new(x,y));
            }
            return n>=12&&max.x-min.x>=4&&max.y-min.y>=2;
        }

        private static PortraitSpeechProfile CompleteLip(Texture2D canvas,PortraitSpeechProfile p)
        {
            if(!p.CanSpeak||p.mouthPoseSheet||!DialoguePortraitAuthoringUtility.RegionOnCanvas(p.mouth))return p;
            var r=Region(p,canvas.width,canvas.height);var pixels=canvas.GetPixels32();
            int x0=Mathf.Max(0,r.x-2),x1=Mathf.Min(canvas.width,r.xMax+2),y0=Mathf.Max(0,r.y-2),y1=Mathf.Min(canvas.height,r.yMax+2);
            int sx=Mathf.Clamp((int)(p.skinSample.x*canvas.width),0,canvas.width-1),sy=Mathf.Clamp((int)(p.skinSample.y*canvas.height),0,canvas.height-1);
            bool Ink(int x,int y)
            {
                var c=pixels[y*canvas.width+x];var skin=pixels[(p.sampleSkinByRow?y:sy)*canvas.width+sx];
                return c.a==255&&skin.a==255&&skin.g-c.g>35&&(c.r>c.g*1.12f||c.r+c.g+c.b<(skin.r+skin.g+skin.b)*.58f);
            }
            var remaining=new HashSet<int>();
            for(int y=y0;y<y1;y++)for(int x=x0;x<x1;x++)if(Ink(x,y))remaining.Add(y*canvas.width+x);
            var best=new List<int>();
            while(remaining.Count>0)
            {
                int seed=remaining.First();remaining.Remove(seed);var queue=new Queue<int>();queue.Enqueue(seed);var points=new List<int>();
                while(queue.Count>0)
                {
                    int i=queue.Dequeue();points.Add(i);int x=i%canvas.width,y=i/canvas.width;
                    void Add(int nx,int ny)
                    {if(nx<x0||nx>=x1||ny<y0||ny>=y1)return;int n=ny*canvas.width+nx;if(remaining.Remove(n))queue.Enqueue(n);}
                    Add(x-1,y);Add(x+1,y);Add(x,y-1);Add(x,y+1);
                    Add(x-1,y-1);Add(x+1,y-1);Add(x-1,y+1);Add(x+1,y+1);
                }
                if(points.Count>best.Count&&points.Any(i=>r.Contains(new Vector2Int(i%canvas.width,i/canvas.width))))best=points;
            }
            if(best.Count<4)return p;
            int left=Mathf.Min(r.x,best.Min(i=>i%canvas.width)),right=Mathf.Max(r.xMax,best.Max(i=>i%canvas.width)+1);
            int bottom=Mathf.Min(r.y,best.Min(i=>i/canvas.width)),top=Mathf.Max(r.yMax,best.Max(i=>i/canvas.width)+1);
            p.mouth=new Rect((float)left/canvas.width,(float)bottom/canvas.height,(float)(right-left)/canvas.width,(float)(top-bottom)/canvas.height);
            return p;
        }

        public static PortraitLibraryMaintenance Scan(PortraitSpeechLibrary library,IEnumerable<DialoguePortraitAuthoringUtility.Avatar> avatars)
        {
            if(!library)throw new ArgumentNullException(nameof(library));
            var result=new PortraitLibraryMaintenance(library);var state=Read(library);var seen=new HashSet<string>();
            foreach(var avatar in avatars)
            {
                if(!avatar.Sprite)continue;
                library.TryGetPortraitProfile(avatar.ActorId,avatar.Sprite.name,out var profile);
                bool found=!string.IsNullOrEmpty(profile.spriteName);
                if(!found){profile.actorId=avatar.ActorId;profile.spriteName=avatar.Sprite.name;}
                string key=profile.actorId+":"+profile.spriteName;if(!seen.Add(key))continue;
                var stamp=Find(state,profile.actorId,profile.spriteName);
                var row=new Entry{Avatar=avatar,Before=profile,Proposed=profile,SpriteName=profile.spriteName,HadProfile=found,
                    SourceGuid=Guid(avatar.Sprite),SourceHash=HashAsset(avatar.Sprite),BeforeCalibration=Calibration(profile),
                    BeforeSheetHash=HashAsset(profile.mouthPoseSheet),BeforeStamp=StampJson(stamp)};
                row.Protected=stamp?.kind==2||profile.mouthPoseSheet&&(stamp==null||stamp.kind!=1||stamp.sheetHash!=row.BeforeSheetHash||stamp.sheetGuid!=Guid(profile.mouthPoseSheet));
                var size=PortraitBlinkMaterial.CanvasSize(avatar.Sprite);
                if(!profile.HasMouth&&!profile.blink.HasEyes)
                {
                    Texture2D image=null;bool silhouette=false;
                    try{image=DialoguePortraitAuthoringUtility.ReadCanvas(avatar.Sprite);
                        silhouette=image.GetPixels32().Where(c=>c.a>127).All(c=>c.r<12&&c.g<12&&c.b<12);}
                    finally{if(image)Object.DestroyImmediate(image);}
                    row.Status=stamp!=null&&stamp.source!=row.SourceHash?Change.SourceChanged:
                        found||silhouette?Change.Inactive:Change.NewPortrait;
                    result.Entries.Add(row);continue;
                }
                Texture2D canvas=null;
                try
                {
                    canvas=DialoguePortraitAuthoringUtility.ReadCanvas(avatar.Sprite);
                    var complete=CompleteLip(canvas,profile);row.Complex=ComplexMouth(canvas,complete);
                    row.Proposed=row.Complex&&!profile.mouthPoseSheet?complete:profile;
                    if(stamp==null)row.Status=!profile.mouthPoseSheet&&row.Complex?Change.NeedsSheet:Change.Untracked;
                    else if(stamp.width!=size.x||stamp.height!=size.y)row.Status=Change.CanvasChanged;
                    else if(stamp.source!=row.SourceHash||stamp.guid!=row.SourceGuid)row.Status=Change.SourceChanged;
                    else if(stamp.calibration!=row.BeforeCalibration)row.Status=Change.CalibrationChanged;
                    else if(stamp.sheetHash!=row.BeforeSheetHash||stamp.sheetGuid!=Guid(profile.mouthPoseSheet))row.Status=Change.SheetEdited;
                    else if(stamp.kind==1&&stamp.recipe!=Recipe)row.Status=Change.ToolChanged;
                    else if(!profile.mouthPoseSheet&&row.Complex)row.Status=Change.NeedsSheet;
                    else row.Status=Change.Current;
                }
                catch(Exception e){row.Status=Change.MissingSource;row.Error=e.Message;}
                finally{if(canvas)Object.DestroyImmediate(canvas);}
                result.Entries.Add(row);
            }
            foreach(var profile in library.portraitSpeech??Array.Empty<PortraitSpeechProfile>())
            {
                string key=profile.actorId+":"+profile.spriteName;if(seen.Contains(key))continue;
                result.Entries.Add(new Entry{Before=profile,Proposed=profile,SpriteName=profile.spriteName,HadProfile=true,Status=Change.MissingSource,
                    BeforeCalibration=Calibration(profile),BeforeSheetHash=HashAsset(profile.mouthPoseSheet),BeforeStamp=StampJson(Find(state,profile.actorId,profile.spriteName))});
            }
            return result;
        }

        public int GenerateCandidates()
        {
            int count=0;
            foreach(var row in Entries.Where(e=>e.CanGenerate&&!e.Applied&&e.Draft==null))
            {
                Texture2D canvas=null;PortraitMouthPoseDraft draft=null;
                try
                {
                    Validate(row);canvas=DialoguePortraitAuthoringUtility.ReadCanvas(row.Avatar.Sprite);
                    draft=new PortraitMouthPoseDraft(canvas,row.Proposed.mouth);draft.Generate(row.Proposed.skinSample,row.Proposed.sampleSkinByRow);
                    row.Draft=draft;row.GeneratedRevision=draft.Revision;row.Proposed.mouthPoseSheet=draft.Texture;draft=null;row.Error=null;count++;
                }
                catch(Exception e){row.Error=e.Message;}
                finally{draft?.Dispose();if(canvas)Object.DestroyImmediate(canvas);}
            }
            return count;
        }
        public void Validate(Entry row)
        {
            if(row.Confirmed&&row.Draft!=null&&row.Draft.Revision!=row.ConfirmedRevision)
            {row.Confirmed=false;throw new InvalidOperationException("候选像素已修改，请重新预览确认："+row.Label);}
            if(!row.Avatar?.Sprite||Guid(row.Avatar.Sprite)!=row.SourceGuid||HashAsset(row.Avatar.Sprite)!=row.SourceHash)
                throw new InvalidOperationException("头像源图已经变化，请重新扫描："+row.Label);
            bool exists=library.TryGetPortraitProfile(row.Before.actorId,row.SpriteName,out var now);
            if(exists!=row.HadProfile||(exists&&(Calibration(now)!=row.BeforeCalibration||HashAsset(now.mouthPoseSheet)!=row.BeforeSheetHash||Guid(now.mouthPoseSheet)!=Guid(row.Before.mouthPoseSheet))))
                throw new InvalidOperationException("头像配置或贴片已经变化，请重新扫描："+row.Label);
            if(StampJson(Find(Read(library),row.Before.actorId,row.SpriteName))!=row.BeforeStamp)
                throw new InvalidOperationException("该头像的版本记录已被更新，请重新扫描："+row.Label);
        }
        public void Confirm(Entry row,PortraitSpeechProfile reviewed)
        {
            Validate(row);
            if(row.Status==Change.MissingSource||row.Status==Change.NewPortrait&&!reviewed.HasMouth&&!reviewed.blink.HasEyes)
                throw new InvalidOperationException("请先为新头像配置需要的口型或眨眼。");
            if(row.Draft!=null&&reviewed.mouthPoseSheet!=row.Draft.Texture)throw new InvalidOperationException("当前预览不是本轮生成的贴片。");
            if(reviewed.mouthPoseSheet&&!DialoguePortraitAuthoringUtility.MouthPoseSheetMatches(reviewed,
                (int)row.Avatar.Sprite.rect.width,(int)row.Avatar.Sprite.rect.height))throw new InvalidOperationException("贴片与嘴框尺寸不同，请先调整。");
            row.Proposed=reviewed;row.ConfirmedRevision=row.Draft?.Revision??0;row.Confirmed=true;
        }
        public int ApplyConfirmed(string folder=null)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("请在编辑模式应用全库更新。");
            var ready=Entries.Where(e=>e.Confirmed&&!e.Applied).ToArray();
            foreach(var row in ready)Validate(row);
            var prepared=new List<(Entry Row,PortraitSpeechProfile Profile,bool Generated)>();
            foreach(var row in ready)
            {
                var profile=row.Proposed;bool generated=false;
                if(row.Draft!=null)
                {
                    generated=row.Draft.Revision==row.GeneratedRevision;
                    // A matching source region can share an already saved candidate, including its cheek fill.
                    var shared=prepared.FirstOrDefault(p=>p.Row.Before.actorId==row.Before.actorId&&p.Row.Draft!=null&&
                        p.Row.Draft.Region==row.Draft.Region&&p.Row.Draft.Texture.GetPixels32().SequenceEqual(row.Draft.Texture.GetPixels32()));
                    if(shared.Profile.mouthPoseSheet)profile.mouthPoseSheet=shared.Profile.mouthPoseSheet;
                    else
                    {
                        string directory=folder??Path.GetDirectoryName(AssetDatabase.GetAssetPath(row.Avatar.Sprite));
                        var path=AssetDatabase.GenerateUniqueAssetPath((directory+"/"+row.SpriteName+"_MouthPoses.png").Replace('\\','/'));
                        profile.mouthPoseSheet=row.Draft.SaveNewAsset(path);CreatedAssets.Add(path);
                    }
                }
                prepared.Add((row,profile,generated));
            }
            // The asset imports above can trigger other editor callbacks; check all baselines again before publishing the config.
            foreach(var row in ready)Validate(row);
            if(ready.Length==0)return 0;
            var profiles=new List<PortraitSpeechProfile>(library.portraitSpeech??Array.Empty<PortraitSpeechProfile>());
            var state=Read(library);
            foreach(var item in prepared)
            {
                var profile=item.Profile;int index=profiles.FindIndex(p=>p.actorId==profile.actorId&&p.spriteName==profile.spriteName);
                if(index>=0){profile.speechDisabled=profiles[index].speechDisabled;profile.blink.playbackDisabled=profiles[index].blink.playbackDisabled;profiles[index]=profile;}
                else profiles.Add(profile);
                var stamp=Find(state,profile.actorId,profile.spriteName);
                if(stamp!=null)state.entries.Remove(stamp);
                int kind=profile.mouthPoseSheet?(item.Generated?1:item.Row.Protected||item.Row.Draft!=null?2:stamp?.kind??2):0;
                var size=PortraitBlinkMaterial.CanvasSize(item.Row.Avatar.Sprite);
                state.entries.Add(new Stamp{actor=profile.actorId,sprite=profile.spriteName,guid=item.Row.SourceGuid,source=item.Row.SourceHash,
                    width=size.x,height=size.y,calibration=Calibration(profile),sheetGuid=Guid(profile.mouthPoseSheet),sheetHash=HashAsset(profile.mouthPoseSheet),kind=kind,recipe=Recipe});
            }
            Undo.RecordObject(library,"应用已确认的全库头像更新");library.portraitSpeech=profiles.ToArray();
            using(var serialized=new SerializedObject(library))
            {serialized.FindProperty(StateField).stringValue=JsonUtility.ToJson(state);serialized.ApplyModifiedPropertiesWithoutUndo();}
            EditorUtility.SetDirty(library);if(AssetDatabase.Contains(library))AssetDatabase.SaveAssetIfDirty(library);
            foreach(var item in prepared){item.Row.Proposed=item.Profile;item.Row.Applied=true;item.Row.Confirmed=false;}
            return ready.Length;
        }
        public void Dispose(){foreach(var row in Entries)row.Draft?.Dispose();}
    }
}
