namespace TG_DispatchLogic_Test2.Models;

public record CakeVehicleDispatchStatus(
    string AmrCode,
    string Status,
    string ConnectionStatus,
    int CarryingCount,
    int CarryingCapacity,
    string? SiteCode,
    bool IsAtWaitingArea,
    bool DispatchEnabled,
    bool IsEligible,
    string Reason,
    IReadOnlyList<CakePortDispatchStatus> Ports,
    DateTime? EligibleSince = null,
    int BatteryPercent = 0);

public record BufferDispatchPair(
    BufferDispatchEvaluation Buffer,
    CakeVehicleDispatchStatus Vehicle,
    TriggerFlowRequest FlowRequest,
    string FlowName,
    string JsonBody);

public record BufferDispatchLogEntry(
    DateTime At,
    string ParkingPointId,
    string AmrCode,
    bool Success,
    string Summary,
    string? JsonBody,
    string? ResponseBody);
