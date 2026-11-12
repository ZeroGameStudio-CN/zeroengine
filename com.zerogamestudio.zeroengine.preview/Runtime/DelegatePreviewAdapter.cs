using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ZeroEngine.Preview
{
    public sealed class DelegatePreviewAdapter : IPreviewAdapter
    {
        public delegate bool Availability(PreviewRequest request,PreviewContext context,out string reason);
        private readonly Func<string> status;
        private readonly Func<IReadOnlyList<PreviewChoice>> choices;
        private readonly Availability availability;
        private readonly Func<PreviewRequest,PreviewContext,Task<string>> execute;
        private readonly Action<PreviewResetReason> reset;
        public DelegatePreviewAdapter(Func<string> status=null,Func<PreviewRequest,PreviewContext,Task<string>> execute=null,
            Availability availability=null,Action<PreviewResetReason> reset=null,Func<IReadOnlyList<PreviewChoice>> choices=null)
        {this.status=status;this.execute=execute;this.availability=availability;this.reset=reset;this.choices=choices;}
        public string ReadStatus()=>status?.Invoke()??"专用入口；未查询其运行状态。";
        public IReadOnlyList<PreviewChoice> Choices=>choices?.Invoke()??Array.Empty<PreviewChoice>();
        public bool CanExecute(PreviewRequest request,PreviewContext context,out string reason)
        {if(availability!=null)return availability(request,context,out reason);reason=null;return true;}
        public Task<string> ExecuteAsync(PreviewRequest request,PreviewContext context)=>execute?.Invoke(request,context)??Task.FromResult("专用入口；未执行操作。");
        public void Reset(PreviewResetReason reason)=>reset?.Invoke(reason);
    }
}
