using System.Collections.Generic;
using System.Threading.Tasks;
using AuditGuard.Application.DTOs;
using AuditGuard.Domain.Entities;

namespace AuditGuard.Application.Interfaces;

public interface IAuditEngine
{
    /// <summary>
    /// Thực thi toàn bộ quy trình kiểm toán đối soát 3 chiều
    /// </summary>
    Task<AuditSessionDto> ExecuteAuditAsync(
        PurchaseOrder po,
        GoodsReceipt grn,
        Invoice invoice,
        List<Invoice>? historicalInvoices = null);
}

public interface IDisputeEmailService
{
    /// <summary>
    /// Tự động sinh nội dung email khiếu nại dựa trên các sai lệch trong phiên đối soát
    /// </summary>
    DisputeEmailDto GenerateDisputeEmail(AuditSessionDto auditSession);
}

public interface IAiEngineClient
{
    Task<List<ThreeWayRowDto>> MatchItemsAsync(
        ICollection<POItem> poItems,
        ICollection<InvoiceItem> invoiceItems,
        ICollection<GRItem> grItems);
}
