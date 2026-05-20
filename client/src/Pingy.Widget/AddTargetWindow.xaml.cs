using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using Pingy.Core.Models;
using Pingy.Widget.ViewModels;

namespace Pingy.Widget;

public partial class AddTargetWindow : Window
{
    private static readonly string[] PresetTags =
    {
        "pc", "laptop", "phone", "tablet",
        "web-server", "db-server", "app-server",
        "gateway", "router", "dns",
        "printer", "iot", "tv", "nas",
    };

    public ObservableCollection<TagChipViewModel> Chips { get; } = new();

    public string LabelText => LabelBox.Text;
    public string HostText => HostBox.Text;
    public string OwnerText => OwnerBox.Text;

    public Target? EditingTarget { get; }
    public bool IsEditing => EditingTarget is not null;
    public bool DeleteRequested { get; private set; }

    // Non-modal dialog: callers read this on the Closed event instead of a ShowDialog result.
    public bool Committed { get; private set; }

    private readonly string _initialSnapshot;
    private bool _completed;          // a normal close path (save/cancel/delete) is underway
    private bool _suppressDeactivate; // a child dialog of ours is up — ignore the lost focus

    public AddTargetWindow() : this(null) { }

    public AddTargetWindow(Target? editing)
    {
        InitializeComponent();
        EditingTarget = editing;

        var existingTagSet = (editing?.Tags ?? System.Array.Empty<string>())
            .Select(t => t.ToLowerInvariant())
            .ToHashSet();

        foreach (var tag in PresetTags)
            Chips.Add(new TagChipViewModel(tag, existingTagSet.Contains(tag)));

        // any custom tags not in presets
        var extras = existingTagSet.Where(t => !PresetTags.Contains(t)).ToList();

        TagChips.DataContext = Chips;

        if (editing is not null)
        {
            TitleText.Text = "EDIT TARGET";
            ActionVerb.Text = "+";
            LabelBox.Text = editing.Label ?? editing.Host;
            OwnerBox.Text = editing.Owner ?? "";
            HostBox.Text = editing.Host;
            CustomTagsBox.Text = string.Join(", ", extras);
            PortsBox.Text = FormatPorts(editing.Ports);
            ChecksBox.Text = FormatChecks(editing.Ports);
            DeleteButton.Visibility = Visibility.Visible;
            SaveButton.Content = "UPDATE";
        }

        // Click-outside-to-close. Snapshot taken last, once every field is populated.
        Deactivated += OnDeactivated;
        _initialSnapshot = Snapshot();
    }

    private static string FormatPorts(System.Collections.Generic.IReadOnlyList<TargetPort>? ports)
    {
        if (ports is null || ports.Count == 0) return "";
        return string.Join(", ", ports.Select(p =>
            string.IsNullOrWhiteSpace(p.Label) ? p.Number.ToString() : $"{p.Number}:{p.Label}"));
    }

    private static string FormatChecks(System.Collections.Generic.IReadOnlyList<TargetPort>? ports)
    {
        if (ports is null) return "";
        var lines = ports
            .Where(p => p.Check is not null)
            .Select(p =>
            {
                var c = p.Check!;
                var kind = string.Equals(c.Kind, "https", System.StringComparison.OrdinalIgnoreCase) ? "https" : "http";
                var path = string.IsNullOrWhiteSpace(c.Path) ? "/" : c.Path!;
                var line = $"{p.Number} {kind} {path} status={c.ExpectStatus ?? 200}";
                if (c.AcceptSelfSigned) line += " insecure";
                return line;
            });
        return string.Join(System.Environment.NewLine, lines);
    }

