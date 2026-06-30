using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

public static class FleetParkingPlanner
{
    public const int DefaultBatchSize = 1;

    public static bool IsCake(string robotId) =>
        robotId.StartsWith("CAKE-", StringComparison.OrdinalIgnoreCase);

    public static bool IsBobbin(string robotId) =>
        robotId.StartsWith("BOBBIN-", StringComparison.OrdinalIgnoreCase);

    public static bool IsFleetVehicle(string robotId) => IsCake(robotId) || IsBobbin(robotId);

    /// <summary>
    /// 找出不在等待點上的車，每批最多 batchSize 台，各自派往最近且未被占用的 WP。
    /// </summary>
    public static IReadOnlyList<FleetParkingAssignment> PlanNearestBatch(
        IEnumerable<FleetStatusDto> fleet,
        IEnumerable<WmsCellDto> waitPoints,
        int batchSize = DefaultBatchSize,
        IReadOnlySet<string>? additionallyReserved = null)
    {
        var wpList = waitPoints
            .Where(w => !string.IsNullOrWhiteSpace(w.CellId))
            .ToList();

        if (wpList.Count == 0 || batchSize <= 0)
            return [];

        var occupied = GetOccupiedWaitPoints(fleet, wpList, additionallyReserved);
        var reserved = new HashSet<string>(occupied, StringComparer.OrdinalIgnoreCase);

        var offWpVehicles = fleet
            .Where(v => IsFleetVehicle(v.RobotId))
            .Where(v => !IsParkedAtAnyWaitPoint(v, wpList))
            .Where(v => !IsEnRoute(v))
            .OrderBy(v => SiteCodeSortKey(v.RobotId))
            .ThenBy(v => v.RobotId, StringComparer.OrdinalIgnoreCase)
            .Take(batchSize)
            .ToList();

        var assignments = new List<FleetParkingAssignment>();

        foreach (var vehicle in offWpVehicles)
        {
            var wp = FindNearestFreeWaitPoint(vehicle, wpList, reserved);
            if (wp is null)
                break;

            reserved.Add(wp.CellId);
            var amrType = IsCake(vehicle.RobotId) ? "Cake" : "Bobbin";
            assignments.Add(new FleetParkingAssignment(vehicle.RobotId, wp.CellId, amrType));
        }

        return assignments;
    }

    public static bool IsOnWaitPoint(FleetStatusDto vehicle, IReadOnlySet<string> waitPointCodes) =>
        IsMeaningfulSite(vehicle.CurrentSite)
        && waitPointCodes.Contains(vehicle.CurrentSite!);

    /// <summary>車輛是否已停在任一 WP 上（站點或座標，且非移動中）。</summary>
    public static bool IsParkedAtAnyWaitPoint(FleetStatusDto vehicle, IReadOnlyList<WmsCellDto> waitPoints)
    {
        if (IsEnRoute(vehicle)) return false;
        return waitPoints.Any(wp => IsVehicleOccupyingWaitPoint(vehicle, wp));
    }

    public static int CountEligibleVehicles(
        IEnumerable<FleetStatusDto> fleet,
        IReadOnlyList<WmsCellDto> waitPoints) =>
        fleet.Count(v => IsFleetVehicle(v.RobotId)
            && !IsParkedAtAnyWaitPoint(v, waitPoints)
            && !IsEnRoute(v));

    public static int CountFreeWaitPoints(
        IEnumerable<FleetStatusDto> fleet,
        IReadOnlyList<WmsCellDto> waitPoints,
        IReadOnlySet<string>? additionallyReserved = null)
    {
        var occupied = GetOccupiedWaitPoints(fleet, waitPoints, additionallyReserved);
        return waitPoints.Count(wp => !occupied.Contains(wp.CellId));
    }

    public static List<WmsCellDto> GetWaitPointList(IEnumerable<WmsCellDto> allSites) =>
        allSites
            .Where(s => !string.IsNullOrWhiteSpace(s.CellId))
            .Where(s => s.CellType.Equals("Waiting", StringComparison.OrdinalIgnoreCase)
                || s.CellId.StartsWith("WP", StringComparison.OrdinalIgnoreCase))
            .ToList();

