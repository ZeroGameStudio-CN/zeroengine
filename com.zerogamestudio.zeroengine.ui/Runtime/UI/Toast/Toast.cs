namespace ZeroEngine.UI.Toast
{
    public static class Toast
    {
        private static readonly ToastManager Manager = new ToastManager();

        public static ToastManager Runtime => Manager;

        public static void Configure(ToastSettings settings, IToastTextResolver resolver, IToastPresenter presenter)
        {
            Manager.Configure(settings, resolver, presenter);
        }

        public static ToastHandle Show(string message)
        {
            return Manager.Show(ToastRequest.Text(message));
        }

        public static ToastHandle Show(ToastRequest request)
        {
            return Manager.Show(request);
        }

        public static ToastHandle Success(string message)
        {
            return Manager.Show(ToastRequest.Text(message).WithSeverity(ToastSeverity.Success));
        }

        public static ToastHandle Warning(string message)
        {
            return Manager.Show(ToastRequest.Text(message).WithSeverity(ToastSeverity.Warning));
        }

        public static ToastHandle Error(string message)
        {
            return Manager.Show(ToastRequest.Text(message).WithSeverity(ToastSeverity.Error));
        }

        public static void ClearAll()
        {
            Manager.ClearAll();
        }

        public static void ClearGroup(string groupKey)
        {
            Manager.ClearGroup(groupKey);
        }
    }
}
