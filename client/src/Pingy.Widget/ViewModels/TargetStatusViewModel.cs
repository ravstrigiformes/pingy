using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
    private const double FailedRtt = 600.0;
    private const double ChartWidth = 120.0;
    private const double ChartHeight = 28.0;
    private const double MaxRttMs = 600.0;

    // Number of timeouts tolerated within the displayed window before they
    // influence the worst-of-window color. <= this many timeouts are treated
    // as transient blips and skipped when computing the row's accent.
    private const int RtoGraceCount = 2;

    // Latency tiers — sorted ascending severity (LAN best, FAIL worst).
    public enum Tier { Lan = 0, Good = 1, Fair = 2, Poor = 3, Bad = 4, Fail = 5, Unknown = -1 }

    private static readonly Brush LanBrush = MakeBrush(0x00, 0xF0, 0xFF); // cyan
    private static readonly Brush GoodBrush = MakeBrush(0x22, 0xC5, 0x5E); // green
    private static readonly Brush FairBrush = MakeBrush(0xFF, 0xE6, 0x00); // yellow
    private static readonly Brush PoorBrush = MakeBrush(0xFF, 0x8C, 0x00); // orange
    private static readonly Brush BadBrush = MakeBrush(0xFF, 0x2E, 0x63); // red/magenta
    private static readonly Brush FailBrush = MakeBrush(0xFF, 0x2E, 0x63); // same as bad
    private static readonly Brush UnknownBrush = MakeBrush(0xFF, 0xE6, 0x00); // yellow init

    private static Brush BrushFor(Tier t) => t switch
    {
        Tier.Lan => LanBrush,
        Tier.Good => GoodBrush,
        Tier.Fair => FairBrush,
        Tier.Poor => PoorBrush,
        Tier.Bad => BadBrush,
        Tier.Fail => FailBrush,
        _ => UnknownBrush,
    };

    private readonly Queue<Sample> _samples = new(HistoryCapacity);

    [ObservableProperty] private Target _target;
    public string Host => Target.Host;
    public string Kind => Target.Kind;
    public string Label => string.IsNullOrWhiteSpace(Target.Label) ? Target.Host : Target.Label!;
    public IReadOnlyList<string> Tags => Target.Tags ?? System.Array.Empty<string>();

    public ObservableCollection<PortStatusViewModel> Ports { get; } = new();
    public bool HasPorts => Ports.Count > 0;

    partial void OnTargetChanged(Target value)
    {
        OnPropertyChanged(nameof(Host));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Tags));
        SyncPorts(value.Ports);
    }

    // Reconcile the Ports VM collection against the Target's port list. Same port-number
    // VMs are preserved (so history isn't reset on a label edit); added/removed ports
    // are added/removed in place.
    private void SyncPorts(IReadOnlyList<TargetPort>? desired)
    {
        var want = desired ?? System.Array.Empty<TargetPort>();
        var byNumber = Ports.ToDictionary(p => p.Number);

        // Remove ports no longer wanted.
        var keep = want.Select(p => p.Number).ToHashSet();
        for (int i = Ports.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(Ports[i].Number)) Ports.RemoveAt(i);
        }

        // Add or refresh.
        for (int i = 0; i < want.Count; i++)
        {
            if (byNumber.TryGetValue(want[i].Number, out var existing))
            {
                if (!Equals(existing.Port, want[i])) existing.Port = want[i];
                // Move to correct index if order shifted.
                var currentIdx = Ports.IndexOf(existing);
                if (currentIdx != i && i < Ports.Count) Ports.Move(currentIdx, i);
            }
            else
            {
                Ports.Insert(System.Math.Min(i, Ports.Count), new PortStatusViewModel(want[i]));
            }
        }

        OnPropertyChanged(nameof(HasPorts));
        RecomputeAccent();
    }

    [ObservableProperty] private string _state = "INIT";
    [ObservableProperty] private string _stateBadge = "—";
    [ObservableProperty] private string _rttDisplay = "—— MS";
    // Live status (UP/DOWN/INIT) — cyan/magenta/yellow. Used for the small status badge text only.
    [ObservableProperty] private Brush _stateBrush = UnknownBrush;
    // Row accent: worst-tier brush computed from the displayed sample window (with RTO grace).
    [ObservableProperty] private Brush _accentBrush = UnknownBrush;
    [ObservableProperty] private System.DateTimeOffset _lastUpdate = System.DateTimeOffset.MinValue;
    [ObservableProperty] private PointCollection _latencyPolyline = new();

    // For sorting / health rollup
    public double? LastRttMs { get; private set; }
    public bool LastWasSuccess { get; private set; }
    public double RttSortKey => LastWasSuccess && LastRttMs.HasValue ? LastRttMs.Value : double.MaxValue;
    public int StatusSortKey => StateBadge switch { "DOWN" => 0, "UP" => 2, _ => 1 };

    public ObservableCollection<Brush> RecentDots { get; } = new();        // last FullDotsCount samples
    public ObservableCollection<Brush> RecentDotsMini { get; } = new();    // last MiniDotsCount samples

    // Drop position is column-flow oriented: Before = visually to the left of this card
    // (or wrap-to-end-of-previous-row), After = visually to the right. Renamed from Above/Below
    // when the layout switched from single-column rows to multi-column WrapPanel.
    public enum DropPos { None, Before, After }
    [ObservableProperty] private DropPos _dropIndicator = DropPos.None;
    public bool IsDropBefore => DropIndicator == DropPos.Before;
    public bool IsDropAfter => DropIndicator == DropPos.After;
    partial void OnDropIndicatorChanged(DropPos value)
    {
        OnPropertyChanged(nameof(IsDropBefore));
        OnPropertyChanged(nameof(IsDropAfter));
    }

    public TargetStatusViewModel(Target target)
    {
        _target = target;
        SyncPorts(target.Ports);
    }

    public void UpdatePort(PortProbeResult result)
    {
        var vm = Ports.FirstOrDefault(p => p.Number == result.Port);
        if (vm is null) return;
        vm.Update(result);
        RecomputeAccent();
    }

    public void Update(PingResult result)
    {
        State = result.Status.ToUpperInvariant();
        StateBadge = result.Success ? "UP" : "DOWN";
        RttDisplay = result.Success ? $"{result.RttMs:F0} MS" : "—— MS";
        StateBrush = result.Success
            ? MakeBrush(0x00, 0xF0, 0xFF)   // live cyan for UP
            : MakeBrush(0xFF, 0x2E, 0x63);  // live magenta for DOWN
        LastUpdate = result.At;
        LastRttMs = result.RttMs;
        LastWasSuccess = result.Success;
        OnPropertyChanged(nameof(RttSortKey));
        OnPropertyChanged(nameof(StatusSortKey));

        var rtt = result.Success ? (result.RttMs ?? 0) : FailedRtt;
        var tier = ComputeTier(result);
        _samples.Enqueue(new Sample(rtt, result.Success, tier));
        while (_samples.Count > HistoryCapacity) _samples.Dequeue();

        LatencyPolyline = BuildPolyline();
        RebuildRecentDots();
        RecomputeAccent();
    }

    // Tier thresholds (round-trip ms):
    //   < 10  -> LAN (intranet-grade)
    //   < 100 -> GOOD (healthy internet)
    //   < 250 -> FAIR (usable but noticeable)
    //   < 500 -> POOR (degraded)
    //   >=500 -> BAD
    //   timeout -> FAIL
    private static Tier ComputeTier(PingResult r)
    {
        if (!r.Success) return Tier.Fail;
        var rtt = r.RttMs ?? 0;
        if (rtt < 10) return Tier.Lan;
        if (rtt < 100) return Tier.Good;
        if (rtt < 250) return Tier.Fair;
        if (rtt < 500) return Tier.Poor;
        return Tier.Bad;
    }

    private void RebuildRecentDots()
    {
        var arr = _samples.ToArray();
        RebuildSlice(RecentDots, arr, FullDotsCount);
        RebuildSlice(RecentDotsMini, arr, MiniDotsCount);
    }

    private static void RebuildSlice(ObservableCollection<Brush> dest, Sample[] arr, int count)
    {
        dest.Clear();
        var start = System.Math.Max(0, arr.Length - count);
        for (int i = start; i < arr.Length; i++)
            dest.Add(BrushFor(arr[i].Tier));
    }

    // Accent = worst tier observed in the displayed window (FullDotsCount most-recent samples),
    // with one quirk: up to RtoGraceCount timeouts are treated as transient blips and ignored.
    // If more than RtoGraceCount timeouts occur in the window, FAIL wins outright.
    private void RecomputeAccent()
    {
        if (_samples.Count == 0)
        {
            AccentBrush = UnknownBrush;
            return;
        }

        var arr = _samples.ToArray();
        var start = System.Math.Max(0, arr.Length - FullDotsCount);
        int failCount = 0;
        Tier worstNonFail = Tier.Lan;
        bool sawAny = false;
        for (int i = start; i < arr.Length; i++)
        {
            var s = arr[i];
            if (s.Tier == Tier.Fail) { failCount++; continue; }
            sawAny = true;
            if (s.Tier > worstNonFail) worstNonFail = s.Tier;
        }

        Tier accent;
        if (failCount > RtoGraceCount) accent = Tier.Fail;
        else if (!sawAny) accent = Tier.Fail; // window is entirely timeouts but <= grace? still all-fail
        else accent = worstNonFail;

        // Port health rolls into the card accent. A DOWN port (TCP refused/timeout) is a
        // hard failure regardless of ICMP. A DEGRADED port (TCP up, L7 check failing) only
        // bumps the card to Poor/amber when ICMP itself is up — if ICMP is down the window
        // already produced Fail and that red wins.
        bool anyPortDown = Ports.Any(p => p.HasResult && p.LastHealth == PortHealth.Down);
        bool anyPortDegraded = Ports.Any(p => p.HasResult && p.LastHealth == PortHealth.Degraded);

        if (anyPortDown)
            accent = Tier.Fail;
        else if (anyPortDegraded && LastWasSuccess && accent < Tier.Poor)
            accent = Tier.Poor;

        AccentBrush = BrushFor(accent);
    }

    private PointCollection BuildPolyline()
    {
        var pts = new PointCollection();
        var arr = _samples.ToArray();
        if (arr.Length == 0) return pts;

        for (int i = 0; i < arr.Length; i++)
        {
            var x = arr.Length == 1 ? ChartWidth : (double)i / (arr.Length - 1) * ChartWidth;
            var clamped = System.Math.Min(System.Math.Max(arr[i].Rtt, 0), MaxRttMs);
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

    private readonly record struct Sample(double Rtt, bool Ok, Tier Tier);
}
