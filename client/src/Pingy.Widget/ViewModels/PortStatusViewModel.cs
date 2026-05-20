using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Pingy.Core.Models;

namespace Pingy.Widget.ViewModels;

public sealed partial class PortStatusViewModel : ObservableObject
{
    public const int HistoryCapacity = 30;

    // Tri-state palette. Ok mirrors the parent's cyan; Degraded is amber (TCP up,
    // L7 failing — the "service is half-broken" signal); Down is magenta.
    private static readonly Brush OkBrush = MakeBrush(0x00, 0xF0, 0xFF);       // cyan
    private static readonly Brush DegradedBrush = MakeBrush(0xFF, 0xB0, 0x00); // amber
    private static readonly Brush DownBrush = MakeBrush(0xFF, 0x2E, 0x63);     // magenta
    private static readonly Brush UnknownBrush = MakeBrush(0xFF, 0xE6, 0x00);  // yellow init

    private static Brush BrushFor(PortHealth h) => h switch
    {
        PortHealth.Ok => OkBrush,
        PortHealth.Degraded => DegradedBrush,
        PortHealth.Down => DownBrush,
        _ => UnknownBrush,
    };

    private readonly Queue<PortHealth> _samples = new(HistoryCapacity);

    [ObservableProperty] private TargetPort _port;
    public int Number => Port.Number;
    public string? PortLabel => Port.Label;
    public string Display =>
        string.IsNullOrWhiteSpace(Port.Label) ? $":{Port.Number}" : $":{Port.Number} {Port.Label}";

    // Whether this port carries an L7 check — used to decide tooltip wording.
    public bool HasCheck => Port.Check is not null;

    partial void OnPortChanged(TargetPort value)
    {
        OnPropertyChanged(nameof(Number));
        OnPropertyChanged(nameof(PortLabel));
        OnPropertyChanged(nameof(Display));
        OnPropertyChanged(nameof(HasCheck));
    }

    [ObservableProperty] private string _stateBadge = "—";
    [ObservableProperty] private string _rttDisplay = "—";
    [ObservableProperty] private Brush _stateBrush = UnknownBrush;
    [ObservableProperty] private string _lastStatus = "INIT";
    [ObservableProperty] private string _tooltip = "Awaiting first probe…";
    [ObservableProperty] private System.DateTimeOffset _lastUpdate = System.DateTimeOffset.MinValue;

    public bool HasResult { get; private set; }
    public PortHealth LastHealth { get; private set; } = PortHealth.Unknown;

    public ObservableCollection<Brush> RecentDots { get; } = new();

    public PortStatusViewModel(TargetPort port) => _port = port;

    public void Update(PortProbeResult result)
    {
        HasResult = true;
        LastHealth = result.Health;

        StateBadge = result.Health switch
        {
            PortHealth.Ok => "OK",
            PortHealth.Degraded => "DEGR",
            PortHealth.Down => "DOWN",
            _ => "—",
        };
        StateBrush = BrushFor(result.Health);
        RttDisplay = FormatRtt(result);
        LastStatus = result.L7Status ?? result.Status;
        Tooltip = BuildTooltip(result);
        LastUpdate = result.At;

        _samples.Enqueue(result.Health);
        while (_samples.Count > HistoryCapacity) _samples.Dequeue();

        RebuildDots();
    }

    private static string FormatRtt(PortProbeResult r)
    {
        if (r.Health == PortHealth.Down) return "—";
        if (r.L7RttMs is double l7 && r.RttMs is double tcp)
            return $"{tcp:F0}+{l7:F0}ms";
        if (r.RttMs is double only)
            return $"{only:F0}ms";
        return "—";
    }

    private string BuildTooltip(PortProbeResult r)
    {
        var l4 = $"port {r.Port} — TCP {r.Status}";
        if (r.RttMs is double tcp) l4 += $" ({tcp:F0}ms)";

        if (!HasCheck || r.L7Status is null)
            return l4;

        var l7 = $"L7 {r.L7Status}";
        if (r.L7RttMs is double rt) l7 += $" ({rt:F0}ms)";
        var verdict = r.Health switch
        {
            PortHealth.Ok => "service OK",
            PortHealth.Degraded => "service DEGRADED",
            _ => "service DOWN",
        };
        return $"{l4}\n{l7} — {verdict}";
    }

    private void RebuildDots()
    {
        RecentDots.Clear();
        foreach (var h in _samples)
            RecentDots.Add(BrushFor(h));
    }

    private static Brush MakeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
