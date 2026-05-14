# Image Replacement

Use package-level image replacement when an existing HWPX picture object should keep its layout properties and only the linked image binary should change.

This is different from deleting a picture and inserting a new one through HWP COM. The package-level path preserves the existing picture control's geometry and wrapping metadata.

## Inspect Pictures

```powershell
$cli = "src\OpenHwp.Automation.Cli\bin\Release\OpenHwp.Automation.Cli.exe"
& $cli list-pictures C:\temp\template.hwpx C:\temp\picture-inventory.md
```

The report includes the package-order graphical-object index, image reference, resolved `BinData` path, pixel size, SHA256, and key placement/wrap properties.

## Replace One Picture

```powershell
& $cli replace-image-control C:\temp\template.hwpx C:\temp\replaced.hwpx --target control:gso:0 --image C:\temp\new-image.png --report C:\temp\replace-image-report.md
```

Target selectors:

- `control:gso:<index>` uses the package-order index from `list-pictures`.
- `picture:<index>` selects a picture by package picture order.
- `image:<binaryItemIDRef>` selects by image reference.

The command fails when the selected image reference or resolved `BinData` path is shared by more than one picture. That guard prevents one replacement from unexpectedly changing multiple objects.

## What The Report Proves

The replacement report records:

- source image SHA256 and pixel size;
- target picture before/after SHA256 and pixel size;
- picture and table counts;
- preserved object properties;
- sibling layout validation when `--report` is supplied.

Replacement success is gated by hash, count, and property preservation checks. A layout report with `review-needed` findings should still be reviewed visually.

## When To Use COM Instead

Use HWP COM image insertion only when the editor must create a new picture object. For existing object replacement, prefer `replace-image-control` because it does not reinterpret width/height values through the editor and does not depend on a visible HWP session.
