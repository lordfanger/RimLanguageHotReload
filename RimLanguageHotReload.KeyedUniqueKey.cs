namespace LordFanger
{
    public static partial class RimLanguageHotReload
    {
        private readonly struct KeyedUniqueKey
        {
            public KeyedUniqueKey(string key, string filePath)
            {
                Key = key;
                FilePath = filePath;
            }

            public string Key { get; }

            public string FilePath { get; }
        }
    }
}
