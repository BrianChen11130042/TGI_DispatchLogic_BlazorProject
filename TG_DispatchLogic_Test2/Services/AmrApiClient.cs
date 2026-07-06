using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

public class AmrApiClient(HttpClient http, IOptions<AmrApiOptions> options)
{
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    readonly AmrApiOptions _options = options.Value;

    public string BaseUrl => _options.BaseUrl;

    /// <summary>POST /login/access-token</summary>
    public Task<ApiCallResult<LoginData>> LoginAsync(CancellationToken cancellationToken = default)
    {
        var steps = new List<ApiDiagnosticStep>();
        var sw = Stopwatch.StartNew();
        var requestUrl = BuildRequestUrl("login/access-token");
        const string method = "POST";

        if (!TryValidateConfig(steps, requestUrl, method, sw, out var configFail))
            return Task.FromResult(configFail!);

        steps.Add(new("1. 讀取設定", true,
            $"BaseUrl={_options.BaseUrl}，Username={_options.Username}，Password=****"));

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = _options.Username,
            ["password"] = _options.Password
        });
        steps.Add(new("2. 建立請求", true,
            $"POST {requestUrl}，Content-Type=application/x-www-form-urlencoded"));

        return SendAndParseAsync<LoginData>(
            () => http.PostAsync("login/access-token", content, cancellationToken),
            steps, sw, requestUrl, method,
            body =>
            {
                var token = JsonSerializer.Deserialize<TokenResponse>(body, JsonOpts);
                if (token is null)
                {
                    steps.Add(new("5. 解析 JSON", false, "Deserialize 回傳 null"));
                    return FailResult<LoginData>(sw, steps, ApiFailureStage.ParseResponse,
                        requestUrl, method, 200, body, "JSON 解析結果為空");
                }

                steps.Add(new("5. 解析 JSON", true,
                    $"access_token 長度={token.AccessToken?.Length ?? 0}，token_type={token.TokenType ?? "(空)"}"));

                if (string.IsNullOrWhiteSpace(token.AccessToken))
                {
                    steps.Add(new("6. 驗證 Token", false, "access_token 為空"));
                    return FailResult<LoginData>(sw, steps, ApiFailureStage.ValidateResponse,
                        requestUrl, method, 200, body, "登入回應缺少 access_token");
                }

                steps.Add(new("6. 驗證 Token", true, "access_token 已取得"));
                sw.Stop();
                return OkResult(sw, steps, requestUrl, method, 200, body,
                    new LoginData(token.AccessToken, token.TokenType ?? ""), "登入成功");
            },
            cancellationToken);
    }

    /// <summary>GET /v2/fleets/status — 所有 AMR 車隊狀態</summary>
    public Task<ApiCallResult<List<FleetStatusDto>>> GetFleetStatusAsync(
        string accessToken,
        CancellationToken cancellationToken = default) =>
        GetAuthorizedListAsync<FleetStatusDto>(
            "v2/fleets/status", accessToken, "車隊狀態", cancellationToken);

    /// <summary>GET /v2/robots/status — 所有機器人狀態</summary>
    public Task<ApiCallResult<List<RobotStatusDto>>> GetRobotsStatusAsync(
        string accessToken,
        CancellationToken cancellationToken = default) =>
        GetAuthorizedListAsync<RobotStatusDto>(
            "v2/robots/status", accessToken, "機器人狀態", cancellationToken);

    /// <summary>GET /v2/wms — 全部站點</summary>
    public Task<ApiCallResult<List<WmsCellDto>>> GetAllWmsCellsAsync(
        string accessToken,
        CancellationToken cancellationToken = default) =>
        GetWmsCellsFilteredAsync(
            accessToken,
            static _ => true,
            "6. 全部站點",
            static count => $"WMS 共 {count} 筆站點",
            static count => $"取得 {count} 個站點",
            cancellationToken);

    /// <summary>GET /v2/wms — 篩選 cell_type=Buffer 的備料站狀態</summary>
    public Task<ApiCallResult<List<WmsCellDto>>> GetBufferStatusAsync(
        string accessToken,
        CancellationToken cancellationToken = default) =>
        GetWmsCellsFilteredAsync(
            accessToken,
            c => string.Equals(c.CellType, "Buffer", StringComparison.OrdinalIgnoreCase),
            "6. 篩選 Buffer",
            static count => count > 0 ? $"備料站 {count} 筆（cell_type=Buffer）" : "無 cell_type=Buffer 的站點",
            static count => $"取得 {count} 個 Buffer 備料站",
            cancellationToken);

    /// <summary>GET /v2/wms — 篩選等待點（cell_type=Waiting 或 WP 代碼）</summary>
    public Task<ApiCallResult<List<WmsCellDto>>> GetWaitPointStatusAsync(
        string accessToken,
        CancellationToken cancellationToken = default) =>
        GetWmsCellsFilteredAsync(
            accessToken,
            static c => string.Equals(c.CellType, "Waiting", StringComparison.OrdinalIgnoreCase)
                || c.CellId.StartsWith("WP", StringComparison.OrdinalIgnoreCase),
            "6. 篩選 Wait Point",
            static count => count > 0
                ? $"等待點 {count} 筆（cell_type=Waiting 或 WP 代碼）"
                : "無等待點站點",
            static count => $"取得 {count} 個 Wait Point 等待點",
            cancellationToken);

    Task<ApiCallResult<List<WmsCellDto>>> GetWmsCellsFilteredAsync(
        string accessToken,
        Func<WmsCellDto, bool> predicate,
        string filterStepName,
        Func<int, string> filterDetail,
        Func<int, string> summary,
        CancellationToken cancellationToken)
    {
        var steps = new List<ApiDiagnosticStep>();
        var sw = Stopwatch.StartNew();
        const string path = "v2/wms";
        var requestUrl = BuildRequestUrl(path);
        const string method = "GET";

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            steps.Add(new("1. 檢查 Token", false, "access_token 為空，請先執行登入測試"));
            return Task.FromResult(FailResult<List<WmsCellDto>>(sw, steps, ApiFailureStage.Authorization,
                requestUrl, method, 0, null, "尚未登入，請先取得 Token"));
        }

        steps.Add(new("1. 檢查 Token", true, $"Bearer token 長度={accessToken.Length}"));
        steps.Add(new("2. 建立請求", true, $"GET {requestUrl}，Authorization=Bearer ***"));

        return SendAndParseAsync<List<WmsCellDto>>(
            () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, path);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                return http.SendAsync(req, cancellationToken);
            },
            steps, sw, requestUrl, method,
            body =>
            {
                List<WmsCellDto>? all;
                try
                {
                    all = JsonSerializer.Deserialize<List<WmsCellDto>>(body, JsonOpts);
                }
                catch (JsonException ex)
                {
                    steps.Add(new("5. 解析 JSON", false, ex.Message));
                    return FailResult<List<WmsCellDto>>(sw, steps, ApiFailureStage.ParseResponse,
                        requestUrl, method, 200, body, "回應不是有效的 JSON 陣列");
                }

                if (all is null)
                {
                    steps.Add(new("5. 解析 JSON", false, "Deserialize 回傳 null"));
                    return FailResult<List<WmsCellDto>>(sw, steps, ApiFailureStage.ParseResponse,
                        requestUrl, method, 200, body, "JSON 解析結果為空");
                }

                var filtered = all.Where(predicate).ToList();

                steps.Add(new("5. 解析 JSON", true, $"WMS 共 {all.Count} 筆站點"));
                steps.Add(new(filterStepName, true, filterDetail(filtered.Count)));
                sw.Stop();
                return OkResult(sw, steps, requestUrl, method, 200, body, filtered, summary(filtered.Count));
            },
            cancellationToken);
    }

    /// <summary>輕量輪詢用 — GET /v2/robots/status</summary>
    public async Task<(bool Success, List<RobotStatusDto> Data, string? Error)> PollRobotsStatusAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return (false, [], "尚未登入");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "v2/robots/status");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await http.SendAsync(req, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return (false, [], $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

            var list = JsonSerializer.Deserialize<List<RobotStatusDto>>(body, JsonOpts) ?? [];
            return (true, list, null);
        }
        catch (TaskCanceledException)
        {
            return (false, [], "請求逾時");
        }
        catch (OperationCanceledException)
        {
            return (false, [], "請求逾時");
        }
        catch (Exception ex)
        {
            return (false, [], ApiErrorFormatter.FromException(ex));
        }
    }

    /// <summary>輕量輪詢用 — GET /api/simulation/tasks/active</summary>
    public async Task<(bool Success, List<ActiveSimulationTaskDto> Data, string? Error)> PollActiveSimulationTasksAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return (false, [], "尚未登入");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "api/simulation/tasks/active");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await http.SendAsync(req, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return (false, [], $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

            var list = JsonSerializer.Deserialize<List<ActiveSimulationTaskDto>>(body, JsonOpts) ?? [];
            return (true, list, null);
        }
        catch (TaskCanceledException)
        {
            return (false, [], "請求逾時");
        }
        catch (OperationCanceledException)
        {
            return (false, [], "請求逾時");
        }
        catch (Exception ex)
        {
            return (false, [], ApiErrorFormatter.FromException(ex));
        }
    }

    /// <summary>輕量輪詢用 — GET /api/simulation/amrs（補充 DispatchEnabled / SiteCode）</summary>
    public async Task<(bool Success, List<SimulationAmrDto> Data, string? Error)> PollSimulationAmrsAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return (false, [], "尚未登入");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "api/simulation/amrs");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await http.SendAsync(req, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return (false, [], $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

            var list = JsonSerializer.Deserialize<List<SimulationAmrDto>>(body, JsonOpts) ?? [];
            return (true, list, null);
        }
        catch (TaskCanceledException)
        {
            return (false, [], "請求逾時");
        }
        catch (OperationCanceledException)
        {
            return (false, [], "請求逾時");
        }
        catch (Exception ex)
        {
            return (false, [], ApiErrorFormatter.FromException(ex));
        }
    }

    /// <summary>輕量輪詢用 — GET /v2/fleets/status</summary>
    public async Task<(bool Success, List<FleetStatusDto> Data, string? Error)> PollFleetStatusAsync(
        string accessToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            return (false, [], "尚未登入");

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "v2/fleets/status");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await http.SendAsync(req, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                return (false, [], $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

            var list = JsonSerializer.Deserialize<List<FleetStatusDto>>(body, JsonOpts) ?? [];
            return (true, list, null);
        }
        catch (TaskCanceledException)
        {
            return (false, [], "請求逾時");
        }
        catch (OperationCanceledException)
        {
            return (false, [], "請求逾時");
        }
        catch (Exception ex)
        {
            return (false, [], ApiErrorFormatter.FromException(ex));
        }
    }

    /// <summary>GET /api/simulation/amrs — AMR 載運件數（Cake / Bobbin 車）</summary>
    public Task<ApiCallResult<List<SimulationAmrDto>>> GetSimulationAmrsAsync(
        string accessToken,
        CancellationToken cancellationToken = default) =>
        GetAuthorizedListAsync<SimulationAmrDto>(
            "api/simulation/amrs", accessToken, "AMR 模擬車輛", cancellationToken);

    /// <summary>GET /api/simulation/tasks/active — 進行中模擬任務</summary>
    public Task<ApiCallResult<List<ActiveSimulationTaskDto>>> GetActiveSimulationTasksAsync(
        string accessToken,
        CancellationToken cancellationToken = default) =>
        GetAuthorizedListAsync<ActiveSimulationTaskDto>(
            "api/simulation/tasks/active", accessToken, "進行中模擬任務", cancellationToken);

    /// <summary>POST /api/simulation/tasks — 模擬派車任務（含診斷）</summary>
    public Task<ApiCallResult<SimulationTaskCreatedDto>> CreateSimulationTaskDiagnosticAsync(
        string accessToken,
        SimulationTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<ApiDiagnosticStep>();
        var sw = Stopwatch.StartNew();
        const string path = "api/simulation/tasks";
        var requestUrl = BuildRequestUrl(path);
        const string method = "POST";

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            steps.Add(new("1. 檢查 Token", false, "access_token 為空"));
            return Task.FromResult(FailResult<SimulationTaskCreatedDto>(sw, steps, ApiFailureStage.Authorization,
                requestUrl, method, 0, null, "尚未登入"));
        }

        steps.Add(new("1. 檢查 Token", true, $"Bearer token 長度={accessToken.Length}"));
        steps.Add(new("2. 建立請求", true,
            $"POST {requestUrl}，{request.TaskKind}：{request.AssignedRobot} " +
            $"{FormatSiteRoute(request.SourceSiteCode, request.TargetSiteCode)}（{request.RequiredAmrType}）"));

        var json = JsonSerializer.Serialize(request, JsonOpts);
        steps.Add(new("3. Request Body", true, json));

        return SendAndParseAsync<SimulationTaskCreatedDto>(
            async () =>
            {
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                return await http.SendAsync(req, cancellationToken);
            },
            steps, sw, requestUrl, method,
            body =>
            {
                SimulationTaskCreatedDto? dto;
                try
                {
                    dto = JsonSerializer.Deserialize<SimulationTaskCreatedDto>(body, JsonOpts);
                }
                catch (JsonException ex)
                {
                    steps.Add(new("5. 解析 JSON", false, ex.Message));
                    return FailResult<SimulationTaskCreatedDto>(sw, steps, ApiFailureStage.ParseResponse,
                        requestUrl, method, 200, body, "回應不是有效的 JSON");
                }

                if (dto is null)
                {
                    steps.Add(new("5. 解析 JSON", false, "Deserialize 回傳 null"));
                    return FailResult<SimulationTaskCreatedDto>(sw, steps, ApiFailureStage.ParseResponse,
                        requestUrl, method, 200, body, "JSON 解析結果為空");
                }

                steps.Add(new("5. 解析 JSON", true, $"task_no={dto.TaskNo}，status={dto.Status}"));
                sw.Stop();
                return OkResult(sw, steps, requestUrl, method, 200, body, dto,
                    $"{request.AssignedRobot} {FormatSiteRoute(request.SourceSiteCode, request.TargetSiteCode)}：" +
                    $"{dto.TaskNo} ({dto.Status})");
            },
            cancellationToken);
    }

    /// <summary>POST /v2/flows/{flow_name} — AMR Modbus 任務（goal_amr + value_amr）</summary>
    public Task<ApiCallResult<TriggerFlowResultDto>> TriggerFlowDiagnosticAsync(
        string accessToken,
        string flowName,
        TriggerFlowRequest request,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<ApiDiagnosticStep>();
        var sw = Stopwatch.StartNew();
        var path = $"v2/flows/{flowName.Trim()}";
        var requestUrl = BuildRequestUrl(path);
        const string method = "POST";

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            steps.Add(new("1. 檢查 Token", false, "access_token 為空"));
            return Task.FromResult(FailResult<TriggerFlowResultDto>(sw, steps, ApiFailureStage.Authorization,
                requestUrl, method, 0, null, "尚未登入"));
        }

        var node = request.Args.Params.GetValueOrDefault(BufferFlowDispatchBuilder.NodeId);
        var goal = node?.GetValueOrDefault("goal_amr") ?? "";
        var robot = node?.GetValueOrDefault("assigned_robot") ?? "";
        steps.Add(new("1. 檢查 Token", true, $"Bearer token 長度={accessToken.Length}"));
        steps.Add(new("2. 建立請求", true,
            $"POST {requestUrl}，flow={flowName}，{goal} → {robot}"));

        var json = BufferFlowDispatchBuilder.Serialize(request);
        steps.Add(new("3. Request Body", true, json));

        return SendAndParseAsync<TriggerFlowResultDto>(
            async () =>
            {
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var req = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                return await http.SendAsync(req, cancellationToken);
            },
            steps, sw, requestUrl, method,
            body =>
            {
                TriggerFlowResultDto? dto;
                try
                {
                    dto = JsonSerializer.Deserialize<TriggerFlowResultDto>(body, JsonOpts);
                }
                catch (JsonException ex)
                {
                    steps.Add(new("5. 解析 JSON", false, ex.Message));
                    return FailResult<TriggerFlowResultDto>(sw, steps, ApiFailureStage.ParseResponse,
                        requestUrl, method, 200, body, "回應不是有效的 JSON");
                }

                if (dto is null)
                {
                    steps.Add(new("5. 解析 JSON", false, "Deserialize 回傳 null"));
                    return FailResult<TriggerFlowResultDto>(sw, steps, ApiFailureStage.ParseResponse,
                        requestUrl, method, 200, body, "JSON 解析結果為空");
                }

                var taskId = string.IsNullOrWhiteSpace(dto.TaskId) ? dto.FlowId : dto.TaskId;
                steps.Add(new("5. 解析 JSON", true,
                    $"status={dto.Status}，task_id={taskId}，robot={dto.AssignedRobot ?? robot}"));
                steps.Add(new("6. 驗證回應", true, "Flow 已觸發"));
                sw.Stop();
                return OkResult(sw, steps, requestUrl, method, 200, body, dto,
                    $"派車成功（{dto.Status}，task={taskId}）");
            },
            cancellationToken);
    }

    /// <summary>POST /api/simulation/tasks — MoveToWp 送車至等待點（含診斷）</summary>
    public Task<ApiCallResult<SimulationTaskCreatedDto>> CreateMoveToWpTaskDiagnosticAsync(
        string accessToken,
        string robotId,
        string targetSiteCode,
        string amrType,
        CancellationToken cancellationToken = default) =>
        CreateSimulationTaskDiagnosticAsync(
            accessToken,
            new SimulationTaskRequest
            {
                TaskKind = "MoveToWp",
                RequiredAmrType = amrType,
                TargetSiteCode = targetSiteCode,
                AssignedRobot = robotId,
                TriggerEvent = "fleet-init"
            },
            cancellationToken);

    static string FormatSiteRoute(string source, string target)
    {
        var hasSource = !string.IsNullOrWhiteSpace(source);
        var hasTarget = !string.IsNullOrWhiteSpace(target);
        if (hasSource && hasTarget) return $"{source} → {target}";
        if (hasTarget) return $"→ {target}";
        if (hasSource) return $"{source} →";
        return "";
    }

    /// <summary>POST /api/simulation/tasks — MoveToWp 送車至等待點</summary>
    public async Task<(bool Success, SimulationTaskCreatedDto? Data, string Error)> CreateMoveToWpTaskAsync(
        string accessToken,
        string robotId,
        string targetSiteCode,
        string amrType,
        CancellationToken cancellationToken = default)
    {
        var result = await CreateMoveToWpTaskDiagnosticAsync(
            accessToken, robotId, targetSiteCode, amrType, cancellationToken);
        return result.Success
            ? (true, result.Data, "")
            : (false, null, result.Summary);
    }

    Task<ApiCallResult<List<T>>> GetAuthorizedListAsync<T>(
        string path,
        string accessToken,
        string label,
        CancellationToken cancellationToken)
    {
        var steps = new List<ApiDiagnosticStep>();
        var sw = Stopwatch.StartNew();
        var requestUrl = BuildRequestUrl(path);
        const string method = "GET";

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            steps.Add(new("1. 檢查 Token", false, "access_token 為空，請先執行登入測試"));
            return Task.FromResult(FailResult<List<T>>(sw, steps, ApiFailureStage.Authorization,
                requestUrl, method, 0, null, "尚未登入，請先取得 Token"));
        }

        steps.Add(new("1. 檢查 Token", true, $"Bearer token 長度={accessToken.Length}"));
        steps.Add(new("2. 建立請求", true, $"GET {requestUrl}，Authorization=Bearer ***"));

        return SendAndParseAsync<List<T>>(
            () =>
            {
                var req = new HttpRequestMessage(HttpMethod.Get, path);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                return http.SendAsync(req, cancellationToken);
            },
            steps, sw, requestUrl, method,
            body =>
            {
                List<T>? list;
                try
                {
                    list = JsonSerializer.Deserialize<List<T>>(body, JsonOpts);
                }
                catch (JsonException ex)
                {
                    steps.Add(new("5. 解析 JSON", false, ex.Message));
                    return FailResult<List<T>>(sw, steps, ApiFailureStage.ParseResponse,
                        requestUrl, method, 200, body, "回應不是有效的 JSON 陣列");
                }

                if (list is null)
                {
                    steps.Add(new("5. 解析 JSON", false, "Deserialize 回傳 null"));
                    return FailResult<List<T>>(sw, steps, ApiFailureStage.ParseResponse,
                        requestUrl, method, 200, body, "JSON 解析結果為空");
                }

                steps.Add(new("5. 解析 JSON", true, $"共 {list.Count} 筆{label}"));
                steps.Add(new("6. 驗證資料", true, list.Count > 0 ? "已取得車輛清單" : "回傳空陣列（目前無車輛資料）"));
                sw.Stop();
                return OkResult(sw, steps, requestUrl, method, 200, body, list,
                    $"取得 {list.Count} 台車輛{label}");
            },
            cancellationToken);
    }

    async Task<ApiCallResult<T>> SendAndParseAsync<T>(
        Func<Task<HttpResponseMessage>> send,
        List<ApiDiagnosticStep> steps,
        Stopwatch sw,
        string requestUrl,
        string method,
        Func<string, ApiCallResult<T>> parseSuccess,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await send();
            steps.Add(new("3. 發送 HTTP", true, $"已收到伺服器回應（耗時 {sw.ElapsedMilliseconds} ms）"));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            var detail = DescribeException(ex);
            steps.Add(new("3. 發送 HTTP", false, $"逾時：{detail}"));
            return FailResult<T>(sw, steps, ApiFailureStage.SendRequest,
                requestUrl, method, 0, null, "請求逾時");
        }
        catch (HttpRequestException ex)
        {
            var detail = DescribeException(ex) + GuessNetworkHint(ex);
            steps.Add(new("3. 發送 HTTP", false, detail));
            return FailResult<T>(sw, steps, ApiFailureStage.SendRequest,
                requestUrl, method, 0, null, detail);
        }
        catch (Exception ex)
        {
            var detail = DescribeException(ex);
            steps.Add(new("3. 發送 HTTP", false, detail));
            return FailResult<T>(sw, steps, ApiFailureStage.SendRequest,
                requestUrl, method, 0, null, detail);
        }

        var statusCode = (int)response.StatusCode;
        var reason = response.ReasonPhrase ?? "";
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "(未知)";

        if (!response.IsSuccessStatusCode)
        {
            var apiDetail = TryReadDetail(body);
            steps.Add(new("4. HTTP 狀態碼", false,
                $"HTTP {statusCode} {reason}，Content-Type={contentType}" +
                (apiDetail is not null ? $"，detail={apiDetail}" : "")));

            var stage = statusCode == 401 ? ApiFailureStage.Authorization : ApiFailureStage.ReceiveResponse;
            var summary = statusCode switch
            {
                401 => "授權失敗（HTTP 401），Token 無效或已過期，請重新登入",
                404 => "端點不存在（HTTP 404）",
                _ => $"HTTP 錯誤 {statusCode} {reason}"
            };
            return FailResult<T>(sw, steps, stage, requestUrl, method, statusCode, body, summary);
        }

        steps.Add(new("4. HTTP 狀態碼", true,
            $"HTTP {statusCode} {reason}，Content-Type={contentType}，Body 長度={body.Length}"));

        return parseSuccess(body);
    }

    bool TryValidateConfig(
        List<ApiDiagnosticStep> steps,
        string requestUrl,
        string method,
        Stopwatch sw,
        out ApiCallResult<LoginData>? fail)
    {
        fail = null;
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
        {
            steps.Add(new("1. 讀取設定", false, "AmrApi:BaseUrl 為空"));
            fail = FailResult<LoginData>(sw, steps, ApiFailureStage.Configuration,
                requestUrl, method, 0, null, "AmrApi:BaseUrl 未設定");
            return false;
        }
        if (string.IsNullOrWhiteSpace(_options.Username) || string.IsNullOrWhiteSpace(_options.Password))
        {
            steps.Add(new("1. 讀取設定", false, "Username 或 Password 為空"));
            fail = FailResult<LoginData>(sw, steps, ApiFailureStage.Configuration,
                requestUrl, method, 0, null, "帳號或密碼未設定");
            return false;
        }
        if (!Uri.TryCreate(_options.BaseUrl.TrimEnd('/'), UriKind.Absolute, out _))
        {
            steps.Add(new("1. 讀取設定", false, $"BaseUrl 格式無效：{_options.BaseUrl}"));
            fail = FailResult<LoginData>(sw, steps, ApiFailureStage.Configuration,
                requestUrl, method, 0, null, "BaseUrl 格式無效");
            return false;
        }
        return true;
    }

    string BuildRequestUrl(string path) =>
        $"{_options.BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

    static ApiCallResult<T> OkResult<T>(
        Stopwatch sw,
        List<ApiDiagnosticStep> steps,
        string requestUrl,
        string method,
        int statusCode,
        string body,
        T data,
        string summary) =>
        new(true, summary, ApiFailureStage.None, requestUrl, method,
            statusCode, sw.ElapsedMilliseconds, steps, body, data);

    static ApiCallResult<T> FailResult<T>(
        Stopwatch sw,
        List<ApiDiagnosticStep> steps,
        ApiFailureStage stage,
        string requestUrl,
        string method,
        int statusCode,
        string? responseBody,
        string summary)
    {
        sw.Stop();
        return new(false, summary, stage, requestUrl, method,
            statusCode, sw.ElapsedMilliseconds, steps, responseBody, default);
    }

    static string DescribeException(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
            parts.Add($"{e.GetType().Name}: {e.Message}");
        return string.Join(" → ", parts);
    }

    static string GuessNetworkHint(HttpRequestException ex)
    {
        for (var e = ex.InnerException; e is not null; e = e.InnerException)
        {
            if (e is SocketException se)
            {
                return se.SocketErrorCode switch
                {
                    SocketError.ConnectionRefused => "。可能原因：Port 未開啟或服務未啟動",
                    SocketError.TimedOut => "。可能原因：網路不通或防火牆阻擋",
                    SocketError.HostNotFound or SocketError.NoData => "。可能原因：IP 無法解析",
                    _ => ""
                };
            }
        }
        return "";
    }

    static string? TryReadDetail(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("detail", out var detail))
                return detail.GetString();
        }
        catch { /* ignore */ }
        return null;
    }
}
