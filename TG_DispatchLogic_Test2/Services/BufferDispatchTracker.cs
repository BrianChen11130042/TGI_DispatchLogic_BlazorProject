using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// 短期作法：派車後鎖定 Buffer，以車輛狀態 + Modbus 料況判斷 amr_mission 是否結束。
/// 完成條件：派車時記錄的作業面（A/B）12 Port 料件皆已取完，且車輛曾 busy 後回 idle。
/// </summary>
public static class BufferDispatchTracker
{
    public const int MinBusySeconds = 3;
    public const int MaxInFlightMinutes = 120;

    static readonly HashSet<string> BusyStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "moving", "picking", "placing", "charging", "waiting", "assigned"
    };

    public static bool IsBufferLocked(IEnumerable<BufferDispatchInFlight> flights, string parkingPointId) =>
        flights.Any(f =>
            f.IsActive &&
            f.ParkingPointId.Equals(parkingPointId, StringComparison.OrdinalIgnoreCase));

    public static BufferDispatchInFlight Register(
        string parkingPointId,
        string amrCode,
        string taskId,
        string dispatchedOperationSide) =>
        new(
            parkingPointId,
            amrCode,
            taskId,
            dispatchedOperationSide,
            DateTime.Now,
            SawRobotBusy: false,
            IsCompleted: false,
            StatusHint: $"已派車（作業面 {dispatchedOperationSide} 側），等待車輛開始作業");

    public static List<BufferDispatchInFlight> Refresh(
        IEnumerable<BufferDispatchInFlight> flights,
        IReadOnlyList<RobotStatusDto> robots,
        IReadOnlyList<BufferDispatchEvaluation>? bufferEvaluations)
    {
        var robotMap = BuildRobotMap(robots);
        var bufferMap = bufferEvaluations?
            .ToDictionary(b => b.ParkingPointId, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, BufferDispatchEvaluation>(StringComparer.OrdinalIgnoreCase);

        return flights.Select(f => RefreshOne(f, robotMap, bufferMap)).ToList();
    }

    public static List<BufferDispatchInFlight> PruneCompleted(IEnumerable<BufferDispatchInFlight> flights) =>
        flights.Where(f => f.IsActive).ToList();

    static BufferDispatchInFlight RefreshOne(
        BufferDispatchInFlight flight,
        Dictionary<string, RobotStatusDto> robotMap,
        Dictionary<string, BufferDispatchEvaluation> bufferMap)
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

        bufferMap.TryGetValue(flight.ParkingPointId, out var bufferEval);
        var dispatchedPresent = GetDispatchedSidePresentCount(bufferEval, flight.DispatchedOperationSide);
        var dispatchedSideEmpty = dispatchedPresent == 0;
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
                    ? FormatProgressHint(flight, dispatchedPresent, "車輛暫時離線，持續等待任務結束")
                    : $"已派車（作業面 {flight.DispatchedOperationSide} 側），等待 TGI 回報車輛狀態"
            };
        }

        if (!sawBusy)
        {
            return flight with
            {
                StatusHint = $"已派車（{robot.State}），等待車輛開始作業 · 作業面 {flight.DispatchedOperationSide} 側 {FormatSideProgress(dispatchedPresent)}"
            };
        }

        if (IsRobotBusy(robot))
        {
            return flight with
            {
                SawRobotBusy = true,
                StatusHint = FormatProgressHint(flight, dispatchedPresent,
                    $"作業中 · {robot.State} · task={FormatTaskId(robot)}")
            };
        }

        if (HasMaterialOnPorts(robot))
        {
            return Complete(flight, true,
                $"任務完成 · 作業面 {flight.DispatchedOperationSide} 側料件已上車（{robot.CarryingCount} Port）");
        }

        if (dispatchedSideEmpty)
        {
            return Complete(flight, true,
                $"任務完成 · 作業面 {flight.DispatchedOperationSide} 側 12 Port 料件已取完 · 車輛 {robot.State}");
        }

        if (elapsed.TotalSeconds >= MinBusySeconds)
        {
            return flight with
            {
                SawRobotBusy = true,
                StatusHint = FormatProgressHint(flight, dispatchedPresent,
                    $"車輛已回 idle，等待作業面 {flight.DispatchedOperationSide} 側料件取完")
            };
        }

        return flight with
        {
            SawRobotBusy = true,
            StatusHint = FormatProgressHint(flight, dispatchedPresent, "車輛已回 idle，確認任務狀態中…")
        };
    }

    static string FormatSideProgress(int presentCount) =>
        presentCount < 0
            ? "—"
            : $"{presentCount}/{BufferParkingRegistry.PortsPerSide} Port 剩餘";

    static string FormatProgressHint(BufferDispatchInFlight flight, int presentCount, string prefix) =>
        $"{prefix} · 作業面 {flight.DispatchedOperationSide} 側 {FormatSideProgress(presentCount)}";

    static int GetDispatchedSidePresentCount(BufferDispatchEvaluation? eval, string dispatchedSide)
    {
        if (eval is null || string.IsNullOrWhiteSpace(dispatchedSide))
            return -1;

        return dispatchedSide.Equals("A", StringComparison.OrdinalIgnoreCase)
            ? eval.SideAPresentCount
            : eval.SideBPresentCount;
    }

    static BufferDispatchInFlight Complete(
        BufferDispatchInFlight flight,
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

    static bool HasMaterialOnPorts(RobotStatusDto robot) =>
        robot.PortStates.Any(v => v != 0);

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
