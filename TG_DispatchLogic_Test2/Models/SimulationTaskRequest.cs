using System.Text.Json.Serialization;

namespace TG_DispatchLogic_Test2.Models;

public class SimulationTaskRequest
{
    [JsonPropertyName("task_kind")]
    public string TaskKind { get; set; } = "MoveToWp";

    [JsonPropertyName("required_amr_type")]
    public string RequiredAmrType { get; set; } = "";

    [JsonPropertyName("source_site_code")]
    public string SourceSiteCode { get; set; } = "";

    [JsonPropertyName("target_site_code")]
    public string TargetSiteCode { get; set; } = "";

    [JsonPropertyName("priority")]
    public int Priority { get; set; } = 5;

    [JsonPropertyName("trigger_event")]
    public string TriggerEvent { get; set; } = "fleet-init";

    [JsonPropertyName("assigned_robot")]
    public string AssignedRobot { get; set; } = "";
}
