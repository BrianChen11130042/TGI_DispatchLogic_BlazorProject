using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// 撚紗上料優先權（由大到小）：
/// 1. 撚紗機（M01 → M02 → …）
/// 2. 同機左右走道（各 TWP 群組互不擋，可同時派尾端組）
/// 3. 同走道內尾端往前（19~21 → 16~18 → …，前組須進入走道才開放後組）
/// </summary>
public static class TwistingLoadLaneAdmission
{
    public static string FormatTwpLane(int twpGroupId) => $"TWP{twpGroupId:D2}";

    public static int GetLaneBlockIndex(IReadOnlyList<TwistingDockingPointEvaluation> stops)
    {
        if (stops.Count == 0) return int.MaxValue;
        var maxSeq = stops.Max(s => s.Sequence);
        return (TwistingParkingRegistry.DockingPointsPerSide - maxSeq)
               / TwistingParkingRegistry.StopsPerLoadMission;
    }

    public static int GetLaneBlockIndex(IReadOnlyList<string> parkingPointIds)
    {
        if (parkingPointIds.Count == 0) return int.MaxValue;
        var maxSeq = parkingPointIds
            .Select(ParseSequenceFromParkingPointId)
            .DefaultIfEmpty(0)
            .Max();
        return (TwistingParkingRegistry.DockingPointsPerSide - maxSeq)
               / TwistingParkingRegistry.StopsPerLoadMission;
    }

    public static bool IsMachineAdmissionGranted(
        int machineId,
        IReadOnlyList<TwistingLoadMachineEvaluation> allMachines,
        IEnumerable<TwistingLoadInFlight> flights,
        IEnumerable<FleetStatusDto>? fleetStatuses = null)
    {
        foreach (var eval in allMachines.Where(m =>
                     m.MachineId < machineId &&
                     m.Status == TwistingParkingRegistry.CallVehicleStatus))
        {
            if (!IsSideTailLaneCleared(eval, flights, fleetStatuses))
                return false;
        }

        return true;
    }

    public static string? TryGetMachineBlockReason(
        int machineId,
        IReadOnlyList<TwistingLoadMachineEvaluation> allMachines,
        IEnumerable<TwistingLoadInFlight> flights,
        IEnumerable<FleetStatusDto>? fleetStatuses = null)
    {
        foreach (var eval in allMachines
                     .Where(m => m.MachineId < machineId && m.Status == TwistingParkingRegistry.CallVehicleStatus)
                     .OrderBy(m => m.MachineId)
                     .ThenBy(m => m.Side))
        {
            if (IsSideTailLaneCleared(eval, flights, fleetStatuses))
                continue;

            return TryGetPriorLaneBlockReason(
                flights,
                eval.TwpGroupId,
                0,
                eval.AvailableMissions,
                eval.DockingPoints,
                eval.MachineCode,
                fleetStatuses: fleetStatuses);
        }

        return null;
    }

    public static bool IsLaneAdmissionGranted(
        IEnumerable<TwistingLoadInFlight> flights,
        int twpGroupId,
        int blockIndex,
        IReadOnlyList<IReadOnlyList<TwistingDockingPointEvaluation>> availableMissions,
        IReadOnlyList<TwistingDockingPointEvaluation>? dockingPoints = null,
        IEnumerable<FleetStatusDto>? fleetStatuses = null)
    {
        if (blockIndex <= 0) return true;

        var activeOnLane = flights
            .Where(f => f.IsActive && f.TwpGroupId == twpGroupId)
            .ToList();

        if (activeOnLane.Any(f =>
                f.LaneBlockIndex < blockIndex && !HasFlightEnteredLane(f, fleetStatuses)))
            return false;

        for (var i = 0; i < blockIndex; i++)
        {
            if (!IsPriorLaneBlockSatisfied(flights, twpGroupId, i, availableMissions, dockingPoints, fleetStatuses))
                return false;
        }

        return true;
    }

