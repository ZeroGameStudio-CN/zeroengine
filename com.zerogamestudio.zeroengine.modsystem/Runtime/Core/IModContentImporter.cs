namespace ZeroEngine.ModSystem
{
    public interface IModContentImporter
    {
        ModContentImportResult Import(ModImportContext context);
    }
}
