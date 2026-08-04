namespace BuildingBlocks.Observability;

/// <summary>
/// Correlation ID theo suốt 1 nghiệp vụ: HTTP request → gRPC call → outbox row → message →
/// consumer ở service khác. Không có nó thì debug sự cố trong hệ event-driven gần như bất khả thi
/// (log 2 service rời rạc, không nối được với nhau).
///
/// Dùng AsyncLocal để publisher/repository lấy được mà không phải truyền tham số xuyên mọi lớp.
/// </summary>
public static class CorrelationContext
{
    public const string HeaderName = "X-Correlation-Id";
    public const string MessagePropertyName = "correlationId";
    public const string LogPropertyName = "CorrelationId";

    private static readonly AsyncLocal<string?> Current = new();

    public static string? Id => Current.Value;

    public static void Set(string correlationId) => Current.Value = correlationId;

    /// <summary>Lấy id hiện tại, sinh mới nếu chưa có (vd job nền không đi từ HTTP request).</summary>
    public static string GetOrCreate()
    {
        if (string.IsNullOrWhiteSpace(Current.Value))
            Current.Value = Guid.NewGuid().ToString("N");
        return Current.Value!;
    }
}
