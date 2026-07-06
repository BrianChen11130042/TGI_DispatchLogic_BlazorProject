using System.Text.Json.Serialization;

namespace TG_DispatchLogic_Test2.Models;

public class TriggerFlowRequest
{
    [JsonPropertyName("args")]
    public TriggerFlowArgs Args { get; set; } = new();
}

public class TriggerFlowArgs
{
    [JsonPropertyName("start_time")]
    public string StartTime { get; set; } = "";

    [JsonPropertyName("end_time")]
    public string EndTime { get; set; } = "";

    [JsonPropertyName("interval")]
    public string Interval { get; set; } = "";

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "3";

    [JsonPropertyName("params")]
    public Dictionary<string, Dictionary<string, string>> Params { get; set; } = new();
}

public class TriggerFlowResultDto
{
    [JsonPropertyName("flow_id")]
    public string FlowId { get; set; } = "";

    [JsonPropertyName("task_id")]
    public string TaskId { get; set; } = "";

    [JsonPropertyName("flow_name")]
    public string FlowName { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonPropertyName("assigned_robot")]
    public string? AssignedRobot { get; set; }
}
