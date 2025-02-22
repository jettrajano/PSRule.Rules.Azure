// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace PSRule.Rules.Azure.Data.Template
{
    internal static class ExpressionHelpers
    {
        private const string TRUE = "True";
        private const string FALSE = "False";

        private static readonly CultureInfo AzureCulture = new CultureInfo("en-US");

        internal static bool TryString(object o, out string value)
        {
            if (o is string s)
            {
                value = s;
                return true;
            }
            else if (o is JToken token && token.Type == JTokenType.String)
            {
                value = token.Value<string>();
                return true;
            }
            else if (o is IMock mock)
            {
                value = mock.ToString();
                return true;
            }
            value = null;
            return false;
        }

        internal static bool TryConvertString(object o, out string value)
        {
            if (TryString(o, out value))
                return true;

            if (TryLong(o, out var i))
            {
                value = i.ToString(Thread.CurrentThread.CurrentCulture);
                return true;
            }
            return false;
        }

        internal static bool TryConvertStringArray(object[] o, out string[] value)
        {
            value = Array.Empty<string>();
            if (o == null || o.Length == 0 || !TryConvertString(o[0], out var s))
                return false;

            value = new string[o.Length];
            value[0] = s;
            for (var i = 1; i < o.Length; i++)
            {
                if (TryConvertString(o[i], out s))
                    value[i] = s;
            }
            return true;
        }

        internal static bool TryFindStringIndex(object o, string stringToFind, out long index, bool caseSensitive, bool first)
        {
            index = -1;
            var comparer = caseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
            var array = GetStringArray(o);
            for (var i = 0; i < array.Length; i++)
            {
                if (comparer.Equals(array[i], stringToFind))
                {
                    index = i;
                    if (first)
                        break;
                }
            }
            return index >= 0;
        }

        internal static bool TryFindLongIndex(object o, long longToFind, out long index, bool first)
        {
            index = -1;
            var array = GetLongArray(o);
            for (var i = 0; i < array.Length; i++)
            {
                if (array[i] == longToFind)
                {
                    index = i;
                    if (first)
                        break;
                }
            }
            return index >= 0;
        }

        internal static bool TryFindArrayIndex(object o, Array arrayToFind, out long index, bool first)
        {
            index = -1;
            if (!TryArray(o, out var array))
                return false;

            for (var i = 0; i < array.Length; i++)
            {
                if (TryArray(array.GetValue(i), out var arrayToSearch) &&
                    SequenceEqual(arrayToSearch, arrayToFind))
                {
                    index = i;
                    if (first)
                        break;
                }
            }
            return index >= 0;
        }

        internal static bool TryFindObjectIndex(object o, object objectToFind, out long index, bool first)
        {
            index = -1;
            if (!TryArray(o, out var array))
                return false;

            for (var i = 0; i < array.Length; i++)
            {
                if (Equal(array.GetValue(i), objectToFind))
                {
                    index = i;
                    if (first)
                        break;
                }
            }
            return index >= 0;
        }

        internal static bool SequenceEqual(Array array1, Array array2)
        {
            if (array1.Length != array2.Length)
                return false;

            for (var i = 0; i < array1.Length; i++)
            {
                if (!Equal(array1.GetValue(i), array2.GetValue(i)))
                    return false;
            }
            return true;
        }

        internal static bool Equal(object o1, object o2)
        {
            // One null
            if (o1 == null || o2 == null)
                return o1 == o2;

            // Arrays
            if (o1 is Array array1 && o2 is Array array2)
                return SequenceEqual(array1, array2);
            else if (o1 is Array || o2 is Array)
                return false;

            // String and int
            if (TryString(o1, out var s1) && TryString(o2, out var s2))
                return s1 == s2;
            else if (TryString(o1, out _) || TryString(o2, out _))
                return false;
            else if (TryLong(o1, out var i1) && TryLong(o2, out var i2))
                return i1 == i2;
            else if (TryLong(o1, out var _) || TryLong(o2, out var _))
                return false;

            // JTokens
            if (o1 is JToken t1 && o2 is JToken t2)
                return JTokenEquals(t1, t2);

            // Objects
            return ObjectEquals(o1, o2);
        }

        private static bool JTokenEquals(JToken t1, JToken t2)
        {
            return JToken.DeepEquals(t1, t2);
        }

        internal static bool ObjectEquals(object o1, object o2)
        {
            var objectType = o1.GetType();
            if (objectType != o2.GetType())
                return false;

            var props = objectType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.GetProperty);
            for (var i = 0; i < props.Length; i++)
            {
                if (!object.Equals(props[i].GetValue(o1), props[i].GetValue(o2)))
                    return false;
            }
            return true;
        }

        private static long[] GetLongArray(object o)
        {
            if (o is Array array)
            {
                var result = new long[array.Length];
                for (var i = 0; i < array.Length; i++)
                {
                    if (TryLong(array.GetValue(i), out var l))
                        result[i] = l;
                }
                return result;
            }
            else if (o is JArray jArray)
            {
                var result = new long[jArray.Count];
                for (var i = 0; i < jArray.Count; i++)
                {
                    if (TryLong(jArray[i], out var l))
                        result[i] = l;
                }
                return result;
            }
            else if (o is IEnumerable<long> elong)
            {
                return elong.ToArray();
            }
            else if (o is IEnumerable<int> eint)
            {
                return eint.Select(i => (long)i).ToArray();
            }
            else if (o is IEnumerable e)
            {
                var result = e.OfType<long>().ToArray();
                if (result.Length == 0)
                    result = e.OfType<int>().Select(i => (long)i).ToArray();

                return result;
            }
            return Array.Empty<long>();
        }

        internal static bool TryStringArray(object o, out string[] value)
        {
            value = null;
            if (o is Array array)
            {
                value = new string[array.Length];
                for (var i = 0; i < array.Length; i++)
                {
                    if (TryString(array.GetValue(i), out var s))
                        value[i] = s;
                }
            }
            else if (o is JArray jArray)
            {
                value = new string[jArray.Count];
                for (var i = 0; i < jArray.Count; i++)
                {
                    if (TryString(jArray[i], out var s))
                        value[i] = s;
                }
            }
            else if (o is IEnumerable<string> enumerable)
            {
                value = enumerable.ToArray();
            }
            else if (o is IEnumerable e)
            {
                value = e.OfType<string>().ToArray();
            }
            return value != null;
        }

        private static string[] GetStringArray(object o)
        {
            return TryStringArray(o, out var value) ? value : Array.Empty<string>();
        }

        /// <summary>
        /// Try to get an int from the existing object.
        /// </summary>
        internal static bool TryLong(object o, out long value)
        {
            if (o is int i)
            {
                value = i;
                return true;
            }
            else if (o is long l)
            {
                value = l;
                return true;
            }
            else if (o is JToken token && token.Type == JTokenType.Integer)
            {
                value = token.Value<long>();
                return true;
            }
            else if (o is MockInteger mock)
            {
                value = mock.Value;
                return true;
            }
            value = default(long);
            return false;
        }

        /// <summary>
        /// Try to get an int from the existing type and allow conversion from string.
        /// </summary>
        internal static bool TryConvertLong(object o, out long value)
        {
            if (TryLong(o, out value))
                return true;

            if (TryString(o, out var svalue) && long.TryParse(svalue, out value))
                return true;

            value = default(long);
            return false;
        }

        /// <summary>
        /// Try to get an int from the existing object.
        /// </summary>
        internal static bool TryInt(object o, out int value)
        {
            if (o is int i)
            {
                value = i;
                return true;
            }
            if (o is long l)
            {
                value = (int)l;
                return true;
            }
            else if (o is JToken token && token.Type == JTokenType.Integer)
            {
                value = token.Value<int>();
                return true;
            }
            else if (o is MockInteger mock)
            {
                value = (int)mock.Value;
                return true;
            }
            value = default(int);
            return false;
        }

        /// <summary>
        /// Try to get an int from the existing type and allow conversion from string.
        /// </summary>
        internal static bool TryConvertInt(object o, out int value)
        {
            if (TryInt(o, out value))
                return true;

            if (TryLong(o, out var l))
            {
                value = (int)l;
                return true;
            }

            if (TryString(o, out var s) && int.TryParse(s, out value))
                return true;

            value = default(int);
            return false;
        }

        /// <summary>
        /// Try to get an bool from the existing object.
        /// </summary>
        internal static bool TryBool(object o, out bool value)
        {
            if (o is bool b)
            {
                value = b;
                return true;
            }
            else if (o is JToken token && token.Type == JTokenType.Boolean)
            {
                value = token.Value<bool>();
                return true;
            }
            else if (o is MockBool mock)
            {
                value = mock.Value;
                return true;
            }
            value = default(bool);
            return false;
        }

        /// <summary>
        /// Try to get an bool from the existing type and allow conversion from string or int.
        /// </summary>
        internal static bool TryConvertBool(object o, out bool value)
        {
            if (TryBool(o, out value))
                return true;

            if (TryLong(o, out var ivalue))
            {
                value = ivalue > 0;
                return true;
            }

            return TryString(o, out var svalue) && bool.TryParse(svalue, out value);
        }

        internal static bool TryArray<T>(object o, out T value) where T : class
        {
            value = default(T);
            if (o is JArray jArray)
            {
                value = jArray as T;
                return true;
            }
            else if (o is Array array)
            {
                value = array as T;
                return true;
            }
            else if (o is MockArray mock)
            {
                value = mock as T;
                return true;
            }
            return false;
        }

        internal static bool TryArray(object o, out Array value)
        {
            value = null;
            if (o is Array array)
            {
                value = array;
                return true;
            }
            else if (o is JArray jArray)
            {
                var jr = new JToken[jArray.Count];
                jArray.CopyTo(jr, 0);
                value = jr;
                return true;
            }
            else if (o is IEnumerable<string>)
            {
                value = GetStringArray(o);
                return true;
            }
            else if (o is IEnumerable<long> || o is IEnumerable<int>)
            {
                value = GetLongArray(o);
                return true;
            }
            return false;
        }

        internal static bool IsArray(object o)
        {
            return o is JArray || o is Array || o is MockArray;
        }

        internal static object UnionArray(object[] o)
        {
            if (o == null || o.Length == 0)
                return Array.Empty<object>();

            var result = new List<object>();
            for (var i = 0; i < o.Length; i++)
            {
                if (!IsArray(o[i]))
                    continue;

                if (o[i] is JArray jArray && jArray.Count > 0)
                {
                    for (var j = 0; j < jArray.Count; j++)
                    {
                        var element = jArray[j];
                        if (!result.Contains(element))
                            result.Add(element);
                    }
                }
                else if (o[i] is MockArray mock && mock.Value != null && mock.Value.Count > 0)
                {
                    for (var j = 0; j < mock.Value.Count; j++)
                    {
                        var element = mock.Value[j];
                        if (!result.Contains(element))
                            result.Add(element);
                    }
                }
                else if (o[i] is Array array && array.Length > 0)
                {
                    for (var j = 0; j < array.Length; j++)
                    {
                        var element = array.GetValue(j);
                        if (!result.Contains(element))
                            result.Add(element);
                    }
                }
            }
            return result.ToArray();
        }

        internal static bool IsObject(object o)
        {
            return o is JObject || o is IDictionary || o is IDictionary<string, string> || o is Dictionary<string, object>;
        }

        internal static bool TryJObject(object o, out JObject value)
        {
            value = null;
            if (o is JObject jObject)
            {
                value = jObject;
                return true;
            }
            else if (o is IDictionary<string, string> dss)
            {
                value = new JObject();
                foreach (var kv in dss)
                {
                    if (!value.ContainsKey(kv.Key))
                        value.Add(kv.Key, JToken.FromObject(kv.Value));
                }
                return true;
            }
            else if (o is IDictionary<string, object> dso)
            {
                value = new JObject();
                foreach (var kv in dso)
                {
                    if (!value.ContainsKey(kv.Key))
                        value.Add(kv.Key, JToken.FromObject(kv.Value));
                }
                return true;
            }
            else if (o is IDictionary d)
            {
                value = new JObject();
                foreach (DictionaryEntry kv in d)
                {
                    var key = kv.Key.ToString();
                    if (!value.ContainsKey(key))
                        value.Add(key, JToken.FromObject(kv.Value));
                }
                return true;
            }
            return false;
        }

        internal static object UnionObject(object[] o)
        {
            var result = new JObject();
            if (o == null || o.Length == 0)
                return result;

            for (var i = 0; i < o.Length; i++)
            {
                if (o[i] is JObject jObject)
                {
                    foreach (var property in jObject.Properties())
                    {
                        if (!result.ContainsKey(property.Name))
                            result.Add(property.Name, property.Value);
                    }
                }
                else if (o[i] is IDictionary<string, string> dss)
                {
                    foreach (var kv in dss)
                    {
                        if (!result.ContainsKey(kv.Key))
                            result.Add(kv.Key, JToken.FromObject(kv.Value));
                    }
                }
                else if (o[i] is IDictionary<string, object> dso)
                {
                    foreach (var kv in dso)
                    {
                        if (!result.ContainsKey(kv.Key))
                            result.Add(kv.Key, JToken.FromObject(kv.Value));
                    }
                }
                else if (o[i] is IDictionary d)
                {
                    foreach (DictionaryEntry kv in d)
                    {
                        var key = kv.Key.ToString();
                        if (!result.ContainsKey(key))
                            result.Add(key, JToken.FromObject(kv.Value));
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Try to get DateTime from the existing object.
        /// </summary>
        internal static bool TryDateTime(object o, out DateTime value)
        {
            if (o is DateTime dvalue)
            {
                value = dvalue;
                return true;
            }
            else if (o is JToken token && token.Type == JTokenType.Date)
            {
                value = token.Value<DateTime>();
                return true;
            }
            value = default;
            return false;
        }

        /// <summary>
        /// Try to get DateTime from the existing type and allow conversion from string.
        /// </summary>
        internal static bool TryConvertDateTime(object o, out DateTime value, DateTimeStyles style = DateTimeStyles.AdjustToUniversal)
        {
            if (TryDateTime(o, out value))
                return true;

            return TryString(o, out var svalue) &&
                (DateTime.TryParseExact(svalue, "yyyyMMddTHHmmssZ", AzureCulture, style, out value) ||
                DateTime.TryParse(svalue, AzureCulture, style, out value));
        }

        internal static bool TryJToken(object o, out JToken value)
        {
            value = default;
            if (o is JToken token)
            {
                value = token;
                return true;
            }
            else if (o is string s)
            {
                value = new JValue(s);
                return true;
            }
            else if (TryLong(o, out var l))
            {
                value = new JValue(l);
                return true;
            }
            else if (o is Array a)
            {
                value = new JArray(a);
                return true;
            }
            else if (o is Hashtable hashtable)
            {
                value = JObject.FromObject(hashtable);
                return true;
            }
            return false;
        }

        internal static JToken GetJToken(object o)
        {
            if (o is JToken token)
                return token;

            if (o is bool b)
                return new JValue(b);

            if (o is long l)
                return new JValue(l);

            if (o is int i)
                return new JValue(i);

            if (o is string s)
                return new JValue(s);

            if (o is Array array)
                return new JArray(array);

            if (o is Hashtable hashtable)
                return JObject.FromObject(hashtable);

            if (o is IMock mock && mock.TryGetToken(out token))
                return token;

            if (o is MockMember mockMember)
                return new JValue(mockMember.ToString());

            return new JValue(o);
        }

        internal static byte[] GetUnique(object[] args)
        {
            // Not actual hash algorithm used in Azure
            using (var algorithm = SHA256.Create())
            {
                var url_uid = new Guid("6ba7b811-9dad-11d1-80b4-00c04fd430c8").ToByteArray();
                algorithm.TransformBlock(url_uid, 0, url_uid.Length, null, 0);

                for (var i = 0; i < args.Length; i++)
                {
                    if (TryString(args[i], out var svalue))
                    {
                        var bvalue = Encoding.UTF8.GetBytes(svalue);
                        if (i == args.Length - 1)
                            algorithm.TransformFinalBlock(bvalue, 0, bvalue.Length);
                        else
                            algorithm.TransformBlock(bvalue, 0, bvalue.Length, null, 0);
                    }
                    else
                    {
                        throw new ArgumentException();
                    }
                }
                return algorithm.Hash;
            }
        }

        internal static string GetUniqueString(object[] args)
        {
            var hash = GetUnique(args);
            var builder = new StringBuilder();
            for (var i = 0; i < hash.Length && i < 7; i++)
                builder.Append(hash[i].ToString("x2", AzureCulture));

            return builder.ToString();
        }

        internal static bool TryBoolString(object o, out string value)
        {
            value = null;
            if (o is bool bValue)
            {
                value = GetBoolString(bValue);
                return true;
            }
            if (o is JValue jValue && jValue.Type == JTokenType.Boolean)
            {
                value = GetBoolString(jValue.Value<bool>());
                return true;
            }
            return false;
        }

        private static string GetBoolString(bool value)
        {
            return value ? TRUE : FALSE;
        }
    }
}
