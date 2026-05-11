# Submission HWPX Code Issues

Reviewed on 2026-05-08 and updated on 2026-05-11 against the current `codex/hwp-automation` source tree.

This note supersedes the first-run issue log for filling the submission template under `test/` from the Markdown draft under `test/`. Several issues below were real when discovered, but are no longer open in the same form after the later CLI and HWPX writer changes.

## Current implementation snapshot

- COM calls now have a default timeout guard through `ComOperationWatchdog`, plus `--com-timeout-ms` and `--no-com-timeout`.
- `diagnose-com [inputPath]` reports HWP process state, COM registration, HWP version, visibility, file-path checker registration, message box mode, and optional document-open success.
- `fill-submission-template <template.hwpx> <source.md> <output.hwpx> [--profile r-and-d-startup-2026] [--report report.md] [--asset-root dir] [--markdown-table-mode text|render] [--image-mode package|com|none]` exists for this submission form.
- `extract-form-map` still creates a whole-package map, and `probe-form-map` still verifies HWP-selectability before editor-backed writes.
- `apply-form-map --package` now supports COM-free package text writes and package-level image embedding, writes apply details when `--report` is supplied, and writes package-layout validation to a sibling `*.layout.md` report.
- `SimpleZipArchive.WriteAllPreservingTemplate` now provides the core package writer that preserves template entry order, compression method, timestamps, and untouched entries.
- `validate-content` exists for required strings, Markdown-artifact checks, unresolved placeholder checks, repeated guide-like text warnings, and possible overflow warnings.
- `HwpSession.Open` falls back to a temporary ASCII-like copy path when HWP COM fails to open the original path.
- Image insertion through HWP automation now passes `sizeOption=1` for table-cell and text-anchor image writes so requested dimensions are honored.
- The `r-and-d-startup-2026` profile renders supported body Markdown tables as HWPX table objects and queues supported Markdown image lines for package-level `BinData`/`hp:pic` insertion by default. HWP COM insertion remains available through `--image-mode com`.
- The profile-specific package image writer embeds original image files, adds `BinData`/`hp:pic`/manifest entries, sizes display objects from image DPI with a 96-DPI fallback, and scales down only when the natural 100% size exceeds the document body area.
- The fill report now includes template/profile compatibility, observed key table signatures, configured asset roots, resolved image paths, missing image candidate paths, image write results, and grouped missing-target causes.
- `validate-layout` classifies findings as `expected-change`, `review-needed`, or `blocking`, and supports intentional table row-growth allowlists.
- Package-mode anchor writes are applied from later paragraphs toward earlier paragraphs to reduce repeated-anchor index drift.
- Package text writes normalize target runs that would otherwise inherit `charPr` below 7pt, and COM table-cell writes set 10pt before `InsertText`.
- Body Markdown tables in the submission profile default to rendered HWPX table objects. `--markdown-table-mode text` keeps the conservative behavior that converts new Markdown tables into text lines.
- Package cell writes validate the extracted `currentText` by default before replacing text, with `validateCurrentText="false"` as an explicit escape hatch.
- Form-map extraction and submission table filling are merge/nested-table aware: grids are reconstructed from `cellAddr` plus `cellSpan`, parent-cell direct text is separated from nested table text, and row-group cloning preserves multi-row records such as the main R&D performance table.

## Resolved Or Mostly Resolved

### 1. COM commands can hang without a useful failure mode

Status: mostly resolved.

What changed:

- `ComOperationWatchdog` wraps session creation, automation setup, and document open.
- Timeout failure prints operation name, timeout, last step, and running `Hwp` processes, then exits with code `124`.
- `diagnose-com` gives a direct diagnostic path instead of forcing users to infer whether HWP is installed, COM is registered, a hidden process is stuck, or document open failed.

Remaining gap:

- There is still no explicit `--attach` / `--new-instance` choice.
- The timeout guard exits the CLI process; it does not recover the COM call in-process.

### 2. Former gap: full-form Markdown-to-template fill path

Status: resolved for this submission template, not a generic product feature.

What changed:

- `fill-submission-template` is now a supported CLI command.
- The command has an explicit `r-and-d-startup-2026` profile and produces a report with template compatibility, cell writes, paragraph writes, rebuilt rows, Markdown table/image counts, table handling mode, rendered/converted table counts, image-anchor counts, resolved image paths, image write results, missing targets, grouped missing-target causes, skipped unsafe targets, and unmapped image references.
- The implementation fills the existing template package instead of rebuilding the official form from scratch. It inserts queued image anchors through the profile-specific package image writer by default, while `--image-mode com` remains available for HWP editor-backed insertion.

