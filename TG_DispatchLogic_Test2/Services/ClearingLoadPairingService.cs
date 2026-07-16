using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>清軸站依 CLR001→CLR005 優先序，與可派無絲 Cake 車 1:1 配對。</summary>
public static class ClearingLoadPairingService
{
    public static IReadOnlyList<ClearingLoadDispatchPair> Pair(
        IReadOnlyList<ClearingLoadDispatchEvaluation> stations,
        IReadOnlyList<CakeVehicleDispatchStatus> vehicles,
        IReadOnlySet<string>? busyAmrCodes = null)
    {
        var readyStations = stations
            .Where(s => s.IsDispatchable)
            .OrderBy(s => s.StationId)
            .ToList();
        var readyVehicles = vehicles
            .Where(v => v.IsEligible)
            .Where(v => busyAmrCodes is null || !CakeVehicleDispatchEvaluator.IsAmrInBusySet(busyAmrCodes, v.AmrCode))
            .OrderBy(v => v.AmrCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var count = Math.Min(readyStations.Count, readyVehicles.Count);
        var pairs = new List<ClearingLoadDispatchPair>(count);
        for (var i = 0; i < count; i++)
        {
            var station = readyStations[i];
            var vehicle = readyVehicles[i];
            var flowRequest = ClearingLoadFlowDispatchBuilder.Build(station, vehicle);
            pairs.Add(new ClearingLoadDispatchPair(
                station,
                vehicle,
                flowRequest,
                ClearingLoadFlowDispatchBuilder.FlowName,
                ClearingLoadFlowDispatchBuilder.Serialize(flowRequest)));
        }

        return pairs;
    }
}
