using System;

namespace ZeroEngine.UI.Toast
{
    public sealed class ToastHandle
    {
        private readonly Action<ToastHandle, ToastDismissReason> dismiss;

        internal ToastHandle(int id, ToastRequest request, Action<ToastHandle, ToastDismissReason> dismiss)
        {
            Id = id;
            Request = request;
            this.dismiss = dismiss;
        }

        public int Id { get; }
        public ToastRequest Request { get; private set; }
        public bool IsDismissed { get; private set; }

        public void Dismiss(ToastDismissReason reason = ToastDismissReason.Cleared)
        {
            if (IsDismissed) return;
            dismiss?.Invoke(this, reason);
        }

        internal void ReplaceRequest(ToastRequest request)
        {
            Request = request;
        }

        internal void MarkDismissed()
        {
            IsDismissed = true;
        }
    }
}
