using Verse;

namespace LordFanger
{
    public class RimLanguageHotReloadGameComponent : GameComponent
    {
        private bool _clearCache;
        public RimLanguageHotReloadGameComponent(Game _)
        {

        }

        public override void GameComponentUpdate()
        {
            ClearCache();
        }

        public void InvalidateCache()
        {
            _clearCache = true;
        }

        private void ClearCache()
        {
            if (!_clearCache) return;
            DefDatabase<DesignationCategoryDef>.AllDefsListForReading.ForEach(def => Util.SafeExecute(() =>
            {
                def.InvokeInstanceMethod("ResolveDesignators");
            }));
            _clearCache = false;
        }
    }
}
