namespace TG_DispatchLogic_Test2.Models;

public record BufferPortMeta(
    int PortIndex,
    int PortNumber,
    int ArmPortNumber,
    int Address);

public record BufferSideMeta(
    int StationId,
    char Side,
    string ParkingPointId,
    int BaseAddress,
    int AddressEnd,
    IReadOnlyList<BufferPortMeta> Ports);

public record BufferStationMeta(
    int StationId,
    string ParkingPointId,
    int BaseAddress,
    int OperationSideAddress,
    BufferSideMeta SideA,
    BufferSideMeta SideB)
{
    public string StationCode => $"Buffer {StationId}";
}

/// <summary>Buffer 停車點：BUF001… · 機台/手臂 Port 皆 1~12 · Modbus 與 SimulateCode 一致。</summary>
public static class BufferParkingRegistry
{
    public const int StationCount = 5;
    public const int PortsPerSide = 12;

    static readonly Lazy<IReadOnlyList<BufferStationMeta>> All = new(BuildAll);

    public static IReadOnlyList<BufferStationMeta> GetAll() => All.Value;

    public static BufferStationMeta? Get(int stationId) =>
        stationId is >= 1 and <= StationCount ? All.Value[stationId - 1] : null;

    public static string GetParkingPointId(int stationId) => $"BUF{stationId:D3}";

    static IReadOnlyList<BufferStationMeta> BuildAll() =>
        Enumerable.Range(1, StationCount).Select(BuildStation).ToList();

    static BufferSideMeta BuildSide(int stationId, char side)
    {
        var baseAddr = 1000 + (stationId - 1) * 40;
        var sideBase = side == 'A' ? baseAddr : baseAddr + 20;
        var ports = Enumerable.Range(0, PortsPerSide).Select(i =>
        {
            var n = i + 1;
            return new BufferPortMeta(i, n, n, sideBase + i);
        }).ToList();

        return new BufferSideMeta(
            stationId, side, GetParkingPointId(stationId),
            sideBase, sideBase + PortsPerSide - 1, ports);
    }

    static BufferStationMeta BuildStation(int stationId)
    {
        var baseAddr = 1000 + (stationId - 1) * 40;
        return new BufferStationMeta(
            stationId, GetParkingPointId(stationId), baseAddr, baseAddr + 12,
            BuildSide(stationId, 'A'), BuildSide(stationId, 'B'));
    }
}
