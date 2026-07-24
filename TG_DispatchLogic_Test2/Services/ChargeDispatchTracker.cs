using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// 充電 in-flight：鎖定充電站與車號；
/// 完成條件：曾進入 busy／charging 後回 idle，且 TGI 無 current_task（或電量已達目標）。
/// 不以 FakeBattery 單獨當唯一結案條件，避免幽靈鎖。
/// </summary>
public static class ChargeDispatchTracker
{
    public const int MinBusySeconds = 3;
    public const int MaxInFlightMinutes = 120;

    static readonly HashSet<string> BusyStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "moving", "picking", "placing", "charging", "waiting", "assigned"
    };

    public static bool IsStationLocked(IEnumerable<ChargeDispatchInFlight> flights, string parkingPointId) =>
        flights.Any(f =>
            f.IsActive &&
            f.ParkingPointId.Equals(parkingPointId, StringComparison.OrdinalIgnoreCase));

    public static bool IsAmrInFlight(IEnumerable<ChargeDispatchInFlight> flights, string amrCode) =>
        flights.Any(f =>
            f.IsActive &&
            CakeVehicleDispatchEvaluator.ExpandRobotAliases(amrCode)
                .Any(a => f.AmrCode.Equals(a, StringComparison.OrdinalIgnoreCase)
                          || CakeVehicleDispatchEvaluator.ExpandRobotAliases(f.AmrCode)
                              .Any(b => b.Equals(a, StringComparison.OrdinalIgnoreCase))));

    public static ChargeDispatchInFlight Register(
        string parkingPointId,
        string amrCode,
        string taskId,
        int targetPercent) =>
        new(
            parkingPointId,
            amrCode,
            taskId,
            Math.Clamp(targetPercent, 1, 100),
            DateTime.Now,
            SawRobotBusy: false,
            IsCompleted: false,
            StatusHint: "已派車，等待車輛前往充電站");

    public static List<ChargeDispatchInFlight> Refresh(
        IEnumerable<ChargeDispatchInFlight> flights,
        IReadOnlyList<RobotStatusDto> robots)
    {
        var robotMap = BuildRobotMap(robots);
        return flights.Select(f => RefreshOne(f, robotMap)).ToList();
    }

    public static List<ChargeDispatchInFlight> PruneCompleted(
        IEnumerable<ChargeDispatchInFlight> flights) =>
        flights.Where(f => f.IsActive).ToList();

    static ChargeDispatchInFlight RefreshOne(
        ChargeDispatchInFlight flight,
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
                    ? "車輛暫時離線，持續等待充電結束"
                    : "已派車，等待 TGI 回報車輛狀態"
            };
        }

        if (!sawBusy)
        {
            return flight with
            {
                StatusHint =
                    $"已派車（{robot.State}），等待車輛開始移動／充電 · 電量 {robot.Battery}% · task={FormatTaskId(robot)}"
            };
        }

        if (IsRobotBusy(robot))
        {
            return flight with
            {
                SawRobotBusy = true,
                StatusHint =
                    $"作業中 · {robot.State} · 電量 {robot.Battery}%／目標 {flight.TargetPercent}% · task={FormatTaskId(robot)}"
            };
        }

        // 已回 idle：優先以 TGI 無任務結案（避免 API FakeBattery 永遠達不到目標）
        var noTask = string.IsNullOrWhiteSpace(robot.CurrentTaskId);
        var batteryOk = robot.Battery >= flight.TargetPercent;
        if (elapsed.TotalSeconds >= MinBusySeconds && (noTask || batteryOk))
        {
            var why = batteryOk
                ? $"電量已達標 {robot.Battery}%／{flight.TargetPercent}%"
                : $"TGI idle 且無任務（電量 {robot.Battery}%）";
            return Complete(flight, true, $"任務完成 · {why} · 解除充電站鎖定");
        }

        return flight with
        {
            SawRobotBusy = true,
            StatusHint = noTask
                ? $"車輛已回 idle，確認結案中…（電量 {robot.Battery}%／目標 {flight.TargetPercent}%）"
                : $"車輛已回 idle，等待 TGI 清任務或電量達標（目前 {robot.Battery}%／目標 {flight.TargetPercent}% · task={FormatTaskId(robot)}）"
        };
    }

    static ChargeDispatchInFlight Complete(
        ChargeDispatchInFlight flight,
        bool sawBusy,
        string hint) =>
        flight with
        {
            SawRobotBusy = sawBusy,
            IsCompleted = true,
            StatusHint = hint
        };

    static bool IsRobotBusy(RobotStatusDto robot)
    {
        if (BusyStates.Contains(robot.State))
            return true;
        return !string.Equals(robot.State, "idle", StringComparison.OrdinalIgnoreCase);
    }

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
