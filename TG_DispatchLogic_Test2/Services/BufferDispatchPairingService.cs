using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

public static class CakeVehicleDispatchEvaluator
{
    public const int DefaultCakeCapacity = 12;
    public const int DefaultBobbinCapacity = TwistingParkingRegistry.PortsPerBobbinUnloadMission;

    public static string FormatPortLabel(ushort value) => value switch
    {
        0 => "空",
        1 => "無絲",
        2 => "有絲",
        9 => "異常",
        _ => value.ToString()
    };

    public static IReadOnlyList<CakePortDispatchStatus> BuildPortStatuses(
        IReadOnlyList<ushort>? portStates,
        int capacity)
    {
        var cap = capacity > 0 ? capacity : DefaultCakeCapacity;
        var list = new List<CakePortDispatchStatus>(cap);
        for (var i = 0; i < cap; i++)
        {
            var raw = portStates is not null && i < portStates.Count ? portStates[i] : (ushort)0;
            list.Add(new CakePortDispatchStatus(i + 1, raw, FormatPortLabel(raw)));
        }
        return list;
    }

    public static IReadOnlyList<CakePortDispatchStatus> BuildEmptyPorts(int capacity = DefaultCakeCapacity) =>
        BuildPortStatuses(null, capacity);

    public static IReadOnlyList<CakeVehicleDispatchStatus> BuildWaitingFleet(
        string reason = "等待 TGI /v2/robots/status…",
        IReadOnlyList<string>? vehicleCodes = null,
        int capacity = DefaultCakeCapacity) =>
        (vehicleCodes ?? DispatchFleetCatalog.CakeVehicleCodes)
            .Select(code => new CakeVehicleDispatchStatus(
                code, "—", "—", 0, capacity, null, false, false, false,
                reason, BuildEmptyPorts(capacity)))
            .ToList();

    public static bool IsParkedAtWaitingArea(
        FleetStatusDto? fleet,
        IReadOnlyList<WmsCellDto> waitPoints,
        string? siteCode)
    {
        if (fleet is not null && waitPoints.Count > 0)
            return FleetParkingPlanner.IsParkedAtAnyWaitPoint(fleet, waitPoints);

        return IsWaitingAreaSiteCode(siteCode);
    }

    public static bool IsWaitingAreaSiteCode(string? siteCode) =>
        !string.IsNullOrWhiteSpace(siteCode)
        && siteCode.StartsWith("WP", StringComparison.OrdinalIgnoreCase);

    public static HashSet<string> BuildBusyAmrSet(
        IEnumerable<ActiveSimulationTaskDto>? activeTasks = null,
        IEnumerable<string>? inFlightAmrCodes = null)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (activeTasks is not null)
        {
            foreach (var task in activeTasks.Where(t => !string.IsNullOrWhiteSpace(t.AssignedAmrCode)))
            {
                foreach (var alias in ExpandRobotAliases(task.AssignedAmrCode))
                    set.Add(alias);
            }
        }

        if (inFlightAmrCodes is not null)
        {
            foreach (var code in inFlightAmrCodes.Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                foreach (var alias in ExpandRobotAliases(code))
                    set.Add(alias);
            }
        }

