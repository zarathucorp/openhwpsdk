# Windows CLI Recipes

## Build And Locate

```powershell
Set-Location -LiteralPath 'C:\Users\ZARATHU11\codex\openhwpsdk'
.\build.cmd Release
$cli = Join-Path (Get-Location) 'src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe'
```

The project targets .NET Framework 4.0 and x86 because Hancom HWP automation is COM-based.

## COM Diagnosis

```powershell
& $cli --visible diagnose-com
& $cli --visible diagnose-com '<input.hwpx>'
```

If HWP automation hangs, retry with a clear timeout while visible:

```powershell
& $cli --visible --com-timeout-ms 120000 diagnose-com '<input.hwpx>'
```

If a file acts read-only, inspect process locks separately from file attributes:

```powershell
Get-Process Hwp -ErrorAction SilentlyContinue
Get-Item -LiteralPath '<input.hwpx>' | Select-Object FullName, IsReadOnly
```

## Read And Export

```powershell
& $cli doc-info '<input.hwp>'
& $cli read-text '<input.hwp>' 'test\out\input_text.txt'
& $cli read-page '<input.hwp>' 1 'test\out\page_001.txt'
& $cli --visible export-pdf '<input.hwpx>' 'test\out\input.pdf'
```

Prefer file output for Korean text because terminal rendering can be misleading.

## Field And Text Replacement

```powershell
& $cli field-list-raw '<form.hwp>'
& $cli field-exists '<form.hwp>' 'FIELD_NAME'
& $cli field-get '<form.hwp>' 'FIELD_NAME'
& $cli --visible field-set '<form.hwpx>' 'FIELD_NAME' 'updated text' 'test\out\form-out.hwpx'
```

For plain text replacement:

```powershell
& $cli --visible replace-text '<input.hwpx>' 'old text' 'new text' 'test\out\replaced.hwpx'
& $cli --visible replace-text-batch '<input.hwpx>' 'test\out\replaced.hwpx' 'old1' 'new1' 'old2' 'new2'
```

## Form Map Workflow

Create a whole-package map:

```powershell
& $cli extract-form-map '<template.hwpx>' 'test\out\template_form_map.xml'
```

Edit only `writeText` or `writeImage` entries in the generated XML. Do not rewrite the template package from scratch.

Probe every mapped position before HWP-backed writing:

```powershell
& $cli --visible probe-form-map '<template.hwpx>' 'test\out\template_form_map.xml' 'test\out\template_form_map_probe_all.md'
```

Apply through HWP automation when editor-backed behavior matters:

```powershell
& $cli --visible apply-form-map '<template.hwpx>' 'test\out\template_form_map_filled.xml' 'test\out\template_form_map_applied.hwpx' --report 'test\out\template_form_map_apply.md'
```

Use package mode for COM-free text writes and package-level image embedding when editor-backed behavior is not required. Package anchor writes are applied from later paragraphs toward earlier paragraphs to reduce repeated-anchor drift.

```powershell
& $cli apply-form-map --package '<template.hwpx>' 'test\out\template_form_map_filled.xml' 'test\out\template_form_map_package.hwpx' --report 'test\out\template_form_map_package_apply.md'
```

When `--report` is supplied, both COM and package mode write attempted/applied/failed/skipped rows. Package mode writes layout validation to a sibling `*.layout.md` report.

## Submission Profile

Use the dedicated profile for the supported startup R&D form:

```powershell
& $cli fill-submission-template '<template.hwpx>' '<source.md>' 'test\out\submission_filled.hwpx' --profile r-and-d-startup-2026 --asset-root '<image-root>' --image-mode package --report 'test\out\submission_filled_report.md'
```

Current profile behavior:

- Supported Markdown body tables are rendered as HWPX table objects by default. Use `--markdown-table-mode text` only when preserving the original HWPX table count matters more than table semantics.
- Supported Markdown image lines become temporary text anchors and are inserted by default with package-level `BinData`/`hp:pic` updates. Use `--image-mode com` only when local HWP COM is healthy and editor-backed insertion is required.
- The report lists template/profile compatibility, total Markdown tables/images, table handling mode, rendered/converted table counts, configured asset roots, resolved image paths, missing image candidate paths, image anchors queued, image writes applied/failed/pending, and image references not mapped by the profile.
- Package text writes guard against tiny placeholder styles by replacing sub-7pt `charPr` references on written runs. HWP COM table-cell writes set 10pt before `InsertText`.
- Package cell writes validate extracted `currentText` by default. Set `validateCurrentText="false"` on `writeText` only when a staged write deliberately targets already-changed text.

