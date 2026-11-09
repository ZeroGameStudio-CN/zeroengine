using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ZeroEngine.Preview
{
    /// <summary>Owns dispatch and lifecycle, never project assets or player storage.</summary>
    public sealed class PreviewController : IDisposable
    {
        private readonly PreviewRegistration[] entries;
        private readonly Dictionary<string,PreviewRegistration> index;
        private readonly Func<PreviewContext> context;
        private int epoch;
        private bool disposed;
        public IReadOnlyList<PreviewRegistration> Entries { get; }
        public bool IsApplying { get; private set; }
        public PreviewController(IEnumerable<PreviewRegistration> entries,Func<PreviewContext> context)
        {
            this.entries=(entries??throw new ArgumentNullException(nameof(entries))).ToArray();
            this.context=context??throw new ArgumentNullException(nameof(context));
            if(this.entries.Any(e=>e==null))throw new ArgumentException("Null preview registration.");
            index=new Dictionary<string,PreviewRegistration>(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in this.entries)
                if(!index.TryAdd(entry.Definition.Id,entry))throw new ArgumentException("Duplicate preview ID: "+entry.Definition.Id);
            Entries=Array.AsReadOnly(this.entries);
        }
        public PreviewRegistration Find(string id)=>id!=null&&index.TryGetValue(id.Trim(),out var value)?value:null;
        public string[] Ids=>entries.Select(e=>e.Definition.Id).Concat(new[]{"all"}).ToArray();
        public string Describe(PreviewRegistration entry,bool help=false)=>
            $"[{entry.Definition.Id}] {entry.Definition.Title}：{entry.Adapter.ReadStatus()}\n{(help?entry.Definition.Rule+"\n":"")}{entry.Definition.Entrance}";

        public string CanExecute(string id,PreviewRequest request)
        {
            if(disposed)return "预览会话已关闭。";
            var entry=Find(id);if(entry==null)return "未知预览功能；未执行操作。";
            if(request.Action is "status" or "help")return null;
            var state=context();
            if(!state.IsDevelopment)return "正式运行环境禁止启用开发预览；未执行操作。";
            if(state.IsTransitioning)return "正在切换播放状态；未执行操作。";
            if(IsApplying)return "预览操作尚未收尾；请等待原操作。";
            var mode=entry.Definition.Mode;
            if(mode==PreviewMode.Information)return "使用专用入口；未执行操作。\n"+entry.Definition.Entrance;
            if(mode==PreviewMode.Toggle&&request.Action is not ("on" or "off")||
               mode==PreviewMode.Selection&&request.Action is not ("start" or "stop"))return "此功能不支持该预览操作；未执行操作。";
            if(mode==PreviewMode.Selection&&request.Action=="start")
            {
                var choices=entry.Adapter.Choices??Array.Empty<PreviewChoice>();
                if(choices.GroupBy(c=>c.Id,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))return "候选 ID 重复；未执行操作。";
                if(!choices.Any(c=>c.Id==request.Selection))return "请选择有效候选；未执行操作。";
            }
            return entry.Adapter.CanExecute(request,state,out string reason)?null:reason??"当前上下文不可试用。";
        }

        public async Task<string> ExecuteAsync(string action="status",string id="ui",string selection="",PreviewOrigin origin=PreviewOrigin.Console)
        {
            var request=new PreviewRequest(action,selection,origin);
            if(request.Action is not ("status" or "help" or "on" or "off" or "start" or "stop"))return "用法：on/off/start/stop/status/help [功能] [候选或参数]。";
            if(string.Equals(id?.Trim(),"all",StringComparison.OrdinalIgnoreCase))
                return request.Action is "status" or "help"?string.Join("\n",entries.Select(e=>Describe(e,request.Action=="help"))):"all 仅支持 status/help；未改变任何开关。";
            var entry=Find(id);if(entry==null)return "未知预览功能；未执行操作。";
            if(disposed)return "预览会话已关闭。";
            if(request.Action is "status" or "help")return Describe(entry,request.Action=="help");
            string denied=CanExecute(id,request);if(denied!=null)return denied;
            IsApplying=true;int generation=epoch;
            try
            {
                string result=await entry.Adapter.ExecuteAsync(request,context());
                if(generation!=epoch){entry.Adapter.Reset(PreviewResetReason.Dispose);return "原预览操作已取消并清理。";}
                return result??Describe(entry);
            }
            catch(Exception original)
            {
                try{entry.Adapter.Reset(PreviewResetReason.Dispose);}
                catch(Exception cleanup){throw new AggregateException(original,cleanup);}
                throw;
            }
            finally{IsApplying=false;}
        }

        public void Reset(PreviewResetReason reason)
        {
            epoch++;var errors=new List<Exception>();
            foreach(var entry in entries.Reverse())
                try{entry.Adapter.Reset(reason);}catch(Exception error){errors.Add(error);}
            if(errors.Count>0)throw new AggregateException("Preview cleanup failed; all registered cleanup handlers were attempted.",errors);
        }
        public void Dispose(){if(disposed)return;disposed=true;Reset(PreviewResetReason.Dispose);}
    }
}
