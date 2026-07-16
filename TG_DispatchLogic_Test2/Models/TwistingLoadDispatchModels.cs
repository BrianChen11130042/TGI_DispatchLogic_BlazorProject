namespace TG_DispatchLogic_Test2.Models;

public record TwistingPortDispatchStatus(
    int PortNumber,
    int ArmPortNumber,
    int CakeAddress,
    int RawValue,
    string Label,
    bool NeedsCakeLoad,
    bool NeedsCakeUnload = false);

public record TwistingDockingPointEvaluation(
    string ParkingPointId,
    int DNumber,
    int Sequence,
    bool IsDispatchable,
    string Reason,
    IReadOnlyList<TwistingPortDispatchStatus> CakePorts);

public record TwistingLoadMachineEvaluation(
    int MachineId,
    string MachineCode,
    char Side,
    int TwpGroupId,
    int Status,
    string StatusLabel,
    bool IsDispatchable,
    string Reason,
    IReadOnlyList<TwistingDockingPointEvaluation> DockingPoints,
    IReadOnlyList<IReadOnlyList<TwistingDockingPointEvaluation>> AvailableMissions,
    IReadOnlyList<TwistingDockingPointEvaluation> MissionStops)
{
    public string MissionKey => $"{MachineCode}-{Side}";
}

public record TwistingLoadDispatchPair(
    TwistingLoadMachineEvaluation Machine,
    IReadOnlyList<TwistingDockingPointEvaluation> MissionStops,
    CakeVehicleDispatchStatus Vehicle,
    IReadOnlyList<TwistingLoadFlowStop> Stops,
    string FlowName,
    bool CanDispatch = true,
    string? DispatchBlockReason = null);

public record TwistingLoadFlowStop(
    int StopIndex,
    string ParkingPointId,
    TriggerFlowRequest FlowRequest,
    string JsonBody);

public record TwistingLoadInFlight(
    string SequenceId,
    int MachineId,
    char Side,
    string MachineCode,
    string AmrCode,
    int TwpGroupId,
    int LaneBlockIndex,
    IReadOnlyList<string> ParkingPointIds,
    IReadOnlyList<TwistingLoadFlowStop> FlowStops,
    IReadOnlyList<string> TaskIds,
    DateTime DispatchedAt,
    DateTime? LastFlowDispatchedAt,
    DateTime? NextRetryAt,
    int CompletedStops,
    int TotalFlows,
    bool SawRobotBusy,
    bool HasEnteredLane,
    bool IsCompleted,
    string StatusHint)
{
    public string MissionKey => $"{MachineCode}-{Side}";
    public bool IsActive => !IsCompleted;
    public bool IsFullyDispatched => CompletedStops >= TotalFlows;
}
