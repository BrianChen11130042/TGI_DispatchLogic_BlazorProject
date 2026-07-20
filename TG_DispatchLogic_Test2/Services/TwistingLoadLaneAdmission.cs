using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// 撚紗派車走道優先權（由大到小）：
/// 1. 撚紗機（M01 → M02 → …）
/// 2. 同機左右走道（各 TWP 群組互不擋，可同時派尾端組）
/// 3. 同走道內尾端往前（前組須進入走道才開放後組）
/// </summary>
public static class TwistingLoadLaneAdmission
{
    public static string FormatTwpLane(int twpGroupId) => $"TWP{twpGroupId:D2}";

    public static int GetLaneBlockIndex(
        IReadOnlyList<TwistingDockingPointEvaluation> stops,
        int stopsPerMission = TwistingParkingRegistry.StopsPerLoadMission)
    {
        if (stops.Count == 0) return int.MaxValue;
        var maxSeq = stops.Max(s => s.Sequence);
        return (TwistingParkingRegistry.DockingPointsPerSide - maxSeq) / stopsPerMission;
    }

    public static int GetLaneBlockIndex(
        IReadOnlyList<string> parkingPointIds,
        int stopsPerMission = TwistingParkingRegistry.StopsPerLoadMission)
    {
        if (parkingPointIds.Count == 0) return int.MaxValue;
        var maxSeq = parkingPointIds
            .Select(ParseSequenceFromParkingPointId)
            .DefaultIfEmpty(0)
            .Max();
        return (TwistingParkingRegistry.DockingPointsPerSide - maxSeq) / stopsPerMission;
    }

    public static bool IsMachineAdmissionGranted(
        int machineId,
        IReadOnlyList<TwistingLoadMachineEvaluation> allMachines,
        IEnumerable<TwistingLoadInFlight> flights,
        IEnumerable<FleetStatusDto>? fleetStatuses = null,
        int requiredStatus = TwistingParkingRegistry.CallVehicleStatus,
        int stopsPerMission = TwistingParkingRegistry.StopsPerLoadMission)
    {
        foreach (var eval in allMachines.Where(m =>
                     m.MachineId < machineId &&
                     m.Status == requiredStatus &&
                     m.Side is not ('X' or 'x')))
        {
            if (!IsSideTailLaneCleared(eval, flights, fleetStatuses, stopsPerMission))
                return false;
        }

        return true;
    }

    public static string? TryGetMachineBlockReason(
        int machineId,
        IReadOnlyList<TwistingLoadMachineEvaluation> allMachines,
        IEnumerable<TwistingLoadInFlight> flights,
        IEnumerable<FleetStatusDto>? fleetStatuses = null,
        int requiredStatus = TwistingParkingRegistry.CallVehicleStatus,
        int stopsPerMission = TwistingParkingRegistry.StopsPerLoadMission)
    {
        foreach (var eval in allMachines
                     .Where(m => m.MachineId < machineId && m.Status == requiredStatus && m.Side is not ('X' or 'x'))
                     .OrderBy(m => m.MachineId)
                     .ThenBy(m => m.Side))
        {
            if (IsSideTailLaneCleared(eval, flights, fleetStatuses, stopsPerMission))
                continue;

            return TryGetPriorLaneBlockReason(
                flights,
                eval.TwpGroupId,
                0,
                eval.AvailableMissions,
                eval.DockingPoints,
                eval.MachineCode,
                fleetStatuses: fleetStatuses,
                stopsPerMission: stopsPerMission);
        }

        return null;
    }

