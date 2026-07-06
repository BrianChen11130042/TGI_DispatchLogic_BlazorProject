namespace TG_DispatchLogic_Test2.Models;

public record PackagingPortMeta(int PortNumber, int ArmPortNumber, int Address);

public record PackagingStationMeta(
    int StationId,
    string ParkingPointId,
    PackagingPortMeta Port)
{
    public string StationCode => $"AOI 包裝 {StationId}";
}

/// <summary>AOI 包裝：PKP001… · 機台/手臂 Port 皆 1 · Addr 18100+(n-1)*10</summary>
public static class PackagingParkingRegistry
{
    public const int StationCount = 2;

    static readonly Lazy<IReadOnlyList<PackagingStationMeta>> All = new(BuildAll);

    public static IReadOnlyList<PackagingStationMeta> GetAll() => All.Value;

    public static PackagingStationMeta? Get(int stationId) =>
        stationId is >= 1 and <= StationCount ? All.Value[stationId - 1] : null;

    public static string GetParkingPointId(int stationId) => $"PKP{stationId:D3}";

    static IReadOnlyList<PackagingStationMeta> BuildAll() =>
        Enumerable.Range(1, StationCount).Select(id =>
        {
            var addr = 18100 + (id - 1) * 10;
            return new PackagingStationMeta(
                id, GetParkingPointId(id),
                new PackagingPortMeta(1, 1, addr));
        }).ToList();
}
