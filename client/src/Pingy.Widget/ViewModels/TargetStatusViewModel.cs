using System;
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

    // The single window used for the chart, the color rule, and the max/avg stats.
    // Keep XAML's marker UniformGrid Columns in sync with this (60).
    private const int Window = FullDotsCount;

    private const double ChartWidth = 120.0;
    private const double ChartHeight = 28.0;
    private const double ChartPad = 3.0;

    // Timeout color thresholds, evaluated over the most-recent Window samples.
    //   run of >= RtoRedRun consecutive timeouts        -> red
    //   >= RtoRedTotal total timeouts in the window     -> red
    //   any timeout(s) present but below both           -> amber (never green)
    private const int RtoRedRun = 3;
    private const int RtoRedTotal = 6;

    // Latency tiers — sorted ascending severity (LAN best, FAIL worst).
    public enum Tier { Lan = 0, Good = 1, Fair = 2, Poor = 3, Bad = 4, Fail = 5, Unknown = -1 }

    private static readonly Brush LanBrush = MakeBrush(0x00, 0xF0, 0xFF); // cyan
    private static readonly Brush GoodBrush = MakeBrush(0x22, 0xC5, 0x5E); // green
    private static readonly Brush FairBrush = MakeBrush(0xFF, 0xE6, 0x00); // yellow (amber)
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
    private bool _hasResult;
    private double? _avgMs;
    private double? _maxMs;

    [ObservableProperty] private Target _target;
    public string Host => Target.Host;
    public string Kind => Target.Kind;
    public string Label => string.IsNullOrWhiteSpace(Target.Label) ? Target.Host : Target.Label!;
    public IReadOnlyList<string> Tags => Target.Tags ?? Array.Empty<string>();
    public string? Owner => Target.Owner;
    public bool HasOwner => !string.IsNullOrWhiteSpace(Target.Owner);

    public ObservableCollection<PortStatusViewModel> Ports { get; } = new();
    public bool HasPorts => Ports.Count > 0;

    partial void OnTargetChanged(Target value)
    {
        OnPropertyChanged(nameof(Host));
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Tags));
        OnPropertyChanged(nameof(Owner));
        OnPropertyChanged(nameof(HasOwner));
        SyncPorts(value.Ports);
    }

    // Reconcile the Ports VM collection against the Target's port list. Same port-number
    // VMs are preserved (so history isn't reset on a label edit); added/removed ports
    // are added/removed in place.
    private void SyncPorts(IReadOnlyList<TargetPort>? desired)
    {
        var want = desired ?? Array.Empty<TargetPort>();
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
                Ports.Insert(Math.Min(i, Ports.Count), new PortStatusViewModel(want[i]));
            }
        }

        OnPropertyChanged(nameof(HasPorts));
        RecomputeAccent();
    }

    [ObservableProperty] private string _state = "INIT";
    [ObservableProperty] private string _stateBadge = "—";
    // Raw current-ping text: "12 MS" on success, "TIMEOUT" on a timed-out ping.
    [ObservableProperty] private string _rttDisplay = "—— MS";
    // Live status (UP/DOWN/INIT) — cyan/magenta/yellow. Used for the small status badge text only.
    [ObservableProperty] private Brush _stateBrush = UnknownBrush;
    // Device accent: ICMP-only health (latency tiers + the timeout rule). Drives the top bar,
    // the latency number, and the chart line. Port health is deliberately excluded — an
    // unreachable port must not paint the whole card red when the device itself is responding.
    [ObservableProperty] private Brush _accentBrush = UnknownBrush;
    // Overall rollup: device accent merged with port health (worst wins). Drives the diamond
    // (full mode) and circle (mini mode) only — the at-a-glance "is anything wrong here" dot.
    [ObservableProperty] private Brush _overallBrush = UnknownBrush;
    [ObservableProperty] private DateTimeOffset _lastUpdate = DateTimeOffset.MinValue;

    // Chart: success-only latency line (true gaps at timeouts) + per-slot timeout flags.
    [ObservableProperty] private Geometry _latencyGeometry = Geometry.Empty;
    public ObservableCollection<bool> TimeoutFlags { get; } = new();

    // -- Primary number + max/avg stat stack (driven by the global cycler) --

    [ObservableProperty] private string _maxValue = "—";       // window max (successful pings)
    [ObservableProperty] private string _secondaryLabel = "AVG";
    [ObservableProperty] private string _secondaryValue = "—";
    [ObservableProperty] private string _primaryValue = "—— MS"; // the big number
    [ObservableProperty] private Brush _primaryBrush = UnknownBrush;
    [ObservableProperty] private bool _primaryIsAverage;          // drives the small "AVG" tag

    // Set by MainViewModel's global cycler. True => the big number shows the window average.
    private bool _showAvgAsPrimary;
    public bool ShowAvgAsPrimary
    {
        get => _showAvgAsPrimary;
        set
        {
            if (_showAvgAsPrimary == value) return;
            _showAvgAsPrimary = value;
            RefreshStatProjections();
        }
    }

    partial void OnAccentBrushChanged(Brush value) => RefreshStatProjections();

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
        RefreshState();      // a port coming up/down can flip the badge (DOWN <-> NO PING)
        RecomputeAccent();
    }

    // Status badge + live brush. A ping failure is only a red "DOWN" when nothing else
    // answers — if a configured port is reachable the device is alive (ICMP is just
    // blocked/filtered), so it reads "NO PING" in amber instead.
    private void RefreshState()
    {
        if (!_hasResult) return;
        if (LastWasSuccess)
        {
            StateBadge = "UP";
            StateBrush = LanBrush;       // cyan
        }
        else if (AnyPortReachable())
        {
            StateBadge = "NO PING";
            StateBrush = FairBrush;      // amber
        }
        else
        {
            StateBadge = "DOWN";
            StateBrush = FailBrush;      // magenta
        }
    }

    // A port is proof-of-life if TCP connected — Ok or Degraded (Degraded = TCP up, only
    // the L7 check failing). Down = TCP refused/timed out, which is not proof of anything.
    private bool AnyPortReachable() =>
        Ports.Any(p => p.HasResult && p.LastHealth is PortHealth.Ok or PortHealth.Degraded);

    public void Update(PingResult result)
    {
        State = result.Status.ToUpperInvariant();
        RttDisplay = result.Success ? $"{result.RttMs:F0} MS" : "TIMEOUT";
        LastUpdate = result.At;
        LastRttMs = result.RttMs;
        LastWasSuccess = result.Success;
        _hasResult = true;
        RefreshState();
        OnPropertyChanged(nameof(RttSortKey));
        OnPropertyChanged(nameof(StatusSortKey));

        var rtt = result.Success ? (result.RttMs ?? 0) : 0;
        var tier = ComputeTier(result);
        _samples.Enqueue(new Sample(rtt, result.Success, tier));
        while (_samples.Count > HistoryCapacity) _samples.Dequeue();

        LatencyGeometry = BuildLatencyGeometry();
        RebuildRecentDots();
        RebuildTimeoutFlags();
        RecomputeAccent();
        RecomputeStats();
    }

    // Tier thresholds (round-trip ms):
    //   < 10  -> LAN  (cyan   — excellent: LAN/local, instant)
    //   < 100 -> GOOD (green  — healthy, normal)
    //   < 200 -> FAIR (yellow — tolerable: noticeable lag)
    //   < 350 -> POOR (orange — warning: degraded, investigate)
    //   >=350 -> BAD  (red    — alert: unusable)
    //   timeout -> FAIL (red  — alert)
    private static Tier ComputeTier(PingResult r)
    {
        if (!r.Success) return Tier.Fail;
        var rtt = r.RttMs ?? 0;
        if (rtt < 10) return Tier.Lan;
        if (rtt < 100) return Tier.Good;
        if (rtt < 200) return Tier.Fair;
        if (rtt < 350) return Tier.Poor;
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
        var start = Math.Max(0, arr.Length - count);
        for (int i = start; i < arr.Length; i++)
            dest.Add(BrushFor(arr[i].Tier));
    }

    // One bool per chart slot (oldest..newest), true where the ping timed out.
    // Drives the magenta "x" marker overlay on the chart.
    private void RebuildTimeoutFlags()
    {
        TimeoutFlags.Clear();
        var arr = _samples.ToArray();
        var start = Math.Max(0, arr.Length - Window);
        for (int i = start; i < arr.Length; i++)
            TimeoutFlags.Add(!arr[i].Ok);
    }

    // Color rule. Two signals are produced from the same window:
    //
    //   AccentBrush  — DEVICE health only (ICMP): latency tiers + the timeout rule below.
    //                  Drives the top bar, latency number, and chart line. Ports excluded —
    //                  an unreachable port never reddens the device-level visuals.
    //   OverallBrush — AccentBrush merged with port health (worst wins). Drives the diamond
    //                  and mini circle — the at-a-glance rollup.
    //
    // Timeout rule (over the most-recent Window samples):
    //   run of >= RtoRedRun consecutive timeouts, OR >= RtoRedTotal total timeouts -> red
    //   any timeout(s) present below those thresholds                              -> amber (>= Fair)
    //   no timeouts                                                                -> worst latency tier
    private void RecomputeAccent()
    {
        if (_samples.Count == 0)
        {
            AccentBrush = UnknownBrush;
            OverallBrush = UnknownBrush;
            return;
        }

        var arr = _samples.ToArray();
        var start = Math.Max(0, arr.Length - Window);

        int totalTimeouts = 0, run = 0, longestRun = 0;
        Tier worstLatency = Tier.Lan;
        bool sawSuccess = false;
        for (int i = start; i < arr.Length; i++)
        {
            var s = arr[i];
            if (!s.Ok)
            {
                totalTimeouts++;
                run++;
                if (run > longestRun) longestRun = run;
            }
            else
            {
                run = 0;
                sawSuccess = true;
                if (s.Tier > worstLatency) worstLatency = s.Tier;
            }
        }

        Tier device;
        if (longestRun >= RtoRedRun || totalTimeouts >= RtoRedTotal)
            device = Tier.Fail;
        else if (totalTimeouts > 0)
            device = (sawSuccess && worstLatency > Tier.Fair) ? worstLatency : Tier.Fair;
        else
            device = sawSuccess ? worstLatency : Tier.Unknown;

        // Proof of life: if ICMP is failing outright but a configured port is reachable,
        // the device IS alive — ICMP is just blocked/filtered. Cap the penalty at amber so
        // the top bar, latency number, and chart don't scream red for a healthy box.
        if (device == Tier.Fail && AnyPortReachable())
            device = Tier.Fair;

        AccentBrush = BrushFor(device);

        // Overall rollup — port health folds in here, and ONLY here. A DOWN port (TCP
        // refused/timeout) is a hard failure; a DEGRADED port (TCP up, L7 check failing)
        // bumps to Poor when the device itself is reachable.
        Tier overall = device;
        bool anyPortDown = Ports.Any(p => p.HasResult && p.LastHealth == PortHealth.Down);
        bool anyPortDegraded = Ports.Any(p => p.HasResult && p.LastHealth == PortHealth.Degraded);

        if (anyPortDown)
            overall = Tier.Fail;
        else if (anyPortDegraded && LastWasSuccess && overall < Tier.Poor)
            overall = Tier.Poor;

        OverallBrush = BrushFor(overall);
    }

    // Max + average over the successful pings in the window. Timeouts are excluded entirely
    // so one blip can't wreck the average (and there is no out-of-band sentinel to skew it).
    private void RecomputeStats()
    {
        var arr = _samples.ToArray();
        var start = Math.Max(0, arr.Length - Window);
        double sum = 0, max = double.MinValue;
        int n = 0;
        for (int i = start; i < arr.Length; i++)
        {
            var s = arr[i];
            if (!s.Ok) continue;
            sum += s.Rtt;
            if (s.Rtt > max) max = s.Rtt;
            n++;
        }
        _avgMs = n > 0 ? sum / n : null;
        _maxMs = n > 0 ? max : null;
        RefreshStatProjections();
    }

    // Projects current/avg/max into the bound display properties, honoring the global cycler.
    private void RefreshStatProjections()
    {
        MaxValue = _maxMs is double mx ? mx.ToString("F0") : "—";
        var avgStr = _avgMs is double av ? av.ToString("F0") : "—";
        var nowStr = !_hasResult
            ? "—"
            : (LastWasSuccess && LastRttMs is double r ? r.ToString("F0") : "T/O");

        if (ShowAvgAsPrimary)
        {
            PrimaryValue = _avgMs is double a ? $"{a:F0} MS" : "—— MS";
            PrimaryBrush = AccentBrush;
            PrimaryIsAverage = true;
            SecondaryLabel = "NOW";
            SecondaryValue = nowStr;
        }
        else
        {
            PrimaryValue = RttDisplay;
            PrimaryBrush = AccentBrush;
            PrimaryIsAverage = false;
            SecondaryLabel = "AVG";
            SecondaryValue = avgStr;
        }
    }

    // Success-only latency line in logical 0..ChartWidth x 0..ChartHeight space, broken into
    // one figure per contiguous run of successes so timeouts leave a true gap. Y auto-scales
    // to the min/max of successful pings in the window — small values stay visible. The
    // geometry is rendered by a Viewbox+Canvas in XAML, so no explicit stretch anchoring is
    // needed here. Timeouts contribute nothing to the line (they are drawn as "x" markers).
    private Geometry BuildLatencyGeometry()
    {
        var arr = _samples.ToArray();
        var start = Math.Max(0, arr.Length - Window);
        if (arr.Length - start == 0) return Geometry.Empty;

        var pts = new List<(int slot, double rtt)>();
        double min = double.MaxValue, max = double.MinValue;
        for (int i = start; i < arr.Length; i++)
        {
            var s = arr[i];
            if (!s.Ok) continue;
            pts.Add((i - start, s.Rtt));
            if (s.Rtt < min) min = s.Rtt;
            if (s.Rtt > max) max = s.Rtt;
        }
        if (pts.Count == 0) return Geometry.Empty;

        double range = max - min;
        double XFor(int slot) => (slot + 0.5) / Window * ChartWidth;
        double YFor(double rtt) => range < 1e-6
            ? ChartHeight / 2.0
            : (ChartHeight - ChartPad) - ((rtt - min) / range) * (ChartHeight - 2 * ChartPad);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            int idx = 0;
            while (idx < pts.Count)
            {
                int runStart = idx;
                while (idx + 1 < pts.Count && pts[idx + 1].slot == pts[idx].slot + 1) idx++;

                var first = pts[runStart];
                ctx.BeginFigure(new Point(XFor(first.slot), YFor(first.rtt)), false, false);
                if (idx == runStart)
                {
                    // Isolated success between timeouts — draw a tiny nub so it stays visible.
                    ctx.LineTo(new Point(XFor(first.slot) + 0.6, YFor(first.rtt)), true, false);
                }
                else
                {
                    for (int k = runStart + 1; k <= idx; k++)
                        ctx.LineTo(new Point(XFor(pts[k].slot), YFor(pts[k].rtt)), true, false);
                }
                idx++;
            }
        }
        geo.Freeze();
        return geo;
    }

    private static Brush MakeBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private readonly record struct Sample(double Rtt, bool Ok, Tier Tier);
}
