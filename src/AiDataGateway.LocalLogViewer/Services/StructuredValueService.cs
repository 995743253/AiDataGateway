using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;
using AiDataGateway.LocalLogViewer.Models;

namespace AiDataGateway.LocalLogViewer.Services
{
    public sealed class StructuredValueService
    {
        private readonly JavaScriptSerializer _serializer = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024, RecursionLimit = 64 };

        public object Normalize(object value, int depth = 0)
        {
            if (value == null || depth >= 12) return value;
            var text = value as string;
            if (text != null)
            {
                var trimmed = text.Trim();
                if (trimmed.Length >= 2 && ((trimmed[0] == '{' && trimmed[trimmed.Length - 1] == '}') ||
                                            (trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')))
                {
                    try { return Normalize(_serializer.DeserializeObject(trimmed), depth + 1); }
                    catch (InvalidOperationException) { return value; }
                    catch (ArgumentException) { return value; }
                }
                return value;
            }

            var dictionary = value as IDictionary<string, object>;
            if (dictionary != null)
            {
                var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var pair in dictionary) result[pair.Key] = Normalize(pair.Value, depth + 1);
                return result;
            }

            var list = value as IEnumerable;
            if (list != null && !(value is byte[]))
            {
                var result = new List<object>();
                foreach (var item in list) result.Add(Normalize(item, depth + 1));
                return result;
            }
            return value;
        }

        public IList<StructuredNode> BuildTree(IDictionary<string, object> properties)
        {
            var result = new List<StructuredNode>();
            foreach (var pair in properties) result.Add(BuildNode(pair.Key, Normalize(pair.Value), 0));
            return result;
        }

        private StructuredNode BuildNode(string name, object value, int depth)
        {
            var node = new StructuredNode { Name = name };
            if (value == null) { node.Value = "null"; return node; }
            if (depth >= 12) { node.Value = "层级过深"; return node; }

            var dictionary = value as IDictionary<string, object>;
            if (dictionary != null)
            {
                node.Value = "对象 · " + dictionary.Count;
                foreach (var pair in dictionary) node.Children.Add(BuildNode(pair.Key, pair.Value, depth + 1));
                return node;
            }

            var list = value as IEnumerable;
            if (list != null && !(value is string) && !(value is byte[]))
            {
                var index = 0;
                foreach (var item in list) node.Children.Add(BuildNode("[" + index++ + "]", item, depth + 1));
                node.Value = "数组 · " + index;
                return node;
            }

            node.Value = Convert.ToString(value, CultureInfo.InvariantCulture);
            return node;
        }
    }
}
