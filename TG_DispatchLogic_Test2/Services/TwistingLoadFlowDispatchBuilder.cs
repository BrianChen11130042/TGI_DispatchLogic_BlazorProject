using System.Text.Json;
using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>撚紗上料：車 → 撚紗機（actionid=3），每停車點 4 Port。</summary>
public static class TwistingLoadFlowDispatchBuilder
{
    public const string FlowName = BufferFlowDispatchBuilder.FlowName;
    public const string NodeId = BufferFlowDispatchBuilder.NodeId;
    public const string ArtifactId = "twp_load_mission";
    public const int ActionIdAmrToTwp = 3;

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true
    };

    /// <summary>任務內停車點序號 0/1/2 → 車上 Port 1-4 / 5-8 / 9-12（配對時固定，派車時不重讀 Modbus）。</summary>
    public static IReadOnlyList<int> GetAssignedCakeFromPorts(int missionStopIndex)
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
        var cakeFromPorts = GetAssignedCakeFromPorts(missionStopIndex);
        var twpPorts = stop.CakePorts.OrderBy(p => p.PortNumber).ToList();
        var node = new Dictionary<string, string>
        {
            ["goal_amr"] = BufferFlowDispatchBuilder.BuildGoalAmr(stop.ParkingPointId),
            ["assigned_robot"] = BufferFlowDispatchBuilder.FormatAssignedRobot(vehicle.AmrCode),
            ["artifact_id_amr"] = ArtifactId,
            ["value_amr"] = BuildValueAmr(twpPorts, cakeFromPorts)
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
        IReadOnlyList<int> cakeFromPorts)
    {
        var activeRuns = new List<(int ActionId, int FromPort, int ToPort)>(twpPorts.Count);
        for (var i = 0; i < twpPorts.Count && i < cakeFromPorts.Count; i++)
            activeRuns.Add((ActionIdAmrToTwp, cakeFromPorts[i], twpPorts[i].ArmPortNumber));

        return BufferFlowDispatchBuilder.BuildValueAmrSlots(activeRuns);
    }
}
