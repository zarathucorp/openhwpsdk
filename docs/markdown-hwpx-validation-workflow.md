# Markdown to HWPX validation workflow

## Test files

Use files under `test/` for conversion tests.

- Template HWPX: `test/[붙임 8] 제출서식_차라투_v1.0.hwpx`
- Source Markdown: `test/제출서식_차라투_R&D수행창업기업_v0.3.md`
- Test outputs: `test/out/`

## Required checks

Page count is not a pass/fail signal. A filled application can naturally have more pages than the blank template.

Use this order instead:

1. Create the candidate HWPX.
2. Run layout validation against the template HWPX.
3. Export both template and candidate to PDF.
4. Inspect the PDFs for visual form preservation and content placement.

## Commands

Build the CLI:

```bat
build.cmd Release
```

Create a candidate:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe markdown-to-hwpx test\제출서식_차라투_R&D수행창업기업_v0.3.md test\[붙임 8] 제출서식_차라투_v1.0.hwpx test\out\markdown_import_current.hwpx
```

Run structural layout validation:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe validate-layout test\[붙임 8] 제출서식_차라투_v1.0.hwpx test\out\markdown_import_current.hwpx test\out\markdown_import_current_layout_report.md
```

Export PDFs for visual inspection:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible export-pdf test\[붙임 8] 제출서식_차라투_v1.0.hwpx test\out\template_original.pdf
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible export-pdf test\out\markdown_import_current.hwpx test\out\markdown_import_current.pdf
```

For table-aware insertion, parse Markdown tables and fill an existing HWPX table cell by cell:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe markdown-table-list test\제출서식_차라투_R&D수행창업기업_v0.3.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible fill-markdown-table test\[붙임 8] 제출서식_차라투_v1.0.hwpx test\제출서식_차라투_R&D수행창업기업_v0.3.md test\out\cell_fill_education_2rows.hwpx 8 3 1 0 1 2 5
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe validate-layout test\[붙임 8] 제출서식_차라투_v1.0.hwpx test\out\cell_fill_education_2rows.hwpx test\out\cell_fill_education_2rows_layout_report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible export-pdf test\out\cell_fill_education_2rows.hwpx test\out\cell_fill_education_2rows.pdf
```

## Acceptance criteria

- `validate-layout` must exit with `0`.
- Existing template tables must not be replaced by newly generated generic tables.
- Table column count, table width, border fill, and leading labels should remain stable unless a specific table is intentionally expanded.
- Leading paragraph style drift should remain low.
- PDF inspection should confirm that the official form appearance is preserved; page growth alone is acceptable and should not be treated as failure.

## Current failed baseline

The current `markdown-to-hwpx` implementation rewrites the section into new paragraphs and new generic tables. It fails layout validation because the official template tables are replaced rather than filled in place.
