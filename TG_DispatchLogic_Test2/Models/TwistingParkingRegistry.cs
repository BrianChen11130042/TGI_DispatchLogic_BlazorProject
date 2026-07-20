namespace TG_DispatchLogic_Test2.Models;

/// <summary>撚紗機停車點常數（與 SimulateCode 一致，TWP01~TWP37）。</summary>
public static class TwistingParkingRegistry
{
    public const int MachineCount = 35;
    public const int MaxTwpGroupId = 37;
    public const int PortsPerDockingPoint = 4;
    public const int StopsPerLoadMission = 3;
    public const int PortsPerLoadMission = PortsPerDockingPoint * StopsPerLoadMission;
    public const int DockingPointsPerSide = 21;
    public const int CallVehicleStatus = 1;
    public const int RequestUnloadStatus = 4;
    public const int MaxConcurrentMissionsPerSide = 7;

    /// <summary>撚紗下料 Bobbin：每趟 6 停車點 × 4 Port = 24（滿車）。</summary>
    public const int StopsPerBobbinUnloadMission = 6;
    public const int PortsPerBobbinUnloadMission =
        PortsPerDockingPoint * StopsPerBobbinUnloadMission;
    /// <summary>單側 21 點 → 3 組滿車後殘餘 3 點（12 Port），可與對側合成第 7 趟。</summary>
    public const int BobbinRemainderStops = 3;
    public const int MaxBobbinFullMissionsPerSide =
        DockingPointsPerSide / StopsPerBobbinUnloadMission;

    static readonly Lazy<ParkingCatalogDto> Catalog = new(Services.SimulateCodeParkingCatalogBuilder.Build);

    public static ParkingCatalogDto GetCatalog() => Catalog.Value;

    public static ParkingMachineDto? GetMachine(int machineId)
    {
        var catalog = Catalog.Value;
        if (machineId < 1 || machineId > catalog.MachineCount) return null;
        return catalog.Machines[machineId - 1];
    }
}
