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

    public Target? EditingTarget { get; }
    public bool IsEditing => EditingTarget is not null;
    public bool DeleteRequested { get; private set; }

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
            HostBox.Text = editing.Host;
            CustomTagsBox.Text = string.Join(", ", extras);
            PortsBox.Text = FormatPorts(editing.Ports);
            DeleteButton.Visibility = Visibility.Visible;
            SaveButton.Content = "UPDATE";
        }
    }

    private static string FormatPorts(System.Collections.Generic.IReadOnlyList<TargetPort>? ports)
    {
        if (ports is null || ports.Count == 0) return "";
        return string.Join(", ", ports.Select(p =>
            string.IsNullOrWhiteSpace(p.Label) ? p.Number.ToString() : $"{p.Number}:{p.Label}"));
    }

    public TargetPort[] CollectPorts()
    {
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
            result.Add(new TargetPort(n, label));
        }
        return result.ToArray();
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
        DialogResult = false;
        Close();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(HostBox.Text))
        {
            HostBox.Focus();
            return;
        }
        DialogResult = true;
        Close();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            this,
            $"Delete target '{(string.IsNullOrWhiteSpace(LabelBox.Text) ? HostBox.Text : LabelBox.Text)}'?",
            "Confirm delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        DeleteRequested = true;
        DialogResult = true;
        Close();
    }
}
