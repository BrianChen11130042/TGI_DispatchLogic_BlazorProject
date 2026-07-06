namespace TG_DispatchLogic_Test2.Services;

using TG_DispatchLogic_Test2.Models;

public static class EquipSimUiHelper
{
    public static string PortCss(int status) => status switch
    {
        2 => "equip-port-has-thread",
        1 => "equip-port-no-thread",
        9 => "equip-port-error",
        _ => "equip-port-empty"
    };

    public static string PortLabel(int status) => status switch
    {
        0 => "空",
        1 => "無絲",
        2 => "有絲",
        9 => "異常",
        _ => $"未知({status})"
    };

    public static string PortShort(int status) => status switch
    {
        0 => "空",
        1 => "無",
        2 => "有",
        9 => "!",
        _ => "?"
    };

    public static string BufferLabel(int raw) =>
        BufferLiveStation.IsPresent(raw) ? "有" : "無";

    public static string BufferCss(int raw) =>
        BufferLiveStation.IsPresent(raw) ? "equip-port-present" : "equip-port-absent";

    public static string MachineStatusLabel(int status) => status switch
    {
        1 => "叫車",
        2 => "請啟動",
        3 => "撚紗中",
        4 => "請下料",
        5 => "空閒",
        9 => "異常",
        _ => $"?({status})"
    };

    public static string MachineStatusShort(int status) => status switch
    {
        1 => "1-叫車",
        2 => "2-請啟動",
        3 => "3-撚紗中",
        4 => "4-請下料",
        5 => "5-空閒",
        9 => "9-異常",
        _ => $"{status}"
    };

    public static string MachineStatusBadgeCss(int status) => status switch
    {
        1 => "equip-badge-call",
        2 => "equip-badge-start",
        3 => "equip-badge-run",
        4 => "equip-badge-unload",
        5 => "equip-badge-idle",
        9 => "equip-badge-error",
        _ => "equip-badge-idle"
    };

    public static string MachineCardBorder(MachineLiveSummary m) =>
        m.Status == 3 ? "equip-mcard-run" :
        m.CakeHasThread == 168 ? "equip-mcard-full" :
        m.CakeEmpty == 168 ? "equip-mcard-empty" :
        m.CakeNoThread == 168 ? "equip-mcard-nothread" : "";

    public static string BufferStationCode(int stationId) => $"BUF{stationId:D3}";
    public static string ClearingStationCode(int stationId) => $"CLR{stationId:D3}";
    public static string PackagingStationCode(int stationId) => $"PKP{stationId:D3}";

    public static string SharedSideHint(ParkingSideDto side, char columnSide) =>
        columnSide == 'A' && side.MachineId > 1
            ? $"左側與 M{side.MachineId - 1:D2}-B 共用"
            : columnSide == 'B' && side.MachineId < 35
                ? $"右側與 M{side.MachineId + 1:D2}-A 共用"
                : "";
}
