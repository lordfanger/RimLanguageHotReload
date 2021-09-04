using System.IO;

namespace LordFanger.IO
{
    public class FileHandle : PathHandle
    {
        private string _cachedFileNameWithoutExtension;

        private string _cachedExtension;

        public FileHandle(string path)
        {
            FileName = Path.GetFileName(path);
            var parent = Path.GetDirectoryName(path);
            Directory = parent == null ? null : new DirectoryHandle(parent);
        }

        public FileHandle(string name, DirectoryHandle directory)
        {
            FileName = name;
            Directory = directory;
        }

        public string FileName { get; }

        public DirectoryHandle Directory { get; }

        public string FilePath => InternalPath;

        public string FilePathLower => InternalPathLower;

        public string FileNameWithoutExtension => _cachedFileNameWithoutExtension ??= Path.GetFileNameWithoutExtension(FileName);

        public string Extension => _cachedExtension ??= Path.GetExtension(FileName);

        public bool HasExtension(string extension) => Extension.Equals(extension, System.StringComparison.OrdinalIgnoreCase);

        protected override string GetPath() => Path.Combine(Directory.ToString(), FileName);

        public bool Exists() => File.Exists(FilePath);

        public string ReadAllText() => File.ReadAllText(FilePath);

        /// <inheritdoc />
        public override string ToString() => FilePath;

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (!(obj is FileHandle otherFile)) return false;
            if (!string.Equals(FileName, otherFile.FileName, System.StringComparison.OrdinalIgnoreCase)) return false;

            return Directory.Equals(otherFile.Directory);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }

    }
}