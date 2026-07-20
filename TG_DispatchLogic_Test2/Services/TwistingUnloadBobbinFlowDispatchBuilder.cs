using System.Text.Json;
using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>撚紗下料：撚紗機 → Bobbin 車（actionid=1），每停車點 4 Port；value_amr 槽位 1~24。</summary>
public static class TwistingUnloadBobbinFlowDispatchBuilder
{
    public const string FlowName = BufferFlowDispatchBuilder.FlowName;
    /// <summary>與 API 範例一致：params 節點 "5"。</summary>
    public const string NodeId = "5";
    public const string ArtifactId = "bobbin_mission";
    public const int ActionIdTwpToAmr = 1;
    public const int MaxArmRuns = TwistingParkingRegistry.PortsPerBobbinUnloadMission;

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true
    };

    /// <summary>任務內停車點序號 0~5 → 車上 Port 1-4 / 5-8 / … / 21-24。</summary>
    public static IReadOnlyList<int> GetAssignedBobbinToPorts(int missionStopIndex)
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
        var bobbinToPorts = GetAssignedBobbinToPorts(missionStopIndex);
        var twpPorts = stop.CakePorts.OrderBy(p => p.PortNumber).ToList();
        var node = new Dictionary<string, string>
        {
            ["goal_amr"] = BufferFlowDispatchBuilder.BuildGoalAmr(stop.ParkingPointId),
            ["assigned_robot"] = BufferFlowDispatchBuilder.FormatAssignedRobot(vehicle.AmrCode),
            ["artifact_id_amr"] = ArtifactId,
            ["value_amr"] = BuildValueAmr(twpPorts, bobbinToPorts)
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
        IReadOnlyList<int> bobbinToPorts)
    {
        var activeRuns = new List<(int ActionId, int FromPort, int ToPort)>(twpPorts.Count);
        for (var i = 0; i < twpPorts.Count && i < bobbinToPorts.Count; i++)
            activeRuns.Add((ActionIdTwpToAmr, twpPorts[i].ArmPortNumber, bobbinToPorts[i]));

        return BufferFlowDispatchBuilder.BuildValueAmrSlots(activeRuns, MaxArmRuns);
    }
}
