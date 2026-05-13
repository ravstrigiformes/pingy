# 230 — Custom grab/grabbing cursor (.cur resource)

**Status:** pending · **Owner:** unassigned · **Depends on:** v2 widget exists · **Effort:** small (cosmetic)

## Why

Currently the drag handle uses `Cursors.Hand` (pointing finger) — the closest stock Windows cursor to "grabbable". CSS has `cursor: grab` (open hand) and `cursor: grabbing` (closed fist) for exactly this UX, but Win32 / WPF don't ship those built-in. Result: the drag affordance reads as "click a link" rather than "grab and drag".

## Scope

Ship two custom `.cur` files embedded as resources:

| File | Purpose | Used when |
|------|---------|-----------|
| `assets/cursors/grab.cur` | Open hand | Mouse hovering over drag handle (resting state) |
| `assets/cursors/grabbing.cur` | Closed fist | Mouse held during active drag |

In WPF:

```csharp
private static readonly Cursor GrabCursor = LoadCursorFromResource("assets/cursors/grab.cur");
private static readonly Cursor GrabbingCursor = LoadCursorFromResource("assets/cursors/grabbing.cur");

private static Cursor LoadCursorFromResource(string relativePath)
{
    var uri = new Uri($"pack://application:,,,/{relativePath}", UriKind.Absolute);
    using var stream = Application.GetResourceStream(uri).Stream;
    return new Cursor(stream);
}
```

Update:
- Drag handle XAML: replace `Cursor="Hand"` with a `x:Static`-bound static field → `Cursor="{x:Static local:Cursors.Grab}"`
- `OnDragGiveFeedback` handler: switch `Mouse.SetCursor(...)` to use `GrabbingCursor` instead of `Cursors.Hand`

Add a `Cursors` static class in `client/src/Pingy.Widget/UI/Cursors.cs` exposing the two cursors.

## Acceptance criteria

- Hovering the drag handle shows an open-hand cursor
- Starting a drag (mouse-down on handle) switches to closed-fist; cursor stays closed-fist throughout the drag, even when not over a valid drop target (use `Cursors.No` overlay for invalid targets if you want fancier feedback)
- Cursors render correctly at standard DPI and on high-DPI (200%, 250%) displays
- `.cur` files are embedded as `Resource` build action (not `Content`) so they ship inside the .exe

## Files

- `client/src/Pingy.Widget/assets/cursors/grab.cur` (new — 32×32 multi-resolution .cur)
- `client/src/Pingy.Widget/assets/cursors/grabbing.cur` (new)
- `client/src/Pingy.Widget/UI/Cursors.cs` (new)
- `client/src/Pingy.Widget/Pingy.Widget.csproj` — add `<Resource Include="assets\cursors\*.cur"/>`
- `client/src/Pingy.Widget/MainWindowV2.xaml` — bind `Cursor` to the static fields
- `client/src/Pingy.Widget/MainWindowV2.xaml.cs` — update `OnDragGiveFeedback`

## Design notes

- Generate cursors with a free editor like RealWorld Cursor Editor or convert from PNG via ImageMagick. Include 16×16, 24×24, 32×32, 48×48 sizes inside the single `.cur` for DPI scaling.
- Keep the cursors monochrome cyan or white-on-transparent to fit the cyberpunk aesthetic — pure black icons feel out of place
- Public-domain CSS-style hand cursors are widely available; just make sure to redistribute compatibly

## Out of scope

- Animated cursors (.ani)
- Custom cursor for resize edges (Windows handles those well)
