# Submission HWPX Code Issues

This note records project-code issues found while filling `test/[붙임 8] 제출서식_차라투_v1.0.hwpx` from `test/제출서식_차라투_R&D수행창업기업_v0.3.md`.

It excludes user workflow mistakes and focuses on problems that should be fixed or supported in the project itself.

## 1. COM commands can hang without a useful failure mode

Observed symptom:

- `OpenHwp.Automation.Cli.exe version` did not return within 60 seconds.
- At the time, hidden `Hwp` processes were present, and the CLI did not produce a diagnostic before timing out externally.

Why this is a code issue:

- CLI commands that create `HWPFrame.HwpObject` have no bounded startup/open timeout, watchdog, or clear stale-process diagnostic.
- A user cannot distinguish "HWP is not installed", "COM startup is blocked", "security module prompt is hidden", and "existing Hwp process is stuck".

Recommended update:

- Add startup/open watchdog diagnostics around `HwpSession.Create`, `Visible` setup, `ConfigureForAutomation`, and `Open`.
- Add a CLI diagnostic command that reports running `Hwp` processes, COM registration availability, file path checker registration result, message box mode, and last step reached.
- Consider a `--attach` / `--new-instance` choice and a `--diagnose-only` mode.

## 2. No supported full-form Markdown-to-template fill path exists

Observed symptom:

- The available commands support partial workflows: `extract-form-map`, `apply-form-map`, `markdown-table-list`, `fill-markdown-table`, `replace-markdown`, and `append-markdown-lines`.
- `replace-markdown` is intentionally unsafe for official forms because it rewrites the document as plain text.
- There is no project command that takes an official HWPX template plus a structured Markdown application and fills the existing form while preserving layout.

Why this is a code issue:

- The project has enough building blocks to inspect and validate HWPX, but not enough orchestration to fill a complete official submission form.
- The one-off fill had to use a custom PowerShell HWPX package rewrite script.

Recommended update:

- Add a supported command such as:

```bat
OpenHwp.Automation.Cli.exe fill-submission-template <template.hwpx> <source.md> <output.hwpx> [--report report.md]
```

- Keep it template-preserving: fill existing cells and body placeholders, do not rebuild official tables.
- Make the mapping explicit and testable, preferably through a named profile for this submission form.

## 3. HWPX package writing is missing from the core CLI

Observed symptom:

- `extract-form-map` can inspect the HWPX package without COM.
- `apply-form-map` writes through HWP automation only.
- When COM was not reliable, there was no built-in package-level writer, so the result had to be written by a separate script.

Why this is a code issue:

- Existing form filling should not depend entirely on COM when HWPX XML can be safely patched and then validated.
- `SimpleZipArchive` only reads ZIP entries; the project has no package update abstraction.

Recommended update:

- Add a safe HWPX package writer that updates selected XML and preview entries while preserving all other package entries.
- Add an `apply-form-map --package` or separate `apply-form-map-package` path for text-only cell/paragraph writes.
- Always run `validate-layout` after package writes.

## 4. Cell text replacement must preserve nested tables and non-target paragraphs

Observed symptom:

- A first draft of the fill script cleared every paragraph in a target cell.
- Some cells in the template contain nested tables or multiple paragraphs; clearing them reduced the document table count from 48 to 44.
- `validate-layout` caught this as a structural failure.

Why this is a code issue:

- HWPX table cells are containers, not plain text boxes.
- Any future package writer or cell writer that treats a cell as one flat text field can accidentally delete nested tables, guide structures, or embedded content.

Recommended update:

- For package-level writes, update only the intended paragraph/run inside a cell.
- If a cell contains nested tables, preserve them by default and require an explicit destructive mode to clear them.
- Add tests where a cell contains both a normal paragraph and nested `hp:tbl` content.

## 5. Stored paragraph indexes become stale after structural edits

Observed symptom:

- After expanding the participant table, later body paragraph positions shifted.
- A fixed paragraph-index write strategy would place body content in the wrong area.

Why this is a code issue:

- `extract-form-map` records paragraph indexes, but indexes are not stable after row or paragraph insertion.
- For multi-step form fills, writes that change row counts can invalidate later anchor positions.

Recommended update:

- Resolve anchors dynamically by heading/current text at write time, not only by the original paragraph index.
- Process structural changes and paragraph writes in separate stages, re-reading the document between stages.
- Prefer semantic anchors such as heading text, nearest section title, table labels, and occurrence within that section.

## 6. Markdown conversion leaks source-format artifacts

Observed symptom:

- Markdown guide markers and image/table syntax can leak into HWPX text when flattened naively.
- In this run, blockquote-style guide lines had to be normalized separately so they did not appear as stray marker text in the output.

Why this is a code issue:

- `MarkdownTextConverter` is useful for plain text, but insufficient for official-form submission content.
- It does not model sections, guide callouts, images, tables, and body placeholders as separate semantic elements.

