using System.Text.Json;
using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// 組裝 TGI POST /v2/flows/amr_mission（goal_amr + value_amr，對齊 flow_0702.txt）。
/// </summary>
public static class BufferFlowDispatchBuilder
{
    public const string FlowName = "amr_mission";
    public const string NodeId = "4";
    public const string GoalMapPrefix = "p17";
    public const string GoalArea = "default_area";
    public const string ArtifactId = "cake_mission";
    public const int ActionIdBufferToAmr = 4;
    public const int PortsPerDispatch = BufferParkingRegistry.PortsPerSide;
    /// <summary>value_amr 固定帶 run_1~run_12；未使用的 run 填 0。</summary>
    public const int MaxArmRuns = PortsPerDispatch;

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = null,
        WriteIndented = true
    };

    public static TriggerFlowRequest Build(
        BufferDispatchEvaluation buffer,
        CakeVehicleDispatchStatus vehicle,
        string priority = "3")
    {
        var bufferPorts = buffer.OperationSidePorts
            .OrderBy(p => p.PortNumber)
            .Take(PortsPerDispatch)
            .ToList();
        var cakePorts = ResolveCakeToports(vehicle, bufferPorts.Count);

        var node = new Dictionary<string, string>
        {
            ["goal_amr"] = BuildGoalAmr(buffer.ParkingPointId),
            ["assigned_robot"] = FormatAssignedRobot(vehicle.AmrCode),
            ["artifact_id_amr"] = ArtifactId,
            ["value_amr"] = BuildValueAmr(bufferPorts, cakePorts)
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

    public static string BuildGoalAmr(string parkingPointId) =>
        $"{GoalMapPrefix}@{GoalArea}@{parkingPointId.Trim()}";

    public static string FormatAssignedRobot(string amrCode)
    {
        if (amrCode.StartsWith("CAKE-", StringComparison.OrdinalIgnoreCase))
            return "Cake-" + amrCode[5..];
        if (amrCode.StartsWith("BOBBIN-", StringComparison.OrdinalIgnoreCase))
            return "Bobbin-" + amrCode[7..];
        return amrCode;
    }

    static IReadOnlyList<int> ResolveCakeToports(CakeVehicleDispatchStatus vehicle, int count)
    {
        var empty = vehicle.Ports
            .Where(p => p.RawValue == 0)
            .OrderBy(p => p.PortNumber)
            .Select(p => p.PortNumber)
            .Take(count)
            .ToList();

        if (empty.Count >= count)
            return empty;

        return Enumerable.Range(1, count).ToList();
    }

    static string BuildValueAmr(
        IReadOnlyList<BufferPortDispatchStatus> bufferPorts,
        IReadOnlyList<int> cakeToports)
    {
        var activeRuns = new List<(int ActionId, int FromPort, int ToPort)>(bufferPorts.Count);
        for (var i = 0; i < bufferPorts.Count; i++)
        {
            activeRuns.Add((ActionIdBufferToAmr, bufferPorts[i].ArmPortNumber, cakeToports[i]));
        }
        return BuildValueAmrSlots(activeRuns);
    }

    /// <summary>組裝 value_amr：前 N 組為實際動作，其餘 run 填 0（預設 12；Bobbin 可傳 24）。</summary>
    public static string BuildValueAmrSlots(
        IReadOnlyList<(int ActionId, int FromPort, int ToPort)> activeRuns,
        int maxArmRuns = MaxArmRuns)
    {
        if (maxArmRuns <= 0)
            maxArmRuns = MaxArmRuns;

        var parts = new List<string>(maxArmRuns * 4);
        for (var n = 1; n <= maxArmRuns; n++)
        {
            var i = n - 1;
            if (i < activeRuns.Count)
            {
                var run = activeRuns[i];
                parts.Add($"run_{n}: 1");
                parts.Add($"actionid_{n}: {run.ActionId}");
                parts.Add($"fromport_{n}: {run.FromPort}");
                parts.Add($"toport_{n}: {run.ToPort}");
            }
            else
            {
                parts.Add($"run_{n}: 0");
                parts.Add($"actionid_{n}: 0");
                parts.Add($"fromport_{n}: 0");
                parts.Add($"toport_{n}: 0");
            }
        }
        return string.Join(",", parts);
    }
}
