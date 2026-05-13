# OpenHwp Automation

[Korean documentation](README.ko.md)

OpenHwp Automation is a C# wrapper and CLI sample for controlling a locally installed Hancom HWP/Hanword application through `COM/OLE`.

This repository does not wrap a separate Hancom SDK runtime. It uses the `HWPFrame.HwpObject` COM object exposed by the HWP installation on the current Windows PC.

## Current Scope

- Library: `src/OpenHwp.Automation`
- CLI sample: `src/OpenHwp.Automation.Cli`
- Build script: `build.cmd`

The core class is `OpenHwp.Automation.HwpSession`.

## Build

```bat
build.cmd
```

## Basic CLI Examples

Check the CLI version:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe version
```

Create a new document with text:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe new-text C:\temp\hello.hwpx "Hello from OpenHwp"
```

Open an existing document and save it to another path:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe copy-save C:\temp\in.hwpx C:\temp\out.hwpx
```

Export a document to PDF:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible export-pdf C:\temp\in.hwpx C:\temp\out.pdf
```

Read document metadata and text:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe doc-info C:\temp\in.hwp
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe read-text C:\temp\in.hwp
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe read-text C:\temp\in.hwp C:\temp\in.txt
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe read-page C:\temp\in.hwp 1
```

Read and write fields:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe field-exists C:\temp\form.hwp TEST
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe field-get C:\temp\form.hwp TEST
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe field-set C:\temp\form.hwpx TEST "updated text" C:\temp\form-out.hwpx
```

Run a generic HWP action:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe action InsertText Text="raw action text" --save C:\temp\raw.hwpx
```

## HWPX Form Map Workflow

`markdown-to-hwpx` is not exposed. A command with that name must render Markdown as HWP structure and character styles: headings as larger headings, tables as HWP tables, and inline marks such as bold/code as styled text.

Existing form templates should be filled through cell/cursor commands, then checked with `validate-layout`.

Template HWPX files can first be mapped from the real package XML, then filled through HWP automation or package mode:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe extract-form-map C:\temp\template.hwpx C:\temp\template-form-map.xml
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible probe-form-map C:\temp\template.hwpx C:\temp\template-form-map.xml C:\temp\template-probe.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible apply-form-map C:\temp\template.hwpx C:\temp\template-form-map.xml C:\temp\filled.hwpx --report C:\temp\filled-apply.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe apply-form-map --package C:\temp\template.hwpx C:\temp\template-form-map.xml C:\temp\filled-package.hwpx --report C:\temp\filled-package-apply.md
```

Edit only the generated map's `writeText`, `writeImage`, and supported field `writeValue` elements. The map records the full HWPX package structure, including `content.hpf`, manifest/spine, XML parts, previews, and metadata entries. It marks write candidates from every XML part that contains HWP paragraph/table text.

The map is merge/nested-table aware. Table grids are reconstructed from `cellAddr` plus `cellSpan`, and each cell's `currentText` excludes nested table text. Use `probe-form-map` before HWP-backed writing to confirm every mapped cell or anchor can be selected in HWP.

Use `apply-form-map --package` for COM-free text writes, package-level image embedding, package press-field writes, and package checkbox value writes. Use the HWP-backed path when editor-backed behavior is required. `--report` writes attempted/applied/failed/skipped details for both COM and package modes, and package-mode layout validation is written next to it as `*.layout.md`.

## Feature Coverage

