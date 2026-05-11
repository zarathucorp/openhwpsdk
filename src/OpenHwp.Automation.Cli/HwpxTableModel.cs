using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace OpenHwp.Automation.Cli
{
    internal static class HwpxTableModel
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";

        public static TableGrid BuildGrid(XElement table)
        {
            var grid = new TableGrid();
            if (table == null)
            {
                return grid;
            }

            var occupied = new Dictionary<string, TableGridCell>();
            var rows = table.Elements(Hp + "tr").ToList();
            for (var rowOrder = 0; rowOrder < rows.Count; rowOrder++)
            {
                var row = rows[rowOrder];
                var cells = row.Elements(Hp + "tc").ToList();
                for (var cellOrder = 0; cellOrder < cells.Count; cellOrder++)
                {
                    var cell = cells[cellOrder];
                    var address = cell.Element(Hp + "cellAddr");
                    var span = cell.Element(Hp + "cellSpan");
                    var rowAddress = address == null ? rowOrder : GetInt(address, "rowAddr", rowOrder);
                    var columnAddress = address == null ? cellOrder : GetInt(address, "colAddr", cellOrder);
                    var rowSpan = span == null ? 1 : GetInt(span, "rowSpan", 1);
                    var columnSpan = span == null ? 1 : GetInt(span, "colSpan", 1);
                    var gridCell = new TableGridCell(
                        cell,
                        rowOrder,
                        cellOrder,
                        rowAddress,
                        columnAddress,
                        rowSpan,
                        columnSpan);

                    grid.Cells.Add(gridCell);
                    grid.RowCount = System.Math.Max(grid.RowCount, rowAddress + System.Math.Max(1, rowSpan));
                    grid.ColumnCount = System.Math.Max(grid.ColumnCount, columnAddress + System.Math.Max(1, columnSpan));

                    for (var rowOffset = 0; rowOffset < gridCell.RowSpan; rowOffset++)
                    {
                        for (var columnOffset = 0; columnOffset < gridCell.ColumnSpan; columnOffset++)
                        {
                            var key = GridKey(gridCell.Row + rowOffset, gridCell.Column + columnOffset);
                            TableGridCell existing;
                            if (occupied.TryGetValue(key, out existing))
                            {
                                grid.OverlapIssues.Add(string.Format(
                                    CultureInfo.InvariantCulture,
                                    "grid r{0}c{1} is covered by both r{2}c{3} span {4}x{5} and r{6}c{7} span {8}x{9}",
                                    gridCell.Row + rowOffset,
                                    gridCell.Column + columnOffset,
                                    existing.Row,
                                    existing.Column,
                                    existing.RowSpan,
                                    existing.ColumnSpan,
                                    gridCell.Row,
                                    gridCell.Column,
                                    gridCell.RowSpan,
                                    gridCell.ColumnSpan));
                                continue;
                            }

                            occupied[key] = gridCell;
                        }
                    }
                }
            }

            return grid;
        }

        public static IEnumerable<XElement> DirectCells(XElement table)
        {
            return table == null
                ? Enumerable.Empty<XElement>()
                : table.Elements(Hp + "tr").SelectMany(row => row.Elements(Hp + "tc"));
        }

        public static XElement FindDirectCellByAddress(XElement table, int rowAddress, int columnAddress)
        {
            return DirectCells(table).FirstOrDefault(cell =>
            {
                var addr = cell.Element(Hp + "cellAddr");
                return addr != null &&
                       GetInt(addr, "rowAddr", -1) == rowAddress &&
                       GetInt(addr, "colAddr", -1) == columnAddress;
            });
        }

        public static bool HasMergedCells(XElement table)
        {
            return DirectCells(table).Any(cell =>
            {
                var span = cell.Element(Hp + "cellSpan");
                return span != null &&
                       (GetInt(span, "rowSpan", 1) > 1 || GetInt(span, "colSpan", 1) > 1);
            });
        }

        public static IEnumerable<XElement> GetDirectCellParagraphs(XElement cell)
        {
            if (cell == null)
            {
                return Enumerable.Empty<XElement>();
            }

            var subList = cell.Element(Hp + "subList");
            return subList == null ? cell.Elements(Hp + "p") : subList.Elements(Hp + "p");
        }

        public static string DirectTextOfCell(XElement cell)
        {
            var builder = new StringBuilder();
            foreach (var paragraph in GetDirectCellParagraphs(cell))
            {
                AppendDirectParagraphText(builder, paragraph);
            }

            return builder.ToString();
        }

        public static string NestedTableTextOfCell(XElement cell)
        {
            if (cell == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder();
            foreach (var nestedTable in cell.Descendants(Hp + "tbl"))
            {
                foreach (var textNode in nestedTable.Descendants(Hp + "t"))
                {
                    builder.Append(textNode.Value);
                }
            }

            return builder.ToString();
        }

        public static IList<XElement> DirectTextNodesOfParagraph(XElement paragraph)
        {
            return paragraph == null
                ? new List<XElement>()
                : paragraph.Elements(Hp + "run")
                    .Descendants(Hp + "t")
                    .Where(textNode => !HasNestedTableBetween(paragraph, textNode))
                    .ToList();
        }

        public static string TextOfDirectParagraph(XElement paragraph)
        {
            var builder = new StringBuilder();
            AppendDirectParagraphText(builder, paragraph);
            return builder.ToString();
        }

        private static void AppendDirectParagraphText(StringBuilder builder, XElement paragraph)
        {
            if (builder == null || paragraph == null)
            {
                return;
            }

            foreach (var textNode in DirectTextNodesOfParagraph(paragraph))
            {
                builder.Append(textNode.Value);
            }
        }

        private static bool HasNestedTableBetween(XElement paragraph, XElement node)
        {
            var current = node.Parent;
            while (current != null && !object.ReferenceEquals(current, paragraph))
            {
                if (current.Name == Hp + "tbl")
                {
                    return true;
                }

                current = current.Parent;
            }

            return false;
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

        private static string GridKey(int row, int column)
        {
            return row.ToString(CultureInfo.InvariantCulture) + ":" + column.ToString(CultureInfo.InvariantCulture);
        }
    }

    internal sealed class TableGrid
    {
        public int RowCount { get; set; }

        public int ColumnCount { get; set; }

        public IList<TableGridCell> Cells { get; private set; }

        public IList<string> OverlapIssues { get; private set; }

        public TableGrid()
        {
            Cells = new List<TableGridCell>();
            OverlapIssues = new List<string>();
        }
    }

    internal sealed class TableGridCell
    {
        public TableGridCell(XElement element, int rowOrder, int cellOrder, int row, int column, int rowSpan, int columnSpan)
        {
            Element = element;
            RowOrder = rowOrder;
            CellOrder = cellOrder;
            Row = row;
            Column = column;
            RowSpan = System.Math.Max(1, rowSpan);
            ColumnSpan = System.Math.Max(1, columnSpan);
        }

        public XElement Element { get; private set; }

        public int RowOrder { get; private set; }

        public int CellOrder { get; private set; }

        public int Row { get; private set; }

        public int Column { get; private set; }

        public int RowSpan { get; private set; }

        public int ColumnSpan { get; private set; }

        public bool Merged
        {
            get { return RowSpan > 1 || ColumnSpan > 1; }
        }
    }
}
