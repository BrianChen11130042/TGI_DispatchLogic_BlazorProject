using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// 派車頁全域共用狀態與輪詢（所有瀏覽器連線看到同一份設定、任務、紀錄）。
/// </summary>
public sealed class DispatchLogicSessionService : IDisposable
{
    public const int ModbusPollIntervalMs = 800;
    public const int TgiPollIntervalMs = 1500;
    public const int ModbusTimeoutMs = 3000;
    public const int BufferAutoDispatchIntervalSeconds = 20;
    public const int TwpAutoDispatchIntervalSeconds = 10;
    public const int TwpDispatchRetryDelaySeconds = 5;

    readonly object _startGate = new();
    readonly SimulateCodeCatalogService _catalogService;
    readonly ModbusEquipPollService _pollService;
    readonly AmrApiClient _amrApi;
    readonly IHostApplicationLifetime _lifetime;

    CancellationTokenSource? _pollCts;
    bool _pollingStarted;
    int _autoDispatchRunning;

    public event Action? StateChanged;

    public DispatchLogicSessionService(
        SimulateCodeCatalogService catalogService,
        ModbusEquipPollService pollService,
        AmrApiClient amrApi,
        IOptions<EquipSimOptions> equipSimOptions,
        IHostApplicationLifetime lifetime)
    {
        _catalogService = catalogService;
        _pollService = pollService;
        _amrApi = amrApi;
        _lifetime = lifetime;

        var opts = equipSimOptions.Value;
        ModbusHost = opts.DefaultModbusHost;
        ModbusPort = opts.DefaultModbusPort;
        UnitId = opts.DefaultUnitId;
        Catalog = catalogService.BuildCatalog();
    }

    public ModbusEquipCatalog? Catalog { get; }

    public string ModbusHost { get; set; } = "";
    public int ModbusPort { get; set; }
    public int UnitId { get; set; }
    public bool AutoRefresh { get; set; } = true;
    public bool ModbusBusy { get; private set; }
    public bool TgiBusy { get; private set; }
    public bool DispatchBusy { get; private set; }
    public string MainTab { get; set; } = "buffer";
    public string DispatchMode { get; set; } = "manual";
    public string TwpDispatchMode { get; set; } = "manual";
    public bool TwpDispatchBusy { get; private set; }
    public string TwpUnloadDispatchMode { get; set; } = "manual";
    public bool TwpUnloadDispatchBusy { get; private set; }
    public string ClrDispatchMode { get; set; } = "manual";
    public bool ClrDispatchBusy { get; private set; }

    public EquipSimLiveSnapshot? Snapshot { get; private set; }
    public IReadOnlyList<BufferDispatchEvaluation>? BufferEvaluations { get; private set; }
    public IReadOnlyList<CakeVehicleDispatchStatus> CakeVehicles { get; private set; } =
        CakeVehicleDispatchEvaluator.BuildWaitingFleet();
    public IReadOnlyList<BufferDispatchPair>? Pairings { get; private set; }
    public List<BufferDispatchLogEntry> DispatchLogs { get; } = [];
    public List<BufferDispatchInFlight> InFlightDispatches { get; } = [];
    public List<RobotStatusDto> LastRobots { get; private set; } = [];
    public List<FleetStatusDto> LastFleetStatuses { get; private set; } = [];

    public IReadOnlyList<TwistingLoadMachineEvaluation>? TwpMachineEvaluations { get; private set; }
    public IReadOnlyList<CakeVehicleDispatchStatus> TwpCakeVehicles { get; private set; } =
        CakeVehicleDispatchEvaluator.BuildWaitingFleet("等待 TGI /v2/robots/status…");
    public IReadOnlyList<TwistingLoadDispatchPair>? TwpPairings { get; private set; }
    public List<BufferDispatchLogEntry> TwpDispatchLogs { get; } = [];
    public List<TwistingLoadInFlight> TwpInFlightDispatches { get; } = [];

    public IReadOnlyList<TwistingLoadMachineEvaluation>? TwpUnloadMachineEvaluations { get; private set; }
    public IReadOnlyList<CakeVehicleDispatchStatus> TwpUnloadCakeVehicles { get; private set; } =
        CakeVehicleDispatchEvaluator.BuildWaitingFleet(
            "等待 TGI /v2/robots/status…",
            DispatchFleetCatalog.TwistingUnloadCakeVehicleCodes);
    public IReadOnlyList<TwistingLoadDispatchPair>? TwpUnloadPairings { get; private set; }
    public List<BufferDispatchLogEntry> TwpUnloadDispatchLogs { get; } = [];
    public List<TwistingLoadInFlight> TwpUnloadInFlightDispatches { get; } = [];

    public IReadOnlyList<ClearingLoadDispatchEvaluation>? ClrStationEvaluations { get; private set; }
    public IReadOnlyList<CakeVehicleDispatchStatus> ClrCakeVehicles { get; private set; } =
        CakeVehicleDispatchEvaluator.BuildWaitingFleet(
            "等待 TGI /v2/robots/status…",
            DispatchFleetCatalog.ClearingLoadCakeVehicleCodes);
    public IReadOnlyList<ClearingLoadDispatchPair>? ClrPairings { get; private set; }
    public List<BufferDispatchLogEntry> ClrDispatchLogs { get; } = [];
    public List<ClearingLoadDispatchInFlight> ClrInFlightDispatches { get; } = [];

    public string? AmrError { get; private set; }
    public DateTime? LastAmrRefresh { get; private set; }
    public int TgiRobotReportCount { get; private set; }

    DateTime? _lastBufferAutoFlowAt;
    DateTime? _lastTwpAutoFlowAt;
    DateTime? _lastTwpUnloadAutoFlowAt;
    DateTime? _lastClrAutoFlowAt;
    string? _token;

    public int DispatchableBufferCount => BufferEvaluations?.Count(e => e.IsDispatchable) ?? 0;

    public int AvailableBufferCount =>
        DispatchableBuffers.Count(b => !BufferDispatchTracker.IsBufferLocked(InFlightDispatches, b.ParkingPointId));

    public int ActiveInFlightCount => InFlightDispatches.Count(f => f.IsActive);

    public IReadOnlyList<BufferDispatchInFlight> ActiveInFlightTasks =>
        InFlightDispatches.Where(f => f.IsActive).ToList();

    IEnumerable<BufferDispatchEvaluation> DispatchableBuffers =>
        BufferEvaluations?.Where(e => e.IsDispatchable) ?? [];

    public int TwpCallableSideCount =>
        TwpMachineEvaluations?.Count(m =>
            m.Status == TwistingParkingRegistry.CallVehicleStatus &&
            m.AvailableMissions.Count > 0) ?? 0;

    public int TwpAvailableMissionBlockCount => CountAvailableTwpMissionBlocks();

    public int TwpActiveInFlightCount => TwpInFlightDispatches.Count(f => f.IsActive);