Scan a file or directory of HWPX packages to see which authoring features are present in the corpus:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe scan-hwpx-features C:\temp\hwpx-samples C:\temp\hwpx-feature-scan.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe list-pictures C:\temp\template.hwpx C:\temp\picture-inventory.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe list-header-footer C:\temp\template.hwpx C:\temp\header-footer-inventory.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe set-header-footer-text C:\temp\template.hwpx C:\temp\header-footer-text-write.hwpx --kind header --section section0 --anchor "Header fixture" --text "Updated Header Fixture" --report C:\temp\header-footer-text-write.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible page-number-set C:\temp\template.hwpx C:\temp\page-numbered.hwpx --draw-pos 5 --side-char - --report C:\temp\page-number-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible list-fields C:\temp\template.hwpx C:\temp\field-inventory.md --com
```

The feature scan report includes aggregate counts, authoring coverage, missing corpus signals, per-file totals, and inventory tables for header/footer, field/form, references, notes, and embedded objects. Feature scan counts are inventory signals; they do not imply broad write/edit support.

Use `list-pictures` for a COM-free picture inventory before image replacement work. The report includes the XML part, package-order graphical-object index, shape id, image reference, resolved `BinData` path, image type, pixel size, byte size, SHA256, `hp:sz`, `hp:pos`, `orgSz`, `curSz`, `imgClip`, `outMargin`, `textWrap`, `textFlow`, and `treatAsChar`. The package-order index is an inspection aid; when HWP COM is available, `list-controls` remains the authoritative editor control inventory.

## Submission Template Profile

For the supported startup R&D submission profile, use:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe fill-submission-template C:\temp\template.hwpx C:\temp\source.md C:\temp\submission-filled.hwpx --profile r-and-d-startup-2026 --asset-root C:\temp --image-mode package --report C:\temp\submission-filled-report.md
```

The profile defaults to `--markdown-table-mode render` and `--image-mode package`. Body Markdown tables are rendered as HWPX table objects cloned from the simplest unmerged top-level template table style. Use `--markdown-table-mode text` only when preserving the original HWPX table count matters more than table semantics.

Supported Markdown image lines are inserted by the profile-specific package image writer by default. It embeds the original image file, writes `BinData`/`hp:pic`/manifest entries, computes display size from image DPI with a 96-DPI fallback, and scales down only when the natural 100% size exceeds the document body area. Use `--image-mode com` only when local HWP COM is healthy and editor-backed image insertion is required.

The fill report includes template/profile compatibility, table handling mode, rendered/converted table counts, rebuilt row changes, style-guard repairs, image write results, mapped/unmapped image counts, missing-target causes, and the sibling layout report. `validate-layout` classifies issues as `expected-change`, `review-needed`, or `blocking`; its exit code is nonzero only for blocking layout findings.

## Package Table and Cell Editing

