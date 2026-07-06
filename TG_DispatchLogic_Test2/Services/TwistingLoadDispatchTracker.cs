using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

public static class TwistingLoadDispatchTracker
{
    public const int MaxInFlightMinutes = 120;

    public static bool IsMissionLocked(IEnumerable<TwistingLoadInFlight> flights, int machineId, char side) =>
        CountActiveMissionsOnSide(flights, machineId, side) >= TwistingParkingRegistry.MaxConcurrentMissionsPerSide;

    public static int CountActiveMissionsOnSide(IEnumerable<TwistingLoadInFlight> flights, int machineId, char side) =>
        flights.Count(f =>
            f.IsActive &&
            f.MachineId == machineId &&
            char.ToUpperInvariant(f.Side) == char.ToUpperInvariant(side));

    public static bool IsParkingPointLocked(IEnumerable<TwistingLoadInFlight> flights, string parkingPointId) =>
        flights.Any(f =>
            f.IsActive &&
            f.ParkingPointIds.Any(id => id.Equals(parkingPointId, StringComparison.OrdinalIgnoreCase)));

    public static bool CanDispatchMissionBlock(
        IEnumerable<TwistingLoadInFlight> flights,
        int machineId,
        char side,
        int twpGroupId,
        int laneBlockIndex,
        IEnumerable<string> parkingPointIds,
        IReadOnlyList<IReadOnlyList<TwistingDockingPointEvaluation>>? availableMissions = null,
        IReadOnlyList<TwistingLoadMachineEvaluation>? allMachines = null,
        IReadOnlyList<TwistingDockingPointEvaluation>? dockingPoints = null,
        IEnumerable<FleetStatusDto>? fleetStatuses = null)
    {
        if (CountActiveMissionsOnSide(flights, machineId, side) >= TwistingParkingRegistry.MaxConcurrentMissionsPerSide)
            return false;

        if (allMachines is not null &&
            !TwistingLoadLaneAdmission.IsMachineAdmissionGranted(machineId, allMachines, flights, fleetStatuses))
            return false;

        if (!TwistingLoadLaneAdmission.IsLaneAdmissionGranted(
                flights, twpGroupId, laneBlockIndex, availableMissions ?? [], dockingPoints, fleetStatuses))
            return false;

        return !parkingPointIds.Any(id => IsParkingPointLocked(flights, id));
    }

    public static string? TryGetDispatchBlockReason(
        IEnumerable<TwistingLoadInFlight> flights,
        int machineId,
        char side,
        int twpGroupId,
        int laneBlockIndex,
        IEnumerable<string> parkingPointIds,
        IReadOnlyList<IReadOnlyList<TwistingDockingPointEvaluation>>? availableMissions = null,
        IReadOnlyList<TwistingLoadMachineEvaluation>? allMachines = null,
        IReadOnlyList<TwistingDockingPointEvaluation>? dockingPoints = null,
        IEnumerable<FleetStatusDto>? fleetStatuses = null)
    {
        if (CountActiveMissionsOnSide(flights, machineId, side) >= TwistingParkingRegistry.MaxConcurrentMissionsPerSide)
            return $"同側已達最多 {TwistingParkingRegistry.MaxConcurrentMissionsPerSide} 組任務";

        if (allMachines is not null)
        {
            var machineReason = TwistingLoadLaneAdmission.TryGetMachineBlockReason(
                machineId, allMachines, flights, fleetStatuses);
            if (machineReason is not null)
                return machineReason;
        }

        var missions = availableMissions ?? [];
        var laneReason = TwistingLoadLaneAdmission.TryGetLaneBlockReason(
            flights, twpGroupId, laneBlockIndex, missions, dockingPoints, fleetStatuses);
        if (laneReason is not null)
            return laneReason;

        var lockedPoint = parkingPointIds.FirstOrDefault(id => IsParkingPointLocked(flights, id));
        if (lockedPoint is not null)
            return $"{lockedPoint} 已被其他任務鎖定";

        return null;
    }

