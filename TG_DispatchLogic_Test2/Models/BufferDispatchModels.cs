namespace TG_DispatchLogic_Test2.Models;

public record BufferPortDispatchStatus(
    int PortNumber,
    int ArmPortNumber,
    int Address,
    bool HasMaterial,
    int RawValue);

public record CakePortDispatchStatus(
    int PortNumber,
    ushort RawValue,
    string Label);

public record BufferDispatchEvaluation(
    int StationId,
    string StationCode,
    string ParkingPointId,
    bool IsDispatchable,
    string Reason,
    string OperationSideLabel,
    bool HasOperationSide,
    bool IsOperationSideA,
    int OperationSidePresentCount,
    int OperationSidePortCount,
    IReadOnlyList<BufferPortDispatchStatus> OperationSidePorts,
    int SideAPresentCount,
    int SideBPresentCount);
