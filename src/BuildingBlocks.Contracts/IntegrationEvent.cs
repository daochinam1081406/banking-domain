namespace BuildingBlocks.Contracts;

/// <summary>
/// Base cho integration event. Assembly này không phụ thuộc gói nào — contract là thứ mọi
/// service buộc phải chia sẻ, implementation thì không; gộp chung là mất khả năng deploy độc lập.
/// </summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public string EventType => GetType().Name;

    /// <summary>
    /// Major version của schema. Thêm field optional thì giữ nguyên; xoá/đổi tên/đổi kiểu/đổi
    /// ngữ nghĩa thì tăng, và publish song song cả hai version tới khi mọi consumer đã nâng cấp.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;
}

public static class SchemaCompatibility
{
    public const string MessagePropertyName = "schemaVersion";
    public const int MaxSupportedVersion = 1;

    public static bool IsSupported(int version) => version is >= 1 and <= MaxSupportedVersion;

    public static string Reason(int version) =>
        $"Schema version {version} không hỗ trợ (tối đa {MaxSupportedVersion}) — cần nâng cấp service này.";
}
