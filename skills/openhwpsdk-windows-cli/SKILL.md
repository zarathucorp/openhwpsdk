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
4. Run `apply-form-map`; use `apply-form-map --package` only for text-only XML writes. Use `--report` in either mode when attempted/applied/failed/skipped detail matters.
5. Run `validate-layout`, then `validate-content`, then export PDFs if visual checking is needed. Use `--allow-table-row-change <indexes>` when a known table expansion is intentional.

For the supported R&D startup submission template, prefer:

```powershell
& $cli fill-submission-template '<template.hwpx>' '<source.md>' 'test\out\submission_filled.hwpx' --profile r-and-d-startup-2026 --asset-root '<image-root>' --markdown-table-mode text --report 'test\out\submission_filled_report.md'
```

This profile converts body Markdown tables to text by default to preserve the original HWPX table structure; use `--markdown-table-mode render` only when inserted HWPX table objects are acceptable. It queues supported Markdown image lines for HWP COM `InsertPicture` through temporary text anchors. The report includes template/profile compatibility, image path resolution candidates, mapped/unmapped image counts, and classified layout findings. If images are present, HWP COM must be healthy; package-mode image insertion remains intentionally unsupported and must be reported as skipped.

Package text writes guard against tiny placeholder text styles by replacing sub-7pt `charPr` references on written runs. HWP COM table-cell writes set 10pt before `InsertText`. Package cell writes validate extracted `currentText` by default; disable only with `validateCurrentText="false"` on deliberate staged rewrites.

For existing tables, inspect Markdown tables first, then fill existing cells:

```powershell
& $cli markdown-table-list '<source.md>'
& $cli --visible fill-markdown-table '<template.hwpx>' '<source.md>' 'test\out\table-out.hwpx' 8 3 1 0 1 2 5
```

Use row/column table writes carefully around merged or irregular tables. Prefer map/probe evidence or a dedicated table dump/resolver before claiming the cell target is safe.

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