    public static HashSet<string> GetReservedWaitPointTargetsFromTasks(
        IEnumerable<ActiveSimulationTaskDto> activeTasks,
        IReadOnlySet<string> waitPointCodes) =>
        activeTasks
            .Where(t => !string.IsNullOrWhiteSpace(t.TargetSiteCode))
            .Where(t => waitPointCodes.Contains(t.TargetSiteCode))
            .Select(t => t.TargetSiteCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>PickAtBuf 是否已完成（已載料且無進行中任務/移動；不要求仍停在 BUF 上）。</summary>
    public static bool HasFinishedPickAtBuf(FleetStatusDto vehicle, string _)
    {
        if (!vehicle.CarryingMaterial) return false;
        if (IsEnRoute(vehicle)) return false;

        return vehicle.State.Equals("idle", StringComparison.OrdinalIgnoreCase)
            || vehicle.State.Equals("waiting", StringComparison.OrdinalIgnoreCase);
    }

    public static string? PlanNearestFreeWaitPointCode(
        FleetStatusDto vehicle,
        IReadOnlyList<WmsCellDto> allSites,
        IEnumerable<FleetStatusDto> fleet,
        IReadOnlySet<string>? extraReserved = null)
    {
        var wpList = GetWaitPointList(allSites);
        if (wpList.Count == 0) return null;

        var reserved = GetOccupiedWaitPoints(fleet, wpList, extraReserved);
        return FindNearestFreeWaitPoint(vehicle, wpList, reserved)?.CellId;
    }

    /// <summary>以指定座標（如 BUF 站點）規劃最近空閒 WP。</summary>
    public static string? PlanNearestFreeWaitPointFromCoordinates(
        double x,
        double y,
        IReadOnlyList<WmsCellDto> allSites,
        IEnumerable<FleetStatusDto> fleet,
        IReadOnlySet<string>? extraReserved = null)
    {
        var proxy = new FleetStatusDto { X = x, Y = y, State = "idle" };
        return PlanNearestFreeWaitPointCode(proxy, allSites, fleet, extraReserved);
    }

    /// <summary>
    /// 判斷哪些 WP 已有 AMR 停放：以 current_site 為主；站點未回報時才用 idle 車輛座標輔助。
    /// 不使用 WMS occupied（通常表示物料，不代表有車）。
    /// </summary>
    public static HashSet<string> GetOccupiedWaitPoints(
        IEnumerable<FleetStatusDto> fleet,
        IReadOnlyList<WmsCellDto> waitPoints,
        IReadOnlySet<string>? additionallyReserved = null)
    {
        var wpCodes = waitPoints
            .Select(w => w.CellId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fleetList = fleet.ToList();
        var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (additionallyReserved is not null)
        {
            foreach (var code in additionallyReserved)
                occupied.Add(code);
        }

        foreach (var vehicle in fleetList)
        {
            if (IsMeaningfulSite(vehicle.CurrentSite) && wpCodes.Contains(vehicle.CurrentSite!))
                occupied.Add(vehicle.CurrentSite!);
        }

        foreach (var wp in waitPoints)
        {
            if (occupied.Contains(wp.CellId))
                continue;

            foreach (var vehicle in fleetList)
            {
                if (IsVehicleOccupyingWaitPoint(vehicle, wp))
                {
                    occupied.Add(wp.CellId);
                    break;
                }
            }
        }

        return occupied;
    }

    public static bool IsWaitPointFree(
        WmsCellDto waitPoint,
        IEnumerable<FleetStatusDto> fleet,
        IReadOnlySet<string> reservedWaitPoints) =>
        !reservedWaitPoints.Contains(waitPoint.CellId)
        && !fleet.Any(v => IsVehicleOccupyingWaitPoint(v, waitPoint));

    public static bool IsVehicleAtWaitPointByPosition(FleetStatusDto vehicle, WmsCellDto waitPoint) =>
        DistanceSquared(vehicle.X, vehicle.Y, waitPoint.XPercent, waitPoint.YPercent)
        <= WaitPointArrivalDistanceSquared;

    public static bool IsVehicleOccupyingWaitPoint(FleetStatusDto vehicle, WmsCellDto waitPoint)
    {
        if (IsMeaningfulSite(vehicle.CurrentSite)
            && vehicle.CurrentSite!.Equals(waitPoint.CellId, StringComparison.OrdinalIgnoreCase))
            return true;

        // 站點已有明確回報（如 CHG、TWP）時，不以座標覆蓋
        if (IsMeaningfulSite(vehicle.CurrentSite)) return false;
        if (IsEnRoute(vehicle)) return false;

        return IsVehicleAtWaitPointByPosition(vehicle, waitPoint);
    }

    public static WmsCellDto? FindNearestFreeWaitPoint(
        FleetStatusDto vehicle,
        IReadOnlyList<WmsCellDto> waitPoints,
        IReadOnlySet<string> reservedWaitPoints)
    {
        WmsCellDto? nearest = null;
        var bestDist = double.MaxValue;

        foreach (var wp in waitPoints)
        {
            if (reservedWaitPoints.Contains(wp.CellId))
                continue;

            var dist = DistanceSquared(vehicle.X, vehicle.Y, wp.XPercent, wp.YPercent);
            if (dist < bestDist)
            {
                bestDist = dist;
                nearest = wp;
            }
        }

        return nearest;
    }

    public static int SiteCodeSortKey(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return 0;
        var digits = new string(code.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : 0;
    }

    public static bool IsMeaningfulSite(string? site) =>
        !string.IsNullOrWhiteSpace(site) && site is not "—" and not "-";

    /// <summary>車輛是否仍在執行任務或移動中（尚未停妥）。</summary>
    public static bool IsEnRoute(FleetStatusDto vehicle)
    {
        if (!string.IsNullOrEmpty(vehicle.CurrentTaskId)) return true;
        return vehicle.State.Equals("moving", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>不在 WP 上、且仍在路上的 CAKE / BOBBIN 車。</summary>
    public static IReadOnlyList<FleetStatusDto> GetEnRouteOffWaitPointVehicles(
        IEnumerable<FleetStatusDto> fleet,
        IReadOnlySet<string> waitPointCodes) =>
        fleet
            .Where(v => IsFleetVehicle(v.RobotId))
            .Where(v => IsEnRoute(v))
            .Where(v => !IsOnWaitPoint(v, waitPointCodes))
            .OrderBy(v => v.RobotId, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public const double WaitPointArrivalDistanceSquared = 6.25;

    /// <summary>是否已抵達指定站點並停妥（站點、狀態、座標皆須符合）。</summary>
    public static bool HasReachedSite(
        FleetStatusDto vehicle,
        string targetSiteCode,
        IEnumerable<WmsCellDto>? sites = null)
    {
        if (!IsMeaningfulSite(vehicle.CurrentSite)) return false;
        if (!vehicle.CurrentSite!.Equals(targetSiteCode, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrEmpty(vehicle.CurrentTaskId)) return false;
        if (!vehicle.State.Equals("idle", StringComparison.OrdinalIgnoreCase)
            && !vehicle.State.Equals("waiting", StringComparison.OrdinalIgnoreCase))
            return false;

        if (sites is not null)
        {
            var site = sites.FirstOrDefault(s =>
                s.CellId.Equals(targetSiteCode, StringComparison.OrdinalIgnoreCase));
            if (site is not null)
            {
                var distSq = DistanceSquared(vehicle.X, vehicle.Y, site.XPercent, site.YPercent);
                if (distSq > WaitPointArrivalDistanceSquared)
                    return false;
            }
        }

        return true;
    }

    /// <summary>是否已抵達指定等待點並停妥（站點、狀態、座標皆須符合）。</summary>
    public static bool HasReachedWaitPoint(
        FleetStatusDto vehicle,
        string targetWaitPointCode,
        IEnumerable<WmsCellDto>? waitPoints = null) =>
        HasReachedSite(vehicle, targetWaitPointCode, waitPoints);

    static double DistanceSquared(double x1, double y1, double x2, double y2)
    {
        var dx = x1 - x2;
        var dy = y1 - y2;
        return dx * dx + dy * dy;
    }
}
