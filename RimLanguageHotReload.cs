using LordFanger.IO;
using RimWorld;
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
using Verse;

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

        private static readonly Dictionary<string, Backstory> _backstories = new Dictionary<string, Backstory>();

        private static readonly IDictionary<DefUniqueKey, Def> _definitions = new Dictionary<DefUniqueKey, Def>();

        private static readonly IDictionary<KeyedUniqueKey, LoadedLanguage.KeyedReplacement> _keyed = new Dictionary<KeyedUniqueKey, LoadedLanguage.KeyedReplacement>();

        private static readonly IDictionary<string, IDictionary<string, Def>> _defsByFileName = new Dictionary<string, IDictionary<string, Def>>();

        //private static readonly IDictionary<string, IDictionary<string, FieldInfo>> _fieldsByFullTypeName = new Dictionary<string, IDictionary<string, FieldInfo>>();

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
            LoadBackstories();

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
                                    if (!_defsByFileName.TryGetValue(fileName, out var defByDefName))
                                    {
                                        defByDefName = new Dictionary<string, Def>();
                                        _defsByFileName[fileName] = defByDefName;
                                    }

                                    defByDefName[defName] = def;

                                    var key = new DefUniqueKey(defName, Path.GetFileNameWithoutExtension(fileName), directoryName);
                                    if (!_definitions.ContainsKey(key)) _definitions.Add(key, def); // todo compare if equals
                                }
                            });
                    }

                });
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
            var tKeyToNormalizedTranslationKey = (IDictionary<string, string>)Util.GetTypeStaticField(typeof(TKeySystem), "tKeyToNormalizedTranslationKey").GetValue(null);
            foreach (var kv in tKeyToNormalizedTranslationKey)
            {
                _tkeyToDefPath.Add(kv.Key, kv.Value);
            }
        }

        private static void LoadBackstories()
        {
            foreach (var kv in BackstoryDatabase.allBackstories)
            {
                _backstories.Add(kv.Key, kv.Value);
            }
        }

        private static void StartFileSystemWatcher()
        {
            _fsw = new FileSystemWatcher();
            _fsw.Path = AppDomain.CurrentDomain.BaseDirectory;
            _fsw.IncludeSubdirectories = true;
            _fsw.Filter = "*.xml";
            _fsw.Changed += (_, e) => EnqueueFileChange(e.FullPath);
            _fsw.Created += (_, e) => EnqueueFileChange(e.FullPath);
            _fsw.Error += (_, e) =>
            {
                Util.SafeExecute(_fsw.Dispose);
                StartFileSystemWatcher();
            };
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
            UpdateFiles(filePaths.AsFiles());
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
                    var directoryName = directory.DirectoryName;

                    if (!IsActiveLanguageSubfolder(directory))
                    {
                        return;
                    }

                    if (!file.Exists())
                    {
                        return;
                    }

                    if (!file.HasExtension(".xml"))
                    {
                        return;
                    }

                    var xml = new XmlDocument();
                    try
                    {
                        var xmlContent = file.ReadAllText();
                        xml.LoadXml(xmlContent);
                    }
                    catch (Exception)
                    {
                        return;
                    }

                    var root = xml.DocumentElement;
                    if (root == null) return;

                    switch (root.Name)
                    {
                        case "LanguageData":
                            UpdateLanguageData(file, directoryName, root);
                            break;
                        case "BackstoryTranslations":
                            UpdateBackstories(root);
                            break;
                    }
                });
        }

        private static void UpdateLanguageData(FileHandle file, string directoryName, XmlElement root)
        {
            //BackstoryTranslations
            foreach (XmlNode item in root.ChildNodes)
            {
                if (item.NodeType != XmlNodeType.Element) continue;

                var defFieldName = item.Name;
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

        private static void UpdateDefinition(string defName, string fieldPathRaw, FileHandle file, string directoryName, XmlNode item)
        {
            var fieldPath = fieldPathRaw.Split('.');

            if (!_definitions.TryGetValue(new DefUniqueKey(defName, file, directoryName), out var def))
            {
                return;
            }

            UpdateDefinitionFieldByPath(def, fieldPath[0], fieldPath, 1, item, null);
        }

        private static void UpdateDefinitionFieldByPath(object obj, string fieldName, string[] fieldPath, int fieldPathOffset, XmlNode item, Attribute[] fieldAttributes)
        {
            fieldAttributes ??= Array.Empty<Attribute>();
            var objType = obj.GetType();
            var fieldByName = Util.GetTypeInstanceFieldsWithAtrtibutesByName(objType);

            if (TryUpdateInUntranslantedCollection(obj, fieldName, fieldPath, fieldPathOffset, item, fieldAttributes))
            {
                return;
            }

            if (obj is ICollection<ThoughtStage> stages)
            {
                var untranslatedStageName = fieldName.Replace("_", " ");
                foreach (var stage in stages)
                {
                    if (stage.untranslatedLabel == untranslatedStageName)
                    {
                        UpdateDefinitionFieldByPath(stage, fieldPath[fieldPathOffset], fieldPath, fieldPathOffset + 1, item, fieldAttributes);
                        return;
                    }
                }
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
                if (fieldInfo.FieldType != typeof(string))
                {
                    return;
                }

                var oldValue = (string)fieldInfo.GetValue(obj);
                var newValue = GetNewValue(item);

                if (newValue == null)
                {
                    // value is TODO = skip
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

                var onlyFieldsByName = Util.GetTypeInstanceFieldsByName(objType);
                fieldInfo.SetValue(obj, newValue);
                TryClearCachedValue(obj, onlyFieldsByName, fieldName, item);
                TryClearCachedValue(obj, onlyFieldsByName, fieldName + "Cap", item);
            }
        }

        private static bool TryUpdateInUntranslantedCollection(object obj, string fieldName, string[] fieldPath, int fieldPathOffset, XmlNode item, Attribute[] fieldAttributes)
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
                        // value is TODO = skip
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

            string PathSoFar()
            {
                return $"{string.Join("/", fieldPath.Take(fieldPathOffset + 1).ToArray())} ({item.Name})";
            }
        }

        private static void UpdateKeyed(FileHandle file, XmlNode item)
        {
            var keyedName = item.Name;
            if (!_keyed.TryGetValue(new KeyedUniqueKey(keyedName, file.FilePathLower), out var keyed)) return;

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

        private static void TryClearCachedValue(object obj, IDictionary<string, FieldInfo> fieldByName, string fieldName, XmlNode item)
        {
            var cachedFieldName = "cached" + fieldName.CapitalizeFirst();
            if (fieldByName.TryGetValue(cachedFieldName, out var cachedField))
            {
                cachedField.SetValue(obj, null);
            }
        }

        private static void UpdateBackstories(XmlElement root)
        {
            foreach (XmlNode item in root.ChildNodes)
            {
                if (item.NodeType != XmlNodeType.Element) continue;
                var backstoryName = item.Name;
                if (!_backstories.TryGetValue(backstoryName, out var backstory))
                {
                    continue;
                }

                var fields = Util.GetTypeInstanceFieldsByName(backstory.GetType());
                foreach (XmlNode fieldNode in item.ChildNodes)
                {
                    if (fieldNode.NodeType != XmlNodeType.Element) continue;

                    var rawFieldName = fieldNode.Name;
                    var fieldName = rawFieldName == "desc" ? "baseDesc" : rawFieldName;
                    if (!fields.TryGetValue(fieldName, out var fieldInfo))
                    {
                        continue;
                    }

                    if (fieldInfo.FieldType != typeof(string))
                    {
                        continue;
                    }

                    var oldValue = (string)fieldInfo.GetValue(backstory);
                    var newValue = GetNewValue(fieldNode);

                    if (newValue == null)
                    {
                        // is TODO = skip
                        continue;
                    }

                    if (oldValue == newValue) continue;
                    if (string.IsNullOrEmpty(oldValue) && string.IsNullOrEmpty(newValue)) continue;

                    fieldInfo.SetValue(backstory, newValue);

                    if (fields.TryGetValue($"{rawFieldName}Translanted", out var translantedFieldInfo))
                    {
                        translantedFieldInfo.SetValue(backstory, true);
                    }

                }
            }
        }

        private static string GetNewValue(XmlNode item)
        {
            var value = item.InnerXml;
            var unescapedValue = value.Replace("\\n", "\n"); // only escape character used
            return unescapedValue == "TODO" ? null : unescapedValue;
        }

        private static bool IsActiveLanguageSubfolder(DirectoryHandle directoryHandle)
        {
            foreach (var directory in _languageDirectories)
            {
                if (directoryHandle.StartsWith(directory)) return true;
            }

            return false;
        }
    }
}
