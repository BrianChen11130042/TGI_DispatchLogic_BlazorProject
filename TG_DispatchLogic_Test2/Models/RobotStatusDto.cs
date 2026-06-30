using System.Text.Json.Serialization;

namespace TG_DispatchLogic_Test2.Models;

public class RobotStatusDto
{
    [JsonPropertyName("robot_id")]
    public string RobotId { get; set; } = "";

    [JsonPropertyName("robot_name")]
    public string RobotName { get; set; } = "";

    [JsonPropertyName("connection_status")]
    public string ConnectionStatus { get; set; } = "";

    [JsonPropertyName("state")]
    public string State { get; set; } = "";

    [JsonPropertyName("battery")]
    public int Battery { get; set; }

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("current_task_id")]
    public string? CurrentTaskId { get; set; }

    [JsonPropertyName("carrying_material")]
    public bool CarryingMaterial { get; set; }

    [JsonPropertyName("current_site_id")]
    public int? CurrentSiteId { get; set; }

    [JsonPropertyName("target_site_id")]
    public int? TargetSiteId { get; set; }
}
