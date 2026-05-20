using System;
using System.Drawing;
using System.Windows.Forms;

namespace Pingy.Widget;

// Thin wrapper over the WinForms NotifyIcon — the only file that touches WinForms.
// The icon stays visible for the whole session (Pingy is a background monitor).
// Gestures: single-click = glance (compact mini view), double-click = open full,
// right-click = menu. WinForms raises both a MouseClick and a MouseDoubleClick on a
// double-click, so a short timer arbitrates: a click arms it, a double-click cancels
// it, and if it elapses the gesture was a genuine single click. Callbacks fire on
// the UI thread (NotifyIcon raises events on its creating thread).
internal sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly System.Windows.Forms.Timer _clickTimer;
    private readonly Action _onSingleClick;
    private readonly Action _onDoubleClick;
    private readonly Action _onExit;
    private bool _disposed;

    public TrayIcon(Action onSingleClick, Action onDoubleClick, Action onExit)
    {
        _onSingleClick = onSingleClick;
        _onDoubleClick = onDoubleClick;
        _onExit = onExit;

        _clickTimer = new System.Windows.Forms.Timer
        {
            Interval = Math.Max(200, SystemInformation.DoubleClickTime + 80),
        };
        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer.Stop();
            _onSingleClick();
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Glance (mini)", null, (_, _) => _onSingleClick());
        menu.Items.Add("Open Pingy", null, (_, _) => _onDoubleClick());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => _onExit());

        _icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "Pingy — network monitor",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.MouseClick += OnMouseClick;
        _icon.MouseDoubleClick += OnMouseDoubleClick;
    }

    public void ShowBalloon(string message)
    {
        try { _icon.ShowBalloonTip(3000, "Pingy", message, ToolTipIcon.Info); }
        catch { /* balloon tips are best-effort */ }
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;   // right-click opens the context menu
        _clickTimer.Stop();
        _clickTimer.Start();
    }

    private void OnMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        _clickTimer.Stop();   // a double-click landed — cancel the pending single-click
        _onDoubleClick();
    }

    private static Icon LoadAppIcon()
    {
        // Preferred: the bundled multi-size pingy.ico — NotifyIcon picks the DPI-right frame.
        try
        {
            var res = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/pingy.ico"));
            if (res?.Stream is { } stream)
            {
                using (stream)
                    return new Icon(stream);
            }
        }
        catch { /* fall through */ }

        // Fallback: the icon embedded in the running .exe via <ApplicationIcon>.
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var fromExe = Icon.ExtractAssociatedIcon(exe);
                if (fromExe is not null) return fromExe;
            }
        }
        catch { /* fall through */ }

        return SystemIcons.Application;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _clickTimer.Stop();
        _clickTimer.Dispose();
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
