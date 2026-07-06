using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using TG_DispatchLogic_Test2.Models;

namespace TG_DispatchLogic_Test2.Services;

/// <summary>呼叫 SimulateCode 停車對照 REST API（無需 Token）。</summary>
public class SimulateCodeApiClient(HttpClient http)
{
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public Task<ApiCallResult<T>> GetAsync<T>(
        string baseUrl,
        string relativePath,
        string label,
        CancellationToken cancellationToken = default)
    {
        var steps = new List<ApiDiagnosticStep>();
        var sw = Stopwatch.StartNew();
        var requestUrl = BuildRequestUrl(baseUrl, relativePath);
        const string method = "GET";

        if (!TryValidateBaseUrl<T>(baseUrl, steps, requestUrl, method, sw, out var configFail))
            return Task.FromResult(configFail!);

        steps.Add(new("2. 建立請求", true, $"GET {requestUrl}"));

        return SendAndParseAsync<T>(
            () => http.GetAsync(requestUrl, cancellationToken),
            steps, sw, requestUrl, method,
            body =>
            {
                T? data;
                try
                {
                    data = JsonSerializer.Deserialize<T>(body, JsonOpts);
                }
                catch (Exception ex)
                {
                    steps.Add(new("5. 解析 JSON", false, ex.Message));
                    return FailResult<T>(sw, steps, ApiFailureStage.ParseResponse,
                        requestUrl, method, 200, body, "JSON 解析失敗");
                }

                if (data is null)
                {
                    steps.Add(new("5. 解析 JSON", false, "Deserialize 回傳 null"));
                    return FailResult<T>(sw, steps, ApiFailureStage.ParseResponse,
                        requestUrl, method, 200, body, "JSON 解析結果為空");
                }

                steps.Add(new("5. 解析 JSON", true, label));
                sw.Stop();
                return OkResult(sw, steps, requestUrl, method, 200, body, data, $"{label} 取得成功");
            },
            cancellationToken);
    }

    static bool TryValidateBaseUrl<T>(
        string baseUrl,
        List<ApiDiagnosticStep> steps,
        string requestUrl,
        string method,
        Stopwatch sw,
        out ApiCallResult<T>? fail)
    {
        fail = null;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            steps.Add(new("1. 讀取設定", false, "API BaseUrl 為空"));
            fail = FailResult<T>(sw, steps, ApiFailureStage.Configuration,
                requestUrl, method, 0, null, "SimulateCode API BaseUrl 未設定");
            return false;
        }

        if (!Uri.TryCreate(baseUrl.TrimEnd('/'), UriKind.Absolute, out _))
        {
            steps.Add(new("1. 讀取設定", false, $"BaseUrl 格式無效：{baseUrl}"));
            fail = FailResult<T>(sw, steps, ApiFailureStage.Configuration,
                requestUrl, method, 0, null, "API BaseUrl 格式無效");
            return false;
        }

        steps.Add(new("1. 讀取設定", true, $"BaseUrl={baseUrl.TrimEnd('/')}"));
        return true;
    }

    static string BuildRequestUrl(string baseUrl, string path) =>
        $"{baseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

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
            steps.Add(new("4. HTTP 狀態碼", false,
                $"HTTP {statusCode} {reason}，Content-Type={contentType}"));
            var summary = statusCode switch
            {
                404 => "端點不存在（HTTP 404）",
                _ => $"HTTP 錯誤 {statusCode} {reason}"
            };
            return FailResult<T>(sw, steps, ApiFailureStage.ReceiveResponse,
                requestUrl, method, statusCode, body, summary);
        }

        steps.Add(new("4. HTTP 狀態碼", true,
            $"HTTP {statusCode} {reason}，Content-Type={contentType}，Body 長度={body.Length}"));

        return parseSuccess(body);
    }

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
                    SocketError.ConnectionRefused => "。可能原因：SimulateCode 未啟動或 Port 錯誤",
                    SocketError.TimedOut => "。可能原因：網路不通或防火牆阻擋",
                    SocketError.HostNotFound or SocketError.NoData => "。可能原因：IP 無法解析",
                    _ => ""
                };
            }
        }
        return "";
    }
}
