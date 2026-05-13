# 220 — Export widget state as PNG (incident screenshot)

**Status:** pending · **Owner:** unassigned · **Depends on:** v2 widget exists · **Effort:** small

## Why

When opening an incident ticket, IT wants to attach "what does pingy show right now?". Built-in screenshot export saves them from Win+Shift+S, cropping, etc.

## Scope

Add an "EXPORT PNG" menu item (could live in the same overflow menu as task 150 export/import).

Implementation:

```csharp
private void ExportAsImage()
{
    var rtb = new RenderTargetBitmap(
        (int)ActualWidth, (int)ActualHeight,
        96, 96, PixelFormats.Pbgra32);
    rtb.Render(this);

    var encoder = new PngBitmapEncoder();
    encoder.Frames.Add(BitmapFrame.Create(rtb));

    var dlg = new SaveFileDialog {
        FileName = $"pingy-{DateTime.Now:yyyy-MM-dd-HHmm}.png",
        Filter = "PNG image (*.png)|*.png"
    };
    if (dlg.ShowDialog() != true) return;

    using var fs = File.Create(dlg.FileName);
    encoder.Save(fs);
}
```

Bonus: a "COPY TO CLIPBOARD" option that puts the bitmap directly on the clipboard for paste.

## Acceptance criteria

- PNG file is exported with current widget state (including current dots/polyline)
- File matches the visible widget at native pixel size
- Transparent areas of the window export as transparent in the PNG
- "Copy to clipboard" puts the image on `Clipboard.SetImage`
- No flicker / window state change during export

## Files

- `client/src/Pingy.Widget/MainWindowV2.xaml` — menu item
- `client/src/Pingy.Widget/MainWindowV2.xaml.cs` — `ExportPng_Click`, `CopyToClipboard_Click`

## Design notes

- `RenderTargetBitmap.Render(this)` captures the current visual tree at full resolution
- For mini mode, just exports the mini view (smaller PNG)
- The animated scanline/border-glow may be captured in mid-animation — that's fine

## Out of scope

- Animated GIF export
- Video recording
- Direct upload to ticketing system (handled by the bigger backend integration in v1 plan)
