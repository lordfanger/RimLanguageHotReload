using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Verse;
using static LordFanger.RimLanguageHotReload;

namespace LordFanger
{
    public class RimLanguageHotReloadGameComponent : GameComponent
    {
        private ClearCacheContext _clearCacheContext;
        public RimLanguageHotReloadGameComponent(Game _)
        {

        }

        public override void GameComponentUpdate()
        {
            ClearCache();
        }

        public void InvalidateCache(ClearCacheContext clearCacheContext)
        {
            _clearCacheContext = clearCacheContext;
        }

        private void ClearCache()
        {
            var clearCacheContext = _clearCacheContext;
            if (clearCacheContext is null) return;
            try
            {
                clearCacheContext.CleanAction();
                
                DefDatabase<DesignationCategoryDef>.AllDefsListForReading.ForEach(def => Util.SafeExecute(() =>
                {
                    def.InvokeInstanceMethod("ResolveDesignators");
                }));

                // reopen rename dialog
                Util.SafeExecute(() =>
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

                // refresh generated books titles and descriptions
                Util.SafeExecute(() =>
                {
                    var mentalBreakChancePerHourField = Util.GetInstanceField(typeof(Book), "mentalBreakChancePerHour");
                    var thingRequest = ThingRequest.ForGroup(ThingRequestGroup.Book);
                    var thingList = new List<Thing>();
                    foreach (var book in Find.Maps
                                 .SelectMany(map =>
                                 {
                                     ThingOwnerUtility.GetAllThingsRecursively(map, thingRequest, thingList);
                                     return thingList;
                                 })
                                 .Select(thing => thing as Book)
                                 .Where(book => book != null)
                            )
                    {
                        Util.SafeExecute(() =>
                        {
                            var oldValue = mentalBreakChancePerHourField.GetValue(book);
                            book.GenerateBook();
                            mentalBreakChancePerHourField.SetValue(book, oldValue);
                        });
                    }
                });

                Util.SafeExecute(() =>
                {
                    // log entry cache
                    var cachedStringField = Util.GetInstanceField(typeof(LogEntry), "cachedString"); // read after read, could result in NullReferenceException if not written in UI thread
                    var cachedStringPovField = Util.GetInstanceField(typeof(LogEntry), "cachedStringPov");
                    var cachedHeightWidthField = Util.GetInstanceField(typeof(LogEntry), "cachedHeightWidth");
                    var cachedHeightField = Util.GetInstanceField(typeof(LogEntry), "cachedHeight");
                    foreach (var entry in Find.PlayLog.AllEntries)
                    {
                        cachedStringField.SetValue(entry, null);
                        cachedStringPovField.SetValue(entry, null);
                        cachedHeightWidthField.SetValue(entry, null);
                        cachedHeightField.SetValue(entry, null);
                    }
                });

                // stats in info dialog
                Util.SafeExecute(() =>
                {
                    var newEntryList = new List<StatDrawEntry>();
                    var drawEntriesField = Util.GetStaticField(typeof(StatsReportUtility), "cachedDrawEntries");
                    drawEntriesField.SetValue(null, newEntryList); // swap with new empty list
                });

                // memory thoughts
                Util.SafeExecute(() =>
                {
                    var cachedLabelCapField = Util.GetInstanceField(typeof(Thought_Memory), "cachedLabelCap"); // read after read, could result in NullReferenceException if not written in UI thread
                    foreach (var pawn in Find.Maps.Select(m => m.mapPawns.AllPawns).Concat(Find.WorldPawns.AllPawnsAliveOrDead).SelectMany(p => p))
                    {
                        foreach (var memory in (IReadOnlyList<Thought_Memory>)pawn.needs?.mood?.thoughts?.memories?.Memories ?? Array.Empty<Thought_Memory>())
                        {
                            var cachedValue = cachedLabelCapField.GetValue(memory);
                            if (cachedValue == null) continue;
                            cachedLabelCapField.SetValue(memory, null);
                        }
                    }
                });
                
                // clear cached date
                Util.SafeExecute(DateReadout.Reset);

                // reload active alerts from new ones
                Util.SafeExecute(() =>
                {
                    var alertsReadout = ((UIRoot_Play)Find.UIRoot).alerts;
                    alertsReadout.AlertsReadoutUpdate();
                });

                // clear cached detailed descriptions
                Util.SafeExecute(
                    () =>
                    {
                        foreach (var def in clearCacheContext.Definitions)
                        {
                            Util.SafeExecute(() => def.ClearInstanceField("descriptionDetailedCached"));

                            // clear cached labels for void provocation ritual
                            if (def is PsychicRitualDef_InvocationCircle)
                            {
                                Util.SafeExecute(() => def.ClearInstanceField("timeAndOfferingLabelCached"));
                            }
                        }
                    });

                // clear ideo precepts cache
                Util.SafeExecute(() =>
                {
                    foreach (var ideo in clearCacheContext.Ideos)
                    {
                        foreach (var item in ideo.PreceptsListForReading.OfType<Precept_Ritual>())
                        {
                            item.SetName(item.def.label);
                        }
                    }
                });

                // clear cached activities string
                Util.SafeExecute(() =>
                {
                    Util.GetStaticField(typeof(Need_Learning), "learningActivitiesLineList").SetValue(null, null);
                });
            }
            catch (Exception e)
            {
                Log.Warning($"Error while clearing cache after hot reload: {e.Message}");
            }
            finally
            {
                Interlocked.CompareExchange(ref _clearCacheContext, null, clearCacheContext);
            }
        }
    }
}
