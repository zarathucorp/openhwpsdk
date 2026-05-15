# OpenHwp Automation

OpenHwp Automation is a Windows-first C# wrapper and CLI for automating Hancom HWP/Hanword through COM, plus HWPX package utilities for workflows where preserving the original document structure matters.

<div class="hero-actions">
  <a class="primary" href="getting-started/">Get started</a>
  <a href="cli-reference/">CLI reference</a>
  <a href="hwp-authoring-feature-roadmap/">Roadmap</a>
</div>

## What It Does

OpenHwp Automation focuses on real document operations that need evidence after mutation:

- open, read, save, and export HWP/HWPX documents through local HWP COM;
- fill existing HWPX form templates while preserving package structure;
- validate layout and content after automated edits;
- inspect and replace existing HWPX picture binaries without moving the picture object;
- edit simple HWPX tables without launching HWP COM;
- diagnose local HWP COM registration, process state, and document-open failures.

## Start Here

=== "Build"

    ```bat
    build.cmd Release
    ```

=== "Check CLI"

    ```bat
    src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe version
    ```

=== "Diagnose HWP COM"

    ```bat
    src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible diagnose-com
    ```

## Main Areas

| Area | Start with | Notes |
| --- | --- | --- |
| HWP COM automation | [Getting started](getting-started.md) | Requires a local HWP/Hanword installation. |
| Existing form filling | [HWPX validation workflow](markdown-hwpx-validation-workflow.md) | Uses form maps, probes, reports, and validators. |
| Image replacement | [Image replacement](image-replacement.md) | Preserves object geometry and wrapping when replacing image binaries. |
| Command details | [CLI reference](cli-reference.md) | Short examples for the public command surface. |
| SDK parity | [SDK parity matrix](sdk-parity-matrix.md) | Maps Hancom SDK capability areas to supported, inventory-only, unsupported, and needs-SDK states. |
| Current boundaries | [Roadmap](hwp-authoring-feature-roadmap.md) | Tracks strong areas, gaps, and next implementation candidates. |

## Requirements

- Windows.
- Hancom HWP/Hanword installed locally for COM-backed operations.
- MSBuild or Visual Studio Build Tools for .NET Framework projects.
- x86 CLI process for HWP COM compatibility.

Package-level HWPX commands can run without opening HWP, but editor-backed copy/paste, PDF export, and COM diagnostics require the installed desktop application.
