namespace TG_DispatchLogic_Test2.Models;

public class EquipSimLiveSnapshot
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public int ElapsedMs { get; init; }
    public DateTime PolledAt { get; init; }

    public IReadOnlyList<BufferLiveStation> Buffers { get; init; } = [];
    public IReadOnlyList<MachineLiveSummary> Machines { get; init; } = [];
    public IReadOnlyList<StationLiveStatus> Clearing { get; init; } = [];
    public IReadOnlyList<StationLiveStatus> Packaging { get; init; } = [];

    public int GetRaw(int address) => _values.TryGetValue(address, out var v) ? v : 0;

    internal Dictionary<int, int> _values = [];
}

public class BufferLiveStation
{
    public int StationId { get; init; }
    public int BaseAddress { get; init; }
    public int OperationSideAddress { get; init; }
    public int OperationSide { get; init; }
    public int[] SideA { get; init; } = new int[12];
    public int[] SideB { get; init; } = new int[12];

    public int PresentCountA => SideA.Count(v => IsPresent(v));
    public int PresentCountB => SideB.Count(v => IsPresent(v));
    public bool IsSideAOperation => OperationSide == 1;
    public bool IsSideBOperation => OperationSide == 2;

    public static bool IsPresent(int v) =>
        v == 1 || v == 256 || (v & 0xFF) == 1 || ((v >> 8) & 0xFF) == 1;
}

public class MachineLiveSummary
{
    public int MachineId { get; init; }
    public string MachineCode { get; init; } = "";
    public int StatusAddress { get; init; }
    public int Status { get; init; }
    public int CakeSummary { get; init; }
    public int BobbinSummary { get; init; }
    public int CakeHasThread { get; init; }
    public int CakeNoThread { get; init; }
    public int CakeEmpty { get; init; }
    public int BobbinHasThread { get; init; }
    public int BobbinNoThread { get; init; }
    public int BobbinEmpty { get; init; }

    public ParkingMachineDto? Detail { get; init; }
}

public class StationLiveStatus
{
    public int StationId { get; init; }
    public int Address { get; init; }
    public int Raw { get; init; }
    public bool IsPresent { get; init; }
}
