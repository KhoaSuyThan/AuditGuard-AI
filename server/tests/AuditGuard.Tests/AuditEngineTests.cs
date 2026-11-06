using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AuditGuard.Application.DTOs;
using AuditGuard.Application.Interfaces;
using AuditGuard.Application.Services;
using AuditGuard.Domain.Entities;
using AuditGuard.Domain.Enums;
using Xunit;

namespace AuditGuard.Tests;

public class MockAiClient : IAiEngineClient
{
    public Task<List<ThreeWayRowDto>> MatchItemsAsync(
        ICollection<POItem> poItems,
        ICollection<InvoiceItem> invoiceItems,
        ICollection<GRItem> grItems)
    {
        var rows = new List<ThreeWayRowDto>();
        foreach (var po in poItems)
        {
            var inv = new List<InvoiceItem>(invoiceItems)[0];
            var gr = new List<GRItem>(grItems)[0];
            rows.Add(new ThreeWayRowDto
            {
                PoItemName = po.ItemName,
                PoQuantity = po.Quantity,
                PoUnitPrice = po.UnitPrice,
                GrnQuantityReceived = gr.QuantityReceived,
                InvoiceItemName = inv.ItemName,
                InvoiceQuantity = inv.Quantity,
                InvoiceUnitPrice = inv.UnitPrice,
                SimilarityScore = 0.95m,
                MatchStatus = "MATCHED"
            });
        }
        return Task.FromResult(rows);
    }
}

public class AuditEngineTests
{
    private readonly IAuditEngine _engine;

    public AuditEngineTests()
    {
        _engine = new AuditEngine(new MockAiClient());
    }

    [Fact]
    public async Task Scenario01_PerfectMatch_ShouldReturnPassedWithZeroDiscrepancy()
    {
        var po = new PurchaseOrder { PoNumber = "PO-01", OrderDate = new DateOnly(2026, 11, 1), Subtotal = 100000, TaxAmount = 8000, TotalAmount = 108000 };
        po.Items.Add(new POItem { ItemName = "Giấy in A4", Quantity = 10, UnitPrice = 10000, TotalPrice = 100000 });

        var grn = new GoodsReceipt { GrnNumber = "GRN-01", ReceivedDate = new DateOnly(2026, 11, 2) };
        grn.Items.Add(new GRItem { ItemName = "Giấy in A4", QuantityReceived = 10 });

        var inv = new Invoice { InvoiceNumber = "HD-01", InvoiceDate = new DateOnly(2026, 11, 2), Subtotal = 100000, TaxRate = 8.0m, TaxAmount = 8000, TotalPayable = 108000 };
        inv.Items.Add(new InvoiceItem { ItemName = "Giấy in A4", Quantity = 10, UnitPrice = 10000, TotalAmount = 100000 });

        var result = await _engine.ExecuteAuditAsync(po, grn, inv);

        Assert.Equal("PASSED", result.Status);
        Assert.Equal(0, result.DiscrepancyCount);
        Assert.Equal(0, result.TotalFinancialDiff);
    }

    [Fact]
    public async Task Scenario02_PriceMismatch_ShouldFlagCritical()
    {
        var po = new PurchaseOrder { PoNumber = "PO-02", OrderDate = new DateOnly(2026, 11, 1), Subtotal = 3500000, TaxAmount = 280000, TotalAmount = 3780000 };
        po.Items.Add(new POItem { ItemName = "Hộp mực in HP 85A", Quantity = 10, UnitPrice = 350000, TotalPrice = 3500000 });

        var grn = new GoodsReceipt { GrnNumber = "GRN-02", ReceivedDate = new DateOnly(2026, 11, 2) };
        grn.Items.Add(new GRItem { ItemName = "Mực in HP 85A", QuantityReceived = 10 });

        // Nhà cung cấp tăng giá lên 420.000đ
        var inv = new Invoice { InvoiceNumber = "HD-02", InvoiceDate = new DateOnly(2026, 11, 2), Subtotal = 4200000, TaxRate = 8.0m, TaxAmount = 336000, TotalPayable = 4536000 };
        inv.Items.Add(new InvoiceItem { ItemName = "Hộp mực Laser CE285A", Quantity = 10, UnitPrice = 420000, TotalAmount = 4200000 });

        var result = await _engine.ExecuteAuditAsync(po, grn, inv);

        Assert.Equal("FLAGGED", result.Status);
        Assert.Contains(result.Discrepancies, d => d.DiscrepancyType == "PriceMismatch");
        Assert.True(result.TotalFinancialDiff > 0);
    }

    [Fact]
    public async Task Scenario04_DuplicateInvoice_ShouldReject()
    {
        var po = new PurchaseOrder { PoNumber = "PO-04", OrderDate = new DateOnly(2026, 11, 1), Subtotal = 15000000, TaxAmount = 1200000, TotalAmount = 16200000 };
        po.Items.Add(new POItem { ItemName = "Dịch vụ vận chuyển", Quantity = 1, UnitPrice = 15000000, TotalPrice = 15000000 });

        var grn = new GoodsReceipt { GrnNumber = "GRN-04", ReceivedDate = new DateOnly(2026, 11, 2) };
        grn.Items.Add(new GRItem { ItemName = "Nghiệm thu vận chuyển", QuantityReceived = 1 });

        var inv = new Invoice { InvoiceNumber = "HD-DUPLICATE", VendorTaxCode = "0311223344", InvoiceDate = new DateOnly(2026, 11, 2), Subtotal = 15000000, TaxRate = 8.0m, TaxAmount = 1200000, TotalPayable = 16200000 };
        inv.Items.Add(new InvoiceItem { ItemName = "Dịch vụ vận chuyển", Quantity = 1, UnitPrice = 15000000, TotalAmount = 15000000 });

        var history = new List<Invoice>
        {
            new Invoice { Id = Guid.NewGuid(), InvoiceNumber = "HD-DUPLICATE", VendorTaxCode = "0311223344", PaymentStatus = "PAID", InvoiceDate = new DateOnly(2026, 10, 15) }
        };

        var result = await _engine.ExecuteAuditAsync(po, grn, inv, history);

        Assert.Equal("REJECTED", result.Status);
        Assert.Contains(result.Discrepancies, d => d.DiscrepancyType == "DuplicateInvoice");
        Assert.Equal(16200000, result.TotalFinancialDiff);
    }
}
