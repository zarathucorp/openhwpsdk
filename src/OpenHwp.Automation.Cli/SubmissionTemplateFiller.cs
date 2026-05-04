using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using System.Xml.Linq;

namespace OpenHwp.Automation.Cli
{
    internal sealed class SubmissionTemplateFiller
    {
        private static readonly XNamespace Hp = "http://www.hancom.co.kr/hwpml/2011/paragraph";
        private static readonly Regex HtmlBreakPattern = new Regex(@"<br\s*/?>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HtmlTagPattern = new Regex("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex MultiWhitespacePattern = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly Regex MarkdownTableSeparatorPattern = new Regex(@"^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?$", RegexOptions.Compiled);

        private readonly string _markdown;
        private readonly IList<IList<IList<string>>> _tables;
        private readonly IDictionary<string, byte[]> _entries;
        private readonly XDocument _section;
        private readonly FillReport _report = new FillReport();

        private SubmissionTemplateFiller(string templatePath, string markdownPath)
        {
            _markdown = ReadTextFile(markdownPath);
            _tables = MarkdownTableParser.ParseTables(_markdown);
            _entries = SimpleZipArchive.ReadAll(Path.GetFullPath(templatePath));

            byte[] sectionBytes;
            if (!_entries.TryGetValue("Contents/section0.xml", out sectionBytes))
            {
                throw new InvalidOperationException("The HWPX template does not contain Contents/section0.xml.");
            }

            _section = XDocument.Parse(Encoding.UTF8.GetString(sectionBytes), LoadOptions.PreserveWhitespace);
        }

        public static FillReport Fill(string templatePath, string markdownPath, string outputPath, string reportPath)
        {
            var filler = new SubmissionTemplateFiller(templatePath, markdownPath);
            filler.FillKnownProfile();
            filler.Save(outputPath);
            filler.WriteReport(templatePath, markdownPath, outputPath, reportPath);
            return filler._report;
        }

        private void FillKnownProfile()
        {
            FillCoverPage();
            FillBodyTables();
            FillCompanyStatus();
            FillBudgetPlan();
            FillTrackOverview();
            FillRoadmapNarrative();
            FillAttachments();
            FillConsentForms();
        }

        private void FillCoverPage()
        {
            SetCellText(0, 0, 12, CellValue(0, 1, 1));
            SetCellText(0, 2, 20, CellValue(0, 5, 1));
            SetCellText(0, 3, 4, CellValue(0, 6, 1));
            SetCellText(0, 3, 22, CellValue(0, 7, 1));

            var nstd = SplitCodePercent(CellValue(1, 1, 1));
            SetCellText(0, 4, 4, nstd.Code);
            SetCellText(0, 4, 11, nstd.Percent);
            var department = SplitCodePercent(CellValue(1, 2, 1));
            SetCellText(0, 5, 4, department.Code);
            SetCellText(0, 5, 11, department.Percent);

            SetCellText(0, 6, 8, CellValue(2, 1, 1));
            SetCellText(0, 6, 25, CellValue(2, 1, 3));
            SetCellText(0, 7, 8, CellValue(2, 2, 1));
            SetCellText(0, 7, 25, CellValue(2, 2, 3));

            SetCellText(0, 8, 11, CellValue(3, 1, 1));
            SetCellText(0, 8, 25, CellValue(3, 2, 1));
            SetCellText(0, 9, 11, CellValue(3, 3, 1));
            SetCellText(0, 9, 25, CellValue(3, 4, 1));
            SetCellText(0, 10, 11, CellValue(3, 5, 1));

            SetCellText(0, 11, 11, CellValue(4, 1, 1));
            SetCellText(0, 11, 25, CellValue(4, 2, 1));
            SetCellText(0, 12, 11, CellValue(4, 3, 1));
            SetCellText(0, 12, 25, CellValue(4, 4, 1));
            SetCellText(0, 13, 11, CellValue(4, 5, 1));

            var period = ExtractFirstMatch(@"(?m)^\s*-\s*전체\s*:\s*(.+)$");
            SetCellText(0, 14, 10, period);
            SetCellText(0, 17, 3, CellValue(5, 1, 1));
            SetCellText(0, 17, 5, CellValue(5, 1, 2));
            SetCellText(0, 17, 9, CellValue(5, 1, 3));
            SetCellText(0, 17, 15, "160,000");
            SetCellText(0, 17, 19, CellValue(5, 1, 3));
            SetCellText(0, 17, 24, CellValue(5, 1, 5));

            SetCellText(0, 18, 11, CellValue(6, 1, 1));
            SetCellText(0, 18, 23, CellValue(6, 2, 1));
            SetCellText(0, 19, 11, CellValue(6, 3, 1));
            SetCellText(0, 19, 23, CellValue(6, 4, 1));
            SetCellText(0, 20, 11, CellValue(6, 5, 1));
            SetCellText(0, 21, 0, "관련 법령 및 규정과 모든 의무사항을 준수하면서 이 사업을 성실하게 수행하기 위하여 기술사업화 패키지 사업계획서를 제출합니다. 아울러 이 계획서에 기재된 내용이 사실임을 확인하며, 만약 사실이 아닌 경우 지원 대상 선정 취소, 협약 해약 등의 불이익도 감수하겠습니다. 2026년 5월 ___일 대표자: 김진섭 (인) 책임자: 김진섭 (인) 중소벤처기업부장관 귀하");
        }

        private void FillBodyTables()
        {
            SetCellText(2, 0, 2, CellValue(7, 1, 1));
            SetCellText(2, 0, 4, CellValue(7, 1, 3));
            SetCellText(2, 1, 2, CellValue(7, 2, 1));
            SetCellText(2, 1, 4, CellValue(7, 2, 3));
            SetCellText(2, 2, 2, CellValue(7, 3, 1));
            SetCellText(2, 2, 4, CellValue(7, 3, 3));
            SetCellText(2, 3, 2, CellValue(7, 4, 1));

            for (var row = 1; row <= 2; row++)
            {
                for (var column = 0; column <= 4; column++)
                {
                    SetCellText(3, row, column, CellValue(8, row, column));
                }
            }

            SetCellText(3, 3, 0, string.Empty);

            for (var row = 1; row <= 2; row++)
            {
                for (var column = 0; column <= 3; column++)
                {
                    SetCellText(4, row, column, CellValue(9, row, column));
                    SetCellText(5, row, column, CellValue(10, row, column));
                }
            }

            SetCellText(6, 0, 2, CellValue(11, 1, 1));
            SetCellText(6, 0, 4, CellValue(11, 1, 3));
            SetCellText(6, 1, 2, CellValue(11, 2, 1));
            SetCellText(6, 1, 4, CellValue(11, 2, 3));
            SetCellText(6, 2, 2, CellValue(11, 3, 1));
            SetCellText(6, 2, 4, CellValue(11, 3, 3));
            SetCellText(6, 3, 2, CellValue(11, 4, 1));
            SetCellText(6, 4, 2, CellValue(11, 5, 1));
            SetCellText(6, 4, 4, CellValue(11, 5, 3));
            SetCellText(6, 5, 2, CellValue(11, 6, 1));
            SetCellText(6, 5, 4, CellValue(11, 6, 3));

            for (var row = 1; row <= 2; row++)
            {
                for (var column = 0; column <= 4; column++)
                {
                    SetCellText(7, row, column, CellValue(12, row, column));
                }

                for (var column = 0; column <= 3; column++)
                {
                    SetCellText(8, row, column, CellValue(13, row, column));
                    SetCellText(9, row, column, CellValue(14, row, column));
                }
            }

            RebuildTableDataRows(10, 2, TableRowsAfterHeader(15), new Dictionary<int, CellProjection>
            {
                { 0, CellProjection.Column(0) },
                { 1, CellProjection.Column(1) },
                { 2, CellProjection.Column(2) },
                { 3, CellProjection.Column(3) },
                { 4, CellProjection.Column(5) },
                { 5, CellProjection.Column(6) },
                { 6, CellProjection.Column(7) },
                { 7, CellProjection.Column(8) },
                { 8, CellProjection.Column(4) },
                { 9, CellProjection.Func(row => "시간선택제: " + Cell(row, 9) + " / 근무구분: " + Cell(row, 10)) },
                { 10, CellProjection.Column(11) }
            }, 2);

            RebuildTableDataRows(11, 1, TableRowsAfterHeader(16), new Dictionary<int, CellProjection>
            {
                { 0, CellProjection.Column(1) },
                { 1, CellProjection.Column(0) },
                { 2, CellProjection.Column(3) },
                { 3, CellProjection.Column(2) }
            }, 1);

            RebuildTableDataRows(12, 2, TableRowsAfterHeader(17), new Dictionary<int, CellProjection>
            {
                { 0, CellProjection.Column(0) },
                { 1, CellProjection.Column(1) },
                { 2, CellProjection.Column(2) },
                { 3, CellProjection.Column(4) },
                { 4, CellProjection.Column(3) },
                { 5, CellProjection.Func(row => Cell(row, 4).Contains("수행중") ? "수행중" : (Cell(row, 4).Contains("완료") ? "완료" : string.Empty)) }
            }, 2);

            SetCellText(13, 2, 0, CellValue(18, 1, 0));
            for (var column = 0; column <= 10; column++)
            {
                SetCellText(14, 3, column, CellValue(19, 1, column));
            }

            for (var column = 0; column <= 6; column++)
            {
                SetCellText(15, 1, column, CellValue(20, 1, column));
            }
        }

        private void FillCompanyStatus()
        {
            for (var row = 1; row < Math.Min(TableRowCount(21), 11); row++)
            {
                SetCellText(16, row + 1, 4, CellValue(21, row, 2));
            }

            for (var index = 0; index < 3; index++)
            {
                SetCellText(16, 12 + index, 3, CellValue(22, index + 1, 0));
                SetCellText(16, 12 + index, 4, CellValue(22, index + 1, 1));
                SetCellText(16, 15 + index, 3, CellValue(23, index + 1, 0));
                SetCellText(16, 15 + index, 4, CellValue(23, index + 1, 1));
                SetCellText(16, 18 + index, 3, CellValue(24, index + 1, 0));
                SetCellText(16, 18 + index, 4, CellValue(24, index + 1, 1));
                SetCellText(16, 21 + index, 3, CellValue(24, index + 1, 0));
                SetCellText(16, 21 + index, 4, CellValue(24, index + 1, 2));
                SetCellText(16, 24 + index, 3, CellValue(25, index + 1, 0));
                SetCellText(16, 24 + index, 4, CellValue(25, index + 1, 1));
                SetCellText(16, 27 + index, 3, CellValue(26, index + 1, 0));
                SetCellText(16, 27 + index, 4, CellValue(26, index + 1, 1));
            }

            for (var row = 1; row < TableRowCount(27); row++)
            {
                SetCellText(16, 29 + row, 4, CellValue(27, row, 1));
            }
        }

        private void FillBudgetPlan()
        {
            SetCellText(17, 3, 2, CellValue(28, 1, 2));
            for (var column = 3; column <= 9; column++)
            {
                SetCellText(17, 3, column, CellValue(28, 1, column));
                SetCellText(17, 5, column, CellValue(28, 2, column));
                SetCellText(17, 6, column, CellValue(28, 3, column));
            }
        }

        private void FillTrackOverview()
        {
            var period = ExtractFirstMatch(@"(?m)^\s*-\s*전체\s*:\s*(.+)$");
            SetCellText(18, 0, 6, "[x] ④R&D 수행 창업기업");
            SetCellText(18, 3, 1, "AI agent 기반 임상통계 분석자동화 SaaS 사업화");
            SetCellText(18, 4, 1, period);
            SetCellText(18, 7, 1, CellValue(29, 1, 1));
            SetCellText(18, 7, 3, CellValue(29, 1, 2));
            SetCellText(18, 7, 4, CellValue(29, 1, 3));
            SetCellText(18, 7, 5, "160,000,000 원");
            SetCellText(18, 7, 7, CellValue(29, 1, 3));
            SetCellText(18, 8, 1, JoinBlockSummary(GetMarkdownBlock(@"^### 1\. 사업화 대상 기술 정의 및 주요 특성.*?$", @"(?=^### 2\. 사업화 대상 기술이 적용된 제품)") + " " + GetMarkdownBlock(@"^### 2\. 사업화 대상 기술이 적용된 제품.*?$", @"(?=^### 3\. 규제샌드박스)"), 6));
            SetCellText(18, 9, 1, JoinBlockSummary(GetMarkdownBlock(@"^### 사업화 추진 계획 \(개요\).*?$", @"(?=^### 기업의 사업화 추진 역량)"), 6));
            SetCellText(18, 10, 1, JoinBlockSummary(GetMarkdownBlock(@"^### 기업의 사업화 추진 역량 \(개요\).*?$", @"(?=^### 기대효과)"), 6));
            SetCellText(18, 11, 1, JoinBlockSummary(GetMarkdownBlock(@"^### 기대효과 \(개요\).*?$", @"(?=^## □ 사업화 로드맵 본문)"), 5));

            for (var row = 1; row <= 8; row++)
            {
                SetCellText(19, row, 2, CellValue(30, row, 1));
            }

            SetCellText(20, 1, 2, "[x] ① 창업기업이 R&D 수행");
            SetCellText(20, 2, 2, "[ ] ② R&D를 수행한 연구자가 창업");
            for (var row = 2; row <= 6; row++)
            {
                SetCellText(20, row + 1, 2, CellValue(31, row, 1));
            }
        }

        private void FillRoadmapNarrative()
        {
            SetExpectedEffectSection(@"^### 5\. 기대효과.*?$", @"\z");
            SetSectionAfterHeading("3-3. 최근 5년간 사업화, 외부 협업, 외부 자금 유치 경험", @"^#### 3-3\. 최근 5년간 사업화, 외부 협업, 외부 자금 유치 경험.*?$", @"(?=^### 5\. 기대효과)");
            SetSectionAfterHeading("3-2. 사업화 추진 기반 현황", @"^#### 3-2\. 사업화 추진 기반 현황.*?$", @"(?=^#### 3-3\.)");
            SetSectionAfterHeading("3-1. 기업 성장 전략", @"^#### 3-1\. 기업 성장 전략.*?$", @"(?=^#### 3-2\.)");
            SetSectionAfterHeading("2-5. 주요 리스크 및 대응 전략", @"^#### 2-5\. 주요 리스크 및 대응 전략.*?$", @"(?=^### 3\. 기업의 사업화 추진 역량)");
            SetSectionAfterHeading("2-4. 단계별 추진 계획", @"^#### 2-4\. 단계별 추진 계획.*?$", @"(?=^#### 2-5\.)");
            SetSectionAfterHeading("2-3. 목표 시장 현황 및 시장 진입 전략", @"^#### 2-3\. 목표 시장 현황 및 시장 진입 전략.*?$", @"(?=^#### 2-4\.)");
            SetSectionAfterHeading("2-2. 사업화 목표", @"^#### 2-2\. 사업화 목표.*?$", @"(?=^#### 2-3\.)");
            SetSectionAfterHeading("2-1. 사업화 추진 배경", @"^#### 2-1\. 사업화 추진 배경.*?$", @"(?=^#### 2-2\.)");
            SetSectionAfterHeading("1-2. 사업화 대상 기술이 적용된 제품", @"^#### 1-2\. 사업화 대상 기술이 적용된 제품.*?$", @"(?=^### 2\. 사업화 추진 계획)");
            SetSectionAfterHeading("1-1. 사업화 대상 기술 정의 및 주요 특성", @"^#### 1-1\. 사업화 대상 기술 정의 및 주요 특성.*?$", @"(?=^#### 1-2\.)");
        }

        private void FillAttachments()
        {
            SetCellText(37, 0, 1, "차라투 주식회사");
            for (var row = 1; row < TableRowCount(41); row++)
            {
                SetCellText(37, row + 1, 2, CellValue(41, row, 2));
                SetCellText(37, row + 1, 3, CellValue(41, row, 3));
            }

            SetParagraphTextByCurrent("신청인 :                          서명", "신청인 : 김진섭 서명");
            SetParagraphTextByCurrent("법인명 :                          직인", "법인명 : 차라투 주식회사 직인");
            SetParagraphTextByCurrent("신청기업명 :", "신청기업명 : 차라투 주식회사");

            for (var column = 0; column <= 3; column++)
            {
                SetCellText(39, 2, column, CellValue(42, 1, column));
            }

            var budgetMap = new Dictionary<int, int>
            {
                { 2, 1 }, { 3, 2 }, { 4, 3 }, { 5, 4 }, { 6, 5 }, { 7, 6 }, { 8, 7 }, { 9, 8 },
                { 10, 9 }, { 11, 10 }, { 12, 11 }, { 13, 12 }, { 14, 13 }, { 15, 14 }
            };

            foreach (var item in budgetMap)
            {
                var sourceRow = item.Value;
                var detail = CellValue(43, sourceRow, 3);
                if (!string.IsNullOrWhiteSpace(detail))
                {
                    SetCellText(40, item.Key, 3, CellValue(43, sourceRow, 2) + " - " + detail);
                }

                SetCellText(40, item.Key, 4, CellValue(43, sourceRow, 4));
                SetCellText(40, item.Key, 5, CellValue(43, sourceRow, 5));
                SetCellText(40, item.Key, 6, CellValue(43, sourceRow, 6));
            }

            var laborRows = TableRowsAfterHeader(44).ToList();
            laborRows.Add(new List<string> { "합계", string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, CellValue(44, 5, 6) });
            RebuildTableDataRows(41, 2, laborRows, new Dictionary<int, CellProjection>
            {
                { 0, CellProjection.Func(row => Cell(row, 0) == "합계" ? "합    계" : "인건비") },
                { 1, CellProjection.Func(row => Cell(row, 0) == "합계" ? string.Empty : Cell(row, 0)) },
                { 2, CellProjection.Column(1) },
                { 3, CellProjection.Column(2) },
                { 4, CellProjection.Column(3) },
                { 5, CellProjection.Column(4) },
                { 6, CellProjection.Column(5) },
                { 7, CellProjection.Column(6) }
            }, 2);
        }

        private void FillConsentForms()
        {
            SetCellText(43, 3, 2, "기술사업화 패키지 지원사업 (R&D 수행 창업기업 트랙)");
            SetCellText(43, 5, 2, "차라투 주식회사");
            SetCellText(43, 5, 7, "김진섭");
            SetCellText(45, 1, 1, "[x] 동의 / [ ] 비동의  (인)");
            SetCellText(45, 1, 2, "[x] 동의 / [ ] 비동의  (인)");

            SetCellText(46, 1, 1, CellValue(49, 1, 1));
            SetCellText(46, 2, 1, CellValue(49, 2, 1));
            SetCellText(46, 2, 4, CellValue(49, 3, 1));
            for (var row = 1; row < Math.Min(TableRowCount(50), 10); row++)
            {
                SetCellText(46, row + 3, 0, CellValue(50, row, 1));
                SetCellText(46, row + 3, 1, CellValue(50, row, 2));
                SetCellText(46, row + 3, 2, CellValue(50, row, 0));
                SetCellText(46, row + 3, 3, CellValue(50, row, 3));
                SetCellText(46, row + 3, 5, CellValue(50, row, 4));
            }

            SetCellText(46, 14, 0, "위의 사업수행을 위하여 제출한 기술사업화 패키지 사업계획서의 사업내용 및 수행에 동의하며, 관련 법령의 제반사항을 준수하면서 본 사업에 적극 참여하겠습니다. 2026년 5월 ___일 / 기업명: 차라투 주식회사 / 대표자: 김진섭 (직인)");

            SetCellText(47, 2, 2, "차라투 주식회사");
            SetCellText(47, 2, 14, "110111-6843878");
            SetCellText(47, 3, 2, "김진섭");
            SetCellText(47, 3, 14, "624-86-01323");
            SetCellText(47, 4, 2, "(우 05854) 서울특별시 송파구 송파대로 201, B동 1513호 (문정동, 테라타워2)");
            SetCellText(47, 5, 2, "박준홍");
            SetCellText(47, 5, 6, "010-3222-8066");
            SetCellText(47, 5, 15, "jhpark@zarathu.com");
        }

        private void SetCellText(int tableIndex, int rowAddress, int columnAddress, string text)
        {
            var cell = GetCell(tableIndex, rowAddress, columnAddress);
            if (cell == null)
            {
                _report.MissingTargets.Add(string.Format(CultureInfo.InvariantCulture, "cell table={0} row={1} col={2}", tableIndex, rowAddress, columnAddress));
                return;
            }

            var paragraph = GetDirectCellParagraphs(cell).FirstOrDefault(item => !item.Descendants(Hp + "tbl").Any());
            if (paragraph == null)
            {
                _report.SkippedUnsafe.Add(string.Format(CultureInfo.InvariantCulture, "cell table={0} row={1} col={2} has no non-nested paragraph", tableIndex, rowAddress, columnAddress));
                return;
            }

            SetParagraphNodeText(paragraph, text);
            _report.CellWrites++;
        }

        private void RebuildTableDataRows(int tableIndex, int firstDataRow, IEnumerable<IList<string>> sourceRows, IDictionary<int, CellProjection> columnMap, int templateRowIndex)
        {
            var table = GetTable(tableIndex);
            if (table == null)
            {
                _report.MissingTargets.Add("table " + tableIndex.ToString(CultureInfo.InvariantCulture));
                return;
            }

            var rows = table.Elements(Hp + "tr").ToList();
            if (templateRowIndex >= rows.Count)
            {
                _report.MissingTargets.Add("template row table=" + tableIndex.ToString(CultureInfo.InvariantCulture));
                return;
            }

            var template = new XElement(rows[templateRowIndex]);
            for (var index = rows.Count - 1; index >= firstDataRow; index--)
            {
                rows[index].Remove();
            }

            var rowAddress = firstDataRow;
            foreach (var sourceRow in sourceRows)
            {
                var newRow = new XElement(template);
                UpdateRowAddresses(newRow, rowAddress);
                foreach (var item in columnMap)
                {
                    SetRowCellText(newRow, item.Key, item.Value.Project(sourceRow));
                }

                table.Add(newRow);
                rowAddress++;
                _report.RebuiltRows++;
            }

            table.SetAttributeValue("rowCnt", rowAddress.ToString(CultureInfo.InvariantCulture));
        }

        private void SetRowCellText(XElement rowNode, int columnAddress, string text)
        {
            var cell = rowNode.Elements(Hp + "tc").FirstOrDefault(item =>
            {
                var addr = item.Element(Hp + "cellAddr");
                return addr != null && GetInt(addr, "colAddr", -1) == columnAddress;
            });

            if (cell == null)
            {
                return;
            }

            var paragraph = GetDirectCellParagraphs(cell).FirstOrDefault(item => !item.Descendants(Hp + "tbl").Any());
            if (paragraph != null)
            {
                SetParagraphNodeText(paragraph, text);
            }
        }

        private void UpdateRowAddresses(XElement rowNode, int rowAddress)
        {
            foreach (var cell in rowNode.Elements(Hp + "tc"))
            {
                var addr = cell.Element(Hp + "cellAddr");
                if (addr != null)
                {
                    addr.SetAttributeValue("rowAddr", rowAddress.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        private void SetSectionAfterHeading(string headingText, string startPattern, string endPattern)
        {
            var headingIndex = FindParagraphIndexByText(headingText, 0);
            if (headingIndex < 0)
            {
                _report.MissingTargets.Add("heading " + headingText);
                return;
            }

            var bulletIndex = FindNextParagraphIndexByText("◦", headingIndex);
            if (bulletIndex < 0)
            {
                _report.MissingTargets.Add("bullet after " + headingText);
                return;
            }

            var dashIndex = FindNextParagraphIndexByText("-", bulletIndex);
            if (dashIndex > 0)
            {
                SetParagraphText(dashIndex, string.Empty);
            }

            SetParagraphBlock(bulletIndex, NormalizeBlockLines(GetMarkdownBlock(startPattern, endPattern)));
        }

        private void SetExpectedEffectSection(string startPattern, string endPattern)
        {
            var selfDiagnosisIndex = FindParagraphIndexByText("󰊳 자가진단표", 0);
            if (selfDiagnosisIndex < 0)
            {
                _report.MissingTargets.Add("self diagnosis heading");
                return;
            }

            var bulletIndex = FindPreviousParagraphIndexByText("◦", selfDiagnosisIndex);
            if (bulletIndex < 0)
            {
                _report.MissingTargets.Add("expected effect bullet");
                return;
            }

            var dashIndex = FindNextParagraphIndexByText("-", bulletIndex);
            if (dashIndex > 0 && dashIndex < selfDiagnosisIndex)
            {
                SetParagraphText(dashIndex, string.Empty);
            }

            SetParagraphBlock(bulletIndex, NormalizeBlockLines(GetMarkdownBlock(startPattern, endPattern)));
        }

        private void SetParagraphBlock(int paragraphIndex, IList<string> lines)
        {
            if (lines.Count == 0)
            {
                return;
            }

            var paragraphs = RootParagraphs().ToList();
            if (paragraphIndex >= paragraphs.Count)
            {
                _report.MissingTargets.Add("paragraph " + paragraphIndex.ToString(CultureInfo.InvariantCulture));
                return;
            }

            var anchor = paragraphs[paragraphIndex];
            SetParagraphNodeText(anchor, lines[0]);
            var current = anchor;
            for (var index = 1; index < lines.Count; index++)
            {
                var clone = new XElement(anchor);
                SetParagraphNodeText(clone, lines[index]);
                current.AddAfterSelf(clone);
                current = clone;
                _report.InsertedParagraphs++;
            }
        }

        private void SetParagraphText(int paragraphIndex, string text)
        {
            var paragraphs = RootParagraphs().ToList();
            if (paragraphIndex >= paragraphs.Count)
            {
                _report.MissingTargets.Add("paragraph " + paragraphIndex.ToString(CultureInfo.InvariantCulture));
                return;
            }

            SetParagraphNodeText(paragraphs[paragraphIndex], text);
            _report.ParagraphWrites++;
        }

        private void SetParagraphTextByCurrent(string currentText, string replacementText)
        {
            var paragraphIndex = FindParagraphIndexByText(currentText, 0);
            if (paragraphIndex < 0)
            {
                _report.MissingTargets.Add("paragraph text " + currentText);
                return;
            }

            SetParagraphText(paragraphIndex, replacementText);
        }

        private int FindParagraphIndexByText(string text, int startIndex)
        {
            var paragraphs = RootParagraphs().ToList();
            for (var index = startIndex; index < paragraphs.Count; index++)
            {
                if (string.Equals(TextOf(paragraphs[index]), text, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        private int FindNextParagraphIndexByText(string text, int startIndex)
        {
            return FindParagraphIndexByText(text, startIndex + 1);
        }

        private int FindPreviousParagraphIndexByText(string text, int startIndex)
        {
            var paragraphs = RootParagraphs().ToList();
            for (var index = Math.Min(startIndex - 1, paragraphs.Count - 1); index >= 0; index--)
            {
                if (string.Equals(TextOf(paragraphs[index]), text, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        private XElement GetTable(int tableIndex)
        {
            var tables = _section.Descendants(Hp + "tbl").ToList();
            return tableIndex >= 0 && tableIndex < tables.Count ? tables[tableIndex] : null;
        }

        private XElement GetCell(int tableIndex, int rowAddress, int columnAddress)
        {
            var table = GetTable(tableIndex);
            if (table == null)
            {
                return null;
            }

            return table.Descendants(Hp + "tc").FirstOrDefault(cell =>
            {
                var addr = cell.Element(Hp + "cellAddr");
                return addr != null &&
                       GetInt(addr, "rowAddr", -1) == rowAddress &&
                       GetInt(addr, "colAddr", -1) == columnAddress;
            });
        }

        private IEnumerable<XElement> RootParagraphs()
        {
            return _section.Root == null ? Enumerable.Empty<XElement>() : _section.Root.Elements(Hp + "p");
        }

        private static IEnumerable<XElement> GetDirectCellParagraphs(XElement cell)
        {
            var subList = cell.Element(Hp + "subList");
            return subList == null ? cell.Elements(Hp + "p") : subList.Elements(Hp + "p");
        }

        private static void SetParagraphNodeText(XElement paragraph, string text)
        {
            var textNodes = paragraph.Descendants(Hp + "t").ToList();
            XElement firstTextNode;
            if (textNodes.Count == 0)
            {
                var run = paragraph.Elements(Hp + "run").FirstOrDefault();
                if (run == null)
                {
                    run = new XElement(Hp + "run");
                    var lineSegArray = paragraph.Element(Hp + "linesegarray");
                    if (lineSegArray == null)
                    {
                        paragraph.Add(run);
                    }
                    else
                    {
                        lineSegArray.AddBeforeSelf(run);
                    }
                }

                firstTextNode = new XElement(Hp + "t");
                run.Add(firstTextNode);
                textNodes.Add(firstTextNode);
            }
            else
            {
                firstTextNode = textNodes[0];
            }

            firstTextNode.Value = NormalizePackageWriteText(text);
            for (var index = 1; index < textNodes.Count; index++)
            {
                textNodes[index].Value = string.Empty;
            }

            foreach (var lineSegments in paragraph.Elements(Hp + "linesegarray").ToList())
            {
                lineSegments.Remove();
            }
        }

        private IList<string> NormalizeBlockLines(string block)
        {
            var lines = new List<string>();
            foreach (var raw in (block ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 ||
                    Regex.IsMatch(line, @"^#{1,6}\s+") ||
                    line.StartsWith("![", StringComparison.Ordinal) ||
                    MarkdownTableSeparatorPattern.IsMatch(line))
                {
                    continue;
                }

                if (line.StartsWith("|", StringComparison.Ordinal) && line.Contains("|"))
                {
                    line = string.Join(" / ", line.Trim().Trim('|').Split('|').Select(NormalizeInline).ToArray());
                }
                else
                {
                    line = Regex.Replace(line, @"^>\s*", string.Empty);
                    line = Regex.Replace(line, @"^F\s+", string.Empty);
                    line = Regex.Replace(line, @"^[-*+]\s+", "- ");
                    line = line.Replace("❖", string.Empty);
                    line = NormalizeInline(line);
                }

                if (line.Length > 0)
                {
                    lines.Add(line);
                }
            }

            return lines;
        }

        private string JoinBlockSummary(string block, int maxLines)
        {
            var lines = NormalizeBlockLines(block)
                .Where(line => !string.IsNullOrWhiteSpace(line) &&
                               line != "---" &&
                               !line.StartsWith("*", StringComparison.Ordinal) &&
                               !line.StartsWith("성과지표 /", StringComparison.Ordinal) &&
                               !line.StartsWith("|", StringComparison.Ordinal))
                .Take(maxLines);

            return string.Join(" ", lines.ToArray());
        }

        private string GetMarkdownBlock(string startPattern, string endPattern)
        {
            var match = Regex.Match(_markdown, startPattern + "(.*?)" + endPattern, RegexOptions.Multiline | RegexOptions.Singleline);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private string ExtractFirstMatch(string pattern)
        {
            var match = Regex.Match(_markdown, pattern);
            return match.Success ? NormalizeInline(match.Groups[1].Value) : string.Empty;
        }

        private string CellValue(int tableIndex, int rowIndex, int columnIndex)
        {
            if (tableIndex >= _tables.Count || rowIndex >= _tables[tableIndex].Count || columnIndex >= _tables[tableIndex][rowIndex].Count)
            {
                return string.Empty;
            }

            return _tables[tableIndex][rowIndex][columnIndex] ?? string.Empty;
        }

        private int TableRowCount(int tableIndex)
        {
            return tableIndex >= _tables.Count ? 0 : _tables[tableIndex].Count;
        }

        private IEnumerable<IList<string>> TableRowsAfterHeader(int tableIndex)
        {
            if (tableIndex >= _tables.Count)
            {
                return Enumerable.Empty<IList<string>>();
            }

            return _tables[tableIndex].Skip(1);
        }

        private static string Cell(IList<string> row, int columnIndex)
        {
            return columnIndex >= 0 && columnIndex < row.Count ? row[columnIndex] ?? string.Empty : string.Empty;
        }

        private static CodePercent SplitCodePercent(string value)
        {
            var match = Regex.Match(value ?? string.Empty, @"^(.*?)\s*/\s*([0-9]+)\s*%?$");
            return match.Success
                ? new CodePercent(NormalizeInline(match.Groups[1].Value), NormalizeInline(match.Groups[2].Value))
                : new CodePercent(value ?? string.Empty, string.Empty);
        }

        private static string NormalizeInline(string text)
        {
            var value = (text ?? string.Empty).Trim();
            value = HtmlBreakPattern.Replace(value, " / ");
            value = value.Replace("&nbsp;", " ");
            value = value.Replace("**", string.Empty).Replace("__", string.Empty).Replace("`", string.Empty).Replace("❖", string.Empty);
            value = HtmlTagPattern.Replace(value, string.Empty);
            value = HttpUtility.HtmlDecode(value);
            return MultiWhitespacePattern.Replace(value, " ").Trim();
        }

        private static string NormalizePackageWriteText(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static string TextOf(XElement element)
        {
            var builder = new StringBuilder();
            foreach (var textNode in element.Descendants(Hp + "t"))
            {
                builder.Append(textNode.Value);
            }

            return builder.ToString().Trim();
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

        private void Save(string outputPath)
        {
            _entries["Contents/section0.xml"] = SerializeXmlDocument(_section);
            if (_entries.ContainsKey("Preview/PrvText.txt"))
            {
                _entries["Preview/PrvText.txt"] = new UTF8Encoding(false).GetBytes(string.Join(Environment.NewLine, NormalizeBlockLines(_markdown).ToArray()));
            }

            SimpleZipArchive.WriteAll(outputPath, _entries);
        }

        private void WriteReport(string templatePath, string markdownPath, string outputPath, string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("# Submission Template Fill Report");
            report.AppendLine();
            report.AppendLine("- template: " + Path.GetFullPath(templatePath));
            report.AppendLine("- markdown: " + Path.GetFullPath(markdownPath));
            report.AppendLine("- output: " + Path.GetFullPath(outputPath));
            report.AppendLine("- markdown tables: " + _tables.Count.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- cell writes: " + _report.CellWrites.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- paragraph writes: " + _report.ParagraphWrites.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- inserted paragraphs: " + _report.InsertedParagraphs.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- rebuilt rows: " + _report.RebuiltRows.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- missing targets: " + _report.MissingTargets.Count.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- skipped unsafe: " + _report.SkippedUnsafe.Count.ToString(CultureInfo.InvariantCulture));

            if (_report.MissingTargets.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("## Missing Targets");
                foreach (var item in _report.MissingTargets)
                {
                    report.AppendLine("- " + item);
                }
            }

            if (_report.SkippedUnsafe.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("## Skipped Unsafe");
                foreach (var item in _report.SkippedUnsafe)
                {
                    report.AppendLine("- " + item);
                }
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
        }

        private static byte[] SerializeXmlDocument(XDocument document)
        {
            using (var output = new MemoryStream())
            {
                var settings = new XmlWriterSettings
                {
                    Encoding = new UTF8Encoding(false),
                    OmitXmlDeclaration = document.Declaration == null
                };

                using (var writer = XmlWriter.Create(output, settings))
                {
                    document.Save(writer);
                }

                return output.ToArray();
            }
        }

        private static string ReadTextFile(string path)
        {
            try
            {
                return File.ReadAllText(path, new UTF8Encoding(false, true));
            }
            catch (DecoderFallbackException)
            {
                return File.ReadAllText(path, Encoding.Default);
            }
        }

        internal sealed class FillReport
        {
            public int CellWrites { get; set; }

            public int ParagraphWrites { get; set; }

            public int InsertedParagraphs { get; set; }

            public int RebuiltRows { get; set; }

            public IList<string> MissingTargets { get; private set; }

            public IList<string> SkippedUnsafe { get; private set; }

            public FillReport()
            {
                MissingTargets = new List<string>();
                SkippedUnsafe = new List<string>();
            }
        }

        private struct CodePercent
        {
            public CodePercent(string code, string percent)
            {
                Code = code;
                Percent = percent;
            }

            public string Code { get; private set; }

            public string Percent { get; private set; }
        }

        private sealed class CellProjection
        {
            private readonly int _columnIndex;
            private readonly Func<IList<string>, string> _projector;

            private CellProjection(int columnIndex, Func<IList<string>, string> projector)
            {
                _columnIndex = columnIndex;
                _projector = projector;
            }

            public static CellProjection Column(int columnIndex)
            {
                return new CellProjection(columnIndex, null);
            }

            public static CellProjection Func(Func<IList<string>, string> projector)
            {
                return new CellProjection(-1, projector);
            }

            public string Project(IList<string> row)
            {
                return _projector == null ? Cell(row, _columnIndex) : _projector(row);
            }
        }
    }
}
