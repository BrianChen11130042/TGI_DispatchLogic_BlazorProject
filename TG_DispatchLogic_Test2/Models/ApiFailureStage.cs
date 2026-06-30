namespace TG_DispatchLogic_Test2.Models;

/// <summary>API 呼叫失敗時，失敗發生在哪個階段。</summary>
public enum ApiFailureStage
{
    None = 0,
    Configuration,
    SendRequest,
    ReceiveResponse,
    ParseResponse,
    ValidateResponse,
    Authorization
}
