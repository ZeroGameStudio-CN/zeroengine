using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroEngine.EditorUI;

namespace ZeroEngine.PortraitSpeech.Editor
{
    [EditorUiSurface]
    public sealed partial class DialoguePortraitAuthoringWindow : EditorWindow
    {
        [SerializeField] private PortraitSpeechLibrary style;
        [SerializeField] private Sprite portrait;
        [SerializeField] private int actorId=3;
        [SerializeField] private PortraitSpeechProfile draft;
        [SerializeField] private ScriptableObject database;
        private List<DialoguePortraitAuthoringUtility.Avatar> avatars=new();
        private readonly List<PortraitSpeechProfile> pending=new();
        private readonly List<PortraitSpeechProfile> pendingBlink=new();
        private readonly Dictionary<(int,string),ReviewStamp> reviewedMouth=new(),reviewedBlink=new();
        private Texture2D canvas;
        private Material preview;
        private RenderTexture previewSurface;
        private readonly PortraitPreviewPlayback playback=new();
        private readonly PortraitBlinkMaterial blinkMaterial=new();
        private bool animate=true,dragging,moving;
        private int pose,editMode,selected=-1,tab,blinkEditMode;
        private bool blinkLoop,showEyeGuides=true;
        private bool batchAll,batchSpeech=true,batchBlink=true;
        private float blinkAmount;
        private double blinkStarted;
        private Vector2 dragStart;
        private Rect dragBox;
        private Vector2 scroll,reviewScroll;
        private double lastTime;
        private string status="选择头像后，可生成草稿或直接框选嘴部。";
        private static readonly string[] Poses={"原图","轻张","张开","圆口","横口","收口"};
        private static readonly string[] DrawnPoses={"原图","半开","收口"};

        private readonly struct ReviewStamp
        {
            public readonly Sprite Sprite;
            public readonly Vector2Int Size;
            public readonly Hash128 AssetHash;
            public ReviewStamp(Sprite sprite,Hash128 assetHash)
            {Sprite=sprite;Size=PortraitBlinkMaterial.CanvasSize(sprite);AssetHash=assetHash;}
        }

        public static void OpenWindow()=>Open(Selection.activeObject as PortraitSpeechLibrary);
        public static DialoguePortraitAuthoringWindow Open(PortraitSpeechLibrary target,ScriptableObject actorDatabase=null)
        {
            var window=GetWindow<DialoguePortraitAuthoringWindow>("对话头像口型");window.minSize=new Vector2(620,700);
            if(window.style!=target||window.database!=actorDatabase){window.ClearDrafts(false);window.ClearDrafts(true);window.ClearMaintenance();window.ClearSheetDrafts();}
            window.style=target;window.database=actorDatabase;window.RefreshAvatars();window.Show();return window;
        }
        private void OnEnable()
        {
            lastTime=EditorApplication.timeSinceStartup;EditorApplication.update+=UpdatePreview;
            if(!style)return;
            var unsaved=draft;RefreshAvatars();
            if(portrait&&unsaved.actorId==actorId&&unsaved.spriteName==portrait.name)draft=unsaved;
        }
        private void OnDisable(){EditorApplication.update-=UpdatePreview;ClearMaintenance();ClearSheetDrafts();ReleasePreview();}
        private void ReleasePreview()
        {
            blinkMaterial.Dispose();if(canvas)DestroyImmediate(canvas);if(preview)DestroyImmediate(preview);
            if(previewSurface){previewSurface.Release();DestroyImmediate(previewSurface);}
            canvas=null;preview=null;previewSurface=null;
        }

        private void RefreshAvatars()
        {
            SyncPending();
            avatars=DialoguePortraitAuthoringUtility.Collect(style,database);
            selected=avatars.FindIndex(a=>a.ActorId==actorId&&(!portrait||a.Sprite==portrait));
            if(selected<0&&!portrait&&avatars.Count>0)selected=0;
            if(selected>=0){actorId=avatars[selected].ActorId;portrait=avatars[selected].Sprite;}
            LoadPortrait();
        }
        private void SelectPortrait(int actor,Sprite sprite)
        {
            SyncPending();actorId=actor;portrait=sprite;
            selected=avatars.FindIndex(a=>a.ActorId==actor&&a.Sprite==sprite);
            LoadPortrait();
        }
        private void LoadPortrait()
        {
            ReleasePreview();playback.Reset();draft=default;blinkStarted=EditorApplication.timeSinceStartup;dragging=false;
            if(!style||!portrait)return;
            try
            {
                canvas=DialoguePortraitAuthoringUtility.ReadCanvas(portrait);
                var prepared=pending.FirstOrDefault(p=>p.actorId==actorId&&p.spriteName==portrait.name);
                if(!string.IsNullOrEmpty(prepared.spriteName))
                {
                    draft=prepared;
                    DialoguePortraitAuthoringUtility.TryFind(style,actorId,portrait.name,out var saved);
                    draft.blink=saved.blink;status="待确认口型草稿，可拖框调整后写入。";
                }
                else draft=DialoguePortraitAuthoringUtility.Propose(style,actorId,portrait,canvas,out status);
                var eyes=pendingBlink.FirstOrDefault(p=>p.actorId==actorId&&p.spriteName==portrait.name);
                if(!string.IsNullOrEmpty(eyes.spriteName)){draft.blink=eyes.blink;status="差分眨眼草稿：请先检查眼区和遮挡。";}
                var shader=Shader.Find(PortraitSpeechMaterial.ShaderName);
                if(shader)preview=new Material(shader){hideFlags=HideFlags.HideAndDontSave};
                RestoreSheetDraft();
            }
            catch(Exception e){status=e.Message;}
            Repaint();
        }
        private void UpdatePreview()
        {
            double now=EditorApplication.timeSinceStartup;float delta=(float)Math.Min(.1,Math.Max(0,now-lastTime));lastTime=now;
            if(!canvas||!style||tab>=2)return;
            if(animate)playback.Advance(now,delta,style);
            if(animate||(tab==1&&blinkLoop))Repaint();
        }

