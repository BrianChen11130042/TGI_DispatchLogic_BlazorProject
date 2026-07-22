using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// 記錄 Bobbin 車首次變成 AOI 可派的時間；不符合條件時清除，再次可派則記新時間。
/// </summary>
public static class AoiLoadEligibleSinceTracker
{
    public static IReadOnlyList<CakeVehicleDispatchStatus> Apply(
        IReadOnlyList<CakeVehicleDispatchStatus> vehicles,
        IDictionary<string, DateTime> eligibleSinceByAmr,
        DateTime? now = null)
    {
        var stamp = now ?? DateTime.Now;
        var result = new List<CakeVehicleDispatchStatus>(vehicles.Count);

        foreach (var vehicle in vehicles)
        {
            if (vehicle.IsEligible)
            {
                if (!eligibleSinceByAmr.TryGetValue(vehicle.AmrCode, out var since))
                {
                    since = stamp;
                    eligibleSinceByAmr[vehicle.AmrCode] = since;
                }

                result.Add(vehicle with { EligibleSince = since });
            }
            else
            {
                eligibleSinceByAmr.Remove(vehicle.AmrCode);
                result.Add(vehicle with { EligibleSince = null });
            }
        }

        return result;
    }
}
