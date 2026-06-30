using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>
/// 純移動至指定站點，抵達後等待 3 秒，再自動回最近空閒 WP。
/// </summary>
public class MoveToSiteReturnWpOrchestrator(AmrApiClient amr)
{
    const int ArrivalTimeoutSec = 180;
    const int WaitBeforeReturnSec = 3;
    const int PollIntervalMs = 400;

    public async Task<ApiCallResult<SimulationTaskCreatedDto>> RunAsync(
        string accessToken,
        string robotId,
        string amrType,
        string targetSiteCode,
        string siteLabel,
        string triggerEvent,
        string? restrictedAmrType,
        Action<string>? onProgress,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(restrictedAmrType)
            && !SimulationTaskTestHelper.InferAmrType(robotId)
                .Equals(restrictedAmrType, StringComparison.OrdinalIgnoreCase))
        {
            return Fail($"移至 {siteLabel} 僅限 {restrictedAmrType} 車（目前：{robotId}）");
        }

        onProgress?.Invoke($"① 移動至 {siteLabel}：{robotId} → {targetSiteCode}");

        var toSite = await amr.CreateSimulationTaskDiagnosticAsync(
            accessToken,
            new SimulationTaskRequest
            {
                TaskKind = "MoveToWp",
                RequiredAmrType = amrType,
                AssignedRobot = robotId,
                TargetSiteCode = targetSiteCode,
                Priority = 5,
                TriggerEvent = triggerEvent
            },
            cancellationToken);

        if (!toSite.Success)
            return toSite;

        onProgress?.Invoke($"② 等待抵達 {targetSiteCode}…");

        var sites = await LoadSitesAsync(accessToken, cancellationToken);
        var arrived = await WaitForSiteArrivalAsync(
            accessToken, robotId, targetSiteCode, sites, onProgress, cancellationToken);
        if (arrived.Error is not null)
            return toSite with { Success = false, Summary = arrived.Error };

        if (!arrived.Ok)
        {
            return toSite with
            {
                Success = false,
                Summary = $"{robotId} 等待抵達 {targetSiteCode} 逾時（{ArrivalTimeoutSec} 秒）。"
            };
        }

        onProgress?.Invoke($"③ 已抵達 {targetSiteCode}，等待 {WaitBeforeReturnSec} 秒…");
        await Task.Delay(TimeSpan.FromSeconds(WaitBeforeReturnSec), cancellationToken);

        onProgress?.Invoke("④ 尋找最近空閒 WP…");

        var (fleet, activeTasks) = await LoadFleetAndTasksAsync(accessToken, cancellationToken);
        var vehicle = fleet.FirstOrDefault(v =>
            v.RobotId.Equals(robotId, StringComparison.OrdinalIgnoreCase));
        if (vehicle is null)
            return Fail($"找不到車輛 {robotId}");

        var wpList = FleetParkingPlanner.GetWaitPointList(sites);
        var wpCodes = wpList.Select(w => w.CellId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var taskReserved = FleetParkingPlanner.GetReservedWaitPointTargetsFromTasks(activeTasks, wpCodes);
        var wpCode = FleetParkingPlanner.PlanNearestFreeWaitPointCode(
            vehicle, sites, fleet, taskReserved);

        if (string.IsNullOrWhiteSpace(wpCode))
            return Fail("無可用空閒 WP（已排除有車停放及其他任務目的地）");

        onProgress?.Invoke($"⑤ 回 WP 待命：{robotId} → {wpCode}");

        var toWp = await amr.CreateSimulationTaskDiagnosticAsync(
            accessToken,
            new SimulationTaskRequest
            {
                TaskKind = "MoveToWp",
                RequiredAmrType = amrType,
                AssignedRobot = robotId,
                TargetSiteCode = wpCode,
                Priority = 5,
                TriggerEvent = triggerEvent
            },
            cancellationToken);

        if (!toWp.Success)
            return toWp;

        onProgress?.Invoke($"⑥ 等待抵達 {wpCode}…");

        var wpArrived = await WaitForWpArrivalAsync(
            accessToken, robotId, wpCode, sites, onProgress, cancellationToken);
        if (wpArrived.Error is not null)
            return toWp with { Success = false, Summary = wpArrived.Error };

        if (!wpArrived.Ok)
        {
            return toWp with
            {
                Success = false,
                Summary = $"{robotId} 等待抵達 {wpCode} 逾時（{ArrivalTimeoutSec} 秒）。"
            };
        }

        return toWp with
        {
            Summary = $"{siteLabel}→回WP：{robotId} {targetSiteCode} → 等{WaitBeforeReturnSec}s → {wpCode}（{toWp.Data?.TaskNo}）"
        };
    }

