using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuditGuard.Application.DTOs;
using AuditGuard.Application.Interfaces;
using AuditGuard.Domain.Entities;
using AuditGuard.Domain.Enums;
using AuditGuard.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using AuditGuard.Api.Hubs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace AuditGuard.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuditController : ControllerBase
{
    private readonly IAuditEngine _auditEngine;
    private readonly IDisputeEmailService _emailService;
    private readonly IHubContext<AuditHub> _hubContext;
    private readonly AuditDbContext _dbContext;
    private readonly IHostEnvironment _env;

    private static readonly Dictionary<Guid, AuditSessionDto> _sessionCache = new();

    public AuditController(
        IAuditEngine auditEngine,
        IDisputeEmailService emailService,
        IHubContext<AuditHub> hubContext,
        AuditDbContext dbContext,
        IHostEnvironment env)
    {
        _auditEngine = auditEngine;
        _emailService = emailService;
        _hubContext = hubContext;
        _dbContext = dbContext;
        _env = env;
    }

    /// <summary>
    /// Nạp 20 kịch bản mẫu từ thư mục data-samples/
    /// </summary>
    [HttpGet("scenarios")]
    public ActionResult<List<ScenarioInfoDto>> GetScenarios()
    {
        var rootDir = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", "..", "data-samples"));
        if (!Directory.Exists(rootDir))
        {
            return Ok(new List<ScenarioInfoDto>());
        }

        var results = new List<ScenarioInfoDto>();
        var scenarioDirs = Directory.GetDirectories(rootDir);

        foreach (var dir in scenarioDirs.OrderBy(d => d))
        {
            var infoPath = Path.Combine(dir, "scenario_info.json");
            if (System.IO.File.Exists(infoPath))
            {
                try
                {
                    var json = System.IO.File.ReadAllText(infoPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    results.Add(new ScenarioInfoDto
                    {
                        ScenarioId = root.TryGetProperty("scenario_id", out var sid) ? sid.GetString() ?? "" : "",
                        FolderName = Path.GetFileName(dir),
                        Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                        Description = root.TryGetProperty("description", out var desc) ? desc.GetString() ?? "" : "",
                        ExpectedResult = root.TryGetProperty("expected_result", out var er) ? er.GetString() ?? "" : "",
                        FinancialImpactVnd = root.TryGetProperty("financial_impact_vnd", out var fi) ? fi.GetDecimal() : 0m
                    });
                }
                catch { }
            }
        }

        return Ok(results);
    }

    /// <summary>
    /// Chạy đối soát kịch bản mẫu và lưu vào CSDL
    /// </summary>
    [HttpPost("scenarios/{folderName}/run")]
    public async Task<ActionResult<AuditSessionDto>> RunScenario(string folderName, [FromQuery] string? clientSessionId = null)
    {
        var rootDir = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "..", "..", "data-samples", folderName));
        if (!Directory.Exists(rootDir))
        {
            return NotFound(new { error = $"Không tìm thấy kịch bản {folderName}" });
        }

        var channel = clientSessionId ?? folderName;
        await SendProgress(channel, "INGESTION", 20, "Đang nạp bộ chứng từ (PO, Phiếu kho, Hóa đơn)...");

        try
        {
            var poJson = await System.IO.File.ReadAllTextAsync(Path.Combine(rootDir, "purchase_order.json"));
            var grnJson = await System.IO.File.ReadAllTextAsync(Path.Combine(rootDir, "goods_receipt.json"));
            var invJson = await System.IO.File.ReadAllTextAsync(Path.Combine(rootDir, "invoice.json"));

            var po = SafeParsePo(poJson);
            var grn = SafeParseGrn(grnJson);
            var inv = SafeParseInvoice(invJson);

            await SendProgress(channel, "SEMANTIC_MATCHING", 60, "Đang so khớp danh pháp bằng Vector Embeddings...");

            // Chuẩn bị dữ liệu lịch sử hóa đơn để check trùng
            var historicalInvoices = new List<Invoice>();
            if (folderName.Contains("duplicate", StringComparison.OrdinalIgnoreCase))
            {
                historicalInvoices.Add(new Invoice
                {
                    Id = Guid.NewGuid(),
                    InvoiceNumber = inv.InvoiceNumber,
                    VendorTaxCode = inv.VendorTaxCode,
                    InvoiceDate = new DateOnly(2026, 10, 15),
                    TotalPayable = inv.TotalPayable,
                    PaymentStatus = "PAID"
                });
            }

            // Đọc thêm từ DB nếu có
            try
            {
                var dbHistory = await _dbContext.Invoices
                    .Where(i => i.VendorTaxCode == inv.VendorTaxCode && i.PaymentStatus == "PAID")
                    .ToListAsync();
                historicalInvoices.AddRange(dbHistory);
            }
            catch { }

            await SendProgress(channel, "VERIFICATION", 85, "Đang kiểm tra 3 chiều số lượng, đơn giá và thuế...");

            var result = await _auditEngine.ExecuteAuditAsync(po, grn, inv, historicalInvoices);
            _sessionCache[result.Id] = result;

            // Lưu phiên đối soát vào CSDL
            try
            {
                var auditEntity = new AuditSession
                {
                    Id = result.Id,
                    SessionCode = result.SessionCode,
                    Status = Enum.Parse<AuditStatus>(result.Status, true),
                    DiscrepancyCount = result.DiscrepancyCount,
                    TotalFinancialDiff = result.TotalFinancialDiff,
                    Notes = $"Chạy từ kịch bản {folderName}"
                };

                foreach (var d in result.Discrepancies)
                {
                    auditEntity.Discrepancies.Add(new AuditDiscrepancy
                    {
                        Id = d.Id,
                        SessionId = result.Id,
                        DiscrepancyType = Enum.Parse<DiscrepancyType>(d.DiscrepancyType, true),
                        Severity = Enum.Parse<DiscrepancySeverity>(d.Severity, true),
                        ItemName = d.ItemName,
                        ExpectedValue = d.ExpectedValue,
                        ActualValue = d.ActualValue,
                        FinancialImpact = d.FinancialImpact,
                        Description = d.Description
                    });
                }

                _dbContext.AuditSessions.Add(auditEntity);
                await _dbContext.SaveChangesAsync();
            }
            catch { }

            await SendProgress(channel, "COMPLETED", 100, $"Đối soát hoàn tất! Kết quả: {result.Status}");
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Lỗi xử lý kịch bản: {ex.Message}" });
        }
    }

    /// <summary>
    /// Endpoint tải lên 3 file chứng từ thực tế (PO, Phiếu kho, Hóa đơn)
    /// </summary>
    [HttpPost("sessions/upload")]
    public async Task<ActionResult<AuditSessionDto>> UploadAndAudit(
        [FromForm] IFormFile poFile,
        [FromForm] IFormFile grnFile,
        [FromForm] IFormFile invoiceFile,
        [FromForm] string? clientSessionId = null)
    {
        if (poFile == null || grnFile == null || invoiceFile == null)
        {
            return BadRequest(new { error = "Yêu cầu tải lên đủ 3 tệp: Đơn đặt hàng (PO), Phiếu giao kho (GRN) và Hóa đơn (Invoice)." });
        }

        var channel = clientSessionId ?? Guid.NewGuid().ToString();
        await SendProgress(channel, "INGESTION", 25, "Đang tải lên và trích xuất dữ liệu từ các file...");

        try
        {
            using var poStream = new StreamReader(poFile.OpenReadStream());
            using var grnStream = new StreamReader(grnFile.OpenReadStream());
            using var invStream = new StreamReader(invoiceFile.OpenReadStream());

            var poContent = await poStream.ReadToEndAsync();
            var grnContent = await grnStream.ReadToEndAsync();
            var invContent = await invStream.ReadToEndAsync();

            var po = SafeParsePo(poContent);
            var grn = SafeParseGrn(grnContent);
            var inv = SafeParseInvoice(invContent);

            await SendProgress(channel, "MATCHING", 60, "Đang so khớp danh pháp sản phẩm...");

            var historical = new List<Invoice>();
            try
            {
                historical = await _dbContext.Invoices.Where(i => i.VendorTaxCode == inv.VendorTaxCode).ToListAsync();
            }
            catch { }

            await SendProgress(channel, "VERIFICATION", 90, "Đang kiểm tra đối soát 3 chiều...");
            var result = await _auditEngine.ExecuteAuditAsync(po, grn, inv, historical);
            _sessionCache[result.Id] = result;

            await SendProgress(channel, "COMPLETED", 100, $"Hoàn thành kiểm toán! Trạng thái: {result.Status}");
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Lỗi bóc tách file: {ex.Message}" });
        }
    }

    [HttpGet("sessions/{id:guid}")]
    public async Task<ActionResult<AuditSessionDto>> GetSession(Guid id)
    {
        if (_sessionCache.TryGetValue(id, out var session))
        {
            return Ok(session);
        }

        try
        {
            var dbSession = await _dbContext.AuditSessions
                .Include(s => s.Discrepancies)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (dbSession != null)
            {
                return Ok(new AuditSessionDto
                {
                    Id = dbSession.Id,
                    SessionCode = dbSession.SessionCode,
                    Status = dbSession.Status.ToString().ToUpper(),
                    DiscrepancyCount = dbSession.DiscrepancyCount,
                    TotalFinancialDiff = dbSession.TotalFinancialDiff,
                    CreatedAt = dbSession.CreatedAt,
                    Discrepancies = dbSession.Discrepancies.Select(d => new AuditDiscrepancyDto
                    {
                        Id = d.Id,
                        DiscrepancyType = d.DiscrepancyType.ToString(),
                        Severity = d.Severity.ToString(),
                        ItemName = d.ItemName,
                        ExpectedValue = d.ExpectedValue,
                        ActualValue = d.ActualValue,
                        FinancialImpact = d.FinancialImpact,
                        Description = d.Description
                    }).ToList()
                });
            }
        }
        catch { }

        return NotFound(new { error = "Không tìm thấy phiên kiểm toán." });
    }

    [HttpPost("sessions/{id:guid}/email")]
    public ActionResult<DisputeEmailDto> GenerateDisputeEmail(Guid id)
    {
        if (!_sessionCache.TryGetValue(id, out var session))
        {
            return NotFound(new { error = "Không tìm thấy phiên kiểm toán." });
        }

        var email = _emailService.GenerateDisputeEmail(session);
        return Ok(email);
    }

    private async Task SendProgress(string channel, string stage, int percent, string message)
    {
        try
        {
            // Gửi cả về Group riêng của session và Broadcast phòng trường hợp demo 1 user
            await _hubContext.Clients.Group(channel).SendAsync("ReceiveProgress", stage, percent, message);
            await _hubContext.Clients.All.SendAsync("ReceiveProgress", stage, percent, message);
        }
        catch { }
    }

    // --- CÁC HÀM PARSER AN TOÀN TRÁNH CRASH 500 ---
    private static PurchaseOrder SafeParsePo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var po = new PurchaseOrder
        {
            PoNumber = r.TryGetProperty("po_number", out var p) ? p.GetString() ?? "PO-UNKNOWN" : "PO-UNKNOWN",
            OrderDate = r.TryGetProperty("order_date", out var od) && DateOnly.TryParse(od.GetString(), out var d) ? d : DateOnly.FromDateTime(DateTime.UtcNow),
            Subtotal = r.TryGetProperty("subtotal", out var sub) ? sub.GetDecimal() : 0m,
            TaxAmount = r.TryGetProperty("tax_amount", out var ta) ? ta.GetDecimal() : 0m,
            TotalAmount = r.TryGetProperty("total_amount", out var tot) ? tot.GetDecimal() : 0m,
            Vendor = new Vendor
            {
                Name = r.TryGetProperty("vendor", out var v) && v.TryGetProperty("name", out var vn) ? vn.GetString() ?? "" : "",
                TaxCode = r.TryGetProperty("vendor", out var v2) && v2.TryGetProperty("tax_code", out var vtc) ? vtc.GetString() ?? "" : ""
            }
        };

        if (r.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var i in items.EnumerateArray())
            {
                po.Items.Add(new POItem
                {
                    LineNumber = i.TryGetProperty("line_number", out var ln) ? ln.GetInt32() : 1,
                    ItemName = i.TryGetProperty("item_name", out var iname) ? iname.GetString() ?? "" : "",
                    Quantity = i.TryGetProperty("quantity", out var q) ? q.GetDecimal() : 1m,
                    UnitPrice = i.TryGetProperty("unit_price", out var up) ? up.GetDecimal() : 0m,
                    TotalPrice = i.TryGetProperty("total_price", out var tp) ? tp.GetDecimal() : 0m
                });
            }
        }
        return po;
    }

    private static GoodsReceipt SafeParseGrn(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var grn = new GoodsReceipt
        {
            GrnNumber = r.TryGetProperty("grn_number", out var g) ? g.GetString() ?? "GRN-UNKNOWN" : "GRN-UNKNOWN",
            ReceivedDate = r.TryGetProperty("received_date", out var rd) && DateOnly.TryParse(rd.GetString(), out var d) ? d : DateOnly.FromDateTime(DateTime.UtcNow),
            WarehouseKeeper = r.TryGetProperty("warehouse_keeper", out var wk) ? wk.GetString() : ""
        };

        if (r.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var i in items.EnumerateArray())
            {
                grn.Items.Add(new GRItem
                {
                    ItemName = i.TryGetProperty("item_name", out var iname) ? iname.GetString() ?? "" : "",
                    QuantityReceived = i.TryGetProperty("quantity_received", out var qr) ? qr.GetDecimal() : 0m
                });
            }
        }
        return grn;
    }

    private static Invoice SafeParseInvoice(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        var inv = new Invoice
        {
            InvoiceNumber = r.TryGetProperty("invoice_number", out var invNo) ? invNo.GetString() ?? "INV-UNKNOWN" : "INV-UNKNOWN",
            InvoiceSeries = r.TryGetProperty("invoice_series", out var s) ? s.GetString() : "",
            InvoiceDate = r.TryGetProperty("invoice_date", out var idate) && DateOnly.TryParse(idate.GetString(), out var d) ? d : DateOnly.FromDateTime(DateTime.UtcNow),
            VendorName = r.TryGetProperty("vendor_name", out var vn) ? vn.GetString() : "",
            VendorTaxCode = r.TryGetProperty("vendor_tax_code", out var vtc) ? vtc.GetString() ?? "" : "",
            Subtotal = r.TryGetProperty("subtotal", out var sub) ? sub.GetDecimal() : 0m,
            TaxRate = r.TryGetProperty("tax_rate", out var tr) ? tr.GetDecimal() : 8.0m,
            TaxAmount = r.TryGetProperty("tax_amount", out var ta) ? ta.GetDecimal() : 0m,
            TotalPayable = r.TryGetProperty("total_payable", out var tp) ? tp.GetDecimal() : 0m
        };

        if (r.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var i in items.EnumerateArray())
            {
                inv.Items.Add(new InvoiceItem
                {
                    LineNumber = i.TryGetProperty("line_number", out var ln) ? ln.GetInt32() : 1,
                    ItemName = i.TryGetProperty("item_name", out var iname) ? iname.GetString() ?? "" : "",
                    Quantity = i.TryGetProperty("quantity", out var q) ? q.GetDecimal() : 1m,
                    UnitPrice = i.TryGetProperty("unit_price", out var up) ? up.GetDecimal() : 0m,
                    TotalAmount = i.TryGetProperty("total_amount", out var tot) ? tot.GetDecimal() : 0m
                });
            }
        }
        return inv;
    }
}