        return set;
    }

    public static bool IsAmrInBusySet(IReadOnlySet<string> busyAmrCodes, string? amrCode)
    {
        if (string.IsNullOrWhiteSpace(amrCode)) return false;
        foreach (var alias in ExpandRobotAliases(amrCode))
        {
            if (busyAmrCodes.Contains(alias))
                return true;
        }
        return false;
    }

    public static CakeVehicleDispatchStatus Evaluate(
        RobotStatusDto? robot,
        SimulationAmrDto? simulation,
        FleetStatusDto? fleet,
        string amrCode,
        IReadOnlySet<string> busyAmrCodes,
        IReadOnlyList<WmsCellDto> waitPoints)
    {
        var siteCode = fleet?.CurrentSite ?? simulation?.SiteCode;
        var isAtWaitingArea = IsParkedAtWaitingArea(fleet, waitPoints, siteCode);

        if (robot is null)
        {
            return new CakeVehicleDispatchStatus(
                amrCode, "—", "—", 0, DefaultCakeCapacity, siteCode, isAtWaitingArea, false, false,
                "TGI /v2/robots/status 未回報此車",
                BuildPortStatuses(null, DefaultCakeCapacity));
        }

        var capacity = robot.CarryingCapacity > 0 ? robot.CarryingCapacity : DefaultCakeCapacity;
        var ports = BuildPortStatuses(robot.PortStates, capacity);
        var carryingCount = Math.Max(robot.CarryingCount, ports.Count(p => p.RawValue != 0));
        var dispatchEnabled = simulation?.DispatchEnabled ?? true;
        var status = robot.State;

        if (!string.Equals(robot.ConnectionStatus, "connected", StringComparison.OrdinalIgnoreCase))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                isAtWaitingArea, "連線狀態非 connected");

        if (!string.Equals(status, "idle", StringComparison.OrdinalIgnoreCase))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                isAtWaitingArea, $"狀態={status}（需 idle）");

        if (ports.Any(p => p.RawValue != 0))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                isAtWaitingArea, $"車上 {carryingCount}/{capacity} Port 有料（需 12 Port 全空）");

        if (IsAmrInBusySet(busyAmrCodes, robot.RobotId) || IsAmrInBusySet(busyAmrCodes, amrCode))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                isAtWaitingArea, "有進行中任務");

        if (!dispatchEnabled)
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                isAtWaitingArea, "DispatchEnabled=false");

        return new CakeVehicleDispatchStatus(
            robot.RobotId,
            status,
            robot.ConnectionStatus,
            carryingCount,
            capacity,
            siteCode,
            isAtWaitingArea,
            dispatchEnabled,
            true,
            isAtWaitingArea
                ? "可派車（等待區 · 12 Port 全空）"
                : "可派車（12 Port 全空）",
            ports);
    }

    public static CakeVehicleDispatchStatus Evaluate(
        RobotStatusDto? robot,
        SimulationAmrDto? simulation,
        string amrCode,
        IReadOnlySet<string> busyAmrCodes) =>
        Evaluate(robot, simulation, null, amrCode, busyAmrCodes, []);

    public static CakeVehicleDispatchStatus EvaluateForTwistingLoad(
        RobotStatusDto? robot,
        SimulationAmrDto? simulation,
        FleetStatusDto? fleet,
        string amrCode,
        IReadOnlySet<string> busyAmrCodes,
        ActiveSimulationTaskDto? activeTask = null)
    {
        var required = TwistingParkingRegistry.PortsPerLoadMission;
        var siteCode = ResolveVehicleSiteCode(fleet, simulation, robot?.State, activeTask);

        if (robot is null)
        {
            return new CakeVehicleDispatchStatus(
                amrCode, "—", "—", 0, DefaultCakeCapacity, siteCode, false, false, false,
                "TGI /v2/robots/status 未回報此車",
                BuildPortStatuses(null, DefaultCakeCapacity));
        }

        var capacity = robot.CarryingCapacity > 0 ? robot.CarryingCapacity : DefaultCakeCapacity;
        var ports = BuildPortStatuses(robot.PortStates, capacity);
        var cakeCount = ports.Count(p => p.RawValue == 2);
        var carryingCount = Math.Max(robot.CarryingCount, ports.Count(p => p.RawValue != 0));
        var dispatchEnabled = simulation?.DispatchEnabled ?? true;
        var status = robot.State;

        if (!string.Equals(robot.ConnectionStatus, "connected", StringComparison.OrdinalIgnoreCase))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, "連線狀態非 connected");

        if (!string.Equals(status, "idle", StringComparison.OrdinalIgnoreCase))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, $"狀態={status}（需 idle）");

        if (cakeCount < required)
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, $"車上僅 {cakeCount}/{required} Port 有 Cake 有絲（需滿載上料）");

        if (IsAmrInBusySet(busyAmrCodes, robot.RobotId) || IsAmrInBusySet(busyAmrCodes, amrCode))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, "有進行中任務");

        if (!dispatchEnabled)
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, "DispatchEnabled=false");

        return new CakeVehicleDispatchStatus(
            robot.RobotId,
            status,
            robot.ConnectionStatus,
            carryingCount,
            capacity,
            siteCode,
            false,
            dispatchEnabled,
            true,
            $"可上料（{cakeCount} Port 有 Cake 有絲）",
            ports);
    }

    /// <summary>撚紗下料用 Cake 車：需 idle、12 Port 全空（承接無絲 Cake）。</summary>
    public static CakeVehicleDispatchStatus EvaluateForTwistingUnload(
        RobotStatusDto? robot,
        SimulationAmrDto? simulation,
        FleetStatusDto? fleet,
        string amrCode,
        IReadOnlySet<string> busyAmrCodes,
        ActiveSimulationTaskDto? activeTask = null)
    {
        var siteCode = ResolveVehicleSiteCode(fleet, simulation, robot?.State, activeTask);

        if (robot is null)
        {
            return new CakeVehicleDispatchStatus(
                amrCode, "—", "—", 0, DefaultCakeCapacity, siteCode, false, false, false,
                "TGI /v2/robots/status 未回報此車",
                BuildPortStatuses(null, DefaultCakeCapacity));
        }

        var capacity = robot.CarryingCapacity > 0 ? robot.CarryingCapacity : DefaultCakeCapacity;
        var ports = BuildPortStatuses(robot.PortStates, capacity);
        var carryingCount = Math.Max(robot.CarryingCount, ports.Count(p => p.RawValue != 0));
        var dispatchEnabled = simulation?.DispatchEnabled ?? true;
        var status = robot.State;

        if (!string.Equals(robot.ConnectionStatus, "connected", StringComparison.OrdinalIgnoreCase))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, "連線狀態非 connected");

        if (!string.Equals(status, "idle", StringComparison.OrdinalIgnoreCase))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, $"狀態={status}（需 idle）");

        if (ports.Any(p => p.RawValue != 0))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, $"車上 {carryingCount}/{capacity} Port 有料（需 12 Port 全空）");

        if (IsAmrInBusySet(busyAmrCodes, robot.RobotId) || IsAmrInBusySet(busyAmrCodes, amrCode))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, "有進行中任務");

        if (!dispatchEnabled)
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, "DispatchEnabled=false");

        return new CakeVehicleDispatchStatus(
            robot.RobotId,
            status,
            robot.ConnectionStatus,
            carryingCount,
            capacity,
            siteCode,
            IsParkedAtWaitingArea(fleet, [], siteCode),
            dispatchEnabled,
            true,
            "可下料（12 Port 全空）",
            ports);
    }

    public static IReadOnlyList<CakeVehicleDispatchStatus> EvaluateFleetForTwistingUnload(
        IEnumerable<RobotStatusDto> robots,
        IEnumerable<SimulationAmrDto>? simulationAmrs,
        IEnumerable<ActiveSimulationTaskDto> activeTasks,
        IEnumerable<FleetStatusDto>? fleetStatuses = null,
        IEnumerable<string>? inFlightAmrCodes = null)
    {
        var robotMap = BuildRobotMap(robots);
        var simMap = BuildSimulationMap(simulationAmrs);
        var fleetMap = BuildFleetMap(fleetStatuses);
        var busy = BuildBusyAmrSet(activeTasks, inFlightAmrCodes);
        var taskList = activeTasks.ToList();

        return DispatchFleetCatalog.TwistingUnloadCakeVehicleCodes
            .Select(code => EvaluateForTwistingUnload(
                ResolveRobot(robotMap, code),
                ResolveSimulation(simMap, code),
                ResolveFleet(fleetMap, code),
                code,
                busy,
                ResolveActiveTask(taskList, code)))
            .ToList();
    }

    /// <summary>撚紗下料用 Bobbin 車：需 idle、24 Port 全空。</summary>
    public static CakeVehicleDispatchStatus EvaluateForTwistingUnloadBobbin(
        RobotStatusDto? robot,
        SimulationAmrDto? simulation,
        FleetStatusDto? fleet,
        string amrCode,
        IReadOnlySet<string> busyAmrCodes,
        ActiveSimulationTaskDto? activeTask = null)
    {
        var siteCode = ResolveVehicleSiteCode(fleet, simulation, robot?.State, activeTask);
        var defaultCap = DefaultBobbinCapacity;

        if (robot is null)
        {
            return new CakeVehicleDispatchStatus(
                amrCode, "—", "—", 0, defaultCap, siteCode, false, false, false,
                "TGI /v2/robots/status 未回報此車",
                BuildPortStatuses(null, defaultCap));
        }

        var capacity = robot.CarryingCapacity > 0 ? robot.CarryingCapacity : defaultCap;
        var ports = BuildPortStatuses(robot.PortStates, capacity);
        var carryingCount = Math.Max(robot.CarryingCount, ports.Count(p => p.RawValue != 0));
        var dispatchEnabled = simulation?.DispatchEnabled ?? true;
        var status = robot.State;

        if (!string.Equals(robot.ConnectionStatus, "connected", StringComparison.OrdinalIgnoreCase))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, "連線狀態非 connected");

        if (!string.Equals(status, "idle", StringComparison.OrdinalIgnoreCase))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, $"狀態={status}（需 idle）");

        if (ports.Any(p => p.RawValue != 0))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, $"車上 {carryingCount}/{capacity} Port 有料（需 {defaultCap} Port 全空）");

        if (IsAmrInBusySet(busyAmrCodes, robot.RobotId) || IsAmrInBusySet(busyAmrCodes, amrCode))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, "有進行中任務");

        if (!dispatchEnabled)
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, "DispatchEnabled=false");

        return new CakeVehicleDispatchStatus(
            robot.RobotId,
            status,
            robot.ConnectionStatus,
            carryingCount,
            capacity,
            siteCode,
            IsParkedAtWaitingArea(fleet, [], siteCode),
            dispatchEnabled,
            true,
            $"可下料（{capacity} Port 全空）",
            ports);
    }

    public static IReadOnlyList<CakeVehicleDispatchStatus> EvaluateFleetForTwistingUnloadBobbin(
        IEnumerable<RobotStatusDto> robots,
        IEnumerable<SimulationAmrDto>? simulationAmrs,
        IEnumerable<ActiveSimulationTaskDto> activeTasks,
        IEnumerable<FleetStatusDto>? fleetStatuses = null,
        IEnumerable<string>? inFlightAmrCodes = null)
    {
        var robotMap = BuildRobotMap(robots);
        var simMap = BuildSimulationMap(simulationAmrs);
        var fleetMap = BuildFleetMap(fleetStatuses);
        var busy = BuildBusyAmrSet(activeTasks, inFlightAmrCodes);
        var taskList = activeTasks.ToList();

        return DispatchFleetCatalog.TwistingUnloadBobbinVehicleCodes
            .Select(code => EvaluateForTwistingUnloadBobbin(
                ResolveRobot(robotMap, code),
                ResolveSimulation(simMap, code),
                ResolveFleet(fleetMap, code),
                code,
                busy,
                ResolveActiveTask(taskList, code)))
            .ToList();
    }

    /// <summary>AOI 上料用 Bobbin 車：需 idle、24 Port 皆為有絲(2)。</summary>
    public static CakeVehicleDispatchStatus EvaluateForAoiLoad(
        RobotStatusDto? robot,
        SimulationAmrDto? simulation,
        FleetStatusDto? fleet,
        string amrCode,
        IReadOnlySet<string> busyAmrCodes,
        ActiveSimulationTaskDto? activeTask = null)
    {
        var siteCode = ResolveVehicleSiteCode(fleet, simulation, robot?.State, activeTask);
        var defaultCap = DefaultBobbinCapacity;
        var required = DefaultBobbinCapacity;

        if (robot is null)
        {
            return new CakeVehicleDispatchStatus(
                amrCode, "—", "—", 0, defaultCap, siteCode, false, false, false,
                "TGI /v2/robots/status 未回報此車",
                BuildPortStatuses(null, defaultCap));
        }

        var capacity = robot.CarryingCapacity > 0 ? robot.CarryingCapacity : defaultCap;
        var ports = BuildPortStatuses(robot.PortStates, capacity);
        var fullSilkCount = ports.Count(p => p.RawValue == 2);
        var carryingCount = Math.Max(robot.CarryingCount, ports.Count(p => p.RawValue != 0));
        var dispatchEnabled = simulation?.DispatchEnabled ?? true;
        var status = robot.State;

        if (!string.Equals(robot.ConnectionStatus, "connected", StringComparison.OrdinalIgnoreCase))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, "連線狀態非 connected");

        if (!string.Equals(status, "idle", StringComparison.OrdinalIgnoreCase))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, $"狀態={status}（需 idle）");

        if (fullSilkCount < required || ports.Count < required || ports.Take(required).Any(p => p.RawValue != 2))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, $"車上僅 {fullSilkCount}/{required} Port 有絲 Bobbin（需滿載上料）");

        if (IsAmrInBusySet(busyAmrCodes, robot.RobotId) || IsAmrInBusySet(busyAmrCodes, amrCode))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, "有進行中任務");

        if (!dispatchEnabled)
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, "DispatchEnabled=false");

        return new CakeVehicleDispatchStatus(
            robot.RobotId,
            status,
            robot.ConnectionStatus,
            carryingCount,
            capacity,
            siteCode,
            IsParkedAtWaitingArea(fleet, [], siteCode),
            dispatchEnabled,
            true,
            $"可上料（{fullSilkCount} Port 有絲 Bobbin）",
            ports);
    }

    public static IReadOnlyList<CakeVehicleDispatchStatus> EvaluateFleetForAoiLoad(
        IEnumerable<RobotStatusDto> robots,
        IEnumerable<SimulationAmrDto>? simulationAmrs,
        IEnumerable<ActiveSimulationTaskDto> activeTasks,
        IEnumerable<FleetStatusDto>? fleetStatuses = null,
        IEnumerable<string>? inFlightAmrCodes = null)
    {
        var robotMap = BuildRobotMap(robots);
        var simMap = BuildSimulationMap(simulationAmrs);
        var fleetMap = BuildFleetMap(fleetStatuses);
        var busy = BuildBusyAmrSet(activeTasks, inFlightAmrCodes);
        var taskList = activeTasks.ToList();

        return DispatchFleetCatalog.AoiLoadBobbinVehicleCodes
            .Select(code => EvaluateForAoiLoad(
                ResolveRobot(robotMap, code),
                ResolveSimulation(simMap, code),
                ResolveFleet(fleetMap, code),
                code,
                busy,
                ResolveActiveTask(taskList, code)))
            .ToList();
    }

    /// <summary>清軸上料用 Cake 車：需 idle、12 Port 皆為無絲(1)。</summary>
    public static CakeVehicleDispatchStatus EvaluateForClearingLoad(
        RobotStatusDto? robot,
        SimulationAmrDto? simulation,
        FleetStatusDto? fleet,
        string amrCode,
        IReadOnlySet<string> busyAmrCodes,
        ActiveSimulationTaskDto? activeTask = null)
    {
        var siteCode = ResolveVehicleSiteCode(fleet, simulation, robot?.State, activeTask);

        if (robot is null)
        {
            return new CakeVehicleDispatchStatus(
                amrCode, "—", "—", 0, DefaultCakeCapacity, siteCode, false, false, false,
                "TGI /v2/robots/status 未回報此車",
                BuildPortStatuses(null, DefaultCakeCapacity));
        }

        var capacity = robot.CarryingCapacity > 0 ? robot.CarryingCapacity : DefaultCakeCapacity;
        var ports = BuildPortStatuses(robot.PortStates, capacity);
        var rawSilkCount = ports.Count(p => p.RawValue == 1);
        var carryingCount = Math.Max(robot.CarryingCount, ports.Count(p => p.RawValue != 0));
        var dispatchEnabled = simulation?.DispatchEnabled ?? true;
        var status = robot.State;

        if (!string.Equals(robot.ConnectionStatus, "connected", StringComparison.OrdinalIgnoreCase))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, "連線狀態非 connected");

        if (!string.Equals(status, "idle", StringComparison.OrdinalIgnoreCase))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, $"狀態={status}（需 idle）");

        if (ports.Count < capacity || ports.Any(p => p.RawValue != 1))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, $"車上僅 {rawSilkCount}/{capacity} Port 為無絲（需 12 Port 皆為 1）");

        if (IsAmrInBusySet(busyAmrCodes, robot.RobotId) || IsAmrInBusySet(busyAmrCodes, amrCode))
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, "有進行中任務");

        if (!dispatchEnabled)
            return Ineligible(robot, simulation, fleet, ports, carryingCount, dispatchEnabled, siteCode,
                false, "DispatchEnabled=false");

        return new CakeVehicleDispatchStatus(
            robot.RobotId,
            status,
            robot.ConnectionStatus,
            carryingCount,
            capacity,
            siteCode,
            IsParkedAtWaitingArea(fleet, [], siteCode),
            dispatchEnabled,
            true,
            "可清軸上料（12 Port 皆無絲）",
            ports);
    }

    public static IReadOnlyList<CakeVehicleDispatchStatus> EvaluateFleetForClearingLoad(
        IEnumerable<RobotStatusDto> robots,
        IEnumerable<SimulationAmrDto>? simulationAmrs,
        IEnumerable<ActiveSimulationTaskDto> activeTasks,
        IEnumerable<FleetStatusDto>? fleetStatuses = null,
        IEnumerable<string>? inFlightAmrCodes = null)
    {
        var robotMap = BuildRobotMap(robots);
        var simMap = BuildSimulationMap(simulationAmrs);
        var fleetMap = BuildFleetMap(fleetStatuses);
        var busy = BuildBusyAmrSet(activeTasks, inFlightAmrCodes);
        var taskList = activeTasks.ToList();

        return DispatchFleetCatalog.ClearingLoadCakeVehicleCodes
            .Select(code => EvaluateForClearingLoad(
                ResolveRobot(robotMap, code),
                ResolveSimulation(simMap, code),
                ResolveFleet(fleetMap, code),
                code,
                busy,
                ResolveActiveTask(taskList, code)))
            .ToList();
    }

    /// <summary>充電派車：需 idle、空車（Port 皆 0）、電量低於門檻。</summary>
    public static CakeVehicleDispatchStatus EvaluateForCharge(
        RobotStatusDto? robot,
        SimulationAmrDto? simulation,
        FleetStatusDto? fleet,
        string amrCode,
        IReadOnlySet<string> busyAmrCodes,
        int batteryThresholdPercent,
        ActiveSimulationTaskDto? activeTask = null)
    {
        var siteCode = ResolveVehicleSiteCode(fleet, simulation, robot?.State, activeTask);
        var defaultCap = DefaultCapacityFor(amrCode);

        if (robot is null)
        {
            return new CakeVehicleDispatchStatus(
                amrCode, "—", "—", 0, defaultCap, siteCode, false, false, false,
                "TGI /v2/robots/status 未回報此車",
                BuildPortStatuses(null, defaultCap));
        }

        var capacity = robot.CarryingCapacity > 0 ? robot.CarryingCapacity : defaultCap;
        var ports = BuildPortStatuses(robot.PortStates, capacity);
        var carryingCount = Math.Max(robot.CarryingCount, ports.Count(p => p.RawValue != 0));
        var dispatchEnabled = simulation?.DispatchEnabled ?? true;
        var status = robot.State;
        var battery = robot.Battery;
        var threshold = Math.Clamp(batteryThresholdPercent, 1, 100);

        if (!string.Equals(robot.ConnectionStatus, "connected", StringComparison.OrdinalIgnoreCase))
            return ChargeIneligible(robot, ports, carryingCount, dispatchEnabled, siteCode, battery,
                "連線狀態非 connected");

        if (!string.Equals(status, "idle", StringComparison.OrdinalIgnoreCase))
            return ChargeIneligible(robot, ports, carryingCount, dispatchEnabled, siteCode, battery,
                $"狀態={status}（需 idle）");

        if (ports.Count < capacity || ports.Take(capacity).Any(p => p.RawValue != 0) || carryingCount > 0)
            return ChargeIneligible(robot, ports, carryingCount, dispatchEnabled, siteCode, battery,
                $"非空車（載料 {carryingCount}/{capacity}，需 Port 皆空）");

        if (IsAmrInBusySet(busyAmrCodes, robot.RobotId) || IsAmrInBusySet(busyAmrCodes, amrCode))
            return ChargeIneligible(robot, ports, carryingCount, dispatchEnabled, siteCode, battery,
                "有進行中任務（作業或充電 in-flight 未結束，完成後才可派充電）");

        if (!dispatchEnabled)
            return ChargeIneligible(robot, ports, carryingCount, dispatchEnabled, siteCode, battery,
                "DispatchEnabled=false");

        if (battery >= threshold)
            return ChargeIneligible(robot, ports, carryingCount, dispatchEnabled, siteCode, battery,
                $"電量 {battery}% ≥ 門檻 {threshold}%（無需充電）");

        return new CakeVehicleDispatchStatus(
            robot.RobotId,
            status,
            robot.ConnectionStatus,
            carryingCount,
            capacity,
            siteCode,
            IsParkedAtWaitingArea(fleet, [], siteCode),
            dispatchEnabled,
            true,
            $"可充電（電量 {battery}% ＜ 門檻 {threshold}% · 空車）",
            ports,
            BatteryPercent: battery);
    }

    public static IReadOnlyList<CakeVehicleDispatchStatus> EvaluateFleetForCharge(
        IEnumerable<RobotStatusDto> robots,
        IEnumerable<SimulationAmrDto>? simulationAmrs,
        IEnumerable<ActiveSimulationTaskDto> activeTasks,
        int batteryThresholdPercent,
        IEnumerable<FleetStatusDto>? fleetStatuses = null,
        IEnumerable<string>? inFlightAmrCodes = null)
    {
        var robotMap = BuildRobotMap(robots);
        var simMap = BuildSimulationMap(simulationAmrs);
        var fleetMap = BuildFleetMap(fleetStatuses);
        var busy = BuildBusyAmrSet(activeTasks, inFlightAmrCodes);
        var taskList = activeTasks.ToList();

        return DispatchFleetCatalog.ChargeVehicleCodes
            .Select(code => EvaluateForCharge(
                ResolveRobot(robotMap, code),
                ResolveSimulation(simMap, code),
                ResolveFleet(fleetMap, code),
                code,
                busy,
                batteryThresholdPercent,
                ResolveActiveTask(taskList, code)))
            .ToList();
    }

    static int DefaultCapacityFor(string amrCode) =>
        amrCode.StartsWith("BOBBIN", StringComparison.OrdinalIgnoreCase)
            ? DefaultBobbinCapacity
            : DefaultCakeCapacity;

    static CakeVehicleDispatchStatus ChargeIneligible(
        RobotStatusDto robot,
        IReadOnlyList<CakePortDispatchStatus> ports,
        int carryingCount,
        bool dispatchEnabled,
        string? siteCode,
        int battery,
        string reason) =>
        new(
            robot.RobotId,
            robot.State,
            robot.ConnectionStatus,
            carryingCount,
            robot.CarryingCapacity > 0 ? robot.CarryingCapacity : DefaultCapacityFor(robot.RobotId),
            siteCode,
            false,
            dispatchEnabled,
            false,
            reason,
            ports,
            BatteryPercent: battery);

    static string? ResolveVehicleSiteCode(
        FleetStatusDto? fleet,
        SimulationAmrDto? simulation,
        string? robotState,
        ActiveSimulationTaskDto? activeTask)
    {
        var site = fleet?.CurrentSite ?? simulation?.SiteCode;
        if (!string.IsNullOrWhiteSpace(site))
            return site;

        if (string.Equals(robotState, "moving", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(activeTask?.TargetSiteCode))
            return $"→ {activeTask.TargetSiteCode}";

        return null;
    }

    static ActiveSimulationTaskDto? ResolveActiveTask(
        IEnumerable<ActiveSimulationTaskDto>? activeTasks,
        string amrCode)
    {
        if (activeTasks is null) return null;
        foreach (var task in activeTasks)
        {
            if (string.IsNullOrWhiteSpace(task.AssignedAmrCode)) continue;
            var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var alias in ExpandRobotAliases(task.AssignedAmrCode))
                assigned.Add(alias);
            if (IsAmrInBusySet(assigned, amrCode))
                return task;
        }
        return null;
    }

    public static CakeVehicleDispatchStatus EvaluateForTwistingLoad(
        RobotStatusDto? robot,
        SimulationAmrDto? simulation,
        string amrCode,
        IReadOnlySet<string> busyAmrCodes) =>
        EvaluateForTwistingLoad(robot, simulation, null, amrCode, busyAmrCodes);

    public static IReadOnlyList<CakeVehicleDispatchStatus> EvaluateFleetForTwistingLoad(
        IEnumerable<RobotStatusDto> robots,
        IEnumerable<SimulationAmrDto>? simulationAmrs,
        IEnumerable<ActiveSimulationTaskDto> activeTasks,
        IEnumerable<FleetStatusDto>? fleetStatuses = null,
        IEnumerable<string>? inFlightAmrCodes = null)
    {
        var robotMap = BuildRobotMap(robots);
        var simMap = BuildSimulationMap(simulationAmrs);
        var fleetMap = BuildFleetMap(fleetStatuses);
        var busy = BuildBusyAmrSet(activeTasks, inFlightAmrCodes);
        var taskList = activeTasks.ToList();

        return DispatchFleetCatalog.CakeVehicleCodes
            .Select(code => EvaluateForTwistingLoad(
                ResolveRobot(robotMap, code),
                ResolveSimulation(simMap, code),
                ResolveFleet(fleetMap, code),
                code,
                busy,
                ResolveActiveTask(taskList, code)))
            .ToList();
    }

    public static IReadOnlyList<CakeVehicleDispatchStatus> EvaluateFleet(
        IEnumerable<RobotStatusDto> robots,
        IEnumerable<SimulationAmrDto>? simulationAmrs,
        IEnumerable<ActiveSimulationTaskDto> activeTasks,
        IEnumerable<FleetStatusDto>? fleetStatuses = null,
        IReadOnlyList<WmsCellDto>? waitPoints = null,
        IEnumerable<string>? inFlightAmrCodes = null)
    {
        var robotMap = BuildRobotMap(robots);
        var simMap = BuildSimulationMap(simulationAmrs);
        var fleetMap = BuildFleetMap(fleetStatuses);
        var busy = BuildBusyAmrSet(activeTasks, inFlightAmrCodes);
        var wpList = waitPoints ?? [];

        return DispatchFleetCatalog.CakeVehicleCodes
            .Select(code => Evaluate(
                ResolveRobot(robotMap, code),
                ResolveSimulation(simMap, code),
                ResolveFleet(fleetMap, code),
                code,
                busy,
                wpList))
            .ToList();
    }

    public static IReadOnlyList<CakeVehicleDispatchStatus> EvaluateFleet(
        IEnumerable<RobotStatusDto> robots,
        IEnumerable<SimulationAmrDto>? simulationAmrs,
        IEnumerable<ActiveSimulationTaskDto> activeTasks) =>
        EvaluateFleet(robots, simulationAmrs, activeTasks, null, null);

    static Dictionary<string, FleetStatusDto> BuildFleetMap(IEnumerable<FleetStatusDto>? fleetStatuses)
    {
        var map = new Dictionary<string, FleetStatusDto>(StringComparer.OrdinalIgnoreCase);
        if (fleetStatuses is null) return map;

        foreach (var fleet in fleetStatuses)
        {
            RegisterRobot(map, fleet.RobotId, fleet);
            RegisterRobot(map, fleet.RobotName, fleet);
        }
        return map;
    }

    static void RegisterRobot(Dictionary<string, FleetStatusDto> map, string? key, FleetStatusDto fleet)
    {
        foreach (var alias in ExpandRobotAliases(key))
        {
            if (!string.IsNullOrWhiteSpace(alias))
                map[alias] = fleet;
        }
    }

    static FleetStatusDto? ResolveFleet(Dictionary<string, FleetStatusDto> map, string amrCode)
    {
        foreach (var alias in ExpandRobotAliases(amrCode))
        {
            if (map.TryGetValue(alias, out var fleet))
                return fleet;
        }
        return null;
    }

    static Dictionary<string, RobotStatusDto> BuildRobotMap(IEnumerable<RobotStatusDto> robots)
    {
        var map = new Dictionary<string, RobotStatusDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var robot in robots)
        {
            RegisterRobot(map, robot.RobotId, robot);
            RegisterRobot(map, robot.RobotName, robot);
        }
        return map;
    }

    static void RegisterRobot(Dictionary<string, RobotStatusDto> map, string? key, RobotStatusDto robot)
    {
        foreach (var alias in ExpandRobotAliases(key))
        {
            if (!string.IsNullOrWhiteSpace(alias))
                map[alias] = robot;
        }
    }

    static RobotStatusDto? ResolveRobot(Dictionary<string, RobotStatusDto> map, string amrCode)
    {
        foreach (var alias in ExpandRobotAliases(amrCode))
        {
            if (map.TryGetValue(alias, out var robot))
                return robot;
        }
        return null;
    }

    static Dictionary<string, SimulationAmrDto> BuildSimulationMap(IEnumerable<SimulationAmrDto>? simulationAmrs)
    {
        var map = new Dictionary<string, SimulationAmrDto>(StringComparer.OrdinalIgnoreCase);
        if (simulationAmrs is null) return map;

        foreach (var amr in simulationAmrs)
        {
            foreach (var alias in ExpandRobotAliases(amr.AmrCode))
            {
                if (!string.IsNullOrWhiteSpace(alias))
                    map[alias] = amr;
            }
        }
        return map;
    }

    static SimulationAmrDto? ResolveSimulation(Dictionary<string, SimulationAmrDto> map, string amrCode)
    {
        foreach (var alias in ExpandRobotAliases(amrCode))
        {
            if (map.TryGetValue(alias, out var amr))
                return amr;
        }
        return null;
    }

    /// <summary>
    /// 作業派車門禁：電量低於充電門檻且空車 → 不可再派（應改充電）；
    /// 仍有料件則繼續可派，直到卸空。已派出的 in-flight 不受影響。
    /// </summary>
    public static IReadOnlyList<CakeVehicleDispatchStatus> ApplyLowBatteryEmptyWorkGate(
        IReadOnlyList<CakeVehicleDispatchStatus> vehicles,
        IEnumerable<RobotStatusDto> robots,
        int batteryThresholdPercent)
    {
        var threshold = Math.Clamp(batteryThresholdPercent, 1, 100);
        var robotMap = BuildRobotMap(robots);
        var result = new List<CakeVehicleDispatchStatus>(vehicles.Count);

        foreach (var vehicle in vehicles)
        {
            var robot = ResolveRobot(robotMap, vehicle.AmrCode);
            var battery = robot?.Battery ?? vehicle.BatteryPercent;
            var stamped = vehicle.BatteryPercent == battery
                ? vehicle
                : vehicle with { BatteryPercent = battery };

            if (!stamped.IsEligible)
            {
                result.Add(stamped);
                continue;
            }

            if (battery >= threshold)
            {
                result.Add(stamped);
                continue;
            }

            if (!IsVehicleEmpty(stamped))
            {
                result.Add(stamped);
                continue;
            }

            result.Add(stamped with
            {
                IsEligible = false,
                Reason = $"電量 {battery}% ＜ 門檻 {threshold}% 且空車，應改充電（完成進行中任務後不再派作業）"
            });
        }

        return result;
    }

    public static bool IsVehicleEmpty(CakeVehicleDispatchStatus vehicle)
    {
        if (vehicle.CarryingCount > 0) return false;
        var capacity = vehicle.CarryingCapacity > 0 ? vehicle.CarryingCapacity : vehicle.Ports.Count;
        if (capacity <= 0 || vehicle.Ports.Count < capacity) return false;
        return vehicle.Ports.Take(capacity).All(p => p.RawValue == 0);
    }

    /// <summary>CAKE-01 / Cake-01 / cake01 等格式互通。</summary>
    public static IEnumerable<string> ExpandRobotAliases(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) yield break;

        var s = code.Trim();
        yield return s;

        var upper = s.ToUpperInvariant();
        if (!upper.Equals(s, StringComparison.Ordinal))
            yield return upper;

        if (upper.Contains('-'))
        {
            var parts = upper.Split('-', 2, StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                yield return $"{parts[0]}-{parts[1].TrimStart('0')}";
                yield return parts[0] + parts[1];
                if (parts[0] == "CAKE")
                    yield return $"Cake-{parts[1]}";
                if (parts[0] == "BOBBIN")
                    yield return $"Bobbin-{parts[1]}";
            }
        }
    }

    static CakeVehicleDispatchStatus Ineligible(
        RobotStatusDto robot,
        SimulationAmrDto? simulation,
        FleetStatusDto? fleet,
        IReadOnlyList<CakePortDispatchStatus> ports,
        int carryingCount,
        bool dispatchEnabled,
        string? siteCode,
        bool isAtWaitingArea,
        string reason) =>
        new(
            robot.RobotId,
            robot.State,
            robot.ConnectionStatus,
            carryingCount,
            robot.CarryingCapacity > 0 ? robot.CarryingCapacity : DefaultCakeCapacity,
            siteCode ?? fleet?.CurrentSite ?? simulation?.SiteCode,
            isAtWaitingArea,
            dispatchEnabled,
            false,
            reason,
            ports);
}

