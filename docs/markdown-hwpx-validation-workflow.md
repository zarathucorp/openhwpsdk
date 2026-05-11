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
- `apply-form-map --package` for COM-free package text/image writes
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
- a separate `fields` section for named fields, press fields, and form objects such as checkbox, radio, combo, and edit controls
- empty `writeText` and `writeImage` elements for the caller to fill

Before HWP-backed writes, probe every mapped position without changing the document:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible probe-form-map "<template.hwpx>" "test\out\template_form_map.xml" "test\out\template_form_map_probe_all.md"
```

The probe checks every table cell by selecting it and every paragraph anchor by finding it in HWP. If a long anchor cannot be found exactly, it tries a shorter heading prefix and records that as `fallback search` in the report.

Apply the filled map through HWP automation when editor-backed behavior is required:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible apply-form-map "<template.hwpx>" "test\out\template_form_map_filled.xml" "test\out\template_form_map_applied.hwpx"
```

Add `--report "<report.md>"` to write attempted/applied/failed/skipped details for HWP COM writes, including `writeImage` operations.

For COM-free map writes, use package mode. This preserves the original HWPX package entries, applies XML text changes, can embed `writeImage` paths as package-level `BinData`/`hp:pic` objects, and runs layout validation after writing. The layout report is written as `*.layout.md` next to the apply report.

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

Press-field entries in the generated `fields` section can be applied by `apply-form-map --package`: set the field's `writeText enabled="true"` value, keep `validateCurrentText="true"` unless the source was deliberately staged, and verify with `list-fields`, `validate-content`, and `validate-layout`. Checkbox entries can be applied with `writeValue enabled="true"` and a value of `CHECKED` or `UNCHECKED`; keep `validateCurrentValue="true"` unless the source was deliberately staged. Radio, combo, edit, and generic field markers are still inventory-only and will be skipped if a write is requested.

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

Package-mode image insertion embeds the referenced file, updates `content.hpf`, allocates image/object ids, and scales natural image size down to the document body area when needed. Width/height values above `1000` are treated as explicit HWPX units; smaller legacy COM-style defaults use natural image sizing.

## Required Checks

Page count is not a pass/fail signal. A filled application can naturally have more pages than the blank template.

Use this order instead:

1. Scan the corpus or candidate with `scan-hwpx-features` when the question is feature coverage.
2. Create the candidate HWPX through profile, package, cell, or cursor-style input.
3. Run layout validation against the template HWPX.
4. Run content validation for Markdown artifacts, unresolved placeholders, required strings, and possible overflows.
5. Export both template and candidate to PDF when visual checking is needed.
6. Render or inspect representative PDF pages.

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
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-create-package "<template.hwpx>" "test\out\new_table.hwpx" --rows 2 --cols 3 --text "Header A|Header B|Header C;Value 1|Value 2|Value 3" --report "test\out\new_table_report.md"
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-row-package "<template.hwpx>" "test\out\row_added.hwpx" --table-index 4 --action add --row 1 --count 2 --text "R1C1|R1C2|R1C3|R1C4;R2C1|R2C2|R2C3|R2C4" --report "test\out\row_added_report.md"
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-column-package "<template.hwpx>" "test\out\column_added.hwpx" --table-index 4 --action add --column 1 --count 1 --text "HNEW;R1NEW;R2NEW" --report "test\out\column_added_report.md"
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-merge-package "<template.hwpx>" "test\out\table_merged.hwpx" --table-index 4 --row 1 --column 1 --row-span 2 --col-span 2 --text "Merged cell" --report "test\out\table_merged_report.md"
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-split-package "test\out\table_merged.hwpx" "test\out\table_split.hwpx" --table-index 4 --row 1 --column 1 --text "A|B;C|D" --report "test\out\table_split_report.md"
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-cell-style-package "<template.hwpx>" "test\out\styled_cell.hwpx" --table-index 4 --row 1 --column 1 --border-fill-id 32 --report "test\out\styled_cell_report.md"
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-cell-align-package "<template.hwpx>" "test\out\aligned_cell.hwpx" --table-index 4 --row 1 --column 1 --horizontal right --vertical bottom --report "test\out\aligned_cell_report.md"
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-cell-diagonal-package "<template.hwpx>" "test\out\diagonal_cell.hwpx" --table-index 4 --row 1 --column 1 --direction both --width "0.15 mm" --color "#000000" --report "test\out\diagonal_cell_report.md"
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible fill-markdown-table "<template.hwpx>" "<source.md>" "test\out\cell_fill_education_2rows.hwpx" 8 3 1 0 1 2 5
```

`table-create-package` is the COM-free path for creating a new simple table. It needs an existing unmerged top-level table in the source HWPX so it can clone stable table, cell, paragraph, and border defaults; it fails instead of guessing when no safe reference table exists. `--after-anchor` only targets top-level body paragraphs, and text input reports ignored or missing cells when the source matrix does not match `--rows` x `--cols`. Validate with content requirements for inserted text and `validate-layout` to confirm the expected table count increase does not disturb existing tables.
`table-row-package` adds or deletes rows in an existing simple top-level text-cell table. It rejects merged, nested, sparse, irregular-address, or object-containing tables and re-normalizes row/cell addresses; use `validate-layout --allow-table-row-change <index>` to classify the intended row-count change as expected. For add, `--row` means insert after that zero-based row; for delete, `--row` is the first zero-based row to delete.
`table-column-package` adds or deletes columns in the same simple text-cell table subset in `section0` and re-normalizes cell addresses. Column operations intentionally change column count and table width, so use `validate-layout --allow-table-column-change <index>` with the validator's all-table index when that change is expected. For add, `--column` means insert after that zero-based column; for delete, `--column` is the first zero-based column to delete.
`table-merge-package` merges a rectangular region in a simple unmerged top-level text-cell table in `section0`. `--row` and `--column` are the zero-based top-left cell, `--row-span` and `--col-span` define the rectangle, and omitted `--text` combines the covered cell text in row-major order. Confirm the result with content validation plus direct XML/grid inspection because the table row and column counts intentionally stay unchanged.
`table-split-package` splits an existing merged top-left cell back into 1x1 cells in the same safe `section0` subset. It preserves row/column counts, inserts the covered cells, samples surrounding sizes when available, and leaves the original merged text in the top-left cell unless `--text` supplies a replacement matrix. Confirm with content validation plus direct XML/grid inspection because layout validation may not flag merge/split shape changes when row and column counts are unchanged.
`table-cell-style-package` applies an existing `borderFillIDRef` from `Contents/header.xml` to one cell or a rectangular range. It validates that the border fill ID exists before writing, applies to intersecting merged cells, and should be checked with direct XML/grid inspection because structural layout counts remain unchanged.
`table-cell-align-package` applies horizontal paragraph alignment and/or cell vertical alignment to one cell or a rectangular range. Horizontal alignment creates a cloned `paraPr` in `Contents/header.xml` instead of mutating the shared source style, then retargets affected direct cell paragraphs; vertical alignment updates `hp:subList@vertAlign`. Validate with direct XML inspection of both `section0.xml` and `header.xml`.
`table-cell-diagonal-package` adds, changes, or clears cell diagonal lines by cloning the affected cells' current `borderFill` definitions, updating `hh:slash` and `hh:backSlash`, and retargeting only the selected cells. `--direction slash|backslash|both|none` maps to `CENTER` or `NONE`, while `--width` and `--color` override the cloned `hh:diagonal` values. Without `--base-border-fill-id`, each affected cell keeps its own existing border/fill defaults. Validate with direct XML inspection of `section0.xml` cell `borderFillIDRef` values and `header.xml` borderFill entries.

Copy a whole table or control from a reference HWP/HWPX document through HWP's editor-backed clipboard path:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible list-controls "<reference.hwpx>" "test\out\reference_controls.md"
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible probe-copy-from-doc "<reference.hwpx>" "<target.hwpx>" --source image:0 --target doc-end --report "test\out\copy_probe.md"
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible copy-from-doc "<reference.hwpx>" "<target.hwpx>" "test\out\copy_from_doc.hwpx" --source image:0 --target doc-end --report "test\out\copy_from_doc.md"
```

