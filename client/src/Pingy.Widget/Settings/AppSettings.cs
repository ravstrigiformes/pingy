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
// Numeric ranges mirror the UI — Opacity 0.3–1.0, Zoom 0.6–2.0; SettingsStore.Load
// sanitises out-of-range values (including 0 from a pre-v2 file) back to the
// defaults below. Adding a field here is safe: missing JSON keys fall to the
// default value of that constructor parameter.
public sealed record AppSettings(
    int Version = 2,
    CloseBehavior CloseBehavior = CloseBehavior.Ask,
    double Opacity = 0.95,
    double Zoom = 1.0,
    bool AnimationsEnabled = true,
    bool AlwaysOnTop = true);
