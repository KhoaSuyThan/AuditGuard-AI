using System;
using System.Collections.Generic;

namespace AuditGuard.Application.DTOs;

public class AuditSessionDto
{
    public Guid Id { get; set; }
    public string SessionCode { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int DiscrepancyCount { get; set; }
    public decimal TotalFinancialDiff { get; set; }
    public string PoNumber { get; set; } = string.Empty;
    public string GrnNumber { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public string VendorName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<AuditDiscrepancyDto> Discrepancies { get; set; } = new();
    public List<ThreeWayRowDto> ComparisonRows { get; set; } = new();
}

public class AuditDiscrepancyDto
{
    public Guid Id { get; set; }
    public string DiscrepancyType { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string? ItemName { get; set; }
    public string? ExpectedValue { get; set; }
    public string? ActualValue { get; set; }
    public decimal FinancialImpact { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class ThreeWayRowDto
{
    public string PoItemName { get; set; } = string.Empty;
    public decimal PoQuantity { get; set; }
    public decimal PoUnitPrice { get; set; }
    public decimal GrnQuantityReceived { get; set; }
    public string InvoiceItemName { get; set; } = string.Empty;
    public decimal InvoiceQuantity { get; set; }
    public decimal InvoiceUnitPrice { get; set; }
    public decimal SimilarityScore { get; set; }
    public string MatchStatus { get; set; } = "MATCHED"; // MATCHED, PRICE_MISMATCH, QTY_MISMATCH, UNMATCHED
}

public class DisputeEmailDto
{
    public string RecipientEmail { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string BodyHtml { get; set; } = string.Empty;
    public string BodyPlainText { get; set; } = string.Empty;
}

public class ScenarioInfoDto
{
    public string ScenarioId { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ExpectedResult { get; set; } = string.Empty;
    public decimal FinancialImpactVnd { get; set; }
}