        private void OnGUI()
        {
            var library=(PortraitSpeechLibrary)EditorGUILayout.ObjectField("统一对话样式",style,typeof(PortraitSpeechLibrary),false);
            if(library!=style){ClearMaintenance();ClearSheetDrafts();style=library;ClearDrafts(false);ClearDrafts(true);RefreshAvatars();}
            using(new EditorGUILayout.HorizontalScope())
            {
                if(avatars.Count>0)
                {
                    int value=EditorGUILayout.Popup("角色头像",selected,avatars.Select(a=>a.Label).ToArray());
                    if(value>=0&&value!=selected)SelectPortrait(avatars[value].ActorId,avatars[value].Sprite);
                }
                if(GUILayout.Button("刷新头像",GUILayout.Width(84)))RefreshAvatars();
            }
            int editedActor=EditorGUILayout.IntField("角色 ID",actorId);
            var editedSprite=(Sprite)EditorGUILayout.ObjectField("头像 Sprite",portrait,typeof(Sprite),false);
            if(editedActor!=actorId||editedSprite!=portrait)SelectPortrait(editedActor,editedSprite);
            if(!style){EditorGUILayout.HelpBox("选择统一对话样式。",MessageType.Info);return;}
            int page=GUILayout.Toolbar(tab,new[]{"口型","眨眼","批量管理","全库更新"});
            if(page!=tab){tab=page;scroll=reviewScroll=Vector2.zero;dragging=false;blinkStarted=EditorApplication.timeSinceStartup;}
            if(tab==2){DrawBatch();return;}
            if(tab==3){DrawMaintenance();return;}
            if(!portrait){EditorGUILayout.HelpBox("选择头像 Sprite。",MessageType.Info);return;}
            if(GUILayout.Button("加入 Project 选中的头像（使用当前角色 ID）"))AddSelection();
            if(DialoguePortraitAuthoringUtility.TryFind(style,actorId,portrait.name,out var saved)&&
                (tab==0?saved.speechDisabled:saved.blink.playbackDisabled))
                EditorGUILayout.HelpBox("该动画在游戏中已停用。仍可编辑和预览，恢复请到批量管理。",MessageType.Info);
            if(tab==1)DrawBlinkControls();
            else using(new EditorGUI.DisabledScope(!canvas))
            {
                using(new EditorGUILayout.HorizontalScope())
                {
                    if(GUILayout.Button("一键生成草稿")){var eyes=draft.blink;draft=DialoguePortraitAuthoringUtility.Propose(style,actorId,portrait,canvas,out status);draft.blink=eyes;}
                    if(GUILayout.Button("复制到同尺寸差分草稿"))CopyVariants();
                }
                using(new EditorGUILayout.HorizontalScope())
                {
                    if(GUILayout.Button("为图库缺失头像生成草稿"))ProposeMissing();
                }
            }
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode||!CanExportGif()))
                if(GUILayout.Button(new GUIContent("导出 GIF","用当前草稿生成循环预览，不保存标定。")))ChooseGifPath();
            EditorGUILayout.HelpBox(status,MessageType.Info);
            DrawMaintenanceReview();
            DrawDraftReview(tab==1);
            scroll=EditorGUILayout.BeginScrollView(scroll);
            if(canvas)
            {
                if(tab==1)blinkEditMode=GUILayout.Toolbar(blinkEditMode,new[]{"左眼框","右眼框","保护像素","左眼肤色","右眼肤色"});
                else editMode=GUILayout.Toolbar(editMode,new[]{"拖动 / 框选嘴部","点击采样肤色"});
                float side=Mathf.Min(position.width-44,tab==0&&showPoseEditor?280:480);var area=GUILayoutUtility.GetRect(side,side,GUILayout.ExpandWidth(false));
                var rect=new Rect(area.x+(side-side*canvas.width/Mathf.Max(canvas.width,canvas.height))*.5f,area.y+(side-side*canvas.height/Mathf.Max(canvas.width,canvas.height))*.5f,side*canvas.width/Mathf.Max(canvas.width,canvas.height),side*canvas.height/Mathf.Max(canvas.width,canvas.height));
                DrawPreview(rect);HandlePointer(rect);
                if(tab==1)DrawBlinkFields();
                else
                {
                using(new EditorGUILayout.HorizontalScope())
                {
                    animate=EditorGUILayout.ToggleLeft("连续说话预览",animate,GUILayout.Width(130));
                    var labels=draft.mouthPoseSheet?DrawnPoses:Poses;
                    int current=GUILayout.Toolbar(Mathf.Clamp(pose,0,labels.Length-1),labels);
                    if(current!=pose){pose=current;animate=false;Repaint();}
                }
                draft.mouth=EditorGUILayout.RectField("嘴部区域（原图左下，0～1）",draft.mouth);
                draft.skinSample=EditorGUILayout.Vector2Field("肤色采样点",draft.skinSample);
                draft.sampleSkinByRow=EditorGUILayout.Toggle(
                    new GUIContent("逐行采样肤色","沿采样点的同一列取色，用于保留脸红等纵向渐变。"),
                    draft.sampleSkinByRow);
                var sheet=(Texture2D)EditorGUILayout.ObjectField("手绘口型贴片",draft.mouthPoseSheet,typeof(Texture2D),false);
                if(sheet!=draft.mouthPoseSheet){draft.mouthPoseSheet=sheet;pose=0;}
                DrawMouthPoseEditor();
                sheet=draft.mouthPoseSheet;
                bool validSheet=DialoguePortraitAuthoringUtility.MouthPoseSheetMatches(draft,canvas.width,canvas.height);
                if(sheet)
                {
                    int w=Mathf.RoundToInt(draft.mouth.width*canvas.width),h=Mathf.RoundToInt(draft.mouth.height*canvas.height);
                    EditorGUILayout.HelpBox($"贴片尺寸 {w} × {h*2}；从上到下为收口、半开。透明像素保留原画。",
                        validSheet?MessageType.Info:MessageType.Warning);
                }
                else
                {
                    bool pixelPoses=EditorGUILayout.Toggle("按原图像素配置口型",draft.usePixelPoses);
                    if(pixelPoses&&!draft.usePixelPoses&&draft.lightPixels==Vector4.zero)
                    {
                        draft.lightPixels=new Vector4(2,1,0,0);draft.openPixels=new Vector4(3,2,.5f,-.5f);draft.roundPixels=new Vector4(2,2,0,-.5f);draft.widePixels=new Vector4(4,1,0,0);draft.tightPixels=new Vector4(1,1,.5f,0);
                    }
                    draft.usePixelPoses=pixelPoses;
                    if(pixelPoses)
                    {
                        EditorGUILayout.HelpBox("宽、高、水平偏移、垂直偏移，单位为原图像素；偏移以嘴部区域中心为原点。",MessageType.None);
                        draft.lightPixels=EditorGUILayout.Vector4Field("轻张",draft.lightPixels);draft.openPixels=EditorGUILayout.Vector4Field("张开",draft.openPixels);draft.roundPixels=EditorGUILayout.Vector4Field("圆口",draft.roundPixels);draft.widePixels=EditorGUILayout.Vector4Field("横口",draft.widePixels);draft.tightPixels=EditorGUILayout.Vector4Field("收口",draft.tightPixels);
                    }
                    draft.mouthColor=EditorGUILayout.ColorField("口腔颜色",draft.mouthColor);draft.tongueColor=EditorGUILayout.ColorField("舌部颜色",draft.tongueColor);draft.teethColor=EditorGUILayout.ColorField("牙齿颜色",draft.teethColor);
                }
                using(new EditorGUI.DisabledScope(EditorApplication.isPlaying||!validSheet||(draft.mouthPoseSheet&&!AssetDatabase.Contains(draft.mouthPoseSheet))))
                    if(GUILayout.Button("保存当前头像口型",GUILayout.Height(28)))
                    {
                        draft.actorId=actorId;draft.spriteName=portrait.name;draft.mouth=DialoguePortraitAuthoringUtility.Snap(draft.mouth,canvas.width,canvas.height);
                        int count=DialoguePortraitAuthoringUtility.Write(style,new[]{draft},true);
                        if(count>0){RemoveCurrentDraft(false);status="已保存 "+portrait.name+"，可用 Unity 撤销。";}
                    }
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawBatch()
        {
            scroll=EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("播放开关",EditorStyles.boldLabel);
            batchAll=EditorGUILayout.Popup("操作范围",batchAll?1:0,new[]{"当前角色（ID "+actorId+"）","全部角色"})==1;
            int? actor=batchAll?null:actorId;
            var profiles=style.portraitSpeech??Array.Empty<PortraitSpeechProfile>();
            int mouths=0,eyes=0,pausedMouths=0,pausedEyes=0;
            foreach(var entry in profiles)
            {
                if(actor.HasValue&&entry.actorId!=actor.Value)continue;
                if(entry.HasMouth){mouths++;if(entry.speechDisabled)pausedMouths++;}
                if(entry.blink.enabled&&entry.blink.HasEyes){eyes++;if(entry.blink.playbackDisabled)pausedEyes++;}
            }
            batchSpeech=EditorGUILayout.ToggleLeft($"口型：{mouths-pausedMouths}/{mouths} 张播放",batchSpeech);
            batchBlink=EditorGUILayout.ToggleLeft($"眨眼：{eyes-pausedEyes}/{eyes} 张播放",batchBlink);
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                using(new EditorGUILayout.HorizontalScope())
                {
                    using(new EditorGUI.DisabledScope((!batchSpeech||mouths==pausedMouths)&&(!batchBlink||eyes==pausedEyes)))
                        if(GUILayout.Button("停用所选动画"))SetBatchPlayback(actor,false);
                    using(new EditorGUI.DisabledScope((!batchSpeech||pausedMouths==0)&&(!batchBlink||pausedEyes==0)))
                        if(GUILayout.Button("恢复所选动画"))SetBatchPlayback(actor,true);
                }
                EditorGUILayout.HelpBox("停用会保留嘴部、眼区和遮挡标定。恢复仅作用于已配置的动画，操作可撤销。",MessageType.None);
                EditorGUILayout.LabelField("全库节奏",EditorStyles.boldLabel);
                using var serialized=new SerializedObject(style);
                void Field(string property,string label)=>EditorGUILayout.PropertyField(serialized.FindProperty(property),new GUIContent(label));
                Field(nameof(PortraitSpeechLibrary.portraitSpeechFramesPerSecond),"每秒最多切换口型");
                Field(nameof(PortraitSpeechLibrary.portraitSpeechHold),"口型保持（秒）");
                Field(nameof(PortraitSpeechLibrary.portraitMouthOpenTime),"张口时间（秒）");
                Field(nameof(PortraitSpeechLibrary.portraitMouthCloseTime),"闭口时间（秒）");
                Field(nameof(PortraitSpeechLibrary.portraitMouthTransition),"口型过渡（秒）");
                var timing=serialized.FindProperty(nameof(PortraitSpeechLibrary.blinkTiming));
                void BlinkField(string property,string label)=>EditorGUILayout.PropertyField(timing.FindPropertyRelative(property),new GUIContent(label));
                BlinkField(nameof(PortraitBlinkTiming.firstDelay),"首次眨眼（秒）");
                BlinkField(nameof(PortraitBlinkTiming.interval),"眨眼间隔（秒）");
                BlinkField(nameof(PortraitBlinkTiming.duration),"单次眨眼时长（秒）");
                BlinkField(nameof(PortraitBlinkTiming.closedHold),"闭眼保持（秒）");
                BlinkField(nameof(PortraitBlinkTiming.intervalVariation),"间隔变化幅度");
                if(serialized.ApplyModifiedProperties()&&AssetDatabase.Contains(style))AssetDatabase.SaveAssetIfDirty(style);
                EditorGUILayout.HelpBox("节奏设置应用到这份图库的所有角色。口型大小、眼区和遮挡仍可单张调整。",MessageType.None);
            }
            EditorGUILayout.HelpBox(status,MessageType.Info);
            EditorGUILayout.EndScrollView();
        }

        private void SetBatchPlayback(int? actor,bool play)
        {
            int count=DialoguePortraitAuthoringUtility.SetPlayback(style,actor,batchSpeech,batchBlink,play);
            status=(play?"恢复":"停用")+$"了 {count} 张头像所选动画，可用 Unity 撤销。";
        }

        private void DrawPreview(Rect rect)
        {
            for(float y=rect.y;y<rect.yMax;y+=16)for(float x=rect.x;x<rect.xMax;x+=16)
                EditorGUI.DrawRect(new Rect(x,y,Mathf.Min(16,rect.xMax-x),Mathf.Min(16,rect.yMax-y)),((int)((x-rect.x)/16)+(int)((y-rect.y)/16))%2==0?new Color(.20f,.21f,.23f):new Color(.25f,.26f,.28f));
            if(preview)
            {
                int stillPose=tab==1?0:pose;
                if(draft.mouthPoseSheet&&stillPose>0)stillPose=stillPose==1?3:5;
                var motion=playback.Motion;
                float blink=tab!=1?0:blinkLoop?PortraitBlinkMotion.Evaluate((float)(EditorApplication.timeSinceStartup-blinkStarted),style.blinkTiming):blinkAmount;
                PortraitPreviewSurface.Apply(preview,canvas,draft,blinkMaterial,animate?motion.Frame:stillPose,
                    animate?motion.PreviousFrame:stillPose,animate?motion.Openness:stillPose>0?1:0,animate?motion.Blend:1,blink);
                // A custom portrait shader does not apply IMGUI's scroll clipping.
                // Render its pixels offscreen, then let GUI.DrawTexture clip the result.
                if(!previewSurface)
                {
                    previewSurface=new RenderTexture(canvas.width,canvas.height,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.Linear)
                    {hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                    previewSurface.Create();
                }
                var active=RenderTexture.active;bool srgb=GL.sRGBWrite;
                try
                {
                    RenderTexture.active=previewSurface;GL.sRGBWrite=false;GL.Clear(true,true,Color.clear);
                    Graphics.Blit(canvas,previewSurface,preview);
                }
                finally{RenderTexture.active=active;GL.sRGBWrite=srgb;}
                GUI.DrawTexture(rect,previewSurface,ScaleMode.StretchToFill,true);
            }
            else GUI.DrawTexture(rect,canvas,ScaleMode.StretchToFill,true);
            if(tab==1){if(showEyeGuides)DrawEyeGuides(rect);}
            else DrawGuide(rect,draft.mouth,draft.skinSample,new Color(.3f,.8f,1));
        }

        private bool CanExportGif()=>canvas&&preview&&
            (tab==0?draft.HasMouth:draft.blink.enabled&&draft.blink.HasEyes);
        private bool ExportMouth()=>tab==0||(animate&&draft.HasMouth);

        private void ChooseGifPath()
        {
            try{PortraitGifExport.Validate(canvas,draft,ExportMouth(),tab==1);}
            catch(InvalidOperationException e){status=e.Message;return;}
            string name=string.Join("_",portrait.name.Split(Path.GetInvalidFileNameChars()))+(tab==0?"-mouth":"-blink");
            string path=EditorUtility.SaveFilePanel("导出头像 GIF",Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),name,"gif");
            if(string.IsNullOrEmpty(path))return;
            try{float seconds=ExportGif(path);status="已导出 "+Path.GetFileName(path)+"（"+seconds.ToString("0.0")+" 秒循环）。";}
            catch(Exception e){status="导出失败："+e.Message;}
        }

        private float ExportGif(string path)
        {
            if(!CanExportGif()||EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("请在编辑模式选中已标定头像。");
            return PortraitGifExport.Write(path,canvas,style,draft,ExportMouth(),tab==1);
        }
        private static void DrawGuide(Rect rect,Rect area,Vector2 skin,Color color)
        {
            if(area.width<=0||area.height<=0)return;
            var box=DialoguePortraitAuthoringUtility.ToGui(area,rect);Handles.BeginGUI();Handles.color=color;
            Handles.DrawAAPolyLine(2,new Vector3(box.x,box.y),new Vector3(box.xMax,box.y),new Vector3(box.xMax,box.yMax),new Vector3(box.x,box.yMax),new Vector3(box.x,box.y));Handles.EndGUI();
            var sample=new Vector2(rect.x+skin.x*rect.width,rect.yMax-skin.y*rect.height);EditorGUI.DrawRect(new Rect(sample.x-2,sample.y-2,4,4),Color.yellow);
        }

        private void DrawBlinkControls()
        {
            using(new EditorGUI.DisabledScope(!canvas))
            {
                using(new EditorGUILayout.HorizontalScope())
                {
                    draft.blink.enabled=EditorGUILayout.ToggleLeft("启用眨眼",draft.blink.enabled,GUILayout.Width(85));
                    animate=EditorGUILayout.ToggleLeft("播放口型",animate,GUILayout.Width(85));
                    showEyeGuides=EditorGUILayout.ToggleLeft("显示标定",showEyeGuides,GUILayout.Width(85));
                    bool loop=EditorGUILayout.ToggleLeft("循环眨眼",blinkLoop,GUILayout.Width(85));
                    if(loop!=blinkLoop){blinkLoop=loop;blinkStarted=EditorApplication.timeSinceStartup;}
                }
                using(new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    blinkAmount=EditorGUILayout.Slider("闭眼程度",blinkAmount,0,1);
                    if(EditorGUI.EndChangeCheck())blinkLoop=false;
                    if(GUILayout.Button("睁眼",GUILayout.Width(48))){blinkLoop=false;blinkAmount=0;}
                    if(GUILayout.Button("半闭",GUILayout.Width(48))){blinkLoop=false;blinkAmount=.5f;}
                    if(GUILayout.Button("闭眼",GUILayout.Width(48))){blinkLoop=false;blinkAmount=1;}
                }
                using(new EditorGUILayout.HorizontalScope())
                {
                    using(new EditorGUI.DisabledScope(!draft.blink.HasEyes))
                        if(GUILayout.Button("复制到同尺寸差分草稿"))CopyBlinkVariants();
                    using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode||(!draft.blink.HasEyes&&draft.blink.enabled)))
                        if(GUILayout.Button("保存当前眨眼"))
                        {
                            draft.actorId=actorId;draft.spriteName=portrait.name;
                            int count=DialoguePortraitAuthoringUtility.WriteBlink(style,new[]{draft},true);
                            if(count>0)RemoveCurrentDraft(true);
                            status=count>0?"已保存 "+portrait.name+" 的眨眼，可用 Unity 撤销。":"先框选眼区。";
                        }
                }
            }
        }

