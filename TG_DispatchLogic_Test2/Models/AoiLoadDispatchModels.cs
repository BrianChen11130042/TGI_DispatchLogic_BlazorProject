namespace TG_DispatchLogic_Test2.Models;

/// <summary>AOI 包裝站可派評估：停車點空、未被 in-flight 鎖定（忽略 Modbus；兩站可同時派）。</summary>
public record AoiLoadDispatchEvaluation(
    int StationId,
    string StationCode,
    string ParkingPointId,
    bool IsEmpty,
    bool IsLocked,
    bool IsDispatchable,
    string Reason,
    string? OccupyingAmrCode);

public record AoiLoadDispatchPair(
    AoiLoadDispatchEvaluation Station,
    CakeVehicleDispatchStatus Vehicle,
    TriggerFlowRequest FlowRequest,
    string FlowName,
    string JsonBody);

public record AoiLoadDispatchInFlight(
    string ParkingPointId,
    string AmrCode,
    string TaskId,
    DateTime DispatchedAt,
    bool SawRobotBusy,
    bool IsCompleted,
    string StatusHint)
{
    public bool IsActive => !IsCompleted;
}
