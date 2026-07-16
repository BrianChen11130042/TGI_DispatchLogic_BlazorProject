using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// 清軸上料 in-flight：鎖定 CLR 停車點；完成條件為曾 busy 後回 idle，且車上 Port 不再全為無絲(1)。
/// </summary>
public static class ClearingLoadDispatchTracker
{
    public const int MinBusySeconds = 3;
    public const int MaxInFlightMinutes = 120;

    static readonly HashSet<string> BusyStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "moving", "picking", "placing", "charging", "waiting", "assigned"
    };

    public static bool IsStationLocked(IEnumerable<ClearingLoadDispatchInFlight> flights, string parkingPointId) =>
        flights.Any(f =>
            f.IsActive &&
            f.ParkingPointId.Equals(parkingPointId, StringComparison.OrdinalIgnoreCase));

    public static ClearingLoadDispatchInFlight Register(
        string parkingPointId,
        string amrCode,
        string taskId) =>
        new(
            parkingPointId,
            amrCode,
            taskId,
            DateTime.Now,
            SawRobotBusy: false,
            IsCompleted: false,
            StatusHint: "已派車，等待車輛開始作業");

    public static List<ClearingLoadDispatchInFlight> Refresh(
        IEnumerable<ClearingLoadDispatchInFlight> flights,
        IReadOnlyList<RobotStatusDto> robots)
    {
        var robotMap = BuildRobotMap(robots);
        return flights.Select(f => RefreshOne(f, robotMap)).ToList();
    }

    public static List<ClearingLoadDispatchInFlight> PruneCompleted(
        IEnumerable<ClearingLoadDispatchInFlight> flights) =>
        flights.Where(f => f.IsActive).ToList();

    static ClearingLoadDispatchInFlight RefreshOne(
        ClearingLoadDispatchInFlight flight,
        Dictionary<string, RobotStatusDto> robotMap)
    {
        if (flight.IsCompleted)
            return flight;

        var elapsed = DateTime.Now - flight.DispatchedAt;
        if (elapsed.TotalMinutes >= MaxInFlightMinutes)
        {
            return flight with
            {
                IsCompleted = true,
                StatusHint = $"逾時解除鎖定（>{MaxInFlightMinutes} 分鐘）"
            };
        }

        var robot = ResolveRobot(robotMap, flight.AmrCode);
        var sawBusy = flight.SawRobotBusy;

        if (robot is not null && IsRobotBusy(robot))
            sawBusy = true;

        if (robot is null)
        {
            return flight with
            {
                SawRobotBusy = sawBusy,
                StatusHint = sawBusy
                    ? "車輛暫時離線，持續等待任務結束"
                    : "已派車，等待 TGI 回報車輛狀態"
            };
        }

        if (!sawBusy)
        {
            return flight with
            {
                StatusHint = $"已派車（{robot.State}），等待車輛開始作業"
            };
        }

        if (IsRobotBusy(robot))
        {
            return flight with
            {
                SawRobotBusy = true,
                StatusHint = $"作業中 · {robot.State} · task={FormatTaskId(robot)} · 無絲 Port {CountRawSilk(robot)}/{GetCapacity(robot)}"
            };
        }

        // idle after sawBusy
        if (!AreAllPortsRawSilk(robot))
        {
            return Complete(flight, true,
                $"任務完成 · 車上已卸出部分無絲 Cake（剩餘無絲 {CountRawSilk(robot)}/{GetCapacity(robot)}）");
        }

        if (elapsed.TotalSeconds >= MinBusySeconds)
        {
            return flight with
            {
                SawRobotBusy = true,
                StatusHint = $"車輛已回 idle，等待車上無絲 Port 減少（目前 {CountRawSilk(robot)}/{GetCapacity(robot)}）"
            };
        }

        return flight with
        {
            SawRobotBusy = true,
            StatusHint = "車輛已回 idle，確認任務狀態中…"
        };
    }

    static ClearingLoadDispatchInFlight Complete(
        ClearingLoadDispatchInFlight flight,
        bool sawBusy,
        string hint) =>
        flight with
        {
            SawRobotBusy = sawBusy,
            IsCompleted = true,
            StatusHint = hint
        };

    static bool IsRobotBusy(RobotStatusDto robot) =>
        !string.Equals(robot.State, "idle", StringComparison.OrdinalIgnoreCase) ||
        BusyStates.Contains(robot.State);

    static bool AreAllPortsRawSilk(RobotStatusDto robot)
    {
        var capacity = GetCapacity(robot);
        if (robot.PortStates.Count < capacity)
            return false;
        return robot.PortStates.Take(capacity).All(v => v == 1);
    }

    static int CountRawSilk(RobotStatusDto robot) =>
        robot.PortStates.Count(v => v == 1);

    static int GetCapacity(RobotStatusDto robot) =>
        robot.CarryingCapacity > 0
            ? robot.CarryingCapacity
            : CakeVehicleDispatchEvaluator.DefaultCakeCapacity;

    static string FormatTaskId(RobotStatusDto robot) =>
        string.IsNullOrWhiteSpace(robot.CurrentTaskId) ? "—" : robot.CurrentTaskId!;

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