    public TargetPort[] CollectPorts()
    {
        var checks = CollectChecks();
        var raw = PortsBox.Text ?? "";
        var parts = raw.Split(new[] { ',', ';', ' ', '\t', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
        var seen = new System.Collections.Generic.HashSet<int>();
        var result = new System.Collections.Generic.List<TargetPort>();
        foreach (var part in parts)
        {
            var token = part.Trim();
            if (token.Length == 0) continue;

            string numberPart;
            string? label = null;
            var colon = token.IndexOf(':');
            if (colon >= 0)
            {
                numberPart = token[..colon].Trim();
                var rest = token[(colon + 1)..].Trim();
                if (rest.Length > 0) label = rest;
            }
            else
            {
                numberPart = token;
            }

            if (!int.TryParse(numberPart, out var n)) continue;
            if (n is <= 0 or > 65535) continue;
            if (!seen.Add(n)) continue;

            checks.TryGetValue(n, out var check);
            result.Add(new TargetPort(n, label, check));
        }
        return result.ToArray();
    }

    // Parses the PORT CHECKS textbox. Each line: <port> <http|https> <path> [status=NNN] [insecure].
    // Forgiving — missing path defaults to "/", missing status to 200, unknown tokens dropped.
    // A line without a valid port number or kind is skipped entirely.
    private System.Collections.Generic.Dictionary<int, ServiceCheck> CollectChecks()
    {
        var map = new System.Collections.Generic.Dictionary<int, ServiceCheck>();
        var raw = ChecksBox.Text ?? "";
        var lines = raw.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var tokens = line.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2) continue;

            if (!int.TryParse(tokens[0], out var port)) continue;
            if (port is <= 0 or > 65535) continue;

            var kind = tokens[1].ToLowerInvariant();
            if (kind is not ("http" or "https")) continue;

            string? path = null;
            int? status = null;
            var insecure = false;

            for (int i = 2; i < tokens.Length; i++)
            {
                var t = tokens[i];
                if (t.StartsWith('/'))
                {
                    path ??= t;
                }
                else if (t.StartsWith("status=", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(t.AsSpan(7), out var s) && s is > 0 and < 1000) status = s;
                }
                else if (t.Equals("insecure", System.StringComparison.OrdinalIgnoreCase))
                {
                    insecure = true;
                }
                // unknown token — silently ignored
            }

            // Preserve a JSON-only timeout_ms across a dialog round-trip (no UI for it in v1).
            var existingTimeout = EditingTarget?.Ports?
                .FirstOrDefault(p => p.Number == port)?.Check?.TimeoutMs;

            map[port] = new ServiceCheck(kind, path, status, insecure, existingTimeout);
        }
        return map;
    }

    public string[] CollectTags()
    {
        var selected = Chips.Where(c => c.IsSelected).Select(c => c.Name);
        var custom = (CustomTagsBox.Text ?? "")
            .Split(new[] { ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0);
        return selected.Concat(custom)
            .Select(s => s.ToLowerInvariant())
            .Distinct()
            .ToArray();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _completed = true;
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e) => TrySave();

    // Quick-add: append a common port to the PORTS box, skipping it if already listed.
    private void PortChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn) return;
        if (btn.Content is not string port || port.Length == 0) return;

        var current = PortsBox.Text ?? "";
        var alreadyListed = current
            .Split(new[] { ',', ';', ' ', '\t', '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Split(':')[0].Trim())
            .Any(n => n == port);

        if (!alreadyListed)
        {
            var trimmed = current.TrimEnd().TrimEnd(',', ';').TrimEnd();
            PortsBox.Text = trimmed.Length == 0 ? port : $"{trimmed}, {port}";
        }
        PortsBox.CaretIndex = (PortsBox.Text ?? string.Empty).Length;
        PortsBox.Focus();
    }

    // Validates and, on success, commits + closes. Returns false when the input is
    // incomplete (host is required) so the dialog can be kept open.
    private bool TrySave()
    {
        if (string.IsNullOrWhiteSpace(HostBox.Text))
        {
            HostBox.Focus();
            return false;
        }
        Committed = true;
        _completed = true;
        Close();
        return true;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        _suppressDeactivate = true;
        var result = MessageBox.Show(
            this,
            $"Delete target '{(string.IsNullOrWhiteSpace(LabelBox.Text) ? HostBox.Text : LabelBox.Text)}'?",
            "Confirm delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        _suppressDeactivate = false;

        if (result != MessageBoxResult.Yes) { Activate(); return; }

        DeleteRequested = true;
        Committed = true;
        _completed = true;
        Close();
    }

    // Click-outside-to-close: when the dialog loses focus, close it — but if there are
    // unsaved edits, ask first (Save / Discard / keep editing).
    private void OnDeactivated(object? sender, System.EventArgs e)
    {
        if (_completed || _suppressDeactivate) return;

        if (Snapshot() == _initialSnapshot)
        {
            _completed = true;
            Close();
            return;
        }

        _suppressDeactivate = true;
        var choice = MessageBox.Show(
            this,
            "You have unsaved changes. Save them?",
            "Unsaved changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        _suppressDeactivate = false;

        switch (choice)
        {
            case MessageBoxResult.Yes:
                if (!TrySave()) Activate();   // incomplete input — keep editing
                break;
            case MessageBoxResult.No:
                _completed = true;
                Close();
                break;
            default:                          // Cancel — keep editing
                Activate();
                break;
        }
    }

    // Change-detection fingerprint of every editable field. U+001F (a control
    // char) can't be typed into any text box, so field contents never collide.
    private string Snapshot() => string.Join((char)31,
        LabelBox.Text, OwnerBox.Text, HostBox.Text,
        CustomTagsBox.Text, PortsBox.Text, ChecksBox.Text,
        string.Join(",", Chips.Where(c => c.IsSelected).Select(c => c.Name)));
}
