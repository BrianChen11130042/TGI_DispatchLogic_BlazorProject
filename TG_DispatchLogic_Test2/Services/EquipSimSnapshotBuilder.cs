using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

public static class EquipSimSnapshotBuilder
{
    const int PortsPerSide = 84;

    public static EquipSimLiveSnapshot Build(
        ModbusEquipCatalog catalog,
        IReadOnlyDictionary<int, int> values,
        bool success,
        string? error,
        int elapsedMs,
        DateTime polledAt)
    {
        var dict = values is Dictionary<int, int> d ? d : new Dictionary<int, int>(values);
        var parking = catalog.Parking!;

        var buffers = Enumerable.Range(1, 5).Select(id =>
        {
            var baseAddr = 1000 + (id - 1) * 40;
            return new BufferLiveStation
            {
                StationId = id,
                BaseAddress = baseAddr,
                OperationSideAddress = baseAddr + 12,
                OperationSide = NormalizeOpSide(dict.GetValueOrDefault(baseAddr + 12)),
                SideA = Enumerable.Range(0, 12).Select(i => dict.GetValueOrDefault(baseAddr + i)).ToArray(),
                SideB = Enumerable.Range(0, 12).Select(i => dict.GetValueOrDefault(baseAddr + 20 + i)).ToArray()
            };
        }).ToList();

        var machines = parking.Machines.OrderBy(m => m.MachineId).Select(m =>
        {
            var cakeA = m.SideA.CakeBaseAddress;
            var cakeB = m.SideB.CakeBaseAddress;
            var bobA = m.SideA.BobbinBaseAddress;
            var bobB = m.SideB.BobbinBaseAddress;

            return new MachineLiveSummary
            {
                MachineId = m.MachineId,
                MachineCode = m.MachineCode,
                StatusAddress = 8000 + m.MachineId - 1,
                Status = dict.GetValueOrDefault(8000 + m.MachineId - 1),
                CakeSummary = ModbusValueFormatter.NormalizeRaw(
                    (ushort)dict.GetValueOrDefault(9000 + m.MachineId - 1), ModbusValueKind.Count),
                BobbinSummary = ModbusValueFormatter.NormalizeRaw(
                    (ushort)dict.GetValueOrDefault(9100 + m.MachineId - 1), ModbusValueKind.Count),
                CakeHasThread = CountStatus(dict, cakeA, PortsPerSide, 2)
                                + CountStatus(dict, cakeB, PortsPerSide, 2),
                CakeNoThread = CountStatus(dict, cakeA, PortsPerSide, 1)
                               + CountStatus(dict, cakeB, PortsPerSide, 1),
                CakeEmpty = CountStatus(dict, cakeA, PortsPerSide, 0)
                            + CountStatus(dict, cakeB, PortsPerSide, 0),
                BobbinHasThread = CountStatus(dict, bobA, PortsPerSide, 2)
                                  + CountStatus(dict, bobB, PortsPerSide, 2),
                BobbinNoThread = CountStatus(dict, bobA, PortsPerSide, 1)
                                 + CountStatus(dict, bobB, PortsPerSide, 1),
                BobbinEmpty = CountStatus(dict, bobA, PortsPerSide, 0)
                              + CountStatus(dict, bobB, PortsPerSide, 0),
                Detail = m
            };
        }).ToList();

        var clearing = Enumerable.Range(1, 5).Select(id =>
        {
            var addr = 18000 + (id - 1) * 10;
            var raw = dict.GetValueOrDefault(addr);
            return new StationLiveStatus
            {
                StationId = id,
                Address = addr,
                Raw = raw,
                IsPresent = BufferLiveStation.IsPresent(raw)
            };
        }).ToList();

        var packaging = Enumerable.Range(1, 2).Select(id =>
        {
            var addr = 18100 + (id - 1) * 10;
            var raw = dict.GetValueOrDefault(addr);
            return new StationLiveStatus
            {
                StationId = id,
                Address = addr,
                Raw = raw,
                IsPresent = BufferLiveStation.IsPresent(raw)
            };
        }).ToList();

        return new EquipSimLiveSnapshot
        {
            Success = success,
            Error = error,
            ElapsedMs = elapsedMs,
            PolledAt = polledAt,
            Buffers = buffers,
            Machines = machines,
            Clearing = clearing,
            Packaging = packaging,
            _values = dict
        };
    }

    static int NormalizeOpSide(int raw) => raw is 1 or 2 ? raw : 0;

    static int CountStatus(IReadOnlyDictionary<int, int> values, int baseAddr, int count, int status)
    {
        var n = 0;
        for (var i = 0; i < count; i++)
            if (values.GetValueOrDefault(baseAddr + i) == status) n++;
        return n;
    }
}
