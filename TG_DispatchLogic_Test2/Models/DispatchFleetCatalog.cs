namespace TG_DispatchLogic_Test2.Models;

/// <summary>各派車分頁使用的車號區段（之後擴充 Bobbin 等時在此加列）。</summary>
public static class DispatchFleetCatalog
{
    public const int CakeVehicleCountPerFleet = 12;

    /// <summary>Buffer / 撚紗上料：CAKE-01 ~ CAKE-12。</summary>
    public static readonly IReadOnlyList<string> CakeVehicleCodes =
        Enumerable.Range(1, CakeVehicleCountPerFleet).Select(i => $"CAKE-{i:D2}").ToList();

    /// <summary>撚紗下料派 Cake 車：CAKE-13 ~ CAKE-24。</summary>
    public static readonly IReadOnlyList<string> TwistingUnloadCakeVehicleCodes =
        Enumerable.Range(13, CakeVehicleCountPerFleet).Select(i => $"CAKE-{i:D2}").ToList();

    /// <summary>清軸上料派 Cake 車：與撚紗下料相同區段 CAKE-13 ~ CAKE-24。</summary>
    public static readonly IReadOnlyList<string> ClearingLoadCakeVehicleCodes =
        TwistingUnloadCakeVehicleCodes;

    public const int BobbinVehicleCountPerFleet = 8;

    /// <summary>撚紗下料派 Bobbin 車：BOBBIN-01 ~ BOBBIN-08。</summary>
    public static readonly IReadOnlyList<string> TwistingUnloadBobbinVehicleCodes =
        Enumerable.Range(1, BobbinVehicleCountPerFleet).Select(i => $"BOBBIN-{i:D2}").ToList();

    /// <summary>AOI 上料派 Bobbin 車：與撚紗下料相同區段 BOBBIN-01 ~ BOBBIN-08。</summary>
    public static readonly IReadOnlyList<string> AoiLoadBobbinVehicleCodes =
        TwistingUnloadBobbinVehicleCodes;

    /// <summary>派車分頁 ↔ 車種 ↔ 車號對照（UI 表格用，可繼續往下加）。</summary>
    public static readonly IReadOnlyList<DispatchFleetAssignment> Assignments =
    [
        new("Buffer 區派Cake車", "Cake", FormatRange(CakeVehicleCodes), CakeVehicleCodes),
        new("撚紗上料派Cake車", "Cake", FormatRange(CakeVehicleCodes), CakeVehicleCodes),
        new("撚紗下料派Cake車", "Cake", FormatRange(TwistingUnloadCakeVehicleCodes), TwistingUnloadCakeVehicleCodes),
        new("撚紗下料派Bobbin車", "Bobbin", FormatRange(TwistingUnloadBobbinVehicleCodes), TwistingUnloadBobbinVehicleCodes),
        new("清軸上料派Cake車", "Cake", FormatRange(ClearingLoadCakeVehicleCodes), ClearingLoadCakeVehicleCodes),
        new("AOI上料派Bobbin車", "Bobbin", FormatRange(AoiLoadBobbinVehicleCodes), AoiLoadBobbinVehicleCodes),
    ];

    static string FormatRange(IReadOnlyList<string> codes) =>
        codes.Count == 0 ? "—" :
        codes.Count == 1 ? codes[0] :
        $"{codes[0]} ~ {codes[^1]}";
}

/// <summary>單一派車動作對應的車隊區段。</summary>
public record DispatchFleetAssignment(
    string DispatchAction,
    string VehicleType,
    string VehicleRangeLabel,
    IReadOnlyList<string> VehicleCodes);
