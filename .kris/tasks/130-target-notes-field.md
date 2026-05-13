# 130 — Notes field per target

**Status:** pending · **Owner:** unassigned · **Depends on:** v2 widget exists · **Effort:** small

## Why

Targets accumulate context over time: "this is the printer in the back office", "responsible: Jane in IT", "rebuilt 2026-04, watch for cert renewal". A free-form notes field captures this without polluting tags or labels.

## Scope

Add to `Target`:

```csharp
public sealed record Target(
    // ... existing fields ...,
    string? Notes = null);
```

In `AddTargetWindow`:
- Add a multi-line `TextBox` between TAGS and the action buttons
- Label: "NOTES (optional)"
- Auto-grow up to 4 lines, then scroll
- Pre-populate when editing

In `MainWindowV2.xaml` row tooltip:
- If Notes is non-empty, append to row tooltip (which currently shows just Label)
- Format: `{Label} — {Notes}` (truncate notes to ~100 chars)

## Acceptance criteria

- Notes field saves to `targets.json` (`"notes": "..."` when present, omitted when null/empty)
- Editor shows existing notes when editing a target
- Hovering a row shows notes in tooltip
- No regression on existing targets without notes field

## Files

- `client/src/Pingy.Core/Models/Target.cs`
- `client/src/Pingy.Widget/AddTargetWindow.xaml` + `.cs`
- `client/src/Pingy.Widget/MainWindowV2.xaml` — extend row tooltip
- `client/src/Pingy.Widget/ViewModels/MainViewModel.cs` — pass notes through `BuildTarget`

## Out of scope

- Markdown rendering in notes
- Hyperlink detection
