using System.Collections.Generic;
using Verse;

namespace LordFanger
{
    public static partial class RimLanguageHotReload
    {
        private abstract class TemplateDefs
        {
            public abstract bool TryRegisterImpliedDef(Def def);

            public abstract void NotifyChanged(Def def);

            public abstract void ReleaseChanges();

            protected static void TryAddToList<TKey, TValue>(IDictionary<TKey, IList<TValue>> dictionary, TKey key, TValue value)
            {
                if (key == null) return;
                if (!dictionary.TryGetValue(key, out var list))
                {
                    list = new List<TValue>();
                    dictionary[key] = list;
                }
                list.Add(value);
            }

            protected static bool TryAddChangedToSet<TKey, TValue>(ISet<TKey> changedSet, IDictionary<TKey, TValue> dictionary, Def def)
            {
                if (def is TKey key) return TryAddChangedToSet(changedSet, dictionary, key);
                return false;
            }

            protected static bool TryAddChangedToSet<TKey, TValue>(ISet<TKey> changedSet, IDictionary<TKey, TValue> dictionary, TKey key)
            {
                if (dictionary.ContainsKey(key)) return changedSet.Add(key);
                return false;
            }
        }

        private abstract class TemplateDefs<TTemplate, TImplied> : TemplateDefs
            where TTemplate : Def
            where TImplied : Def
        {
            private bool _anyChange;

            public override bool TryRegisterImpliedDef(Def def)
            {
                if (!IsImpliedDef(def, out var impliedDef)) return false;
                RegisterImpliedDef(impliedDef);
                return true;
            }

            public override void NotifyChanged(Def def)
            {
                if (IsTemplateDef(def, out var templateDef))
                {
                    NotifyTemplateChanged(templateDef);
                    _anyChange = true;
                }

                if (TryNotifyChanged(def)) _anyChange = true;
            }

            public override void ReleaseChanges()
            {
                if (!_anyChange) return;

                ReleaseAllChanges();
                _anyChange = false;
            }

            private bool IsImpliedDef(Def def, out TImplied impliedDef)
            {
                impliedDef = default;
                if (!def.generated) return false;
                impliedDef = def as TImplied;
                if (impliedDef == null) return false;
                if (!CanRegisterImpliedDef(impliedDef)) return false;
                return true;
            }

            private bool IsTemplateDef(Def def, out TTemplate templateDef)
            {
                templateDef = def as TTemplate;
                return templateDef != null;
            }

            protected abstract bool CanRegisterImpliedDef(TImplied def);

            protected abstract void RegisterImpliedDef(TImplied def);

            protected abstract bool TryNotifyChanged(Def def);

            protected abstract void NotifyTemplateChanged(TTemplate def);

            protected abstract void ReleaseAllChanges();
        }
    }
}
