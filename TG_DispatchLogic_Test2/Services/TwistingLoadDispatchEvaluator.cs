using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// 撚紗上料可派車：撚紗機狀態=叫車(1)，停車點 Cake 4 Port 全空，可湊滿尾端起算 3 停車點一組。
/// </summary>
public static class TwistingLoadDispatchEvaluator
{
    public static IReadOnlyList<TwistingLoadMachineEvaluation> EvaluateAll(EquipSimLiveSnapshot snapshot)
    {
        var list = new List<TwistingLoadMachineEvaluation>();
        foreach (var machine in snapshot.Machines)
        {
            if (machine.Detail is null) continue;
            list.Add(EvaluateSide(machine, machine.Detail.SideA, snapshot));
            list.Add(EvaluateSide(machine, machine.Detail.SideB, snapshot));
        }
        return list;
    }

    public static TwistingLoadMachineEvaluation EvaluateSide(
        MachineLiveSummary machine,
        ParkingSideDto side,
        EquipSimLiveSnapshot snapshot)
    {
        var statusLabel = EquipSimUiHelper.MachineStatusLabel(machine.Status);
        var dockingPoints = side.ParkingPoints
            .Select(pt => EvaluateDockingPoint(pt, snapshot))
            .ToList();

        var availableMissions = TwistingLoadMissionPlanner.PlanTailFirstMissions(dockingPoints);
        var firstMission = availableMissions.FirstOrDefault() ?? [];

        var isDispatchable = machine.Status == TwistingParkingRegistry.CallVehicleStatus
                             && availableMissions.Count > 0;

        var reason = machine.Status != TwistingParkingRegistry.CallVehicleStatus
            ? $"狀態={statusLabel}（需 叫車）"
            : availableMissions.Count == 0
                ? "尚無完整 3 停車點可上 Cake 料（單行道尾端往前湊組）"
                : availableMissions.Count == 1
                    ? $"叫車上 Cake 料 · {TwistingLoadLaneAdmission.FormatTwpLane(side.TwpGroupId)} {TwistingLoadMissionPlanner.FormatMissionStops(firstMission)}"
                    : $"叫車上 Cake 料 · {TwistingLoadLaneAdmission.FormatTwpLane(side.TwpGroupId)} {availableMissions.Count} 組可派（優先 {TwistingLoadMissionPlanner.FormatMissionStops(firstMission)}）";

        return new TwistingLoadMachineEvaluation(
            machine.MachineId,
            machine.MachineCode,
            side.Side,
            side.TwpGroupId,
            machine.Status,
            statusLabel,
            isDispatchable,
            reason,
            dockingPoints,
            availableMissions,
            firstMission);
    }

    static TwistingDockingPointEvaluation EvaluateDockingPoint(
        ParkingPointDto point,
        EquipSimLiveSnapshot snapshot)
    {
        var ports = point.Ports
            .Select(p => BuildPortStatus(p, snapshot))
            .ToList();
        var needCount = ports.Count(p => p.NeedsCakeLoad);
        var allEmpty = needCount == TwistingParkingRegistry.PortsPerDockingPoint;

        return new TwistingDockingPointEvaluation(
            point.ParkingPointId,
            point.DNumber,
            point.Sequence,
            allEmpty,
            allEmpty
                ? $"4/4 Cake Port 待上料"
                : $"僅 {needCount}/{TwistingParkingRegistry.PortsPerDockingPoint} Port 待上料",
            ports);
    }

    static TwistingPortDispatchStatus BuildPortStatus(ParkingPortDto port, EquipSimLiveSnapshot snapshot)
    {
        var raw = NormalizePortValue(snapshot.GetRaw(port.CakeAddress));
        return new TwistingPortDispatchStatus(
            port.PortNumber,
            port.ArmPortNumber,
            port.CakeAddress,
            raw,
            CakeVehicleDispatchEvaluator.FormatPortLabel((ushort)raw),
            raw == 0);
    }

    static int NormalizePortValue(int raw) => raw & 0xFF;
}
