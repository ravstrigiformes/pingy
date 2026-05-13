# 180 — Keyboard shortcuts for target reorder + edit

**Status:** pending · **Owner:** unassigned · **Depends on:** v2 widget exists · **Effort:** small

## Why

Power users want to reorder/edit targets without reaching for the mouse. Drag-handles are great for novices; keyboard shortcuts are faster for repeat operations and add accessibility (motor-impaired users can't easily drag).

## Scope

Add keyboard input handlers on the targets `ItemsControl` (or each row) for the FOCUSED row:

| Shortcut | Action |
|----------|--------|
| `↑` / `↓` | Move focus to previous/next row |
| `Alt+↑` | Move focused row up one position (calls `MoveTargetAsync(idx, idx-1)`) |
| `Alt+↓` | Move focused row down one position |
| `Enter` / `F2` | Open editor for focused row |
| `Delete` | Delete focused row (with same confirm dialog as the editor's DELETE button) |
| `Ctrl+N` | Open the "+ ADD TARGET" dialog |
| `Esc` | Close any dialog / cancel drag |

For focus visualization: each row shows a thin cyan outline when keyboard-focused (separate from mouse hover).

Implementation:
- Make each row Border `Focusable=True`, with a `Style` that adds a `KeyboardFocused`-trigger glow
- `KeyDown` handler on the Border (or window-level via input bindings) checks modifiers + key, dispatches the action

## Acceptance criteria

- Tab/Shift-Tab cycles into the targets list and through rows
- Arrow keys move focus
- Alt+↑/Alt+↓ reorder respects all the same rules as drag (auto-reset sort if non-default, persists immediately, blocked by active filter)
- Enter opens editor, Delete prompts for confirm, Ctrl+N opens add dialog
- Visual focus indicator is clearly distinguishable from hover

## Files

- `client/src/Pingy.Widget/MainWindowV2.xaml` — add focus visual style + InputBindings on the row Border or ItemsControl
- `client/src/Pingy.Widget/MainWindowV2.xaml.cs` — KeyDown handler dispatching to MainViewModel methods
- `client/src/Pingy.Widget/ViewModels/MainViewModel.cs` — convenience helpers if needed (e.g., `MoveByOffsetAsync(targetId, offset)`)

## Design notes

- Use `RoutedCommand` or `InputBindings` on the Window for global shortcuts (Ctrl+N), and per-row KeyDown for row-scoped (Alt+arrows, Delete, Enter)
- Make sure typing in the Add/Edit dialog's TextBoxes doesn't intercept the global shortcuts inappropriately
- For the focus outline: a `Trigger Property="IsKeyboardFocusWithin" Value="True"` on `TargetRowBorder` style with a cyan border + glow

## Out of scope

- Vim-style keybindings
- User-customizable shortcuts (defer to a settings UI)
- Multi-row selection (different feature)
