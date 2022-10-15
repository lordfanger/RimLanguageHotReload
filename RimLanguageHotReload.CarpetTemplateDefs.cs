using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace LordFanger
{
    public static partial class RimLanguageHotReload
    {
        private class CarpetTemplateDefs : TemplateDefs<TerrainTemplateDef, TerrainDef>
        {
            private static MethodInfo _templater = typeof(TerrainDefGenerator_Carpet).GetRuntimeMethods().First(m => m.Name == "CarpetFromBlueprint");
            private ISet<ColorDef> _changedColors = new HashSet<ColorDef>();
            private ISet<TerrainTemplateDef> _changedTemplates = new HashSet<TerrainTemplateDef>();
            private IDictionary<ColorDef, IList<TerrainDef>> _terrainDefsByColor = new Dictionary<ColorDef, IList<TerrainDef>>();
            private IDictionary<TerrainTemplateDef, IList<TerrainDef>> _terrainDefsByTemplate = new Dictionary<TerrainTemplateDef, IList<TerrainDef>>();
            private IDictionary<TerrainDef, TerrainTemplateDef> _templateByTerrain = new Dictionary<TerrainDef, TerrainTemplateDef>();

            protected override bool CanRegisterImpliedDef(TerrainDef terrainDef) => TryGetDefs(terrainDef, out _);

            protected override void RegisterImpliedDef(TerrainDef terrainDef)
            {
                if (!TryGetDefs(terrainDef, out var colorDef)) return;
                TryAddToList(_terrainDefsByColor, colorDef, terrainDef);

                var templateDef = GetTemplateDef(terrainDef, colorDef);
                if (templateDef == null) return;
                TryAddToList(_terrainDefsByTemplate, templateDef, terrainDef);
            }

            protected override bool TryNotifyChanged(Def def) => TryAddChangedToSet(_changedColors, _terrainDefsByColor, def);

            protected override void NotifyTemplateChanged(TerrainTemplateDef templateDef) => TryAddChangedToSet(_changedTemplates, _terrainDefsByTemplate, templateDef);

            protected override void ReleaseAllChanges()
            {
                foreach (var changedColor in _changedColors)
                {
                    Util.SafeExecute(() =>
                    {
                        foreach (var terrainDef in _terrainDefsByColor[changedColor])
                        {
                            var templateDef = GetTemplateDef(terrainDef, changedColor);
                            UpdateDef(terrainDef, templateDef, changedColor);
                        }
                    });
                }

                foreach (var changedTemplate in _changedTemplates)
                {
                    Util.SafeExecute(() =>
                    {
                        foreach (var terrainDef in _terrainDefsByTemplate[changedTemplate])
                        {
                            var colorDef = terrainDef.colorDef;
                            UpdateDef(terrainDef, changedTemplate, colorDef);
                        }
                    });
                }
            }

            private static bool TryGetDefs(TerrainDef def, out ColorDef colorDef)
            {
                if (def is TerrainDef terrainDef && terrainDef.colorDef is ColorDef colorDef2)
                {
                    colorDef = colorDef2;
                    return true;
                }

                colorDef = default;
                return false;
            }

            private TerrainTemplateDef GetTemplateDef(TerrainDef terrainDef, ColorDef colorDef)
            {
                if (_templateByTerrain.TryGetValue(terrainDef, out var templateDef)) return templateDef;
                if (colorDef == null) return null;

                var colorDefName = colorDef.defName.Replace("Structure_", "");
                if (!terrainDef.defName.EndsWith(colorDefName)) return null;
                var templateDefName = terrainDef.defName.Substring(0, terrainDef.defName.Length - colorDefName.Length);
                templateDef = DefDatabase<TerrainTemplateDef>.GetNamed(templateDefName, false);
                _templateByTerrain[terrainDef] = templateDef;
                return templateDef;
            }

            private static void UpdateDef(TerrainDef terrainDef, TerrainTemplateDef templateDef, ColorDef colorDef)
            {
                var newDef = (TerrainDef)_templater.Invoke(null, new object[] { templateDef, colorDef, terrainDef.index - templateDef.index });
                terrainDef.label = newDef.label;
                terrainDef.description = newDef.description;
            }
        }
    }
}
