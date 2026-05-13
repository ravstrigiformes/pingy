# 140 — Right-click context menu on target rows

**Status:** pending · **Owner:** unassigned · **Depends on:** v2 widget exists · **Effort:** small

## Why

Quick actions (copy IP, edit, delete, pause) without leaving the row or opening the editor. Standard desktop UX.

## Scope

Add a `ContextMenu` to each target row with these items:

| Item | Action |
|------|--------|
| ⎘ Copy IP | `Clipboard.SetText(target.Host)` |
| ⎘ Copy Label | `Clipboard.SetText(target.Label)` |
| ✎ Edit… | Same as left-click (opens editor) |
| ⏸ Pause / ▶ Resume | Toggles `Enabled` (depends on task 110) |
| 🗑 Delete… | Same as Delete in editor (with confirm) |

Style the ContextMenu to match the cyberpunk theme (cyan border, dark background, hover highlight).

## Acceptance criteria

- Right-click on any row opens the menu near the cursor
- Each action works as described
- Pause/Resume label updates based on current state (only useful with task 110)
- Menu styling matches the rest of the widget (no default Windows chrome)
- Menu doesn't pop in mini mode (or uses a simpler menu in mini mode)

## Files

- `client/src/Pingy.Widget/MainWindowV2.xaml` — add `<ContextMenu>` resource and reference on row Border
- `client/src/Pingy.Widget/MainWindowV2.xaml.cs` — handlers (Copy / Edit / TogglePause / Delete)
- `client/src/Pingy.Widget/ViewModels/MainViewModel.cs` — possibly expose helper methods (Copy doesn't need VM; Pause needs `ToggleTargetEnabledAsync` from task 110)

## Design notes

- Use `MenuItem` with custom `Template` for cyber styling (ContextMenu's default chrome is jarring against the cyber UI)
- Glyphs as Unicode characters for consistency with the other icon buttons

## Out of scope

- Customizable context menu (keep the same items for everyone)
- Submenus
