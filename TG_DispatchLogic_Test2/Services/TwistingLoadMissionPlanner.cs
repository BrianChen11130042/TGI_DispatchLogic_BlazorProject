using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// 撚紗上料停車點規劃：各 TWP 走道（如 TWP01、TWP02）獨立運作；
/// 單行道由尾端（seq 21）往前，每 3 點一組；組內 flow 順序為 seq 遞增（例：19 → 20 → 21）。
/// </summary>
public static class TwistingLoadMissionPlanner
{
    public static IReadOnlyList<IReadOnlyList<TwistingDockingPointEvaluation>> PlanTailFirstMissions(
        IReadOnlyList<TwistingDockingPointEvaluation> dockingPoints)
    {
        var readyBySeq = dockingPoints
            .Where(p => p.IsDispatchable)
            .ToDictionary(p => p.Sequence);

        var missions = new List<IReadOnlyList<TwistingDockingPointEvaluation>>();
        var maxSeq = TwistingParkingRegistry.DockingPointsPerSide;

        for (var tailSeq = maxSeq; tailSeq >= TwistingParkingRegistry.StopsPerLoadMission; tailSeq -= TwistingParkingRegistry.StopsPerLoadMission)
        {
            var headSeq = tailSeq - TwistingParkingRegistry.StopsPerLoadMission + 1;
            var block = new List<TwistingDockingPointEvaluation>(TwistingParkingRegistry.StopsPerLoadMission);
            var complete = true;

            for (var seq = headSeq; seq <= tailSeq; seq++)
            {
                if (!readyBySeq.TryGetValue(seq, out var point))
                {
                    complete = false;
                    break;
                }
                block.Add(point);
            }

            if (complete)
                missions.Add(block);
        }

        return missions;
    }

    public static string FormatMissionStops(IReadOnlyList<TwistingDockingPointEvaluation> stops) =>
        string.Join(" → ", stops.Select(s => s.ParkingPointId));
}
