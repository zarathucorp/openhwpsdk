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

Apply through HWP automation when image insertion or editor-backed behavior matters:

```powershell
& $cli --visible apply-form-map '<template.hwpx>' 'test\out\template_form_map_filled.xml' 'test\out\template_form_map_applied.hwpx' --report 'test\out\template_form_map_apply.md'
```

Use package mode only for text-only writes. Package mode reports `writeImage` entries as skipped and returns nonzero when such writes are present; use HWP COM mode for image insertion.

```powershell
& $cli apply-form-map --package '<template.hwpx>' 'test\out\template_form_map_filled.xml' 'test\out\template_form_map_package.hwpx' --report 'test\out\template_form_map_package_apply.md'
```

When `--report` is supplied, both COM and package mode write attempted/applied/failed/skipped rows. Package mode writes layout validation to a sibling `*.layout.md` report.

## Submission Profile

Use the dedicated profile for the supported startup R&D form:

```powershell
& $cli fill-submission-template '<template.hwpx>' '<source.md>' 'test\out\submission_filled.hwpx' --profile r-and-d-startup-2026 --report 'test\out\submission_filled_report.md'
```

Current profile behavior:

- Supported Markdown body tables are rendered as real HWPX `tbl/tr/tc` objects cloned from existing template table style.
- Supported Markdown image lines become temporary text anchors and are inserted by HWP COM `InsertPicture`.
- The report lists total Markdown tables/images, rendered table counts, image anchors queued, image writes applied/failed/pending, and image references not mapped by the profile.
- If HWP COM cannot start, the pre-COM report still shows pending image anchors so missing image support is visible instead of silent.

Then validate:

```powershell
& $cli validate-layout '<template.hwpx>' 'test\out\submission_filled.hwpx' 'test\out\submission_filled_layout.md'
& $cli validate-content 'test\out\submission_filled.hwpx' 'test\out\submission_filled_content.md' --require 'required text'
```

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
