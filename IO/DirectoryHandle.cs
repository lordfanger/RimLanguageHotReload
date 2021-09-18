using System.Collections.Generic;
using System;
using System.IO;

namespace LordFanger.IO
{
    public class DirectoryHandle : PathHandle
    {
        private int _cachedLevel = -1;

        public DirectoryHandle(string path)
        {
            var directoryName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(directoryName))
            {
                DirectoryName = path;
            }
            else
            {
                DirectoryName = directoryName;
                var parent = Path.GetDirectoryName(path);
                ParentDirectory = parent == null ? null : new DirectoryHandle(parent);
            }
        }

        public DirectoryHandle(string name, DirectoryHandle parent)
        {
            DirectoryName = name;
            ParentDirectory = parent;
        }

        public string DirectoryName { get; }

        public DirectoryHandle ParentDirectory { get; }

        public string DirectoryPath => InternalPath;

        public string DirectoryPathLower => InternalPathLower;

        public int Level => _cachedLevel != -1 ? _cachedLevel : _cachedLevel = GetLevel();

        public bool IsRoot => ParentDirectory == null;

        protected override string GetPath()
        {
            if (IsRoot) return DirectoryName;
            return Path.Combine(ParentDirectory.InternalPath, DirectoryName);
        }

        private int GetLevel()
        {
            if (IsRoot) return 0;
            return ParentDirectory.Level + 1;
        }

        public bool Exists() => Directory.Exists(DirectoryPath);

        public bool StartsWith(DirectoryHandle directoryHandle)
        {
            if (directoryHandle == null) return false;
            var ancestor = BackToLevel(directoryHandle.Level);
            if (ancestor == null) return false;
            return ancestor.Equals(directoryHandle);
        }

        public DirectoryHandle BackToLevel(int level)
        {
            if (level > Level || level < 0) return null;
            var parent = this;
            while (parent.Level > level)
            {
                parent = parent.ParentDirectory;
            }
            return parent;
        }

        public IReadOnlyList<string> GetRelativePathTo(DirectoryHandle baseDirectoryHandle)
        {
            if (Level <= baseDirectoryHandle.Level) return Array.Empty<string>();
            var parts = new List<string>();
            var directory = this;
            while (!directory.Equals(baseDirectoryHandle))
            {
                parts.Add(directory.DirectoryName);
                directory = directory.ParentDirectory;
            }

            parts.Reverse();
            return parts;
        }

        public DirectoryHandle SubDirectory(string name) => new DirectoryHandle(name, this);

        public FileHandle File(string name) => new FileHandle(name, this);

        /// <inheritdoc />
        public override string ToString() => DirectoryPath;

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return Equals(obj as DirectoryHandle);
        }

        public bool Equals(DirectoryHandle otherDirectory)
        {
            if (otherDirectory == null) return false;
            if (Level != otherDirectory.Level) return false;
            if (!string.Equals(DirectoryName, otherDirectory.DirectoryName, System.StringComparison.OrdinalIgnoreCase)) return false;
            if (IsRoot && otherDirectory.IsRoot) return true;
            if (IsRoot || otherDirectory.IsRoot) return false;
            return ParentDirectory.Equals(otherDirectory.ParentDirectory);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }
    }
}