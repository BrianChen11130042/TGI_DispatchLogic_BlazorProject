using System.Text.Json;
using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// 清軸上料：Cake 車 → 清軸站（actionid=2），一次 12 Port：fromport_N=N、toport_N=1。
/// </summary>
public static class ClearingLoadFlowDispatchBuilder
{
    public const string FlowName = BufferFlowDispatchBuilder.FlowName;
    public const string NodeId = BufferFlowDispatchBuilder.NodeId;
    public const string ArtifactId = BufferFlowDispatchBuilder.ArtifactId;
    public const int ActionIdAmrToClr = 2;
    public const int PortsPerDispatch = CakeVehicleDispatchEvaluator.DefaultCakeCapacity;
    public const int ClrArmPort = 1;

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true
    };

    public static TriggerFlowRequest Build(
        ClearingLoadDispatchEvaluation station,
        CakeVehicleDispatchStatus vehicle,
        string priority = "3")
    {
        var node = new Dictionary<string, string>
        {
            ["goal_amr"] = BufferFlowDispatchBuilder.BuildGoalAmr(station.ParkingPointId),
            ["assigned_robot"] = BufferFlowDispatchBuilder.FormatAssignedRobot(vehicle.AmrCode),
            ["artifact_id_amr"] = ArtifactId,
            ["value_amr"] = BuildValueAmr()
        };

        return new TriggerFlowRequest
        {
            Args = new TriggerFlowArgs
            {
                Priority = priority,
                Params = new Dictionary<string, Dictionary<string, string>>
                {
                    [NodeId] = node
                }
            }
        };
    }

    public static string Serialize(TriggerFlowRequest request) =>
        JsonSerializer.Serialize(request, JsonOpts);

    public static string BuildEndpoint(string baseUrl) =>
        BufferFlowDispatchBuilder.BuildEndpoint(baseUrl);

    static string BuildValueAmr()
    {
        var activeRuns = new List<(int ActionId, int FromPort, int ToPort)>(PortsPerDispatch);
        for (var n = 1; n <= PortsPerDispatch; n++)
            activeRuns.Add((ActionIdAmrToClr, n, ClrArmPort));

        return BufferFlowDispatchBuilder.BuildValueAmrSlots(activeRuns);
    }
}
