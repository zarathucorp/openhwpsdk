using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace OpenHwp.Automation.Cli
{
    internal static class HwpxTextLayoutHelper
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";

        public static void ExpandRowHeightForText(XElement cell, string text)
        {
            if (cell == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var cellSize = cell.Element(Hp + "cellSz");
            if (cellSize == null)
            {
                return;
            }

            var currentHeight = GetInt(cellSize, "height", 0);
            var effectiveText = TextOf(cell);
            if (string.IsNullOrWhiteSpace(effectiveText))
            {
                effectiveText = text;
            }

            var requiredHeight = EstimateRequiredHeight(cell, effectiveText);
            if (requiredHeight <= currentHeight)
            {
                return;
            }

            var row = cell.Parent;
            if (row == null)
            {
                SetCellHeight(cell, requiredHeight);
                return;
            }

            foreach (var rowCell in row.Elements(Hp + "tc"))
            {
                SetCellHeight(rowCell, Math.Max(GetCellHeight(rowCell), requiredHeight));
            }
        }

        public static IEnumerable<string> FindPotentialOverflows(XDocument document, string partPath)
        {
            foreach (var cell in document.Descendants(Hp + "tc"))
            {
                var text = TextOf(cell);
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                var currentHeight = GetCellHeight(cell);
                var requiredHeight = EstimateRequiredHeight(cell, text);
                if (currentHeight > 0 && requiredHeight > currentHeight + 2000)
                {
                    yield return string.Format(
                        CultureInfo.InvariantCulture,
                        "{0} tableCell row={1} col={2} height={3} estimated={4} text={5}",
                        partPath,
                        CellAddress(cell, "rowAddr"),
                        CellAddress(cell, "colAddr"),
                        currentHeight,
                        requiredHeight,
                        Abbreviate(text.Trim(), 80));
                }
            }
        }

        private static int EstimateRequiredHeight(XElement cell, string text)
        {
            var cellSize = cell.Element(Hp + "cellSz");
            if (cellSize == null)
            {
                return 0;
            }

            var width = GetInt(cellSize, "width", 0);
            var currentHeight = GetInt(cellSize, "height", 0);
            if (width <= 0)
            {
                return currentHeight;
            }

            var charsPerLine = Math.Max(8, width / 850);
            var lineCount = 0;
            foreach (var line in (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var length = Math.Max(1, line.Trim().Length);
                lineCount += Math.Max(1, (length + charsPerLine - 1) / charsPerLine);
            }

            if (lineCount <= 1)
            {
                return currentHeight;
            }

            var requiredHeight = 700 + (lineCount * 900);
            return Math.Min(Math.Max(currentHeight, requiredHeight), 200000);
        }

        private static int GetCellHeight(XElement cell)
        {
            var cellSize = cell.Element(Hp + "cellSz");
            return cellSize == null ? 0 : GetInt(cellSize, "height", 0);
        }

        private static void SetCellHeight(XElement cell, int height)
        {
            var cellSize = cell.Element(Hp + "cellSz");
            if (cellSize != null)
            {
                cellSize.SetAttributeValue("height", height.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static string CellAddress(XElement cell, string attributeName)
        {
            var cellAddress = cell.Element(Hp + "cellAddr");
            return cellAddress == null ? "?" : GetInt(cellAddress, attributeName, -1).ToString(CultureInfo.InvariantCulture);
        }

        private static string TextOf(XElement element)
        {
            var builder = new StringBuilder();
            foreach (var textNode in element.Descendants(Hp + "t"))
            {
                builder.Append(textNode.Value);
            }

            return builder.ToString();
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

        private static string Abbreviate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value ?? string.Empty;
            }

            return value.Substring(0, maxLength - 3) + "...";
        }
    }
}