    public static string? TryGetLaneBlockReason(
        IEnumerable<TwistingLoadInFlight> flights,
        int twpGroupId,
        int blockIndex,
        IReadOnlyList<IReadOnlyList<TwistingDockingPointEvaluation>> availableMissions,
        IReadOnlyList<TwistingDockingPointEvaluation>? dockingPoints = null,
        IEnumerable<FleetStatusDto>? fleetStatuses = null)
    {
        if (blockIndex <= 0) return null;

        var laneLabel = FormatTwpLane(twpGroupId);
        var activeOnLane = flights
            .Where(f => f.IsActive && f.TwpGroupId == twpGroupId)
            .ToList();

        var waitingEntry = activeOnLane
            .Where(f => f.LaneBlockIndex < blockIndex && !HasFlightEnteredLane(f, fleetStatuses))
            .OrderBy(f => f.LaneBlockIndex)
            .FirstOrDefault();
        if (waitingEntry is not null)
            return FormatWaitingForLaneEntry(waitingEntry, laneLabel, fleetStatuses);

        for (var i = 0; i < blockIndex; i++)
        {
            if (IsPriorLaneBlockSatisfied(flights, twpGroupId, i, availableMissions, dockingPoints, fleetStatuses))
                continue;

            return TryGetPriorLaneBlockReason(
                flights, twpGroupId, i, availableMissions, dockingPoints, laneLabel: laneLabel, fleetStatuses: fleetStatuses);
        }

        return null;
    }

    /// <summary>
    /// 車輛 current_site 已在該 TWP 走道任一站點（如 TWP01-19）視為已進入走道。
    /// </summary>
    public static bool HasFlightEnteredLane(
        TwistingLoadInFlight flight,
        IEnumerable<FleetStatusDto>? fleetStatuses) =>
        IsVehicleOnTwpLane(ResolveFleetVehicle(fleetStatuses, flight.AmrCode), flight.TwpGroupId);

