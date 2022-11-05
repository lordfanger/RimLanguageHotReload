using RimWorld;
using System.Linq;
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
            try
            {
                DefDatabase<DesignationCategoryDef>.AllDefsListForReading.ForEach(def => Util.SafeExecute(() =>
                {
                    def.InvokeInstanceMethod("ResolveDesignators");
                }));

                // reopen rename dialog
                Util.SafeExecute(
                    () =>
                    {
                        var windowStack = Find.WindowStack;
                        if (!(windowStack.Windows.FirstOrDefault(w => w is Dialog_NamePawn) is Dialog_NamePawn renameDialog)) return;
                        var pawn = renameDialog.GetInstanceFieldValue<Pawn>("pawn");
                        if (pawn == null) return;
                        windowStack.TryRemove(renameDialog.GetType(), false);
                        windowStack.Add(pawn.NamePawnDialog());
                    });

                // clear tips for research projects
                DefDatabase<ResearchProjectDef>.AllDefsListForReading.ForEach(def => Util.SafeExecute(
                    () =>
                    {
                        def.ClearInstanceField("cachedTip");
                    }));
            }
            finally
            {
                _clearCache = false;
            }
        }
    }
}
