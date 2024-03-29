﻿using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Verse;

namespace LordFanger
{
    public static class Util
    {
        public class FieldInfoWithAttributes
        {
            public FieldInfoWithAttributes(FieldInfo fieldInfo)
            {
                FieldInfo = fieldInfo;
                Attributes = fieldInfo.GetCustomAttributes().ToArray();
            }

            public FieldInfo FieldInfo { get; }

            public Attribute[] Attributes { get; }
        }

        private class CacheFields
        {
            private readonly struct CacheEntry
            {
                public CacheEntry(FieldInfo[] fields, FieldInfoWithAttributes[] fieldsWithAttributes, IDictionary<string, FieldInfo> fieldsByName, IDictionary<string, FieldInfoWithAttributes> fieldsWithAttributesByName)
                {
                    Fields = fields;
                    FieldsWithAttributes = fieldsWithAttributes;
                    FieldsByName = fieldsByName;
                    FieldsWithAttributesByName = fieldsWithAttributesByName;
                }

                public FieldInfo[] Fields { get; }

                public FieldInfoWithAttributes[] FieldsWithAttributes { get; }

                public IDictionary<string, FieldInfo> FieldsByName { get; }

                public IDictionary<string, FieldInfoWithAttributes> FieldsWithAttributesByName { get; }
            }

            private readonly ConcurrentDictionary<Type, CacheEntry> _fields = new ConcurrentDictionary<Type, CacheEntry>();

            private readonly Func<FieldInfo, bool> _predicate;

            public CacheFields(Func<FieldInfo, bool> predicate)
            {
                _predicate = predicate;
            }

            public IDictionary<string, FieldInfo> GetFieldsByName(Type type)
            {
                var entry = GetEntry(type);
                return entry.FieldsByName;
            }

            public IDictionary<string, FieldInfoWithAttributes> GetFieldsWithAttributesByName(Type type)
            {
                var entry = GetEntry(type);
                return entry.FieldsWithAttributesByName;
            }

            public FieldInfo[] GetFields(Type type)
            {
                var entry = GetEntry(type);
                return entry.Fields;
            }

            public FieldInfoWithAttributes[] GetFieldsWithAttributes(Type type)
            {
                var entry = GetEntry(type);
                return entry.FieldsWithAttributes;
            }

            private CacheEntry GetEntry(Type type)
            {
                if (_fields.TryGetValue(type, out var entry))
                {
                    return entry;
                }
                var rawFields = new List<FieldInfo>();
                var fieldNames = new HashSet<string>(); // TODO solve new overiden fields
                var type2 = type;
                var ignoreStatic = false;
                while (type2 != null && type2 != typeof(object))
                {
                    foreach (var field in type2.GetRuntimeFields())
                    {
                        if (ignoreStatic && field.IsStatic) continue;
                        if (!_predicate(field)) continue;
                        if (fieldNames.Contains(field.Name)) continue;
                        rawFields.Add(field);
                        fieldNames.Add(field.Name);
                    }

                    ignoreStatic = true;
                    type2 = type2.BaseType;
                }

                var fieldsWithAttributes = rawFields.Select(WithAttributes).OrderBy(f => f.FieldInfo.Name).ToArray();
                var fieldsWithAttributesByName = fieldsWithAttributes.ToDictionary(f => f.FieldInfo.Name);
                var fields = fieldsWithAttributes.Select(f => f.FieldInfo).OrderBy(f => f.Name).ToArray();
                var fieldsByName = fields.ToDictionary(f => f.Name);
                entry = new CacheEntry(fields, fieldsWithAttributes, fieldsByName, fieldsWithAttributesByName);
                _fields[type] = entry;
                return entry;
            }

            private static FieldInfoWithAttributes WithAttributes(FieldInfo fieldInfo) => new FieldInfoWithAttributes(fieldInfo);
        }

        private static readonly CacheFields _instances = new CacheFields(f => !f.IsStatic);

        private static readonly CacheFields _statics = new CacheFields(f => f.IsStatic);

        private static readonly CacheFields _untranslanted = new CacheFields(f => f.GetCustomAttributes().Any(a => a is TranslationHandleAttribute));

        private static readonly ConcurrentDictionary<Type, MethodInfo[]> _cachedInstanceMethods = new ConcurrentDictionary<Type, MethodInfo[]>();

        public static IDictionary<string, FieldInfo> GetTypeInstanceFieldsByName(Type type)
        {
            var fieldsByName = _instances.GetFieldsByName(type);
            return fieldsByName;
        }

        public static IDictionary<string, FieldInfoWithAttributes> GetTypeInstanceFieldsWithAtrtibutesByName(Type type)
        {
            var fieldsByName = _instances.GetFieldsWithAttributesByName(type);
            return fieldsByName;
        }