    public static bool IsLaneAdmissionGranted(
        IEnumerable<TwistingLoadInFlight> flights,
        int twpGroupId,
        int blockIndex,
        IReadOnlyList<IReadOnlyList<TwistingDockingPointEvaluation>> availableMissions,
        IReadOnlyList<TwistingDockingPointEvaluation>? dockingPoints = null,
        IEnumerable<FleetStatusDto>? fleetStatuses = null,
        int stopsPerMission = TwistingParkingRegistry.StopsPerLoadMission)
    {
        if (blockIndex <= 0) return true;

        var activeOnLane = flights
            .Where(f => f.IsActive && IsFlightOnLane(f, twpGroupId))
            .ToList();

        if (activeOnLane.Any(f =>
                f.LaneBlockIndex < blockIndex && !HasFlightEnteredSpecificLane(f, twpGroupId, fleetStatuses)))
            return false;

        for (var i = 0; i < blockIndex; i++)
        {
            if (!IsPriorLaneBlockSatisfied(
                    flights, twpGroupId, i, availableMissions, dockingPoints, fleetStatuses, stopsPerMission))
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
        IEnumerable<FleetStatusDto>? fleetStatuses = null,
        int stopsPerMission = TwistingParkingRegistry.StopsPerLoadMission)
    {
        if (blockIndex <= 0) return null;

        var laneLabel = FormatTwpLane(twpGroupId);
        var activeOnLane = flights
            .Where(f => f.IsActive && IsFlightOnLane(f, twpGroupId))
            .ToList();

        var waitingEntry = activeOnLane
            .Where(f => f.LaneBlockIndex < blockIndex && !HasFlightEnteredSpecificLane(f, twpGroupId, fleetStatuses))
            .OrderBy(f => f.LaneBlockIndex)
            .FirstOrDefault();
        if (waitingEntry is not null)
            return FormatWaitingForLaneEntry(waitingEntry, laneLabel, fleetStatuses);

        for (var i = 0; i < blockIndex; i++)
        {
            if (IsPriorLaneBlockSatisfied(
                    flights, twpGroupId, i, availableMissions, dockingPoints, fleetStatuses, stopsPerMission))
                continue;

            return TryGetPriorLaneBlockReason(
                flights, twpGroupId, i, availableMissions, dockingPoints,
                laneLabel: laneLabel, fleetStatuses: fleetStatuses, stopsPerMission: stopsPerMission);
        }

        return null;
    }

    /// <summary>
    /// 車輛 current_site 已在該任務任一相關 TWP 走道視為已進入走道。
    /// </summary>
    public static bool HasFlightEnteredLane(
        TwistingLoadInFlight flight,
        IEnumerable<FleetStatusDto>? fleetStatuses)
    {
        var vehicle = ResolveFleetVehicle(fleetStatuses, flight.AmrCode);
        if (IsVehicleOnTwpLane(vehicle, flight.TwpGroupId))
            return true;
        return flight.SecondaryTwpGroupId is int secondary
               && IsVehicleOnTwpLane(vehicle, secondary);
    }

    public static bool HasFlightEnteredSpecificLane(
        TwistingLoadInFlight flight,
        int twpGroupId,
        IEnumerable<FleetStatusDto>? fleetStatuses) =>
        IsVehicleOnTwpLane(ResolveFleetVehicle(fleetStatuses, flight.AmrCode), twpGroupId);

    public static bool IsFlightOnLane(TwistingLoadInFlight flight, int twpGroupId) =>
        flight.TwpGroupId == twpGroupId || flight.SecondaryTwpGroupId == twpGroupId;

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
    /// 前序走道組已滿足：執行中車輛已開進走道、該組已無待派停車點、或區塊料況已處理完。
    /// </summary>
    public static bool IsPriorLaneBlockSatisfied(
        IEnumerable<TwistingLoadInFlight> flights,
        int twpGroupId,
        int priorBlockIndex,
        IReadOnlyList<IReadOnlyList<TwistingDockingPointEvaluation>> availableMissions,
        IReadOnlyList<TwistingDockingPointEvaluation>? dockingPoints = null,
        IEnumerable<FleetStatusDto>? fleetStatuses = null,
        int stopsPerMission = TwistingParkingRegistry.StopsPerLoadMission)
    {
        var activePrior = flights.FirstOrDefault(f =>
            f.IsActive && IsFlightOnLane(f, twpGroupId) && f.LaneBlockIndex == priorBlockIndex);
        if (activePrior is not null)
            return HasFlightEnteredSpecificLane(activePrior, twpGroupId, fleetStatuses);

        if (!HasDispatchableMissionAtBlock(priorBlockIndex, availableMissions, stopsPerMission))
            return true;

        if (IsLaneBlockCleared(priorBlockIndex, dockingPoints, stopsPerMission))
            return true;

        return false;
    }

    static bool IsSideTailLaneCleared(
        TwistingLoadMachineEvaluation eval,
        IEnumerable<TwistingLoadInFlight> flights,
        IEnumerable<FleetStatusDto>? fleetStatuses,
        int stopsPerMission) =>
        IsPriorLaneBlockSatisfied(
            flights,
            eval.TwpGroupId,
            0,
            eval.AvailableMissions,
            eval.DockingPoints,
            fleetStatuses,
            stopsPerMission);

    static string? TryGetPriorLaneBlockReason(
        IEnumerable<TwistingLoadInFlight> flights,
        int twpGroupId,
        int priorBlockIndex,
        IReadOnlyList<IReadOnlyList<TwistingDockingPointEvaluation>> availableMissions,
        IReadOnlyList<TwistingDockingPointEvaluation>? dockingPoints,
        string? machineCode = null,
        string? laneLabel = null,
        IEnumerable<FleetStatusDto>? fleetStatuses = null,
        int stopsPerMission = TwistingParkingRegistry.StopsPerLoadMission)
    {
        laneLabel ??= FormatTwpLane(twpGroupId);
        var prefix = machineCode is not null ? $"{machineCode} " : "";

        var activePrior = flights.FirstOrDefault(f =>
            f.IsActive && IsFlightOnLane(f, twpGroupId) && f.LaneBlockIndex == priorBlockIndex);
        if (activePrior is not null && !HasFlightEnteredSpecificLane(activePrior, twpGroupId, fleetStatuses))
            return FormatWaitingForLaneEntry(activePrior, $"{prefix}{laneLabel}".Trim(), fleetStatuses);

        var waitingMission = availableMissions.FirstOrDefault(m =>
            GetLaneBlockIndex(m, stopsPerMission) == priorBlockIndex);
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
        IReadOnlyList<IReadOnlyList<TwistingDockingPointEvaluation>> availableMissions,
        int stopsPerMission) =>
        availableMissions.Any(m =>
            GetLaneBlockIndex(m, stopsPerMission) == blockIndex &&
            m.Any(p => p.IsDispatchable));

    /// <summary>該區塊停車點皆已不可派（料況已處理），視為前序完成。</summary>
    static bool IsLaneBlockCleared(
        int blockIndex,
        IReadOnlyList<TwistingDockingPointEvaluation>? dockingPoints,
        int stopsPerMission)
    {
        if (dockingPoints is null || dockingPoints.Count == 0)
            return false;

        var tailSeq = TwistingParkingRegistry.DockingPointsPerSide - blockIndex * stopsPerMission;
        var headSeq = tailSeq - stopsPerMission + 1;
        var bySeq = dockingPoints.ToDictionary(p => p.Sequence);

        for (var seq = headSeq; seq <= tailSeq; seq++)
        {
            if (!bySeq.TryGetValue(seq, out var point) || point.IsDispatchable)
                return false;
        }

        return true;
    }

    static int ParseSequenceFromParkingPointId(string parkingPointId)
    {
        var dash = parkingPointId.LastIndexOf('-');
        if (dash < 0 || dash >= parkingPointId.Length - 1) return 0;
        return int.TryParse(parkingPointId[(dash + 1)..], out var seq) ? seq : 0;
    }
}
