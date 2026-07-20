using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// AOI 包裝站可派：停車點空 + 未鎖（忽略 Modbus）。
/// PKP001 / PKP002 可同時派，無單行道互斥。
/// </summary>
public static class AoiLoadDispatchEvaluator
{
    public static IReadOnlyList<AoiLoadDispatchEvaluation> EvaluateAll(
        IEnumerable<FleetStatusDto>? fleetStatuses,
        IEnumerable<AoiLoadDispatchInFlight>? inFlight = null)
    {
        var occupancy = BuildOccupancyMap(fleetStatuses);
        var lockedIds = new HashSet<string>(
            (inFlight ?? []).Where(f => f.IsActive).Select(f => f.ParkingPointId),
            StringComparer.OrdinalIgnoreCase);

        return PackagingParkingRegistry.GetAll()
            .Select(meta => EvaluateStation(meta, occupancy, lockedIds))
            .ToList();
    }

    public static AoiLoadDispatchEvaluation EvaluateStation(
        PackagingStationMeta meta,
        IReadOnlyDictionary<string, string> occupancyByParkingId,
        IReadOnlySet<string> lockedParkingIds)
    {
        occupancyByParkingId.TryGetValue(meta.ParkingPointId, out var occupying);
        var isEmpty = string.IsNullOrWhiteSpace(occupying);
        var isLocked = lockedParkingIds.Contains(meta.ParkingPointId);

        if (isLocked)
        {
            return new AoiLoadDispatchEvaluation(
                meta.StationId, meta.StationCode, meta.ParkingPointId,
                isEmpty, true, false,
                $"任務中鎖定（{meta.ParkingPointId}）", occupying);
        }

        if (!isEmpty)
        {
            return new AoiLoadDispatchEvaluation(
                meta.StationId, meta.StationCode, meta.ParkingPointId,
                false, false, false,
                $"停車點已有 {occupying} 停靠", occupying);
        }

        return new AoiLoadDispatchEvaluation(
            meta.StationId, meta.StationCode, meta.ParkingPointId,
            true, false, true,
            "停車點空，可派車", null);
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

    static Dictionary<string, string> BuildOccupancyMap(IEnumerable<FleetStatusDto>? fleetStatuses)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (fleetStatuses is null) return map;

        foreach (var vehicle in fleetStatuses)
        {
            foreach (var meta in PackagingParkingRegistry.GetAll())
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