    public static bool IsVehicleOnTwpLane(FleetStatusDto? vehicle, int twpGroupId)
    {
        if (vehicle is null || !FleetParkingPlanner.IsMeaningfulSite(vehicle.CurrentSite))
            return false;

        var prefix = FormatTwpLane(twpGroupId) + "-";
        return vehicle.CurrentSite!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    public static FleetStatusDto? ResolveFleetVehicle(IEnumerable<FleetStatusDto>? fleet, string amrCode)
    {
        if (fleet is null) return null;

        foreach (var alias in CakeVehicleDispatchEvaluator.ExpandRobotAliases(amrCode))
        {
            var match = fleet.FirstOrDefault(v =>
                v.RobotId.Equals(alias, StringComparison.OrdinalIgnoreCase)
                || v.RobotName.Equals(alias, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        return null;
    }

    /// <summary>
    /// 前序走道組已滿足：執行中車輛已開進走道、已上料完成、或該組已無待派停車點。
    /// </summary>
    public static bool IsPriorLaneBlockSatisfied(
        IEnumerable<TwistingLoadInFlight> flights,
        int twpGroupId,
        int priorBlockIndex,
        IReadOnlyList<IReadOnlyList<TwistingDockingPointEvaluation>> availableMissions,
        IReadOnlyList<TwistingDockingPointEvaluation>? dockingPoints = null,
        IEnumerable<FleetStatusDto>? fleetStatuses = null)
    {
        var activePrior = flights.FirstOrDefault(f =>
            f.IsActive && f.TwpGroupId == twpGroupId && f.LaneBlockIndex == priorBlockIndex);
        if (activePrior is not null)
            return HasFlightEnteredLane(activePrior, fleetStatuses);

        if (!HasDispatchableMissionAtBlock(priorBlockIndex, availableMissions))
            return true;

        if (IsLaneBlockLoaded(priorBlockIndex, dockingPoints))
            return true;

        return false;
    }

    static bool IsSideTailLaneCleared(
        TwistingLoadMachineEvaluation eval,
        IEnumerable<TwistingLoadInFlight> flights,
        IEnumerable<FleetStatusDto>? fleetStatuses) =>
        IsPriorLaneBlockSatisfied(
            flights,
            eval.TwpGroupId,
            0,
            eval.AvailableMissions,
            eval.DockingPoints,
            fleetStatuses);

    static string? TryGetPriorLaneBlockReason(
        IEnumerable<TwistingLoadInFlight> flights,
        int twpGroupId,
        int priorBlockIndex,
        IReadOnlyList<IReadOnlyList<TwistingDockingPointEvaluation>> availableMissions,
        IReadOnlyList<TwistingDockingPointEvaluation>? dockingPoints,
        string? machineCode = null,
        string? laneLabel = null,
        IEnumerable<FleetStatusDto>? fleetStatuses = null)
    {
        laneLabel ??= FormatTwpLane(twpGroupId);
        var prefix = machineCode is not null ? $"{machineCode} " : "";

        var activePrior = flights.FirstOrDefault(f =>
            f.IsActive && f.TwpGroupId == twpGroupId && f.LaneBlockIndex == priorBlockIndex);
        if (activePrior is not null && !HasFlightEnteredLane(activePrior, fleetStatuses))
            return FormatWaitingForLaneEntry(activePrior, $"{prefix}{laneLabel}".Trim(), fleetStatuses);

        var waitingMission = availableMissions.FirstOrDefault(m => GetLaneBlockIndex(m) == priorBlockIndex);
        if (waitingMission is not null)
        {
            var label = priorBlockIndex == 0 ? "尾端組" : "前序組";
            return $"需先派送 {prefix}{laneLabel} {label} {TwistingLoadMissionPlanner.FormatMissionStops(waitingMission)}";
        }

        return $"需等待 {prefix}{laneLabel} 前序組完成";
    }

    static string FormatWaitingForLaneEntry(
        TwistingLoadInFlight flight,
        string laneLabel,
        IEnumerable<FleetStatusDto>? fleetStatuses)
    {
        var fleet = ResolveFleetVehicle(fleetStatuses, flight.AmrCode);
        var site = fleet?.CurrentSite ?? "—";
        return $"需等待 {flight.AmrCode} 開進 {laneLabel} 走道（目前位置 {site}）";
    }

    static bool HasDispatchableMissionAtBlock(
        int blockIndex,
        IReadOnlyList<IReadOnlyList<TwistingDockingPointEvaluation>> availableMissions) =>
        availableMissions.Any(m =>
            GetLaneBlockIndex(m) == blockIndex &&
            m.Any(p => p.IsDispatchable));

    static bool IsLaneBlockLoaded(
        int blockIndex,
        IReadOnlyList<TwistingDockingPointEvaluation>? dockingPoints)
    {
        if (dockingPoints is null || dockingPoints.Count == 0)
            return false;

        var tailSeq = TwistingParkingRegistry.DockingPointsPerSide
                      - blockIndex * TwistingParkingRegistry.StopsPerLoadMission;
        var headSeq = tailSeq - TwistingParkingRegistry.StopsPerLoadMission + 1;
        var bySeq = dockingPoints.ToDictionary(p => p.Sequence);

        for (var seq = headSeq; seq <= tailSeq; seq++)
        {
            if (!bySeq.TryGetValue(seq, out var point) || point.IsDispatchable)
                return false;
        }

        return true;
    }

    static string FormatMissionStops(IReadOnlyList<string> parkingPointIds) =>
        string.Join(" → ", parkingPointIds);

    static int ParseSequenceFromParkingPointId(string parkingPointId)
    {
        var dash = parkingPointId.LastIndexOf('-');
        if (dash < 0 || dash >= parkingPointId.Length - 1) return 0;
        return int.TryParse(parkingPointId[(dash + 1)..], out var seq) ? seq : 0;
    }
}
