using System.Text.Json.Serialization;

namespace TG_DispatchLogic_Test2.Models;

/// <summary>GET /api/simulation/amrs 回應（TGI-AMR-System SimulationController）。</summary>
public class SimulationAmrDto
{
    [JsonPropertyName("amr_code")]
    public string AmrCode { get; set; } = "";

    [JsonPropertyName("amr_type")]
    public string AmrType { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("battery_percent")]
    public double BatteryPercent { get; set; }

    [JsonPropertyName("carrying_count")]
    public int CarryingCount { get; set; }

    [JsonPropertyName("carrying_capacity")]
    public int CarryingCapacity { get; set; }

    [JsonPropertyName("dispatch_enabled")]
    public bool DispatchEnabled { get; set; }

    [JsonPropertyName("site_code")]
    public string? SiteCode { get; set; }
}
