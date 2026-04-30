# Markdown Form Entry Validation Workflow

## Test files

Use files under `test/` for conversion and form-entry tests.

- Template HWPX: `test/[붙임 8] 제출서식_차라투_v1.0.hwpx`
- Source Markdown: `test/제출서식_차라투_R&D수행창업기업_v0.3.md`
- Test outputs: `test/out/`

## Direction

Do not use a text-only Markdown import to fill an existing official form template.

`markdown-to-hwpx` is intentionally not exposed right now. A command with that name must render Markdown as HWP structure and styles: headings as headings, Markdown tables as HWP tables, and inline marks such as bold/code as styled text.

Existing HWPX forms must be handled like a person using the editor: select or move to the target place, write text, and verify that the original table/style layout did not change.

For existing forms, use cell/cursor-style commands such as:

- `extract-form-map`
- `apply-form-map`
- `markdown-table-list`
- `table-cell-set`
- `fill-markdown-table`
- future cursor movement/input commands

## Template form map workflow

When a template HWPX exists, inspect the actual package XML first and create a write map:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe extract-form-map test\[붙임 8] 제출서식_차라투_v1.0.hwpx test\out\template_form_map.xml
```

The generated XML is intentionally smaller than the source HWPX package, but it maps the whole document package before exposing write targets:

- package entries, rootfiles, `content.hpf` metadata, manifest, and spine order
- XML part roles such as `contentPackage`, `documentHead`, `bodySection`, `settings`, and `packageMeta`
- XML part counts for paragraphs, tables, cells, text runs, and header reference definitions
- table id, HWP table index, part path, table size, border id, and merged-cell flag
- cell id, row/column address, row/column span, width/height, style references, border id, and current text
- paragraph anchors outside tables with part path, paragraph index, style references, duplicate occurrence, and current text
- empty `writeText` and `writeImage` elements for the caller to fill

Fill the map, then apply it one operation at a time through HWP automation:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible apply-form-map test\[붙임 8] 제출서식_차라투_v1.0.hwpx test\out\template_form_map_filled.xml test\out\template_form_map_applied.hwpx
```

Before applying writes, probe every mapped position without changing the document:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible probe-form-map test\[붙임 8] 제출서식_차라투_v1.0.hwpx test\out\template_form_map.xml test\out\template_form_map_probe_all.md
```

The probe checks every table cell by selecting it and every paragraph anchor by finding it in HWP. If a long anchor cannot be found exactly, it tries a shorter heading prefix and records that as `fallback search` in the report.

Example cell write:

```xml
<cell id="table-003-r001-c001" tableIndex="3" row="1" col="1" merged="false">
  <currentText>학교명</currentText>
  <writeText>서울대학교 보건대학원</writeText>
  <writeImage path="" width="200" height="200" clearCell="true" />
</cell>
```

Example cell image insertion, using a path relative to the map XML:

```xml
<writeImage path="sample_insert.png" width="40" height="20" clearCell="true" />
```

Paragraph anchor text is replaced by default when its `writeText` is filled:

```xml
<anchor id="anchor-0001" partPath="Contents/section0.xml" partRole="bodySection" paragraphIndex="1" occurrence="0">
  <currentText>기존 문단 텍스트</currentText>
  <writeText replaceAnchorText="true">새 문단 텍스트</writeText>
</anchor>
```

Merged cells are skipped by default when `writeText` or `writeImage` is present. Add `force="true"` to the `cell` element only after visual verification confirms the target is safe.

## Required Checks

Page count is not a pass/fail signal. A filled application can naturally have more pages than the blank template.

Use this order instead:

1. Create the candidate HWPX through cell/cursor-style input.
2. Run layout validation against the template HWPX.
3. Export both template and candidate to PDF when visual checking is needed.
4. Render or inspect representative PDF pages.

## Commands

Build the CLI:

```bat
build.cmd Release
```

List Markdown tables:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe markdown-table-list test\제출서식_차라투_R&D수행창업기업_v0.3.md
```

Fill an existing HWPX table without recreating it:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible fill-markdown-table test\[붙임 8] 제출서식_차라투_v1.0.hwpx test\제출서식_차라투_R&D수행창업기업_v0.3.md test\out\cell_fill_education_2rows.hwpx 8 3 1 0 1 2 5
```

Run structural layout validation:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe validate-layout test\[붙임 8] 제출서식_차라투_v1.0.hwpx test\out\cell_fill_education_2rows.hwpx test\out\cell_fill_education_2rows_layout_report.md
```

Export PDFs for visual inspection:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible export-pdf test\[붙임 8] 제출서식_차라투_v1.0.hwpx test\out\template_original.pdf
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible export-pdf test\out\cell_fill_education_2rows.hwpx test\out\cell_fill_education_2rows.pdf
```

## Acceptance Criteria

- `validate-layout` must exit with `0`.
- Existing template tables must not be replaced by newly generated generic tables.
- Table column count, table width, border fill, and leading labels should remain stable unless a specific table is intentionally expanded.
- Leading paragraph style drift should remain low.
- PDF inspection should confirm that the official form appearance is preserved; page growth alone is acceptable and should not be treated as failure.

## Removed Direction

The old template-based `markdown-to-hwpx <markdown> <template> <output>` direction was removed. It rewrote official template tables into new generic tables, so it does not match a human-like form-entry workflow.
