namespace TG_DispatchLogic_Test2.Models;

public record ApiCallResult(
    bool Success,
    string Summary,
    ApiFailureStage FailureStage,
    string RequestUrl,
    string HttpMethod,
    int HttpStatusCode,
    long ElapsedMs,
    IReadOnlyList<ApiDiagnosticStep> Steps,
    string? ResponseBody)
{
    public string FailureStageLabel => FailureStage switch
    {
        ApiFailureStage.None              => "—",
        ApiFailureStage.Configuration     => "設定錯誤",
        ApiFailureStage.SendRequest       => "發送請求（網路層）",
        ApiFailureStage.ReceiveResponse   => "HTTP 回應錯誤",
        ApiFailureStage.ParseResponse     => "解析回應",
        ApiFailureStage.ValidateResponse  => "驗證回應內容",
        ApiFailureStage.Authorization     => "授權失敗",
        _                                 => FailureStage.ToString()
    };
}

public record ApiCallResult<T>(
    bool Success,
    string Summary,
    ApiFailureStage FailureStage,
    string RequestUrl,
    string HttpMethod,
    int HttpStatusCode,
    long ElapsedMs,
    IReadOnlyList<ApiDiagnosticStep> Steps,
    string? ResponseBody,
    T? Data)
    : ApiCallResult(Success, Summary, FailureStage, RequestUrl, HttpMethod,
        HttpStatusCode, ElapsedMs, Steps, ResponseBody);
