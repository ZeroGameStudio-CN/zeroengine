using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ZeroEngine.PortraitSpeech.Editor
{
    public sealed partial class DialoguePortraitAuthoringWindow
    {
        private PortraitLibraryMaintenance maintenance;
        private bool showOnlyMaintenance=true;
        private void ClearMaintenance()
        {
            if(maintenance==null)return;
            foreach(var row in maintenance.Entries.Where(e=>e.Draft!=null))
            {
                if(sheetEdits.TryGetValue((row.Before.actorId,row.SpriteName),out var edit)&&ReferenceEquals(edit.Pixels,row.Draft))
                {
                    if(portrait&&actorId==row.Before.actorId&&portrait.name==row.SpriteName)
                        draft.mouthPoseSheet=row.Applied?row.Proposed.mouthPoseSheet:edit.Previous;
                    sheetEdits.Remove((row.Before.actorId,row.SpriteName));
                }
            }
            maintenance.Dispose();maintenance=null;
        }
        private void ScanMaintenance(bool discard=false)
        {
            if(!discard&&maintenance!=null&&maintenance.Entries.Any(e=>e.Draft!=null&&!e.Applied))
            {status="本轮还有未应用候选。可先确认保存，或点击丢弃候选后重新扫描。";return;}
            try{ClearMaintenance();maintenance=PortraitLibraryMaintenance.Scan(style,avatars);status=$"扫描 {maintenance.Entries.Count} 张头像；{maintenance.Entries.Count(e=>e.NeedsWork)} 项待处理。";}
            catch(Exception e){status=e.Message;}
        }
        private void DrawMaintenance()
        {
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                EditorGUILayout.HelpBox("扫描原图、标定、贴片和生成器的变化；更新先生成候选，确认后保存。手工贴片保留，已暂停的动画不会被重新开启。",MessageType.Info);
                using(new EditorGUILayout.HorizontalScope())
                {
                    if(GUILayout.Button("扫描全库变化"))ScanMaintenance();
                    using(new EditorGUI.DisabledScope(maintenance==null))
                        if(GUILayout.Button("一键生成待更新候选"))
                        {try{status=$"准备了 {maintenance.GenerateCandidates()} 张候选；已有手工贴片保留，请逐张预览。";}catch(Exception e){status=e.Message;}}
                }
                if(maintenance!=null)
                {
                    int changed=maintenance.Entries.Count(e=>e.NeedsWork),ready=maintenance.Entries.Count(e=>e.Confirmed&&!e.Applied);
                    EditorGUILayout.LabelField($"共 {maintenance.Entries.Count} 张 · 待处理 {changed} · 手工保护 {maintenance.Entries.Count(e=>e.Protected)} · 已确认 {ready}");
                    using(new EditorGUILayout.HorizontalScope())
                    {
                        using(new EditorGUI.DisabledScope(ready==0))if(GUILayout.Button("保存并应用已确认项"))ApplyMaintenance();
                        if(GUILayout.Button("丢弃候选并重新扫描"))ScanMaintenance(true);
                    }
                    showOnlyMaintenance=EditorGUILayout.ToggleLeft("只显示待处理项",showOnlyMaintenance);
                    scroll=EditorGUILayout.BeginScrollView(scroll);
                    foreach(var row in maintenance.Entries)
                    {
                        if(showOnlyMaintenance&&!row.NeedsWork)continue;
                        using(new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField(row.Applied?"本轮已应用":row.Confirmed?"已确认":MaintenanceLabel(row),GUILayout.Width(145));
                            EditorGUILayout.LabelField(row.Label);
                            using(new EditorGUI.DisabledScope(!row.Avatar?.Sprite))
                                if(GUILayout.Button("预览 / 校准",GUILayout.Width(88)))PreviewMaintenance(row);
                        }
                        if(!string.IsNullOrEmpty(row.Error))EditorGUILayout.HelpBox(row.Error,MessageType.Warning);
                    }
                    EditorGUILayout.EndScrollView();
                }
                EditorGUILayout.HelpBox(status,MessageType.Info);
            }
        }
        private static string MaintenanceLabel(PortraitLibraryMaintenance.Entry row)
        {
            string label=row.Status switch
            {
                PortraitLibraryMaintenance.Change.Current=>"最新",
                PortraitLibraryMaintenance.Change.Untracked=>"待登记当前版本",
                PortraitLibraryMaintenance.Change.NeedsSheet=>"复杂嘴形待补",
                PortraitLibraryMaintenance.Change.NewPortrait=>"新头像待标定",
                PortraitLibraryMaintenance.Change.SourceChanged=>"原图变化",
                PortraitLibraryMaintenance.Change.CalibrationChanged=>"标定变化",
                PortraitLibraryMaintenance.Change.ToolChanged=>"生成器变化",
                PortraitLibraryMaintenance.Change.SheetEdited=>"贴片有修改",
                PortraitLibraryMaintenance.Change.CanvasChanged=>"尺寸变化，需重标",
                PortraitLibraryMaintenance.Change.Inactive=>"保持静态",
                _=>"源图缺失"
            };
            return row.Protected?label+" · 手工保护":label;
        }
        private void PreviewMaintenance(PortraitLibraryMaintenance.Entry row)
        {
            if(sheetEdits.TryGetValue((row.Before.actorId,row.SpriteName),out var existing)&&!ReferenceEquals(existing.Pixels,row.Draft))
            {status="这张头像已有其他未保存草稿，请先保存或撤回。";return;}
            try
            {
                maintenance.Validate(row);SelectPortrait(row.Before.actorId,row.Avatar.Sprite);draft=row.Proposed;
                if(row.Draft!=null)
                {
                    sheetEdits[(row.Before.actorId,row.SpriteName)]=new SheetEdit{Sprite=row.Avatar.Sprite,SourceHash=SpriteHash(row.Avatar.Sprite),
                        Baseline=row.Before,HadBaseline=row.HadProfile,Previous=row.Before.mouthPoseSheet,Pixels=row.Draft};
                    showPoseEditor=true;
                }
                tab=0;animate=false;pose=row.Draft!=null?1:0;scroll=Vector2.zero;
                status="全库更新候选：可先检查口型和眨眼，完成后确认当前预览。";
            }
            catch(Exception e){row.Confirmed=false;status=e.Message;}
        }
        private void DrawMaintenanceReview()
        {
            if(maintenance==null||!portrait)return;
            var row=maintenance.Entries.Find(e=>!e.Applied&&e.Before.actorId==actorId&&e.SpriteName==portrait.name);
            if(row==null||!row.NeedsWork)return;
            using(new EditorGUILayout.HorizontalScope())
            {
                if(GUILayout.Button(row.Confirmed?"重新确认当前预览":"确认当前头像的更新"))
                {
                    try{maintenance.Confirm(row,draft);status="已确认当前头像；回全库更新页统一保存。";}
                    catch(Exception e){row.Confirmed=false;status=e.Message;}
                }
                if(GUILayout.Button("返回全库更新")){tab=3;scroll=Vector2.zero;}
            }
        }
        private void ApplyMaintenance()
        {
            try
            {
                int count=maintenance.ApplyConfirmed();
                foreach(var row in maintenance.Entries.Where(e=>e.Applied&&e.Draft!=null))
                {
                    if(sheetEdits.TryGetValue((row.Before.actorId,row.SpriteName),out var edit)&&ReferenceEquals(edit.Pixels,row.Draft))
                        sheetEdits.Remove((row.Before.actorId,row.SpriteName));
                    row.Draft.Dispose();row.Draft=null;
                }
                if(portrait&&style.TryGetPortraitProfile(actorId,portrait.name,out var saved))draft=saved;
                status=$"已应用 {count} 张确认项；手工贴片和未确认项保留。";
            }
            catch(Exception e){status=e.Message+(maintenance.CreatedAssets.Count>0?" 新建但未接入的文件："+string.Join("、",maintenance.CreatedAssets):"");}
        }
    }
}
