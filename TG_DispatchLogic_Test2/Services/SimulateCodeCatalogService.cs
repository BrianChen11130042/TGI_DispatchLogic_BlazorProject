using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// 依 SimulateCode 程式規則建立完整 Modbus 對照表（不呼叫 API）。
/// </summary>
public class SimulateCodeCatalogService
{
    const int BufferCount = 5;
    const int BufferBlockSize = 40;
    const int BufferPortsPerSide = 12;
    const int MachineCount = 35;
    const int PortsPerSide = 84;
    const int ClearingCount = 5;
    const int PackagingCount = 2;

    public ModbusEquipCatalog BuildCatalog()
    {
        var parking = SimulateCodeParkingCatalogBuilder.Build();
        var registers = new List<ModbusRegisterDef>();
        AddBufferRegisters(registers);
        AddMachineStatusRegisters(registers, parking);
        AddSummaryRegisters(registers, parking);
        AddTwistingDetailRegisters(registers, parking);
        AddClearingRegisters(registers);
        AddPackagingRegisters(registers);

        return new ModbusEquipCatalog
        {
            Parking = parking,
            Registers = registers,
            PollBlocks = BuildPollBlocks(parking)
        };
    }

    static void AddBufferRegisters(List<ModbusRegisterDef> list)
    {
        for (var stationId = 1; stationId <= BufferCount; stationId++)
        {
            var baseAddr = 1000 + (stationId - 1) * BufferBlockSize;
            for (var i = 0; i < BufferPortsPerSide; i++)
                list.Add(new("Buffer", $"Buffer {stationId} A[{i}]", baseAddr + i, ModbusValueKind.Binary));

            list.Add(new("Buffer", $"Buffer {stationId} 作業面", baseAddr + 12, ModbusValueKind.OperationSide));

            for (var i = 0; i < BufferPortsPerSide; i++)
                list.Add(new("Buffer", $"Buffer {stationId} B[{i}]", baseAddr + 20 + i, ModbusValueKind.Binary));
        }
    }

    static void AddMachineStatusRegisters(List<ModbusRegisterDef> list, ParkingCatalogDto parking)
    {
        foreach (var machine in parking.Machines.OrderBy(m => m.MachineId))
            list.Add(new("MachineStatus", $"{machine.MachineCode} 機台狀態", 8000 + machine.MachineId - 1, ModbusValueKind.MachineStatus));
    }

    static void AddSummaryRegisters(List<ModbusRegisterDef> list, ParkingCatalogDto parking)
    {
        foreach (var machine in parking.Machines.OrderBy(m => m.MachineId))
        {
            list.Add(new("Summary", $"{machine.MachineCode} Cake 有絲合計", 9000 + machine.MachineId - 1, ModbusValueKind.Count));
            list.Add(new("Summary", $"{machine.MachineCode} Bobbin 有絲合計", 9100 + machine.MachineId - 1, ModbusValueKind.Count));
        }
    }

    static void AddTwistingDetailRegisters(List<ModbusRegisterDef> list, ParkingCatalogDto parking)
    {
        foreach (var machine in parking.Machines.OrderBy(m => m.MachineId))
        {
            AddSidePorts(list, machine, machine.SideA);
            AddSidePorts(list, machine, machine.SideB);
        }
    }

    static void AddSidePorts(List<ModbusRegisterDef> list, ParkingMachineDto machine, ParkingSideDto side)
    {
        for (var i = 0; i < PortsPerSide; i++)
        {
            list.Add(new("TwistingCake", $"{machine.MachineCode} {side.Side} Cake[{i}]", side.CakeBaseAddress + i, ModbusValueKind.PortStatus));
            list.Add(new("TwistingBobbin", $"{machine.MachineCode} {side.Side} Bobbin[{i}]", side.BobbinBaseAddress + i, ModbusValueKind.PortStatus));
        }
    }

    static void AddClearingRegisters(List<ModbusRegisterDef> list)
    {
        for (var id = 1; id <= ClearingCount; id++)
            list.Add(new("Clearing", $"清軸站 {id}", 18000 + (id - 1) * 10, ModbusValueKind.Binary));
    }

    static void AddPackagingRegisters(List<ModbusRegisterDef> list)
    {
        for (var id = 1; id <= PackagingCount; id++)
            list.Add(new("Packaging", $"AOI 包裝 {id}", 18100 + (id - 1) * 10, ModbusValueKind.Binary));
    }

    static List<ModbusPollBlock> BuildPollBlocks(ParkingCatalogDto parking)
    {
        var blocks = new List<ModbusPollBlock>
        {
            new("Buffer", 1000, BufferCount * BufferBlockSize),
            new("MachineStatus", 8000, MachineCount),
            new("Summary", 9000, MachineCount),
            new("Summary", 9100, MachineCount),
            new("Clearing", 18000, ClearingCount * 10),
            new("Packaging", 18100, PackagingCount * 10)
        };

        foreach (var machine in parking.Machines.OrderBy(m => m.MachineId))
        {
            blocks.Add(new("TwistingCake", machine.SideA.CakeBaseAddress, PortsPerSide));
            blocks.Add(new("TwistingCake", machine.SideB.CakeBaseAddress, PortsPerSide));
            blocks.Add(new("TwistingBobbin", machine.SideA.BobbinBaseAddress, PortsPerSide));
            blocks.Add(new("TwistingBobbin", machine.SideB.BobbinBaseAddress, PortsPerSide));
        }

        return blocks;
    }
}