Use `probe-copy-from-doc` before mutation. Source selectors support `all`, `paragraph-to-end:<text>`, `table:<index>`, `image:<index>`, and `control:<ctrlId>:<index>`. `image:<index>` maps to `gso` controls in tested HWPX files; use the `typeIndex` column from `list-controls`, not the global `index` column. `paragraph-to-end:<text>` selects from the paragraph containing the text through the document end. Target selectors can use `doc-end`, `anchor:<text>`, `cell:<table,rowMove,colMove>`, or `control:<ctrlId>:<index>`. Cell targets use HWP movement-count selection from the first cell, not robust absolute grid addressing. After a copy, run `scan-hwpx-features`, `validate-layout`, and PDF export when visual placement matters.

Run structural layout validation:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe validate-layout "<template.hwpx>" "test\out\cell_fill_education_2rows.hwpx" "test\out\cell_fill_education_2rows_layout_report.md"
```

Run content validation:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe validate-content "test\out\submission_filled.hwpx" "test\out\submission_filled_content_report.md" --require "required text"
```

Scan HWPX feature coverage:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe scan-hwpx-features "test" "test\out\hwpx_feature_scan.md"
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe list-header-footer "test\corpus\features\header-footer.hwpx" "test\out\header_footer_inventory.md"
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe set-header-footer-text "test\corpus\features\header-footer.hwpx" "test\out\header_footer_text_write.hwpx" --kind header --section section0 --anchor "Header fixture" --text "Updated Header Fixture" --report "test\out\header_footer_text_write.md"
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible page-number-set "<template.hwpx>" "test\out\page_numbered.hwpx" --draw-pos 5 --side-char "-" --report "test\out\page_numbered.md"
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible list-fields "<template.hwpx>" "test\out\field_inventory.md" --com
```

Regenerate and scan the tracked authoring feature corpus:

```bat
powershell -NoProfile -ExecutionPolicy Bypass -File tools\New-HwpxFeatureFixtures.ps1
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe scan-hwpx-features "test\corpus\features" "test\out\hwpx_feature_scan_features.md"
```

The feature scan report separates corpus evidence from implementation support. It includes aggregate counts, authoring coverage, detailed feature groups, missing corpus signals, per-file totals, and inventory tables for:

- header/footer bodies and references
- field/form objects and named field signals
- bookmarks, captions, hyperlinks, cross references, TOC/index markers, page/auto numbers
- footnotes, endnotes, memos, and comments

Use `Missing Corpus Signals` to decide which fixture is absent. Use `list-header-footer` for a focused section-aware header/footer inventory when placement, body/reference split, `applyPageType`, or text/table/picture/shape counts matter. Use `set-header-footer-text` for package-level replacement of an existing text anchor inside a header/footer body, then rerun `list-header-footer`, `validate-content`, and `validate-layout`. Use `page-number-set` for COM-backed page number insertion and confirm `pageNumbers` via `scan-hwpx-features`. Use `list-fields --com` to merge package field/form rows with HWP COM field-list output where the document opens in HWP. Use `extract-form-map` when field/form signals need to be reviewed alongside cell and anchor targets; press fields and checkboxes are package-write targets, while other field/form entries are inventory-only. Do not treat a nonzero count as broad write support; the scan proves only that the HWPX package contains the feature.

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
