using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZeroEngine.EditorUI;

namespace ZeroEngine.Preview.Editor
{
    /// <summary>One UI for registered switches and parameterized candidates. Merely opening it is read-only.</summary>
    public sealed class PreviewControlsPanel : IEditorWorkspacePanel, IEditorWorkspaceFullWidthPanel
    {
        private readonly PreviewController controller;
        private int selected,choice;
        private string selectionId,message,plan;
        public PreviewControlsPanel(PreviewController controller)=>this.controller=controller??throw new ArgumentNullException(nameof(controller));
        public float RefreshInterval=>.5f;
        public void Activate(EditorWorkspacePanelContext context){}
        public void Deactivate(){}
        public void Dispose(){}
        public void Tick(EditorWorkspacePanelContext context,double timeSinceStartup){}
        public void OnGUI(EditorWorkspacePanelContext context)
        {
            if(controller.Entries.Count==0){EditorGUILayout.HelpBox("没有登记的预览。",MessageType.Info);return;}
            var entries=controller.Entries;
            int next=EditorGUILayout.Popup("预览功能",selected,entries.Select(e=>e.Definition.Title).ToArray());
            if(next!=selected){selected=next;choice=0;selectionId=null;message=null;plan=null;}
            var entry=entries[selected];var definition=entry.Definition;
            EditorGUILayout.LabelField(definition.Rule,EditorStyles.wordWrappedMiniLabel);
            using(EditorUiGUILayout.Section("当前状态"))
            {
                EditorGUILayout.LabelField(entry.Adapter.ReadStatus(),EditorStyles.wordWrappedLabel);
                if(definition.Mode==PreviewMode.Selection)
                {
                    var choices=entry.Adapter.Choices??Array.Empty<PreviewChoice>();
                    if(choices.Count>0)
                    {
                        int found=Enumerable.Range(0,choices.Count).FirstOrDefault(i=>choices[i].Id==selectionId);
                        choice=EditorGUILayout.Popup("候选配置",Mathf.Clamp(found,0,choices.Count-1),choices.Select(c=>c.Title).ToArray());
                        selectionId=choices[choice].Id;
                    }
                    else{selectionId="";EditorGUILayout.HelpBox("没有可用候选；不会自动生成或修改正式配置。",MessageType.Info);}
                }
                string start=definition.Mode==PreviewMode.Selection?"start":"on",stop=definition.Mode==PreviewMode.Selection?"stop":"off";
                if(definition.Mode!=PreviewMode.Information)
                {
                    using(new EditorGUILayout.HorizontalScope())
                    {
                        DrawAction(start,definition.Mode==PreviewMode.Selection?"试用选中候选":"开启",entry,context);
                        DrawAction(stop,"关闭并恢复",entry,context);
                    }
                    string reason=controller.CanExecute(definition.Id,new PreviewRequest(start,selectionId,PreviewOrigin.Dashboard));
                    if(reason!=null)EditorGUILayout.LabelField(reason,EditorStyles.wordWrappedMiniLabel);
                }
                EditorGUILayout.LabelField(definition.Entrance,EditorStyles.wordWrappedMiniLabel);
            }
            using(EditorUiGUILayout.Section("正式采用与清理"))
            {
                if(GUILayout.Button("查看本项采用清单"))plan=PreviewAdoptionPlan.Describe(new[]{entry});
                if(GUILayout.Button("复制全部采用清单"))EditorGUIUtility.systemCopyBuffer=PreviewAdoptionPlan.Describe(entries);
                if(!string.IsNullOrEmpty(plan))EditorGUILayout.LabelField(plan,EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField("这里只检查和展示接入边界；不修改正式引用、源码或版本计划。",EditorStyles.wordWrappedMiniLabel);
            }
            if(!string.IsNullOrEmpty(message))EditorGUILayout.HelpBox(message,MessageType.Info);
        }
        private void DrawAction(string action,string label,PreviewRegistration entry,EditorWorkspacePanelContext context)
        {
            using(new EditorGUI.DisabledScope(controller.CanExecute(entry.Definition.Id,new PreviewRequest(action,selectionId,PreviewOrigin.Dashboard))!=null))
                if(GUILayout.Button(label))Apply(action,entry.Definition.Id,selectionId,context);
        }
        private async void Apply(string action,string id,string candidate,EditorWorkspacePanelContext context)
        {
            try{message=await controller.ExecuteAsync(action,id,candidate,PreviewOrigin.Dashboard);}
            catch(Exception error){message="操作未成功，已请求清理："+error.Message;}
            finally{if(context?.Owner)context.RequestRepaint();}
        }
    }
}