        public static FieldInfo[] GetInstanceFields(Type type)
        {
            var fields = _instances.GetFields(type);
            return fields;
        }

        public static FieldInfo GetInstanceField(Type type, string fieldName)
        {
            var fields = _instances.GetFieldsByName(type);
            var field = fields[fieldName];
            return field;
        }

        public static TValue GetInstanceFieldValue<TValue>(this object instance, string fieldName)
        {
            var field = GetInstanceField(instance.GetType(), fieldName);
            var value = (TValue)field.GetValue(instance);
            return value;
        }

        public static void ClearInstanceField(this object instance, string fieldName)
        {
            var field = GetInstanceField(instance.GetType(), fieldName);
            field.SetValue(instance, null);
        }

        public static TValue GetStaticFieldValue<TType, TValue>(string fieldName)
        {
            var field = GetStaticField(typeof(TType), fieldName);
            var value = (TValue)field.GetValue(null);
            return value;
        }

        public static FieldInfoWithAttributes[] GetInstanceFieldsWithAttributes(Type type)
        {
            var fields = _instances.GetFieldsWithAttributes(type);
            return fields;
        }

        public static FieldInfo[] GetStaticFields(Type type)
        {
            var fields = _statics.GetFields(type);
            return fields;
        }

        public static FieldInfo GetStaticField(Type type, string fieldName)
        {
            var fields = _statics.GetFieldsByName(type);
            var field = fields[fieldName];
            return field;
        }

        public static TValue GetStaticFieldValue<TValue>(Type type, string fieldName)
        {
            var fields = _statics.GetFieldsByName(type);
            var field = fields[fieldName];
            var value = (TValue)field.GetValue(null);
            return value;
        }
        
        public static void ClearStaticField(Type type, string fieldName)
        {
            var fieldInfo = GetStaticField(type, fieldName);
            fieldInfo.SetValue(null, null);
        }

        public static FieldInfo[] GetTypeUntranslantedFields(Type type)
        {
            var fields = _untranslanted.GetFields(type);
            return fields;
        }

        public static FieldInfoWithAttributes[] GetTypeUntranslantedFieldsWithAttributes(Type type)
        {
            var fields = _untranslanted.GetFieldsWithAttributes(type);
            return fields;
        }

        private static IDictionary<string, FieldInfo> GetTypeFieldsByName(Type type, ConcurrentDictionary<Type, IDictionary<string, FieldInfo>> cachedFieldsByName)
        {
            if (cachedFieldsByName.TryGetValue(type, out var fieldsByName)) return fieldsByName;
            fieldsByName = GetInstanceFields(type)
                .ToDictionary(f => f.Name);
            cachedFieldsByName[type] = fieldsByName;
            return fieldsByName;
        }

        public static MethodInfo GetInstanceMethod(Type type, string methodName)
        {
            if (!_cachedInstanceMethods.TryGetValue(type, out var methods))
            {
                methods = type.GetRuntimeMethods().ToArray();
                _cachedInstanceMethods[type] = methods;
            }

            var method = methods.FirstOrDefault(m => m.Name == methodName);
            return method;
        }

        public static object InvokeInstanceMethod(this object instance, string method, params object[] arguments)
        {
            var type = instance.GetType();
            var result = GetInstanceMethod(type, method)?.Invoke(instance, arguments ?? Array.Empty<object>());
            return result;
        }

        public static object ElementAt(this ICollection collection, int index)
        {
            var i = 0;
            return collection.Cast<object>().FirstOrDefault(item => i++ == index);
        }

        public static bool Any<TAttribute>(this IEnumerable<Attribute> attributes)
            where TAttribute : Attribute
            => attributes?.Any(a => a is TAttribute) ?? false;

        public static void SafeExecute(this Action action)
        {
            try
            {
                action();
            }
            catch
            {
                // ignore any exception
            }
        }

        public static string ToNormalizedHandle(this string handle)
        {
            if (handle == null) return null;
            handle = handle.Replace(' ', '_');
            handle = handle.Replace('\n', '_');
            handle = handle.Replace("\r", "");
            handle = handle.Replace('\t', '_');
            handle = handle.Replace(".", "");
            handle = Regex.Replace(handle, @"\{[^\}]*\}", "");
            handle = Regex.Replace(handle, @"[^a-zA-Z0-9_]+", "");
            handle = Regex.Replace(handle, @"_+", "_");
            handle = handle.Trim('_');
            return handle;
        }

        public static string ToCachedName(this string fieldName)
        {
            var cachedFieldname = $"cached{fieldName.CapitalizeFirst()}";
            return cachedFieldname;
        }
    }
}