    public static TwistingLoadInFlight Register(
        TwistingLoadDispatchPair pair,
        string sequenceId,
        IReadOnlyList<string> taskIds)
    {
        var laneBlockIndex = TwistingLoadLaneAdmission.GetLaneBlockIndex(pair.MissionStops);
        return new TwistingLoadInFlight(
            sequenceId,
            pair.Machine.MachineId,
            pair.Machine.Side,
            pair.Machine.MachineCode,
            pair.Vehicle.AmrCode,
            pair.Machine.TwpGroupId,
            laneBlockIndex,
            pair.MissionStops.Select(s => s.ParkingPointId).ToList(),
            pair.Stops.ToList(),
            taskIds.ToList(),
            DateTime.Now,
            LastFlowDispatchedAt: taskIds.Count > 0 ? DateTime.Now : null,
            NextRetryAt: null,
            CompletedStops: taskIds.Count,
            TotalFlows: pair.Stops.Count,
            SawRobotBusy: false,
            HasEnteredLane: false,
            IsCompleted: false,
            StatusHint: taskIds.Count == 0
                ? $"派車中 0/{pair.Stops.Count} flow"
                : $"已派車 {taskIds.Count}/{pair.Stops.Count} flow，等待作業完成");
    }

    public static TwistingLoadInFlight UpdateProgress(
        TwistingLoadInFlight flight,
        IReadOnlyList<string> taskIds,
        string statusHint,
        DateTime? lastFlowDispatchedAt = null,
        DateTime? nextRetryAt = null,
        bool? hasEnteredLane = null,
        bool? sawRobotBusy = null) =>
        flight with
        {
            TaskIds = taskIds.ToList(),
            CompletedStops = taskIds.Count,
            StatusHint = statusHint,
            LastFlowDispatchedAt = lastFlowDispatchedAt ?? flight.LastFlowDispatchedAt,
            NextRetryAt = nextRetryAt,
            HasEnteredLane = hasEnteredLane ?? flight.HasEnteredLane,
            SawRobotBusy = sawRobotBusy ?? flight.SawRobotBusy
        };

