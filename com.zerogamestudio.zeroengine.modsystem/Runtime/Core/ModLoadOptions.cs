namespace ZeroEngine.ModSystem
{
    public sealed class ModLoadOptions
    {
        public string ManifestFileName { get; set; } = "manifest.json";

        public string GetManifestFileName()
        {
            return string.IsNullOrWhiteSpace(ManifestFileName) ? "manifest.json" : ManifestFileName;
        }
    }
}