    public IReadOnlyList<TwistingLoadInFlight> TwpActiveInFlightTasks =>
        TwpInFlightDispatches.Where(f => f.IsActive).ToList();

    public int TwpUnloadRequestSideCount =>
        TwpUnloadMachineEvaluations?.Count(m =>
            m.Status == TwistingParkingRegistry.RequestUnloadStatus &&
            m.AvailableMissions.Count > 0) ?? 0;

    public int TwpUnloadAvailableMissionBlockCount => CountAvailableTwpUnloadMissionBlocks();

    public int TwpUnloadActiveInFlightCount => TwpUnloadInFlightDispatches.Count(f => f.IsActive);

    public IReadOnlyList<TwistingLoadInFlight> TwpUnloadActiveInFlightTasks =>
        TwpUnloadInFlightDispatches.Where(f => f.IsActive).ToList();

    public int ClrDispatchableStationCount => ClrStationEvaluations?.Count(e => e.IsDispatchable) ?? 0;

    public int ClrAvailableStationCount =>
        DispatchableClrStations.Count(s =>
            !ClearingLoadDispatchTracker.IsStationLocked(ClrInFlightDispatches, s.ParkingPointId));

    public int ClrActiveInFlightCount => ClrInFlightDispatches.Count(f => f.IsActive);

    public IReadOnlyList<ClearingLoadDispatchInFlight> ClrActiveInFlightTasks =>
        ClrInFlightDispatches.Where(f => f.IsActive).ToList();

    IEnumerable<ClearingLoadDispatchEvaluation> DispatchableClrStations =>
        ClrStationEvaluations?.Where(e => e.IsDispatchable) ?? [];

    public void EnsurePollingStarted()
    {
        lock (_startGate)
        {
            if (_pollingStarted) return;
            _pollingStarted = true;
            _pollCts = new CancellationTokenSource();
            _lifetime.ApplicationStopping.Register(() => _pollCts?.Cancel());
            _ = ModbusPollLoopAsync(_pollCts.Token);
            _ = TgiPollLoopAsync(_pollCts.Token);
        }
    }

    public void SetMainTab(string tab)
    {
        MainTab = tab;
        NotifyChanged();
    }

    public void SetDispatchModeManual()
    {
        DispatchMode = "manual";
        NotifyChanged();
    }

    public void SetDispatchModeAuto()
    {
        DispatchMode = "auto";
        NotifyChanged();
    }

    public void SetTwpDispatchModeManual()
    {
        TwpDispatchMode = "manual";
        NotifyChanged();
    }

    public void SetTwpDispatchModeAuto()
    {
        TwpDispatchMode = "auto";
        NotifyChanged();
    }

    public void SetTwpUnloadDispatchModeManual()
    {
        TwpUnloadDispatchMode = "manual";
        NotifyChanged();
    }

    public void SetTwpUnloadDispatchModeAuto()
    {
        TwpUnloadDispatchMode = "auto";
        NotifyChanged();
    }

    public void SetClrDispatchModeManual()
    {
        ClrDispatchMode = "manual";
        NotifyChanged();
    }

    public void SetClrDispatchModeAuto()
    {
        ClrDispatchMode = "auto";
        NotifyChanged();
    }

    public void NotifySettingsChanged() => NotifyChanged();

    public async Task RefreshNowAsync()
    {
        await PollModbusOnceAsync();
        await PollTgiOnceAsync();
    }

    public bool IsAmrBusy(string amrCode) =>
        CakeVehicleDispatchEvaluator.IsAmrInBusySet(GetBusyAmrCodes(), amrCode);

    public bool GetTwpPairCanDispatch(TwistingLoadDispatchPair pair) => CanStartTwpPair(pair);

    public string? GetTwpPairBlockReason(TwistingLoadDispatchPair pair)
    {
        if (CanStartTwpPair(pair)) return null;

        var machine = ResolveTwpMachineEvaluation(pair.Machine.MachineId, pair.Machine.Side);
        var blockIndex = TwistingLoadLaneAdmission.GetLaneBlockIndex(pair.MissionStops);
        return TwistingLoadDispatchTracker.TryGetDispatchBlockReason(
            TwpInFlightDispatches,
            pair.Machine.MachineId,
            pair.Machine.Side,
            pair.Machine.TwpGroupId,
            blockIndex,
            pair.MissionStops.Select(s => s.ParkingPointId),
            machine?.AvailableMissions ?? pair.Machine.AvailableMissions,
            TwpMachineEvaluations,
            machine?.DockingPoints ?? pair.Machine.DockingPoints,
            LastFleetStatuses);
    }

    public bool GetTwpUnloadPairCanDispatch(TwistingLoadDispatchPair pair) => CanStartTwpUnloadPair(pair);

    public string? GetTwpUnloadPairBlockReason(TwistingLoadDispatchPair pair)
    {
        if (CanStartTwpUnloadPair(pair)) return null;

        var machine = ResolveTwpUnloadMachineEvaluation(pair.Machine.MachineId, pair.Machine.Side);
        var blockIndex = TwistingLoadLaneAdmission.GetLaneBlockIndex(pair.MissionStops);
        return TwistingLoadDispatchTracker.TryGetDispatchBlockReason(
            TwpUnloadInFlightDispatches,
            pair.Machine.MachineId,
            pair.Machine.Side,
            pair.Machine.TwpGroupId,
            blockIndex,
            pair.MissionStops.Select(s => s.ParkingPointId),
            machine?.AvailableMissions ?? pair.Machine.AvailableMissions,
            TwpUnloadMachineEvaluations,
            machine?.DockingPoints ?? pair.Machine.DockingPoints,
            LastFleetStatuses,
            TwistingParkingRegistry.RequestUnloadStatus);
    }

