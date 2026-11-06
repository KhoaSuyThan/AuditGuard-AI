using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuditGuard.Api.Hubs;
using AuditGuard.Application.DTOs;
using AuditGuard.Application.Interfaces;
using AuditGuard.Application.Models;
using AuditGuard.Domain.Entities;
using AuditGuard.Domain.Enums;
using AuditGuard.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AuditGuard.Api.Workers;

/// <summary>
/// Background Service đọc tác vụ đối soát từ Channel và xử lý hoàn toàn bất đồng bộ
/// </summary>
public class AuditProcessingWorker : BackgroundService
{
    private readonly IAuditTaskQueue _taskQueue;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHubContext<AuditHub> _hubContext;
    private readonly ILogger<AuditProcessingWorker> _logger;

    public AuditProcessingWorker(
        IAuditTaskQueue taskQueue,
        IServiceProvider serviceProvider,
        IHubContext<AuditHub> hubContext,
        ILogger<AuditProcessingWorker> logger)
    {
        _taskQueue = taskQueue;
        _serviceProvider = serviceProvider;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("AuditProcessingWorker đã khởi động và đang lắng nghe hàng đợi Channel...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await _taskQueue.DequeueAsync(stoppingToken);
                _logger.LogInformation("Bắt đầu xử lý tác vụ đối soát SessionId: {SessionId}", job.SessionId);

                await ProcessJobAsync(job, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi xảy ra trong quá trình xử lý tác vụ kiểm toán nền.");
            }
        }

        _logger.LogInformation("AuditProcessingWorker đang dừng lại.");
    }

