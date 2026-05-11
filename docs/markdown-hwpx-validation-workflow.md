# Markdown Form Entry Validation Workflow

## Test Files

Use files under `test/` for conversion and form-entry tests:

- Template HWPX: the submission template `.hwpx` file directly under `test/`.
- Source Markdown: the submission draft `.md` file directly under `test/`.
- Test outputs: `test/out/`.

The real test filenames contain Korean text, spaces, brackets, and `&`. Always quote those paths in CLI examples. In PowerShell discovery commands, prefer `-LiteralPath`.

## Direction

Do not use a text-only Markdown import to fill an existing official form template.

`markdown-to-hwpx` is intentionally not exposed right now. A command with that name must render Markdown as HWP structure and styles: headings as headings, Markdown tables as HWP tables, and inline marks such as bold/code as styled text.

Existing HWPX forms should be handled like a person using the editor: select or move to the target place, write text, and verify that the original table/style layout did not change.

For existing forms, use these commands as appropriate:

- `extract-form-map`
- `probe-form-map`
- `apply-form-map`
- `apply-form-map --package` for text-only package writes
- `fill-submission-template` for the supported `r-and-d-startup-2026` profile
- `markdown-table-list`
- `table-cell-set`
- `fill-markdown-table`
- `validate-layout`
- `validate-content`

## Template Form Map Workflow

When a template HWPX exists, inspect the actual package XML first and create a write map:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe extract-form-map "<template.hwpx>" "test\out\template_form_map.xml"
```

The generated XML is intentionally smaller than the source HWPX package, but it maps the whole document package before exposing write targets:

- package entries, rootfiles, `content.hpf` metadata, manifest, and spine order
- XML part roles such as `contentPackage`, `documentHead`, `bodySection`, `settings`, and `packageMeta`
- XML part counts for paragraphs, tables, cells, text runs, and header reference definitions
- table id, HWP table index, part path, table size, border id, and merged-cell flag
- cell id, row/column address, row/column span, width/height, style references, border id, and current text
- paragraph anchors outside tables with part path, paragraph index, style references, duplicate occurrence, and current text
- empty `writeText` and `writeImage` elements for the caller to fill

Before HWP-backed writes, probe every mapped position without changing the document:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible probe-form-map "<template.hwpx>" "test\out\template_form_map.xml" "test\out\template_form_map_probe_all.md"
```

The probe checks every table cell by selecting it and every paragraph anchor by finding it in HWP. If a long anchor cannot be found exactly, it tries a shorter heading prefix and records that as `fallback search` in the report.

Apply the filled map through HWP automation when image insertion or editor-backed behavior is required:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible apply-form-map "<template.hwpx>" "test\out\template_form_map_filled.xml" "test\out\template_form_map_applied.hwpx"
```

Add `--report "<report.md>"` to write attempted/applied/failed/skipped details for HWP COM writes, including `writeImage` operations.

For text-only map writes, use package mode. This preserves the original HWPX package entries, applies XML text changes without COM, and runs layout validation after writing. Package mode reports skipped image writes instead of trying to inject BinData directly; the layout report is written as `*.layout.md` next to the apply report.

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe apply-form-map --package "<template.hwpx>" "test\out\template_form_map_filled.xml" "test\out\template_form_map_package_applied.hwpx" --report "test\out\template_form_map_package_apply.md"
```