        private void DrawBlinkFields()
        {
            EditorGUILayout.HelpBox("拖框调整眼区。保护像素模式：左键涂刘海等不动的像素，右键擦除。黄色点为各眼的肤色采样。",MessageType.None);
            draft.blink.leftEye=DrawEyeFields("左眼",draft.blink.leftEye);
            draft.blink.rightEye=DrawEyeFields("右眼",draft.blink.rightEye);
            draft.blink.lidColor=EditorGUILayout.ColorField("闭眼线颜色",draft.blink.lidColor);
            using(new EditorGUI.DisabledScope((draft.blink.protectedPixels?.Length??0)==0))
                if(GUILayout.Button("清空保护像素")){draft.blink.protectedPixels=null;draft.blink.maskCanvasSize=default;}
        }

        private PortraitBlinkEye DrawEyeFields(string label,PortraitBlinkEye eye)
        {
            var area=EditorGUILayout.RectField(label+"区（原图左下，0～1）",eye.area);
            if(area!=eye.area)eye.area=area.width>0&&area.height>0?DialoguePortraitAuthoringUtility.Snap(area,canvas.width,canvas.height):default;
            if(eye.IsConfigured)
            {
                eye.skinSample=EditorGUILayout.Vector2Field(label+"肤色采样",eye.skinSample);
                eye.closedHeight=EditorGUILayout.Slider(label+"闭眼线高度",eye.closedHeight,.15f,.85f);
                eye.tilt=EditorGUILayout.Slider(label+"倾斜",eye.tilt,-.4f,.4f);
                eye.curve=EditorGUILayout.Slider(label+"弧度",eye.curve,-.2f,.2f);
            }
            return eye;
        }

