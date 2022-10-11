using LordFanger.IO;
using RimWorld;
using RimWorld.IO;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Verse;
using Verse.Grammar;
using File = System.IO.File;

namespace LordFanger
{
    [StaticConstructorOnStartup]
    public static class RimLanguageHotReload
    {
        private readonly struct DefUniqueKey
        {
            public DefUniqueKey(string name, FileHandle file, string directoryName) : this(name, file.FileNameWithoutExtension, directoryName)
            {
            }

            public DefUniqueKey(string name, string fileNameWithoutExtension, string directoryName)
            {
                Name = name;
                FileName = fileNameWithoutExtension;
                DirectoryName = directoryName;
            }

            public string Name { get; }

            public string FileName { get; }

            public string DirectoryName { get; }
        }

        private readonly struct KeyedUniqueKey
        {
            public KeyedUniqueKey(string key, string filePath)
            {
                Key = key;
                FilePath = filePath;
            }

            public string Key { get; }

            public string FilePath { get; }
        }

        private static readonly IDictionary<string, string> _tkeyToDefPath = new Dictionary<string, string>();

        private static readonly IDictionary<DefUniqueKey, Def> _definitions = new Dictionary<DefUniqueKey, Def>();

        private static readonly IDictionary<KeyedUniqueKey, LoadedLanguage.KeyedReplacement> _keyed = new Dictionary<KeyedUniqueKey, LoadedLanguage.KeyedReplacement>();

        private static DateTime _delayedFswTo;

        private static FileSystemWatcher _fsw;

        private static readonly ICollection<DirectoryHandle> _languageDirectories = new List<DirectoryHandle>();

        private static readonly ConcurrentDictionary<string, bool> _changedFiles = new ConcurrentDictionary<string, bool>();

        private static LoadedLanguage ActiveLanguage => LanguageDatabase.activeLanguage;

        static RimLanguageHotReload()
        {
            LoadDefinitions();
            LoadKeyed();
            LoadTKeys();

            Task.Run(() =>
            {
                LoadDirectories();
                StartUpdateLoop();
                StartFileSystemWatcher();
            });
        }

        private static void LoadDefinitions()
        {
            Util.SafeExecute(
                () =>
                {
                    var defTypes = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(a => a.GetTypes())
                        .Where(t => typeof(Def).IsAssignableFrom(t));
                    var defsDbType = typeof(DefDatabase<>);
                    foreach (var defType in defTypes)
                    {
                        Util.SafeExecute(
                            () =>
                            {
                                var defDbType = defsDbType.MakeGenericType(defType);
                                var defDbProperty = defDbType.GetRuntimeProperties()
                                    .First(p => p.Name == nameof(DefDatabase<Def>.AllDefs));
                                if (!(defDbProperty.GetValue(null) is IEnumerable defDb)) return;

                                foreach (Def def in defDb)
                                {
                                    var directoryName = def.GetType().Name;
                                    var fileName = def.fileName;
                                    var defName = def.defName;

                                    var key = new DefUniqueKey(defName, Path.GetFileNameWithoutExtension(fileName), directoryName);
                                    if (!_definitions.ContainsKey(key)) _definitions.Add(key, def); // todo compare if equals
                                }
                            });
                    }
                });
        }

        private static void ReloadKeyed()
        {
            _keyed.Clear();
            LoadKeyed();
        }

        private static void LoadKeyed()
        {
            foreach (var kv in ActiveLanguage.keyedReplacements)
            {
                var keyed = kv.Value;
                _keyed.Add(new KeyedUniqueKey(keyed.key, keyed.fileSourceFullPath.ToLower()), keyed);
            }
        }

        private static void LoadTKeys()
        {
            var tKeyToNormalizedTranslationKey = (IDictionary<string, string>)Util.GetStaticField(typeof(TKeySystem), "tKeyToNormalizedTranslationKey").GetValue(null);
            foreach (var kv in tKeyToNormalizedTranslationKey)
            {
                _tkeyToDefPath.Add(kv.Key, kv.Value);
            }
        }

