using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

public static class TwistingLoadPairingService
{
    public static IReadOnlyList<TwistingLoadDispatchPair> Pair(
        IReadOnlyList<TwistingLoadMachineEvaluation> machines,
        IReadOnlyList<CakeVehicleDispatchStatus> vehicles,
        IEnumerable<TwistingLoadInFlight> inFlight,
        IEnumerable<FleetStatusDto>? fleetStatuses = null)
    {
        var activeFlights = inFlight.Where(f => f.IsActive).ToList();
        var readyVehicles = vehicles
            .Where(v => v.IsEligible)
            .OrderBy(v => v.AmrCode, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var candidates = BuildCandidates(machines, activeFlights);
        var pairs = new List<TwistingLoadDispatchPair>(Math.Min(candidates.Count, readyVehicles.Count));
        var reservedPoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reservedSideSlots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var vehicleIndex = 0;

        foreach (var candidate in candidates)
        {
            if (vehicleIndex >= readyVehicles.Count) break;

            var sideKey = candidate.Machine.MissionKey;
            var activeOnSide = TwistingLoadDispatchTracker.CountActiveMissionsOnSide(
                activeFlights, candidate.Machine.MachineId, candidate.Machine.Side);
            reservedSideSlots.TryGetValue(sideKey, out var reservedOnSide);
            if (activeOnSide + reservedOnSide >= TwistingParkingRegistry.MaxConcurrentMissionsPerSide)
                continue;

            var pointIds = candidate.MissionStops.Select(s => s.ParkingPointId).ToList();
            if (pointIds.Any(id =>
                    reservedPoints.Contains(id) ||
                    TwistingLoadDispatchTracker.IsParkingPointLocked(activeFlights, id)))
                continue;

            var vehicle = readyVehicles[vehicleIndex++];
            var stops = BuildStops(candidate.Machine, candidate.MissionStops, vehicle);
            var laneBlockIndex = TwistingLoadLaneAdmission.GetLaneBlockIndex(candidate.MissionStops);
            var canDispatch = TwistingLoadDispatchTracker.CanDispatchMissionBlock(
                activeFlights,
                candidate.Machine.MachineId,
                candidate.Machine.Side,
                candidate.Machine.TwpGroupId,
                laneBlockIndex,
                pointIds,
                candidate.Machine.AvailableMissions,
                machines,
                candidate.Machine.DockingPoints,
                fleetStatuses);
            var blockReason = canDispatch
                ? null
                : TwistingLoadDispatchTracker.TryGetDispatchBlockReason(
                    activeFlights,
                    candidate.Machine.MachineId,
                    candidate.Machine.Side,
                    candidate.Machine.TwpGroupId,
                    laneBlockIndex,
                    pointIds,
                    candidate.Machine.AvailableMissions,
                    machines,
                    candidate.Machine.DockingPoints,
                    fleetStatuses);

            pairs.Add(new TwistingLoadDispatchPair(
                candidate.Machine,
                candidate.MissionStops,
                vehicle,
                stops,
                TwistingLoadFlowDispatchBuilder.FlowName,
                canDispatch,
                blockReason));

            foreach (var id in pointIds)
                reservedPoints.Add(id);
            reservedSideSlots[sideKey] = reservedOnSide + 1;
        }

        return pairs;
    }

    static List<TwistingLoadMissionCandidate> BuildCandidates(
        IReadOnlyList<TwistingLoadMachineEvaluation> machines,
        IReadOnlyList<TwistingLoadInFlight> activeFlights)
    {
        var list = new List<TwistingLoadMissionCandidate>();
        foreach (var machine in machines
                     .Where(m => m.Status == TwistingParkingRegistry.CallVehicleStatus)
                     .OrderBy(m => m.MachineId)
                     .ThenBy(m => m.Side))
        {
            for (var blockIndex = 0; blockIndex < machine.AvailableMissions.Count; blockIndex++)
            {
                var missionStops = machine.AvailableMissions[blockIndex];
                var pointIds = missionStops.Select(s => s.ParkingPointId).ToList();
                if (pointIds.Any(id => TwistingLoadDispatchTracker.IsParkingPointLocked(activeFlights, id)))
                    continue;

                var laneBlockIndex = TwistingLoadLaneAdmission.GetLaneBlockIndex(missionStops);
                list.Add(new TwistingLoadMissionCandidate(machine, laneBlockIndex, missionStops));
            }
        }

        return list
            .OrderBy(c => c.Machine.MachineId)
            .ThenBy(c => c.BlockIndex)
            .ThenBy(c => c.Machine.TwpGroupId)
            .ThenBy(c => c.Machine.Side)
            .ToList();
    }

    static IReadOnlyList<TwistingLoadFlowStop> BuildStops(
        TwistingLoadMachineEvaluation machine,
        IReadOnlyList<TwistingDockingPointEvaluation> missionStops,
        CakeVehicleDispatchStatus vehicle) =>
        BuildFlowStops(machine, missionStops, vehicle);

    public static IReadOnlyList<TwistingLoadFlowStop> BuildFlowStops(
        TwistingLoadMachineEvaluation machine,
        IReadOnlyList<TwistingDockingPointEvaluation> missionStops,
        CakeVehicleDispatchStatus vehicle)
    {
        var list = new List<TwistingLoadFlowStop>(missionStops.Count);
        for (var i = 0; i < missionStops.Count; i++)
        {
            var stop = missionStops[i];
            var request = TwistingLoadFlowDispatchBuilder.Build(stop, vehicle, i);
            list.Add(new TwistingLoadFlowStop(
                i + 1,
                stop.ParkingPointId,
                request,
                TwistingLoadFlowDispatchBuilder.Serialize(request)));
        }
        return list;
    }

    sealed record TwistingLoadMissionCandidate(
        TwistingLoadMachineEvaluation Machine,
        int BlockIndex,
        IReadOnlyList<TwistingDockingPointEvaluation> MissionStops);
}
