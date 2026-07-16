using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// 清軸站可派：停車點空 + 單行道可達 + 未鎖（忽略 Modbus present/absent）。
/// CLR001 最內側 … CLR005 最外側；站 k 可達 iff 無車停在任何外側站 j（j &gt; k）。
/// </summary>
public static class ClearingLoadDispatchEvaluator
{
    public static IReadOnlyList<ClearingLoadDispatchEvaluation> EvaluateAll(
        IEnumerable<FleetStatusDto>? fleetStatuses,
        IEnumerable<ClearingLoadDispatchInFlight>? inFlight = null)
    {
        var occupancy = BuildOccupancyMap(fleetStatuses);
        var lockedIds = new HashSet<string>(
            (inFlight ?? []).Where(f => f.IsActive).Select(f => f.ParkingPointId),
            StringComparer.OrdinalIgnoreCase);

        return ClearingParkingRegistry.GetAll()
            .Select(meta => EvaluateStation(meta, occupancy, lockedIds))
            .ToList();
    }

    public static ClearingLoadDispatchEvaluation EvaluateStation(
        ClearingStationMeta meta,
        IReadOnlyDictionary<string, string> occupancyByParkingId,
        IReadOnlySet<string> lockedParkingIds)
    {
        occupancyByParkingId.TryGetValue(meta.ParkingPointId, out var occupying);
        var isEmpty = string.IsNullOrWhiteSpace(occupying);
        var isReachable = IsReachable(meta.StationId, occupancyByParkingId);
        var isLocked = lockedParkingIds.Contains(meta.ParkingPointId);

        if (isLocked)
        {
            return new ClearingLoadDispatchEvaluation(
                meta.StationId, meta.StationCode, meta.ParkingPointId,
                isEmpty, isReachable, true, false,
                $"任務中鎖定（{meta.ParkingPointId}）", occupying);
        }

        if (!isEmpty)
        {
            return new ClearingLoadDispatchEvaluation(
                meta.StationId, meta.StationCode, meta.ParkingPointId,
                false, isReachable, false, false,
                $"停車點已有 {occupying} 停靠", occupying);
        }

        if (!isReachable)
        {
            var blocker = FindOuterOccupant(meta.StationId, occupancyByParkingId);
            return new ClearingLoadDispatchEvaluation(
                meta.StationId, meta.StationCode, meta.ParkingPointId,
                true, false, false, false,
                blocker is null
                    ? "單行道外側有車，無法進入"
                    : $"單行道外側 {blocker} 有車，無法進入",
                null);
        }

        return new ClearingLoadDispatchEvaluation(
            meta.StationId, meta.StationCode, meta.ParkingPointId,
            true, true, false, true,
            "停車點空且單行道可達，可派車", null);
    }

    public static bool IsParkingPointOccupied(
        IEnumerable<FleetStatusDto>? fleetStatuses,
        string parkingPointId,
        out string? parkedRobotId)
    {
        parkedRobotId = null;
        if (fleetStatuses is null) return false;

        foreach (var vehicle in fleetStatuses)
        {
            if (!IsVehicleParkedAt(vehicle, parkingPointId))
                continue;

            parkedRobotId = !string.IsNullOrWhiteSpace(vehicle.RobotId)
                ? vehicle.RobotId
                : vehicle.RobotName;
            return !string.IsNullOrWhiteSpace(parkedRobotId);
        }

        return false;
    }

    static bool IsReachable(int stationId, IReadOnlyDictionary<string, string> occupancy)
    {
        for (var j = stationId + 1; j <= ClearingParkingRegistry.StationCount; j++)
        {
            var parkingId = ClearingParkingRegistry.GetParkingPointId(j);
            if (occupancy.ContainsKey(parkingId))
                return false;
        }
        return true;
    }

    static string? FindOuterOccupant(int stationId, IReadOnlyDictionary<string, string> occupancy)
    {
        for (var j = stationId + 1; j <= ClearingParkingRegistry.StationCount; j++)
        {
            var parkingId = ClearingParkingRegistry.GetParkingPointId(j);
            if (occupancy.TryGetValue(parkingId, out var robot))
                return $"{parkingId}（{robot}）";
        }
        return null;
    }

    static Dictionary<string, string> BuildOccupancyMap(IEnumerable<FleetStatusDto>? fleetStatuses)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (fleetStatuses is null) return map;

        foreach (var vehicle in fleetStatuses)
        {
            foreach (var meta in ClearingParkingRegistry.GetAll())
            {
                if (!IsVehicleParkedAt(vehicle, meta.ParkingPointId))
                    continue;

                var robot = !string.IsNullOrWhiteSpace(vehicle.RobotId)
                    ? vehicle.RobotId
                    : vehicle.RobotName;
                if (!string.IsNullOrWhiteSpace(robot))
                    map[meta.ParkingPointId] = robot!;
            }
        }

        return map;
    }

    static bool IsVehicleParkedAt(FleetStatusDto vehicle, string parkingPointId)
    {
        if (!FleetParkingPlanner.IsMeaningfulSite(vehicle.CurrentSite))
            return false;

        return vehicle.CurrentSite!.Equals(parkingPointId, StringComparison.OrdinalIgnoreCase);
    }
}
