using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

public static class ModbusValueFormatter
{
    public static int NormalizeRaw(ushort raw, ModbusValueKind kind) => kind switch
    {
        ModbusValueKind.Count => SwapBytes(raw),
        _ => raw
    };

    public static string Format(int value, ModbusValueKind kind) => kind switch
    {
        ModbusValueKind.Binary => value switch
        {
            0 => "無 (0)",
            1 => "有 (1)",
            256 => "有 (256, byte-swap)",
            _ when (value & 0xFF) == 1 || ((value >> 8) & 0xFF) == 1 => $"有 ({value})",
            _ => $"無 ({value})"
        },
        ModbusValueKind.OperationSide => value switch
        {
            0 => "未設定 (0)",
            1 => "A 側作業 (1)",
            2 => "B 側作業 (2)",
            _ => $"未知 ({value})"
        },
        ModbusValueKind.PortStatus => value switch
        {
            0 => "空 (0)",
            1 => "無絲 (1)",
            2 => "有絲 (2)",
            9 => "異常 (9)",
            _ => $"未知 ({value})"
        },
        ModbusValueKind.MachineStatus => value switch
        {
            0 => "未設定 (0)",
            1 => "叫車 (1)",
            2 => "請啟動 (2)",
            3 => "撚紗中 (3)",
            4 => "請下料 (4)",
            5 => "空閒 (5)",
            9 => "異常 (9)",
            _ => $"未知 ({value})"
        },
        ModbusValueKind.Count => $"{value}",
        _ => value.ToString()
    };

    static int SwapBytes(ushort v) => ((v & 0xFF) << 8) | (v >> 8);
}