    private async Task ProcessJobAsync(AuditBackgroundJob job, CancellationToken ct)
    {
        var channel = job.ClientSessionId ?? job.SessionId.ToString();

        using var scope = _serviceProvider.CreateScope();
        var auditEngine = scope.ServiceProvider.GetRequiredService<IAuditEngine>();
        var aiClient = scope.ServiceProvider.GetRequiredService<IAiEngineClient>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var env = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();

        // 1. Cập nhật trạng thái phiên trong CSDL sang PROCESSING
        try
        {
            var existingSession = await dbContext.AuditSessions.FirstOrDefaultAsync(s => s.Id == job.SessionId, ct);
            if (existingSession != null)
            {
                existingSession.Notes = "PROCESSING: Đang phân tích OCR và trích xuất cấu trúc chứng từ...";
                await dbContext.SaveChangesAsync(ct);
            }
        }
        catch (Exception exDb)
        {
            _logger.LogWarning(exDb, "Không thể cập nhật trạng thái PROCESSING vào CSDL.");
        }

        await _hubContext.Clients.Group(channel).SendAsync("ReceiveProgress", "Ingestion", 15, "Đã nhận tài liệu và bắt đầu phân tích...", ct);

        PurchaseOrder po = new();
        GoodsReceipt grn = new();
        Invoice invoice = new();

        try
        {
            if (!string.IsNullOrEmpty(job.ScenarioFolderName))
            {
                // Xử lý kịch bản mẫu từ thư mục data-samples
                var root = Path.GetFullPath(Path.Combine(env.ContentRootPath, "..", "..", "data-samples", job.ScenarioFolderName));
                if (!Directory.Exists(root))
                {
                    throw new FileNotFoundException($"Không tìm thấy thư mục kịch bản mẫu: {job.ScenarioFolderName}");
                }

                await _hubContext.Clients.Group(channel).SendAsync("ReceiveProgress", "Parsing", 40, "Đang đọc và phân tích cấu trúc chứng từ JSON...", ct);

                var poPath = Path.Combine(root, "purchase_order.json");
                var grnPath = Path.Combine(root, "goods_receipt.json");
                if (!File.Exists(grnPath))
                {
                    grnPath = Path.Combine(root, "goods_receipt_note.json");
                }
                var invPath = Path.Combine(root, "invoice.json");

                if (!File.Exists(poPath) || !File.Exists(grnPath) || !File.Exists(invPath))
                {
                    throw new FileNotFoundException($"Kịch bản {job.ScenarioFolderName} thiếu tệp chứng từ bắt buộc (purchase_order.json, goods_receipt.json, invoice.json).");
                }

                var poJson = await File.ReadAllTextAsync(poPath, ct);
                var grnJson = await File.ReadAllTextAsync(grnPath, ct);
                var invJson = await File.ReadAllTextAsync(invPath, ct);

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                po = JsonSerializer.Deserialize<PurchaseOrder>(poJson, options) ?? new();
                grn = JsonSerializer.Deserialize<GoodsReceipt>(grnJson, options) ?? new();
                invoice = JsonSerializer.Deserialize<Invoice>(invJson, options) ?? new();
            }
            else if (!string.IsNullOrEmpty(job.PoFilePath) && !string.IsNullOrEmpty(job.GrnFilePath) && !string.IsNullOrEmpty(job.InvoiceFilePath))
            {
                // Bóc tách độc lập và thực sự cả 3 tệp PO, GRN, INVOICE
                await _hubContext.Clients.Group(channel).SendAsync("ReceiveProgress", "OCR & AI Extraction", 40, "Đang bóc tách thông tin độc lập cả 3 tệp (PO, Phiếu kho, Hóa đơn)...", ct);

                await using var poStream = File.OpenRead(job.PoFilePath);
                await using var grnStream = File.OpenRead(job.GrnFilePath);
                await using var invStream = File.OpenRead(job.InvoiceFilePath);

                var poTask = aiClient.ExtractPurchaseOrderAsync(poStream, Path.GetFileName(job.PoFilePath));
                var grnTask = aiClient.ExtractGoodsReceiptAsync(grnStream, Path.GetFileName(job.GrnFilePath));
                var invTask = aiClient.ExtractInvoiceAsync(invStream, Path.GetFileName(job.InvoiceFilePath));

                await Task.WhenAll(poTask, grnTask, invTask);

                po = poTask.Result;
                grn = grnTask.Result;
                invoice = invTask.Result;
            }

            // 2. Tra cứu lịch sử hóa đơn trong DB để phát hiện gian lận trùng lặp
            await _hubContext.Clients.Group(channel).SendAsync("ReceiveProgress", "3-Way Matching", 70, "Đang đối soát 3 chiều và so khớp thực thể ngữ nghĩa...", ct);

            List<Invoice> dbHistory = new();
            try
            {
                dbHistory = await dbContext.Invoices
                    .Where(i => i.VendorTaxCode == invoice.VendorTaxCode)
                    .ToListAsync(ct);
            }
            catch (Exception exHist)
            {
                _logger.LogWarning(exHist, "Không thể tra cứu lịch sử hóa đơn từ CSDL.");
            }

            // 3. Thực thi đối soát 3 chiều qua AuditEngine
            var result = await auditEngine.ExecuteAuditAsync(po, grn, invoice, dbHistory);
            result.Id = job.SessionId;
            result.SessionCode = "SES-" + job.SessionId.ToString()[..8].ToUpper();

            // 4. Lưu vết bền vững vào PostgreSQL qua EF Core
            await _hubContext.Clients.Group(channel).SendAsync("ReceiveProgress", "Persistence", 90, "Đang lưu trữ hồ sơ kiểm toán vào CSDL PostgreSQL...", ct);

            // Lưu trước các thực thể liên quan nếu chưa có trong DB
            if (po.Id == Guid.Empty) po.Id = Guid.NewGuid();
            if (grn.Id == Guid.Empty) grn.Id = Guid.NewGuid();
            if (invoice.Id == Guid.Empty) invoice.Id = Guid.NewGuid();

            dbContext.PurchaseOrders.Add(po);
            dbContext.GoodsReceipts.Add(grn);
            dbContext.Invoices.Add(invoice);

            var dbSession = await dbContext.AuditSessions
                .Include(s => s.Discrepancies)
                .FirstOrDefaultAsync(s => s.Id == job.SessionId, ct);

            if (dbSession == null)
            {
                dbSession = new AuditSession
                {
                    Id = job.SessionId,
                    SessionCode = result.SessionCode,
                    CreatedAt = DateTime.UtcNow
                };
                dbContext.AuditSessions.Add(dbSession);
            }

            dbSession.PoId = po.Id;
            dbSession.GrnId = grn.Id;
            dbSession.InvoiceId = invoice.Id;
            dbSession.Status = Enum.TryParse<AuditStatus>(result.Status, out var st) ? st : AuditStatus.Pending;
            dbSession.TotalFinancialDiff = result.TotalFinancialDiff;
            dbSession.DiscrepancyCount = result.Discrepancies.Count;
            dbSession.Notes = $"COMPLETED: Đối soát hoàn tất lúc {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC";

            dbSession.Discrepancies.Clear();
            foreach (var d in result.Discrepancies)
            {
                dbSession.Discrepancies.Add(new AuditDiscrepancy
                {
                    SessionId = job.SessionId,
                    DiscrepancyType = Enum.TryParse<DiscrepancyType>(d.DiscrepancyType, out var dt) ? dt : DiscrepancyType.PriceMismatch,
                    Severity = Enum.TryParse<DiscrepancySeverity>(d.Severity, out var ds) ? ds : DiscrepancySeverity.Critical,
                    ItemName = d.ItemName,
                    Description = d.Description,
                    ExpectedValue = d.ExpectedValue,
                    ActualValue = d.ActualValue,
                    FinancialImpact = d.FinancialImpact,
                    CreatedAt = DateTime.UtcNow
                });
            }

            await dbContext.SaveChangesAsync(ct);

            // 5. Báo thành công
            await _hubContext.Clients.Group(channel).SendAsync("ReceiveProgress", "Completed", 100, "Hoàn tất đối soát 3 chiều!", ct);
            await _hubContext.Clients.Group(channel).SendAsync("ReceiveAuditCompleted", result, ct);

            _logger.LogInformation("Hoàn tất thành công tác vụ SessionId: {SessionId} với trạng thái {Status}", job.SessionId, result.Status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi xử lý phiên đối soát {SessionId}", job.SessionId);

            // Cập nhật trạng thái FAILED rõ ràng trong CSDL, không nuốt lỗi
            try
            {
                var failedSession = await dbContext.AuditSessions.FirstOrDefaultAsync(s => s.Id == job.SessionId, ct);
                if (failedSession != null)
                {
                    failedSession.Status = AuditStatus.Rejected;
                    failedSession.Notes = $"FAILED: {ex.Message}";
                    await dbContext.SaveChangesAsync(ct);
                }
            }
            catch (Exception exSaveFail)
            {
                _logger.LogError(exSaveFail, "Không thể lưu trạng thái FAILED vào CSDL cho SessionId {SessionId}", job.SessionId);
            }

            await _hubContext.Clients.Group(channel).SendAsync("ReceiveProgress", "Failed", 0, $"Lỗi đối soát: {ex.Message}", ct);
        }
        finally
        {
            // Dọn dẹp an toàn các tệp tạm đã tải lên
            try
            {
                if (!string.IsNullOrEmpty(job.PoFilePath) && File.Exists(job.PoFilePath)) File.Delete(job.PoFilePath);
                if (!string.IsNullOrEmpty(job.GrnFilePath) && File.Exists(job.GrnFilePath)) File.Delete(job.GrnFilePath);
                if (!string.IsNullOrEmpty(job.InvoiceFilePath) && File.Exists(job.InvoiceFilePath)) File.Delete(job.InvoiceFilePath);
            }
            catch (Exception exClean)
            {
                _logger.LogWarning(exClean, "Không thể xóa tệp tạm của phiên đối soát {SessionId}", job.SessionId);
            }
        }
    }
}
