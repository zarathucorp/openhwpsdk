# OpenHwp Automation

설치된 한글 프로그램을 `COM/OLE`로 제어하기 위한 C# 래퍼와 CLI 샘플입니다.

이 저장소는 한컴의 별도 SDK 런타임을 감싸지 않습니다. 대신 현재 PC에 설치된 한글이 노출하는 `HWPFrame.HwpObject`를 사용합니다.

## 현재 구현

- 라이브러리: `src/OpenHwp.Automation`
- CLI 샘플: `src/OpenHwp.Automation.Cli`
- 빌드 스크립트: `build.cmd`

핵심 클래스는 `OpenHwp.Automation.HwpSession` 입니다.

## 빌드

```bat
build.cmd
```

## CLI 예제

버전 확인:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe version
```

새 문서에 텍스트를 넣고 저장:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe new-text C:\temp\hello.hwpx "Hello from OpenHwp"
```

기존 문서를 열어서 다른 이름으로 저장:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe copy-save C:\temp\in.hwpx C:\temp\out.hwpx
```

기존 문서를 PDF로 저장:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible export-pdf C:\temp\in.hwpx C:\temp\out.pdf
```

문서 정보 읽기:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe doc-info C:\temp\in.hwp
```

문서 전체 텍스트 읽기:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe read-text C:\temp\in.hwp
```

문서 전체 텍스트를 UTF-8 파일로 저장:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe read-text C:\temp\in.hwp C:\temp\in.txt
```

특정 페이지 텍스트 읽기:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe read-page C:\temp\in.hwp 1
```

필드 존재 여부 및 값 조회:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe field-exists C:\temp\form.hwp TEST
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe field-get C:\temp\form.hwp TEST
```

필드 값 쓰기:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe field-set C:\temp\form.hwpx TEST "updated text" C:\temp\form-out.hwpx
```

제네릭 액션 실행:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe action InsertText Text="raw action text" --save C:\temp\raw.hwpx
```

Markdown 내용을 HWPX 템플릿으로 옮기고, HWPX 레이아웃 보존 여부를 검사:

`markdown-to-hwpx` is not exposed. A command with that name must render Markdown as HWP structure and character styles: headings as larger headings, tables as HWP tables, and inline marks such as bold/code as styled text.

Existing form templates should be filled through cell/cursor commands, then checked with `validate-layout`.

Template HWPX files can first be mapped from the real package XML, then filled through HWP automation or package mode:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe extract-form-map C:\temp\template.hwpx C:\temp\template-form-map.xml
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible probe-form-map C:\temp\template.hwpx C:\temp\template-form-map.xml C:\temp\template-probe.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible apply-form-map C:\temp\template.hwpx C:\temp\template-form-map.xml C:\temp\filled.hwpx --report C:\temp\filled-apply.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe apply-form-map --package C:\temp\template.hwpx C:\temp\template-form-map.xml C:\temp\filled-package.hwpx --report C:\temp\filled-package-apply.md
```

Edit only the generated map's `writeText`, `writeImage`, and supported field `writeValue` elements. The map records the full HWPX package structure (`content.hpf`, manifest/spine, XML parts, previews, metadata entries) and marks write candidates from every XML part that contains HWP paragraph/table text. It also emits a separate `fields` section for named fields, press fields, and form objects such as `checkBtn`/checkbox, radio, combo, and edit controls. Press-field entries are package-writable with `writeText enabled="true"`; checkbox entries are package-writable with `writeValue enabled="true"` and values `CHECKED` or `UNCHECKED`. Other field/form entries remain inventory-only with `writeSupported="false"`. It is merge/nested-table aware: table grids are reconstructed from `cellAddr` plus `cellSpan`, and each cell's `currentText` excludes nested table text. Use `probe-form-map` before HWP-backed writing to confirm every mapped cell/anchor can be selected in HWP. Use `apply-form-map --package` for COM-free text writes, package-level image embedding, package press-field writes, and package checkbox value writes; use the HWP-backed path when editor-backed behavior is required. `--report` writes apply attempted/applied/failed/skipped details for both COM and package modes; package-mode layout validation is written next to it as `*.layout.md`. Package anchor writes are applied from the end of each part to reduce repeated-anchor index drift.