        private void DrawEyeGuides(Rect rect)
        {
            DrawGuide(rect,draft.blink.leftEye.area,draft.blink.leftEye.skinSample,new Color(.3f,.8f,1));
            DrawGuide(rect,draft.blink.rightEye.area,draft.blink.rightEye.skinSample,new Color(.95f,.65f,.3f));
            if(blinkEditMode!=2||draft.blink.protectedPixels==null||draft.blink.maskCanvasSize!=new Vector2Int(canvas.width,canvas.height))return;
            var cell=new Vector2(rect.width/canvas.width,rect.height/canvas.height);
            foreach(var p in draft.blink.protectedPixels)
                EditorGUI.DrawRect(new Rect(rect.x+p.x*cell.x,rect.yMax-(p.y+1)*cell.y,cell.x,cell.y),new Color(.92f,.42f,.38f,.48f));
        }

        private Vector2Int CanvasPoint(Vector2 point,Rect rect)=>new(
            Mathf.Clamp(Mathf.FloorToInt((point.x-rect.x)/rect.width*canvas.width),0,canvas.width-1),
            Mathf.Clamp(Mathf.FloorToInt((rect.yMax-point.y)/rect.height*canvas.height),0,canvas.height-1));
        private Vector2 PixelCenter(Vector2Int point)=>new((point.x+.5f)/canvas.width,(point.y+.5f)/canvas.height);

