using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// Buffer 可派車條件：作業面已設定、作業面 12 Port 皆有料、停車點無車輛停靠。
/// 料況判斷與 Equip 模擬頁相同（Modbus Buffer 暫存器）；停靠判斷來自 TGI /v2/fleet/status。
/// </summary>
public static class BufferDispatchEvaluator
{
    public const int PortsPerSide = BufferParkingRegistry.PortsPerSide;

    public static BufferDispatchEvaluation Evaluate(BufferLiveStation buffer, BufferStationMeta? meta)
    {
        var stationCode = meta?.StationCode ?? $"Buffer {buffer.StationId}";
        var parkingPointId = meta?.ParkingPointId ?? BufferParkingRegistry.GetParkingPointId(buffer.StationId);

        if (buffer.OperationSide is not (1 or 2))
        {
            return new BufferDispatchEvaluation(
                buffer.StationId,
                stationCode,
                parkingPointId,
                IsDispatchable: false,
                Reason: "作業面未設定，無法判斷可派車",
                OperationSideLabel: "未設定",
                HasOperationSide: false,
                IsOperationSideA: false,
                OperationSidePresentCount: 0,
                OperationSidePortCount: PortsPerSide,
                OperationSidePorts: [],
                SideAPresentCount: buffer.PresentCountA,
                SideBPresentCount: buffer.PresentCountB);
        }

        var isSideA = buffer.OperationSide == 1;
        var sideMeta = isSideA ? meta?.SideA : meta?.SideB;
        var values = isSideA ? buffer.SideA : buffer.SideB;
        var sideLabel = isSideA ? "A" : "B";
        var ports = BuildPortStatuses(sideMeta, values);
        var presentCount = ports.Count(p => p.HasMaterial);
        var allFull = presentCount == PortsPerSide;

        return new BufferDispatchEvaluation(
            buffer.StationId,
            stationCode,
            parkingPointId,
            IsDispatchable: allFull,
            Reason: allFull
                ? $"作業面 {sideLabel} 側 12 Port 皆有料，可派車"
                : $"作業面 {sideLabel} 側僅 {presentCount}/{PortsPerSide} Port 有料",
            OperationSideLabel: sideLabel,
            HasOperationSide: true,
            IsOperationSideA: isSideA,
            OperationSidePresentCount: presentCount,
            OperationSidePortCount: PortsPerSide,
            OperationSidePorts: ports,
            SideAPresentCount: buffer.PresentCountA,
            SideBPresentCount: buffer.PresentCountB);
    }

    public static IReadOnlyList<BufferDispatchEvaluation> EvaluateAll(EquipSimLiveSnapshot snapshot) =>
        snapshot.Buffers
            .Select(b => Evaluate(b, BufferParkingRegistry.Get(b.StationId)))
            .ToList();

    public static IReadOnlyList<BufferDispatchEvaluation> ApplyParkingOccupancy(
        IReadOnlyList<BufferDispatchEvaluation> evaluations,
        IEnumerable<FleetStatusDto>? fleetStatuses) =>
        evaluations.Select(e => ApplyParkingOccupancy(e, fleetStatuses)).ToList();

    public static BufferDispatchEvaluation ApplyParkingOccupancy(
        BufferDispatchEvaluation evaluation,
        IEnumerable<FleetStatusDto>? fleetStatuses)
    {
        if (!evaluation.IsDispatchable || fleetStatuses is null)
            return evaluation;

        var parkedRobot = FindParkedVehicleAt(fleetStatuses, evaluation.ParkingPointId);
        if (parkedRobot is null)
            return evaluation;

        return evaluation with
        {
            IsDispatchable = false,
            Reason = $"停車點 {evaluation.ParkingPointId} 已有 {parkedRobot} 停靠，不可派車"
        };
    }

    public static bool IsParkingPointOccupied(
        IEnumerable<FleetStatusDto>? fleetStatuses,
        string parkingPointId,
        out string? parkedRobotId)
    {
        if (fleetStatuses is null)
        {
            parkedRobotId = null;
            return false;
        }

        parkedRobotId = FindParkedVehicleAt(fleetStatuses, parkingPointId);
        return parkedRobotId is not null;
    }

    static string? FindParkedVehicleAt(IEnumerable<FleetStatusDto> fleetStatuses, string parkingPointId)
    {
        foreach (var vehicle in fleetStatuses)
        {
            if (!IsVehicleParkedAt(vehicle, parkingPointId))
                continue;

            if (!string.IsNullOrWhiteSpace(vehicle.RobotId))
                return vehicle.RobotId;
            if (!string.IsNullOrWhiteSpace(vehicle.RobotName))
                return vehicle.RobotName;
        }

        return null;
    }

    static bool IsVehicleParkedAt(FleetStatusDto vehicle, string parkingPointId)
    {
        if (!FleetParkingPlanner.IsMeaningfulSite(vehicle.CurrentSite))
            return false;

        return vehicle.CurrentSite!.Equals(parkingPointId, StringComparison.OrdinalIgnoreCase);
    }

    static IReadOnlyList<BufferPortDispatchStatus> BuildPortStatuses(BufferSideMeta? sideMeta, int[] values)
    {
        var list = new List<BufferPortDispatchStatus>(PortsPerSide);
        for (var i = 0; i < PortsPerSide; i++)
        {
            var portMeta = sideMeta?.Ports.ElementAtOrDefault(i);
            list.Add(new BufferPortDispatchStatus(
                PortNumber: portMeta?.PortNumber ?? i + 1,
                ArmPortNumber: portMeta?.ArmPortNumber ?? i + 1,
                Address: portMeta?.Address ?? 0,
                HasMaterial: BufferLiveStation.IsPresent(values[i]),
                RawValue: values[i]));
        }
        return list;
    }
}
