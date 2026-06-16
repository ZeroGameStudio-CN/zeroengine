namespace ZeroEngine.ModSystem
{
    public sealed class ModImportContext
    {
        public ModImportContext(ModManifest manifest)
        {
            Manifest = manifest;
        }

        public ModManifest Manifest { get; }
    }
}
