using System.Collections.Generic;
using System.Linq;

namespace LordFanger.IO
{
    public static class PathExtensions
    {
        public static DirectoryHandle AsDirectory(this string path)
        {
            if (path == null) return null;
            var directory = new DirectoryHandle(path);
            return directory;
        }

        public static IEnumerable<DirectoryHandle> AsDirectories(this IEnumerable<string> directories) => directories.Select(AsDirectory);

        public static FileHandle AsFile(this string path)
        {
            if (path == null) return null;
            var file = new FileHandle(path);
            return file;
        }

        public static IEnumerable<FileHandle> AsFiles(this IEnumerable<string> files) => files.Select(AsFile);
    }
}