    public Task ResetBufferDispatchStateAsync()
    {
        InFlightDispatches.Clear();
        DispatchLogs.Clear();
        _lastBufferAutoFlowAt = null;
        RecomputePairings();
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task ResetTwpDispatchStateAsync()
    {
        TwpInFlightDispatches.Clear();
        TwpDispatchLogs.Clear();
        _lastTwpAutoFlowAt = null;
        RecomputeTwistingPairings();
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task ResetTwpUnloadDispatchStateAsync()
    {
        TwpUnloadInFlightDispatches.Clear();
        TwpUnloadDispatchLogs.Clear();
        _lastTwpUnloadAutoFlowAt = null;
        RecomputeTwistingUnloadPairings();
        NotifyChanged();
        return Task.CompletedTask;
    }

    public Task ResetClrDispatchStateAsync()
    {
        ClrInFlightDispatches.Clear();
        ClrDispatchLogs.Clear();
        _lastClrAutoFlowAt = null;
        RecomputeClearingLoadEvaluations();
        RecomputeClearingLoadPairings();
        NotifyChanged();
        return Task.CompletedTask;
    }

    public async Task DispatchPairManualAsync(BufferDispatchPair pair)
    {
        if (!await EnsureTokenAsync()) return;
        if (IsAmrBusy(pair.Vehicle.AmrCode))
        {
            DispatchLogs.Insert(0, new BufferDispatchLogEntry(
                DateTime.Now, pair.Buffer.ParkingPointId, pair.Vehicle.AmrCode, false,
                "暫不可派車：此車已在任務中", null, null));
            NotifyChanged();
            return;
        }

        if (BufferDispatchEvaluator.IsParkingPointOccupied(
                LastFleetStatuses, pair.Buffer.ParkingPointId, out var parkedRobot))
        {
            DispatchLogs.Insert(0, new BufferDispatchLogEntry(
                DateTime.Now, pair.Buffer.ParkingPointId, pair.Vehicle.AmrCode, false,
                $"暫不可派車：{pair.Buffer.ParkingPointId} 已有 {parkedRobot} 停靠", null, null));
            NotifyChanged();
            return;
        }

        DispatchBusy = true;
        NotifyChanged();
        try
        {
            var result = await _amrApi.TriggerFlowDiagnosticAsync(
                _token!, pair.FlowName, pair.FlowRequest, _pollCts?.Token ?? default);
            HandleDispatchResult(pair, result);
        }
        finally
        {
            DispatchBusy = false;
            NotifyChanged();
        }
    }

    public async Task DispatchTwpPairManualAsync(TwistingLoadDispatchPair pair)
    {
        if (!await EnsureTokenAsync()) return;
        if (!CanStartTwpPair(pair))
        {
            TwpDispatchLogs.Insert(0, new BufferDispatchLogEntry(
                DateTime.Now,
                $"{pair.Machine.MissionKey} · {TwistingLoadMissionPlanner.FormatMissionStops(pair.MissionStops)}",
                pair.Vehicle.AmrCode,
                false,
                GetTwpPairBlockReason(pair) ?? "暫不可派車",
                null,
                null));
            NotifyChanged();
            return;
        }

        TwpDispatchBusy = true;
        NotifyChanged();
        try
        {
            await DispatchTwpPairCoreAsync(pair);
        }
        finally
        {
            TwpDispatchBusy = false;
            NotifyChanged();
        }
    }

    public async Task DispatchTwpUnloadPairManualAsync(TwistingLoadDispatchPair pair)
    {
        if (!await EnsureTokenAsync()) return;
        if (!CanStartTwpUnloadPair(pair))
        {
            TwpUnloadDispatchLogs.Insert(0, new BufferDispatchLogEntry(
                DateTime.Now,
                $"{pair.Machine.MissionKey} · {TwistingLoadMissionPlanner.FormatMissionStops(pair.MissionStops)}",
                pair.Vehicle.AmrCode,
                false,
                GetTwpUnloadPairBlockReason(pair) ?? "暫不可派車",
                null,
                null));
            NotifyChanged();
            return;
        }

        TwpUnloadDispatchBusy = true;
        NotifyChanged();
        try
        {
            await DispatchTwpUnloadPairCoreAsync(pair);
        }
        finally
        {
            TwpUnloadDispatchBusy = false;
            NotifyChanged();
        }
    }

    public async Task DispatchClrPairManualAsync(ClearingLoadDispatchPair pair)
    {
        if (!await EnsureTokenAsync()) return;
        if (IsAmrBusy(pair.Vehicle.AmrCode))
        {
            ClrDispatchLogs.Insert(0, new BufferDispatchLogEntry(
                DateTime.Now, pair.Station.ParkingPointId, pair.Vehicle.AmrCode, false,
                "暫不可派車：此車已在任務中", null, null));
            NotifyChanged();
            return;
        }

        if (ClearingLoadDispatchTracker.IsStationLocked(ClrInFlightDispatches, pair.Station.ParkingPointId))
        {
            ClrDispatchLogs.Insert(0, new BufferDispatchLogEntry(
                DateTime.Now, pair.Station.ParkingPointId, pair.Vehicle.AmrCode, false,
                $"暫不可派車：{pair.Station.ParkingPointId} 任務中鎖定", null, null));
            NotifyChanged();
            return;
        }

        if (ClearingLoadDispatchEvaluator.IsParkingPointOccupied(
                LastFleetStatuses, pair.Station.ParkingPointId, out var parkedRobot))
        {
            ClrDispatchLogs.Insert(0, new BufferDispatchLogEntry(
                DateTime.Now, pair.Station.ParkingPointId, pair.Vehicle.AmrCode, false,
                $"暫不可派車：{pair.Station.ParkingPointId} 已有 {parkedRobot} 停靠", null, null));
            NotifyChanged();
            return;
        }

        ClrDispatchBusy = true;
        NotifyChanged();
        try
        {
            var result = await _amrApi.TriggerFlowDiagnosticAsync(
                _token!, pair.FlowName, pair.FlowRequest, _pollCts?.Token ?? default);
            HandleClrDispatchResult(pair, result);
        }
        finally
        {
            ClrDispatchBusy = false;
            NotifyChanged();
        }
    }

    void NotifyChanged() => StateChanged?.Invoke();

    int CountAvailableTwpMissionBlocks()
    {
        if (TwpMachineEvaluations is null) return 0;
        var count = 0;
        foreach (var machine in TwpMachineEvaluations)
        {
            if (machine.Status != TwistingParkingRegistry.CallVehicleStatus) continue;
            foreach (var mission in machine.AvailableMissions)
            {
                var laneBlockIndex = TwistingLoadLaneAdmission.GetLaneBlockIndex(mission);
                if (TwistingLoadDispatchTracker.CanDispatchMissionBlock(
                        TwpInFlightDispatches,
                        machine.MachineId,
                        machine.Side,
                        machine.TwpGroupId,
                        laneBlockIndex,
                        mission.Select(s => s.ParkingPointId),
                        machine.AvailableMissions,
                        TwpMachineEvaluations,
                        machine.DockingPoints,
                        LastFleetStatuses))
                    count++;
            }
        }
        return count;
    }

    int CountAvailableTwpUnloadMissionBlocks()
    {
        if (TwpUnloadMachineEvaluations is null) return 0;
        var count = 0;
        foreach (var machine in TwpUnloadMachineEvaluations)
        {
            if (machine.Status != TwistingParkingRegistry.RequestUnloadStatus) continue;
            foreach (var mission in machine.AvailableMissions)
            {
                var laneBlockIndex = TwistingLoadLaneAdmission.GetLaneBlockIndex(mission);
                if (TwistingLoadDispatchTracker.CanDispatchMissionBlock(
                        TwpUnloadInFlightDispatches,
                        machine.MachineId,
                        machine.Side,
                        machine.TwpGroupId,
                        laneBlockIndex,
                        mission.Select(s => s.ParkingPointId),
                        machine.AvailableMissions,
                        TwpUnloadMachineEvaluations,
                        machine.DockingPoints,
                        LastFleetStatuses,
                        TwistingParkingRegistry.RequestUnloadStatus))
                    count++;
            }
        }
        return count;
    }

    HashSet<string> GetBusyAmrCodes()
    {
        var codes = new List<string>();
        foreach (var f in InFlightDispatches.Where(f => f.IsActive))
            codes.Add(f.AmrCode);
        foreach (var f in TwpInFlightDispatches.Where(f => f.IsActive))
            codes.Add(f.AmrCode);
        foreach (var f in TwpUnloadInFlightDispatches.Where(f => f.IsActive))
            codes.Add(f.AmrCode);
        foreach (var f in ClrInFlightDispatches.Where(f => f.IsActive))
            codes.Add(f.AmrCode);
        return CakeVehicleDispatchEvaluator.BuildBusyAmrSet(inFlightAmrCodes: codes);
    }

    async Task ModbusPollLoopAsync(CancellationToken ct)
    {
        await PollModbusOnceAsync();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(ModbusPollIntervalMs));
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (AutoRefresh && !ModbusBusy)
                await PollModbusOnceAsync();
        }
    }

