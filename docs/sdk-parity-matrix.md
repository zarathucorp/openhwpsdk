# SDK Parity Matrix

This page maps the public Hancom Hwp SDK capability areas to the current OpenHwp Automation CLI surface. It is a support boundary, not a promise that every observed HWPX XML signal can be safely authored.

Primary references:

- [Hancom Hwp SDK](https://www.hancom.com/en/product/sdk/hwpSdk): HWP/HWPX viewing, create/open/save, format conversion, data insertion/editing, document security, and comparison/merge/history areas.
- [Hancom SDK lineup](https://sdk.hancom.com/en): separate SDK product areas such as document editing, automation, comparison, Docs Compare SDK, OCR SDK, and PDF SDK.

## Status Terms

| Status | Meaning |
| --- | --- |
| `verified` | Implemented and covered by a repeatable command-level validation path. |
| `COM-smoke` | Routed through local HWP/Hanword COM and validated by an open/save/export or report smoke test. |
| `package-writer` | Writes HWPX package XML directly with postcondition checks. |
| `inventory-only` | The CLI can detect and report the feature, but does not broadly author it. |
| `unsupported` | No named CLI command or supported workflow exists. |
| `needs-SDK` | The public Hancom SDK material points to a separate SDK or capability family outside this wrapper. |

## Capability Matrix

| Hancom SDK area | State | Current CLI support | Verification command | Current gap |
| --- | --- | --- | --- | --- |
| HWP/HWPX viewing and document open | `COM-smoke` | `doc-info`, `read-text`, `read-page`, `diagnose-com` use installed HWP COM. | `--visible diagnose-com <file>` then `doc-info <file>` or `read-text <file>`. | No SDK-free viewer/runtime is bundled. COM behavior depends on the local desktop installation. |
| Create, open, save, save-as | `COM-smoke` / `package-writer` | `new-text`, `copy-save`, `export-pdf`, form-map apply paths, and supported package writers. | `copy-save <in> <out>`, `validate-content <out>`, and PDF export when visual behavior matters. | No general HWP writer that can safely recreate arbitrary rich documents from scratch. |
| PDF format conversion | `COM-smoke` | `export-pdf` and `visual-smoke-corpus` cover PDF export through HWP COM. | `--visible visual-smoke-corpus <corpus> <out> <report> --strict-cleanup`. | PDF export still requires local HWP COM and human visual review for layout quality. |
| Non-PDF format conversion | `unsupported` | No named command for HTML, web page, image, or other document-filter conversions. | None yet. | Add a named conversion command plus text/page/object preservation checks before claiming support. |
| Extract and edit text/image/table data | `package-writer` / `COM-smoke` | `extract-form-map`, `probe-form-map`, `apply-form-map`, `fill-submission-template`, `list-pictures`, `replace-image-control`, and `table-*-package`. | Run the specific command report, then `validate-layout`, `validate-content`, `scan-hwpx-features`, and PDF export for visual review. | Rich arbitrary authoring remains feature-specific. Unsupported targets must stay skipped or inventory-only. |
| Header/footer and page numbering | `inventory-only` / `package-writer` / `COM-smoke` | `list-header-footer`, `set-header-footer-text`, `set-header-footer-apply-page-type`, `add-header-footer-reference`, and `page-number-set` cover focused cases. | Inventory before/after plus `validate-layout`; `page-number-set` also needs COM smoke. | `add-header-footer-reference` reuses an existing `idRef` only and rejects duplicate `kind + section + applyPageType`. New body creation and rich header/footer authoring are not general-purpose yet. |
| Page/section layout setup | `unsupported` | No named command for paper size, margins, orientation, columns, section breaks, or section-specific numbering policy. | None yet. | Add focused inventory and writer commands before promoting this area. `page-number-set` is only a narrow page-numbering smoke, not general page setup support. |
| Fields and form controls | `package-writer` / `inventory-only` | Package form-map writes support press-field text and checkbox values in known paths; `list-fields --com` inventories broader fields. | `list-fields --com`, form-map apply report, package XML check, and layout/content validation. | Radio, combo, edit, button, and generic field semantics need separate write contracts. |
| Captions, bookmarks, references, TOC/index, notes | `inventory-only` | `scan-hwpx-features` inventories these signals. | `scan-hwpx-features <file-or-corpus> <report>`. | Broad COM/package authoring and refresh workflows are not implemented. |
| Shapes, text boxes, WordArt-like objects, equations, charts, OLE, media | `inventory-only` | `scan-hwpx-features` inventories many signals; some objects are preservation-only today. | Feature scan plus PDF visual smoke when the document opens. | Authoring is not broadly supported; chart/OLE/media may require editor or SDK-specific handling. |
| Document security | `unsupported` | No supported CLI command for document password, distribution/read-only restrictions, or private-text encryption. | None yet; future work requires reversible fixture smoke tests. | Do not mark implemented without a real HWP/SDK-backed command and output-only verification. |
| Document comparison, merge, and history | `needs-SDK` | No document-level compare/merge/history command. Table cell merge/split commands are unrelated package-table operations. | None for document-level compare/merge/history. | External dependency research is required where Hancom points to separate comparison SDK capability. |
| OCR/PDF SDK areas | `needs-SDK` | Outside the HWP/HWPX automation wrapper. | Not applicable. | Treat as external SDK/product scope, not OpenHwp Automation support. |

## Gap Ledger

| Area | State | Next evidence required |
| --- | --- | --- |
| Document password and read-only/distribution controls | `unsupported` | Identify an HWP COM action or separate SDK path, then add a reversible fixture smoke test. |
| Private text encryption | `unsupported` | Add a fixture proving before/after protection behavior without mutating the original input. |
| Document compare/merge/history | `needs-SDK` | Confirm whether the available path is report-only, merge-output, or a separate Hancom SDK dependency. |
| HTML and non-PDF conversion | `unsupported` | Add a named conversion command plus text/page/object preservation checks. |
| General rich authoring | `inventory-only` | Promote one feature at a time from inventory to COM/package writer with fixture output and validation. |
| Page/section layout setup | `unsupported` | Add focused inventory and writer commands for paper, margin, orientation, columns, section breaks, and section numbering policies. |
| Press field and checkbox writes | `package-writer` | Keep form-map reports, value postchecks, and layout/content validation as the gate. |
| Radio/combo/edit/button controls | `inventory-only` / `unsupported` | Define control-type value semantics and mismatch behavior before writing. |

## Verification Gate

Use this gate before moving a capability from `unsupported` or `inventory-only` to a stronger state:

1. Add or select a real HWPX fixture that HWP can open.
2. Run `scan-hwpx-features` or the focused inventory command and save the report.
3. Run the mutation or COM workflow with an explicit report.
4. Run `validate-layout` and `validate-content` where the command changes a document.
5. Export to PDF or run `visual-smoke-corpus` for visual smoke evidence.
6. Keep expected failures exact: `fileNameOrPath=exitCode[:reasonFragment]`.

If any step depends on a separate Hancom SDK or license that this repository does not bundle, leave the state as `needs-SDK` rather than implying OpenHwp Automation support.