Remaining gap:

- The command is profile-specific and hard-coded to this application form.
- A reusable structured Markdown-to-official-form engine is still not available.
- Template compatibility is a heuristic table-count/key-table check, not a full semantic profile migration engine.

### 3. Former gap: HWPX package writing in the core CLI

Status: resolved for text writes and basic package-level image embedding.

What changed:

- `SimpleZipArchive` now reads and writes HWPX/ZIP packages.
- `WriteAllPreservingTemplate` preserves the original package and replaces only changed entries.
- `apply-form-map --package` applies text writes without HWP COM, embeds generic map `writeImage` paths as `BinData`/`hp:pic` objects, writes apply attempted/applied/failed/skipped rows when `--report` is supplied, and writes layout validation to a sibling `*.layout.md` report.
- `fill-submission-template` writes text and supported Markdown table content through the package-preserving path, then inserts queued Markdown images through the profile-specific package image writer by default.

Remaining gap:

- Generic package image insertion now covers ordinary anchor/cell images, manifest updates, and object id allocation, but it is still not a full drawing-object editor for arbitrary wrapping/positioning styles.
- Package-mode text anchors are now applied from later paragraphs toward earlier paragraphs, but a richer section-scoped anchor resolver is still needed for heavily duplicated text.

### 4. Cell text replacement must preserve nested tables and non-target paragraphs

Status: mostly resolved.

What changed:

- Package cell writes select a direct, non-nested paragraph and avoid paragraphs that contain nested `hp:tbl` nodes.
- Submission-template cell writes use the same non-nested paragraph rule.
- Unsafe cells are skipped and reported instead of being cleared destructively.

Remaining gap:

- There is still no dedicated automated regression fixture that proves nested-table preservation across multiple template variants.

### 5. Cell height does not auto-grow after direct package writes

Status: partially resolved.

What changed:

- `HwpxTextLayoutHelper.ExpandRowHeightForText` estimates required wrapped line count and raises row cell heights.
- `validate-content` can warn on possible text overflow.
- `SubmissionTemplateFiller` removes stale `hp:linesegarray` metadata from paragraphs it rewrites.

Remaining gap:

- Height expansion is heuristic, not a real Hanword layout pass.
- Editor-backed reflow is still needed when precise visual fidelity matters.

### 6. Markdown conversion leaks source-format artifacts

Status: resolved for the supported submission profile.

What changed:

- `SubmissionTemplateFiller.NormalizeBlockLines` still removes unsupported inline Markdown artifacts for plain-text summaries and preview text.
- Markdown list lines are normalized into the blank-template body style (`circle` style lines and indented dash detail lines).
- Profile body blocks now preserve Markdown table semantics and render supported Markdown tables as HWPX `tbl/tr/tc` objects cloned from existing template table style.
- The profile now defaults body Markdown tables to rendered HWPX `tbl/tr/tc` objects. The renderer chooses the simplest unmerged top-level table style available in the template; `--markdown-table-mode text` is still available when preserving the original HWPX table count is more important.
- The fill report counts total Markdown tables, rendered HWP tables, text-converted tables, and rendered table rows.

Remaining gap:

- The normalization lives inside the submission profile, not a reusable Markdown semantic parser.
- Generic package-map text writes still do not create arbitrary Markdown table structures.
- The text conversion is intentionally conservative; it preserves content and table count but does not provide semantic cell-level mapping into existing official-form tables.

### 7. `validate-layout` passes structure but not content quality

Status: partially resolved.

What changed:

- `validate-layout` now distinguishes `expected-change`, `review-needed`, and `blocking` findings and prints a one-line verdict.
- Intentional table row growth can be allowed with `--allow-table-row-change`, and the submission profile allows the known participant-table expansion.
- `validate-content` now separates content checks from layout checks.
- It detects suspicious Markdown artifacts, unresolved TODO-style placeholders, missing required strings, repeated guide-like text, and possible cell overflow warnings.

Remaining gap:

- It does not validate domain-specific business rules such as budget consistency, duplicate guide text by exact Korean form wording, image presence by semantic section, or reviewer-facing content quality.

### 8. Direct XML text writes do not preserve HWP paragraph styling

Status: partially resolved.

What changed:

