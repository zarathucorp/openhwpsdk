<h1 align="center">OpenHwp Automation</h1>

<p align="center">
  Windows-first C# automation and CLI tooling for Hancom HWP/HWPX documents.
</p>

<p align="center">
  <a href="https://zarathucorp.github.io/openhwpsdk/">Documentation</a>
  ·
  <a href="https://zarathucorp.github.io/openhwpsdk/getting-started/">Getting started</a>
  ·
  <a href="https://zarathucorp.github.io/openhwpsdk/cli-reference/">CLI reference</a>
  ·
  <a href="README.ko.md">한국어</a>
</p>

<p align="center">
  <a href="https://zarathucorp.github.io/openhwpsdk/">
    <img alt="Docs" src="https://img.shields.io/badge/docs-GitHub%20Pages-2ea44f">
  </a>
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows-0078D4">
  <img alt=".NET Framework" src="https://img.shields.io/badge/.NET%20Framework-4.0-512BD4">
  <img alt="HWPX" src="https://img.shields.io/badge/HWPX-package%20editing-2ea44f">
</p>

OpenHwp Automation controls a locally installed Hancom HWP/Hanword application through its `HWPFrame.HwpObject` COM interface and adds HWPX package utilities for structure-preserving document work.

It does not bundle or wrap a separate Hancom SDK runtime. The supported automation target is the HWP installation on the current Windows PC.

Use this project if you need to:

- automate HWP/HWPX documents from a Windows C# or CLI workflow;
- fill existing official forms without rebuilding their table structure;
- inspect, validate, or patch HWPX package content with reviewable reports.

## Why This Exists

HWP/HWPX automation is usually blocked by a mix of editor behavior, COM stability, package XML details, merged tables, embedded images, and official-form layout requirements. This repository keeps those concerns visible:

- C# library wrapper for HWP COM automation.
- Windows CLI for repeatable document operations.
- COM-free HWPX package edits where structural preservation matters.
- Reports for layout/content validation instead of silent document mutation.
- Diagnostics for HWP COM registration, process cleanup, and file-open behavior.

## Quick Start

Before running the commands below, use Windows with Hancom HWP/Hanword installed for COM-backed commands and MSBuild or Visual Studio Build Tools for compilation. Create a scratch output directory first:

```bat
mkdir C:\temp
```

Build the CLI:

```bat
build.cmd Release
```

Check the executable:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe version
```

Create a simple HWPX document:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe new-text C:\temp\hello.hwpx "Hello from OpenHwp"
```

Run a COM health check before editor-backed workflows:

```bat
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe --visible diagnose-com
```

## Common Workflows

| Workflow | Command family | Use when |
| --- | --- | --- |
| Read or save documents | `doc-info`, `read-text`, `copy-save`, `export-pdf` | Basic HWP/HWPX automation is enough. |
| Fill existing forms | `extract-form-map`, `probe-form-map`, `apply-form-map` | A template must keep its original table and section structure. |
| Fill supported submission templates | `fill-submission-template` | A known profile can map Markdown content into an official HWPX form. |
| Validate generated HWPX files | `validate-layout`, `validate-content` | You need evidence that the output did not corrupt layout or required content. |
| Replace existing pictures | `list-pictures`, `replace-image-control` | The image binary should change while supported placement, wrap, size, crop, and anchor properties are preserved and verified. |
| Edit package tables | `table-*-package` | A simple HWPX table can be edited without launching HWP COM. |
| Copy rich editor content | `list-controls`, `probe-copy-from-doc`, `copy-from-doc` | The reference document already contains the formatting or object you need. |

See the [CLI reference](https://zarathucorp.github.io/openhwpsdk/cli-reference/) for examples.

## Documentation

The full documentation site is published with GitHub Pages:

- [Getting started](https://zarathucorp.github.io/openhwpsdk/getting-started/)
- [CLI reference](https://zarathucorp.github.io/openhwpsdk/cli-reference/)
- [HWPX validation workflow](https://zarathucorp.github.io/openhwpsdk/markdown-hwpx-validation-workflow/)
- [Image replacement workflow](https://zarathucorp.github.io/openhwpsdk/image-replacement/)
- [Roadmap and feature coverage](https://zarathucorp.github.io/openhwpsdk/hwp-authoring-feature-roadmap/)

To preview the documentation locally:

```bat
python -m pip install -r requirements-docs.txt
mkdocs serve
```

## Project Layout

```text
.
|-- src/
|   |-- OpenHwp.Automation/        C# COM automation wrapper
|   `-- OpenHwp.Automation.Cli/    Windows CLI and HWPX package utilities
|-- docs/                          GitHub Pages documentation source
|-- skills/                        Codex skill packaging for local workflows
|-- build.cmd                      Windows build entrypoint
`-- mkdocs.yml                     Documentation site configuration
```

## Requirements

- Windows.
- Hancom HWP/Hanword installed locally for COM-backed commands.
- MSBuild or Visual Studio Build Tools that can build .NET Framework projects.
- x86 host process for the CLI.

COM-free HWPX package commands can operate without launching HWP, but editor-backed operations still require a healthy local HWP COM installation.

## Limitations

- HWP COM behavior can vary by installed HWP version.
- Hidden-mode behavior, new-window behavior, and tab behavior are environment-dependent.
- Some package-level commands intentionally reject complex tables or shared image references when preservation cannot be proven.
- The repository does not currently declare broad support for every HWP authoring feature.

See the [roadmap](https://zarathucorp.github.io/openhwpsdk/hwp-authoring-feature-roadmap/) for the current support boundary.
