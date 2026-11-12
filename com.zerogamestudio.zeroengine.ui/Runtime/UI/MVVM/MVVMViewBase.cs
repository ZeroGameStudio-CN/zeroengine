namespace ZeroEngine.UI.MVVM
{
    /// <summary>
    /// MVVM view driven by UIViewBase/UIManager rather than MonoBehaviour Start.
    /// Product event subscriptions and data selection remain in the consumer.
    /// </summary>
    public abstract class MVVMViewBase<TViewModel> : UIViewBase where TViewModel : ViewModelBase, new()
    {
        protected TViewModel ViewModel { get; private set; }
        protected BindingContext BindingContext { get; private set; }

        /// <summary>
        /// Installs a model for a standalone view. The caller owns its initialization and bindings;
        /// this view owns its disposal. Use either standalone initialization or the managed
        /// create lifecycle for a view instance, not both.
        /// </summary>
        protected void SetViewModel(TViewModel viewModel)
        {
            ViewModel = viewModel;
            BindingContext ??= new BindingContext();
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            ViewModel = new TViewModel();
            BindingContext = new BindingContext();
            ViewModel.Initialize();
            SetupBindings();
        }

        protected abstract void SetupBindings();

        protected override void OnOpen()
        {
            base.OnOpen();
            ViewModel?.Refresh();
        }

        protected override void OnViewDestroy()
        {
            base.OnViewDestroy();
            BindingContext?.Dispose();
            ViewModel?.Dispose();
        }
    }
}
