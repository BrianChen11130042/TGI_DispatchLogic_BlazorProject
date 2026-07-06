namespace TG_DispatchLogic_Test2.Models;

public enum ModbusValueKind
{
    Binary,
    OperationSide,
    PortStatus,
    MachineStatus,
    Count
}

public record ModbusRegisterDef(
    string Category,
    string Label,
    int Address,
    ModbusValueKind ValueKind);

public record ModbusPollBlock(
    string Category,
    int StartAddress,
    int Count);

public class ModbusEquipCatalog
{
    public ParkingCatalogDto? Parking { get; init; }
    public IReadOnlyList<ModbusRegisterDef> Registers { get; init; } = [];
    public IReadOnlyList<ModbusPollBlock> PollBlocks { get; init; } = [];
}

public record ModbusRegisterRow(
    string Category,
    string Label,
    int Address,
    int RawValue,
    string DisplayValue);

public class ModbusPollResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public int ElapsedMs { get; init; }
    public DateTime PolledAt { get; init; }
    public IReadOnlyList<ModbusRegisterRow> Rows { get; init; } = [];
    public IReadOnlyDictionary<int, int> ValuesByAddress { get; init; }
        = new Dictionary<int, int>();
}
