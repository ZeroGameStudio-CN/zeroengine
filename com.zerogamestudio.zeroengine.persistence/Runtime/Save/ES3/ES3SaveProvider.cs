namespace ZeroEngine.Save
{
    /// <summary>
    /// ES3 save provider wrapper.
    /// </summary>
    public class ES3SaveProvider : ISaveProvider
    {
        public void Save<T>(string key, T data, string fileName) => ES3.Save(key, data, fileName);
        public T Load<T>(string key, T defaultValue, string fileName) => ES3.Load(key, fileName, defaultValue);
        public bool Exists(string key, string fileName) => ES3.KeyExists(key, fileName);
        public void DeleteKey(string key, string fileName) => ES3.DeleteKey(key, fileName);
        public void DeleteFile(string fileName) => ES3.DeleteFile(fileName);
        public byte[] LoadBytes(string key, string fileName) => ES3.KeyExists(key, fileName) ? ES3.Load<byte[]>(key, fileName) : null;
        public void SaveBytes(string key, byte[] bytes, string fileName) => ES3.Save(key, bytes, fileName);
    }
}
