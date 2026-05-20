namespace Pingy.Widget.Settings;

// What the close button does. Ask = not yet decided — the widget prompts the user
// on the first close and (if they opt to) persists their pick.
public enum CloseBehavior
{
    Ask,
    MinimizeToTray,
    Exit,
}

// Persisted widget preferences, separate from targets.json (which is target config).
public sealed record AppSettings(int Version = 1, CloseBehavior CloseBehavior = CloseBehavior.Ask);