For the current submission form, use the dedicated profile command instead of hand-editing every map entry:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe fill-submission-template "<template.hwpx>" "<source.md>" "test\out\submission_filled.hwpx" --profile r-and-d-startup-2026 --asset-root "<image-root>" --image-mode package --report "test\out\submission_filled_report.md"
```

The submission profile queues supported Markdown image lines as text anchors, then inserts images with package-level `BinData`/`hp:pic` updates by default. Package image fallback embeds the original image file but computes the displayed object size from the image DPI, falling back to 96 DPI, and scales down only when the natural 100% size would exceed the document body area. This follows the HWP picture insertion behavior of preserving 100% size by default while avoiding body-width overflow. Use `--image-mode com` only when the local HWP COM session is known to be healthy and editor-backed insertion is required. Body Markdown tables default to rendered HWPX table objects using the simplest unmerged top-level table style available in the template; use `--markdown-table-mode text` only when preserving the original table count matters more than table semantics. The report lists template/profile compatibility, configured asset roots, rebuilt table row changes, style-guard repairs, resolved image paths, candidate paths for missing image files, pending image anchors, and unmapped image references.

Form-map table extraction is merge/nested-table aware. Tables expose `gridRows` and `gridCols` computed from `cellAddr` plus `cellSpan`, and each cell's `currentText` contains only direct cell paragraph text. Text from nested tables is separated into `nestedTableText` so parent-cell matching and package writes do not accidentally treat an inner form table as parent-cell body text. When the submission profile rebuilds data rows, it clones the full row group covered by the template row's `rowSpan`; for example, a visible record that spans two XML rows is copied as a two-row unit, not as a single row with dangling spans. Profile projections can target a specific row offset inside that row group, so split headers such as `주관기관명` over `연구개발기관명 및 역할(주관/공동)` are filled into the matching upper and lower cells.

Package text writes guard against inheriting tiny placeholder character styles. Runs with `charPr` height below 7pt are moved to a normal-size character style, and HWP COM table-cell insertion sets 10pt before `InsertText`. The submission profile applies the same guard to its package writes, so a profile constant that points to a tiny template-specific `charPr` is replaced by a normal body style. Package cell writes also validate `currentText` by default; set `validateCurrentText="false"` only for deliberate staged rewrites where the original text is expected to differ.

Layout reports classify findings as `expected-change`, `review-needed`, or `blocking`. Any table row-count change is listed, and overlapping `cellSpan` coverage is blocking because it indicates a visually corrupted merged-cell grid. Intentional row expansion can be allowlisted by table index:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe validate-layout "<template.hwpx>" "test\out\submission_filled.hwpx" "test\out\submission_filled_layout.md" --allow-table-row-change 10
```

Package-mode anchor writes are applied from later paragraphs toward earlier paragraphs to reduce repeated-anchor drift when identical bullet text appears many times.

Example cell write:

```xml
<cell id="table-003-r001-c001" tableIndex="3" row="1" col="1" merged="false">
  <currentText>Existing cell text</currentText>
  <writeText>Replacement cell text</writeText>
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
  <currentText>Existing paragraph text</currentText>
  <writeText replaceAnchorText="true">Replacement paragraph text</writeText>
</anchor>
```

Merged cells are skipped by default when `writeText` or `writeImage` is present. Add `force="true"` to the `cell` element only after visual verification confirms the target is safe.

## Required Checks

Page count is not a pass/fail signal. A filled application can naturally have more pages than the blank template.

Use this order instead:

1. Create the candidate HWPX through profile, package, cell, or cursor-style input.
2. Run layout validation against the template HWPX.
3. Run content validation for Markdown artifacts, unresolved placeholders, required strings, and possible overflows.
4. Export both template and candidate to PDF when visual checking is needed.
5. Render or inspect representative PDF pages.

`validate-layout` exits with `0` when there are no `blocking` findings. A `review-needed` verdict still requires human review, especially for intentional row growth, inserted Markdown tables, image placement, and section-level visual fit. `fill-submission-template` is stricter than `validate-layout`: it returns nonzero when missing targets, skipped or unsupported writes, unmapped Markdown images, failed image writes, or blocking layout findings remain.

## Commands

Build the CLI:

```bat
build.cmd Release
```

List Markdown tables:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe markdown-table-list "<source.md>"
```

Fill an existing HWPX table without recreating it:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible fill-markdown-table "<template.hwpx>" "<source.md>" "test\out\cell_fill_education_2rows.hwpx" 8 3 1 0 1 2 5
```

Run structural layout validation:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe validate-layout "<template.hwpx>" "test\out\cell_fill_education_2rows.hwpx" "test\out\cell_fill_education_2rows_layout_report.md"
```

Run content validation:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe validate-content "test\out\submission_filled.hwpx" "test\out\submission_filled_content_report.md" --require "required text"
```

Export PDFs for visual inspection:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible export-pdf "<template.hwpx>" "test\out\template_original.pdf"
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible export-pdf "test\out\cell_fill_education_2rows.hwpx" "test\out\cell_fill_education_2rows.pdf"
```

## Acceptance Criteria

- `validate-layout` must have no `blocking` findings; `review-needed` findings must be reviewed rather than treated as a clean pass.
- `validate-content` should have no failures; warnings must be reviewed.
- Existing template tables must not be replaced by newly generated generic tables.
- Table column count, table width, border fill, and leading labels should remain stable unless a specific table is intentionally expanded.
- Leading paragraph style drift should remain low.
- PDF inspection should confirm that the official form appearance is preserved; page growth alone is acceptable and should not be treated as failure.

## Removed Direction

The old template-based `markdown-to-hwpx <markdown> <template> <output>` direction was removed. It rewrote official template tables into new generic tables, so it does not match a human-like form-entry workflow.
