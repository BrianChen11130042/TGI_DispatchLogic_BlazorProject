namespace TG_DispatchLogic_Test2.Models;

/// <summary>SimulateCode 共用停車點對應機台。</summary>
public record SharedDockingPartnerDto(
    int MachineId,
    char Side,
    string ParkingPointId);

/// <summary>SimulateCode GET /api/parking/twm 回傳結構（與 SimulateCode 專案一致）。</summary>
public record ParkingPortDto(
    int PortNumber,
    int ArmPortNumber,
    int CakeAddress,
    int BobbinAddress);

public record ParkingPointDto(
    int MachineId,
    string MachineCode,
    char Side,
    string ParkingPointId,
    int DNumber,
    int Sequence,
    SharedDockingPartnerDto? SharedWith,
    IReadOnlyList<ParkingPortDto> Ports);

public record ParkingSideDto(
    int MachineId,
    string MachineCode,
    char Side,
    int TwpGroupId,
    int PortNumberStart,
    int PortNumberEnd,
    int ArmPortNumberStart,
    int ArmPortNumberEnd,
    int CakeBaseAddress,
    int CakeAddressEnd,
    int BobbinBaseAddress,
    int BobbinAddressEnd,
    SharedDockingPartnerDto? SharedWith,
    IReadOnlyList<ParkingPointDto> ParkingPoints);

public record ParkingMachineDto(
    int MachineId,
    string MachineCode,
    ParkingSideDto SideA,
    ParkingSideDto SideB,
    int StatusAddress = 0,
    int CakeSummaryAddress = 0,
    int BobbinSummaryAddress = 0);

public record ParkingCatalogDto(
    string Version,
    DateTime GeneratedAt,
    int MachineCount,
    string Description,
    IReadOnlyList<ParkingMachineDto> Machines);

public record BufferPortDto(
    int PortNumber,
    int ArmPortNumber,
    int Address);

public record BufferSideDto(
    int StationId,
    string StationCode,
    char Side,
    string ParkingPointId,
    int PortNumberStart,
    int PortNumberEnd,
    int ArmPortNumberStart,
    int ArmPortNumberEnd,
    int CakeBaseAddress,
    int CakeAddressEnd,
    IReadOnlyList<BufferPortDto> Ports);

public record BufferStationDto(
    int StationId,
    string StationCode,
    string ParkingPointId,
    int BaseAddress,
    int OperationSideAddress,
    BufferSideDto SideA,
    BufferSideDto SideB);

public record BufferCatalogDto(
    string Version,
    DateTime GeneratedAt,
    int StationCount,
    string Description,
    IReadOnlyList<BufferStationDto> Stations);

public record ClearingPortDto(
    int PortNumber,
    int ArmPortNumber,
    int Address);

public record ClearingStationDto(
    int StationId,
    string StationCode,
    string ParkingPointId,
    ClearingPortDto Port);

public record ClearingCatalogDto(
    string Version,
    DateTime GeneratedAt,
    int StationCount,
    string Description,
    IReadOnlyList<ClearingStationDto> Stations);

public record PackagingPortDto(
    int PortNumber,
    int ArmPortNumber,
    int Address);

public record PackagingStationDto(
    int StationId,
    string StationCode,
    string ParkingPointId,
    PackagingPortDto Port);

public record PackagingCatalogDto(
    string Version,
    DateTime GeneratedAt,
    int StationCount,
    string Description,
    IReadOnlyList<PackagingStationDto> Stations);
