using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Pingy.Core.Config;
using Pingy.Core.Models;
using Pingy.Core.Probing;
using Pingy.Core.Util;

namespace Pingy.Widget.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public const int MinIntervalSeconds = 1;
    public const int MaxIntervalSeconds = 300;

    private static readonly Brush HealthCyanBrush = Frozen(0x00, 0xF0, 0xFF);    // all up
    private static readonly Brush HealthYellowBrush = Frozen(0xFF, 0xE6, 0x00);  // mixed / unknown
    private static readonly Brush HealthMagentaBrush = Frozen(0xFF, 0x2E, 0x63); // all down

    private readonly IPinger _pinger;
    private readonly IPortProbe _portProbe;
    private readonly IServiceCheck _serviceCheck;
    private readonly ITargetLoader _loader;
    private CancellationTokenSource? _cts;
    private TargetsConfig? _currentConfig;
    private bool _initialized;

    [ObservableProperty] private string _configPath = "";
    [ObservableProperty] private string _statusLine = "Loading targets…";
    [ObservableProperty] private int _intervalSeconds = 5;
    [ObservableProperty] private bool _animationsEnabled = true;
    [ObservableProperty] private bool _showMiniDots = true;
    [ObservableProperty] private bool _isMiniMode;
    [ObservableProperty] private bool _isTopmost = true;
    [ObservableProperty] private Brush _healthBrush = HealthYellowBrush;
    [ObservableProperty] private string _healthLabel = "INIT";

    // --- This device (self) identity ------------------------------------
    // Surfaced in the status bar so the user can read off their own machine
    // name, LAN IP and gateway at a glance — the first things IT asks for.
    // Re-resolved on any network change so a Wi-Fi roam / dock can't make it lie.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeviceLabel))]
    [NotifyPropertyChangedFor(nameof(IpLabel))]
    [NotifyPropertyChangedFor(nameof(GatewayLabel))]
    [NotifyPropertyChangedFor(nameof(HasGateway))]
    [NotifyPropertyChangedFor(nameof(SelfIdentityTooltip))]
    [NotifyPropertyChangedFor(nameof(SelfIdentityCopyText))]
    private LocalHostInfo _self = LocalHost.Describe();

    public string DeviceLabel => Self.DeviceName;
    public string IpLabel => Self.IPv4 ?? "no IPv4";
    public string GatewayLabel => Self.Gateway ?? "—";
    public bool HasGateway => Self.Gateway is not null;

    // Multi-line hover detail — the inline readout stays terse; this carries the rest.
    public string SelfIdentityTooltip =>
        "THIS DEVICE  (click to copy)\n" +
        $"Name      {Self.DeviceName}\n" +
        $"Address   {Self.Cidr ?? "no IPv4"}\n" +
        $"Gateway   {Self.Gateway ?? "—"}" +
        (Self.AdapterName is { Length: > 0 } adapter ? $"\nAdapter   {adapter}" : "");

    // One-liner formatted for pasting into a support ticket / chat to IT.
    public string SelfIdentityCopyText =>
        $"Device: {Self.DeviceName} | IP: {Self.Cidr ?? "n/a"} | Gateway: {Self.Gateway ?? "n/a"}";

    private void RefreshSelf() => Self = LocalHost.Describe();

    // Manual zoom factor applied via LayoutTransform on the content root.
    // Decoupled from window size — resize and zoom are now independent.
    public const double MinZoom = 0.6;
    public const double MaxZoom = 2.0;
    [ObservableProperty] private double _zoom = 1.0;

    public bool IsFullMode => !IsMiniMode;
    partial void OnIsMiniModeChanged(bool value) => OnPropertyChanged(nameof(IsFullMode));

    // Reorder is allowed unless a filter is active (filtered subset hides rows that the user
    // would otherwise drop next to). Sort no longer blocks: a drag auto-resets to MANUAL ORDER.
    public bool CanReorder => !FilterChips.Any(c => c.IsSelected);

    public enum MiniDisplay { Host, Label }
    [ObservableProperty] private MiniDisplay _miniDisplayField = MiniDisplay.Host;
    public string MiniDisplayLabel => MiniDisplayField == MiniDisplay.Host ? "IP" : "LABEL";
    partial void OnMiniDisplayFieldChanged(MiniDisplay value) => OnPropertyChanged(nameof(MiniDisplayLabel));

    public void CycleMiniDisplay()
    {
        MiniDisplayField = MiniDisplayField switch
        {
            MiniDisplay.Host => MiniDisplay.Label,
            _ => MiniDisplay.Host,
        };
    }

    // Global toggle for the big number on every card: live latency (default) vs window average.
    public enum StatDisplay { Current, Average }
    [ObservableProperty] private StatDisplay _statDisplayMode = StatDisplay.Current;
    public string StatDisplayLabel => StatDisplayMode == StatDisplay.Current ? "NOW" : "AVG";

    partial void OnStatDisplayModeChanged(StatDisplay value)
    {
        OnPropertyChanged(nameof(StatDisplayLabel));
        var avg = value == StatDisplay.Average;
        foreach (var t in Targets) t.ShowAvgAsPrimary = avg;
    }

    public void CycleStatDisplay()
    {
        StatDisplayMode = StatDisplayMode == StatDisplay.Current
            ? StatDisplay.Average
            : StatDisplay.Current;
    }

    public ObservableCollection<TargetStatusViewModel> Targets { get; } = new();
    public ICollectionView TargetsView { get; }

    public ObservableCollection<TagChipViewModel> FilterChips { get; } = new();

    public IReadOnlyList<SortChoice> SortChoices { get; } = new[]
    {
        new SortChoice("MANUAL ORDER", "", ListSortDirection.Ascending),
        new SortChoice("LABEL A→Z", nameof(TargetStatusViewModel.Label), ListSortDirection.Ascending),
        new SortChoice("LABEL Z→A", nameof(TargetStatusViewModel.Label), ListSortDirection.Descending),
        new SortChoice("HOST A→Z", nameof(TargetStatusViewModel.Host), ListSortDirection.Ascending),
        new SortChoice("STATUS (DOWN first)", nameof(TargetStatusViewModel.StatusSortKey), ListSortDirection.Ascending),
        new SortChoice("LATENCY (HIGH→LOW)", nameof(TargetStatusViewModel.RttSortKey), ListSortDirection.Descending),
        new SortChoice("LATENCY (LOW→HIGH)", nameof(TargetStatusViewModel.RttSortKey), ListSortDirection.Ascending),
    };

    [ObservableProperty] private SortChoice _selectedSort;

    public MainViewModel(IPinger pinger, IPortProbe portProbe, IServiceCheck serviceCheck, ITargetLoader loader)
    {
        _pinger = pinger;
        _portProbe = portProbe;
        _serviceCheck = serviceCheck;
        _loader = loader;
        ConfigPath = loader.ConfigPath;

        _selectedSort = SortChoices[0];

        TargetsView = CollectionViewSource.GetDefaultView(Targets);
        TargetsView.Filter = TargetFilter;

        FilterChips.CollectionChanged += (_, _) => RefreshFilterSubscriptions();

        // The LAN attachment can change under the user (Wi-Fi roam, dock, VPN) — keep it honest.
        NetworkChange.NetworkAddressChanged += (_, _) =>
            Application.Current?.Dispatcher.Invoke(RefreshSelf);
    }

    partial void OnIntervalSecondsChanged(int value)
    {
        if (!_initialized) return;
        if (value < MinIntervalSeconds || value > MaxIntervalSeconds) return;
        _ = PersistAndRestartAsync(value);
    }

    partial void OnSelectedSortChanged(SortChoice value)
    {
        TargetsView.SortDescriptions.Clear();
        if (!value.IsDefault)
            TargetsView.SortDescriptions.Add(new SortDescription(value.PropertyName, value.Direction));
        TargetsView.Refresh();
    }

    public async Task MoveTargetAsync(int fromIdx, int toIdx)
    {
        if (_currentConfig is null) return;
        if (fromIdx < 0 || toIdx < 0) return;
        if (fromIdx == toIdx) return;
        if (fromIdx >= Targets.Count || toIdx >= Targets.Count) return;

        // Manual reorder always wins — reset any active sort so the result is visible.
        var sortReset = !SelectedSort.IsDefault;
        if (sortReset) SelectedSort = SortChoices[0];

        Targets.Move(fromIdx, toIdx);

        var ordered = Targets.Select(t => t.Target).ToArray();
        var updated = _currentConfig with { Targets = ordered };
        await _loader.SaveAsync(updated);
        _currentConfig = updated;
        StatusLine = sortReset
            ? "~ Reordered & saved (sort reset to MANUAL ORDER)."
            : "~ Reordered & saved to targets.json.";
    }

    public void SetDropIndicator(TargetStatusViewModel? target, TargetStatusViewModel.DropPos pos)
    {
        foreach (var t in Targets)
            t.DropIndicator = (t == target) ? pos : TargetStatusViewModel.DropPos.None;
    }

    public void ClearDropIndicators()
    {
        foreach (var t in Targets)
            t.DropIndicator = TargetStatusViewModel.DropPos.None;
    }

    public void SetInterval(int seconds)
    {
        IntervalSeconds = System.Math.Clamp(seconds, MinIntervalSeconds, MaxIntervalSeconds);
    }

    public async Task StartAsync()
    {
        try
        {
            _currentConfig = await _loader.LoadAsync();
            IntervalSeconds = System.Math.Clamp(_currentConfig.IntervalSeconds, MinIntervalSeconds, MaxIntervalSeconds);

            Targets.Clear();
            foreach (var target in _currentConfig.Targets)
                AddTargetVm(target);

            RebuildFilterChips();
            RecomputeHealth();
            UpdateStatusLine();

            _initialized = true;
            _cts = new CancellationTokenSource();
            _ = RunLoopAsync(_cts.Token);
        }
        catch (System.Exception ex)
        {
            StatusLine = $"Failed to load config: {ex.Message}";
            Pingy.Widget.CrashLogger.Log("startup.load", ex);
        }
    }

    public async Task<bool> AddTargetAsync(string label, string host, IReadOnlyList<string> tags, IReadOnlyList<TargetPort>? ports = null, string? owner = null)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        if (_currentConfig is null) return false;

        var id = HostNormalizer.Slugify(label);
        if (Targets.Any(t => t.Target.Id == id))
            id = $"{id}-{System.Guid.NewGuid().ToString("N")[..4]}";

        var newTarget = BuildTarget(id, label, host, tags, ports, owner);
        var updated = _currentConfig with { Targets = _currentConfig.Targets.Concat(new[] { newTarget }).ToArray() };
        await _loader.SaveAsync(updated);
        _currentConfig = updated;

        AddTargetVm(newTarget);
        RebuildFilterChips();
        RecomputeHealth();
        StatusLine = $"+ {newTarget.Label} ({newTarget.Host}). {Targets.Count} target(s) every {IntervalSeconds}s.";
        return true;
    }

    public async Task<bool> UpdateTargetAsync(string id, string label, string host, IReadOnlyList<string> tags, IReadOnlyList<TargetPort>? ports = null, string? owner = null)
    {
        if (_currentConfig is null) return false;
        var existingIdx = Targets.ToList().FindIndex(t => t.Target.Id == id);
        if (existingIdx < 0) return false;

        var updatedTarget = BuildTarget(id, label, host, tags, ports, owner);
        var newList = _currentConfig.Targets.ToArray();
        var configIdx = System.Array.FindIndex(newList, t => t.Id == id);
        if (configIdx < 0) return false;
        newList[configIdx] = updatedTarget;

        var updatedConfig = _currentConfig with { Targets = newList };
        await _loader.SaveAsync(updatedConfig);
        _currentConfig = updatedConfig;

        var existingVm = Targets[existingIdx];
        var hostChanged = !string.Equals(existingVm.Target.Host, updatedTarget.Host, System.StringComparison.OrdinalIgnoreCase);

        if (hostChanged)
        {
            // Different network entity — reset sample history.
            existingVm.PropertyChanged -= OnTargetPropertyChanged;
            var newVm = new TargetStatusViewModel(updatedTarget)
            {
                ShowAvgAsPrimary = StatDisplayMode == StatDisplay.Average,
            };
            newVm.PropertyChanged += OnTargetPropertyChanged;
            Targets[existingIdx] = newVm;
        }
        else
        {
            // Same host — preserve sample history, just refresh metadata.
            existingVm.Target = updatedTarget;
        }

        RebuildFilterChips();
        RecomputeHealth();
        StatusLine = hostChanged
            ? $"~ Updated {updatedTarget.Label} (host changed; history reset)."
            : $"~ Updated {updatedTarget.Label} (history preserved).";
        return true;
    }

    public async Task<bool> DeleteTargetAsync(string id)
    {
        if (_currentConfig is null) return false;
        var existingIdx = Targets.ToList().FindIndex(t => t.Target.Id == id);
        if (existingIdx < 0) return false;

        var removed = Targets[existingIdx];
        removed.PropertyChanged -= OnTargetPropertyChanged;
        Targets.RemoveAt(existingIdx);

        var updated = _currentConfig with { Targets = _currentConfig.Targets.Where(t => t.Id != id).ToArray() };
        await _loader.SaveAsync(updated);
        _currentConfig = updated;

        RebuildFilterChips();
        RecomputeHealth();
        StatusLine = $"× Removed {removed.Label}. {Targets.Count} target(s) remaining.";
        return true;
    }

    public void Stop() => _cts?.Cancel();

    // -- internals -------------------------------------------------------

    private void AddTargetVm(Target t)
    {
        var vm = new TargetStatusViewModel(t)
        {
            ShowAvgAsPrimary = StatDisplayMode == StatDisplay.Average,
        };
        vm.PropertyChanged += OnTargetPropertyChanged;
        Targets.Add(vm);
    }

    private static Target BuildTarget(string id, string label, string host, IReadOnlyList<string> tags, IReadOnlyList<TargetPort>? ports, string? owner)
    {
        var primaryKind = (tags?.FirstOrDefault() ?? "host").ToLowerInvariant();
        var cleanedTags = tags?.Where(t => !string.IsNullOrWhiteSpace(t))
                              .Select(t => t.Trim().ToLowerInvariant())
                              .Distinct()
                              .ToArray()
                          ?? System.Array.Empty<string>();

        // De-dupe ports by number, keep first label and first check seen, drop out-of-range.
        var cleanedPorts = ports?
            .Where(p => p.Number is > 0 and <= 65535)
            .GroupBy(p => p.Number)
            .Select(g => new TargetPort(
                g.Key,
                g.Select(x => x.Label).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim(),
                g.Select(x => x.Check).FirstOrDefault(c => c is not null)))
            .ToArray();

        return new Target(
            Id: id,
            Host: host.Trim(),
            Kind: primaryKind,
            Label: string.IsNullOrWhiteSpace(label) ? host : label.Trim(),
            Tags: cleanedTags,
            Ports: cleanedPorts is { Length: > 0 } ? cleanedPorts : null,
            Owner: string.IsNullOrWhiteSpace(owner) ? null : owner.Trim());
    }

    private void OnTargetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TargetStatusViewModel.StateBadge))
        {
            RecomputeHealth();
            if (!SelectedSort.IsDefault) TargetsView.Refresh();
        }
        else if (e.PropertyName == nameof(TargetStatusViewModel.RttSortKey)
              || e.PropertyName == nameof(TargetStatusViewModel.StatusSortKey))
        {
            if (!SelectedSort.IsDefault) TargetsView.Refresh();
        }
    }

    private void RecomputeHealth()
    {
        int up = 0, down = 0, init = 0;
        foreach (var t in Targets)
        {
            switch (t.StateBadge)
            {
                // NO PING = ICMP blocked but a port is reachable — the device is alive,
                // so it counts as up for the at-a-glance global rollup.
                case "UP":
                case "NO PING": up++; break;
                case "DOWN": down++; break;
                default: init++; break;
            }
        }

        if (Targets.Count == 0)
        {
            HealthBrush = HealthYellowBrush; HealthLabel = "EMPTY";
        }
        else if (down == 0 && init == 0)
        {
            HealthBrush = HealthCyanBrush; HealthLabel = "ALL UP";
        }
        else if (up == 0 && init == 0)
        {
            HealthBrush = HealthMagentaBrush; HealthLabel = "ALL DOWN";
        }
        else if (down > 0)
        {
            HealthBrush = HealthYellowBrush; HealthLabel = $"DEGRADED {down}/{Targets.Count}";
        }
        else
        {
            HealthBrush = HealthYellowBrush; HealthLabel = "INIT";
        }
    }

    // ---- Filter chips --------------------------------------------------

    private void RebuildFilterChips()
    {
        var distinct = Targets.SelectMany(t => t.Tags).Distinct().OrderBy(s => s).ToList();
        var active = FilterChips.Where(c => c.IsSelected).Select(c => c.Name).ToHashSet();

        foreach (var c in FilterChips) c.PropertyChanged -= ChipChanged;
        FilterChips.Clear();
        foreach (var tag in distinct)
        {
            var chip = new TagChipViewModel(tag, active.Contains(tag));
            chip.PropertyChanged += ChipChanged;
            FilterChips.Add(chip);
        }
    }

    private void RefreshFilterSubscriptions()
    {
        foreach (var c in FilterChips)
        {
            c.PropertyChanged -= ChipChanged;
            c.PropertyChanged += ChipChanged;
        }
    }

    private void ChipChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TagChipViewModel.IsSelected))
        {
            TargetsView.Refresh();
            OnPropertyChanged(nameof(CanReorder));
        }
    }

    private bool TargetFilter(object obj)
    {
        if (obj is not TargetStatusViewModel t) return false;
        var active = FilterChips.Where(c => c.IsSelected).Select(c => c.Name).ToList();
        if (active.Count == 0) return true;
        return t.Tags.Any(tag => active.Contains(tag));
    }

    // ---- Probe loop ----------------------------------------------------

    private async Task PersistAndRestartAsync(int seconds)
    {
        if (_currentConfig is not null)
        {
            var updated = _currentConfig with { IntervalSeconds = seconds };
            await _loader.SaveAsync(updated);
            _currentConfig = updated;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        UpdateStatusLine();
        _ = RunLoopAsync(_cts.Token);
    }

    private void UpdateStatusLine()
    {
        StatusLine = $"Probing {Targets.Count} target(s) every {IntervalSeconds}s — {ConfigPath}";
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var period = System.TimeSpan.FromSeconds(IntervalSeconds);
        using var timer = new PeriodicTimer(period);
        while (!ct.IsCancellationRequested)
        {
            await ProbeAllAsync(ct);
            try { await timer.WaitForNextTickAsync(ct); }
            catch (System.OperationCanceledException) { break; }
        }
    }

    private async Task ProbeAllAsync(CancellationToken ct)
    {
        var snapshot = Targets.ToList();
        var timeout = System.TimeSpan.FromMilliseconds(1500);
        var tasks = new List<Task>();

        foreach (var tvm in snapshot)
        {
            tasks.Add(ProbeIcmpAsync(tvm, timeout, ct));
            foreach (var port in tvm.Target.Ports ?? System.Array.Empty<TargetPort>())
                tasks.Add(ProbePortAsync(tvm, port, timeout, ct));
        }

        await Task.WhenAll(tasks);
    }

    private async Task ProbeIcmpAsync(TargetStatusViewModel tvm, System.TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            var result = await _pinger.PingAsync(tvm.Target, timeout, ct);
            Application.Current?.Dispatcher.Invoke(() => tvm.Update(result));
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception ex)
        {
            // Survive a single bad probe/UI update — log & keep the loop running. Without this
            // the exception aggregates through Task.WhenAll → unobserved task → silent process exit.
            Pingy.Widget.CrashLogger.Log("probe.icmp", ex, $"target={tvm.Target.Id} host={tvm.Target.Host}");
        }
    }

    private async Task ProbePortAsync(TargetStatusViewModel tvm, TargetPort port, System.TimeSpan timeout, CancellationToken ct)
    {
        try
        {
            var tcp = await _portProbe.ProbeAsync(tvm.Target, port.Number, timeout, ct);

            PortProbeResult combined;
            if (!tcp.Connected)
            {
                combined = new PortProbeResult(tvm.Target.Id, port.Number,
                    PortHealth.Down, null, null, tcp.Status, null, tcp.At);
            }
            else if (port.Check is null)
            {
                combined = new PortProbeResult(tvm.Target.Id, port.Number,
                    PortHealth.Ok, tcp.RttMs, null, tcp.Status, null, tcp.At);
            }
            else
            {
                var l7Timeout = port.Check.TimeoutMs is int ms
                    ? System.TimeSpan.FromMilliseconds(ms)
                    : timeout;
                var l7 = await _serviceCheck.CheckAsync(tvm.Target, port, l7Timeout, ct);
                combined = new PortProbeResult(tvm.Target.Id, port.Number,
                    l7.Ok ? PortHealth.Ok : PortHealth.Degraded,
                    tcp.RttMs, l7.RttMs, tcp.Status, l7.Status, System.DateTimeOffset.UtcNow);
            }

            Application.Current?.Dispatcher.Invoke(() => tvm.UpdatePort(combined));
        }
        catch (System.OperationCanceledException) { }
        catch (System.Exception ex)
        {
            Pingy.Widget.CrashLogger.Log("probe.port", ex, $"target={tvm.Target.Id} host={tvm.Target.Host} port={port.Number}");
        }
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
