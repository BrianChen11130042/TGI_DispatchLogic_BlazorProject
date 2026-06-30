using System.Text.Json.Serialization;

namespace TG_DispatchLogic_Test2.Models;

public class WmsCellDto
{
    [JsonPropertyName("cell_id")]
    public string CellId { get; set; } = "";

    [JsonPropertyName("cell_name")]
    public string CellName { get; set; } = "";

    [JsonPropertyName("cell_type")]
    public string CellType { get; set; } = "";

    [JsonPropertyName("has_load")]
    public bool HasLoad { get; set; }

    [JsonPropertyName("material_count")]
    public int MaterialCount { get; set; }

    [JsonPropertyName("capacity")]
    public int Capacity { get; set; }

    [JsonPropertyName("occupied")]
    public bool Occupied { get; set; }

    [JsonPropertyName("x_percent")]
    public double XPercent { get; set; }

    [JsonPropertyName("y_percent")]
    public double YPercent { get; set; }

    [JsonPropertyName("site_group")]
    public string SiteGroup { get; set; } = "";
}
