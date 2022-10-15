using System.Collections.Generic;
using Verse;

namespace LordFanger
{
    public static partial class RimLanguageHotReload
    {
        private class TemplateDefsResolver
        {
            private readonly IList<TemplateDefs> _templateDefs = new TemplateDefs[]
            {
                new CarpetTemplateDefs(),
                new GeneTemplateDefs()
            };

            public void AddImpliedDefIfAny(Def def)
            {
                foreach (var templateDef in _templateDefs)
                {
                    if (templateDef.TryRegisterImpliedDef(def)) return;
                }
            }

            public void NotifyDefChanged(Def def)
            {
                foreach (var templateDef in _templateDefs)
                {
                    templateDef.NotifyChanged(def);
                }
            }

            public void ReleaseChanges()
            {
                foreach (var templateDef in _templateDefs)
                {
                    templateDef.ReleaseChanges();
                }
            }
        }
    }
}
