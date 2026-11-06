using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuditGuard.Application.DTOs;
using AuditGuard.Application.Interfaces;
using AuditGuard.Domain.Entities;
using AuditGuard.Domain.Enums;

namespace AuditGuard.Application.Services;

public class AuditEngine : IAuditEngine
{
    private readonly IAiEngineClient _aiEngineClient;

    public AuditEngine(IAiEngineClient aiEngineClient)
    {
        _aiEngineClient = aiEngineClient;
    }

    public async Task<AuditSessionDto> ExecuteAuditAsync(
        PurchaseOrder po,
        GoodsReceipt grn,
        Invoice invoice,
        List<Invoice>? historicalInvoices = null)
    {
        var session = new AuditSession
        {
            SessionCode = $"AUDIT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..6].ToUpper()}",
            PoId = po.Id,
            GrnId = grn.Id,
            InvoiceId = invoice.Id,
            Status = AuditStatus.Passed
        };

        var discrepancies = new List<AuditDiscrepancy>();

        // 1. RULE: KIỂM TRA TRÙNG LẶP HÓA ĐƠN (Duplicate Invoice Check)
        if (historicalInvoices != null && historicalInvoices.Any())
        {
            var duplicate = historicalInvoices.FirstOrDefault(h =>
                h.InvoiceNumber.Equals(invoice.InvoiceNumber, StringComparison.OrdinalIgnoreCase) &&
                h.VendorTaxCode.Equals(invoice.VendorTaxCode, StringComparison.OrdinalIgnoreCase) &&
                h.Id != invoice.Id &&
                h.PaymentStatus.Equals("PAID", StringComparison.OrdinalIgnoreCase));

            if (duplicate != null)
            {
                discrepancies.Add(new AuditDiscrepancy
                {
                    SessionId = session.Id,
                    DiscrepancyType = DiscrepancyType.DuplicateInvoice,
                    Severity = DiscrepancySeverity.Critical,
                    ItemName = "Toàn bộ hóa đơn",
                    ExpectedValue = "Hóa đơn mới chưa thanh toán",
                    ActualValue = $"Trùng hóa đơn #{duplicate.InvoiceNumber} đã thanh toán ngày {duplicate.InvoiceDate:dd/MM/yyyy}",
                    FinancialImpact = invoice.TotalPayable,
                    Description = $"CẢNH BÁO GIAN LẬN: Hóa đơn #{invoice.InvoiceNumber} của NCC (MST: {invoice.VendorTaxCode}) đã được chi trả trước đó. Nguy cơ thanh toán trùng!"
                });
                session.Status = AuditStatus.Rejected;
            }
        }

        // 2. RULE: KIỂM TRA BẤT THƯỜNG THỜI GIAN (Invoice date trước PO date)
        if (invoice.InvoiceDate < po.OrderDate)
        {
            discrepancies.Add(new AuditDiscrepancy
            {
                SessionId = session.Id,
                DiscrepancyType = DiscrepancyType.TimingAnomaly,
                Severity = DiscrepancySeverity.Warning,
                ItemName = "Ngày lập chứng từ",
                ExpectedValue = $"Hóa đơn phải sau ngày đặt hàng ({po.OrderDate:dd/MM/yyyy})",
                ActualValue = $"Ngày hóa đơn: {invoice.InvoiceDate:dd/MM/yyyy}",
                FinancialImpact = 0,
                Description = "Bất thường trình tự thời gian: Ngày xuất hóa đơn diễn ra trước ngày ban hành Đơn đặt hàng (PO)."
            });
        }

        // 3. RULE: GỌI AI MATCHING & SO KHỚP 3 CHIỀU TỪNG DÒNG HÀNG
        var comparisonRows = await _aiEngineClient.MatchItemsAsync(po.Items, invoice.Items, grn.Items);

        foreach (var row in comparisonRows)
        {
            // 3.1. Mặt hàng xuất hiện trên Hóa đơn nhưng KHÔNG có trong PO (Extra Item)
            if (string.IsNullOrEmpty(row.PoItemName) && !string.IsNullOrEmpty(row.InvoiceItemName))
            {
                var extraImpact = row.InvoiceQuantity * row.InvoiceUnitPrice;
                discrepancies.Add(new AuditDiscrepancy
                {
                    SessionId = session.Id,
                    DiscrepancyType = DiscrepancyType.ExtraItem,
                    Severity = DiscrepancySeverity.Critical,
                    ItemName = row.InvoiceItemName,
                    ExpectedValue = "Không có trong Đơn đặt hàng (PO)",
                    ActualValue = $"Kê khống {row.InvoiceQuantity:N0} x {row.InvoiceUnitPrice:N0} đ",
                    FinancialImpact = extraImpact,
                    Description = $"Hàng/phí lạ xuất hiện trên hóa đơn: '{row.InvoiceItemName}' không có trong thỏa thuận Đơn đặt hàng."
                });
                row.MatchStatus = "EXTRA_ITEM";
                continue;
            }

            // 3.2. Mặt hàng có trong PO nhưng hóa đơn không có
            if (!string.IsNullOrEmpty(row.PoItemName) && string.IsNullOrEmpty(row.InvoiceItemName))
            {
                row.MatchStatus = "UNMATCHED";
                continue;
            }

            // 3.3. Kiểm tra đơn giá (PO Price vs Invoice Price)
            if (row.InvoiceUnitPrice > row.PoUnitPrice && row.PoUnitPrice > 0)
            {
                var priceDiff = (row.InvoiceUnitPrice - row.PoUnitPrice) * row.InvoiceQuantity;
                var taxMultiplier = 1 + (invoice.TaxRate / 100m);
                var totalImpact = Math.Round(priceDiff * taxMultiplier, 2);

                discrepancies.Add(new AuditDiscrepancy
                {
                    SessionId = session.Id,
                    DiscrepancyType = DiscrepancyType.PriceMismatch,
                    Severity = DiscrepancySeverity.Critical,
                    ItemName = row.InvoiceItemName,
                    ExpectedValue = $"{row.PoUnitPrice:N0} đ",
                    ActualValue = $"{row.InvoiceUnitPrice:N0} đ",
                    FinancialImpact = totalImpact,
                    Description = $"Lệch đơn giá: Hóa đơn tăng giá {((row.InvoiceUnitPrice - row.PoUnitPrice) / row.PoUnitPrice):P1} so với PO."
                });
                row.MatchStatus = "PRICE_MISMATCH";
            }

            // 3.4. Kiểm tra số lượng TOÀN DIỆN 3 CHIỀU:
            // a. Hóa đơn đòi nhiều hơn Kho thực nhận (Thanh toán khống)
            if (row.InvoiceQuantity > row.GrnQuantityReceived)
            {
                var missingQty = row.InvoiceQuantity - row.GrnQuantityReceived;
                var taxMultiplier = 1 + (invoice.TaxRate / 100m);
                var financialImpact = Math.Round(missingQty * row.InvoiceUnitPrice * taxMultiplier, 2);

                discrepancies.Add(new AuditDiscrepancy
                {
                    SessionId = session.Id,
                    DiscrepancyType = DiscrepancyType.QuantityMismatch,
                    Severity = DiscrepancySeverity.Critical,
                    ItemName = row.InvoiceItemName,
                    ExpectedValue = $"Thực nhập kho: {row.GrnQuantityReceived:N0}",
                    ActualValue = $"Hóa đơn đòi: {row.InvoiceQuantity:N0}",
                    FinancialImpact = financialImpact,
                    Description = $"Giao thiếu hàng: Kho chỉ nhận {row.GrnQuantityReceived:N0} nhưng hóa đơn đòi thanh toán {row.InvoiceQuantity:N0}."
                });
                row.MatchStatus = "QTY_MISMATCH";
            }
            // b. Kho nhận thiếu so với PO đặt hàng
            else if (row.GrnQuantityReceived < row.PoQuantity && row.PoQuantity > 0)
            {
                discrepancies.Add(new AuditDiscrepancy
                {
                    SessionId = session.Id,
                    DiscrepancyType = DiscrepancyType.QuantityMismatch,
                    Severity = DiscrepancySeverity.Warning,
                    ItemName = row.PoItemName,
                    ExpectedValue = $"Đơn đặt hàng: {row.PoQuantity:N0}",
                    ActualValue = $"Kho mới nhận: {row.GrnQuantityReceived:N0}",
                    FinancialImpact = 0,
                    Description = $"Giao hàng từng đợt / Chưa đủ: Kho mới nhận {row.GrnQuantityReceived:N0}/{row.PoQuantity:N0} sản phẩm."
                });
            }
            // c. Hóa đơn đòi số lượng vượt quá PO cho phép
            else if (row.InvoiceQuantity > row.PoQuantity && row.PoQuantity > 0)
            {
                var overQty = row.InvoiceQuantity - row.PoQuantity;
                var financialImpact = Math.Round(overQty * row.InvoiceUnitPrice * (1 + invoice.TaxRate / 100m), 2);
                discrepancies.Add(new AuditDiscrepancy
                {
                    SessionId = session.Id,
                    DiscrepancyType = DiscrepancyType.QuantityMismatch,
                    Severity = DiscrepancySeverity.Critical,
                    ItemName = row.InvoiceItemName,
                    ExpectedValue = $"Hạn mức PO: {row.PoQuantity:N0}",
                    ActualValue = $"Hóa đơn đòi: {row.InvoiceQuantity:N0}",
                    FinancialImpact = financialImpact,
                    Description = $"Vượt hạn mức PO: Hóa đơn tính số lượng vượt quá đơn đặt hàng được duyệt ({row.InvoiceQuantity} > {row.PoQuantity})."
                });
                row.MatchStatus = "QTY_MISMATCH";
            }
        }

        // 4. RULE: KIỂM TRA TỔNG TIỀN VÀ CỘNG DỒN DÒNG HÀNG (Reconciliation)
        // 4.1. Tổng dòng hàng vs Subtotal hóa đơn
        var sumLineItems = invoice.Items.Sum(i => i.TotalAmount);
        if (Math.Abs(sumLineItems - invoice.Subtotal) > 100m && invoice.Items.Any())
        {
            var lineDiff = Math.Abs(sumLineItems - invoice.Subtotal);
            discrepancies.Add(new AuditDiscrepancy
            {
                SessionId = session.Id,
                DiscrepancyType = DiscrepancyType.PriceMismatch,
                Severity = DiscrepancySeverity.Critical,
                ItemName = "Tổng tiền trước thuế (Subtotal)",
                ExpectedValue = $"Tổng cộng các dòng: {sumLineItems:N0} đ",
                ActualValue = $"Subtotal trên hóa đơn: {invoice.Subtotal:N0} đ",
                FinancialImpact = lineDiff,
                Description = $"Sai lệch cộng dồn: Tổng thành tiền các dòng hàng ({sumLineItems:N0} đ) không khớp với Subtotal ({invoice.Subtotal:N0} đ)."
            });
        }

        // 4.2. Kiểm tra thuế VAT
        var calculatedTax = Math.Round(invoice.Subtotal * (invoice.TaxRate / 100m), 2);
        if (Math.Abs(calculatedTax - invoice.TaxAmount) > 1000m)
        {
            var taxDiff = Math.Abs(calculatedTax - invoice.TaxAmount);
            discrepancies.Add(new AuditDiscrepancy
            {
                SessionId = session.Id,
                DiscrepancyType = DiscrepancyType.TaxRateMismatch,
                Severity = DiscrepancySeverity.Warning,
                ItemName = "Tiền thuế GTGT",
                ExpectedValue = $"{calculatedTax:N0} đ (Tính theo {invoice.TaxRate}%)",
                ActualValue = $"{invoice.TaxAmount:N0} đ",
                FinancialImpact = taxDiff,
                Description = $"Sai lệch tiền thuế GTGT trên hóa đơn chênh lệch {taxDiff:N0} đ."
            });
        }

        // 4.3. Kiểm tra Total Payable vs Subtotal + Tax
        var expectedTotal = invoice.Subtotal + invoice.TaxAmount;
        if (Math.Abs(expectedTotal - invoice.TotalPayable) > 100m)
        {
            var totalDiff = Math.Abs(expectedTotal - invoice.TotalPayable);
            discrepancies.Add(new AuditDiscrepancy
            {
                SessionId = session.Id,
                DiscrepancyType = DiscrepancyType.PriceMismatch,
                Severity = DiscrepancySeverity.Critical,
                ItemName = "Tổng tiền thanh toán (Total Payable)",
                ExpectedValue = $"Subtotal + Thuế: {expectedTotal:N0} đ",
                ActualValue = $"Hóa đơn ghi: {invoice.TotalPayable:N0} đ",
                FinancialImpact = totalDiff,
                Description = $"Sai lệch tổng thanh toán: Hóa đơn tính sai tổng thanh toán chênh lệch {totalDiff:N0} đ."
            });
        }

        // Cập nhật trạng thái
        if (session.Status != AuditStatus.Rejected)
        {
            session.Status = discrepancies.Any() ? AuditStatus.Flagged : AuditStatus.Passed;
        }

        session.DiscrepancyCount = discrepancies.Count;
        session.TotalFinancialDiff = discrepancies.Sum(d => d.FinancialImpact);

        return new AuditSessionDto
        {
            Id = session.Id,
            SessionCode = session.SessionCode,
            Status = session.Status.ToString().ToUpper(),
            DiscrepancyCount = session.DiscrepancyCount,
            TotalFinancialDiff = session.TotalFinancialDiff,
            PoNumber = po.PoNumber,
            GrnNumber = grn.GrnNumber,
            InvoiceNumber = invoice.InvoiceNumber,
            VendorName = invoice.VendorName ?? po.Vendor?.Name ?? "N/A",
            CreatedAt = session.CreatedAt,
            Discrepancies = discrepancies.Select(d => new AuditDiscrepancyDto
            {
                Id = d.Id,
                DiscrepancyType = d.DiscrepancyType.ToString(),
                Severity = d.Severity.ToString(),
                ItemName = d.ItemName,
                ExpectedValue = d.ExpectedValue,
                ActualValue = d.ActualValue,
                FinancialImpact = d.FinancialImpact,
                Description = d.Description
            }).ToList(),
            ComparisonRows = comparisonRows
        };
    }
}
