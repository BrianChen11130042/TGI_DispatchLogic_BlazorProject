namespace TG_DispatchLogic_Test2.Models;

public record BufferDispatchInFlight(
    string ParkingPointId,
    string AmrCode,
    string TaskId,
    string DispatchedOperationSide,
    DateTime DispatchedAt,
    bool SawRobotBusy,
    bool IsCompleted,
    string StatusHint)
{
    public bool IsActive => !IsCompleted;
}