Markdown table values can be inserted into existing HWPX table cells without recreating the table:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe markdown-table-list C:\temp\input.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-create-package C:\temp\template.hwpx C:\temp\new-table.hwpx --rows 2 --cols 3 --text "Header A|Header B|Header C;Value 1|Value 2|Value 3" --report C:\temp\new-table-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-row-package C:\temp\template.hwpx C:\temp\row-added.hwpx --table-index 4 --action add --row 1 --count 2 --text "R1C1|R1C2|R1C3|R1C4;R2C1|R2C2|R2C3|R2C4" --report C:\temp\row-added-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-column-package C:\temp\template.hwpx C:\temp\column-added.hwpx --table-index 4 --action add --column 1 --count 1 --text "HNEW;R1NEW;R2NEW" --report C:\temp\column-added-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-merge-package C:\temp\template.hwpx C:\temp\merged.hwpx --table-index 4 --row 1 --column 1 --row-span 2 --col-span 2 --text "Merged cell" --report C:\temp\merged-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-split-package C:\temp\merged.hwpx C:\temp\split.hwpx --table-index 4 --row 1 --column 1 --text "A|B;C|D" --report C:\temp\split-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-cell-style-package C:\temp\template.hwpx C:\temp\styled-cell.hwpx --table-index 4 --row 1 --column 1 --border-fill-id 32 --report C:\temp\styled-cell-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-cell-align-package C:\temp\template.hwpx C:\temp\aligned-cell.hwpx --table-index 4 --row 1 --column 1 --horizontal right --vertical bottom --report C:\temp\aligned-cell-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-cell-background-package C:\temp\template.hwpx C:\temp\background-cell.hwpx --table-index 4 --row 1 --column 1 --color "#FFF2CC" --report C:\temp\background-cell-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-cell-diagonal-package C:\temp\template.hwpx C:\temp\diagonal-cell.hwpx --table-index 4 --row 1 --column 1 --direction both --width "0.15 mm" --color "#000000" --report C:\temp\diagonal-cell-report.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe table-cell-size-package C:\temp\template.hwpx C:\temp\equalized-table.hwpx --table-index 11 --equalize-widths --equalize-heights --report C:\temp\equalized-table-report.md
```

- `table-create-package`: Creates a new simple table without HWP COM.
- `table-row-package`: Adds or deletes rows in simple top-level text-cell tables without COM.
- `table-column-package`: Adds or deletes columns in the same safe text-cell table subset in the selected body section.
- `table-merge-package`: Merges a rectangular cell range in simple top-level text-cell tables in the selected body section.
- `table-split-package`: Splits an existing merged top-left cell back into 1x1 cells.
- `table-cell-style-package`: Applies an existing `Contents/header.xml` borderFill ID to a cell or rectangular range.
- `table-cell-align-package`: Changes direct cell paragraph horizontal alignment and/or cell vertical alignment.
- `table-cell-background-package`: Adds, changes, or clears a solid cell background without mutating shared `borderFill` definitions.
- `table-cell-diagonal-package`: Adds, changes, or clears cell diagonal lines without mutating shared `borderFill` definitions.
- `table-cell-size-package`: Equalizes column widths and/or row heights in a simple unmerged top-level table.

Package table commands reject unsafe tables such as merged/nested/sparse/irregular/object-containing tables when the specific operation cannot preserve structure safely. Validate intentional row/column changes with `validate-layout --allow-table-row-change <index>` or `validate-layout --allow-table-column-change <index>`.

## Editor-Backed Copy/Paste

Use HWP COM-backed rich copy/paste when an existing reference document already has the table or control formatting you need:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe list-pictures C:\temp\reference.hwpx C:\temp\reference-pictures.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible list-controls C:\temp\reference.hwpx C:\temp\reference-controls.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible probe-copy-from-doc C:\temp\reference.hwpx C:\temp\target.hwpx --source image:0 --target doc-end --report C:\temp\copy-probe.md
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible copy-from-doc C:\temp\reference.hwpx C:\temp\target.hwpx C:\temp\copied.hwpx --source image:0 --target doc-end --report C:\temp\copy-report.md
```

`copy-from-doc` supports source selection for the whole document, paragraph-to-end text blocks, whole tables, images exposed by HWP as graphical object controls, and generic controls: `all`, `paragraph-to-end:<text>`, `table:<index>`, `image:<index>`, and `control:<ctrlId>:<index>`.

Targets can be `doc-end`, `anchor:<text>`, `cell:<table,rowMove,colMove>`, or `control:<ctrlId>:<index>`. Cell targets use HWP movement-count selection from the first cell, not robust absolute grid addressing, so be careful with merged or irregular tables.

## C# Example

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

## Design Notes

- The base control object is `HWPFrame.HwpObject`.
- Generic action execution is exposed through the CLI.
- File-open security module registration uses `RegisterModule("FilePathCheckDLL", "<registry value name>")`.
- `ConfigureForAutomation()` groups automation defaults, including security-module registration attempts and `SetMessageBoxMode(0x10)`.
- The CLI is built as `x86`.
- The library uses COM late binding and does not require a type-library reference.

## Limitations

- Windows only.
- Some action names or parameter names can vary by installed HWP version.
- Hidden-mode behavior, new-window behavior, and tab behavior can vary by HWP version.
- If this expands to in-process `HwpCtrl.ocx` or `HwpAutomation.dll`, using an `x86` host is safer.
