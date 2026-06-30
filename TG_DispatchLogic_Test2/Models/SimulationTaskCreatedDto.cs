using System.Text.Json.Serialization;

namespace TG_DispatchLogic_Test2.Models;

public class SimulationTaskCreatedDto
{
    [JsonPropertyName("task_no")]
    public string TaskNo { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}
