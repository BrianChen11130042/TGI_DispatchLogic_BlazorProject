using System.Text.Json;
using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// 充電派車：POST /v2/flows/charge_mission（params node 6：goal_amr + assigned_robot + percentage_amr）。
/// </summary>
public static class ChargeFlowDispatchBuilder
{
    public const string FlowName = "charge_mission";
    public const string NodeId = "6";

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true
    };

    public static TriggerFlowRequest Build(
        ChargeStationEvaluation station,
        CakeVehicleDispatchStatus vehicle,
        int targetPercent,
        string priority = "3")
    {
        var pct = Math.Clamp(targetPercent, 1, 100);
        var node = new Dictionary<string, string>
        {
            ["goal_amr"] = BufferFlowDispatchBuilder.BuildGoalAmr(station.CellId),
            ["assigned_robot"] = BufferFlowDispatchBuilder.FormatAssignedRobot(vehicle.AmrCode),
            ["percentage_amr"] = pct.ToString()
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
        $"{baseUrl.TrimEnd('/')}/v2/flows/{FlowName}";
}
