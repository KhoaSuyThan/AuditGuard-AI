using System;
using System.Collections.Generic;
using AuditGuard.Domain.Enums;
using Pgvector;

namespace AuditGuard.Domain.Entities;

public class Vendor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string TaxCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? ContactEmail { get; set; }
    public string RiskLevel { get; set; } = "LOW";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PurchaseOrder> PurchaseOrders { get; set; } = new List<PurchaseOrder>();
}

public class PurchaseOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string PoNumber { get; set; } = string.Empty;
    public Guid? VendorId { get; set; }
    public Vendor? Vendor { get; set; }
    public DateOnly OrderDate { get; set; }
    public string Currency { get; set; } = "VND";
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "APPROVED";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<POItem> Items { get; set; } = new List<POItem>();
}

public class POItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PoId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public int LineNumber { get; set; }
    public string? ItemCode { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "cái";
    public decimal UnitPrice { get; set; }
    public decimal TotalPrice { get; set; }
    public Vector? Embedding { get; set; }
}

public class GoodsReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GrnNumber { get; set; } = string.Empty;
    public Guid? PoId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public DateOnly ReceivedDate { get; set; }
    public string? WarehouseKeeper { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<GRItem> Items { get; set; } = new List<GRItem>();
}

public class GRItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GrId { get; set; }
    public GoodsReceipt? GoodsReceipt { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal QuantityReceived { get; set; }
    public string Unit { get; set; } = "cái";
    public string ConditionStatus { get; set; } = "GOOD";
}

public class Invoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string InvoiceNumber { get; set; } = string.Empty;
    public string? InvoiceSeries { get; set; }
    public string VendorTaxCode { get; set; } = string.Empty;
    public string? VendorName { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxRate { get; set; } = 8.00m;
    public decimal TaxAmount { get; set; }
    public decimal TotalPayable { get; set; }
    public string? FilePath { get; set; }
    public string? RawOcrJson { get; set; }
    public string PaymentStatus { get; set; } = "UNPAID";
    public bool IsDuplicate { get; set; } = false;
    public Guid? OriginalInvoiceId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<InvoiceItem> Items { get; set; } = new List<InvoiceItem>();
}

public class InvoiceItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public int LineNumber { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "cái";
    public decimal UnitPrice { get; set; }
    public decimal TotalAmount { get; set; }
    public Vector? Embedding { get; set; }
}

public class AuditSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string SessionCode { get; set; } = string.Empty;
    public Guid? PoId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public Guid? GrnId { get; set; }
    public GoodsReceipt? GoodsReceipt { get; set; }
    public Guid? InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public AuditStatus Status { get; set; } = AuditStatus.Pending;
    public int DiscrepancyCount { get; set; }
    public decimal TotalFinancialDiff { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<AuditDiscrepancy> Discrepancies { get; set; } = new List<AuditDiscrepancy>();
}

public class AuditDiscrepancy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SessionId { get; set; }
    public AuditSession? Session { get; set; }
    public DiscrepancyType DiscrepancyType { get; set; }
    public DiscrepancySeverity Severity { get; set; } = DiscrepancySeverity.Critical;
    public string? ItemName { get; set; }
    public string? ExpectedValue { get; set; }
    public string? ActualValue { get; set; }
    public decimal FinancialImpact { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsResolved { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