    async Task TgiPollLoopAsync(CancellationToken ct)
    {
        await PollTgiOnceAsync();
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TgiPollIntervalMs));
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (AutoRefresh && !TgiBusy)
                await PollTgiOnceAsync();
        }
    }

    async Task PollModbusOnceAsync()
    {
        if (Catalog is null || ModbusBusy) return;
        ModbusBusy = true;
        try
        {
            var snap = await _pollService.PollSnapshotAsync(
                Catalog, ModbusHost.Trim(), ModbusPort, (byte)UnitId,
                ModbusTimeoutMs, _pollCts?.Token ?? default);
            Snapshot = snap;
            if (snap.Success)
            {
                BufferEvaluations = BufferDispatchEvaluator.ApplyParkingOccupancy(
                    BufferDispatchEvaluator.EvaluateAll(snap),
                    LastFleetStatuses);
                TwpMachineEvaluations = TwistingLoadDispatchEvaluator.EvaluateAll(snap);
                TwpUnloadMachineEvaluations = TwistingUnloadDispatchEvaluator.EvaluateAll(snap);
                RefreshInFlightStatus();
                RefreshTwistingInFlightStatus();
                RefreshTwistingUnloadInFlightStatus();
                RefreshClearingLoadInFlightStatus();
                RecomputeClearingLoadEvaluations();
                RecomputePairings();
                RecomputeTwistingPairings();
                RecomputeTwistingUnloadPairings();
                RecomputeClearingLoadPairings();
            }

            NotifyChanged();
        }
        finally
        {
            ModbusBusy = false;
        }
    }

    async Task PollTgiOnceAsync()
    {
        if (TgiBusy) return;
        TgiBusy = true;
        try
        {
            // 只鎖住「讀 TGI / 重算配對」；派車另開通道，避免 3 flow 長任務卡住更新時間戳
            await RefreshAmrAndPairAsync();
            NotifyChanged();
        }
        finally
        {
            TgiBusy = false;
        }

        ScheduleAutoDispatches();
    }

    void ScheduleAutoDispatches()
    {
        if (DispatchMode != "auto"
            && TwpDispatchMode != "auto"
            && TwpUnloadDispatchMode != "auto"
            && ClrDispatchMode != "auto")
            return;

        if (Interlocked.CompareExchange(ref _autoDispatchRunning, 1, 0) != 0)
            return;

        _ = RunAutoDispatchesAsync();
    }

    async Task RunAutoDispatchesAsync()
    {
        try
        {
            if (DispatchMode == "auto")
                await TryAutoDispatchAsync();

            if (TwpDispatchMode == "auto")
                await TryTwpAutoDispatchAsync();

            if (TwpUnloadDispatchMode == "auto")
                await TryTwpUnloadAutoDispatchAsync();

            if (ClrDispatchMode == "auto")
                await TryClrAutoDispatchAsync();

            NotifyChanged();
        }
        catch
        {
            // fire-and-forget：避免例外中斷背景；下次輪詢會再試
        }
        finally
        {
            Interlocked.Exchange(ref _autoDispatchRunning, 0);
        }
    }

    void RecomputePairings()
    {
        if (BufferEvaluations is null) return;
        var buffers = DispatchableBuffers
            .Where(b => !BufferDispatchTracker.IsBufferLocked(InFlightDispatches, b.ParkingPointId))
            .ToList();
        var busy = GetBusyAmrCodes();
        Pairings = BufferDispatchPairingService.Pair(buffers, CakeVehicles, busy);
    }

    void RecomputeTwistingPairings()
    {
        if (TwpMachineEvaluations is null) return;
        var machines = TwpMachineEvaluations
            .Where(m => m.Status == TwistingParkingRegistry.CallVehicleStatus && m.AvailableMissions.Count > 0)
            .ToList();
        var busyAmrs = GetBusyAmrCodes();
        var vehicles = TwpCakeVehicles
            .Where(v => v.IsEligible && !CakeVehicleDispatchEvaluator.IsAmrInBusySet(busyAmrs, v.AmrCode))
            .ToList();
        TwpPairings = TwistingLoadPairingService.Pair(machines, vehicles, TwpInFlightDispatches, LastFleetStatuses);
    }

    void RecomputeTwistingUnloadPairings()
    {
        if (TwpUnloadMachineEvaluations is null) return;
        var machines = TwpUnloadMachineEvaluations
            .Where(m => m.Status == TwistingParkingRegistry.RequestUnloadStatus && m.AvailableMissions.Count > 0)
            .ToList();
        var busyAmrs = GetBusyAmrCodes();
        var vehicles = TwpUnloadCakeVehicles
            .Where(v => v.IsEligible && !CakeVehicleDispatchEvaluator.IsAmrInBusySet(busyAmrs, v.AmrCode))
            .ToList();
        TwpUnloadPairings = TwistingUnloadPairingService.Pair(
            machines, vehicles, TwpUnloadInFlightDispatches, LastFleetStatuses);
    }

    void RecomputeClearingLoadEvaluations()
    {
        ClrStationEvaluations = ClearingLoadDispatchEvaluator.EvaluateAll(
            LastFleetStatuses, ClrInFlightDispatches);
    }

    void RecomputeClearingLoadPairings()
    {
        if (ClrStationEvaluations is null) return;
        var stations = DispatchableClrStations
            .Where(s => !ClearingLoadDispatchTracker.IsStationLocked(ClrInFlightDispatches, s.ParkingPointId))
            .ToList();
        var busy = GetBusyAmrCodes();
        ClrPairings = ClearingLoadPairingService.Pair(stations, ClrCakeVehicles, busy);
    }

    void RefreshInFlightStatus()
    {
        var previous = InFlightDispatches.ToList();
        var updated = BufferDispatchTracker.Refresh(
            InFlightDispatches, LastRobots, BufferEvaluations);

        foreach (var done in updated.Where(f => f.IsCompleted))
        {
            var wasActive = previous.Any(p =>
                p.IsActive &&
                p.TaskId.Equals(done.TaskId, StringComparison.OrdinalIgnoreCase));
            if (wasActive)
                AppendCompletionLog(done);
        }

        InFlightDispatches.Clear();
        InFlightDispatches.AddRange(BufferDispatchTracker.PruneCompleted(updated));
    }

    void RefreshTwistingInFlightStatus()
    {
        var previous = TwpInFlightDispatches.ToList();
        var updated = TwistingLoadDispatchTracker.Refresh(
            TwpInFlightDispatches, LastRobots, TwpMachineEvaluations, LastFleetStatuses);

        foreach (var done in updated.Where(f => f.IsCompleted))
        {
            var wasActive = previous.Any(p =>
                p.IsActive &&
                p.MissionKey.Equals(done.MissionKey, StringComparison.OrdinalIgnoreCase));
            if (wasActive)
                AppendTwpCompletionLog(done);
        }

        TwpInFlightDispatches.Clear();
        TwpInFlightDispatches.AddRange(TwistingLoadDispatchTracker.PruneCompleted(updated));
    }

    void RefreshTwistingUnloadInFlightStatus()
    {
        var previous = TwpUnloadInFlightDispatches.ToList();
        var updated = TwistingLoadDispatchTracker.Refresh(
            TwpUnloadInFlightDispatches,
            LastRobots,
            TwpUnloadMachineEvaluations,
            LastFleetStatuses,
            forUnload: true);

        foreach (var done in updated.Where(f => f.IsCompleted))
        {
            var wasActive = previous.Any(p =>
                p.IsActive &&
                p.MissionKey.Equals(done.MissionKey, StringComparison.OrdinalIgnoreCase));
            if (wasActive)
                AppendTwpUnloadCompletionLog(done);
        }

        TwpUnloadInFlightDispatches.Clear();
        TwpUnloadInFlightDispatches.AddRange(TwistingLoadDispatchTracker.PruneCompleted(updated));
    }

    void RefreshClearingLoadInFlightStatus()
    {
        var previous = ClrInFlightDispatches.ToList();
        var updated = ClearingLoadDispatchTracker.Refresh(ClrInFlightDispatches, LastRobots);

        foreach (var done in updated.Where(f => f.IsCompleted))
        {
            var wasActive = previous.Any(p =>
                p.IsActive &&
                p.TaskId.Equals(done.TaskId, StringComparison.OrdinalIgnoreCase));
            if (wasActive)
                AppendClrCompletionLog(done);
        }

        ClrInFlightDispatches.Clear();
        ClrInFlightDispatches.AddRange(ClearingLoadDispatchTracker.PruneCompleted(updated));
    }

    void AppendCompletionLog(BufferDispatchInFlight flight)
    {
        DispatchLogs.Insert(0, new BufferDispatchLogEntry(
            DateTime.Now, flight.ParkingPointId, flight.AmrCode, true,
            $"任務完成：{flight.StatusHint}", null, null));
    }

    void AppendTwpCompletionLog(TwistingLoadInFlight flight)
    {
        TwpDispatchLogs.Insert(0, new BufferDispatchLogEntry(
            DateTime.Now, flight.MissionKey, flight.AmrCode, true,
            $"任務完成：{flight.StatusHint}", null, null));
    }

    void AppendTwpUnloadCompletionLog(TwistingLoadInFlight flight)
    {
        TwpUnloadDispatchLogs.Insert(0, new BufferDispatchLogEntry(
            DateTime.Now, flight.MissionKey, flight.AmrCode, true,
            $"任務完成：{flight.StatusHint}", null, null));
    }

    void AppendClrCompletionLog(ClearingLoadDispatchInFlight flight)
    {
        ClrDispatchLogs.Insert(0, new BufferDispatchLogEntry(
            DateTime.Now, flight.ParkingPointId, flight.AmrCode, true,
            $"任務完成：{flight.StatusHint}", null, null));
    }

    async Task RefreshAmrAndPairAsync()
    {
        if (!await EnsureTokenAsync()) return;

        try
        {
            var ct = _pollCts?.Token ?? default;
            var robotResult = await _amrApi.PollRobotsStatusAsync(_token!, ct);
            if (!robotResult.Success)
            {
                AmrError = ApiErrorFormatter.FromMessage(robotResult.Error, "無法取得 /v2/robots/status");
                if (ShouldInvalidateTgiToken(robotResult.Error))
                    InvalidateTgiSession();
                return;
            }

            var taskResult = await _amrApi.PollActiveSimulationTasksAsync(_token!, ct);
            var simResult = await _amrApi.PollSimulationAmrsAsync(_token!, ct);
            var fleetResult = await _amrApi.PollFleetStatusAsync(_token!, ct);
            var wpResult = await _amrApi.GetWaitPointStatusAsync(_token!, ct);

            var activeTasks = taskResult.Success ? taskResult.Data : [];
            var simulationAmrs = simResult.Success ? simResult.Data : null;
            var waitPoints = wpResult.Success
                ? FleetParkingPlanner.GetWaitPointList(wpResult.Data ?? [])
                : [];
            var fleetStatuses = fleetResult.Success ? fleetResult.Data : null;
            if (fleetStatuses is not null)
                LastFleetStatuses = fleetStatuses;

            TgiRobotReportCount = robotResult.Data.Count;
            LastRobots = robotResult.Data;
            RefreshInFlightStatus();
            RefreshTwistingInFlightStatus();
            RefreshTwistingUnloadInFlightStatus();
            RefreshClearingLoadInFlightStatus();

            var inFlightAmrCodes = InFlightDispatches.Where(f => f.IsActive).Select(f => f.AmrCode)
                .Concat(TwpInFlightDispatches.Where(f => f.IsActive).Select(f => f.AmrCode))
                .Concat(TwpUnloadInFlightDispatches.Where(f => f.IsActive).Select(f => f.AmrCode))
                .Concat(ClrInFlightDispatches.Where(f => f.IsActive).Select(f => f.AmrCode));
            CakeVehicles = CakeVehicleDispatchEvaluator.EvaluateFleet(
                robotResult.Data, simulationAmrs, activeTasks, fleetStatuses, waitPoints, inFlightAmrCodes);
            TwpCakeVehicles = CakeVehicleDispatchEvaluator.EvaluateFleetForTwistingLoad(
                robotResult.Data, simulationAmrs, activeTasks, fleetStatuses, inFlightAmrCodes);
            TwpUnloadCakeVehicles = CakeVehicleDispatchEvaluator.EvaluateFleetForTwistingUnload(
                robotResult.Data, simulationAmrs, activeTasks, fleetStatuses, inFlightAmrCodes);
            ClrCakeVehicles = CakeVehicleDispatchEvaluator.EvaluateFleetForClearingLoad(
                robotResult.Data, simulationAmrs, activeTasks, fleetStatuses, inFlightAmrCodes);

            AmrError = null;
            LastAmrRefresh = DateTime.Now;
            if (BufferEvaluations is not null)
            {
                BufferEvaluations = BufferDispatchEvaluator.ApplyParkingOccupancy(
                    BufferEvaluations,
                    LastFleetStatuses);
            }
            RecomputeClearingLoadEvaluations();
            RecomputePairings();
            RecomputeTwistingPairings();
            RecomputeTwistingUnloadPairings();
            RecomputeClearingLoadPairings();
        }
        catch (Exception ex)
        {
            AmrError = ApiErrorFormatter.FromException(ex);
        }
    }

    static bool ShouldInvalidateTgiToken(string? error) =>
        error is not null && error.Contains("401", StringComparison.Ordinal);

    void InvalidateTgiSession() => _token = null;

    static bool IsBufferAutoDispatchElapsed(DateTime? lastAt) =>
        lastAt is null ||
        (DateTime.Now - lastAt.Value).TotalSeconds >= BufferAutoDispatchIntervalSeconds;

    static bool IsTwpAutoDispatchElapsed(DateTime? lastAt) =>
        lastAt is null ||
        (DateTime.Now - lastAt.Value).TotalSeconds >= TwpAutoDispatchIntervalSeconds;

    async Task<bool> TryDispatchNextFlowForInFlightAsync(string sequenceId)
    {
        if (string.IsNullOrEmpty(_token)) return false;

        var idx = TwpInFlightDispatches.FindIndex(f =>
            f.SequenceId.Equals(sequenceId, StringComparison.Ordinal));
        if (idx < 0) return false;

        var flight = TwpInFlightDispatches[idx];
        if (flight.IsFullyDispatched) return true;
        if (flight.CompletedStops >= flight.FlowStops.Count) return false;

        var stop = flight.FlowStops[flight.CompletedStops];
        var ct = _pollCts?.Token ?? default;
        var result = await _amrApi.TriggerFlowDiagnosticAsync(
            _token!, TwistingLoadFlowDispatchBuilder.FlowName, stop.FlowRequest, ct);
        AppendTwpDispatchLog(flight, stop, result);

        if (!result.Success || result.Data is null)
        {
            TwpInFlightDispatches[idx] = TwistingLoadDispatchTracker.UpdateProgress(
                flight, flight.TaskIds,
                $"{stop.ParkingPointId} 派車失敗，{TwpDispatchRetryDelaySeconds} 秒後重試",
                nextRetryAt: DateTime.Now.AddSeconds(TwpDispatchRetryDelaySeconds));
            return false;
        }

        var taskId = !string.IsNullOrWhiteSpace(result.Data.TaskId)
            ? result.Data.TaskId
            : result.Data.FlowId;
        if (string.IsNullOrWhiteSpace(taskId))
        {
            TwpInFlightDispatches[idx] = TwistingLoadDispatchTracker.UpdateProgress(
                flight, flight.TaskIds,
                $"{stop.ParkingPointId} 未取得 taskId，{TwpDispatchRetryDelaySeconds} 秒後重試",
                nextRetryAt: DateTime.Now.AddSeconds(TwpDispatchRetryDelaySeconds));
            return false;
        }

        var taskIds = flight.TaskIds.ToList();
        taskIds.Add(taskId);
        var now = DateTime.Now;
        var enteredLane = TwistingLoadLaneAdmission.HasFlightEnteredLane(flight, LastFleetStatuses);
        var laneLabel = TwistingLoadLaneAdmission.FormatTwpLane(flight.TwpGroupId);
        var fleet = TwistingLoadLaneAdmission.ResolveFleetVehicle(LastFleetStatuses, flight.AmrCode);
        var hint = taskIds.Count < flight.TotalFlows
            ? enteredLane
                ? $"已派 {taskIds.Count}/{flight.TotalFlows} flow · 已開放 {laneLabel} 下一組派車"
                : $"已派 {taskIds.Count}/{flight.TotalFlows} flow · 等待 {flight.AmrCode} 開進 {laneLabel}（目前 {fleet?.CurrentSite ?? "—"}）"
            : $"已派車 {flight.TotalFlows}/{flight.TotalFlows} flow，等待作業完成";

        TwpInFlightDispatches[idx] = TwistingLoadDispatchTracker.UpdateProgress(
            flight, taskIds, hint,
            lastFlowDispatchedAt: now,
            nextRetryAt: null,
            hasEnteredLane: enteredLane);
        RefreshTwistingInFlightStatus();
        RecomputeTwistingPairings();
        return true;
    }

    async Task<bool> TryDispatchNextUnloadFlowForInFlightAsync(string sequenceId)
    {
        if (string.IsNullOrEmpty(_token)) return false;

        var idx = TwpUnloadInFlightDispatches.FindIndex(f =>
            f.SequenceId.Equals(sequenceId, StringComparison.Ordinal));
        if (idx < 0) return false;

        var flight = TwpUnloadInFlightDispatches[idx];
        if (flight.IsFullyDispatched) return true;
        if (flight.CompletedStops >= flight.FlowStops.Count) return false;

        var stop = flight.FlowStops[flight.CompletedStops];
        var ct = _pollCts?.Token ?? default;
        var result = await _amrApi.TriggerFlowDiagnosticAsync(
            _token!, TwistingUnloadFlowDispatchBuilder.FlowName, stop.FlowRequest, ct);
        AppendTwpUnloadDispatchLog(flight, stop, result);

        if (!result.Success || result.Data is null)
        {
            TwpUnloadInFlightDispatches[idx] = TwistingLoadDispatchTracker.UpdateProgress(
                flight, flight.TaskIds,
                $"{stop.ParkingPointId} 派車失敗，{TwpDispatchRetryDelaySeconds} 秒後重試",
                nextRetryAt: DateTime.Now.AddSeconds(TwpDispatchRetryDelaySeconds));
            return false;
        }

        var taskId = !string.IsNullOrWhiteSpace(result.Data.TaskId)
            ? result.Data.TaskId
            : result.Data.FlowId;
        if (string.IsNullOrWhiteSpace(taskId))
        {
            TwpUnloadInFlightDispatches[idx] = TwistingLoadDispatchTracker.UpdateProgress(
                flight, flight.TaskIds,
                $"{stop.ParkingPointId} 未取得 taskId，{TwpDispatchRetryDelaySeconds} 秒後重試",
                nextRetryAt: DateTime.Now.AddSeconds(TwpDispatchRetryDelaySeconds));
            return false;
        }

        var taskIds = flight.TaskIds.ToList();
        taskIds.Add(taskId);
        var now = DateTime.Now;
        var enteredLane = TwistingLoadLaneAdmission.HasFlightEnteredLane(flight, LastFleetStatuses);
        var laneLabel = TwistingLoadLaneAdmission.FormatTwpLane(flight.TwpGroupId);
        var fleet = TwistingLoadLaneAdmission.ResolveFleetVehicle(LastFleetStatuses, flight.AmrCode);
        var hint = taskIds.Count < flight.TotalFlows
            ? enteredLane
                ? $"已派 {taskIds.Count}/{flight.TotalFlows} flow · 已開放 {laneLabel} 下一組派車"
                : $"已派 {taskIds.Count}/{flight.TotalFlows} flow · 等待 {flight.AmrCode} 開進 {laneLabel}（目前 {fleet?.CurrentSite ?? "—"}）"
            : $"已派車 {flight.TotalFlows}/{flight.TotalFlows} flow，等待作業完成";

        TwpUnloadInFlightDispatches[idx] = TwistingLoadDispatchTracker.UpdateProgress(
            flight, taskIds, hint,
            lastFlowDispatchedAt: now,
            nextRetryAt: null,
            hasEnteredLane: enteredLane);
        RefreshTwistingUnloadInFlightStatus();
        RecomputeTwistingUnloadPairings();
        return true;
    }

    TwistingLoadMachineEvaluation? ResolveTwpMachineEvaluation(int machineId, char side) =>
        TwpMachineEvaluations?.FirstOrDefault(m =>
            m.MachineId == machineId &&
            char.ToUpperInvariant(m.Side) == char.ToUpperInvariant(side));

    TwistingLoadMachineEvaluation? ResolveTwpUnloadMachineEvaluation(int machineId, char side) =>
        TwpUnloadMachineEvaluations?.FirstOrDefault(m =>
            m.MachineId == machineId &&
            char.ToUpperInvariant(m.Side) == char.ToUpperInvariant(side));

    bool CanStartTwpPair(TwistingLoadDispatchPair pair)
    {
        var machine = ResolveTwpMachineEvaluation(pair.Machine.MachineId, pair.Machine.Side);
        var blockIndex = TwistingLoadLaneAdmission.GetLaneBlockIndex(pair.MissionStops);
        return TwistingLoadDispatchTracker.CanDispatchMissionBlock(
            TwpInFlightDispatches,
            pair.Machine.MachineId,
            pair.Machine.Side,
            pair.Machine.TwpGroupId,
            blockIndex,
            pair.MissionStops.Select(s => s.ParkingPointId),
            machine?.AvailableMissions ?? pair.Machine.AvailableMissions,
            TwpMachineEvaluations,
            machine?.DockingPoints ?? pair.Machine.DockingPoints,
            LastFleetStatuses);
    }

    bool CanStartTwpUnloadPair(TwistingLoadDispatchPair pair)
    {
        var machine = ResolveTwpUnloadMachineEvaluation(pair.Machine.MachineId, pair.Machine.Side);
        var blockIndex = TwistingLoadLaneAdmission.GetLaneBlockIndex(pair.MissionStops);
        return TwistingLoadDispatchTracker.CanDispatchMissionBlock(
            TwpUnloadInFlightDispatches,
            pair.Machine.MachineId,
            pair.Machine.Side,
            pair.Machine.TwpGroupId,
            blockIndex,
            pair.MissionStops.Select(s => s.ParkingPointId),
            machine?.AvailableMissions ?? pair.Machine.AvailableMissions,
            TwpUnloadMachineEvaluations,
            machine?.DockingPoints ?? pair.Machine.DockingPoints,
            LastFleetStatuses,
            TwistingParkingRegistry.RequestUnloadStatus);
    }

    async Task TryAutoDispatchAsync()
    {
        if (Pairings is null || Pairings.Count == 0 || DispatchBusy || string.IsNullOrEmpty(_token)) return;
        if (!IsBufferAutoDispatchElapsed(_lastBufferAutoFlowAt)) return;

        DispatchBusy = true;
        try
        {
            var pair = Pairings.FirstOrDefault(p =>
                !IsAmrBusy(p.Vehicle.AmrCode)
                && !BufferDispatchEvaluator.IsParkingPointOccupied(
                    LastFleetStatuses, p.Buffer.ParkingPointId, out _));
            if (pair is null) return;

            var result = await _amrApi.TriggerFlowDiagnosticAsync(
                _token!, pair.FlowName, pair.FlowRequest, _pollCts?.Token ?? default);
            _lastBufferAutoFlowAt = DateTime.Now;
            HandleDispatchResult(pair, result);
        }
        finally
        {
            DispatchBusy = false;
        }
    }

    async Task TryTwpAutoDispatchAsync()
    {
        if (TwpDispatchBusy || string.IsNullOrEmpty(_token)) return;
        if (TwpPairings is null || TwpPairings.Count == 0) return;
        if (!IsTwpAutoDispatchElapsed(_lastTwpAutoFlowAt)) return;

        var pair = TwpPairings.FirstOrDefault(GetTwpPairCanDispatch);
        if (pair is null) return;

        TwpDispatchBusy = true;
        try
        {
            _lastTwpAutoFlowAt = DateTime.Now;
            await DispatchTwpPairCoreAsync(pair);
        }
        finally
        {
            TwpDispatchBusy = false;
        }
    }

    async Task TryTwpUnloadAutoDispatchAsync()
    {
        if (TwpUnloadDispatchBusy || string.IsNullOrEmpty(_token)) return;
        if (TwpUnloadPairings is null || TwpUnloadPairings.Count == 0) return;
        if (!IsTwpAutoDispatchElapsed(_lastTwpUnloadAutoFlowAt)) return;

        var pair = TwpUnloadPairings.FirstOrDefault(GetTwpUnloadPairCanDispatch);
        if (pair is null) return;

        TwpUnloadDispatchBusy = true;
        try
        {
            _lastTwpUnloadAutoFlowAt = DateTime.Now;
            await DispatchTwpUnloadPairCoreAsync(pair);
        }
        finally
        {
            TwpUnloadDispatchBusy = false;
        }
    }

    async Task TryClrAutoDispatchAsync()
    {
        if (ClrPairings is null || ClrPairings.Count == 0 || ClrDispatchBusy || string.IsNullOrEmpty(_token))
            return;
        if (!IsTwpAutoDispatchElapsed(_lastClrAutoFlowAt)) return;

        ClrDispatchBusy = true;
        try
        {
            var pair = ClrPairings.FirstOrDefault(p =>
                !IsAmrBusy(p.Vehicle.AmrCode)
                && !ClearingLoadDispatchTracker.IsStationLocked(ClrInFlightDispatches, p.Station.ParkingPointId)
                && !ClearingLoadDispatchEvaluator.IsParkingPointOccupied(
                    LastFleetStatuses, p.Station.ParkingPointId, out _));
            if (pair is null) return;

            var result = await _amrApi.TriggerFlowDiagnosticAsync(
                _token!, pair.FlowName, pair.FlowRequest, _pollCts?.Token ?? default);
            _lastClrAutoFlowAt = DateTime.Now;
            HandleClrDispatchResult(pair, result);
        }
        finally
        {
            ClrDispatchBusy = false;
        }
    }

    async Task DispatchTwpPairCoreAsync(TwistingLoadDispatchPair pair)
    {
        if (string.IsNullOrEmpty(_token)) return;

        var sequenceId = Guid.NewGuid().ToString("N");
        TwpInFlightDispatches.Add(TwistingLoadDispatchTracker.Register(pair, sequenceId, []));
        RecomputeTwistingPairings();
        NotifyChanged();

        var ct = _pollCts?.Token ?? default;
        while (true)
        {
            var flight = TwpInFlightDispatches.FirstOrDefault(f =>
                f.SequenceId.Equals(sequenceId, StringComparison.Ordinal));
            if (flight is null || flight.IsFullyDispatched) break;
            if (ct.IsCancellationRequested) break;

            if (flight.NextRetryAt is not null)
            {
                var waitMs = (int)(flight.NextRetryAt.Value - DateTime.Now).TotalMilliseconds;
                if (waitMs > 0)
                    await Task.Delay(waitMs, ct);
            }

            await TryDispatchNextFlowForInFlightAsync(sequenceId);
            NotifyChanged();
        }

        RefreshTwistingInFlightStatus();
        NotifyChanged();
    }

    async Task DispatchTwpUnloadPairCoreAsync(TwistingLoadDispatchPair pair)
    {
        if (string.IsNullOrEmpty(_token)) return;

        var sequenceId = Guid.NewGuid().ToString("N");
        TwpUnloadInFlightDispatches.Add(TwistingLoadDispatchTracker.Register(pair, sequenceId, []));
        RecomputeTwistingUnloadPairings();
        RecomputeClearingLoadPairings();
        NotifyChanged();

        var ct = _pollCts?.Token ?? default;
        while (true)
        {
            var flight = TwpUnloadInFlightDispatches.FirstOrDefault(f =>
                f.SequenceId.Equals(sequenceId, StringComparison.Ordinal));
            if (flight is null || flight.IsFullyDispatched) break;
            if (ct.IsCancellationRequested) break;

            if (flight.NextRetryAt is not null)
            {
                var waitMs = (int)(flight.NextRetryAt.Value - DateTime.Now).TotalMilliseconds;
                if (waitMs > 0)
                    await Task.Delay(waitMs, ct);
            }

            await TryDispatchNextUnloadFlowForInFlightAsync(sequenceId);
            NotifyChanged();
        }

        RefreshTwistingUnloadInFlightStatus();
        NotifyChanged();
    }

    void AppendTwpDispatchLog(
        TwistingLoadInFlight flight,
        TwistingLoadFlowStop stop,
        ApiCallResult result)
    {
        TwpDispatchLogs.Insert(0, new BufferDispatchLogEntry(
            DateTime.Now,
            $"{flight.MissionKey} · {stop.ParkingPointId} (#{stop.StopIndex})",
            flight.AmrCode,
            result.Success,
            result.Summary,
            stop.JsonBody,
            result.ResponseBody));
    }

    void AppendTwpUnloadDispatchLog(
        TwistingLoadInFlight flight,
        TwistingLoadFlowStop stop,
        ApiCallResult result)
    {
        TwpUnloadDispatchLogs.Insert(0, new BufferDispatchLogEntry(
            DateTime.Now,
            $"{flight.MissionKey} · {stop.ParkingPointId} (#{stop.StopIndex})",
            flight.AmrCode,
            result.Success,
            result.Summary,
            stop.JsonBody,
            result.ResponseBody));
    }

    void HandleDispatchResult(BufferDispatchPair pair, ApiCallResult<TriggerFlowResultDto> result)
    {
        AppendDispatchLog(pair, result);
        if (!result.Success || result.Data is null) return;

        var taskId = !string.IsNullOrWhiteSpace(result.Data.TaskId)
            ? result.Data.TaskId
            : result.Data.FlowId;
        if (string.IsNullOrWhiteSpace(taskId)) return;

        InFlightDispatches.Add(BufferDispatchTracker.Register(
            pair.Buffer.ParkingPointId,
            pair.Vehicle.AmrCode,
            taskId,
            pair.Buffer.OperationSideLabel));
        RefreshInFlightStatus();
        RecomputePairings();
    }

    void HandleClrDispatchResult(ClearingLoadDispatchPair pair, ApiCallResult<TriggerFlowResultDto> result)
    {
        AppendClrDispatchLog(pair, result);
        if (!result.Success || result.Data is null) return;

        var taskId = !string.IsNullOrWhiteSpace(result.Data.TaskId)
            ? result.Data.TaskId
            : result.Data.FlowId;
        if (string.IsNullOrWhiteSpace(taskId)) return;

        ClrInFlightDispatches.Add(ClearingLoadDispatchTracker.Register(
            pair.Station.ParkingPointId,
            pair.Vehicle.AmrCode,
            taskId));
        RefreshClearingLoadInFlightStatus();
        RecomputeClearingLoadEvaluations();
        RecomputeClearingLoadPairings();
        RecomputeTwistingUnloadPairings();
    }

    void AppendDispatchLog(BufferDispatchPair pair, ApiCallResult result)
    {
        DispatchLogs.Insert(0, new BufferDispatchLogEntry(
            DateTime.Now,
            pair.Buffer.ParkingPointId,
            pair.Vehicle.AmrCode,
            result.Success,
            result.Summary,
            pair.JsonBody,
            result.ResponseBody));
    }

    void AppendClrDispatchLog(ClearingLoadDispatchPair pair, ApiCallResult result)
    {
        ClrDispatchLogs.Insert(0, new BufferDispatchLogEntry(
            DateTime.Now,
            pair.Station.ParkingPointId,
            pair.Vehicle.AmrCode,
            result.Success,
            result.Summary,
            pair.JsonBody,
            result.ResponseBody));
    }

    async Task<bool> EnsureTokenAsync()
    {
        if (!string.IsNullOrEmpty(_token)) return true;

        var login = await _amrApi.LoginAsync(_pollCts?.Token ?? default);
        if (login.Success && login.Data is not null)
        {
            _token = login.Data.AccessToken;
            AmrError = null;
            return true;
        }

        _token = null;
        AmrError = ApiErrorFormatter.FromMessage(login.Summary, "TGI 登入失敗");
        return false;
    }

    public void Dispose()
    {
        if (_pollCts is null) return;
        _pollCts.Cancel();
        _pollCts.Dispose();
        _pollCts = null;
    }
}
