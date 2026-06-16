namespace ZeroEngine.UI.Toast
{
    public interface IToastTextResolver
    {
        string ResolveToastText(ToastRequest request);
    }
}
