using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

public static class TwistingUnloadBobbinPairingService
{
    const int StopsPerMission = TwistingParkingRegistry.StopsPerBobbinUnloadMission;

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

            if (!TryReserveSideSlots(candidate, activeFlights, reservedSideSlots))
                continue;

            var pointIds = candidate.MissionStops.Select(s => s.ParkingPointId).ToList();
            if (pointIds.Any(id =>
                    reservedPoints.Contains(id) ||
                    TwistingLoadDispatchTracker.IsParkingPointLocked(activeFlights, id)))
                continue;

            var vehicle = readyVehicles[vehicleIndex++];
            var stops = BuildFlowStops(candidate.MissionStops, vehicle);
            var (canDispatch, blockReason) = EvaluateCanDispatch(
                candidate, machines, activeFlights, fleetStatuses);

            pairs.Add(new TwistingLoadDispatchPair(
                candidate.PrimaryMachine,
                candidate.MissionStops,
                vehicle,
                stops,
                TwistingUnloadBobbinFlowDispatchBuilder.FlowName,
                canDispatch,
                blockReason,
                candidate.SecondaryMachine));

            foreach (var id in pointIds)
                reservedPoints.Add(id);

            IncrementSideSlots(candidate, reservedSideSlots);
        }

        return pairs;
    }

    static bool TryReserveSideSlots(
        TwistingUnloadBobbinMissionCandidate candidate,
        IReadOnlyList<TwistingLoadInFlight> activeFlights,
        Dictionary<string, int> reservedSideSlots)
    {
        foreach (var machine in candidate.AffectedSides)
        {
            var sideKey = $"{machine.MachineId}-{machine.Side}";
            var activeOnSide = TwistingLoadDispatchTracker.CountActiveMissionsOnSide(
                activeFlights, machine.MachineId, machine.Side);
            reservedSideSlots.TryGetValue(sideKey, out var reservedOnSide);
            if (activeOnSide + reservedOnSide >= TwistingParkingRegistry.MaxConcurrentMissionsPerSide)
                return false;
        }

        return true;
    }

    static void IncrementSideSlots(
        TwistingUnloadBobbinMissionCandidate candidate,
        Dictionary<string, int> reservedSideSlots)
    {
        foreach (var machine in candidate.AffectedSides)
        {
            var sideKey = $"{machine.MachineId}-{machine.Side}";
            reservedSideSlots.TryGetValue(sideKey, out var reservedOnSide);
            reservedSideSlots[sideKey] = reservedOnSide + 1;
        }
    }

    static (bool CanDispatch, string? BlockReason) EvaluateCanDispatch(
        TwistingUnloadBobbinMissionCandidate candidate,
        IReadOnlyList<TwistingLoadMachineEvaluation> machines,
        IReadOnlyList<TwistingLoadInFlight> activeFlights,
        IEnumerable<FleetStatusDto>? fleetStatuses)
    {
        var pointIds = candidate.MissionStops.Select(s => s.ParkingPointId).ToList();
        // 跨側時 pair.Machine 為合成 X；lane / 機台優先權仍以真實 A 側為準
        var laneMachine = candidate.LanePrimaryMachine;
        var laneBlockIndex = TwistingLoadLaneAdmission.GetLaneBlockIndex(
            candidate.MissionStopsForPrimaryLane, StopsPerMission);

        var canPrimary = TwistingLoadDispatchTracker.CanDispatchMissionBlock(
            activeFlights,
            laneMachine.MachineId,
            laneMachine.Side,
            laneMachine.TwpGroupId,
            laneBlockIndex,
            pointIds,
            laneMachine.AvailableMissions,
            machines,
            laneMachine.DockingPoints,
            fleetStatuses,
            TwistingParkingRegistry.RequestUnloadStatus,
            StopsPerMission);

        if (!canPrimary)
        {
            return (false, TwistingLoadDispatchTracker.TryGetDispatchBlockReason(
                activeFlights,
                laneMachine.MachineId,
                laneMachine.Side,
                laneMachine.TwpGroupId,
                laneBlockIndex,
                pointIds,
                laneMachine.AvailableMissions,
                machines,
                laneMachine.DockingPoints,
                fleetStatuses,
                TwistingParkingRegistry.RequestUnloadStatus,
                StopsPerMission));
        }

        if (candidate.SecondaryMachine is null)
            return (true, null);

        var secondary = candidate.SecondaryMachine;
        var secondaryBlockIndex = TwistingLoadLaneAdmission.GetLaneBlockIndex(
            candidate.MissionStopsForSecondaryLane!, StopsPerMission);

        var canSecondary = TwistingLoadLaneAdmission.IsLaneAdmissionGranted(
            activeFlights,
            secondary.TwpGroupId,
            secondaryBlockIndex,
            secondary.AvailableMissions,
            secondary.DockingPoints,
            fleetStatuses,
            StopsPerMission);

        if (!canSecondary)
        {
            return (false, TwistingLoadLaneAdmission.TryGetLaneBlockReason(
                activeFlights,
                secondary.TwpGroupId,
                secondaryBlockIndex,
                secondary.AvailableMissions,
                secondary.DockingPoints,
                fleetStatuses,
                StopsPerMission) ?? $"{TwistingLoadLaneAdmission.FormatTwpLane(secondary.TwpGroupId)} 走道尚未放行");
        }

        return (true, null);
    }

    static List<TwistingUnloadBobbinMissionCandidate> BuildCandidates(
        IReadOnlyList<TwistingLoadMachineEvaluation> machines,
        IReadOnlyList<TwistingLoadInFlight> activeFlights)
    {
        var list = new List<TwistingUnloadBobbinMissionCandidate>();
        var byMachine = machines
            .Where(m => m.Status == TwistingParkingRegistry.RequestUnloadStatus)
            .GroupBy(m => m.MachineId)
            .OrderBy(g => g.Key);

        foreach (var group in byMachine)
        {
            var sideA = group.FirstOrDefault(m => m.Side is 'A' or 'a');
            var sideB = group.FirstOrDefault(m => m.Side is 'B' or 'b');

            foreach (var machine in group.OrderBy(m => m.Side))
            {
                for (var blockIndex = 0; blockIndex < machine.AvailableMissions.Count; blockIndex++)
                {
                    var missionStops = machine.AvailableMissions[blockIndex];
                    var pointIds = missionStops.Select(s => s.ParkingPointId).ToList();
                    if (pointIds.Any(id => TwistingLoadDispatchTracker.IsParkingPointLocked(activeFlights, id)))
                        continue;

                    var laneBlockIndex = TwistingLoadLaneAdmission.GetLaneBlockIndex(
                        missionStops, StopsPerMission);
                    list.Add(TwistingUnloadBobbinMissionCandidate.SingleSide(
                        machine, laneBlockIndex, missionStops));
                }
            }

            if (sideA?.HasBobbinRemainderOnly == true &&
                sideB?.HasBobbinRemainderOnly == true &&
                sideA.RemainderStops is not null &&
                sideB.RemainderStops is not null)
            {
                var crossStops = sideA.RemainderStops.Concat(sideB.RemainderStops).ToList();
                var pointIds = crossStops.Select(s => s.ParkingPointId).ToList();
                if (!pointIds.Any(id => TwistingLoadDispatchTracker.IsParkingPointLocked(activeFlights, id)))
                {
                    var laneBlockIndex = TwistingLoadLaneAdmission.GetLaneBlockIndex(
                        sideA.RemainderStops, StopsPerMission);
                    list.Add(TwistingUnloadBobbinMissionCandidate.CrossSide(
                        sideA, sideB, laneBlockIndex, crossStops));
                }
            }
        }

        return list
            .OrderBy(c => c.PrimaryMachine.MachineId)
            .ThenBy(c => c.IsCrossSide ? 1 : 0)
            .ThenBy(c => c.BlockIndex)
            .ThenBy(c => c.PrimaryMachine.TwpGroupId)
            .ThenBy(c => c.PrimaryMachine.Side)
            .ToList();
    }

    public static IReadOnlyList<TwistingLoadFlowStop> BuildFlowStops(
        IReadOnlyList<TwistingDockingPointEvaluation> missionStops,
        CakeVehicleDispatchStatus vehicle)
    {
        var list = new List<TwistingLoadFlowStop>(missionStops.Count);
        for (var i = 0; i < missionStops.Count; i++)
        {
            var stop = missionStops[i];
            var request = TwistingUnloadBobbinFlowDispatchBuilder.Build(stop, vehicle, i);
            list.Add(new TwistingLoadFlowStop(
                i + 1,
                stop.ParkingPointId,
                request,
                TwistingUnloadBobbinFlowDispatchBuilder.Serialize(request)));
        }
        return list;
    }

    sealed class TwistingUnloadBobbinMissionCandidate
    {
        TwistingUnloadBobbinMissionCandidate(
            TwistingLoadMachineEvaluation pairMachine,
            TwistingLoadMachineEvaluation lanePrimaryMachine,
            TwistingLoadMachineEvaluation? secondaryMachine,
            int blockIndex,
            IReadOnlyList<TwistingDockingPointEvaluation> missionStops,
            IReadOnlyList<TwistingDockingPointEvaluation> missionStopsForPrimaryLane,
            IReadOnlyList<TwistingDockingPointEvaluation>? missionStopsForSecondaryLane)
        {
            PrimaryMachine = pairMachine;
            LanePrimaryMachine = lanePrimaryMachine;
            SecondaryMachine = secondaryMachine;
            BlockIndex = blockIndex;
            MissionStops = missionStops;
            MissionStopsForPrimaryLane = missionStopsForPrimaryLane;
            MissionStopsForSecondaryLane = missionStopsForSecondaryLane;
        }

        /// <summary>寫入 pair.Machine（跨側為合成 X）。</summary>
        public TwistingLoadMachineEvaluation PrimaryMachine { get; }
        /// <summary>A 側（或單側）真實評估，供 lane / 機台優先權。</summary>
        public TwistingLoadMachineEvaluation LanePrimaryMachine { get; }
        public TwistingLoadMachineEvaluation? SecondaryMachine { get; }
        public int BlockIndex { get; }
        public IReadOnlyList<TwistingDockingPointEvaluation> MissionStops { get; }
        public IReadOnlyList<TwistingDockingPointEvaluation> MissionStopsForPrimaryLane { get; }
        public IReadOnlyList<TwistingDockingPointEvaluation>? MissionStopsForSecondaryLane { get; }
        public bool IsCrossSide => SecondaryMachine is not null;

        public IEnumerable<TwistingLoadMachineEvaluation> AffectedSides
        {
            get
            {
                yield return LanePrimaryMachine;
                if (SecondaryMachine is not null)
                    yield return SecondaryMachine;
            }
        }

        public static TwistingUnloadBobbinMissionCandidate SingleSide(
            TwistingLoadMachineEvaluation machine,
            int blockIndex,
            IReadOnlyList<TwistingDockingPointEvaluation> missionStops) =>
            new(machine, machine, null, blockIndex, missionStops, missionStops, null);

        public static TwistingUnloadBobbinMissionCandidate CrossSide(
            TwistingLoadMachineEvaluation sideA,
            TwistingLoadMachineEvaluation sideB,
            int blockIndex,
            IReadOnlyList<TwistingDockingPointEvaluation> missionStops)
        {
            // 合成 Machine：Side=X，MissionKey=Mxx-AB，DockingPoints=A+B（供完成偵測）
            var synthetic = new TwistingLoadMachineEvaluation(
                sideA.MachineId,
                sideA.MachineCode,
                'X',
                sideA.TwpGroupId,
                sideA.Status,
                sideA.StatusLabel,
                true,
                $"跨側殘餘 · A {TwistingLoadMissionPlanner.FormatMissionStops(sideA.RemainderStops!)} → B {TwistingLoadMissionPlanner.FormatMissionStops(sideB.RemainderStops!)}",
                sideA.DockingPoints.Concat(sideB.DockingPoints).ToList(),
                [missionStops],
                missionStops,
                null);

            return new TwistingUnloadBobbinMissionCandidate(
                synthetic,
                sideA,
                sideB,
                blockIndex,
                missionStops,
                sideA.RemainderStops!,
                sideB.RemainderStops);
        }
    }
}
