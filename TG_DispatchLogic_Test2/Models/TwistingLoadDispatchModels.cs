namespace TG_DispatchLogic_Test2.Models;

public record TwistingPortDispatchStatus(
    int PortNumber,
    int ArmPortNumber,
    int CakeAddress,
    int RawValue,
    string Label,
    bool NeedsCakeLoad,
    bool NeedsCakeUnload = false,
    bool NeedsBobbinUnload = false);

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
    IReadOnlyList<TwistingDockingPointEvaluation> MissionStops,
    IReadOnlyList<TwistingDockingPointEvaluation>? RemainderStops = null)
{
    public string MissionKey => Side is 'X' or 'x'
        ? $"{MachineCode}-AB"
        : $"{MachineCode}-{Side}";

    /// <summary>單側已無完整 6 停組，且僅殘餘 3 停（seq 1~3）就緒。</summary>
    public bool HasBobbinRemainderOnly =>
        AvailableMissions.Count == 0 &&
        RemainderStops is { Count: TwistingParkingRegistry.BobbinRemainderStops };
}

public record TwistingLoadDispatchPair(
    TwistingLoadMachineEvaluation Machine,
    IReadOnlyList<TwistingDockingPointEvaluation> MissionStops,
    CakeVehicleDispatchStatus Vehicle,
    IReadOnlyList<TwistingLoadFlowStop> Stops,
    string FlowName,
    bool CanDispatch = true,
    string? DispatchBlockReason = null,
    /// <summary>跨側第 7 趟：對側機台評估（B），用於雙走道 lane admission。</summary>
    TwistingLoadMachineEvaluation? SecondaryMachine = null)
{
    public bool IsCrossSide => SecondaryMachine is not null;
    public int? SecondaryTwpGroupId => SecondaryMachine?.TwpGroupId;
}

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
    string StatusHint,
    int? SecondaryTwpGroupId = null)
{
    public string MissionKey => Side is 'X' or 'x'
        ? $"{MachineCode}-AB"
        : $"{MachineCode}-{Side}";
    public bool IsActive => !IsCompleted;
    public bool IsFullyDispatched => CompletedStops >= TotalFlows;
    public bool IsCrossSide => SecondaryTwpGroupId is not null;
}
