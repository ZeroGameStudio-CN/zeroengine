namespace POB.Extraction
{
    public interface IExtractionProfileBlobStore
    {
        bool TryLoad(out string json);
        void Save(string json);
    }

    public class ExtractionInMemoryProfileBlobStore : IExtractionProfileBlobStore
    {
        private string json;

        public bool HasProfile => !string.IsNullOrEmpty(json);

        public bool TryLoad(out string json)
        {
            json = this.json;
            return !string.IsNullOrEmpty(json);
        }

        public void Save(string json)
        {
            this.json = json;
        }
    }
}