        private void PaintProtection(Vector2Int point,bool protect)
        {
            var size=new Vector2Int(canvas.width,canvas.height);
            var center=PixelCenter(point);
            if(protect&&!draft.blink.leftEye.area.Contains(center)&&!draft.blink.rightEye.area.Contains(center))return;
            var existing=draft.blink.maskCanvasSize==size?draft.blink.protectedPixels:null;
            bool already=existing!=null&&Array.IndexOf(existing,point)>=0;
            if(already==protect)return;
            existing??=Array.Empty<Vector2Int>();
            draft.blink.protectedPixels=protect?existing.Append(point).ToArray():existing.Where(p=>p!=point).ToArray();
            draft.blink.maskCanvasSize=size;
        }

        private void SetEyeArea(bool left,Rect area)
        {
            bool first=!draft.blink.HasEyes;
            var eye=left?draft.blink.leftEye:draft.blink.rightEye;
            if(!eye.IsConfigured)
            {
                eye.closedHeight=.4f;eye.curve=.08f;
                eye.skinSample=new Vector2(area.center.x,Mathf.Max(.5f/canvas.height,area.yMin-.5f/canvas.height));
            }
            eye.area=area;
            if(left)draft.blink.leftEye=eye;else draft.blink.rightEye=eye;
            if(first){draft.blink.enabled=true;draft.blink.lidColor=new Color(.15f,.07f,.085f,1);}
        }

