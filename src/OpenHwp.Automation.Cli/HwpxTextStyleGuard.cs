using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace OpenHwp.Automation.Cli
{
    internal sealed class HwpxTextStyleGuard
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";
        private static readonly XNamespace Hh = "http://www.hancom.co.kr/hwpml/2011/head";
        private const int MinimumSafeHeight = 700;
        private const int PreferredMinimumHeight = 850;
        private const int PreferredMaximumHeight = 1200;
        private readonly IDictionary<string, int> _charHeights;

        private HwpxTextStyleGuard(IDictionary<string, int> charHeights, string fallbackCharPrId)
        {
            _charHeights = charHeights;
            FallbackCharPrId = fallbackCharPrId ?? string.Empty;
        }

        public string FallbackCharPrId { get; private set; }

        public static HwpxTextStyleGuard Create(IDictionary<string, byte[]> entries)
        {
            var documents = new Dictionary<string, XDocument>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries ?? new Dictionary<string, byte[]>())
            {
                if (!entry.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    documents[entry.Key] = XDocument.Parse(Encoding.UTF8.GetString(entry.Value), LoadOptions.PreserveWhitespace);
                }
                catch
                {
                    // Non-document XML parts should not block text-style fallback detection.
                }
            }

            return Create(documents);
        }

        public static HwpxTextStyleGuard Create(IDictionary<string, XDocument> xmlDocuments)
        {
            var charHeights = ReadCharHeights(xmlDocuments);
            if (charHeights.Count == 0)
            {
                return new HwpxTextStyleGuard(charHeights, string.Empty);
            }

            var usage = CountRunCharPrUsage(xmlDocuments);
            var fallback = usage
                .Where(item => IsPreferredHeight(charHeights, item.Key))
                .OrderByDescending(item => item.Value)
                .ThenBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => item.Key)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(fallback))
            {
                fallback = charHeights
                    .Where(item => item.Value >= MinimumSafeHeight)
                    .OrderBy(item => Math.Abs(item.Value - 1000))
                    .ThenBy(item => item.Key, StringComparer.Ordinal)
                    .Select(item => item.Key)
                    .FirstOrDefault();
            }

            return new HwpxTextStyleGuard(charHeights, fallback ?? string.Empty);
        }

        public int? NormalizeRequestedCharPrId(int? charPrId)
        {
            if (!charPrId.HasValue)
            {
                return null;
            }

            if (_charHeights.Count == 0)
            {
                return charPrId.Value;
            }

            var requested = charPrId.Value.ToString(CultureInfo.InvariantCulture);
            int height;
            if (!_charHeights.TryGetValue(requested, out height) || height >= MinimumSafeHeight)
            {
                return charPrId.Value;
            }

            int fallback;
            if (!string.IsNullOrWhiteSpace(FallbackCharPrId) &&
                int.TryParse(FallbackCharPrId, NumberStyles.Integer, CultureInfo.InvariantCulture, out fallback))
            {
                return fallback;
            }

            return null;
        }

        public int EnsureMinimumCharHeight(XElement scope)
        {
            if (scope == null || string.IsNullOrWhiteSpace(FallbackCharPrId) || _charHeights.Count == 0)
            {
                return 0;
            }

            var changed = 0;
            foreach (var run in scope.Descendants(Hp + "run"))
            {
                var charPrId = GetString(run, "charPrIDRef");
                if (string.IsNullOrWhiteSpace(charPrId))
                {
                    continue;
                }

                int height;
                if (_charHeights.TryGetValue(charPrId, out height) && height > 0 && height < MinimumSafeHeight)
                {
                    run.SetAttributeValue("charPrIDRef", FallbackCharPrId);
                    changed++;
                }
            }

            return changed;
        }

        private static bool IsPreferredHeight(IDictionary<string, int> charHeights, string charPrId)
        {
            int height;
            return charHeights.TryGetValue(charPrId, out height) &&
                   height >= PreferredMinimumHeight &&
                   height <= PreferredMaximumHeight;
        }

        private static IDictionary<string, int> ReadCharHeights(IDictionary<string, XDocument> xmlDocuments)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var document in (xmlDocuments ?? new Dictionary<string, XDocument>()).Values)
            {
                foreach (var charPr in document.Descendants(Hh + "charPr"))
                {
                    var id = GetString(charPr, "id");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    var height = GetInt(charPr, "height", 0);
                    if (height > 0 && !result.ContainsKey(id))
                    {
                        result.Add(id, height);
                    }
                }
            }

            return result;
        }

        private static IDictionary<string, int> CountRunCharPrUsage(IDictionary<string, XDocument> xmlDocuments)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var document in (xmlDocuments ?? new Dictionary<string, XDocument>()).Values)
            {
                foreach (var run in document.Descendants(Hp + "run"))
                {
                    var id = GetString(run, "charPrIDRef");
                    if (string.IsNullOrWhiteSpace(id))
                    {
                        continue;
                    }

                    result[id] = result.ContainsKey(id) ? result[id] + 1 : 1;
                }
            }

            return result;
        }

        private static int GetInt(XElement element, string attributeName, int defaultValue)
        {
            var attribute = element.Attribute(attributeName);
            if (attribute == null)
            {
                return defaultValue;
            }

            int value;
            return int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : defaultValue;
        }

        private static string GetString(XElement element, string attributeName)
        {
            var attribute = element.Attribute(attributeName);
            return attribute == null ? string.Empty : attribute.Value;
        }
    }
}