    public static List<TwistingLoadInFlight> Refresh(
        IEnumerable<TwistingLoadInFlight> flights,
        IReadOnlyList<RobotStatusDto> robots,
        IReadOnlyList<TwistingLoadMachineEvaluation>? machineEvaluations,
        IEnumerable<FleetStatusDto>? fleetStatuses = null)
    {
        var robotMap = BuildRobotMap(robots);
        var evalMap = machineEvaluations?
            .ToDictionary(m => m.MissionKey, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, TwistingLoadMachineEvaluation>(StringComparer.OrdinalIgnoreCase);

        return flights.Select(f => RefreshOne(f, robotMap, evalMap, fleetStatuses)).ToList();
    }

    public static List<TwistingLoadInFlight> PruneCompleted(IEnumerable<TwistingLoadInFlight> flights) =>
        flights.Where(f => f.IsActive).ToList();

    static TwistingLoadInFlight RefreshOne(
        TwistingLoadInFlight flight,
        Dictionary<string, RobotStatusDto> robotMap,
        Dictionary<string, TwistingLoadMachineEvaluation> evalMap,
        IEnumerable<FleetStatusDto>? fleetStatuses)
    {
        if (flight.IsCompleted) return flight;

        if (DateTime.Now - flight.DispatchedAt > TimeSpan.FromMinutes(MaxInFlightMinutes))
        {
            return flight with
            {
                IsCompleted = true,
                StatusHint = $"逾時解除鎖定（>{MaxInFlightMinutes} 分鐘）"
            };
        }

        evalMap.TryGetValue(flight.MissionKey, out var eval);
        var robot = ResolveRobot(robotMap, flight.AmrCode);
        var sawBusy = flight.SawRobotBusy;
        var hasEnteredLane = TwistingLoadLaneAdmission.HasFlightEnteredLane(flight, fleetStatuses);
        var laneLabel = TwistingLoadLaneAdmission.FormatTwpLane(flight.TwpGroupId);
        var fleet = TwistingLoadLaneAdmission.ResolveFleetVehicle(fleetStatuses, flight.AmrCode);
        var currentSite = fleet?.CurrentSite ?? "—";

        if (robot is not null && IsRobotBusy(robot))
            sawBusy = true;

        if (!flight.IsFullyDispatched)
        {
            var hint = flight.NextRetryAt is not null && DateTime.Now < flight.NextRetryAt.Value
                ? $"{flight.StatusHint}"
                : flight.CompletedStops == 0
                    ? hasEnteredLane
                        ? $"派車中 0/{flight.TotalFlows} flow · 已進入 {laneLabel} 走道"
                        : $"派車中 0/{flight.TotalFlows} flow · 等待 {flight.AmrCode} 開進 {laneLabel}（目前 {currentSite}）"
                    : hasEnteredLane
                        ? $"派車中 {flight.CompletedStops}/{flight.TotalFlows} flow · 已進入 {laneLabel} 走道"
                        : $"派車中 {flight.CompletedStops}/{flight.TotalFlows} flow · 等待 {flight.AmrCode} 開進 {laneLabel}（目前 {currentSite}）";

            return flight with
            {
                SawRobotBusy = sawBusy,
                HasEnteredLane = hasEnteredLane,
                StatusHint = hint
            };
        }

        var loadedCount = CountLoadedMissionPoints(flight, eval);

        if (robot is null)
        {
            return flight with
            {
                SawRobotBusy = sawBusy,
                HasEnteredLane = hasEnteredLane,
                StatusHint = sawBusy
                    ? $"作業中 · 停車點已上料 {loadedCount}/{flight.ParkingPointIds.Count} · 車輛暫時離線"
                    : $"已派車，等待 TGI 回報車輛狀態 · 停車點已上料 {loadedCount}/{flight.ParkingPointIds.Count}"
            };
        }

        if (!sawBusy)
        {
            return flight with
            {
                HasEnteredLane = hasEnteredLane,
                StatusHint = $"已派車（{robot.State}），等待車輛開始作業 · 停車點已上料 {loadedCount}/{flight.ParkingPointIds.Count}"
            };
        }

        if (IsRobotBusy(robot))
        {
            return flight with
            {
                SawRobotBusy = true,
                HasEnteredLane = hasEnteredLane,
                StatusHint = $"作業中 · {robot.State} · 停車點已上料 {loadedCount}/{flight.ParkingPointIds.Count}"
            };
        }

        if (AreMissionPointsLoaded(flight, eval))
        {
            return Complete(flight, true, hasEnteredLane,
                $"任務完成 · {flight.ParkingPointIds.Count} 停車點 Cake 已上料 · 車輛 {robot.State}");
        }

        return flight with
        {
            SawRobotBusy = sawBusy,
            HasEnteredLane = hasEnteredLane,
            StatusHint = $"車輛已回 idle，等待停車點上料完成（{loadedCount}/{flight.ParkingPointIds.Count}）"
        };
    }

    static int CountLoadedMissionPoints(TwistingLoadInFlight flight, TwistingLoadMachineEvaluation? eval)
    {
        if (eval is null) return 0;
        var map = eval.DockingPoints.ToDictionary(p => p.ParkingPointId, StringComparer.OrdinalIgnoreCase);
        return flight.ParkingPointIds.Count(id =>
            map.TryGetValue(id, out var pt) && IsPointFullyLoaded(pt));
    }

    static bool AreMissionPointsLoaded(TwistingLoadInFlight flight, TwistingLoadMachineEvaluation? eval)
    {
        if (eval is null || flight.ParkingPointIds.Count == 0) return false;
        var map = eval.DockingPoints.ToDictionary(p => p.ParkingPointId, StringComparer.OrdinalIgnoreCase);
        return flight.ParkingPointIds.All(id =>
            map.TryGetValue(id, out var pt) && IsPointFullyLoaded(pt));
    }

    static bool IsPointFullyLoaded(TwistingDockingPointEvaluation point) =>
        point.CakePorts.Count > 0 && point.CakePorts.All(p => !p.NeedsCakeLoad);

    static bool IsRobotBusy(RobotStatusDto robot) =>
        !string.Equals(robot.State, "idle", StringComparison.OrdinalIgnoreCase);

    static TwistingLoadInFlight Complete(
        TwistingLoadInFlight flight,
        bool sawBusy,
        bool hasEnteredLane,
        string hint) =>
        flight with
        {
            SawRobotBusy = sawBusy,
            HasEnteredLane = hasEnteredLane,
            IsCompleted = true,
            StatusHint = hint
        };

    static Dictionary<string, RobotStatusDto> BuildRobotMap(IReadOnlyList<RobotStatusDto> robots)
    {
        var map = new Dictionary<string, RobotStatusDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var robot in robots)
        {
            foreach (var alias in CakeVehicleDispatchEvaluator.ExpandRobotAliases(robot.RobotId))
                map[alias] = robot;
            foreach (var alias in CakeVehicleDispatchEvaluator.ExpandRobotAliases(robot.RobotName))
                map[alias] = robot;
        }
        return map;
    }

    static RobotStatusDto? ResolveRobot(Dictionary<string, RobotStatusDto> map, string amrCode)
    {
        foreach (var alias in CakeVehicleDispatchEvaluator.ExpandRobotAliases(amrCode))
        {
            if (map.TryGetValue(alias, out var robot))
                return robot;
        }
        return null;
    }
}
