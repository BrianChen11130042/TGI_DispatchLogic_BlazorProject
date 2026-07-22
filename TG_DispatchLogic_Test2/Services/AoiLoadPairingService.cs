using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// AOI 站依 PKP001→PKP002 優先序，與可派滿載 Bobbin 車 1:1 配對。
/// 車輛優先序：率先達成可派條件者優先（EligibleSince），同時間再比車號。
/// </summary>
public static class AoiLoadPairingService
{
    public static IReadOnlyList<AoiLoadDispatchPair> Pair(
        IReadOnlyList<AoiLoadDispatchEvaluation> stations,
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
            .OrderBy(v => v.EligibleSince ?? DateTime.MaxValue)
            .ThenBy(v => v.AmrCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var count = Math.Min(readyStations.Count, readyVehicles.Count);
        var pairs = new List<AoiLoadDispatchPair>(count);
        for (var i = 0; i < count; i++)
        {
            var station = readyStations[i];
            var vehicle = readyVehicles[i];
            var flowRequest = AoiLoadFlowDispatchBuilder.Build(station, vehicle);
            pairs.Add(new AoiLoadDispatchPair(
                station,
                vehicle,
                flowRequest,
                AoiLoadFlowDispatchBuilder.FlowName,
                AoiLoadFlowDispatchBuilder.Serialize(flowRequest)));
        }

        return pairs;
    }
}
