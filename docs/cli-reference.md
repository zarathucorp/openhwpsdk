# CLI Reference

This page lists the main public command families. It is intentionally shorter than the implementation notes; use command reports and validators as the source of truth for a specific document run.

## Basics

```powershell
$cli = "src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe"
& $cli version
& $cli new-text C:\temp\hello.hwpx "Hello from OpenHwp"
& $cli copy-save C:\temp\in.hwpx C:\temp\out.hwpx
& $cli doc-info C:\temp\in.hwp
& $cli read-text C:\temp\in.hwp
& $cli read-page C:\temp\in.hwp 1
& $cli --visible export-pdf C:\temp\in.hwpx C:\temp\out.pdf
```

## HWP COM Diagnostics

```powershell
& $cli --visible diagnose-com
& $cli --visible diagnose-com C:\temp\in.hwpx
```

Use this before workflows that depend on the desktop editor, such as export, field operations, list-controls, or rich copy/paste.

## Field Commands

```powershell
& $cli field-exists C:\temp\form.hwp TEST
& $cli field-get C:\temp\form.hwp TEST
& $cli field-set C:\temp\form.hwpx TEST "updated text" C:\temp\form-out.hwpx
```

## Form Map Workflow

```powershell
& $cli extract-form-map C:\temp\template.hwpx C:\temp\template-form-map.xml
& $cli --visible probe-form-map C:\temp\template.hwpx C:\temp\template-form-map.xml C:\temp\template-probe.md
& $cli --visible apply-form-map C:\temp\template.hwpx C:\temp\template-form-map.xml C:\temp\filled.hwpx --report C:\temp\filled-apply.md
& $cli apply-form-map --package C:\temp\template.hwpx C:\temp\template-form-map.xml C:\temp\filled-package.hwpx --report C:\temp\filled-package-apply.md
```

Use package mode when COM-free text, image, press-field, or checkbox writes are enough. Use HWP-backed mode when editor behavior is required.

## Submission Template Profile

```powershell
& $cli fill-submission-template C:\temp\template.hwpx C:\temp\source.md C:\temp\submission-filled.hwpx --profile r-and-d-startup-2026 --asset-root C:\temp --image-mode package --report C:\temp\submission-filled-report.md
```

Use `--markdown-table-mode text` only when preserving the original HWPX table count matters more than table semantics. Use `--image-mode com` only when local HWP COM is healthy and editor-backed image insertion is required.

## Validation

```powershell
& $cli validate-layout C:\temp\template.hwpx C:\temp\filled.hwpx C:\temp\layout-report.md
& $cli validate-content C:\temp\filled.hwpx C:\temp\content-report.md --require "required text"
```

`validate-layout` classifies findings as `expected-change`, `review-needed`, or `blocking`. Treat `review-needed` as a human review request, not as a clean pass.

## Visual Smoke

```powershell
& $cli --visible visual-smoke-corpus C:\temp\hwpx-corpus C:\temp\visual-smoke C:\temp\visual-smoke\visual-smoke-report.md
& $cli --visible visual-smoke-corpus C:\temp\hwpx-corpus C:\temp\visual-smoke C:\temp\visual-smoke\visual-smoke-report.md --expect-export-failure "known-nonrenderable.hwpx=1:Failed to open" --strict-cleanup
```

This runs a feature scan for the HWPX input, exports the selected files to PDFs under the output directory, and writes one Markdown report that links the scan report, PDF paths, export byte sizes, child export exit codes, and HWP process diagnostics. Use `--expect-export-failure` only for known inventory-only or non-renderable fixtures, and provide an exact contract in `fileNameOrPath=exitCode[:reasonFragment]` form. An expected failure that exports successfully, fails with a different exit code, fails with a different reason, or never runs still fails the smoke command. `--strict-cleanup` waits briefly for child HWP processes to exit before checking for newly remaining processes. It verifies scan/export health; still review the generated PDFs before accepting a visual change.

## Feature Inventory

```powershell
& $cli scan-hwpx-features C:\temp\hwpx-samples C:\temp\feature-scan.md
& $cli list-pictures C:\temp\template.hwpx C:\temp\picture-inventory.md
& $cli list-header-footer C:\temp\template.hwpx C:\temp\header-footer-inventory.md
& $cli --visible list-fields C:\temp\template.hwpx C:\temp\field-inventory.md --com
```

Inventory commands report what a package contains. A nonzero count does not imply broad write support for that feature.

## Package Image Replacement

```powershell
& $cli list-pictures C:\temp\template.hwpx C:\temp\picture-inventory.md
& $cli replace-image-control C:\temp\template.hwpx C:\temp\replaced.hwpx --target control:gso:0 --image C:\temp\new-image.png --report C:\temp\replace-image-report.md
```

Use this when a picture object's placement, wrapping, margins, crop, z-order, and anchor behavior should remain unchanged while only the linked image binary changes.

## Package Table Editing

```powershell
& $cli markdown-table-list C:\temp\input.md
& $cli table-create-package C:\temp\template.hwpx C:\temp\new-table.hwpx --rows 2 --cols 3 --text "Header A|Header B|Header C;Value 1|Value 2|Value 3" --report C:\temp\new-table-report.md
& $cli table-row-package C:\temp\template.hwpx C:\temp\row-added.hwpx --table-index 4 --action add --row 1 --count 2 --text "R1C1|R1C2|R1C3|R1C4;R2C1|R2C2|R2C3|R2C4" --report C:\temp\row-added-report.md
& $cli table-column-package C:\temp\template.hwpx C:\temp\column-added.hwpx --table-index 4 --action add --column 1 --count 1 --text "HNEW;R1NEW;R2NEW" --report C:\temp\column-added-report.md
& $cli table-cell-background-package C:\temp\template.hwpx C:\temp\background-cell.hwpx --table-index 4 --row 1 --column 1 --color "#FFF2CC" --report C:\temp\background-cell-report.md
& $cli table-cell-size-package C:\temp\template.hwpx C:\temp\equalized-table.hwpx --table-index 11 --equalize-widths --equalize-heights --report C:\temp\equalized-table-report.md
```

Package table commands reject unsafe tables when the requested operation cannot preserve structure reliably.

## Editor-Backed Copy/Paste

```powershell
& $cli list-pictures C:\temp\reference.hwpx C:\temp\reference-pictures.md
& $cli --visible list-controls C:\temp\reference.hwpx C:\temp\reference-controls.md
& $cli --visible probe-copy-from-doc C:\temp\reference.hwpx C:\temp\target.hwpx --source image:0 --target doc-end --report C:\temp\copy-probe.md
& $cli --visible copy-from-doc C:\temp\reference.hwpx C:\temp\target.hwpx C:\temp\copied.hwpx --source image:0 --target doc-end --report C:\temp\copy-report.md
```

Run `probe-copy-from-doc` before mutation. Validate copied output with feature scans, layout checks, and visual/PDF review when placement matters.
