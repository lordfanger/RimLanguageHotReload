namespace LordFanger.IO
{
    public abstract class PathHandle
    {
        private string _cachedPath;

        private string _cachedPathLower;

        protected string InternalPath => _cachedPath ?? CachePath(false);

        protected string InternalPathLower => _cachedPathLower ?? CachePath(true);

        protected abstract string GetPath();

        private string CachePath(bool lower)
        {
            var path = GetPath();
            _cachedPath = path;
            _cachedPathLower = path.ToLower();
            return lower ? _cachedPathLower : _cachedPath;
        }
    }
}