    async Task<(bool Ok, string? Error)> WaitForSiteArrivalAsync(
        string accessToken,
        string robotId,
        string siteCode,
        IReadOnlyList<WmsCellDto> sites,
        Action<string>? onProgress,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(ArrivalTimeoutSec);
        var started = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elapsed = (int)(DateTime.UtcNow - started).TotalSeconds;

            var (fleetOk, fleet, _) = await amr.PollFleetStatusAsync(accessToken, cancellationToken);
            if (fleetOk)
            {
                var vehicle = fleet.FirstOrDefault(v =>
                    v.RobotId.Equals(robotId, StringComparison.OrdinalIgnoreCase));
                if (vehicle is not null && FleetParkingPlanner.HasReachedSite(vehicle, siteCode, sites))
                    return (true, null);

                if (vehicle is not null)
                {
                    onProgress?.Invoke(
                        $"② 等待抵達（{elapsed}s）：{robotId}（{vehicle.State} @ {vehicle.CurrentSite ?? "—"}）→ {siteCode}…");
                }
            }

            await Task.Delay(PollIntervalMs, cancellationToken);
        }

        return (false, null);
    }

    async Task<(bool Ok, string? Error)> WaitForWpArrivalAsync(
        string accessToken,
        string robotId,
        string wpCode,
        IReadOnlyList<WmsCellDto> sites,
        Action<string>? onProgress,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(ArrivalTimeoutSec);
        var started = DateTime.UtcNow;
        var wpList = FleetParkingPlanner.GetWaitPointList(sites);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elapsed = (int)(DateTime.UtcNow - started).TotalSeconds;

            var (fleetOk, fleet, _) = await amr.PollFleetStatusAsync(accessToken, cancellationToken);
            if (fleetOk)
            {
                var vehicle = fleet.FirstOrDefault(v =>
                    v.RobotId.Equals(robotId, StringComparison.OrdinalIgnoreCase));
                if (vehicle is not null
                    && FleetParkingPlanner.HasReachedWaitPoint(vehicle, wpCode, wpList))
                {
                    return (true, null);
                }

                if (vehicle is not null)
                {
                    onProgress?.Invoke(
                        $"⑥ 等待抵達（{elapsed}s）：{robotId}（{vehicle.State} @ {vehicle.CurrentSite ?? "—"}）→ {wpCode}…");
                }
            }

            await Task.Delay(PollIntervalMs, cancellationToken);
        }

        return (false, null);
    }

    async Task<List<WmsCellDto>> LoadSitesAsync(
        string accessToken, CancellationToken cancellationToken)
    {
        var sitesResult = await amr.GetAllWmsCellsAsync(accessToken, cancellationToken);
        return sitesResult.Success && sitesResult.Data is not null ? sitesResult.Data : [];
    }

    async Task<(List<FleetStatusDto> Fleet, List<ActiveSimulationTaskDto> ActiveTasks)>
        LoadFleetAndTasksAsync(string accessToken, CancellationToken cancellationToken)
    {
        var fleetTask = amr.PollFleetStatusAsync(accessToken, cancellationToken);
        var activeTask = amr.GetActiveSimulationTasksAsync(accessToken, cancellationToken);
        await Task.WhenAll(fleetTask, activeTask);

        var (fleetOk, fleet, _) = await fleetTask;
        var activeResult = await activeTask;
        return (
            fleetOk ? fleet : [],
            activeResult.Success && activeResult.Data is not null ? activeResult.Data : []);
    }

    static ApiCallResult<SimulationTaskCreatedDto> Fail(string message) =>
        new(false, message, ApiFailureStage.None, "", "POST", 0, 0, [], null, null);
}
