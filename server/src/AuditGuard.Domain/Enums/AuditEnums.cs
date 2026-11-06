namespace AuditGuard.Domain.Enums;

/// <summary>
/// Trạng thái của phiên đối soát kiểm toán
/// </summary>
public enum AuditStatus
{
    Pending,
    Passed,
    Flagged,
    Rejected
}

/// <summary>
/// Phân loại lỗi sai lệch chứng từ
/// </summary>
public enum DiscrepancyType
{
    PriceMismatch,      // Lệch đơn giá so với đơn đặt hàng
    QuantityMismatch,   // Lệch số lượng thực nhập kho so với hóa đơn
    DuplicateInvoice,   // Trùng lặp số hóa đơn đã thanh toán
    TaxRateMismatch,    // Sai lệch thuế suất VAT
    TimingAnomaly,      // Ngày hóa đơn xuất trước ngày lập đơn đặt hàng
    ExtraItem           // Mặt hàng xuất khống trên hóa đơn không có trong PO
}

/// <summary>
/// Mức độ nghiêm trọng của cảnh báo
/// </summary>
public enum DiscrepancySeverity
{
    Info,
    Warning,
    Critical
}