Then validate:

```powershell
& $cli validate-layout '<template.hwpx>' 'test\out\submission_filled.hwpx' 'test\out\submission_filled_layout.md' --allow-table-row-change 10
& $cli validate-content 'test\out\submission_filled.hwpx' 'test\out\submission_filled_content.md' --require 'required text'
```

Layout reports classify findings as `expected-change`, `review-needed`, or `blocking` and include a one-line verdict.

## Feature Coverage

Scan a file or corpus to see which HWPX authoring features are present:

```powershell
& $cli scan-hwpx-features 'test' 'test\out\hwpx_feature_scan.md'
```

Regenerate and scan the tracked feature fixtures:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\New-HwpxFeatureFixtures.ps1
& $cli scan-hwpx-features 'test\corpus\features' 'test\out\hwpx_feature_scan_features.md'
```

The scan report includes aggregate counts, authoring coverage, detailed feature groups, missing corpus signals, per-file totals, and inventory tables for header/footer, field/form, reference, and note signals. Treat these as corpus evidence only; writing/editing support must be verified separately.

## Markdown Tables Into Existing HWPX Tables

List Markdown tables:

```powershell
& $cli markdown-table-list '<source.md>'
```

Fill an existing HWP table without recreating the table:

```powershell
& $cli --visible fill-markdown-table '<template.hwpx>' '<source.md>' 'test\out\table-out.hwpx' <markdownTableIndex> <hwpTableIndex> [startRow] [startCol] [skipMarkdownRows] [maxRows] [maxCols]
```

Set one cell:

```powershell
& $cli --visible table-cell-set '<template.hwpx>' 'test\out\cell-out.hwpx' <tableIndex> <rowMoveCount> <columnMoveCount> 'cell text'
```

Merged tables can make row/column movement misleading. Verify with map/probe evidence or a visible HWP check before relying on a cell coordinate.

## Rich Copy/Paste From Reference Documents

Inspect controls in the reference document:

```powershell
& $cli --visible list-controls '<reference.hwpx>' 'test\out\reference_controls.md'
```

Probe before mutating the target:

```powershell
& $cli --visible probe-copy-from-doc '<reference.hwpx>' '<target.hwpx>' --source table:0 --target doc-end --report 'test\out\copy_probe.md'
```

Copy through HWP's editor-backed clipboard path:

```powershell
& $cli --visible copy-from-doc '<reference.hwpx>' '<target.hwpx>' 'test\out\copy_from_doc.hwpx' --source table:0 --target doc-end --report 'test\out\copy_from_doc.md'
```

Supported source selectors are `table:<index>` and `control:<ctrlId>:<index>`. Supported target selectors are `doc-end`, `anchor:<text>`, `cell:<table,rowMove,colMove>`, and `control:<ctrlId>:<index>`. Cell targets use HWP movement-count selection from the first cell, not robust absolute grid addressing. Source text ranges and image-specific selectors are not implemented yet. Validate copied output with `scan-hwpx-features`, `validate-layout`, and visual/PDF checks when placement matters.

## Acceptance Checklist

- `validate-layout` exits `0`.
- `validate-content` has no failures; warnings are reviewed.
- Existing template tables are not replaced by generic generated tables.
- Table count, column count, widths, border fills, and leading labels stay stable unless intentionally changed.
- Leading paragraph style drift is low.
- PDF inspection confirms the official form appearance is preserved.

## Common Failures

- `MSBuild.exe was not found`: install Visual Studio Build Tools or .NET Framework MSBuild, then rerun `build.cmd Release`.
- COM registration fails: confirm Hancom HWP is installed and run `--visible diagnose-com`.
- Hidden-mode COM hangs: rerun the operation with `--visible` and `--com-timeout-ms`.
- COM hangs at `Create HWP COM instance`: inspect `Get-Process Hwp`; ask the user to save and close HWP windows before retrying. Do not force-kill a possible user-owned HWP process unless explicitly approved after the data-loss risk is clear.
- Korean paths break in helper scripts: discover files with PowerShell and `-LiteralPath` instead of hardcoding paths into Python.
- Console Korean text looks wrong: write CLI output to a file and inspect the file.
- A generated HWPX opens but layout drifted: go back to map/probe/apply and validate layout; do not accept a direct reconstruction path.
