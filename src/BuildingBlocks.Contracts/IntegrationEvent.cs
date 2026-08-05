namespace BuildingBlocks.Contracts;

/// <summary>
/// Base cho integration event. Nằm ở project **BuildingBlocks.Contracts** — assembly *thuần khai báo*,
/// KHÔNG phụ thuộc gói nào (không Azure SDK, không EF, không ASP.NET).
///
/// Vì sao tách riêng: contract là thứ mọi service **buộc** phải chia sẻ; implementation (Service Bus,
/// Redis, Auth…) thì không. Gộp chung ⇒ sửa 1 dòng hạ tầng phải rebuild + redeploy TOÀN BỘ service,
/// mất đúng lợi ích lớn nhất của microservices là deploy độc lập.
/// </summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public string EventType => GetType().Name;

    /// <summary>
    /// **Major version của schema**. Publisher gắn vào message, consumer từ chối version lạ.
    ///
    /// Quy ước tiến hoá contract (rất quan trọng khi deploy độc lập — publisher và consumer
    /// KHÔNG bao giờ lên phiên bản cùng lúc):
    ///  • **Thêm field optional** ⇒ GIỮ NGUYÊN version. Consumer cũ bỏ qua field lạ, vẫn chạy.
    ///  • **Xoá/đổi tên/đổi kiểu field, đổi ngữ nghĩa** ⇒ **TĂNG version** (breaking).
    ///  • Đổi breaking thì publish **song song cả 2 version** cho tới khi mọi consumer đã nâng cấp,
    ///    rồi mới bỏ version cũ (expand → migrate → contract).
    ///
    /// Consumer chỉ xử lý version nó hiểu; version cao hơn ⇒ dead-letter kèm lý do rõ ràng,
    /// KHÔNG đoán mò — đoán sai trên dữ liệu tiền bạc còn tệ hơn là dừng lại.
    /// </summary>
    public int SchemaVersion { get; init; } = 1;
}

/// <summary>Version mà service này hiểu được, dùng để kiểm tra khi nhận message.</summary>
public static class SchemaCompatibility
{
    public const string MessagePropertyName = "schemaVersion";

    /// <summary>Version tối đa hiện hỗ trợ. Nâng khi consumer đã xử lý được schema mới.</summary>
    public const int MaxSupportedVersion = 1;

    public static bool IsSupported(int version) => version is >= 1 and <= MaxSupportedVersion;

    public static string Reason(int version) =>
        $"Schema version {version} không hỗ trợ (tối đa {MaxSupportedVersion}) — cần nâng cấp service này.";
}
