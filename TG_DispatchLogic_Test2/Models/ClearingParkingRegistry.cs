namespace TG_DispatchLogic_Test2.Models;

public record ClearingPortMeta(int PortNumber, int ArmPortNumber, int Address);

public record ClearingStationMeta(
    int StationId,
    string ParkingPointId,
    ClearingPortMeta Port)
{
    public string StationCode => $"清軸 {StationId}";
}

/// <summary>清軸：CLR001… · 機台/手臂 Port 皆 1 · Addr 18000+(n-1)*10</summary>
public static class ClearingParkingRegistry
{
    public const int StationCount = 5;

    static readonly Lazy<IReadOnlyList<ClearingStationMeta>> All = new(BuildAll);

    public static IReadOnlyList<ClearingStationMeta> GetAll() => All.Value;

    public static ClearingStationMeta? Get(int stationId) =>
        stationId is >= 1 and <= StationCount ? All.Value[stationId - 1] : null;

    public static string GetParkingPointId(int stationId) => $"CLR{stationId:D3}";

    static IReadOnlyList<ClearingStationMeta> BuildAll() =>
        Enumerable.Range(1, StationCount).Select(id =>
        {
            var addr = 18000 + (id - 1) * 10;
            return new ClearingStationMeta(
                id, GetParkingPointId(id),
                new ClearingPortMeta(1, 1, addr));
        }).ToList();
}