        private void CopyBlinkVariants()
        {
            ClearDrafts(true);
            var size=PortraitBlinkMaterial.CanvasSize(portrait);
            foreach(var avatar in avatars)
            {
                if(avatar.ActorId!=actorId||avatar.Sprite.name==portrait.name||PortraitBlinkMaterial.CanvasSize(avatar.Sprite)!=size)continue;
                DialoguePortraitAuthoringUtility.TryFind(style,actorId,avatar.Sprite.name,out var saved);
                if(saved.blink.HasEyes)continue;
                saved.actorId=actorId;saved.spriteName=avatar.Sprite.name;saved.blink=draft.blink;pendingBlink.Add(saved);
            }
            status="已准备 "+pendingBlink.Count+" 个同尺寸差分草稿。逐张预览眼睛位置并勾选后写入。";
        }

        private void HandlePointer(Rect rect)
        {
            var e=Event.current;
            if(tab==1&&blinkEditMode==2&&rect.Contains(e.mousePosition)&&(e.type==EventType.MouseDown||e.type==EventType.MouseDrag)&&(e.button==0||e.button==1))
            {PaintProtection(CanvasPoint(e.mousePosition,rect),e.button==0);e.Use();Repaint();return;}
            if(e.type==EventType.MouseDown&&e.button==0&&rect.Contains(e.mousePosition))
            {
                if(tab==1&&blinkEditMode>=3)
                {
                    var sample=PixelCenter(CanvasPoint(e.mousePosition,rect));
                    if(blinkEditMode==3)draft.blink.leftEye.skinSample=sample;else draft.blink.rightEye.skinSample=sample;
                    e.Use();Repaint();return;
                }
                if(tab==0&&editMode==1){draft.skinSample=PixelCenter(CanvasPoint(e.mousePosition,rect));e.Use();Repaint();return;}
                Rect area=tab==0?draft.mouth:blinkEditMode==0?draft.blink.leftEye.area:draft.blink.rightEye.area;
                dragging=true;dragStart=e.mousePosition;dragBox=DialoguePortraitAuthoringUtility.ToGui(area,rect);moving=area.width>0&&area.height>0&&dragBox.Contains(e.mousePosition);e.Use();
            }
            if(dragging&&e.type==EventType.MouseDrag)
            {
                Rect value;
                if(moving)value=new Rect(dragBox.position+e.mousePosition-dragStart,dragBox.size);
                else value=Rect.MinMaxRect(Mathf.Min(dragStart.x,e.mousePosition.x),Mathf.Min(dragStart.y,e.mousePosition.y),Mathf.Max(dragStart.x,e.mousePosition.x),Mathf.Max(dragStart.y,e.mousePosition.y));
                var normalized=DialoguePortraitAuthoringUtility.FromGui(value,rect,canvas.width,canvas.height);
                if(tab==0)draft.mouth=normalized;else SetEyeArea(blinkEditMode==0,normalized);
                e.Use();Repaint();
            }
            if(dragging&&e.rawType==EventType.MouseUp){dragging=false;e.Use();}
        }
        private void CopyVariants()
        {
            if(!DialoguePortraitAuthoringUtility.RegionOnCanvas(draft.mouth)){status="先标定画布内的嘴部区域。";return;}
            ClearDrafts(false);
            var size=PortraitBlinkMaterial.CanvasSize(portrait);
            foreach(var avatar in avatars.Where(a=>a.ActorId==actorId))
            {
                if(avatar.Sprite==portrait||PortraitBlinkMaterial.CanvasSize(avatar.Sprite)!=size)continue;
                if(DialoguePortraitAuthoringUtility.TryFind(style,actorId,avatar.Sprite.name,out var saved)&&saved.HasMouth)continue;
                var entry=draft;entry.actorId=actorId;entry.spriteName=avatar.Sprite.name;entry.blink=default;pending.Add(entry);
            }
            status="已准备 "+pending.Count+" 个同尺寸差分草稿。逐张预览嘴部位置并勾选后写入。";
        }
        private List<PortraitSpeechProfile> Drafts(bool blink)=>blink?pendingBlink:pending;
        private Dictionary<(int,string),ReviewStamp> Reviews(bool blink)=>blink?reviewedBlink:reviewedMouth;
        private void ClearDrafts(bool blink){Drafts(blink).Clear();Reviews(blink).Clear();reviewScroll=Vector2.zero;}
        private static (int,string) DraftKey(PortraitSpeechProfile profile)=>(profile.actorId,profile.spriteName);
        private DialoguePortraitAuthoringUtility.Avatar FindAvatar(PortraitSpeechProfile profile)=>
            avatars.Find(a=>a.ActorId==profile.actorId&&a.Sprite&&a.Sprite.name==profile.spriteName);
        private void RemoveCurrentDraft(bool blink)
        {
            Drafts(blink).RemoveAll(p=>p.actorId==actorId&&p.spriteName==portrait.name);
            Reviews(blink).Remove((actorId,portrait.name));
        }

