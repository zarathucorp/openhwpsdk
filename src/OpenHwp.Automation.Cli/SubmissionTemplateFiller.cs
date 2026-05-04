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
        private static readonly Regex MarkdownImagePattern = new Regex(@"!\[([^\]]*)\]\(([^)]+)\)", RegexOptions.Compiled);
        private static readonly Regex MarkdownTableSeparatorPattern = new Regex(@"^\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?$", RegexOptions.Compiled);
        private const int RoadmapOverviewBodyCharPrId = 50;
        private const int DefaultMarkdownImageWidth = 135;
        private const int DefaultMarkdownImageHeight = 80;

        private readonly string _markdown;
        private readonly string _markdownPath;
        private readonly IList<IList<IList<string>>> _tables;
        private readonly IList<MarkdownImageReference> _imageReferences;
        private readonly string _templatePath;
        private readonly IDictionary<string, byte[]> _entries;
        private readonly XDocument _section;
        private readonly FillReport _report = new FillReport();
        private int _nextImageAnchorIndex;
        private long _nextGeneratedObjectId;

        private SubmissionTemplateFiller(string templatePath, string markdownPath)
        {
            _templatePath = Path.GetFullPath(templatePath);
            _markdownPath = Path.GetFullPath(markdownPath);
            _markdown = ReadTextFile(markdownPath);
            _tables = MarkdownTableParser.ParseTables(_markdown);
            _imageReferences = ParseMarkdownImages(_markdown);
            _entries = SimpleZipArchive.ReadAll(_templatePath);

            byte[] sectionBytes;
            if (!_entries.TryGetValue("Contents/section0.xml", out sectionBytes))
            {
                throw new InvalidOperationException("The HWPX template does not contain Contents/section0.xml.");
            }

            _section = XDocument.Parse(Encoding.UTF8.GetString(sectionBytes), LoadOptions.PreserveWhitespace);
            _nextGeneratedObjectId = Math.Max(1, MaxNumericId(_section) + 1);
            _report.MarkdownTables = _tables.Count;
            _report.MarkdownImages = _imageReferences.Count;
            foreach (var image in _imageReferences)
            {
                _report.MarkdownImageReferences.Add(image);
            }
        }

        public static FillReport Fill(string templatePath, string markdownPath, string outputPath)
        {
            var filler = new SubmissionTemplateFiller(templatePath, markdownPath);
            filler.FillKnownProfile();
            filler.Save(outputPath);
            return filler._report;
        }

        private void FillKnownProfile()
        {
            FillCoverPage();
            FillBodyTables();
            FillCompanyStatus();
            FillBudgetPlan();
            FillTrackOverview();
            FillAttachments();
            FillConsentForms();
            FillRoadmapNarrative();
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
            SetCellLines(
                18,
                8,
                1,
                new[]
                {
                    "기술정의: 자체개발 임상통계 R패키지 핵심 함수를 AI agent 호출형 분석모듈로 표준화",
                    "제품화: openstat.ai 유료 구독형 SaaS와 기관 전용 배포형 임상통계 AI agent로 고도화",
                    "차별성: 분석엔진 호출, 데이터 검증, 통계 실행, 보고서 초안, Audit Trail을 단일 흐름으로 제공",
                },
                RoadmapOverviewBodyCharPrId);
            SetCellLines(
                18,
                9,
                1,
                new[]
                {
                    "추진배경: 인력 중심 분석지원 업무의 반복 절차를 SW, AI agent 기반 제품 자산으로 전환",
                    "목표: PoC 8건, AI agent 적용 분석 프로젝트 50건, Audit Trail 적용 10건, 매출 2억원",
                    "전략: 기존 병원, 바이오, CRO 고객 기반 PoC 후 구독형, 기관계약형으로 전환",
                },
                RoadmapOverviewBodyCharPrId);
            SetCellLines(
                18,
                10,
                1,
                new[]
                {
                    "성장전략: 의학연구 분석서비스, 오픈소스 R패키지, SaaS, 기관 맞춤형 분석웹을 제품군으로 통합",
                    "기반: SCI급 분석지원 경험, 30만 다운로드 오픈소스 생태계, 병원, 바이오 고객 레퍼런스 보유",
                    "확장: 일본, 중국 사용자 기반과 해외 CRO 협력 경험을 활용해 해외 진입 추진",
                },
                RoadmapOverviewBodyCharPrId);
            SetCellLines(
                18,
                11,
                1,
                new[]
                {
                    "활용방안: 병원 연구지원, 바이오 임상시험, CRO 통계분석, 공공의료 데이터 분석으로 확장",
                    "효과: 분석 리드타임 단축, 검토 이력 확보, 품질 재현성 향상, 반복업무 자동화",
                    "성과: 구독형 SaaS 매출과 기관 전용 구축 매출을 병행해 후속 투자, 고용 기반 확보",
                },
                RoadmapOverviewBodyCharPrId);

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

            SetTableTextStyle(20, RoadmapOverviewBodyCharPrId);
        }

        private void FillRoadmapNarrative()
        {
            SetExpectedEffectSection(@"^### 5\. 기대효과.*?$", @"(?=^## □ 자가진단표)");
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

            FillLaborCostTable(TableRowsAfterHeader(44).ToList());
        }

        private void FillLaborCostTable(IList<IList<string>> sourceRows)
        {
            var detailRows = sourceRows.Where(row => !IsSummaryRow(row)).ToList();
            FillLaborCostRow(2, "정규직", detailRows.Where(row => Cell(row, 0).Contains("정규직")).ToList());
            FillLaborCostRow(3, "비정규직", detailRows.Where(row => Cell(row, 0).Contains("비정규직")).ToList());
            FillLaborCostRow(4, "무기계약직", detailRows.Where(row => Cell(row, 0).Contains("무기계약직")).ToList());

            var summary = sourceRows.FirstOrDefault(IsSummaryRow);
            var total = summary == null ? FormatAmount(SumAmount(detailRows, 6)) : Cell(summary, 6);
            SetCellText(41, 5, 7, total);
        }

        private void FillLaborCostRow(int targetRow, string category, IList<IList<string>> rows)
        {
            SetCellText(41, targetRow, 1, category);
            if (rows.Count == 0)
            {
                for (var column = 2; column <= 7; column++)
                {
                    SetCellText(41, targetRow, column, string.Empty);
                }

                return;
            }

            SetCellText(41, targetRow, 2, rows.Count == 1 ? Cell(rows[0], 1) : Cell(rows[0], 1) + " 외 " + (rows.Count - 1).ToString(CultureInfo.InvariantCulture) + "명");
            SetCellText(41, targetRow, 3, rows.Count == 1 ? Cell(rows[0], 2) : FirstNonEmpty(rows, 2) + " 등");
            SetCellText(41, targetRow, 4, rows.Count == 1 ? Cell(rows[0], 3) : "별첨");
            SetCellText(41, targetRow, 5, rows.Count == 1 ? Cell(rows[0], 4) : string.Empty);
            SetCellText(41, targetRow, 6, JoinDistinct(rows, 5));
            SetCellText(41, targetRow, 7, FormatAmount(SumAmount(rows, 6)));
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

        private void SetCellText(int tableIndex, int rowAddress, int columnAddress, string text, int? charPrIdRef = null)
        {
            var cell = GetCell(tableIndex, rowAddress, columnAddress);
            if (cell == null)
            {
                _report.MissingTargets.Add(string.Format(CultureInfo.InvariantCulture, "cell table={0} row={1} col={2}", tableIndex, rowAddress, columnAddress));
                return;
            }

            var paragraphs = GetDirectCellParagraphs(cell).Where(item => !item.Descendants(Hp + "tbl").Any()).ToList();
            if (paragraphs.Count == 0)
            {
                _report.SkippedUnsafe.Add(string.Format(CultureInfo.InvariantCulture, "cell table={0} row={1} col={2} has no non-nested paragraph", tableIndex, rowAddress, columnAddress));
                return;
            }

            SetParagraphNodeText(paragraphs[0], text, charPrIdRef);
            paragraphs.Skip(1).Remove();
            HwpxTextLayoutHelper.ExpandRowHeightForText(cell, text);
            _report.CellWrites++;
        }

        private void SetCellLines(int tableIndex, int rowAddress, int columnAddress, IList<string> lines, int? charPrIdRef = null)
        {
            if (lines.Count == 0)
            {
                SetCellText(tableIndex, rowAddress, columnAddress, string.Empty, charPrIdRef);
                return;
            }

            var cell = GetCell(tableIndex, rowAddress, columnAddress);
            if (cell == null)
            {
                _report.MissingTargets.Add(string.Format(CultureInfo.InvariantCulture, "cell table={0} row={1} col={2}", tableIndex, rowAddress, columnAddress));
                return;
            }

            var paragraphs = GetDirectCellParagraphs(cell).Where(item => !item.Descendants(Hp + "tbl").Any()).ToList();
            if (paragraphs.Count == 0)
            {
                _report.SkippedUnsafe.Add(string.Format(CultureInfo.InvariantCulture, "cell table={0} row={1} col={2} has no non-nested paragraph", tableIndex, rowAddress, columnAddress));
                return;
            }

            SetParagraphNodeText(paragraphs[0], lines[0], charPrIdRef);
            paragraphs.Skip(1).Remove();
            var current = paragraphs[0];
            for (var index = 1; index < lines.Count; index++)
            {
                var paragraph = new XElement(paragraphs[0]);
                SetParagraphNodeText(paragraph, lines[index], charPrIdRef);
                current.AddAfterSelf(paragraph);
                current = paragraph;
                _report.InsertedParagraphs++;
            }

            HwpxTextLayoutHelper.ExpandRowHeightForText(cell, string.Join("\n", lines.ToArray()));
            _report.CellWrites++;
        }

        private void SetTableTextStyle(int tableIndex, int charPrIdRef)
        {
            var table = GetTable(tableIndex);
            if (table == null)
            {
                _report.MissingTargets.Add("table " + tableIndex.ToString(CultureInfo.InvariantCulture));
                return;
            }

            foreach (var run in table.Descendants(Hp + "run"))
            {
                run.SetAttributeValue("charPrIDRef", charPrIdRef.ToString(CultureInfo.InvariantCulture));
            }
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
                HwpxTextLayoutHelper.ExpandRowHeightForText(cell, text);
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

            SetParagraphBlock(bulletIndex, GetMarkdownBlock(startPattern, endPattern));
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

            SetParagraphBlock(bulletIndex, GetMarkdownBlock(startPattern, endPattern));
        }

        private void SetParagraphBlock(int paragraphIndex, string block)
        {
            var paragraphs = RootParagraphs().ToList();
            if (paragraphIndex >= paragraphs.Count)
            {
                _report.MissingTargets.Add("paragraph " + paragraphIndex.ToString(CultureInfo.InvariantCulture));
                return;
            }

            var items = NormalizeBlockItems(block);
            if (items.Count == 0)
            {
                return;
            }

            var anchor = paragraphs[paragraphIndex];
            var current = anchor;
            var anchorUsed = false;

            foreach (var item in items)
            {
                if (item.Kind == BlockItemKind.Table)
                {
                    if (!anchorUsed)
                    {
                        SetParagraphNodeText(anchor, string.Empty);
                        anchorUsed = true;
                    }

                    var tableParagraph = CreateMarkdownTableParagraph(item.TableRows);
                    if (tableParagraph == null)
                    {
                        _report.SkippedUnsupported.Add("markdown table could not be rendered because no reference table was available");
                        continue;
                    }

                    current.AddAfterSelf(tableParagraph);
                    current = tableParagraph;
                    _report.RenderedMarkdownTables++;
                    _report.RenderedMarkdownTableRows += item.TableRows.Count;
                    continue;
                }

                var text = item.Kind == BlockItemKind.Image ? item.Image.AnchorText : item.Text;
                if (!anchorUsed)
                {
                    SetParagraphNodeText(anchor, text);
                    anchorUsed = true;
                    current = anchor;
                    continue;
                }

                var clone = new XElement(anchor);
                SetParagraphNodeText(clone, text);
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

        private IList<BlockItem> NormalizeBlockItems(string block)
        {
            var items = new List<BlockItem>();
            var lines = (block ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index].Trim();
                if (line.Length == 0 ||
                    Regex.IsMatch(line, @"^#{1,6}\s+") ||
                    string.Equals(line, "---", StringComparison.Ordinal) ||
                    IsMarkdownCaptionLine(line))
                {
                    continue;
                }

                string imageAlt;
                string imagePath;
                if (TryParseMarkdownImageLine(line, out imageAlt, out imagePath))
                {
                    items.Add(BlockItem.ForImage(CreateImageWriteOperation(imageAlt, imagePath)));
                    continue;
                }

                if (IsMarkdownTableLine(line))
                {
                    var tableLines = new List<string>();
                    while (index < lines.Length && IsMarkdownTableLine(lines[index].Trim()))
                    {
                        tableLines.Add(lines[index].Trim());
                        index++;
                    }

                    index--;
                    var tableRows = ParseMarkdownTableLines(tableLines);
                    if (tableRows.Count > 0)
                    {
                        items.Add(BlockItem.ForTable(tableRows));
                    }

                    continue;
                }

                if (MarkdownTableSeparatorPattern.IsMatch(line))
                {
                    continue;
                }

                line = Regex.Replace(line, @"^>\s*", string.Empty).Trim();
                if (Regex.IsMatch(line, @"^F\s+") ||
                    line.StartsWith("⛔", StringComparison.Ordinal) ||
                    IsMarkdownCaptionLine(line))
                {
                    continue;
                }

                line = NormalizeProposalListLine(line);
                if (line.Length > 0)
                {
                    items.Add(BlockItem.ForText(line));
                }
            }

            return items;
        }

        private ImageWriteOperation CreateImageWriteOperation(string altText, string sourcePath)
        {
            _nextImageAnchorIndex++;
            var operation = new ImageWriteOperation
            {
                AltText = NormalizeInline(altText),
                SourcePath = (sourcePath ?? string.Empty).Trim(),
                ResolvedPath = ResolveMarkdownRelativePath(sourcePath),
                AnchorText = "[[openhwpsdk-image-" + _nextImageAnchorIndex.ToString("0000", CultureInfo.InvariantCulture) + "]]",
                Width = DefaultMarkdownImageWidth,
                Height = DefaultMarkdownImageHeight,
                Status = "pending",
                Note = "queued for HWP COM InsertPicture"
            };

            _report.ImageWrites.Add(operation);
            return operation;
        }

        private string ResolveMarkdownRelativePath(string sourcePath)
        {
            var path = (sourcePath ?? string.Empty).Trim().Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_markdownPath) ?? Directory.GetCurrentDirectory(), path));
        }

        private static bool TryParseMarkdownImageLine(string line, out string altText, out string sourcePath)
        {
            altText = string.Empty;
            sourcePath = string.Empty;
            var match = MarkdownImagePattern.Match(line ?? string.Empty);
            if (!match.Success)
            {
                return false;
            }

            altText = match.Groups[1].Value;
            sourcePath = match.Groups[2].Value;
            return true;
        }

        private static bool IsMarkdownTableLine(string line)
        {
            var value = (line ?? string.Empty).Trim();
            return value.StartsWith("|", StringComparison.Ordinal) && value.Contains("|");
        }

        private static IList<IList<string>> ParseMarkdownTableLines(IEnumerable<string> lines)
        {
            var rows = new List<IList<string>>();
            foreach (var raw in lines)
            {
                var line = (raw ?? string.Empty).Trim().Trim('|');
                if (line.Length == 0 || IsMarkdownTableSeparator(line))
                {
                    continue;
                }

                rows.Add(line.Split('|').Select(NormalizeInline).ToList());
            }

            return rows;
        }

        private static bool IsMarkdownTableSeparator(string tableLine)
        {
            foreach (var character in tableLine ?? string.Empty)
            {
                if (character != '-' && character != ':' && character != '|' && character != ' ')
                {
                    return false;
                }
            }

            return !string.IsNullOrWhiteSpace(tableLine);
        }

        private XElement CreateMarkdownTableParagraph(IList<IList<string>> sourceRows)
        {
            if (sourceRows == null || sourceRows.Count == 0)
            {
                return null;
            }

            var columnCount = Math.Max(1, sourceRows.Select(row => row.Count).DefaultIfEmpty(1).Max());
            var referenceTable = FindMarkdownTableReference(columnCount);
            if (referenceTable == null)
            {
                return null;
            }

            var referenceParagraph = referenceTable.Ancestors(Hp + "p").FirstOrDefault();
            if (referenceParagraph == null)
            {
                return null;
            }

            var paragraph = new XElement(referenceParagraph);
            paragraph.SetAttributeValue("id", NextGeneratedObjectId());
            paragraph.Elements(Hp + "linesegarray").Remove();

            var table = paragraph.Descendants(Hp + "tbl").FirstOrDefault();
            if (table == null)
            {
                return null;
            }

            table.SetAttributeValue("id", NextGeneratedObjectId());
            table.SetAttributeValue("rowCnt", sourceRows.Count.ToString(CultureInfo.InvariantCulture));
            table.SetAttributeValue("colCnt", columnCount.ToString(CultureInfo.InvariantCulture));

            var existingRows = table.Elements(Hp + "tr").ToList();
            if (existingRows.Count == 0)
            {
                return null;
            }

            var headerTemplate = existingRows[0];
            var bodyTemplate = existingRows.Count > 1 ? existingRows[1] : existingRows[0];
            var tableWidth = GetTableWidth(referenceTable);
            var columnWidths = BuildMarkdownTableColumnWidths(referenceTable, columnCount, tableWidth);
            table.Elements(Hp + "tr").Remove();

            for (var rowIndex = 0; rowIndex < sourceRows.Count; rowIndex++)
            {
                var templateRow = rowIndex == 0 ? headerTemplate : bodyTemplate;
                table.Add(CreateMarkdownTableRow(templateRow, rowIndex, sourceRows[rowIndex], columnCount, columnWidths));
            }

            return paragraph;
        }

        private XElement CreateMarkdownTableRow(XElement templateRow, int rowIndex, IList<string> sourceRow, int columnCount, IList<int> columnWidths)
        {
            var row = new XElement(templateRow);
            var templateCells = templateRow.Elements(Hp + "tc").ToList();
            row.Elements(Hp + "tc").Remove();

            for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
            {
                var templateCell = templateCells.Count == 0 ? null : templateCells[Math.Min(columnIndex, templateCells.Count - 1)];
                if (templateCell == null)
                {
                    continue;
                }

                row.Add(CreateMarkdownTableCell(
                    templateCell,
                    rowIndex,
                    columnIndex,
                    columnWidths[Math.Min(columnIndex, columnWidths.Count - 1)],
                    columnIndex < sourceRow.Count ? sourceRow[columnIndex] : string.Empty));
            }

            return row;
        }

        private XElement CreateMarkdownTableCell(XElement templateCell, int rowIndex, int columnIndex, int width, string text)
        {
            var cell = new XElement(templateCell);
            EnsureCellChild(cell, "cellAddr").SetAttributeValue("rowAddr", rowIndex.ToString(CultureInfo.InvariantCulture));
            cell.Element(Hp + "cellAddr").SetAttributeValue("colAddr", columnIndex.ToString(CultureInfo.InvariantCulture));
            EnsureCellChild(cell, "cellSpan").SetAttributeValue("rowSpan", "1");
            cell.Element(Hp + "cellSpan").SetAttributeValue("colSpan", "1");
            EnsureCellChild(cell, "cellSz").SetAttributeValue("width", width.ToString(CultureInfo.InvariantCulture));

            var paragraphs = GetDirectCellParagraphs(cell).Where(item => !item.Descendants(Hp + "tbl").Any()).ToList();
            if (paragraphs.Count > 0)
            {
                paragraphs[0].SetAttributeValue("id", NextGeneratedObjectId());
                SetParagraphNodeText(paragraphs[0], text);
                paragraphs.Skip(1).Remove();
            }

            HwpxTextLayoutHelper.ExpandRowHeightForText(cell, text);
            return cell;
        }

        private static XElement EnsureCellChild(XElement cell, string localName)
        {
            var child = cell.Element(Hp + localName);
            if (child != null)
            {
                return child;
            }

            child = new XElement(Hp + localName);
            cell.Add(child);
            return child;
        }

        private XElement FindMarkdownTableReference(int columnCount)
        {
            var tables = _section.Descendants(Hp + "tbl")
                .Where(table => !HasMergedCells(table) && table.Elements(Hp + "tr").Count() >= 2)
                .ToList();

            return tables.FirstOrDefault(table => MaxColumnCount(table) == columnCount) ??
                   tables.FirstOrDefault(table => MaxColumnCount(table) > columnCount) ??
                   tables.FirstOrDefault();
        }

        private static bool HasMergedCells(XElement table)
        {
            return table.Descendants(Hp + "tc").Any(cell =>
            {
                var span = cell.Element(Hp + "cellSpan");
                return span != null && (GetInt(span, "rowSpan", 1) > 1 || GetInt(span, "colSpan", 1) > 1);
            });
        }

        private static int MaxColumnCount(XElement table)
        {
            return table.Elements(Hp + "tr").Select(row => row.Elements(Hp + "tc").Count()).DefaultIfEmpty(0).Max();
        }

        private static int GetTableWidth(XElement table)
        {
            var size = table == null ? null : table.Element(Hp + "sz");
            if (size != null)
            {
                var width = GetInt(size, "width", 0);
                if (width > 0)
                {
                    return width;
                }
            }

            return table == null
                ? 0
                : table.Descendants(Hp + "cellSz").Select(cell => GetInt(cell, "width", 0)).DefaultIfEmpty(0).Max();
        }

        private static IList<int> BuildMarkdownTableColumnWidths(XElement referenceTable, int columnCount, int tableWidth)
        {
            var firstRow = referenceTable.Elements(Hp + "tr").FirstOrDefault();
            var referenceWidths = firstRow == null
                ? new List<int>()
                : firstRow.Elements(Hp + "tc")
                    .Select(cell =>
                    {
                        var size = cell.Element(Hp + "cellSz");
                        return size == null ? 0 : GetInt(size, "width", 0);
                    })
                    .Where(width => width > 0)
                    .ToList();

            if (referenceWidths.Count == columnCount)
            {
                return referenceWidths;
            }

            var width = tableWidth > 0 ? tableWidth : 45000;
            var baseWidth = Math.Max(1000, width / Math.Max(1, columnCount));
            var widths = Enumerable.Repeat(baseWidth, columnCount).ToList();
            var remainder = width - (baseWidth * columnCount);
            if (widths.Count > 0)
            {
                widths[widths.Count - 1] += remainder;
            }

            return widths;
        }

        private string NextGeneratedObjectId()
        {
            var value = _nextGeneratedObjectId;
            _nextGeneratedObjectId++;
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static void SetParagraphNodeText(XElement paragraph, string text, int? charPrIdRef = null)
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

            if (charPrIdRef.HasValue)
            {
                foreach (var run in paragraph.Elements(Hp + "run"))
                {
                    run.SetAttributeValue("charPrIDRef", charPrIdRef.Value.ToString(CultureInfo.InvariantCulture));
                }
            }

            firstTextNode.Value = NormalizePackageWriteText(text);
            for (var index = 1; index < textNodes.Count; index++)
            {
                textNodes[index].Value = string.Empty;
            }

            paragraph.Elements(Hp + "linesegarray").Remove();
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
                    string.Equals(line, "---", StringComparison.Ordinal) ||
                    IsMarkdownCaptionLine(line) ||
                    MarkdownTableSeparatorPattern.IsMatch(line))
                {
                    continue;
                }

                line = Regex.Replace(line, @"^>\s*", string.Empty).Trim();
                if (Regex.IsMatch(line, @"^F\s+") ||
                    line.StartsWith("⛔", StringComparison.Ordinal) ||
                    IsMarkdownCaptionLine(line))
                {
                    continue;
                }

                if (line.StartsWith("|", StringComparison.Ordinal) && line.Contains("|"))
                {
                    line = "   - " + string.Join(" / ", line.Trim().Trim('|').Split('|').Select(NormalizeInline).ToArray());
                }
                else
                {
                    line = NormalizeProposalListLine(line);
                }

                if (line.Length > 0)
                {
                    lines.Add(line);
                }
            }

            return lines;
        }

        private static string NormalizeProposalListLine(string line)
        {
            var value = (line ?? string.Empty).Replace("❖", string.Empty).Trim();
            var headingMatch = Regex.Match(value, @"^[-*+]\s+◦\s*(.+)$");
            if (headingMatch.Success)
            {
                return " ◦ " + NormalizeInline(headingMatch.Groups[1].Value);
            }

            var bulletMatch = Regex.Match(value, @"^[-*+]\s+(.+)$");
            if (bulletMatch.Success)
            {
                return "   - " + NormalizeInline(bulletMatch.Groups[1].Value);
            }

            var circleMatch = Regex.Match(value, @"^◦\s*(.+)$");
            if (circleMatch.Success)
            {
                return " ◦ " + NormalizeInline(circleMatch.Groups[1].Value);
            }

            return NormalizeInline(value);
        }

        private static bool IsMarkdownCaptionLine(string line)
        {
            var value = (line ?? string.Empty).Trim();
            return value.Length > 2 &&
                   value.StartsWith("*", StringComparison.Ordinal) &&
                   value.EndsWith("*", StringComparison.Ordinal) &&
                   !value.StartsWith("**", StringComparison.Ordinal);
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

        private static bool IsSummaryRow(IList<string> row)
        {
            var marker = Regex.Replace(Cell(row, 0), @"[\s\*]+", string.Empty);
            return marker.Contains("합계");
        }

        private static string JoinDistinct(IEnumerable<IList<string>> rows, int columnIndex)
        {
            return string.Join(" / ", rows
                .Select(row => Cell(row, columnIndex).Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.Ordinal));
        }

        private static string FirstNonEmpty(IEnumerable<IList<string>> rows, int columnIndex)
        {
            return rows
                .Select(row => Cell(row, columnIndex).Trim())
                .FirstOrDefault(value => value.Length > 0) ?? string.Empty;
        }

        private static long SumAmount(IEnumerable<IList<string>> rows, int columnIndex)
        {
            long sum = 0;
            foreach (var row in rows)
            {
                var value = Regex.Replace(Cell(row, columnIndex), @"[^0-9\-]", string.Empty);
                long parsed;
                if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    sum += parsed;
                }
            }

            return sum;
        }

        private static string FormatAmount(long value)
        {
            return value == 0 ? string.Empty : value.ToString("N0", CultureInfo.InvariantCulture);
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
            value = RemoveUnsupportedSupplementarySymbols(value);
            return MultiWhitespacePattern.Replace(value, " ").Trim();
        }

        private static string RemoveUnsupportedSupplementarySymbols(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return Regex.Replace(value, @"[\uD800-\uDBFF][\uDC00-\uDFFF]", match =>
            {
                var codePoint = char.ConvertToUtf32(match.Value, 0);
                return codePoint >= 0xF0000 && codePoint <= 0xFFFFD ? match.Value : string.Empty;
            });
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

            SimpleZipArchive.WriteAllPreservingTemplate(_templatePath, outputPath, _entries);
        }

        public static void WriteReport(FillReport fillReport, string templatePath, string markdownPath, string outputPath, string reportPath)
        {
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                return;
            }

            var appliedImages = fillReport.ImageWrites.Count(item => string.Equals(item.Status, "applied", StringComparison.OrdinalIgnoreCase));
            var failedImages = fillReport.ImageWrites.Count(item => string.Equals(item.Status, "failed", StringComparison.OrdinalIgnoreCase));
            var pendingImages = fillReport.ImageWrites.Count(item => string.Equals(item.Status, "pending", StringComparison.OrdinalIgnoreCase));
            var mappedImageCount = fillReport.ImageWrites.Count;
            var unmappedImages = FindUnmappedMarkdownImages(fillReport);
            var unmappedImageCount = unmappedImages.Count;

            var report = new StringBuilder();
            report.AppendLine("# Submission Template Fill Report");
            report.AppendLine();
            report.AppendLine("- template: " + Path.GetFullPath(templatePath));
            report.AppendLine("- markdown: " + Path.GetFullPath(markdownPath));
            report.AppendLine("- output: " + Path.GetFullPath(outputPath));
            report.AppendLine("- markdown tables: " + fillReport.MarkdownTables.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- markdown tables rendered as HWP tables: " + fillReport.RenderedMarkdownTables.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- markdown table rows rendered: " + fillReport.RenderedMarkdownTableRows.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- markdown images: " + fillReport.MarkdownImages.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- image anchors queued for HWP COM: " + mappedImageCount.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- image writes applied: " + appliedImages.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- image writes failed: " + failedImages.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- image writes pending: " + pendingImages.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- markdown images not mapped by profile: " + unmappedImageCount.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- cell writes: " + fillReport.CellWrites.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- paragraph writes: " + fillReport.ParagraphWrites.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- inserted paragraphs: " + fillReport.InsertedParagraphs.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- rebuilt rows: " + fillReport.RebuiltRows.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- missing targets: " + fillReport.MissingTargets.Count.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- skipped unsafe: " + fillReport.SkippedUnsafe.Count.ToString(CultureInfo.InvariantCulture));
            report.AppendLine("- skipped unsupported: " + fillReport.SkippedUnsupported.Count.ToString(CultureInfo.InvariantCulture));

            if (fillReport.ImageWrites.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("## Image Writes");
                report.AppendLine();
                report.AppendLine("| anchor | status | path | note |");
                report.AppendLine("| --- | --- | --- | --- |");
                foreach (var item in fillReport.ImageWrites)
                {
                    report.Append("| ");
                    report.Append(EscapeMarkdownTable(item.AnchorText));
                    report.Append(" | ");
                    report.Append(EscapeMarkdownTable(item.Status));
                    report.Append(" | ");
                    report.Append(EscapeMarkdownTable(item.SourcePath));
                    report.Append(" | ");
                    report.Append(EscapeMarkdownTable(item.Note));
                    report.AppendLine(" |");
                }
            }

            if (unmappedImageCount > 0)
            {
                report.AppendLine();
                report.AppendLine("## Markdown Images Not Mapped");
                report.AppendLine();
                report.AppendLine("These image references were present in the Markdown source but were not queued for HWP COM insertion by the current profile.");
                foreach (var item in unmappedImages)
                {
                    report.AppendLine("- line " + item.LineNumber.ToString(CultureInfo.InvariantCulture) + ": " + item.SourcePath);
                }
            }

            if (fillReport.MissingTargets.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("## Missing Targets");
                foreach (var item in fillReport.MissingTargets)
                {
                    report.AppendLine("- " + item);
                }
            }

            if (fillReport.SkippedUnsafe.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("## Skipped Unsafe");
                foreach (var item in fillReport.SkippedUnsafe)
                {
                    report.AppendLine("- " + item);
                }
            }

            if (fillReport.SkippedUnsupported.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("## Skipped Unsupported");
                foreach (var item in fillReport.SkippedUnsupported)
                {
                    report.AppendLine("- " + item);
                }
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(true));
        }

        public static int CountUnmappedMarkdownImages(FillReport fillReport)
        {
            return FindUnmappedMarkdownImages(fillReport).Count;
        }

        private static IList<MarkdownImageReference> FindUnmappedMarkdownImages(FillReport fillReport)
        {
            var mappedCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var image in fillReport.ImageWrites)
            {
                var key = NormalizeImageSourceKey(image.SourcePath);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                mappedCounts[key] = mappedCounts.ContainsKey(key) ? mappedCounts[key] + 1 : 1;
            }

            var unmapped = new List<MarkdownImageReference>();
            foreach (var image in fillReport.MarkdownImageReferences)
            {
                var key = NormalizeImageSourceKey(image.SourcePath);
                if (!string.IsNullOrWhiteSpace(key) && mappedCounts.ContainsKey(key) && mappedCounts[key] > 0)
                {
                    mappedCounts[key]--;
                    continue;
                }

                unmapped.Add(image);
            }

            return unmapped;
        }

        private static string NormalizeImageSourceKey(string sourcePath)
        {
            return (sourcePath ?? string.Empty).Trim().Replace('\\', '/');
        }

        private static string EscapeMarkdownTable(string value)
        {
            return (value ?? string.Empty)
                .Replace("\\", "\\\\")
                .Replace("|", "\\|")
                .Replace("\r", " ")
                .Replace("\n", " ");
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

        private static IList<MarkdownImageReference> ParseMarkdownImages(string markdown)
        {
            var images = new List<MarkdownImageReference>();
            var lines = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                foreach (Match match in MarkdownImagePattern.Matches(lines[index]))
                {
                    images.Add(new MarkdownImageReference
                    {
                        LineNumber = index + 1,
                        AltText = NormalizeInline(match.Groups[1].Value),
                        SourcePath = (match.Groups[2].Value ?? string.Empty).Trim()
                    });
                }
            }

            return images;
        }

        private static long MaxNumericId(XDocument document)
        {
            long max = 0;
            foreach (var attribute in document.Descendants().Attributes("id"))
            {
                long value;
                if (long.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) && value > max)
                {
                    max = value;
                }
            }

            return max;
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
            public int MarkdownTables { get; set; }

            public int RenderedMarkdownTables { get; set; }

            public int RenderedMarkdownTableRows { get; set; }

            public int MarkdownImages { get; set; }

            public int CellWrites { get; set; }

            public int ParagraphWrites { get; set; }

            public int InsertedParagraphs { get; set; }

            public int RebuiltRows { get; set; }

            public IList<string> MissingTargets { get; private set; }

            public IList<string> SkippedUnsafe { get; private set; }

            public IList<string> SkippedUnsupported { get; private set; }

            public IList<MarkdownImageReference> MarkdownImageReferences { get; private set; }

            public IList<ImageWriteOperation> ImageWrites { get; private set; }

            public FillReport()
            {
                MissingTargets = new List<string>();
                SkippedUnsafe = new List<string>();
                SkippedUnsupported = new List<string>();
                MarkdownImageReferences = new List<MarkdownImageReference>();
                ImageWrites = new List<ImageWriteOperation>();
            }
        }

        internal sealed class MarkdownImageReference
        {
            public int LineNumber { get; set; }

            public string AltText { get; set; }

            public string SourcePath { get; set; }
        }

        internal sealed class ImageWriteOperation
        {
            public string AltText { get; set; }

            public string SourcePath { get; set; }

            public string ResolvedPath { get; set; }

            public string AnchorText { get; set; }

            public int Width { get; set; }

            public int Height { get; set; }

            public string Status { get; set; }

            public string Note { get; set; }
        }

        private enum BlockItemKind
        {
            Text,
            Table,
            Image
        }

        private sealed class BlockItem
        {
            public BlockItemKind Kind { get; private set; }

            public string Text { get; private set; }

            public IList<IList<string>> TableRows { get; private set; }

            public ImageWriteOperation Image { get; private set; }

            public static BlockItem ForText(string text)
            {
                return new BlockItem
                {
                    Kind = BlockItemKind.Text,
                    Text = text ?? string.Empty
                };
            }

            public static BlockItem ForTable(IList<IList<string>> tableRows)
            {
                return new BlockItem
                {
                    Kind = BlockItemKind.Table,
                    TableRows = tableRows
                };
            }

            public static BlockItem ForImage(ImageWriteOperation image)
            {
                return new BlockItem
                {
                    Kind = BlockItemKind.Image,
                    Image = image
                };
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