Scan a file or directory of HWPX packages to see which authoring features are actually present in the corpus:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe scan-hwpx-features test test\out\hwpx_feature_scan.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe list-header-footer test\corpus\features\header-footer.hwpx test\out\header_footer_inventory.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe set-header-footer-text test\corpus\features\header-footer.hwpx test\out\header_footer_text_write.hwpx --kind header --section section0 --anchor "Header fixture" --text "Updated Header Fixture" --report test\out\header_footer_text_write.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible page-number-set C:\temp\template.hwpx C:\temp\page-numbered.hwpx --draw-pos 5 --side-char - --report C:\temp\page-number-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible list-fields C:\temp\template.hwpx C:\temp\field-inventory.md --com
```

The tracked feature corpus can be regenerated and scanned independently:

```bat
powershell -NoProfile -ExecutionPolicy Bypass -File tools\New-HwpxFeatureFixtures.ps1
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe scan-hwpx-features test\corpus\features test\out\hwpx_feature_scan_features.md
```

The feature scan report includes aggregate counts, authoring coverage, detailed feature groups, missing corpus signals, per-file totals, and inventory tables for header/footer, field/form, references, and notes. Console output also reports `field_markers`, `form_objects`, `headers_footers`, `notes`, `references`, and `embedded_objects`. Use `list-header-footer` when you need a focused section-aware header/footer report with body/reference, `applyPageType`, text/table/picture/shape counts, and source XML part paths. Use `set-header-footer-text` for package-level replacement of an existing text anchor inside a header/footer body; it does not create new header/footer areas. Use `page-number-set` for COM-backed page number insertion, then verify with `scan-hwpx-features` and `validate-layout`. Use `list-fields --com` when you need package field/form rows and HWP COM field-list output in one report. `extract-form-map` includes those field/form signals in a separate `fields` section; press fields can be written in package mode with current-text validation, and checkboxes can be written with current-value validation. Radio/combo/edit/generic field entries remain inventory signals. Feature scan counts are inventory signals; they do not imply broad write/edit support.

For the supported startup R&D submission profile, use the dedicated profile command:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe fill-submission-template C:\temp\template.hwpx C:\temp\source.md C:\temp\submission-filled.hwpx --profile r-and-d-startup-2026 --asset-root C:\temp --image-mode package --report C:\temp\submission-filled-report.md
```

The profile defaults to `--markdown-table-mode render` and `--image-mode package`. Body Markdown tables are rendered as HWPX table objects cloned from the simplest unmerged top-level template table style. Use `--markdown-table-mode text` only when preserving the original HWPX table count matters more than table semantics. Supported Markdown image lines are inserted by the profile-specific package image writer by default: it embeds the original image file, writes `BinData`/`hp:pic`/manifest entries, computes display size from image DPI with a 96-DPI fallback, and scales down only when the natural 100% size exceeds the document body area. Use `--image-mode com` only when local HWP COM is healthy and editor-backed image insertion is required; use `--image-mode none` for text/table-only staging.

The fill report includes template/profile compatibility, table handling mode, rendered/converted table counts, rebuilt row changes, style-guard repairs, image write results, mapped/unmapped image counts, missing-target causes, and the sibling layout report. `validate-layout` classifies issues as `expected-change`, `review-needed`, or `blocking`; its exit code is nonzero only for blocking layout findings. `fill-submission-template` is stricter and returns nonzero when missing targets, unsupported/skipped targets, unmapped Markdown images, image insertion failures, or blocking layout findings remain.

