namespace TG_DispatchLogic_Test2.Models;

/// <summary>清軸站可派評估：停車點空、單行道可達、未被 in-flight 鎖定（忽略 Modbus）。</summary>
public record ClearingLoadDispatchEvaluation(
    int StationId,
    string StationCode,
    string ParkingPointId,
    bool IsEmpty,
    bool IsReachable,
    bool IsLocked,
    bool IsDispatchable,
    string Reason,
    string? OccupyingAmrCode);

public record ClearingLoadDispatchPair(
    ClearingLoadDispatchEvaluation Station,
    CakeVehicleDispatchStatus Vehicle,
    TriggerFlowRequest FlowRequest,
    string FlowName,
    string JsonBody);

public record ClearingLoadDispatchInFlight(
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
