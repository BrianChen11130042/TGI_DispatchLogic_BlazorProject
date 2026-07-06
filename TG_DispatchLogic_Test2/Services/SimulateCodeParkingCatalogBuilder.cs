using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>依 SimulateCode TwistingParkingRegistry 規則建立停車對照表。</summary>
public static class SimulateCodeParkingCatalogBuilder
{
    const int MachineCount = 35;
    const int PortsPerSide = 84;
    const int DockingPointsPerSide = 21;
    const int PortsPerDockingPoint = 4;
    const int SideAPortStart = 85;
    const int SideBPortStart = 1;
    const int SideAArmPortStart = 5;
    const int SideBArmPortStart = 1;
    const int SideBArmPortEnd = 4;

    public static ParkingCatalogDto Build() =>
        new(
            Version: "embedded-1.0",
            GeneratedAt: DateTime.Now,
            MachineCount: MachineCount,
            Description: "內建對照表（與 SimulateCode 停車點 / Modbus / 手臂 Port 規則一致）",
            Machines: Enumerable.Range(1, MachineCount).Select(BuildMachine).ToList());

    static ParkingMachineDto BuildMachine(int machineId) =>
        new(
            MachineId: machineId,
            MachineCode: $"M{machineId:D2}",
            SideA: BuildSide(machineId, 'A'),
            SideB: BuildSide(machineId, 'B'));

    static ParkingSideDto BuildSide(int machineId, char side)
    {
        var cakeBase = 2000 + (machineId - 1) * 200 + (side == 'B' ? 100 : 0);
        var bobbinBase = 10000 + (machineId - 1) * 200 + (side == 'B' ? 100 : 0);
        var twpGroupId = GetTwpGroupId(machineId, side);
        var portStart = side == 'A' ? SideAPortStart : SideBPortStart;
        var armStart = side == 'A' ? SideAArmPortStart : SideBArmPortStart;
        var armEnd = side == 'A' ? SideAArmPortStart + 3 : SideBArmPortEnd;

        var points = new List<ParkingPointDto>();
        for (var dp = 0; dp < DockingPointsPerSide; dp++)
        {
            var sequence = DockingPointsPerSide - dp;
            var portIndexBase = dp * PortsPerDockingPoint;
            var ports = new List<ParkingPortDto>();

            for (var p = 0; p < PortsPerDockingPoint; p++)
            {
                var portIndex = portIndexBase + p;
                var portNumber = GetPortNumber(side, sequence, p);
                var armPort = GetArmPortNumber(side, p);
                ports.Add(new ParkingPortDto(
                    portNumber, armPort,
                    cakeBase + portIndex, bobbinBase + portIndex));
            }

            points.Add(new ParkingPointDto(
                machineId, $"M{machineId:D2}", side,
                $"TWP{twpGroupId:D2}-{sequence:D2}",
                dp + 1, sequence,
                GetSharedPartner(machineId, side, sequence, twpGroupId),
                ports));
        }

        return new ParkingSideDto(
            machineId, $"M{machineId:D2}", side, twpGroupId,
            portStart, portStart + PortsPerSide - 1,
            armStart, armEnd,
            cakeBase, cakeBase + PortsPerSide - 1,
            bobbinBase, bobbinBase + PortsPerSide - 1,
            points.FirstOrDefault()?.SharedWith, points);
    }

    static int GetTwpGroupId(int machineId, char side) =>
        side == 'A' ? (machineId == 1 ? 1 : machineId) : machineId + 1;

    static int GetPortNumber(char side, int sequence, int portInPoint) =>
        (side == 'A' ? SideAPortStart : SideBPortStart)
        + (sequence - 1) * PortsPerDockingPoint + portInPoint;

    static int GetArmPortNumber(char side, int portInPoint) =>
        (side == 'A' ? SideAArmPortStart - 1 : 0) + portInPoint + 1;

    static SharedDockingPartnerDto? GetSharedPartner(int machineId, char side, int sequence, int twpGroup)
    {
        var parkingId = $"TWP{twpGroup:D2}-{sequence:D2}";
        if (side == 'B' && machineId < MachineCount)
            return new SharedDockingPartnerDto(machineId + 1, 'A', parkingId);
        if (side == 'A' && machineId > 1)
            return new SharedDockingPartnerDto(machineId - 1, 'B', parkingId);
        return null;
    }
}
