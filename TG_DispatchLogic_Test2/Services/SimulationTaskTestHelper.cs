using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

public record SimulationTaskKindOption(
    string Value,
    string Label,
    string? FixedAmrType,
    bool NeedsSourceSite,
    bool NeedsTargetSite,
    string[]? SourceCellTypes,
    string[]? TargetCellTypes,
    bool IsCompositeFlow = false,
    string? ApiTaskKind = null)
{
    /// <summary>實際送 AMR API 的 task_kind（純移動至 BUFFER 仍用 MoveToWp）。</summary>
    public string ResolvedApiTaskKind => ApiTaskKind ?? Value;
}

public static class SimulationTaskTestHelper
{
    public static IReadOnlyList<SimulationTaskKindOption> TaskKinds { get; } =
    [
        new("MoveToNearestWp", "移至最近空閒 WP（純移動）", null, false, false, null, null, IsCompositeFlow: true),
        new("MoveToBuffer", "移至指定 BUFFER（純移動）", "Cake", false, true, null, ["Buffer"], ApiTaskKind: "MoveToWp"),
        new("MoveToTwp", "移至指定撚紗機站點（TWP）", null, false, true, null, ["Machine"], ApiTaskKind: "MoveToWp"),
        new("MoveToClr", "移至指定清軸站（CLR）", "Cake", false, true, null, ["AxisCleanClr"], ApiTaskKind: "MoveToWp"),
        new("MoveToPkg", "移至AOI包裝站（PKG）", "Bobbin", false, true, null, ["AoiPkg"], ApiTaskKind: "MoveToWp"),
    ];

    public static SimulationTaskKindOption? FindTaskKind(string? value) =>
        TaskKinds.FirstOrDefault(t => t.Value.Equals(value, StringComparison.OrdinalIgnoreCase));

    public static string InferAmrType(string robotId)
    {
        if (robotId.StartsWith("BOBBIN-", StringComparison.OrdinalIgnoreCase)) return "Bobbin";
        if (robotId.StartsWith("CAKE-", StringComparison.OrdinalIgnoreCase)) return "Cake";
        return "Cake";
    }

    public static bool IsFleetRobot(string robotId) =>
        robotId.StartsWith("CAKE-", StringComparison.OrdinalIgnoreCase)
        || robotId.StartsWith("BOBBIN-", StringComparison.OrdinalIgnoreCase);

    public static IEnumerable<WmsCellDto> FilterSites(
        IEnumerable<WmsCellDto> sites,
        string[]? cellTypes)
    {
        var list = sites
            .Where(s => !string.IsNullOrWhiteSpace(s.CellId))
            .OrderBy(s => CellTypeSortKey(s.CellType))
            .ThenBy(s => s.CellId, StringComparer.OrdinalIgnoreCase);

        if (cellTypes is null || cellTypes.Length == 0)
            return list;

        return list.Where(s => MatchesCellType(s, cellTypes));
    }

    public static string CellTypeLabel(string cellType) => cellType switch
    {
        "Buffer" => "備料站",
        "Waiting" => "等待點",
        "Machine" => "捻紗機",
        "AxisClean" => "清軸站",
        "AxisCleanClr" => "清軸站（CLR）",
        "Aoi" => "AOI 包裝站",
        "AoiPkg" => "AOI 包裝站（PKG）",
        _ => string.IsNullOrWhiteSpace(cellType) ? "其他" : cellType
    };

    static bool MatchesCellType(WmsCellDto cell, string[] cellTypes) =>
        cellTypes.Any(t =>
            cell.CellType.Equals(t, StringComparison.OrdinalIgnoreCase)
            || (t.Equals("Waiting", StringComparison.OrdinalIgnoreCase)
                && cell.CellId.StartsWith("WP", StringComparison.OrdinalIgnoreCase))
            || (t.Equals("Machine", StringComparison.OrdinalIgnoreCase)
                && cell.CellId.StartsWith("TWP", StringComparison.OrdinalIgnoreCase))
            || (t.Equals("AxisClean", StringComparison.OrdinalIgnoreCase)
                && cell.CellId.StartsWith("CLR", StringComparison.OrdinalIgnoreCase))
            || (t.Equals("AxisCleanClr", StringComparison.OrdinalIgnoreCase)
                && cell.CellType.Equals("AxisClean", StringComparison.OrdinalIgnoreCase)
                && cell.CellId.StartsWith("CLR", StringComparison.OrdinalIgnoreCase))
            || (t.Equals("Aoi", StringComparison.OrdinalIgnoreCase)
                && cell.CellId.StartsWith("PKG", StringComparison.OrdinalIgnoreCase))
            || (t.Equals("AoiPkg", StringComparison.OrdinalIgnoreCase)
                && cell.CellType.Equals("Aoi", StringComparison.OrdinalIgnoreCase)
                && cell.CellId.StartsWith("PKG", StringComparison.OrdinalIgnoreCase)));

    static int CellTypeSortKey(string cellType) => cellType switch
    {
        "Buffer" => 0,
        "Waiting" => 1,
        "Machine" => 2,
        "AxisClean" => 3,
        "AxisCleanClr" => 3,
        "Aoi" => 4,
        "AoiPkg" => 4,
        _ => 5
    };
}