Recommended update:

- Add a structured Markdown parser for proposal/application Markdown:
  - headings as sections
  - blockquote guide lines as optional labels or skipped instructions
  - Markdown tables as table row data
  - images as image references or explicit placeholders
  - bullet/list text as clean HWP paragraph text
- Add a "submission plain text" normalization mode that removes source-only guide markers and Markdown image syntax.

## 7. `validate-layout` passes structure but not content quality

Observed symptom:

- `validate-layout` correctly detected table count/structure failures.
- It does not detect content problems such as repeated guide text, leftover Markdown artifacts, or missing key fields.

Why this is a code issue:

- Layout preservation is necessary but not sufficient for generated official-form output.
- A generated file can pass structural validation while still needing content cleanup.

Recommended update:

- Add a content validation command or extend reports with optional checks:
  - required key strings exist
  - known placeholders are gone or intentionally preserved
  - suspicious Markdown tokens are absent
  - guide text is not duplicated into answer cells
  - budget totals match across repeated tables
- Keep this separate from layout validation so structural and content failures are easy to distinguish.

## 8. Existing docs and report paths show Korean mojibake in some outputs

Observed symptom:

- Existing documentation and some generated reports display Korean filenames/text as mojibake in the current PowerShell environment.

Why this is a code issue:

- The repository should be reliable on the target Windows setup where Korean HWP/HWPX filenames are normal.
- Garbled paths make generated commands and reports harder to reuse.

Recommended update:

- Normalize docs to UTF-8 and verify they display correctly in Windows PowerShell and common editors.
- When generating reports, write UTF-8 consistently and avoid copying console-mojibake text into persistent files.
- Prefer `-LiteralPath`-safe examples for Korean/bracketed filenames.

## 9. Direct XML text writes do not preserve HWP paragraph styling

Observed symptom:

- Long body text was inserted too densely.
- Markdown sections, lists, tables, and image references were flattened into plain text instead of becoming HWP paragraphs, styled headings, bullet paragraphs, tables, or image placeholders.
- Some inserted text visually looked unlike the surrounding official form style.

Why this is a code issue:

- The fallback fill path edited `Contents/section0.xml` directly and mostly replaced or appended `hp:t` text nodes.
- It did not create proper HWP paragraph runs for each logical Markdown block.
- It did not clone the surrounding paragraph style intentionally for body text, bullet text, table text, captions, and long-form descriptions.
- It also left existing `hp:linesegarray` layout metadata in place. That metadata belongs to the old text layout and is not a real reflow of the newly inserted text.

Recommended update:

- Do not treat Markdown body content as one large plain string.
- Convert Markdown into HWP-native structures:
  - section headings as styled HWP heading paragraphs
  - bullets as repeated paragraphs cloned from the template bullet style
  - Markdown tables as HWP tables or mapped rows
  - image references as image placeholders or inserted pictures
  - captions as caption-style paragraphs
- When writing package XML directly, create fresh paragraph/run structures and either remove stale `hp:linesegarray` safely or force a HWP layout recalculation through the editor after writing.
- Add visual or PDF regression checks for text density, leftover Markdown syntax, and style drift.

## 10. Cell height does not auto-grow after direct package writes

Observed symptom:

- When large text was inserted into existing table cells, the cell did not naturally expand vertically.
- The result can look cramped, clipped, or visually dense even though the table structure count is preserved.

Why this is a code issue:

- In HWPX, table cell dimensions are explicit XML data, including `hp:cellSz height`.
- Directly replacing `hp:t` text does not run Hanword's table layout engine.
- Existing line segment data and cell heights remain from the blank template or old short placeholder text.
- `validate-layout` currently checks that structure did not break, but it does not verify whether text overflow, auto-fit, row height expansion, or visual readability is acceptable.

Recommended update:

- Add an editor-backed write path for table cells that uses HWP's own insertion/layout engine, then saves the file.
- For package-level writing, implement row-height adjustment:
  - estimate required wrapped line count from cell width, font size, and text length
  - update the relevant `hp:cellSz height` values consistently across the row
  - avoid changing table width, columns, or border fills
- Preserve nested tables while expanding only the target row/cell height.
- Add a validation check that flags cells where inserted text length exceeds a safe threshold for the current cell size.
- For long answer cells, prefer splitting content into multiple paragraphs rather than one long run.

## Suggested update order

1. Add COM diagnostics and timeout-friendly failure reporting.
2. Add a safe HWPX package writer and package-level form-map apply path.
3. Add HWP-native paragraph/style generation and table-cell auto-height handling.
4. Add semantic anchor lookup for tables and body sections.
5. Add a structured Markdown-to-submission-form profile for this template.
6. Add content validation checks in addition to `validate-layout`.
7. Clean encoding in docs and generated reports.
