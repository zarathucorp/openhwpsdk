---
name: openhwpsdk-windows-cli
description: Build, run, and troubleshoot the OpenHWP SDK C# CLI on Windows. Use when Codex is working in or against C:\Users\ZARATHU11\codex\openhwpsdk and needs OpenHwp.Automation.Cli.exe for HWP/HWPX COM automation, Markdown-to-template filling, HWPX form-map extract/probe/apply workflows, table or field writes, layout/content validation, PDF export, or Windows-specific CLI diagnosis.
---

# OpenHWP SDK Windows CLI

## Core Rule

Use this repo's C# CLI as the primary path for HWP/HWPX work. Do not route existing-template conversion through `kordoc`, direct package rebuilding, or a nonexistent `markdown-to-hwpx` command unless the user explicitly asks for a different tool.

Default repo root on this machine:

```powershell
C:\Users\ZARATHU11\codex\openhwpsdk
```

When the checkout differs, replace that root with the current repo path.

## Quick Start

Build Release x86 first:

```powershell
Set-Location -LiteralPath 'C:\Users\ZARATHU11\codex\openhwpsdk'
.\build.cmd Release
```

Locate and call the CLI with PowerShell's call operator:

```powershell
$cli = Join-Path (Get-Location) 'src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe'
& $cli version
```

Run `diagnose-com` before debugging document failures:

```powershell
& $cli --visible diagnose-com
```

Use `--visible` for HWP-backed operations that open documents, probe selections, export PDFs, or may hang in hidden COM mode.

## Workflow Choice

For an existing official HWPX template, preserve the template and fill it:

1. `extract-form-map` from the real HWPX package.
2. Edit only generated `writeText` and `writeImage` entries in the map XML.
3. Run `--visible probe-form-map` before HWP-backed writes.
4. Run `apply-form-map`; use `apply-form-map --package` for COM-free text writes and package-level image embedding when editor-backed behavior is not required. Use `--report` in either mode when attempted/applied/failed/skipped detail matters.
5. Run `validate-layout`, then `validate-content`, then export PDFs if visual checking is needed. Use `--allow-table-row-change <indexes>` when a known table expansion is intentional.

For the supported R&D startup submission template, prefer:

```powershell
& $cli fill-submission-template '<template.hwpx>' '<source.md>' 'test\out\submission_filled.hwpx' --profile r-and-d-startup-2026 --asset-root '<image-root>' --image-mode package --report 'test\out\submission_filled_report.md'
```

This profile renders body Markdown tables as HWPX table objects by default; use `--markdown-table-mode text` only when preserving the original HWPX table count matters more than table semantics. It queues supported Markdown image lines as temporary text anchors and inserts them by default with profile-specific package-level `BinData`/`hp:pic` updates. Package image insertion embeds the original image file, sizes the displayed object from image DPI with a 96-DPI fallback, and scales down only when the natural 100% size exceeds the document body area. Use `--image-mode com` only when local HWP COM is healthy and editor-backed image insertion is required; use `--image-mode none` for text/table-only staging. The report includes template/profile compatibility, image path resolution candidates, mapped/unmapped image counts, image write results, and classified layout findings.

Package text writes guard against tiny placeholder text styles by replacing sub-7pt `charPr` references on written runs. HWP COM table-cell writes set 10pt before `InsertText`. Package cell writes validate extracted `currentText` by default; disable only with `validateCurrentText="false"` on deliberate staged rewrites.

For existing tables, inspect Markdown tables first, then fill existing cells:

```powershell
& $cli markdown-table-list '<source.md>'
& $cli --visible fill-markdown-table '<template.hwpx>' '<source.md>' 'test\out\table-out.hwpx' 8 3 1 0 1 2 5
```

Use row/column table writes carefully around merged or irregular tables. Prefer map/probe evidence or a dedicated table dump/resolver before claiming the cell target is safe.

For rich copy/paste from an existing reference HWP/HWPX, use HWP COM and probe first:

```powershell
& $cli --visible list-controls '<reference.hwpx>' 'test\out\reference_controls.md'
& $cli --visible probe-copy-from-doc '<reference.hwpx>' '<target.hwpx>' --source image:0 --target doc-end --report 'test\out\copy_probe.md'
& $cli --visible copy-from-doc '<reference.hwpx>' '<target.hwpx>' 'test\out\copy_from_doc.hwpx' --source image:0 --target doc-end --report 'test\out\copy_from_doc.md'
```

`copy-from-doc` currently supports whole-document, paragraph-to-end text block, whole-table, image, and generic-control sources: `all`, `paragraph-to-end:<text>`, `table:<index>`, `image:<index>`, and `control:<ctrlId>:<index>`. `image:<index>` maps to `gso` controls in tested HWPX files; use the `typeIndex` column from `list-controls`, not the global `index` column. `paragraph-to-end:<text>` starts at the paragraph containing the text. Targets can be `doc-end`, `anchor:<text>`, `cell:<table,rowMove,colMove>`, or `control:<ctrlId>:<index>`. Cell targets use HWP movement-count selection from the first cell, not robust absolute grid addressing.

## Feature Coverage

Use `scan-hwpx-features` when the question is what HWPX authoring features are present in a file or corpus:

```powershell
& $cli scan-hwpx-features 'test' 'test\out\hwpx_feature_scan.md'
```

Regenerate the tracked feature corpus before relying on fixture coverage:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\New-HwpxFeatureFixtures.ps1
& $cli scan-hwpx-features 'test\corpus\features' 'test\out\hwpx_feature_scan_features.md'
```

The report includes aggregate counts, authoring coverage, detailed feature groups, missing corpus signals, per-file totals, and inventory tables for header/footer, field/form, reference, and note signals. Counts are inventory signals only; they do not mean the feature can be written or edited yet.

## Windows Rules

- Quote every path that may contain spaces, Korean text, brackets, or `&`.
- In PowerShell discovery commands, prefer `-LiteralPath`.
- Write Korean text extraction to UTF-8 files when console output looks garbled.
- Keep output artifacts under `test\out\` or another repo-local working directory.
- Check running HWP processes if files appear locked or COM behavior is inconsistent.
- If COM hangs at `Create HWP COM instance`, treat it as an environment/process state problem before blaming a specific document. Ask the user to save and close HWP windows rather than force-killing possible user-owned documents.
- Treat page-count growth as a review signal, not an automatic failure; use layout/content reports and PDF inspection.

## Validation

After any generated or modified HWPX:

```powershell
& $cli validate-layout '<template.hwpx>' '<candidate.hwpx>' 'test\out\layout_report.md'
& $cli validate-layout '<template.hwpx>' '<candidate.hwpx>' 'test\out\layout_report.md' --allow-table-row-change 10
& $cli validate-content '<candidate.hwpx>' 'test\out\content_report.md' --require 'required text'
& $cli --visible export-pdf '<candidate.hwpx>' 'test\out\candidate.pdf'
```

Accept only after reports are clean or warnings are reviewed. For existing templates, table count, widths, border fills, leading labels, and paragraph style drift matter more than page count.

## More Detail

Read [windows-cli-recipes.md](references/windows-cli-recipes.md) when exact command recipes, form-map examples, or troubleshooting steps are needed.
