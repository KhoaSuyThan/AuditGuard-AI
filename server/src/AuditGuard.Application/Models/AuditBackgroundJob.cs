using System;

namespace AuditGuard.Application.Models;

/// <summary>
/// Đại diện cho tác vụ đối soát được đẩy vào hàng đợi bất đồng bộ
/// </summary>
public class AuditBackgroundJob
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public string? ClientSessionId { get; set; }
    public string? ScenarioFolderName { get; set; }
    public string? PoFilePath { get; set; }
    public string? GrnFilePath { get; set; }
    public string? InvoiceFilePath { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
