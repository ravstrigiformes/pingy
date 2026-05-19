using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Pingy.Core.Models;

namespace Pingy.Widget.ViewModels;

public sealed partial class PortStatusViewModel : ObservableObject
{
    public const int HistoryCapacity = 30;

    // Brushes intentionally mirror TargetStatusViewModel's palette so a closed port
    // looks identical to a DOWN ICMP probe — a port is meant to read as a peer signal,
    // not a footnote.
    private static readonly Brush OpenBrush = MakeBrush(0x00, 0xF0, 0xFF);    // cyan
    private static readonly Brush ClosedBrush = MakeBrush(0xFF, 0x2E, 0x63);  // magenta
    private static readonly Brush UnknownBrush = MakeBrush(0xFF, 0xE6, 0x00); // yellow init

    private readonly Queue<bool> _samples = new(HistoryCapacity);

    [ObservableProperty] private TargetPort _port;
    public int Number => Port.Number;
    public string? PortLabel => Port.Label;
    public string Display =>
        string.IsNullOrWhiteSpace(Port.Label) ? $":{Port.Number}" : $":{Port.Number} {Port.Label}";

    partial void OnPortChanged(TargetPort value)
    {
        OnPropertyChanged(nameof(Number));
        OnPropertyChanged(nameof(PortLabel));
        OnPropertyChanged(nameof(Display));
    }

    [ObservableProperty] private string _stateBadge = "—";
    [ObservableProperty] private string _rttDisplay = "—";
    [ObservableProperty] private Brush _stateBrush = UnknownBrush;
    [ObservableProperty] private string _lastStatus = "INIT";
    [ObservableProperty] private System.DateTimeOffset _lastUpdate = System.DateTimeOffset.MinValue;

    public bool HasResult { get; private set; }
    public bool LastWasOpen { get; private set; }

    public ObservableCollection<Brush> RecentDots { get; } = new();

    public PortStatusViewModel(TargetPort port) => _port = port;

    public void Update(PortProbeResult result)
    {
        HasResult = true;
        LastWasOpen = result.Success;
        StateBadge = result.Success ? "OPEN" : "CLOSED";
        RttDisplay = result.Success ? $"{result.RttMs:F0}ms" : "—";
        StateBrush = result.Success ? OpenBrush : ClosedBrush;
        LastStatus = result.Status;
        LastUpdate = result.At;

        _samples.Enqueue(result.Success);
        while (_samples.Count > HistoryCapacity) _samples.Dequeue();

        RebuildDots();
    }

    private void RebuildDots()
    {
        RecentDots.Clear();
        foreach (var ok in _samples)
            RecentDots.Add(ok ? OpenBrush : ClosedBrush);
    }

    private static Brush MakeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
