namespace TG_DispatchLogic_Test2.Services;

public class EquipSimOptions
{
    public const string SectionName = "EquipSim";

    public string DefaultModbusHost { get; set; } = "172.25.91.143";
    public int DefaultModbusPort { get; set; } = 5020;
    public int DefaultUnitId { get; set; } = 1;

    /// <summary>SimulateCode Web API（停車對照 GET /api/parking/twm|buf|clr|pkg）</summary>
    public string SimulateCodeApiBaseUrl { get; set; } = "http://172.25.91.143:5189";
}