public static class BufferDispatchPairingService
{
    public static IReadOnlyList<BufferDispatchPair> Pair(
        IReadOnlyList<BufferDispatchEvaluation> buffers,
        IReadOnlyList<CakeVehicleDispatchStatus> vehicles,
        IReadOnlySet<string>? busyAmrCodes = null)
    {
        var readyBuffers = buffers
            .Where(b => b.IsDispatchable)
            .OrderBy(b => b.StationId)
            .ToList();
        var readyVehicles = vehicles
            .Where(v => v.IsEligible)
            .Where(v => busyAmrCodes is null || !CakeVehicleDispatchEvaluator.IsAmrInBusySet(busyAmrCodes, v.AmrCode))
            .OrderByDescending(v => v.IsAtWaitingArea)
            .ThenBy(v => v.AmrCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var count = Math.Min(readyBuffers.Count, readyVehicles.Count);
        var pairs = new List<BufferDispatchPair>(count);
        for (var i = 0; i < count; i++)
        {
            var buffer = readyBuffers[i];
            var vehicle = readyVehicles[i];
            var flowRequest = BufferFlowDispatchBuilder.Build(buffer, vehicle);
            pairs.Add(new BufferDispatchPair(
                buffer,
                vehicle,
                flowRequest,
                BufferFlowDispatchBuilder.FlowName,
                BufferFlowDispatchBuilder.Serialize(flowRequest)));
        }

        return pairs;
    }
}
