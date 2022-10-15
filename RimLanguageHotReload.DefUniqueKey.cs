using LordFanger.IO;

namespace LordFanger
{
    public static partial class RimLanguageHotReload
    {
        private readonly struct DefUniqueKey
        {
            public DefUniqueKey(string name, FileHandle file, string directoryName) : this(name, file.FileNameWithoutExtension, directoryName)
            {
            }

            public DefUniqueKey(string name, string fileNameWithoutExtension, string directoryName)
            {
                Name = name;
                FileName = fileNameWithoutExtension;
                DirectoryName = directoryName;
            }

            public string Name { get; }

            public string FileName { get; }

            public string DirectoryName { get; }
        }
    }
}
