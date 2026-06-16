namespace ZeroEngine.UI.Toast
{
    public interface IToastPresenter
    {
        void ShowToast(ToastHandle handle, string resolvedText, ToastStyle style, ToastAnimationTimings timings);
        void RefreshToast(ToastHandle handle, string resolvedText, ToastStyle style, ToastAnimationTimings timings);
        void DismissToast(ToastHandle handle, ToastDismissReason reason);
        void RepositionToast(ToastHandle handle, int index, float spacing);
        void ClearAll();
    }
}
