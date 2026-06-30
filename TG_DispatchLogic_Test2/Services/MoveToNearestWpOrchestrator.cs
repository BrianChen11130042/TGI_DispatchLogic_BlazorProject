using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>依車輛目前位置，派 MoveToWp 至最近空閒 WP（純移動，不取料、不送 TWP）。</summary>
public class MoveToNearestWpOrchestrator(AmrApiClient amr)
{
    public async Task<ApiCallResult<SimulationTaskCreatedDto>> RunAsync(
        string accessToken,
        string robotId,
        string amrType,
        CancellationToken cancellationToken = default)
    {
        var (fleetOk, fleet, fleetErr) = await amr.PollFleetStatusAsync(accessToken, cancellationToken);
        if (!fleetOk)
        {
            return new ApiCallResult<SimulationTaskCreatedDto>(
                false, fleetErr ?? "查詢車隊失敗", ApiFailureStage.None,
                "", "GET", 0, 0, [], null, null);
        }

        var sitesResult = await amr.GetAllWmsCellsAsync(accessToken, cancellationToken);
        var activeResult = await amr.GetActiveSimulationTasksAsync(accessToken, cancellationToken);

        var sites = sitesResult.Success && sitesResult.Data is not null ? sitesResult.Data : [];
        var activeTasks = activeResult.Success && activeResult.Data is not null ? activeResult.Data : [];

        var vehicle = fleet.FirstOrDefault(v =>
            v.RobotId.Equals(robotId, StringComparison.OrdinalIgnoreCase));
        if (vehicle is null)
        {
            return new ApiCallResult<SimulationTaskCreatedDto>(
                false, $"找不到車輛 {robotId}", ApiFailureStage.None,
                "", "POST", 0, 0, [], null, null);
        }

        var wpList = FleetParkingPlanner.GetWaitPointList(sites);
        var wpCodes = wpList.Select(w => w.CellId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var taskReserved = FleetParkingPlanner.GetReservedWaitPointTargetsFromTasks(activeTasks, wpCodes);
        var wpCode = FleetParkingPlanner.PlanNearestFreeWaitPointCode(
            vehicle, sites, fleet, taskReserved);

        if (string.IsNullOrWhiteSpace(wpCode))
        {
            return new ApiCallResult<SimulationTaskCreatedDto>(
                false,
                "無可用空閒 WP（已排除有車停放及其他任務目的地）",
                ApiFailureStage.None,
                "", "POST", 0, 0, [], null, null);
        }

        var moveResult = await amr.CreateSimulationTaskDiagnosticAsync(
            accessToken,
            new SimulationTaskRequest
            {
                TaskKind = "MoveToWp",
                RequiredAmrType = amrType,
                AssignedRobot = robotId,
                TargetSiteCode = wpCode,
                Priority = 5,
                TriggerEvent = "api-test-nearest-wp"
            },
            cancellationToken);

        if (!moveResult.Success)
            return moveResult;

        return moveResult with
        {
            Summary = $"純移動：{robotId} → {wpCode}（{moveResult.Data?.TaskNo}）"
        };
    }
}
