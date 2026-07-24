using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// 充電站可派：未鎖，且站上空 或 停靠車已 Idle（充飽可趕走）。
/// 停靠車仍在 charging／移動中則不可派。
/// </summary>
public static class ChargeDispatchEvaluator
{
    public static IReadOnlyList<ChargeStationEvaluation> EvaluateAll(
        IEnumerable<WmsCellDto>? chargeCells,
        IEnumerable<FleetStatusDto>? fleetStatuses,
        IEnumerable<RobotStatusDto>? robots = null,
        IEnumerable<ChargeDispatchInFlight>? inFlight = null)
    {
        var cells = (chargeCells ?? [])
            .Where(c => !string.IsNullOrWhiteSpace(c.CellId))
            .OrderBy(c => c.CellId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var occupancy = BuildOccupancyMap(fleetStatuses, cells.Select(c => c.CellId));
        var robotMap = BuildRobotMap(robots);
        var lockedIds = new HashSet<string>(
            (inFlight ?? []).Where(f => f.IsActive).Select(f => f.ParkingPointId),
            StringComparer.OrdinalIgnoreCase);

        return cells.Select(cell => EvaluateStation(cell, occupancy, robotMap, lockedIds)).ToList();
    }

    public static ChargeStationEvaluation EvaluateStation(
        WmsCellDto cell,
        IReadOnlyDictionary<string, string> occupancyByCellId,
        IReadOnlyDictionary<string, RobotStatusDto> robotMap,
        IReadOnlySet<string> lockedCellIds)
    {
        occupancyByCellId.TryGetValue(cell.CellId, out var occupying);
        var isEmpty = string.IsNullOrWhiteSpace(occupying);
        var isLocked = lockedCellIds.Contains(cell.CellId);
        var name = string.IsNullOrWhiteSpace(cell.CellName) ? cell.CellId : cell.CellName;

        if (isLocked)
        {
            return new ChargeStationEvaluation(
                cell.CellId, name, isEmpty, true, false,
                $"任務中鎖定（{cell.CellId}）", occupying);
        }

        if (isEmpty)
        {
            return new ChargeStationEvaluation(
                cell.CellId, name, true, false, true,
                "充電站空，可派車", null);
        }

        var occupant = ResolveRobot(robotMap, occupying!);
        var state = occupant?.State ?? "—";
        var isIdle = string.Equals(state, "idle", StringComparison.OrdinalIgnoreCase);

        if (isIdle)
        {
            return new ChargeStationEvaluation(
                cell.CellId, name, false, false, true,
                $"有 {occupying} Idle 停靠（已可趕走），可派車", occupying);
        }

        return new ChargeStationEvaluation(
            cell.CellId, name, false, false, false,
            $"停車點已有 {occupying} 停靠（狀態={state}，非 Idle）", occupying);
    }

    static Dictionary<string, string> BuildOccupancyMap(
        IEnumerable<FleetStatusDto>? fleetStatuses,
        IEnumerable<string> cellIds)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (fleetStatuses is null) return map;

        var cells = cellIds.ToList();
        foreach (var vehicle in fleetStatuses)
        {
            if (!FleetParkingPlanner.IsMeaningfulSite(vehicle.CurrentSite))
                continue;

            var matchedCell = cells.FirstOrDefault(c => ChargeSitesMatch(c, vehicle.CurrentSite!));
            if (matchedCell is null)
                continue;

            var robot = !string.IsNullOrWhiteSpace(vehicle.RobotId)
                ? vehicle.RobotId
                : vehicle.RobotName;
            if (!string.IsNullOrWhiteSpace(robot))
                map[matchedCell] = robot!;
        }

        return map;
    }

    /// <summary>對齊 TGI：CHG001 ↔ C01。</summary>
    public static bool ChargeSitesMatch(string cellId, string siteCode)
    {
        if (cellId.Equals(siteCode, StringComparison.OrdinalIgnoreCase))
            return true;

        var a = NormalizeChargeCode(cellId);
        var b = NormalizeChargeCode(siteCode);
        return a is not null && b is not null && a == b;
    }

    static string? NormalizeChargeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var s = code.Trim();
        if (s.StartsWith("CHG", StringComparison.OrdinalIgnoreCase))
        {
            var digits = new string(s.SkipWhile(c => !char.IsDigit(c)).ToArray()).TrimStart('0');
            if (string.IsNullOrEmpty(digits)) digits = "1";
            return digits;
        }

        if (s.Length >= 2 && (s[0] is 'C' or 'c') && char.IsDigit(s[1]))
        {
            var digits = new string(s.Skip(1).TakeWhile(char.IsDigit).ToArray()).TrimStart('0');
            if (string.IsNullOrEmpty(digits)) digits = "1";
            return digits;
        }

        return null;
    }

    static Dictionary<string, RobotStatusDto> BuildRobotMap(IEnumerable<RobotStatusDto>? robots)
    {
        var map = new Dictionary<string, RobotStatusDto>(StringComparer.OrdinalIgnoreCase);
        if (robots is null) return map;

        foreach (var robot in robots)
        {
            foreach (var alias in CakeVehicleDispatchEvaluator.ExpandRobotAliases(robot.RobotId))
                map[alias] = robot;
            foreach (var alias in CakeVehicleDispatchEvaluator.ExpandRobotAliases(robot.RobotName))
                map[alias] = robot;
        }

        return map;
    }

    static RobotStatusDto? ResolveRobot(IReadOnlyDictionary<string, RobotStatusDto> map, string amrCode)
    {
        foreach (var alias in CakeVehicleDispatchEvaluator.ExpandRobotAliases(amrCode))
        {
            if (map.TryGetValue(alias, out var robot))
                return robot;
        }
        return null;
    }
}
