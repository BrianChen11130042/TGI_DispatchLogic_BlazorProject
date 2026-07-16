using System.Text.Json;
using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>撚紗下料：撚紗機 → Cake 車（actionid=1），每停車點 4 Port。</summary>
public static class TwistingUnloadFlowDispatchBuilder
{
    public const string FlowName = BufferFlowDispatchBuilder.FlowName;
    public const string NodeId = BufferFlowDispatchBuilder.NodeId;
    public const string ArtifactId = BufferFlowDispatchBuilder.ArtifactId;
    public const int ActionIdTwpToAmr = 1;

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true
    };

    /// <summary>任務內停車點序號 0/1/2 → 車上 Port 1-4 / 5-8 / 9-12。</summary>
    public static IReadOnlyList<int> GetAssignedCakeToPorts(int missionStopIndex)
    {
        var portStart = missionStopIndex * TwistingParkingRegistry.PortsPerDockingPoint + 1;
        return Enumerable.Range(portStart, TwistingParkingRegistry.PortsPerDockingPoint).ToList();
    }

    public static TriggerFlowRequest Build(
        TwistingDockingPointEvaluation stop,
        CakeVehicleDispatchStatus vehicle,
        int missionStopIndex,
        string priority = "3")
    {
        var cakeToPorts = GetAssignedCakeToPorts(missionStopIndex);
        var twpPorts = stop.CakePorts.OrderBy(p => p.PortNumber).ToList();
        var node = new Dictionary<string, string>
        {
            ["goal_amr"] = BufferFlowDispatchBuilder.BuildGoalAmr(stop.ParkingPointId),
            ["assigned_robot"] = BufferFlowDispatchBuilder.FormatAssignedRobot(vehicle.AmrCode),
            ["artifact_id_amr"] = ArtifactId,
            ["value_amr"] = BuildValueAmr(twpPorts, cakeToPorts)
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

    static string BuildValueAmr(
        IReadOnlyList<TwistingPortDispatchStatus> twpPorts,
        IReadOnlyList<int> cakeToPorts)
    {
        var activeRuns = new List<(int ActionId, int FromPort, int ToPort)>(twpPorts.Count);
        for (var i = 0; i < twpPorts.Count && i < cakeToPorts.Count; i++)
            activeRuns.Add((ActionIdTwpToAmr, twpPorts[i].ArmPortNumber, cakeToPorts[i]));

        return BufferFlowDispatchBuilder.BuildValueAmrSlots(activeRuns);
    }
}