- The submission filler clones existing template paragraphs for multi-line body content.
- It can force known character style references for the roadmap overview table and related normalized table text.
- It preserves the template's heading/body split rather than importing Markdown headings as raw text.

Remaining gap:

- Generic package-map text writes still update one paragraph's text node; they do not create full HWP-native structures for arbitrary Markdown headings, lists, captions, or tables.
- Package text writes now prevent sub-7pt `charPr` inheritance on replaced runs and validate target `currentText` before writing cells.
- A true generic Markdown-to-HWP renderer remains out of scope.

### 9. HWP image insertion ignores requested size

Status: resolved for the supported image insertion paths.

What changed:

- `InsertPictureInTableCell` and `InsertPictureAtTextAnchor` call `InsertPicture` with `sizeOption=1` when `--image-mode com` is used.
- Supported `fill-submission-template` image lines are routed to temporary text anchors and then replaced by package-level `hp:pic` objects by default.
- Package-level profile images are displayed at natural 100% size based on DPI, with 96 DPI as fallback, and are proportionally reduced to the document body area when needed.

Remaining gap:

- Generic package-map image insertion uses package-level natural sizing by default and supports explicit HWPX-unit dimensions; exact editor placement/wrapping should still be verified visually when it matters.
- `--image-mode com` still depends on a healthy local HWP COM session. If COM hangs at `Create HWP COM instance`, fix the local HWP process state before retrying, then verify the result with content validation and PDF export.

### 10. HWP COM open fails on some Korean/special-character paths

Status: mostly resolved.

What changed:

- `HwpSession.Open` retries through a temporary `%TEMP%\openhwpsdk-<guid>.hwpx` copy when the original path fails and the file exists.
- Temporary copies are cleaned up when the session owns and closes HWP.

Remaining gap:

- This is a fallback for COM path fragility, not a guarantee that every path/permission/process-lock case will succeed.

## Still Open

### A. Generic semantic anchors can still become stale after structural edits

Current state:

- Package anchor writes first try the recorded paragraph index, then fall back to matching the original paragraph text and occurrence.
- This is safer than index-only writing, but it can still fail if the anchor text itself changes or appears in a newly duplicated section.

Recommended next step:

- Add section-scoped semantic anchors: nearest heading, table label, occurrence within section, and current text.
- Re-read the modified package between structural row expansion and later anchor writes when using generic map workflows.

### B. Content validation is useful but still shallow

Current state:

- `validate-content` catches common generated-output mistakes.
- It is not a proposal reviewer and does not know this form's budget, section, table, or image requirements.

Recommended next step:

- Add profile-specific content checks for `r-and-d-startup-2026`.
- Check required section strings, expected image anchors, removed guide markers, budget totals across repeated tables, and known leftover Markdown patterns.

### C. Encoding cleanup is still incomplete

Current state:

- Generated reports are generally written as UTF-8 with BOM where intended.
- Some existing docs, including README text and older workflow examples, still contain Korean mojibake copied from console output.

Recommended next step:

- Normalize human-facing docs in a separate pass.
- Avoid copying console-mojibake text into tracked files.
- Prefer `-LiteralPath`-safe examples for Korean or bracketed filenames.

### D. Generic package-map image writes need broader fixtures

Current state:

- HWP automation can insert images.
- `apply-form-map --package` can now embed generic map `writeImage` paths by adding binary resources, manifest entries, and `hp:pic` drawing objects at resolved anchors or cells.
- The submission profile still uses its own queueing logic for Markdown image lines, but both paths share the package-level image object writer.
- `apply-form-map --report` writes attempted/applied/failed/skipped details for HWP COM mode and package mode, and package mode writes layout validation to a sibling `*.layout.md` report.

Recommended next step:

- Add regression fixtures for anchor image insertion, cell image insertion, repeated image ids, explicit HWPX-unit dimensions, and PDF render output.
- Keep editor-specific wrapping/positioning cases on the HWP automation path until the package writer has fixtures for those object styles.

## Current Priority Order

1. Add profile-specific `validate-content` checks for the submission template.
2. Add regression fixtures for nested-table preservation, row-height expansion, Markdown artifact removal, image path resolution, image sizing, and repeated-anchor package writes.
3. Improve generic anchor resolution with section-scoped semantic anchors and richer candidate reporting.
4. Add the staged pipeline / final-promotion commands described in `test/openhwpsdk_개선사항_정리.md`.
5. Normalize tracked docs and examples to readable UTF-8 Korean.
6. Expand package-level image insertion fixtures across multiple templates and object styles.
