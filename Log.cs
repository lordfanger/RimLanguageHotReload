namespace LordFanger
{
    public static class Log
    {
        public static void Message(string message) => Verse.Log.Message($"{nameof(RimLanguageHotReload)}: {message}");

        public static void Warning(string message) => Verse.Log.Warning($"{nameof(RimLanguageHotReload)}: {message}");
    }
}