Package text writes guard against inheriting tiny placeholder character styles: if a target run references a `charPr` below 7pt, it is moved to a commonly used normal-size character style. HWP COM cell text insertion also sets a 10pt normal character shape before `InsertText`. Package cell writes validate the extracted `currentText` by default; set `validateCurrentText="false"` on `writeText` only when a deliberate staged write should ignore the original text check.

Markdown 내용을 한 줄씩 `InsertText`로 문서 끝에 추가:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible append-markdown-lines C:\temp\template.hwpx C:\temp\input.md C:\temp\linewise-out.hwpx
```

Markdown table values can be inserted into existing HWPX table cells without recreating the table:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe markdown-table-list C:\temp\input.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-create-package C:\temp\template.hwpx C:\temp\new-table.hwpx --rows 2 --cols 3 --text "Header A|Header B|Header C;Value 1|Value 2|Value 3" --report C:\temp\new-table-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-row-package C:\temp\template.hwpx C:\temp\row-added.hwpx --table-index 4 --action add --row 1 --count 2 --text "R1C1|R1C2|R1C3|R1C4;R2C1|R2C2|R2C3|R2C4" --report C:\temp\row-added-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-column-package C:\temp\template.hwpx C:\temp\column-added.hwpx --table-index 4 --action add --column 1 --count 1 --text "HNEW;R1NEW;R2NEW" --report C:\temp\column-added-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-merge-package C:\temp\template.hwpx C:\temp\merged.hwpx --table-index 4 --row 1 --column 1 --row-span 2 --col-span 2 --text "Merged cell" --report C:\temp\merged-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-split-package C:\temp\merged.hwpx C:\temp\split.hwpx --table-index 4 --row 1 --column 1 --text "A|B;C|D" --report C:\temp\split-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-cell-style-package C:\temp\template.hwpx C:\temp\styled-cell.hwpx --table-index 4 --row 1 --column 1 --border-fill-id 32 --report C:\temp\styled-cell-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible fill-markdown-table C:\temp\template.hwpx C:\temp\input.md C:\temp\table-out.hwpx 8 3 1 0 1 2 5
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible table-cell-set C:\temp\template.hwpx C:\temp\cell-out.hwpx 3 1 1 "cell text"
```

Use `table-create-package` when a new simple table is needed without HWP COM. It clones the style of an existing unmerged top-level table, creates a `rows x cols` grid, inserts it at the document end by default, and can insert after an existing top-level paragraph with `--after-anchor`. `--text` and `--text-file` use `;` or newline between rows and `|` between cells; ignored or missing cells are reported. Use `--reference-table` for a specific top-level table style and `--border-fill-id` / `--header-border-fill-id` only when those border fill IDs are known from the template.
Use `table-row-package` for COM-free row add/delete on simple top-level text-cell tables. It rejects merged, nested, sparse, irregular-address, or object-containing tables, readdresses `cellAddr` values after mutation, and should be checked with `validate-layout --allow-table-row-change <index>` when row count changes are intentional. For add, `--row` means insert after that zero-based row; omit it to append after the last row. For delete, `--row` is the zero-based first row to delete.
Use `table-column-package` for COM-free column add/delete on the same safe text-cell table subset in `section0`. It changes table width by adding/removing cloned cell widths, so validate intentional changes with `validate-layout --allow-table-column-change <index>` using the validator's all-table index. For add, `--column` means insert after that zero-based column; omit it to append after the last column. For delete, `--column` is the zero-based first column to delete.
Use `table-merge-package` for COM-free rectangular cell merge in simple top-level text-cell tables in `section0`. It starts from an unmerged table, uses `--row` and `--column` as the zero-based top-left cell, preserves the table grid size, removes covered cells, and writes `cellSpan`/combined cell size on the top-left cell. If `--text` is omitted, text from the covered cells is joined in row-major order.
Use `table-split-package` for COM-free split of an existing merged top-left cell back into 1x1 cells. It preserves the table grid size, samples neighboring column widths and row heights when possible, keeps existing merged text in the top-left cell when `--text` is omitted, and can distribute replacement text with the same `;`/newline row and `|` cell delimiters.
Use `table-cell-style-package` to apply an existing `Contents/header.xml` borderFill ID to a cell or rectangular range. The selected range applies to all cells whose span intersects the zero-based `--row`/`--column` rectangle, so a covered coordinate inside a merged cell styles that merged top-left cell.

