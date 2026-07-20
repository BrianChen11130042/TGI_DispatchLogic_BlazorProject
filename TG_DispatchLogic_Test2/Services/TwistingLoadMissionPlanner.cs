using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// 撚紗停車點規劃：各 TWP 走道獨立；單行道由尾端（seq 21）往前湊組；
/// 組內 flow 順序為 seq 遞增（例：19 → 20 → 21）。
/// </summary>
public static class TwistingLoadMissionPlanner
{
    public static IReadOnlyList<IReadOnlyList<TwistingDockingPointEvaluation>> PlanTailFirstMissions(
        IReadOnlyList<TwistingDockingPointEvaluation> dockingPoints,
        int stopsPerMission = TwistingParkingRegistry.StopsPerLoadMission)
    {
        if (stopsPerMission <= 0)
            throw new ArgumentOutOfRangeException(nameof(stopsPerMission));

        var readyBySeq = dockingPoints
            .Where(p => p.IsDispatchable)
            .ToDictionary(p => p.Sequence);

        var missions = new List<IReadOnlyList<TwistingDockingPointEvaluation>>();
        var maxSeq = TwistingParkingRegistry.DockingPointsPerSide;

        for (var tailSeq = maxSeq; tailSeq >= stopsPerMission; tailSeq -= stopsPerMission)
        {
            var headSeq = tailSeq - stopsPerMission + 1;
            var block = new List<TwistingDockingPointEvaluation>(stopsPerMission);
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

    /// <summary>
    /// 殘餘組：僅 seq 1~N 就緒，且沒有其他就緒停車點（配合 Bobbin 滿車後剩餘 3 停）。
    /// </summary>
    public static IReadOnlyList<TwistingDockingPointEvaluation>? TryGetRemainderOnlyStops(
        IReadOnlyList<TwistingDockingPointEvaluation> dockingPoints,
        int remainderStops = TwistingParkingRegistry.BobbinRemainderStops)
    {
        var ready = dockingPoints.Where(p => p.IsDispatchable).OrderBy(p => p.Sequence).ToList();
        if (ready.Count != remainderStops)
            return null;

        for (var i = 0; i < remainderStops; i++)
        {
            if (ready[i].Sequence != i + 1)
                return null;
        }

        return ready;
    }

    public static string FormatMissionStops(IReadOnlyList<TwistingDockingPointEvaluation> stops) =>
        string.Join(" → ", stops.Select(s => s.ParkingPointId));
}
