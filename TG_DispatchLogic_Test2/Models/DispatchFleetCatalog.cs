namespace TG_DispatchLogic_Test2.Models;

/// <summary>Buffer / 撚紗派車可使用的 Cake 車號。</summary>
public static class DispatchFleetCatalog
{
    public const int CakeVehicleCount = 12;

    public static readonly IReadOnlyList<string> CakeVehicleCodes =
        Enumerable.Range(1, CakeVehicleCount).Select(i => $"CAKE-{i:D2}").ToList();
}