Use HWP COM-backed rich copy/paste when an existing reference document already has the table or control formatting you need:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible list-controls C:\temp\reference.hwpx C:\temp\reference-controls.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible probe-copy-from-doc C:\temp\reference.hwpx C:\temp\target.hwpx --source image:0 --target doc-end --report C:\temp\copy-probe.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible copy-from-doc C:\temp\reference.hwpx C:\temp\target.hwpx C:\temp\copied.hwpx --source image:0 --target doc-end --report C:\temp\copy-report.md
```

`copy-from-doc` currently supports rich source selection for the whole document, paragraph-to-end text blocks, whole tables, images exposed by HWP as graphical object controls, and generic controls (`all`, `paragraph-to-end:<text>`, `table:<index>`, `image:<index>`, or `control:<ctrlId>:<index>`). `image:<index>` is a source-only convenience selector for `gso` controls in tested HWPX files; use the `typeIndex` column from `list-controls`, not the global `index` column. `paragraph-to-end:<text>` selects from the paragraph containing the text through the document end, so use a heading or paragraph-leading marker when you need a clean block. Targets can be `doc-end`, `anchor:<text>`, `cell:<table,rowMove,colMove>`, or `control:<ctrlId>:<index>`. Cell targets use HWP movement-count selection from the first cell, not robust absolute grid addressing, so be careful with merged or irregular tables.

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe demo-list
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe demo-feature insertTable Rows=2 Cols=3 TreatAsChar=false --save C:\temp\table.hwpx
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe demo-feature pageNumbering DrawPos=5 SideChar=- --open C:\temp\table.hwpx --save C:\temp\table-numbered.hwpx
```

실제 한글 창을 띄운 상태로 테스트:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible copy-save C:\temp\in.hwpx C:\temp\out.hwpx
```

## 코드 사용 예시

```csharp
using OpenHwp.Automation;

[STAThread]
static void Main()
{
    using (var hwp = HwpSession.Create(visible: false))
    {
        hwp.ConfigureForAutomation();
        hwp.InsertText("hello");
        hwp.SaveAs(@"C:\temp\hello.hwpx");
    }
}
```

## 설계 메모

- 기본 제어 객체는 `HWPFrame.HwpObject`
- 액션 실행은 generic wrapper 제공
- 파일 열기 보안 모듈은 `RegisterModule("FilePathCheckDLL", "<registry value name>")` 방식 사용
- 자동화용 기본 설정은 `ConfigureForAutomation()`으로 묶음
- `ConfigureForAutomation()`은 보안 모듈 등록 시도 후 `SetMessageBoxMode(0x10)` 적용
- 읽기 기능은 `GetDocumentText()`, `GetPageText()`, `FieldExists()`, `GetFieldText()`, `GetFieldListRaw()` 제공
- CLI는 `x86`으로 빌드
- 라이브러리는 COM late binding 기반이라 타입 라이브러리 참조 없이 동작

## 제한 사항

- Windows 전용입니다.
- 설치된 한글 버전에 따라 일부 액션 이름/파라미터 이름이 달라질 수 있습니다.
- 숨김 상태 처리, 새 창/새 탭 동작은 버전별 차이가 있을 수 있습니다.
- 인프로세스 `HwpCtrl.ocx` 또는 `HwpAutomation.dll`까지 확장하려면 호스트를 `x86`으로 맞추는 편이 안전합니다.
