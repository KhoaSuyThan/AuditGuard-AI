using System;
using System.Text;
using AuditGuard.Application.DTOs;
using AuditGuard.Application.Interfaces;

namespace AuditGuard.Application.Services;

public class DisputeEmailService : IDisputeEmailService
{
    public DisputeEmailDto GenerateDisputeEmail(AuditSessionDto auditSession)
    {
        var subject = $"[AUDIT-DISPUTE] Yêu cầu hiệu chỉnh Hóa đơn #{auditSession.InvoiceNumber} - {auditSession.VendorName}";
        var sb = new StringBuilder();

        sb.AppendLine($"Kính gửi Quý Đối tác {auditSession.VendorName},");
        sb.AppendLine();
        sb.AppendLine($"Bộ phận Kiểm toán & Kế toán công ty chúng tôi xin thông báo kết quả đối soát tự động (3-Way Matching) cho Hóa đơn số #{auditSession.InvoiceNumber}.");
        sb.AppendLine($"Hệ thống phát hiện {auditSession.DiscrepancyCount} điểm sai lệch cần hiệu chỉnh với tổng số tiền chênh lệch là {auditSession.TotalFinancialDiff:N0} VNĐ.");
        sb.AppendLine();
        sb.AppendLine("--- CHI TIẾT CÁC ĐIỂM SAI LỆCH PHÁT HIỆN ĐƯỢC ---");

        int index = 1;
        foreach (var disc in auditSession.Discrepancies)
        {
            sb.AppendLine($"{index}. Mặt hàng / Hạng mục: {disc.ItemName}");
            sb.AppendLine($"   - Phân loại lỗi: {disc.DiscrepancyType}");
            sb.AppendLine($"   - Giá trị quy định (PO/Kho): {disc.ExpectedValue}");
            sb.AppendLine($"   - Giá trị ghi trên Hóa đơn: {disc.ActualValue}");
            sb.AppendLine($"   - Chênh lệch tài chính: {disc.FinancialImpact:N0} VNĐ");
            sb.AppendLine($"   - Chi tiết: {disc.Description}");
            sb.AppendLine();
            index++;
        }

        sb.AppendLine("Đề nghị Quý đối tác kiểm tra lại số liệu và gửi lại Hóa đơn điều chỉnh hợp lệ để công ty chúng tôi tiến hành thanh toán đúng hạn.");
        sb.AppendLine();
        sb.AppendLine("Trân trọng,");
        sb.AppendLine("Bộ phận Kiểm toán Tự động AuditGuard-AI");

        return new DisputeEmailDto
        {
            RecipientEmail = "ketoan@vendor.com",
            Subject = subject,
            BodyPlainText = sb.ToString(),
            BodyHtml = sb.ToString().Replace("\n", "<br/>")
        };
    }
}
