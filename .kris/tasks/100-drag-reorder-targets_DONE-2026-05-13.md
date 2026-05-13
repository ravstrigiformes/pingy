# 100 — Drag-to-reorder targets in v2 widget

**Status:** ✅ DONE 2026-05-13 (rounds 6–8) · **Depends on:** v2 widget exists

**Implementation summary (delivered):**
- Drag handle column (`28 px` wide, `Cursor=Hand`, `Tag="DragHandle"`) spans the FULL row height (info + viz strips together) as a hoverable cyan rail
- Floating cyan-bordered ghost popup follows cursor during drag (label + host preview)
- Glowing 6-px insertion bar appears above/below the active drop-target row (`TargetStatusViewModel.DropPos.Above/Below` drives it)
- `MoveTargetAsync` persists the new order atomically to `targets.json`
- **Sort no longer blocks reorder**: dragging while sorted auto-resets `SelectedSort` to `MANUAL ORDER` so the result is visible (cleaner than the original "drag disabled when sorted" idea)
- `GiveFeedback` keeps `Cursors.Hand` active throughout the drag instead of falling back to system DnD chrome
- Filter still blocks (would be confusing with hidden rows)

Files touched: `MainWindowV2.xaml`, `MainWindowV2.xaml.cs`, `ViewModels/MainViewModel.cs`, `ViewModels/TargetStatusViewModel.cs`.

---

# Original brief (preserved for reference)

## Why

Users want to control target order (most-important on top). The v2 widget supports manual sort, but it's hardcoded by JSON insertion order. Drag handles let users rearrange visually.

This was deferred from round 3 of v2 because **drag-reorder interacts awkwardly with the active filter/sort layer**: when sort != Manual or filter is non-empty, the visible order ≠ underlying order, so drag would "feel teleported". Needs careful UX.

## Scope

In each target row, add a vertical drag handle (e.g., `⋮⋮` glyph) on the left edge — visible only when:
- `MainViewModel.SelectedSort.IsDefault` (sort is "MANUAL ORDER"), AND
- `MainViewModel.FilterChips.Any(c => c.IsSelected) == false` (no active filter)

When dragged:
- `MouseLeftButtonDown` on the handle starts a `DragDrop.DoDragDrop` with the source `TargetStatusViewModel`
- Each row Border has `AllowDrop="True"` and handles `DragOver` + `Drop` events
- On Drop: `MainViewModel.MoveTargetAsync(fromIndex, toIndex)` moves the item in `Targets` collection AND persists the new order to `targets.json`

Visual feedback during drag:
- The dragged row dims to 50% opacity
- A horizontal cyan glow line appears between rows showing where it'll insert
- ESC cancels the drag

Add `MoveTargetAsync(int fromIdx, int toIdx)` to `MainViewModel`:

```csharp
public async Task MoveTargetAsync(int fromIdx, int toIdx)
{
    if (_currentConfig is null) return;
    if (fromIdx == toIdx) return;

    Targets.Move(fromIdx, toIdx);

    var ordered = Targets.Select(t => t.Target).ToArray();
    var updated = _currentConfig with { Targets = ordered };
    await _loader.SaveAsync(updated);
    _currentConfig = updated;
}
```

When user toggles sort to non-Manual or applies a filter, the drag handles fade out (opacity 0.2 + IsHitTestVisible=false) — clear visual signal that drag is disabled.

## Acceptance criteria

- Drag handle visible on each row only in MANUAL ORDER + no filters
- Drag-and-drop reorders the row visually
- New order persists to `targets.json` (verify by reopening the app)
- Drag handle hidden/disabled when sort != Manual or filter active
- ESC during drag cancels (no reorder)
- No crashes if dragged onto self
- Clicking the drag handle (without dragging) does NOT trigger the row's edit-on-click

## Files

- `client/src/Pingy.Widget/MainWindowV2.xaml` — add drag handle column to row template + drag/drop event handlers
- `client/src/Pingy.Widget/MainWindowV2.xaml.cs` — drag-drop handler implementation
- `client/src/Pingy.Widget/ViewModels/MainViewModel.cs` — `MoveTargetAsync` method + `CanReorder` computed property

## Design notes

- WPF's `DragDrop.DoDragDrop(visual, data, allowedEffects)` is synchronous (modal pump) — handler returns when drop completes
- Drop position determined by Y-coordinate of drop event relative to target row's mid-Y: if drop Y < mid-Y, insert before; else after
- The visual drop indicator is most easily a horizontal `Rectangle` (Width=row, Height=2, Fill=cyan glow) inserted at the right place via a Canvas overlay or Border insertions
- For the `CanReorder` computed: bind drag handle visibility to `{Binding DataContext.CanReorder, RelativeSource={RelativeSource AncestorType=Window}, Converter={StaticResource BoolToVis}}`. `CanReorder = SelectedSort.IsDefault && !FilterChips.Any(c => c.IsSelected)`
- Don't use ListBox for this — keeping ItemsControl avoids selection visual that we don't want; manual drag-drop on each row Border is cleaner

## Out of scope

- Multi-select drag (one row at a time)
- Cross-window drag (no support for moving between widget instances)
- Touch-based drag (mouse only for v1)
