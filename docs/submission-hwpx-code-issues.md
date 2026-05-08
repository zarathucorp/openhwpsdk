# Submission HWPX Code Issues

Reviewed on 2026-05-08 against the current `codex/hwp-automation` source tree.

This note supersedes the first-run issue log for filling the submission template under `test/` from the Markdown draft under `test/`. Several issues below were real when discovered, but are no longer open in the same form after the later CLI and HWPX writer changes.

## Current implementation snapshot

- COM calls now have a default timeout guard through `ComOperationWatchdog`, plus `--com-timeout-ms` and `--no-com-timeout`.
- `diagnose-com [inputPath]` reports HWP process state, COM registration, HWP version, visibility, file-path checker registration, message box mode, and optional document-open success.
- `fill-submission-template <template.hwpx> <source.md> <output.hwpx> [--profile r-and-d-startup-2026] [--report report.md] [--asset-root dir]` exists for this submission form.
- `extract-form-map` still creates a whole-package map, and `probe-form-map` still verifies HWP-selectability before editor-backed writes.
- `apply-form-map --package` now supports text-only package writes, writes apply details when `--report` is supplied, and writes package-layout validation to a sibling `*.layout.md` report.
- `SimpleZipArchive.WriteAllPreservingTemplate` now provides the core package writer that preserves template entry order, compression method, timestamps, and untouched entries.
- `validate-content` exists for required strings, Markdown-artifact checks, unresolved placeholder checks, repeated guide-like text warnings, and possible overflow warnings.
- `HwpSession.Open` falls back to a temporary ASCII-like copy path when HWP COM fails to open the original path.
- Image insertion through HWP automation now passes `sizeOption=1` for table-cell and text-anchor image writes so requested dimensions are honored.
- The `r-and-d-startup-2026` profile renders supported body Markdown tables as HWPX table objects and queues supported Markdown image lines for HWP COM `InsertPicture`.
- The fill report now includes template/profile compatibility, observed key table signatures, configured asset roots, resolved image paths, missing image candidate paths, and grouped missing-target causes.
- `validate-layout` classifies findings as `expected-change`, `review-needed`, or `blocking`, and supports intentional table row-growth allowlists.
- Package-mode anchor writes are applied from later paragraphs toward earlier paragraphs to reduce repeated-anchor index drift.

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
- The command has an explicit `r-and-d-startup-2026` profile and produces a report with template compatibility, cell writes, paragraph writes, rebuilt rows, Markdown table/image counts, rendered HWP table counts, image-anchor counts, resolved image paths, image write results, missing targets, grouped missing-target causes, skipped unsafe targets, and unmapped image references.
- The implementation fills the existing template package instead of rebuilding the official form from scratch, then uses a HWP COM post-pass for queued image anchors when images are present.

Remaining gap:

- The command is profile-specific and hard-coded to this application form.
- A reusable structured Markdown-to-official-form engine is still not available.
- Template compatibility is a heuristic table-count/key-table check, not a full semantic profile migration engine.

### 3. Former gap: HWPX package writing in the core CLI

Status: resolved for text/package updates.

What changed:

- `SimpleZipArchive` now reads and writes HWPX/ZIP packages.
- `WriteAllPreservingTemplate` preserves the original package and replaces only changed entries.
- `apply-form-map --package` applies text writes without HWP COM, writes apply attempted/applied/failed/skipped rows when `--report` is supplied, and writes layout validation to a sibling `*.layout.md` report.
- `fill-submission-template` writes text and supported Markdown tables through the package-preserving path, then uses HWP COM for queued Markdown images.

Remaining gap:

- Package mode intentionally skips image writes as unsafe, reports them as skipped, and returns nonzero when unsafe/image writes are present. Images still require the HWP automation path.
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
- The fill report counts total Markdown tables, rendered HWP tables, and rendered table rows.

Remaining gap:

- The normalization lives inside the submission profile, not a reusable Markdown semantic parser.
- Generic package-map text writes still do not create arbitrary Markdown table structures.

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
- A true generic Markdown-to-HWP renderer remains out of scope.

### 9. HWP image insertion ignores requested size

Status: resolved for the supported image insertion paths.

What changed:

- `InsertPictureInTableCell` and `InsertPictureAtTextAnchor` now call `InsertPicture` with `sizeOption=1`.
- Supported `fill-submission-template` image lines are routed to temporary text anchors and then to HWP COM `InsertPicture`.

Remaining gap:

- Package-mode image insertion is still intentionally unsupported.
- Image insertion still depends on a healthy local HWP COM session. If COM hangs at `Create HWP COM instance`, fix the local HWP process state before retrying, then verify the result with content validation and PDF export.

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

### D. Generic package image writes are intentionally unsupported

Current state:

- HWP automation can insert images.
- Package mode skips image writes because embedding binary image resources and drawing-object XML safely requires more than replacing text nodes.
- `apply-form-map --report` writes attempted/applied/failed/skipped details for HWP COM mode and package mode. Package mode records image writes as skipped and writes layout validation to a sibling `*.layout.md` report.

Recommended next step:

- Keep image writes on the HWP automation path until a tested package-level image writer exists.
- If package-level image support is added, validate binary part insertion, manifest updates, object IDs, dimensions, and PDF render output.

## Current Priority Order

1. Add profile-specific `validate-content` checks for the submission template.
2. Add regression fixtures for nested-table preservation, row-height expansion, Markdown artifact removal, image path resolution, image sizing, and repeated-anchor package writes.
3. Improve generic anchor resolution with section-scoped semantic anchors and richer candidate reporting.
4. Add the staged pipeline / final-promotion commands described in `test/openhwpsdk_개선사항_정리.md`.
5. Normalize tracked docs and examples to readable UTF-8 Korean.
6. Add package-level image insertion only after a narrow fixture proves manifest/object/dimension handling is safe.
