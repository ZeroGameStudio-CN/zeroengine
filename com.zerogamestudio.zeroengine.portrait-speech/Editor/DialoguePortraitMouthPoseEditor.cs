using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.PortraitSpeech.Editor
{
    public sealed partial class DialoguePortraitAuthoringWindow
    {
        private sealed class SheetEdit
        {
            public Sprite Sprite;
            public Hash128 SourceHash;
            public Texture2D Previous;
            public PortraitSpeechProfile Baseline;
            public bool HadBaseline;
            public PortraitMouthPoseDraft Pixels;
        }
        private sealed class SheetReuse
        {
            public DialoguePortraitAuthoringUtility.Avatar Avatar;
            public PortraitSpeechProfile Before,Proposed;
            public Hash128 SourceHash;
            public bool Confirmed;
        }
        private readonly Dictionary<(int,string),SheetEdit> sheetEdits=new();
        private readonly List<SheetReuse> sheetReuse=new();
        private PortraitMouthPoseDraft reuseSource;
        private Texture2D reuseSheet;
        private Sprite reuseSourceSprite;
        private Hash128 reuseSourceHash;
        private Hash128 reuseSheetHash;
        private bool showPoseEditor;
        private int paintPose,paintTool;
        private Color paintColor=new(.55f,.18f,.22f,1);

        private void ClearSheetDrafts()
        {
            foreach(var item in sheetEdits.Values)item.Pixels.Dispose();
            sheetEdits.Clear();ClearSheetReuse();
        }
        private void ClearSheetReuse(){sheetReuse.Clear();reuseSource?.Dispose();reuseSource=null;reuseSheet=null;reuseSourceSprite=null;}
        private static Hash128 SpriteHash(Sprite sprite)=>AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(sprite));
        private bool CurrentSheetEdit(out SheetEdit edit)
        {
            edit=null;return portrait&&sheetEdits.TryGetValue((actorId,portrait.name),out edit);
        }

        private bool SheetIsCurrent(SheetEdit edit)
        {
            if(!portrait||!canvas||edit.Sprite!=portrait||edit.SourceHash!=SpriteHash(portrait))return false;
            return edit.Pixels.Matches(canvas,draft.mouth);
        }
        private void RestoreSheetDraft()
        {
            if(CurrentSheetEdit(out var edit))
            {
                if(SheetIsCurrent(edit))draft.mouthPoseSheet=edit.Pixels.Texture;
                else status="该头像源图或嘴框已变化；旧贴片草稿保留，请重新生成。";
            }
            else
            {
                var reuse=sheetReuse.Find(r=>r.Avatar.ActorId==actorId&&r.Avatar.Sprite==portrait);
                if(reuse!=null){draft=reuse.Proposed;showPoseEditor=true;status="待确认复用：请检查当前头像的嘴形。";}
            }
        }

        private void StartSheetDraft(bool generate)
        {
            PortraitMouthPoseDraft pixels=null;
            try
            {
                pixels=new PortraitMouthPoseDraft(canvas,draft.mouth);
                var prior=CurrentSheetEdit(out var old)?old.Previous:draft.mouthPoseSheet;
                if(generate)pixels.Generate(draft.skinSample,draft.sampleSkinByRow);
                else pixels.LoadSheet(draft.mouthPoseSheet);
                if(old!=null)old.Pixels.Dispose();
                bool had=style.TryGetPortraitProfile(actorId,portrait.name,out var baseline);
                sheetEdits[(actorId,portrait.name)]=new SheetEdit{Sprite=portrait,SourceHash=SpriteHash(portrait),Previous=prior,
                    Pixels=pixels,Baseline=baseline,HadBaseline=had};
                draft.mouthPoseSheet=pixels.Texture;pixels=null;showPoseEditor=true;animate=false;paintPose=0;pose=1;
                status=generate?"已生成两档候选；请预览并修正像素，保存前不会改动已有贴片。":"已复制现有贴片到本窗口草稿；原文件保持不变。";
            }
            catch(Exception e){pixels?.Dispose();status=e.Message;}
        }

        private void DrawMouthPoseEditor()
        {
            showPoseEditor=EditorGUILayout.Foldout(showPoseEditor,"复杂嘴形 · 候选与像素修正",true);
            if(!showPoseEditor)return;
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode||!canvas))
            {
                using(new EditorGUILayout.HorizontalScope())
                {
                    if(GUILayout.Button("生成两档候选"))StartSheetDraft(true);
                    using(new EditorGUI.DisabledScope(!draft.mouthPoseSheet))
                        if(GUILayout.Button("编辑当前贴片"))StartSheetDraft(false);
                }
                if(CurrentSheetEdit(out var edit))
                {
                    bool current=SheetIsCurrent(edit)&&draft.mouthPoseSheet==edit.Pixels.Texture;
                    if(!current)EditorGUILayout.HelpBox("源图、嘴框或贴片引用已变化。旧草稿保留；重新生成后才能保存。",MessageType.Warning);
                    using(new EditorGUI.DisabledScope(!current))
                    {
                        int selectedPose=GUILayout.Toolbar(paintPose,new[]{"半开","收口"});
                        if(selectedPose!=paintPose){edit.Pixels.EndStroke();paintPose=selectedPose;animate=false;pose=paintPose+1;}
                        paintTool=GUILayout.Toolbar(paintTool,new[]{"画笔","还原原图","吸管"});
                        paintColor=EditorGUILayout.ColorField("画笔颜色",paintColor);
                        using(new EditorGUILayout.HorizontalScope())
                        {
                            foreach(var c in edit.Pixels.Palette)
                            {
                                var button=GUILayoutUtility.GetRect(22,22,GUILayout.ExpandWidth(false));EditorGUI.DrawRect(button,c);
                                if(Event.current.type==EventType.MouseDown&&button.Contains(Event.current.mousePosition))
                                {paintColor=c;paintTool=0;Event.current.Use();}
                            }
                        }
                        EditorGUILayout.HelpBox("左边取原图颜色，右边修口型。右键还原原像素。草稿保留在当前窗口，切换头像可继续编辑。",MessageType.None);
                        float cell=Mathf.Clamp((position.width-92)/(edit.Pixels.Region.width*2f),4,24);
                        using(new EditorGUILayout.HorizontalScope())
                        {DrawMouthPixelGrid(edit.Pixels,cell,false);DrawMouthPixelGrid(edit.Pixels,cell,true);}
                        using(new EditorGUILayout.HorizontalScope())
                        {
                            using(new EditorGUI.DisabledScope(!edit.Pixels.CanUndo))if(GUILayout.Button("撤销像素修改"))edit.Pixels.Undo();
                            using(new EditorGUI.DisabledScope(!edit.Pixels.CanRedo))if(GUILayout.Button("重做"))edit.Pixels.Redo();
                        }
                    }
                    using(new EditorGUILayout.HorizontalScope())
                    {
                        using(new EditorGUI.DisabledScope(!current))if(GUILayout.Button("保存新贴片并应用"))SaveCurrentSheet();
                        if(GUILayout.Button("撤回本草稿"))
                        {draft.mouthPoseSheet=edit.Previous;edit.Pixels.Dispose();sheetEdits.Remove((actorId,portrait.name));status="已撤回本窗口草稿，原贴片未改动。";}
                    }
                }
                using(new EditorGUI.DisabledScope(!draft.mouthPoseSheet||!AssetDatabase.Contains(draft.mouthPoseSheet)))
                    if(GUILayout.Button("寻找可复用的同角色差分"))FindSheetVariants();
                DrawSheetReuse();
            }
        }

        private void DrawMouthPixelGrid(PortraitMouthPoseDraft pixels,float cell,bool editable)
        {
            using(new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(false)))
            {
                EditorGUILayout.LabelField(editable?"当前口型":"原画",GUILayout.Width(cell*pixels.Region.width));
                var area=GUILayoutUtility.GetRect(cell*pixels.Region.width,cell*pixels.Region.height,GUILayout.ExpandWidth(false));
                for(int y=0;y<pixels.Region.height;y++)for(int x=0;x<pixels.Region.width;x++)
                {
                    var r=new Rect(area.x+x*cell,area.y+(pixels.Region.height-1-y)*cell,cell-1,cell-1);
                    EditorGUI.DrawRect(r,editable?pixels.Composed(paintPose,x,y):pixels.Original(x,y));
                }
                var e=Event.current;
                if(e.rawType==EventType.MouseUp)pixels.EndStroke();
                if(!area.Contains(e.mousePosition)||(e.type!=EventType.MouseDown&&e.type!=EventType.MouseDrag)||(e.button!=0&&e.button!=1))return;
                int px=Mathf.Clamp((int)((e.mousePosition.x-area.x)/cell),0,pixels.Region.width-1);
                int py=Mathf.Clamp(pixels.Region.height-1-(int)((e.mousePosition.y-area.y)/cell),0,pixels.Region.height-1);
                if(!editable||paintTool==2){paintColor=editable?pixels.Composed(paintPose,px,py):pixels.Original(px,py);paintTool=0;}
                else
                {
                    pixels.Paint(paintPose,px,py,e.button==1||paintTool==1?null:(Color32)paintColor);
                    animate=false;pose=paintPose+1;
                }
                e.Use();Repaint();
            }
        }

        private void SaveCurrentSheet()
        {
            if(!CurrentSheetEdit(out var edit)||!SheetIsCurrent(edit)||draft.mouthPoseSheet!=edit.Pixels.Texture)
            {status="请先更新当前头像的贴片草稿。";return;}
            string directory=Path.GetDirectoryName(AssetDatabase.GetAssetPath(portrait))?.Replace('\\','/');
            string suggested=AssetDatabase.GenerateUniqueAssetPath(directory+"/"+portrait.name+"_MouthPoses.png");
            string path=EditorUtility.SaveFilePanelInProject("保存新的口型贴片",Path.GetFileNameWithoutExtension(suggested),"png",
                "已有 PNG 不会被覆盖；保存后应用到当前头像。",directory);
            if(string.IsNullOrEmpty(path))return;
            try{SaveSheetTo(path);}catch(Exception e){status=e.Message;}
        }
        private Texture2D SaveSheetTo(string path)
        {
            if(!CurrentSheetEdit(out var edit)||!SheetIsCurrent(edit))throw new InvalidOperationException("源图或嘴框已变化，请重新生成。");
            bool exists=style.TryGetPortraitProfile(actorId,portrait.name,out var now);
            if(exists!=edit.HadBaseline||(exists&&JsonUtility.ToJson(now)!=JsonUtility.ToJson(edit.Baseline)))
                throw new InvalidOperationException("当前头像配置已被修改，草稿仍保留。请重新读取配置后再保存。");
            var texture=edit.Pixels.SaveNewAsset(path);var saved=draft;saved.mouthPoseSheet=texture;
            DialoguePortraitAuthoringUtility.Write(style,new[]{saved},true);draft=saved;
            edit.Pixels.Dispose();sheetEdits.Remove((actorId,portrait.name));RemoveCurrentDraft(false);
            status="已保存并接入口型贴片。Unity 撤销可恢复配置引用，新 PNG 保留。";return texture;
        }

        private void FindSheetVariants()
        {
            ClearSheetReuse();
            try
            {
                reuseSource=new PortraitMouthPoseDraft(canvas,draft.mouth);reuseSheet=draft.mouthPoseSheet;
                reuseSourceSprite=portrait;reuseSourceHash=SpriteHash(portrait);
                reuseSheetHash=AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(reuseSheet));
                foreach(var avatar in avatars.Where(a=>a.ActorId==actorId&&a.Sprite!=portrait).Take(128))
                {
                    Texture2D other=null;
                    try
                    {
                        other=DialoguePortraitAuthoringUtility.ReadCanvas(avatar.Sprite);
                        if(!reuseSource.Matches(other,draft.mouth))continue;
                        if(!style.TryGetPortraitProfile(actorId,avatar.Sprite.name,out var before))continue;
                        if(before.mouthPoseSheet==reuseSheet&&before.mouth==draft.mouth)continue;
                        var proposed=before;proposed.mouth=draft.mouth;proposed.mouthPoseSheet=reuseSheet;
                        sheetReuse.Add(new SheetReuse{Avatar=avatar,Before=before,Proposed=proposed,SourceHash=SpriteHash(avatar.Sprite)});
                    }
                    finally{if(other)DestroyImmediate(other);}
                }
                status=$"找到 {sheetReuse.Count} 张嘴部及周边像素一致的差分，逐张预览后确认复用。";
            }
            catch(Exception e){ClearSheetReuse();status=e.Message;}
        }
        private void DrawSheetReuse()
        {
            if(sheetReuse.Count==0)return;
            foreach(var row in sheetReuse)
            {
                bool active=portrait==row.Avatar.Sprite&&actorId==row.Avatar.ActorId;
                if(active&&row.Confirmed&&(draft.mouth!=row.Proposed.mouth||draft.mouthPoseSheet!=reuseSheet))row.Confirmed=false;
                using(new EditorGUILayout.HorizontalScope())
                {
                    using(new EditorGUI.DisabledScope(!active))
                    {
                        bool value=EditorGUILayout.ToggleLeft("确认",row.Confirmed,GUILayout.Width(52));
                        if(value!=row.Confirmed)row.Confirmed=value&&SheetReuseIsCurrent(row)&&draft.mouth==row.Proposed.mouth&&draft.mouthPoseSheet==reuseSheet;
                    }
                    EditorGUILayout.LabelField(row.Avatar.Label);
                    using(new EditorGUI.DisabledScope(active))if(GUILayout.Button(active?"当前":"预览",GUILayout.Width(52)))
                        SelectPortrait(row.Avatar.ActorId,row.Avatar.Sprite);
                }
            }
            using(new EditorGUI.DisabledScope(!sheetReuse.Any(r=>r.Confirmed)))
                if(GUILayout.Button("应用已确认的贴片复用"))ApplySheetReuse();
        }
        private bool SheetReuseIsCurrent(SheetReuse row)=>reuseSourceSprite&&SpriteHash(reuseSourceSprite)==reuseSourceHash&&
            reuseSheet&&AssetDatabase.Contains(reuseSheet)&&
            AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(reuseSheet))==reuseSheetHash&&
            SpriteHash(row.Avatar.Sprite)==row.SourceHash&&style.TryGetPortraitProfile(row.Before.actorId,row.Before.spriteName,out var now)&&
            JsonUtility.ToJson(now)==JsonUtility.ToJson(row.Before);
        private int ApplySheetReuse()
        {
            foreach(var row in sheetReuse.Where(r=>r.Confirmed))if(!SheetReuseIsCurrent(row))
            {row.Confirmed=false;status="源图、贴片或配置已变化，请重新预览。";return 0;}
            var selected=sheetReuse.Where(r=>r.Confirmed).ToArray();
            int count=DialoguePortraitAuthoringUtility.Write(style,selected.Select(r=>r.Proposed),true);
            foreach(var row in selected)sheetReuse.Remove(row);
            status=$"已给 {count} 张确认的差分复用贴片，未确认项保留。";return count;
        }
    }
}
