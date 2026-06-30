using System.Text.Json.Serialization;

namespace TG_DispatchLogic_Test2.Models;

public class ActiveSimulationTaskDto
{
    [JsonPropertyName("task_no")]
    public string TaskNo { get; set; } = "";

    [JsonPropertyName("simulation_kind")]
    public string SimulationKind { get; set; } = "";

    [JsonPropertyName("source_site_code")]
    public string SourceSiteCode { get; set; } = "";

    [JsonPropertyName("target_site_code")]
    public string TargetSiteCode { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("assigned_amr_code")]
    public string? AssignedAmrCode { get; set; }
}
