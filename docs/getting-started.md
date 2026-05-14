# Getting Started

This page covers the shortest path from a fresh clone to a working CLI.

## Prerequisites

- Windows.
- Hancom HWP/Hanword installed locally for COM-backed commands.
- MSBuild or Visual Studio Build Tools with .NET Framework support.

The CLI project targets x86 because HWP desktop automation is usually safer from an x86 host.

## Build

From the repository root:

```bat
build.cmd Release
```

The Release executable is written to:

```text
src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe
```

For shorter commands in PowerShell:

```powershell
$cli = "src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe"
& $cli version
```

## First Commands

Create a simple HWPX document:

```powershell
& $cli new-text C:\temp\hello.hwpx "Hello from OpenHwp"
```

Read document text:

```powershell
& $cli read-text C:\temp\hello.hwpx
```

Export to PDF through HWP COM:

```powershell
& $cli --visible export-pdf C:\temp\hello.hwpx C:\temp\hello.pdf
```

## Diagnose HWP COM

Before running editor-backed workflows, confirm the local HWP COM environment:

```powershell
& $cli --visible diagnose-com
```

Use visible mode when debugging because hidden HWP sessions can be difficult to distinguish from file locks, modal dialogs, or environment-level COM failures.

## Next Steps

- Use [CLI reference](cli-reference.md) for command examples.
- Use [HWPX validation workflow](markdown-hwpx-validation-workflow.md) when filling existing form templates.
- Use [Image replacement](image-replacement.md) when an existing picture object should keep its layout properties.
