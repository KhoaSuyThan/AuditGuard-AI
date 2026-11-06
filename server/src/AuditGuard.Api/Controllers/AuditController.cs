using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuditGuard.Api.Hubs;
using AuditGuard.Application.DTOs;
using AuditGuard.Application.Interfaces;
using AuditGuard.Application.Models;
using AuditGuard.Domain.Entities;
using AuditGuard.Domain.Enums;
using AuditGuard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AuditGuard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuditController : ControllerBase
{
    private readonly IAuditEngine _auditEngine;
    private readonly IDisputeEmailService _emailService;
    private readonly IHubContext<AuditHub> _hubContext;
    private readonly AuditDbContext _dbContext;
    private readonly IAuditTaskQueue _taskQueue;
    private readonly IHostEnvironment _env;
    private readonly ILogger<AuditController> _logger;

    private static readonly string[] AllowedExtensions = { ".pdf", ".json", ".png", ".jpg", ".jpeg", ".webp" };
    private const long MaxFileSize = 20 * 1024 * 1024; // 20 MB

    public AuditController(
        IAuditEngine auditEngine,
        IDisputeEmailService emailService,
        IHubContext<AuditHub> hubContext,
        AuditDbContext dbContext,
        IAuditTaskQueue taskQueue,
        IHostEnvironment env,
        ILogger<AuditController> logger)
    {
        _auditEngine = auditEngine;
        _emailService = emailService;
        _hubContext = hubContext;
        _dbContext = dbContext;
        _taskQueue = taskQueue;
        _env = env;
        _logger = logger;
    }

    private string ResolveDataSamplesPath(string? subFolder = null)
    {
        var searchRoots = new[] { _env.ContentRootPath, AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var startDir in searchRoots)
        {
            var cur = new DirectoryInfo(startDir);
            while (cur != null)
            {
                var target = Path.Combine(cur.FullName, "data-samples");
                if (Directory.Exists(target))
                {
                    return string.IsNullOrEmpty(subFolder) ? target : Path.Combine(target, subFolder);
                }
                cur = cur.Parent;
            }
        }
        var fallback = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", "..", "..", "data-samples"));
        return string.IsNullOrEmpty(subFolder) ? fallback : Path.Combine(fallback, subFolder ?? "");
    }

    /// <summary>
    /// Lấy danh sách 20 kịch bản mẫu từ data-samples
    /// </summary>
    [HttpGet("scenarios")]
    public ActionResult<IEnumerable<ScenarioInfoDto>> GetScenarios()
    {
        var root = ResolveDataSamplesPath();
        if (!Directory.Exists(root))
        {
            return Ok(Array.Empty<ScenarioInfoDto>());
        }

        var dirs = Directory.GetDirectories(root);
        var list = new List<ScenarioInfoDto>();

        foreach (var dir in dirs)
        {
            var folder = Path.GetFileName(dir);
            var descPath = Path.Combine(dir, "description.txt");
            var desc = System.IO.File.Exists(descPath) ? System.IO.File.ReadAllText(descPath).Trim() : folder;

            list.Add(new ScenarioInfoDto
            {
                ScenarioId = folder,
                FolderName = folder,
                Name = folder.Replace("_", " ").ToUpper(),
                Description = desc,
                ExpectedResult = "Đối soát 3 chiều tự động",
                FinancialImpactVnd = 0
            });
        }

        return Ok(list.OrderBy(x => x.FolderName));
    }

    /// <summary>
    /// Chạy đối soát kịch bản mẫu bất đồng bộ qua Background Worker Channel
    /// </summary>
    [HttpPost("scenarios/{folderName}/run-async")]
    public async Task<IActionResult> RunScenarioAsync(string folderName, [FromQuery] string? clientSessionId = null)
    {
        var root = ResolveDataSamplesPath(folderName);
        if (!Directory.Exists(root))
        {
            return NotFound(new ProblemDetails
            {
                Title = "Không tìm thấy kịch bản",
                Detail = $"Thư mục kịch bản {folderName} không tồn tại.",
                Status = StatusCodes.Status404NotFound
            });
        }

        var sessionId = Guid.NewGuid();

        // Khởi tạo bản ghi PENDING trong CSDL ngay lập tức
        try
        {
            var auditSession = new AuditSession
            {
                Id = sessionId,
                SessionCode = "SES-" + sessionId.ToString()[..8].ToUpper(),
                Status = AuditStatus.Pending,
                Notes = "QUEUED: Kịch bản đã được đưa vào hàng đợi xử lý nền.",
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.AuditSessions.Add(auditSession);
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể tạo trước bản ghi PENDING trong CSDL. Tiếp tục với hàng đợi.");
        }

        var job = new AuditBackgroundJob
        {
            SessionId = sessionId,
            ClientSessionId = clientSessionId,
            ScenarioFolderName = folderName
        };

        await _taskQueue.EnqueueAsync(job);

        return Accepted(new
        {
            SessionId = sessionId,
            Status = "QUEUED",
            Message = $"Kịch bản {folderName} đã được đưa vào hàng đợi đối soát bất đồng bộ."
        });
    }

    /// <summary>
    /// Chạy đối soát kịch bản mẫu đồng bộ (trả về kết quả ngay)
    /// </summary>
    [HttpPost("scenarios/{folderName}/run")]
    public async Task<ActionResult<AuditSessionDto>> RunScenario(string folderName, [FromQuery] string? clientSessionId = null)
    {
        var root = ResolveDataSamplesPath(folderName);
        if (!Directory.Exists(root))
        {
            return NotFound(new ProblemDetails
            {
                Title = "Không tìm thấy kịch bản",
                Detail = $"Thư mục kịch bản {folderName} không tồn tại.",
                Status = StatusCodes.Status404NotFound
            });
        }

        var poFile = Path.Combine(root, "purchase_order.json");
        var grnFile = Path.Combine(root, "goods_receipt.json");
        if (!System.IO.File.Exists(grnFile))
        {
            grnFile = Path.Combine(root, "goods_receipt_note.json");
        }
        var invFile = Path.Combine(root, "invoice.json");

        if (!System.IO.File.Exists(poFile) || !System.IO.File.Exists(grnFile) || !System.IO.File.Exists(invFile))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Thiếu tệp dữ liệu kịch bản",
                Detail = "Kịch bản phải chứa đủ purchase_order.json, goods_receipt.json (hoặc goods_receipt_note.json) và invoice.json.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        PurchaseOrder po;
        GoodsReceipt grn;
        Invoice invoice;

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            po = JsonSerializer.Deserialize<PurchaseOrder>(await System.IO.File.ReadAllTextAsync(poFile), options) ?? new();
            grn = JsonSerializer.Deserialize<GoodsReceipt>(await System.IO.File.ReadAllTextAsync(grnFile), options) ?? new();
            invoice = JsonSerializer.Deserialize<Invoice>(await System.IO.File.ReadAllTextAsync(invFile), options) ?? new();
        }
        catch (JsonException ex)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Lỗi định dạng JSON kịch bản",
                Detail = ex.Message,
                Status = StatusCodes.Status400BadRequest
            });
        }

        var sessionId = Guid.NewGuid();
        var channel = clientSessionId ?? sessionId.ToString();

        await SendProgress(channel, "Ingestion", 20, "Đang nạp chứng từ và đối soát 3 chiều...");

        // Tra cứu hóa đơn lịch sử qua EF Core
        List<Invoice> dbHistory = new();
        try
        {
            dbHistory = await _dbContext.Invoices
                .Where(i => i.VendorTaxCode == invoice.VendorTaxCode)
                .ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể đọc lịch sử hóa đơn từ CSDL.");
        }

        await SendProgress(channel, "Reconciliation", 60, "Đang so khớp thực thể và kiểm tra rủi ro tài chính...");

        var result = await _auditEngine.ExecuteAuditAsync(po, grn, invoice, dbHistory);
        result.Id = sessionId;
        result.SessionCode = "SES-" + sessionId.ToString()[..8].ToUpper();

        await SendProgress(channel, "Persistence", 85, "Đang lưu trữ hồ sơ kiểm toán vào CSDL...");

        // Lưu bền vững vào CSDL PostgreSQL
        try
        {
            if (po.Id == Guid.Empty) po.Id = Guid.NewGuid();
            if (grn.Id == Guid.Empty) grn.Id = Guid.NewGuid();
            if (invoice.Id == Guid.Empty) invoice.Id = Guid.NewGuid();

            _dbContext.PurchaseOrders.Add(po);
            _dbContext.GoodsReceipts.Add(grn);
            _dbContext.Invoices.Add(invoice);

            var auditEntity = new AuditSession
            {
                Id = sessionId,
                SessionCode = result.SessionCode,
                InvoiceId = invoice.Id,
                PoId = po.Id,
                GrnId = grn.Id,
                Status = Enum.TryParse<AuditStatus>(result.Status, out var st) ? st : AuditStatus.Pending,
                TotalFinancialDiff = result.TotalFinancialDiff,
                DiscrepancyCount = result.Discrepancies.Count,
                Notes = $"COMPLETED: Đối soát hoàn tất lúc {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
                CreatedAt = DateTime.UtcNow,
                Discrepancies = result.Discrepancies.Select(d => new AuditDiscrepancy
                {
                    SessionId = sessionId,
                    DiscrepancyType = Enum.TryParse<DiscrepancyType>(d.DiscrepancyType, out var dt) ? dt : DiscrepancyType.PriceMismatch,
                    Severity = Enum.TryParse<DiscrepancySeverity>(d.Severity, out var ds) ? ds : DiscrepancySeverity.Critical,
                    ItemName = d.ItemName,
                    Description = d.Description,
                    ExpectedValue = d.ExpectedValue,
                    ActualValue = d.ActualValue,
                    FinancialImpact = d.FinancialImpact,
                    CreatedAt = DateTime.UtcNow
                }).ToList()
            };

            _dbContext.AuditSessions.Add(auditEntity);
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi lưu kết quả đối soát vào CSDL.");
            // Không nuốt lỗi âm thầm nếu DB có sự cố
        }

        await SendProgress(channel, "Completed", 100, "Hoàn tất đối soát!");
        return Ok(result);
    }

    /// <summary>
    /// Tải lên 3 file chứng từ để xử lý bất đồng bộ qua Channel Worker
    /// </summary>
    [HttpPost("sessions/upload-async")]
    public async Task<IActionResult> UploadAndAuditAsync(
        IFormFile poFile, 
        IFormFile grnFile, 
        IFormFile invoiceFile, 
        [FromQuery] string? clientSessionId = null)
    {
        // 1. Kiểm tra sự tồn tại của 3 tệp
        if (poFile == null || grnFile == null || invoiceFile == null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Thiếu tệp tin tải lên",
                Detail = "Vui lòng cung cấp đủ cả 3 tệp tin: poFile (Đơn đặt hàng), grnFile (Phiếu nhập kho) và invoiceFile (Hóa đơn).",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // 2. Kiểm tra định dạng và kích thước an toàn
        var files = new[] { (poFile, "PO"), (grnFile, "GRN"), (invoiceFile, "Invoice") };
        foreach (var (f, label) in files)
        {
            var ext = Path.GetExtension(f.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext))
            {
                return BadRequest(new ProblemDetails
                {
                    Title = $"Định dạng tệp {label} không hợp lệ",
                    Detail = $"Tệp '{f.FileName}' có định dạng không được hỗ trợ. Chỉ chấp nhận các định dạng: {string.Join(", ", AllowedExtensions)}.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            if (f.Length > MaxFileSize)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = $"Dung lượng tệp {label} vượt quá giới hạn",
                    Detail = $"Tệp '{f.FileName}' có dung lượng ({f.Length / (1024 * 1024)}MB) vượt quá giới hạn tối đa 20MB.",
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }

        var sessionId = Guid.NewGuid();

        // 3. Khởi tạo bản ghi PENDING trong CSDL ngay lập tức
        try
        {
            var auditSession = new AuditSession
            {
                Id = sessionId,
                SessionCode = "SES-" + sessionId.ToString()[..8].ToUpper(),
                Status = AuditStatus.Pending,
                Notes = "QUEUED: Đang chờ xử lý trong hàng đợi Channel.",
                CreatedAt = DateTime.UtcNow
            };
            _dbContext.AuditSessions.Add(auditSession);
            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Không thể tạo trước bản ghi PENDING trong CSDL cho SessionId {SessionId}", sessionId);
        }

        // 4. Lưu tệp an toàn vào thư mục tạm có GUID cách ly (chống Path Traversal)
        var tempDir = Path.Combine(Path.GetTempPath(), "AuditGuard", sessionId.ToString());
        Directory.CreateDirectory(tempDir);

        var poExt = Path.GetExtension(poFile.FileName);
        var grnExt = Path.GetExtension(grnFile.FileName);
        var invExt = Path.GetExtension(invoiceFile.FileName);

        var poPath = Path.Combine(tempDir, $"po_{Path.GetRandomFileName()}{poExt}");
        var grnPath = Path.Combine(tempDir, $"grn_{Path.GetRandomFileName()}{grnExt}");
        var invPath = Path.Combine(tempDir, $"inv_{Path.GetRandomFileName()}{invExt}");

        await using (var fs = System.IO.File.Create(poPath)) await poFile.CopyToAsync(fs);
        await using (var fs = System.IO.File.Create(grnPath)) await grnFile.CopyToAsync(fs);
        await using (var fs = System.IO.File.Create(invPath)) await invoiceFile.CopyToAsync(fs);

        // 5. Đẩy vào hàng đợi Channel
        var job = new AuditBackgroundJob
        {
            SessionId = sessionId,
            ClientSessionId = clientSessionId,
            PoFilePath = poPath,
            GrnFilePath = grnPath,
            InvoiceFilePath = invPath
        };

        await _taskQueue.EnqueueAsync(job);

        return Accepted(new
        {
            SessionId = sessionId,
            Status = "QUEUED",
            Message = "3 tệp chứng từ đã được nạp và đưa vào hàng đợi đối soát bất đồng bộ thành công."
        });
    }

    /// <summary>
    /// Lấy chi tiết phiên đối soát theo ID (ưu tiên đọc từ CSDL PostgreSQL qua EF Core)
    /// </summary>
    [HttpGet("sessions/{id}")]
    public async Task<ActionResult<AuditSessionDto>> GetSession(Guid id)
    {
        try
        {
            var dbSession = await _dbContext.AuditSessions
                .Include(s => s.Discrepancies)
                .Include(s => s.PurchaseOrder)
                .Include(s => s.GoodsReceipt)
                .Include(s => s.Invoice)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (dbSession != null)
            {
                return Ok(new AuditSessionDto
                {
                    Id = dbSession.Id,
                    SessionCode = dbSession.SessionCode,
                    Status = dbSession.Status.ToString().ToUpper(),
                    TotalFinancialDiff = dbSession.TotalFinancialDiff,
                    DiscrepancyCount = dbSession.DiscrepancyCount,
                    PoNumber = dbSession.PurchaseOrder?.PoNumber ?? "",
                    GrnNumber = dbSession.GoodsReceipt?.GrnNumber ?? "",
                    InvoiceNumber = dbSession.Invoice?.InvoiceNumber ?? "",
                    VendorName = dbSession.Invoice?.VendorName ?? "",
                    CreatedAt = dbSession.CreatedAt,
                    Discrepancies = dbSession.Discrepancies.Select(d => new AuditDiscrepancyDto
                    {
                        Id = d.Id,
                        DiscrepancyType = d.DiscrepancyType.ToString(),
                        Severity = d.Severity.ToString(),
                        ItemName = d.ItemName,
                        Description = d.Description,
                        ExpectedValue = d.ExpectedValue,
                        ActualValue = d.ActualValue,
                        FinancialImpact = d.FinancialImpact
                    }).ToList()
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi khi truy vấn phiên đối soát {Id} từ CSDL", id);
            return StatusCode(StatusCodes.Status500InternalServerError, new ProblemDetails
            {
                Title = "Lỗi truy vấn CSDL",
                Detail = ex.Message,
                Status = StatusCodes.Status500InternalServerError
            });
        }

        return NotFound(new ProblemDetails
        {
            Title = "Không tìm thấy phiên kiểm toán",
            Detail = $"Không tìm thấy phiên kiểm toán với ID {id} trong CSDL.",
            Status = StatusCodes.Status404NotFound
        });
    }

    /// <summary>
    /// Sinh nội dung email khiếu nại nhà cung cấp tự động
    /// </summary>
    [HttpPost("sessions/{id}/email")]
    public async Task<ActionResult<DisputeEmailDto>> GenerateDisputeEmail(Guid id)
    {
        var sessionResult = await GetSession(id);
        if (sessionResult.Result is NotFoundObjectResult || sessionResult.Value == null)
        {
            return NotFound("Không tìm thấy phiên kiểm toán để tạo email khiếu nại.");
        }

        var session = sessionResult.Value;
        var email = _emailService.GenerateDisputeEmail(session);
        return Ok(email);
    }

    private async Task SendProgress(string channel, string stage, int percent, string message)
    {
        await _hubContext.Clients.Group(channel).SendAsync("ReceiveProgress", stage, percent, message);
    }
}
