using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace LordFanger
{
    public static partial class RimLanguageHotReload
    {
        private class GeneTemplateDefs : TemplateDefs<GeneTemplateDef, GeneDef>
        {
            private static MethodInfo _templater = typeof(GeneDefGenerator).GetRuntimeMethods().First(m => m.Name == "GetFromTemplate");
            private ISet<SkillDef> _changedSkills = new HashSet<SkillDef>();
            private ISet<ChemicalDef> _changedChemicals = new HashSet<ChemicalDef>();
            private ISet<GeneTemplateDef> _changedTemplates = new HashSet<GeneTemplateDef>();
            private IDictionary<SkillDef, IList<GeneDef>> _geneDefsBySkill = new Dictionary<SkillDef, IList<GeneDef>>();
            private IDictionary<ChemicalDef, IList<GeneDef>> _geneDefsByChemical = new Dictionary<ChemicalDef, IList<GeneDef>>();
            private IDictionary<GeneTemplateDef, IList<GeneDef>> _geneDefsByTemplate = new Dictionary<GeneTemplateDef, IList<GeneDef>>();
            private IDictionary<GeneDef, GeneTemplateDef> _templateByGene = new Dictionary<GeneDef, GeneTemplateDef>();
            private IDictionary<GeneDef, Def> _secondaryDef = new Dictionary<GeneDef, Def>();

            protected override bool CanRegisterImpliedDef(GeneDef geneDef) => TryGetDefs(geneDef, out _);

            protected override void RegisterImpliedDef(GeneDef geneDef)
            {
                if (!TryGetDefs(geneDef, out var defs)) return;
                var (skillDef, chemicalDef) = defs;
                TryAddToList(_geneDefsBySkill, skillDef, geneDef);
                TryAddToList(_geneDefsByChemical, chemicalDef, geneDef);

                var secondaryDef = (Def)skillDef ?? chemicalDef;
                _secondaryDef.Add(geneDef, secondaryDef);
                var templateDef = GetTemplateDef(geneDef, secondaryDef);
                if (templateDef == null) return;
            
                TryAddToList(_geneDefsByTemplate, templateDef, geneDef);
            }

            protected override bool TryNotifyChanged(Def def) => TryAddChangedToSet(_changedSkills, _geneDefsBySkill, def) || TryAddChangedToSet(_changedChemicals, _geneDefsByChemical, def);

            protected override void NotifyTemplateChanged(GeneTemplateDef templateDef) => TryAddChangedToSet(_changedTemplates, _geneDefsByTemplate, templateDef);

            protected override void ReleaseAllChanges()
            {
                foreach (var changedSkill in _changedSkills)
                {
                    Util.SafeExecute(() =>
                    {
                        foreach (var geneDef in _geneDefsBySkill[changedSkill])
                        {
                            var templateDef = GetTemplateDef(geneDef, changedSkill);
                            UpdateDef(geneDef, templateDef, changedSkill);
                        }
                    });
                }

                foreach (var changedChemical in _changedChemicals)
                {
                    Util.SafeExecute(() =>
                    {
                        foreach (var geneDef in _geneDefsByChemical[changedChemical])
                        {
                            var templateDef = GetTemplateDef(geneDef, changedChemical);
                            UpdateDef(geneDef, templateDef, changedChemical);
                        }
                    });
                }

                foreach (var changedTemplate in _changedTemplates)
                {
                    Util.SafeExecute(() =>
                    {
                        foreach (var geneDef in _geneDefsByTemplate[changedTemplate])
                        {
                            var secondaryDef = _secondaryDef[geneDef];
                            UpdateDef(geneDef, changedTemplate, secondaryDef);
                        }
                    });
                }
            }

            private static bool TryGetDefs(GeneDef geneDef, out (SkillDef skillDef, ChemicalDef chemicalDef) defs)
            {
                if (geneDef.chemical is ChemicalDef chemicalDef)
                {
                    defs = (null, chemicalDef);
                    return true;
                }

                if (TryGetSkilDef(geneDef.defName, out var skillDef))
                {
                    defs = (skillDef, null);
                    return false;
                }

                defs = default;
                return false;
            }

            private GeneTemplateDef GetTemplateDef(GeneDef geneDef, Def def)
            {
                if (_templateByGene.TryGetValue(geneDef, out var templateDef)) return templateDef;
                if (def == null) return null;

                var defSuffix = "_" + def.defName;
                if (!geneDef.defName.EndsWith(defSuffix)) return null;
                var templateDefName = geneDef.defName.Substring(0, geneDef.defName.Length - defSuffix.Length);
                templateDef = DefDatabase<GeneTemplateDef>.GetNamed(templateDefName, false);
                return templateDef;
            }

            private static bool TryGetSkilDef(string defName, out SkillDef skillDef)
            {
                var index = 0;
                do
                {
                    index = defName.IndexOf('_', index);
                    index++;
                    var skillDefName = defName.Substring(index);
                    skillDef = DefDatabase<SkillDef>.GetNamedSilentFail(skillDefName);
                    if (skillDef != null) return true;
                } while (index > 0);
                return false;
            }

            private static void UpdateDef(GeneDef geneDef, GeneTemplateDef templateDef, Def secondaryDef)
            {
                var newDef = (GeneDef)_templater.Invoke(null, new object[] { templateDef, secondaryDef, geneDef.displayOrderInCategory - templateDef.displayOrderOffset });
                geneDef.label = newDef.label;
                geneDef.labelShortAdj = newDef.labelShortAdj;
                geneDef.description = newDef.description;
            }
        }
    }
}