        private void DrawDraftReview(bool blink)
        {
            var rows=Drafts(blink);if(rows.Count==0)return;
            SyncPending();
            var reviews=Reviews(blink);
            EditorGUILayout.LabelField("差分审核",reviews.Count+" / "+rows.Count+" 已确认");
            reviewScroll=EditorGUILayout.BeginScrollView(reviewScroll,GUILayout.Height(Mathf.Min(106,rows.Count*23+6)));
            for(int i=0;i<rows.Count;i++)
            {
                var row=rows[i];var avatar=FindAvatar(row);var key=DraftKey(row);
                bool current=avatar!=null&&avatar.Sprite==portrait&&actorId==row.actorId&&canvas&&preview;
                bool confirmed=reviews.ContainsKey(key);
                using(new EditorGUILayout.HorizontalScope())
                {
                    using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode||(!current&&!confirmed)))
                    {
                        bool chosen=EditorGUILayout.ToggleLeft("确认",confirmed,GUILayout.Width(61));
                        if(chosen!=confirmed)
                        {
                            if(!chosen)reviews.Remove(key);
                            else ConfirmCurrentDraft(blink);
                        }
                    }
                    EditorGUILayout.LabelField(avatar?.Label??row.spriteName);
                    using(new EditorGUI.DisabledScope(avatar==null||current))
                        if(GUILayout.Button(current?"当前":"预览",GUILayout.Width(55)))SelectPortrait(row.actorId,avatar.Sprite);
                }
            }
            EditorGUILayout.EndScrollView();
            using(new EditorGUI.DisabledScope(reviews.Count==0||EditorApplication.isPlayingOrWillChangePlaymode))
                if(GUILayout.Button("只写入已确认（"+reviews.Count+"）"))WriteReviewedDrafts(blink);
        }

        private bool ConfirmCurrentDraft(bool blink)
        {
            if(!canvas||!preview||!portrait||EditorApplication.isPlayingOrWillChangePlaymode)return false;
            SyncPending();
            var rows=Drafts(blink);var key=(actorId,portrait.name);
            int index=rows.FindIndex(p=>DraftKey(p)==key);
            if(index<0||FindAvatar(rows[index])?.Sprite!=portrait)return false;
            var size=new Vector2Int(canvas.width,canvas.height);
            if(blink?!draft.blink.CanRender(size):!DialoguePortraitAuthoringUtility.RegionOnCanvas(draft.mouth))
            {status=blink?"请先启用并校准当前眨眼。":"嘴部框需要位于原图内。";return false;}
            Reviews(blink)[key]=new ReviewStamp(portrait,AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(portrait)));
            status="已确认 "+portrait.name+"；修改标定后需要重新确认。";
            return true;
        }

        private int WriteReviewedDrafts(bool blink)
        {
            if(!style||EditorApplication.isPlayingOrWillChangePlaymode)return 0;
            SyncPending();
            var rows=Drafts(blink);var reviews=Reviews(blink);
            var accepted=new List<PortraitSpeechProfile>();int stale=0;
            foreach(var row in rows)
            {
                var key=DraftKey(row);
                if(!reviews.TryGetValue(key,out var stamp))continue;
                var sprite=FindAvatar(row)?.Sprite;
                if(!sprite||sprite!=stamp.Sprite||PortraitBlinkMaterial.CanvasSize(sprite)!=stamp.Size||
                    AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(sprite))!=stamp.AssetHash)
                {reviews.Remove(key);stale++;continue;}
                accepted.Add(row);
            }
            int count=blink?DialoguePortraitAuthoringUtility.WriteBlink(style,accepted,false):DialoguePortraitAuthoringUtility.Write(style,accepted,false);
            foreach(var row in accepted)
            {
                var key=DraftKey(row);reviews.Remove(key);rows.RemoveAll(p=>DraftKey(p)==key);
            }
            status="写入 "+count+" 项，"+rows.Count+" 项草稿保留。"+(stale>0?" 有 "+stale+" 张源图已变更，请重新预览。":"");
            return count;
        }

        private void SyncPending()
        {
            if(!canvas||!portrait)return;
            draft.actorId=actorId;draft.spriteName=portrait.name;
            var key=DraftKey(draft);
            int index=pending.FindIndex(p=>DraftKey(p)==key);
            if(index>=0)
            {
                var before=pending[index];var after=draft;before.blink=after.blink=default;
                if(!before.Equals(after))reviewedMouth.Remove(key);
                pending[index]=after;
            }
            int blinkIndex=pendingBlink.FindIndex(p=>DraftKey(p)==key);
            if(blinkIndex>=0)
            {
                if(!pendingBlink[blinkIndex].blink.Equals(draft.blink))reviewedBlink.Remove(key);
                pendingBlink[blinkIndex]=draft;
            }
        }
        private void AddSelection()
        {
            int added=0;
            foreach(var asset in Selection.objects.Take(128))
            {
                string path=AssetDatabase.GetAssetPath(asset);
                if(string.IsNullOrEmpty(path))continue;
                foreach(var sprite in AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>())
                    if(!avatars.Any(a=>a.ActorId==actorId&&a.Sprite.name==sprite.name))
                    {avatars.Add(new DialoguePortraitAuthoringUtility.Avatar{ActorId=actorId,ActorName=actorId.ToString(),Sprite=sprite});added++;}
            }
            status="已加入 "+added+" 张头像，可复制同角色配置或生成缺失草稿。";
        }
        private void ProposeMissing()
        {
            ClearDrafts(false);int failed=0;
            foreach(var avatar in avatars)
            {
                if(DialoguePortraitAuthoringUtility.TryFind(style,avatar.ActorId,avatar.Sprite.name,out var saved)&&saved.HasMouth)continue;
                Texture2D image=null;
                try{image=DialoguePortraitAuthoringUtility.ReadCanvas(avatar.Sprite);pending.Add(DialoguePortraitAuthoringUtility.Propose(style,avatar.ActorId,avatar.Sprite,image,out _));}
                catch{failed++;}
                finally{if(image)DestroyImmediate(image);}
            }
            status="已准备 "+pending.Count+" 个缺失头像草稿，已有项保留。"+(failed>0?"另有 "+failed+" 项无法采样，可手动框选。":"");
        }
    }

    [CustomEditor(typeof(PortraitSpeechLibrary),true)]
    public sealed class PortraitSpeechLibraryInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if(GUILayout.Button("头像口型配置与预览",GUILayout.Height(28)))DialoguePortraitAuthoringWindow.Open((PortraitSpeechLibrary)target);
            DrawDefaultInspector();
        }
    }
}
