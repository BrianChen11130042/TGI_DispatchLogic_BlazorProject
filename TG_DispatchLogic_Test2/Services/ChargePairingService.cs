using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// 充電站與低電量空車 1:1 配對。站點依代號排序；車輛電量低者優先。
/// </summary>
public static class ChargePairingService
{
    public static IReadOnlyList<ChargeDispatchPair> Pair(
        IReadOnlyList<ChargeStationEvaluation> stations,
        IReadOnlyList<CakeVehicleDispatchStatus> vehicles,
        int targetPercent,
        IReadOnlySet<string>? busyAmrCodes = null)
    {
        var readyStations = stations
            .Where(s => s.IsDispatchable)
            .OrderBy(s => s.CellId, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var readyVehicles = vehicles
            .Where(v => v.IsEligible)
            .Where(v => busyAmrCodes is null || !CakeVehicleDispatchEvaluator.IsAmrInBusySet(busyAmrCodes, v.AmrCode))
            .OrderBy(v => v.BatteryPercent)
            .ThenBy(v => v.AmrCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var count = Math.Min(readyStations.Count, readyVehicles.Count);
        var pairs = new List<ChargeDispatchPair>(count);
        for (var i = 0; i < count; i++)
        {
            var station = readyStations[i];
            var vehicle = readyVehicles[i];
            var flowRequest = ChargeFlowDispatchBuilder.Build(station, vehicle, targetPercent);
            pairs.Add(new ChargeDispatchPair(
                station,
                vehicle,
                Math.Clamp(targetPercent, 1, 100),
                flowRequest,
                ChargeFlowDispatchBuilder.FlowName,
                ChargeFlowDispatchBuilder.Serialize(flowRequest)));
        }

        return pairs;
    }
}
