using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Pingy.Core.Models;

namespace Pingy.Widget.ViewModels;

public sealed partial class TargetStatusViewModel : ObservableObject
{
    public const int HistoryCapacity = 90;
    public const int FullDotsCount = 60;
    public const int MiniDotsCount = 30;
    private const double FailedRtt = 250.0;
    private const double ChartWidth = 120.0;
    private const double ChartHeight = 28.0;
    private const double MaxRttMs = 200.0;

    // Cyber-friendly health palette for sample dots
    private static readonly Brush ExcellentBrush = MakeBrush(0x22, 0xC5, 0x5E); // green
    private static readonly Brush GoodBrush      = MakeBrush(0x84, 0xCC, 0x16); // lime
    private static readonly Brush OkBrush        = MakeBrush(0xFF, 0xE6, 0x00); // yellow
    private static readonly Brush PoorBrush      = MakeBrush(0xFF, 0xA5, 0x00); // orange
    private static readonly Brush FailedBrush    = MakeBrush(0xFF, 0x2E, 0x63); // magenta-red

    // Cyberpunk palette for primary status (header indicator, badge, etc.)
    private static readonly Brush UnknownBrush = MakeBrush(0xFF, 0xE6, 0x00); // yellow init
    private static readonly Brush UpBrush      = MakeBrush(0x00, 0xF0, 0xFF); // cyan
    private static readonly Brush DownBrush    = MakeBrush(0xFF, 0x2E, 0x63); // magenta

    private readonly Queue<(double rtt, bool ok, Brush dotBrush)> _samples = new(HistoryCapacity);

    [ObservableProperty] private Target _target;
    public string Host => Target.Host;
    public string Kind => Target.Kind;
    public string Label => string.IsNullOrWhiteSpace(Target.Label) ? Target.Host : Target.Label!;
    public IReadOnlyList<string> Tags => Target.Tags ?? System.Array.Empty<string>();

    partial void OnTargetChanged(Target value)
    {
        OnPropertyChanged(nameof(Host));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Tags));
    }

    [ObservableProperty] private string _state = "INIT";
    [ObservableProperty] private string _stateBadge = "—";
    [ObservableProperty] private string _rttDisplay = "—— MS";
    [ObservableProperty] private Brush _stateBrush = UnknownBrush;
    [ObservableProperty] private System.DateTimeOffset _lastUpdate = System.DateTimeOffset.MinValue;
    [ObservableProperty] private PointCollection _latencyPolyline = new();

    // For sorting / health rollup
    public double? LastRttMs { get; private set; }
    public bool LastWasSuccess { get; private set; }
    public double RttSortKey => LastWasSuccess && LastRttMs.HasValue ? LastRttMs.Value : double.MaxValue;
    public int StatusSortKey => StateBadge switch { "DOWN" => 0, "UP" => 2, _ => 1 };

    public ObservableCollection<Brush> RecentDots { get; } = new();        // last FullDotsCount samples
    public ObservableCollection<Brush> RecentDotsMini { get; } = new();    // last MiniDotsCount samples

    public enum DropPos { None, Above, Below }
    [ObservableProperty] private DropPos _dropIndicator = DropPos.None;
    public bool IsDropAbove => DropIndicator == DropPos.Above;
    public bool IsDropBelow => DropIndicator == DropPos.Below;
    partial void OnDropIndicatorChanged(DropPos value)
    {
        OnPropertyChanged(nameof(IsDropAbove));
        OnPropertyChanged(nameof(IsDropBelow));
    }

    public TargetStatusViewModel(Target target) => _target = target;

    public void Update(PingResult result)
    {
        State = result.Status.ToUpperInvariant();
        StateBadge = result.Success ? "UP" : "DOWN";
        RttDisplay = result.Success ? $"{result.RttMs:F0} MS" : "—— MS";
        StateBrush = result.Success ? UpBrush : DownBrush;
        LastUpdate = result.At;
        LastRttMs = result.RttMs;
        LastWasSuccess = result.Success;
        OnPropertyChanged(nameof(RttSortKey));
        OnPropertyChanged(nameof(StatusSortKey));

        // Always track samples; the XAML decides between polyline / dot-strip via ShowGraph
        var rtt = result.Success ? (result.RttMs ?? 0) : FailedRtt;
        var dotBrush = ComputeDotBrush(result);
        _samples.Enqueue((rtt, result.Success, dotBrush));
        while (_samples.Count > HistoryCapacity) _samples.Dequeue();
        LatencyPolyline = BuildPolyline();
        RebuildRecentDots();
    }

    private static Brush ComputeDotBrush(PingResult r)
    {
        if (!r.Success) return FailedBrush;
        var rtt = r.RttMs ?? 0;
        if (rtt < 50) return ExcellentBrush;
        if (rtt < 150) return GoodBrush;
        if (rtt < 300) return OkBrush;
        return PoorBrush;
    }

    private void RebuildRecentDots()
    {
        var arr = _samples.ToArray();
        RebuildSlice(RecentDots, arr, FullDotsCount);
        RebuildSlice(RecentDotsMini, arr, MiniDotsCount);
    }

    private static void RebuildSlice(ObservableCollection<Brush> dest, (double rtt, bool ok, Brush dotBrush)[] arr, int count)
    {
        dest.Clear();
        var start = System.Math.Max(0, arr.Length - count);
        for (int i = start; i < arr.Length; i++)
            dest.Add(arr[i].dotBrush);
    }

    private PointCollection BuildPolyline()
    {
        var pts = new PointCollection();
        var arr = _samples.ToArray();
        if (arr.Length == 0) return pts;

        for (int i = 0; i < arr.Length; i++)
        {
            var x = arr.Length == 1 ? ChartWidth : (double)i / (arr.Length - 1) * ChartWidth;
            var clamped = System.Math.Min(System.Math.Max(arr[i].rtt, 0), MaxRttMs);
            var y = ChartHeight - (clamped / MaxRttMs) * ChartHeight;
            pts.Add(new Point(x, y));
        }
        return pts;
    }

    private static Brush MakeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