        private static void StartFileSystemWatcher()
        {
            var fsw = new FileSystemWatcher
            {
                Path = AppDomain.CurrentDomain.BaseDirectory,
                IncludeSubdirectories = true,
                Filter = "*"
            };
            fsw.Changed += (_, e) => EnqueueFileChange(e.FullPath);
            fsw.Created += (_, e) => EnqueueFileChange(e.FullPath);
            fsw.Error += (_, e) =>
            {
                Util.SafeExecute(fsw.Dispose);
                Log.Warning($"Error in file system watching - {e.GetException().Message}\nRestarting file system watcher.");
                StartFileSystemWatcher();
            };
            _fsw = fsw;
            _fsw.EnableRaisingEvents = true;
        }

        private static void EnqueueFileChange(string filePath)
        {
            _changedFiles.TryAdd(filePath, true);
            _delayedFswTo = DateTime.Now.AddMilliseconds(500);
        }

        private static void LoadDirectories()
        {
            foreach (var (virtualDirectory, _, _) in ActiveLanguage.AllDirectories)
            {
                _languageDirectories.Add(virtualDirectory.FullPath.AsDirectory());
            }
        }

        private static void StartUpdateLoop()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(10);
                    if (_changedFiles.Count == 0) continue;
                    if (_delayedFswTo > DateTime.Now) continue;
                    Util.SafeExecute(UpdateChangedFiles);
                }
                // ReSharper disable once FunctionNeverReturns
            });
        }

        private static void UpdateChangedFiles()
        {
            var filePaths = _changedFiles.Keys.ToArray();
            foreach (var filePath in filePaths)
            {
                _changedFiles.TryRemove(filePath, out _);
            }

            if (filePaths.Length == 0) return;
            UpdateFiles(filePaths.AsFiles());
            ClearCaches();
        }

        private static void UpdateFiles(IEnumerable<FileHandle> filePaths)
        {
            foreach (var filePath in filePaths) UpdateFile(filePath);
        }

        private static void UpdateFile(FileHandle file)
        {
            Util.SafeExecute(
                () =>
                {
                    var directory = file.Directory;

                    if (!IsActiveLanguageSubfolder(directory, out var rootDirectory))
                    {
                        return;
                    }

                    if (!file.Exists())
                    {
                        return;
                    }

                    if (file.HasExtension(".xml"))
                    {
                        UpdateXmlFile(file, directory);
                        return;
                    }

                    if (file.HasExtension(".txt"))
                    {
                        UpdateTxtFile(file, directory, rootDirectory);
                        return;
                    }
                });
        }

        private static void UpdateXmlFile(FileHandle file, DirectoryHandle directory)
        {
            var directoryName = directory.DirectoryName;
            XDocument xml;
            try
            {
                var xmlContent = file.ReadAllText();
                xml = XDocument.Parse(xmlContent);
            }
            catch
            {
                return;
            }

            var root = xml.Root;
            if (root == null) throw new Exception("Invalid XML");

            if (root.Name == "LanguageData")
            {
                UpdateLanguageData(file, directoryName, root);
            }
        }

        private static void UpdateLanguageData(FileHandle file, string directoryName, XElement root)
        {
            foreach (var item in root.Elements())
            {
                if (item.NodeType != XmlNodeType.Element) continue;

                var defFieldName = item.Name.LocalName;
                var tkeyMatch = Regex.Match(defFieldName, @"^(?<TKEY>[^\.]+\.[^\.]+)");
                if (tkeyMatch.Success)
                {
                    var tkey = tkeyMatch.Groups["TKEY"].Value;
                    if (_tkeyToDefPath.TryGetValue(tkey, out var tkeyDefPath))
                    {
                        defFieldName = $"{tkeyDefPath}{defFieldName.Substring(tkey.Length)}";
                    }
                }

                var match = Regex.Match(defFieldName, @"^(?<DEF_NAME>[^\.]+)\.(?<FIELD_PATH>.+)$");
                if (match.Success)
                {
                    var defName = match.Groups["DEF_NAME"].Value;
                    var fieldPathRaw = match.Groups["FIELD_PATH"].Value;
                    UpdateDefinition(defName, fieldPathRaw, file, directoryName, item);
                }
                else
                {
                    UpdateKeyed(file, item);
                }
            }
        }

        private static void UpdateDefinition(string defName, string fieldPathRaw, FileHandle file, string directoryName, XElement item)
        {
            var fieldPath = fieldPathRaw.Split('.');

            if (!_definitions.TryGetValue(new DefUniqueKey(defName, file, directoryName), out var def))
            {
                return;
            }

            UpdateDefinitionFieldByPath(def, fieldPath[0], fieldPath, 1, item, null);
        }

        private static void UpdateDefinitionFieldByPath(object obj, string fieldName, string[] fieldPath, int fieldPathOffset, XElement item, Attribute[] fieldAttributes)
        {
            fieldAttributes ??= Array.Empty<Attribute>();
            var objType = obj.GetType();
            var fieldByName = Util.GetTypeInstanceFieldsWithAtrtibutesByName(objType);

            if (TryUpdateInUntranslantedCollection(obj, fieldName, fieldPath, fieldPathOffset, item, fieldAttributes))
            {
                return;
            }

            if (!fieldByName.TryGetValue(fieldName, out var field) || field == null)
            {
                return;
            }

            var fieldInfo = field.FieldInfo;
            var fieldValue = fieldInfo.GetValue(obj);
            if (fieldPathOffset < fieldPath.Length)
            {
                if (fieldValue == null)
                {
                    return;
                }
                UpdateDefinitionFieldByPath(fieldValue, fieldPath[fieldPathOffset], fieldPath, fieldPathOffset + 1, item, field.Attributes);
            }
            else
            {
                if (fieldInfo.FieldType == typeof(string))
                {
                    UpdateStringDefinition(obj, objType, fieldName, fieldInfo, fieldPath, fieldPathOffset, item);
                    return;
                }

                if (objType == typeof(RulePack) && typeof(IList<string>).IsAssignableFrom(fieldInfo.FieldType))
                {
                    if (TryUpdateRulesDefinition(obj, objType, fieldName, fieldInfo, fieldPath, fieldPathOffset, item))
                    {
                        TryClearCachedValue(obj, "cachedRules", item);
                    }
                    return;
                }
            }
        }

        private static void UpdateStringDefinition(object obj, Type objType, string fieldName, FieldInfo fieldInfo, string[] fieldPath, int fieldPathOffset, XElement item)
        {
            var oldValue = (string)fieldInfo.GetValue(obj);
            var newValue = GetNewValue(item);

            if (newValue == null)
            {
                return;
            }

            if (newValue == oldValue)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(oldValue) && string.IsNullOrWhiteSpace(newValue))
            {
                return;
            }

            var fieldsByName = Util.GetTypeInstanceFieldsByName(objType);
            fieldInfo.SetValue(obj, newValue);
            TryClearCachedValue(obj, fieldName.ToCachedName(), item, fieldsByName);
            TryClearCachedValue(obj, fieldName.ToCachedName() + "Cap", item, fieldsByName);
        }

        private static bool TryUpdateRulesDefinition(object obj, Type objType, string fieldName, FieldInfo fieldInfo, string[] fieldPath, int fieldPathOffset, XElement item)
        {
            var oldValue = (IList<string>)fieldInfo.GetValue(obj);
            var newValue = new List<string>();
            foreach (var li in item.Elements())
            {
                if (li.Name != "li") throw new Exception($"Unknown element {li} found");
                var value = li.Value;
                var rule = value.Replace("\\n", "\n");
                newValue.Add(rule);
            }

            if (oldValue.SequenceEqual(newValue))
            {
                return false;
            }

            newValue.TrimExcess();

            // clear local rules cache
            fieldInfo.SetValue(obj, newValue);
            TryClearCachedValue(obj, "rulesResolved", item);

            // clear global rules cache
            TryClearCachedValue(RulePackDefOf.GlobalUtility, "cachedRules", item);
            return true;
        }

        private static bool TryUpdateInUntranslantedCollection(object obj, string fieldName, string[] fieldPath, int fieldPathOffset, XElement item, Attribute[] fieldAttributes)
        {
            if (!(obj is ICollection collection)) return false;

            if (int.TryParse(fieldName, out var index) && index >= 0 && index < collection.Count)
            {
                if (fieldPath.Length == fieldPathOffset && collection is IList<string> list/* && fieldAttributes.Any<MustTranslateAttribute>()*/)
                {
                    var oldValue = list[index];
                    var newValue = GetNewValue(item);

                    if (newValue == null)
                    {
                        return false;
                    }

                    if (oldValue == newValue)
                    {
                        return true;
                    }

                    if (string.IsNullOrWhiteSpace(oldValue) && string.IsNullOrWhiteSpace(newValue))
                    {
                        return true;
                    }

                    list[index] = newValue;
                }
                else
                {
                    UpdateDefinitionFieldByPath(collection.ElementAt(index), fieldPath[fieldPathOffset], fieldPath, fieldPathOffset + 1, item, null);
                }

                return true;
            }

            var match = Regex.Match(fieldName, @"^(?<NAME>.+?)(\-(?<INDEX>\d+))?$");
            var untranslantedName = match.Groups["NAME"].Value;
            var untranslantedIndex = 0;
            var validItemIndex = 0;
            if (match.Groups["INDEX"].Success) untranslantedIndex = int.Parse(match.Groups["INDEX"].Value);
            foreach (var collectionItem in collection)
            {
                if (collectionItem == null) continue;

                var untranslantedFields = Util.GetTypeUntranslantedFieldsWithAttributes(collectionItem.GetType());
                foreach (var untranslantedField in untranslantedFields)
                {
                    var rawValueForTranslation = untranslantedField.FieldInfo.GetValue(collectionItem);
                    var valueForTranslation = (rawValueForTranslation as string).ToNormalizedHandle();
                    if (valueForTranslation == null && rawValueForTranslation is Def def) valueForTranslation = def.defName;

                    if (valueForTranslation == null && rawValueForTranslation is Type type)
                    {
                        valueForTranslation = type.Name.ToNormalizedHandle();
                        if (valueForTranslation != untranslantedName)
                        {
                            valueForTranslation = type.FullName.ToNormalizedHandle();
                            if (valueForTranslation != untranslantedName) continue;
                        }
                    }
                    else if (valueForTranslation != untranslantedName) continue;

                    // index of valid item by untranslanted
                    if (untranslantedIndex != validItemIndex)
                    {
                        validItemIndex++;
                        continue;
                    }

                    UpdateDefinitionFieldByPath(collectionItem, fieldPath[fieldPathOffset], fieldPath, fieldPathOffset + 1, item, untranslantedField.Attributes);
                    return true;
                }
            }

            return false;
        }

        private static void UpdateKeyed(FileHandle file, XElement item)
        {
            var keyedName = item.Name.LocalName;
            if (!_keyed.TryGetValue(new KeyedUniqueKey(keyedName, file.FilePathLower), out var keyed))
            {
                // try to reload file keyed file if was missing at game initialization
                // TODO could take very long when many files are missing
                ReloadKeyedFile(file);
                if (!_keyed.TryGetValue(new KeyedUniqueKey(keyedName, file.FilePathLower), out keyed))
                {
                    return;
                }
            }

            var oldValue = keyed.value;
            var newValue = GetNewValue(item);

            if (newValue == oldValue)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(oldValue) && string.IsNullOrWhiteSpace(newValue))
            {
                return;
            }

            keyed.value = newValue;
            keyed.isPlaceholder = string.IsNullOrWhiteSpace(newValue);
        }

        private static void ReloadKeyedFile(FileHandle file)
        {
            Util.SafeExecute(
                () =>
                {
                    var virtualDirectory = AbstractFilesystem.GetDirectory(file.Directory.DirectoryPath);
                    var virtualFile = virtualDirectory.GetFile(file.FileName);

                    // reload translation file in game
                    ActiveLanguage.InvokeInstanceMethod("LoadFromFile_Keyed", virtualFile);

                    // reload hot reload keyed cache
                    ReloadKeyed();
                });
        }

        private static void TryClearCachedValue(object obj, string cachedFieldName, XElement item, IDictionary<string, FieldInfo> fieldByName = null)
        {
            fieldByName ??= Util.GetTypeInstanceFieldsByName(obj.GetType());
            if (!fieldByName.TryGetValue(cachedFieldName, out var cachedField)) return;
            if (cachedField.GetValue(obj) == null) return;

            cachedField.SetValue(obj, null);
        }

        private static string GetNewValue(XElement item)
        {
            var value = item.Value;

            var unescapedValue = value.Replace("\\n", "\n"); // only escape character used
            return unescapedValue == "TODO" ? null : unescapedValue;
        }

        private static void UpdateTxtFile(FileHandle file, DirectoryHandle directory, DirectoryHandle rootDirectory)
        {
            var relativePath = directory.GetRelativePathTo(rootDirectory);
            if (relativePath.Count == 0) return;
            if (relativePath[0].Equals("WordInfo"))
            {
                var directoryName = relativePath[1];
                if (directoryName == "Gender")
                {
                    UpdateWordInfoGenderFile();
                }
                else
                {
                    UpdateWordInfoLookupFile(file, relativePath);
                }
                return;
            }

            if (relativePath[0].Equals("Strings"))
            {
                UpdateStringsFile(file, relativePath);
            }
        }

        private static void UpdateWordInfoGenderFile()
        {
            Util.SafeExecute(
                () =>
                {
                    var wordInfo = ActiveLanguage.WordInfo;
                    var genders = wordInfo.GetInstanceFieldValue<Dictionary<string, Gender>>("genders");
                    genders.Clear();
                    foreach (var directory in ActiveLanguage.AllDirectories)
                    {
                        RemoveCachedGenderFiles(directory.Item2);
                        wordInfo.LoadFrom(directory, ActiveLanguage);
                    }
                });

            void RemoveCachedGenderFiles(ModContentPack modContentPack)
            {
                var alreadyLoadedFiles = ActiveLanguage.GetInstanceFieldValue<Dictionary<ModContentPack, HashSet<string>>>("tmpAlreadyLoadedFiles");
                var modLoadedFiles = alreadyLoadedFiles[modContentPack];
                var loadedFiles = modLoadedFiles.ToList();
                foreach (var file in loadedFiles)
                {
                    if (file.EndsWith(@"\WordInfo\Gender\Male.txt", StringComparison.OrdinalIgnoreCase)) modLoadedFiles.Remove(file);
                    if (file.EndsWith(@"\WordInfo\Gender\Female.txt", StringComparison.OrdinalIgnoreCase)) modLoadedFiles.Remove(file);
                    if (file.EndsWith(@"\WordInfo\Gender\Neuter.txt", StringComparison.OrdinalIgnoreCase)) modLoadedFiles.Remove(file);
                }
            }
        }

        private static void UpdateWordInfoLookupFile(FileHandle file, IReadOnlyList<string> relativePath)
        {
            Util.SafeExecute(
                () =>
                {
                    var databaseName = $"{string.Join("\\", relativePath.Skip(1))}\\{file.FileNameWithoutExtension}".ToLower();
                    var wordInfo = ActiveLanguage.WordInfo;
                    var lookupTables = wordInfo.GetInstanceFieldValue<Dictionary<string, Dictionary<string, string[]>>>("lookupTables");
                    lookupTables.Remove(databaseName);
                    wordInfo.RegisterLut(databaseName);
                });
        }

        private static void UpdateStringsFile(FileHandle file, IReadOnlyList<string> relativePath)
        {
            var stringsCache = ActiveLanguage.GetInstanceFieldValue<Dictionary<string, List<string>>>("stringFiles");
            var relativeStringsFile = $"{string.Join("\\", relativePath.Skip(1))}\\{file.FileNameWithoutExtension}";
            var relativeStringsFileKey = relativeStringsFile.Replace('\\', '/');
            stringsCache.Remove(relativeStringsFileKey);

            var stringsList = new List<string>();
            foreach (var (directory, _, _) in ActiveLanguage.AllDirectories)
            {
                var filePath = $"{directory}\\Strings\\{relativeStringsFile}.txt";
                if (!File.Exists(filePath)) continue;
                var text = File.ReadAllText(filePath);
                var lines = GenText.LinesFromString(text);
                stringsList.AddRange(lines);
            }

            stringsList.TrimExcess();
            stringsCache.Add(relativeStringsFileKey, stringsList);
        }

        private static bool IsActiveLanguageSubfolder(DirectoryHandle directoryHandle, out DirectoryHandle languageRootHandle)
        {
            foreach (var directory in _languageDirectories)
            {
                if (!directoryHandle.StartsWith(directory)) continue;

                languageRootHandle = directory;
                return true;
            }

            languageRootHandle = null;
            return false;
        }

        private static void ClearCaches()
        {
            // label cache
            GenLabel.ClearCache();

            // art tab cache
            Util.ClearStaticField(typeof(ITab_Art), "cachedImageDescription");
            Util.ClearStaticField(typeof(ITab_Art), "cachedImageSource");
            Util.ClearStaticField(typeof(ITab_Art), "cachedTaleRef");

            // cache for colorization
            ColoredText.ClearCache();
            ColoredText.ResetStaticData();

            // designators cache
            Find.ReverseDesignatorDatabase.Reinit();

            // cache only in game
            if (Current.Game.Maps.Count > 0)
            {
                ClearInGameCaches();
            }
        }

        private static void ClearInGameCaches()
        {
            // log entry cache
            var cachedStringField = Util.GetInstanceField(typeof(LogEntry), "cachedString");
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

            // memory thoughts
            var cachedLabelCapField = Util.GetInstanceField(typeof(Thought_Memory), "cachedLabelCap");
            foreach (var pawn in Find.Maps.Select(m => m.mapPawns.AllPawns).Concat(Find.WorldPawns.AllPawnsAliveOrDead).SelectMany(p => p))
            {
                foreach (var memory in (IReadOnlyList<Thought_Memory>)pawn.needs?.mood?.thoughts?.memories?.Memories ?? Array.Empty<Thought_Memory>())
                {
                    var cachedValue = cachedLabelCapField.GetValue(memory);
                    if (cachedValue == null) continue;
                    cachedLabelCapField.SetValue(memory, null);
                }
            }

            // stats in info dialog
            var newEntryList = new List<StatDrawEntry>();
            var drawEntriesField = Util.GetStaticField(typeof(StatsReportUtility), "cachedDrawEntries");
            var oldEntryList = (List<StatDrawEntry>)drawEntriesField.GetValue(null);
            drawEntriesField.SetValue(null, newEntryList);
            // dispose later, whan should not by in use
            Task.Run(
                async () =>
                {
                    await Task.Delay(500);
                    oldEntryList.Clear();
                });

            // clear cached lights texts
            var mouseoverReadout = Find.MapUI.GetInstanceFieldValue<MouseoverReadout>("mouseoverReadout");
            Util.SafeExecute(() =>
            {
                mouseoverReadout.InvokeInstanceMethod("MakePermaCache");
                mouseoverReadout.ClearInstanceField("cachedTerrain");
            });

            // clear cached alerts
            Util.SafeExecute(
                () =>
                {
                    var alertsReadout = ((UIRoot_Play)Find.UIRoot).alerts;
                    var allAlerts = alertsReadout.GetInstanceFieldValue<List<Alert>>("AllAlerts");

                    // recreate all alerts
                    // TODO can be dangerous for some cached fields, that will not be initialized properlly
                    // TODO maybe can be enough copy all string values (namely defaultLabel and defaultExplanation
                    for (var i = 0; i < allAlerts.Count; i++)
                    {
                        var alert = allAlerts[i];
                        var alertType = alert.GetType();
                        var newAlert = (Alert)Activator.CreateInstance(alertType);
                        allAlerts[i] = newAlert;
                    }

                    // remove old alerts
                    var oldAlerts = alertsReadout.GetInstanceFieldValue<List<Alert>>("activeAlerts");
                    oldAlerts.Clear();

                    // remove cached precept alerts
                    var oldPreceptAlerts = alertsReadout.GetInstanceFieldValue<Dictionary<Precept, List<Alert>>>("activePreceptAlerts");
                    oldPreceptAlerts.Clear();

                    // reload active alerts from new ones
                    alertsReadout.AlertsReadoutUpdate();
                });

            // clear cached date
            DateReadout.Reset();

            // clear cache from main thread
            Current.Game.GetComponent<RimLanguageHotReloadGameComponent>().InvalidateCache();
        }
    }
}
