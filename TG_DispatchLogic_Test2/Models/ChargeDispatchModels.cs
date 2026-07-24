namespace TG_DispatchLogic_Test2.Models;

/// <summary>充電站可派評估：未鎖；空站或停靠車已 Idle（可趕走）則可派。</summary>
public record ChargeStationEvaluation(
    string CellId,
    string CellName,
    bool IsEmpty,
    bool IsLocked,
    bool IsDispatchable,
    string Reason,
    string? OccupyingAmrCode);

public record ChargeDispatchPair(
    ChargeStationEvaluation Station,
    CakeVehicleDispatchStatus Vehicle,
    int TargetPercent,
    TriggerFlowRequest FlowRequest,
    string FlowName,
    string JsonBody);

public record ChargeDispatchInFlight(
    string ParkingPointId,
    string AmrCode,
    string TaskId,
    int TargetPercent,
    DateTime DispatchedAt,
    bool SawRobotBusy,
    bool IsCompleted,
    string StatusHint)
{
    public bool IsActive => !IsCompleted;
}
