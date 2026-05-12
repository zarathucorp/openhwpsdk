using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace OpenHwp.Automation.Cli
{
    internal static class HwpxSectionPartResolver
    {
        public static IList<string> GetBodySectionPartPaths(IDictionary<string, byte[]> entries)
        {
            var result = new List<string>();
            if (entries == null)
            {
                return result;
            }

            var contentPackagePath = FindContentPackagePath(entries);
            if (!string.IsNullOrWhiteSpace(contentPackagePath) && entries.ContainsKey(contentPackagePath))
            {
                try
                {
                    var package = XDocument.Parse(Encoding.UTF8.GetString(entries[contentPackagePath]), LoadOptions.PreserveWhitespace);
                    foreach (var partPath in GetSpinePartPaths(entries, contentPackagePath, package))
                    {
                        if (IsBodySectionPart(partPath) && entries.ContainsKey(partPath) && !ContainsPath(result, partPath))
                        {
                            result.Add(partPath);
                        }
                    }
                }
                catch
                {
                    // Fall back to package entry order if the content package is not parseable.
                }
            }

            foreach (var partPath in entries.Keys
                .Where(IsBodySectionPart)
                .OrderBy(GetSectionNumber)
                .ThenBy(path => NormalizePackagePath(path), StringComparer.OrdinalIgnoreCase))
            {
                if (!ContainsPath(result, partPath))
                {
                    result.Add(partPath);
                }
            }

            return result;
        }

        public static bool IsBodySectionPart(string path)
        {
            var normalized = NormalizePackagePath(path);
            var fileName = Path.GetFileName(normalized);
            return normalized.StartsWith("Contents/", StringComparison.OrdinalIgnoreCase) &&
                   fileName.StartsWith("section", StringComparison.OrdinalIgnoreCase) &&
                   fileName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);
        }

        public static long NextBodyObjectId(IDictionary<string, byte[]> entries)
        {
            var max = 0L;
            foreach (var path in GetBodySectionPartPaths(entries))
            {
                if (entries == null || !entries.ContainsKey(path))
                {
                    continue;
                }

                var document = XDocument.Parse(Encoding.UTF8.GetString(entries[path]), LoadOptions.PreserveWhitespace);
                foreach (var attribute in document.Descendants().Attributes("id"))
                {
                    long value;
                    if (long.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > max)
                    {
                        max = value;
                    }
                }
            }

            return Math.Max(1, max + 1);
        }

        private static IEnumerable<string> GetSpinePartPaths(IDictionary<string, byte[]> entries, string contentPackagePath, XDocument document)
        {
            var manifestById = GetHpfManifestItems(document)
                .GroupBy(item => GetString(item, "id"), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            foreach (var itemref in document.Descendants().Where(element => element.Name.LocalName == "itemref"))
            {
                var idref = GetString(itemref, "idref");
                if (!manifestById.ContainsKey(idref))
                {
                    continue;
                }

                var href = GetString(manifestById[idref], "href");
                if (!string.IsNullOrWhiteSpace(href))
                {
                    yield return ResolvePackageHref(contentPackagePath, href, entries);
                }
            }
        }

        private static IList<XElement> GetHpfManifestItems(XDocument document)
        {
            var manifest = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "manifest");
            return manifest == null
                ? new List<XElement>()
                : manifest.Elements().Where(element => element.Name.LocalName == "item").ToList();
        }

        private static string FindContentPackagePath(IDictionary<string, byte[]> entries)
        {
            if (entries.ContainsKey("Contents/content.hpf"))
            {
                return "Contents/content.hpf";
            }

            if (entries.ContainsKey("content.hpf"))
            {
                return "content.hpf";
            }

            return entries.Keys.FirstOrDefault(path => path.EndsWith(".hpf", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        }

        private static string ResolvePackageHref(string contentPackagePath, string href, IDictionary<string, byte[]> entries)
        {
            if (string.IsNullOrWhiteSpace(href))
            {
                return string.Empty;
            }

            var normalized = NormalizePackagePath(href).TrimStart('/');
            if (entries.ContainsKey(normalized))
            {
                return normalized;
            }

            var baseDirectory = PackageDirectoryName(contentPackagePath);
            var combined = string.IsNullOrWhiteSpace(baseDirectory) ? normalized : baseDirectory + "/" + normalized;
            return NormalizePackagePath(combined);
        }

        private static string PackageDirectoryName(string path)
        {
            var normalized = NormalizePackagePath(path);
            var index = normalized.LastIndexOf('/');
            return index < 0 ? string.Empty : normalized.Substring(0, index);
        }

        private static int GetSectionNumber(string path)
        {
            var fileName = Path.GetFileNameWithoutExtension(NormalizePackagePath(path)) ?? string.Empty;
            var suffix = fileName.StartsWith("section", StringComparison.OrdinalIgnoreCase)
                ? fileName.Substring("section".Length)
                : string.Empty;
            int value;
            return int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
                ? value
                : int.MaxValue;
        }

        private static bool ContainsPath(IEnumerable<string> paths, string path)
        {
            return paths.Any(item => string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizePackagePath(string path)
        {
            var parts = new List<string>();
            foreach (var part in (path ?? string.Empty).Replace('\\', '/').Split('/'))
            {
                if (string.IsNullOrWhiteSpace(part) || part == ".")
                {
                    continue;
                }

                if (part == "..")
                {
                    if (parts.Count > 0)
                    {
                        parts.RemoveAt(parts.Count - 1);
                    }

                    continue;
                }

                parts.Add(part);
            }

            return string.Join("/", parts.ToArray());
        }

        private static string GetString(XElement element, string attributeName)
        {
            var attribute = element == null ? null : element.Attribute(attributeName);
            return attribute == null ? string.Empty : attribute.Value;
        }
    }
}
