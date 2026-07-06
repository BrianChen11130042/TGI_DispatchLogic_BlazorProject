namespace TG_DispatchLogic_Test2.Services;

/// <summary>將 API / 連線錯誤轉成簡短使用者可讀訊息。</summary>
public static class ApiErrorFormatter
{
    public static string FromException(Exception ex)
    {
        if (ex is TaskCanceledException or OperationCanceledException)
            return "請求逾時";

        var msg = DescribeChain(ex);
        return FromMessage(msg);
    }

    public static string FromMessage(string? message) => FromMessage(message, "連線失敗");

    public static string FromMessage(string? message, string fallback)
    {
        if (string.IsNullOrWhiteSpace(message))
            return fallback;

        var m = message.Trim();
        if (m.Contains("拒絕", StringComparison.OrdinalIgnoreCase)
            || m.Contains("refused", StringComparison.OrdinalIgnoreCase))
            return "目標電腦拒絕連線（請確認服務已啟動）";

        if (m.Contains("逾時", StringComparison.OrdinalIgnoreCase)
            || m.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || m.Contains("canceled", StringComparison.OrdinalIgnoreCase)
            || m.Contains("cancelled", StringComparison.OrdinalIgnoreCase))
            return "請求逾時";

        if (m.Contains("一次只能用一個通訊端", StringComparison.Ordinal)
            || m.Contains("10048", StringComparison.Ordinal))
            return "Modbus 連線過於頻繁，請稍後再試";

        if (m.Contains("401", StringComparison.Ordinal))
            return "授權失敗，將重新登入";

        var first = m.Split('→')[0].Trim();
        if (first.StartsWith("HttpRequestException:", StringComparison.OrdinalIgnoreCase))
            first = first["HttpRequestException:".Length..].Trim();
        if (first.StartsWith("SocketException:", StringComparison.OrdinalIgnoreCase))
            first = first["SocketException:".Length..].Trim();
        if (first.StartsWith("TaskCanceledException:", StringComparison.OrdinalIgnoreCase))
            return "請求逾時";

        return first.Length > 120 ? first[..120] + "…" : first;
    }

    public static string Modbus(string? message) =>
        FromMessage(message, "Modbus 讀取失敗");

    static string DescribeChain(Exception ex)
    {
        var parts = new List<string>();
        for (var e = ex; e is not null; e = e.InnerException)
            parts.Add(e.Message);
        return string.Join(" → ", parts);
    }
}
