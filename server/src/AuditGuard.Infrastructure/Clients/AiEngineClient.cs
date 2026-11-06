using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AuditGuard.Application.DTOs;
using AuditGuard.Application.Interfaces;
using AuditGuard.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AuditGuard.Infrastructure.Clients;

public class AiEngineClient : IAiEngineClient
{
    private readonly HttpClient _httpClient;
    private readonly string _aiEngineUrl;
    private readonly ILogger<AiEngineClient> _logger;

    public AiEngineClient(
        HttpClient httpClient, 
        IConfiguration configuration,
        ILogger<AiEngineClient> logger)
    {
        _httpClient = httpClient;
        _aiEngineUrl = configuration["AiEngine:BaseUrl"] ?? "http://localhost:8000";
        _logger = logger;
    }

    /// <summary>
    /// Gọi endpoint /api/ai/extract-document trên AI Service (FastAPI) để bóc tách tệp PDF/ảnh
    /// </summary>
    private async Task<JsonElement?> CallExtractDocumentApiAsync(Stream fileStream, string fileName, string docType)
    {
        try
        {
            using var form = new MultipartFormDataContent();
            using var fileContent = new StreamContent(fileStream);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "file", fileName);
            form.Add(new StringContent(docType), "document_type");

            var endpoint = $"{_aiEngineUrl}/api/ai/extract-document";
            var response = await _httpClient.PostAsync(endpoint, form);

            if (!response.IsSuccessStatusCode)
            {
                var errDetail = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("AI Engine từ chối yêu cầu bóc tách {DocType} cho tệp '{FileName}' (HTTP {Code}): {Detail}", 
                    docType, fileName, (int)response.StatusCode, errDetail);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("data", out var dataElem))
            {
                return dataElem.Clone();
            }

            _logger.LogWarning("Phản hồi từ AI Engine cho {DocType} không chứa thuộc tính 'data': {Json}", docType, json);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi kết nối tới AI Engine ({Url}/api/ai/extract-document) khi bóc tách {DocType} cho tệp '{FileName}'", 
                _aiEngineUrl, docType, fileName);
            return null;
        }
    }

    public async Task<Invoice> ExtractInvoiceAsync(Stream fileStream, string fileName)
    {
        // 1. Nếu là file JSON kịch bản
        if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var reader = new StreamReader(fileStream);
                var content = await reader.ReadToEndAsync();
                var inv = JsonSerializer.Deserialize<Invoice>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (inv != null) return inv;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không thể đọc định dạng JSON Hóa đơn từ tệp {FileName}", fileName);
                throw new InvalidDataException($"Tệp {fileName} không chứa dữ liệu JSON Hóa đơn hợp lệ: {ex.Message}");
            }
        }

        // 2. Nếu là PDF hoặc hình ảnh scan, gọi AI Engine
        var data = await CallExtractDocumentApiAsync(fileStream, fileName, "INVOICE");
        if (!data.HasValue)
        {
            // Minh bạch: KHÔNG sinh dữ liệu giả mặc định, ném lỗi để worker ghi nhận FAILED
            throw new InvalidOperationException($"Trích xuất Hóa đơn thất bại từ tệp '{fileName}'. AI Engine không thể bóc tách nội dung hoặc không thể kết nối.");
        }

        var root = data.Value;
        var invoice = new Invoice
        {
            InvoiceNumber = root.TryGetProperty("invoice_number", out var invNum) ? invNum.GetString() ?? "" : "",
            InvoiceSeries = root.TryGetProperty("invoice_series", out var s) ? s.GetString() : "",
            VendorTaxCode = root.TryGetProperty("vendor_tax_code", out var vtc) ? vtc.GetString() ?? "" : "",
            VendorName = root.TryGetProperty("vendor_name", out var vn) ? vn.GetString() : "",
            Subtotal = root.TryGetProperty("subtotal", out var sub) ? sub.GetDecimal() : 0,
            TaxRate = root.TryGetProperty("tax_rate", out var tr) ? tr.GetDecimal() : 8.0m,
            TaxAmount = root.TryGetProperty("tax_amount", out var ta) ? ta.GetDecimal() : 0,
            TotalPayable = root.TryGetProperty("total_payable", out var tp) ? tp.GetDecimal() : 0,
            InvoiceDate = root.TryGetProperty("invoice_date", out var idt) && DateOnly.TryParse(idt.GetString(), out var d) ? d : DateOnly.FromDateTime(DateTime.UtcNow)
        };

        if (root.TryGetProperty("items", out var itemsElem) && itemsElem.ValueKind == JsonValueKind.Array)
        {
            int line = 1;
            foreach (var item in itemsElem.EnumerateArray())
            {
                invoice.Items.Add(new InvoiceItem
                {
                    LineNumber = line++,
                    ItemName = item.TryGetProperty("item_name", out var iname) ? iname.GetString() ?? "" : "",
                    Quantity = item.TryGetProperty("quantity", out var iqty) ? iqty.GetDecimal() : 0,
                    UnitPrice = item.TryGetProperty("unit_price", out var iupr) ? iupr.GetDecimal() : 0,
                    TotalAmount = item.TryGetProperty("total_amount", out var itot) ? itot.GetDecimal() : 0,
                    Unit = item.TryGetProperty("unit", out var iunit) ? iunit.GetString() ?? "cái" : "cái"
                });
            }
        }

        return invoice;
    }

    public async Task<PurchaseOrder> ExtractPurchaseOrderAsync(Stream fileStream, string fileName)
    {
        if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var reader = new StreamReader(fileStream);
                var content = await reader.ReadToEndAsync();
                var po = JsonSerializer.Deserialize<PurchaseOrder>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (po != null) return po;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không thể đọc định dạng JSON Đơn đặt hàng từ tệp {FileName}", fileName);
                throw new InvalidDataException($"Tệp {fileName} không chứa dữ liệu JSON Đơn đặt hàng hợp lệ: {ex.Message}");
            }
        }

        var data = await CallExtractDocumentApiAsync(fileStream, fileName, "PO");
        if (!data.HasValue)
        {
            throw new InvalidOperationException($"Trích xuất Đơn đặt hàng thất bại từ tệp '{fileName}'. AI Engine không thể bóc tách nội dung hoặc không thể kết nối.");
        }

        var root = data.Value;
        var poObj = new PurchaseOrder
        {
            PoNumber = root.TryGetProperty("po_number", out var pNum) ? pNum.GetString() ?? "" : "",
            OrderDate = root.TryGetProperty("order_date", out var odt) && DateOnly.TryParse(odt.GetString(), out var d) ? d : DateOnly.FromDateTime(DateTime.UtcNow),
            TotalAmount = root.TryGetProperty("total_amount", out var tp) ? tp.GetDecimal() : 0
        };

        if (root.TryGetProperty("items", out var itemsElem) && itemsElem.ValueKind == JsonValueKind.Array)
        {
            int line = 1;
            foreach (var item in itemsElem.EnumerateArray())
            {
                poObj.Items.Add(new POItem
                {
                    LineNumber = line++,
                    ItemName = item.TryGetProperty("item_name", out var iname) ? iname.GetString() ?? "" : "",
                    Quantity = item.TryGetProperty("quantity", out var iqty) ? iqty.GetDecimal() : 0,
                    UnitPrice = item.TryGetProperty("unit_price", out var iupr) ? iupr.GetDecimal() : 0,
                    Unit = item.TryGetProperty("unit", out var iunit) ? iunit.GetString() ?? "cái" : "cái"
                });
            }
        }

        return poObj;
    }

    public async Task<GoodsReceipt> ExtractGoodsReceiptAsync(Stream fileStream, string fileName)
    {
        if (fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var reader = new StreamReader(fileStream);
                var content = await reader.ReadToEndAsync();
                var grn = JsonSerializer.Deserialize<GoodsReceipt>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (grn != null) return grn;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Không thể đọc định dạng JSON Phiếu nhập kho từ tệp {FileName}", fileName);
                throw new InvalidDataException($"Tệp {fileName} không chứa dữ liệu JSON Phiếu nhập kho hợp lệ: {ex.Message}");
            }
        }

        var data = await CallExtractDocumentApiAsync(fileStream, fileName, "GRN");
        if (!data.HasValue)
        {
            throw new InvalidOperationException($"Trích xuất Phiếu nhập kho thất bại từ tệp '{fileName}'. AI Engine không thể bóc tách nội dung hoặc không thể kết nối.");
        }

        var root = data.Value;
        var grnObj = new GoodsReceipt
        {
            GrnNumber = root.TryGetProperty("grn_number", out var gNum) ? gNum.GetString() ?? "" : "",
            ReceivedDate = root.TryGetProperty("received_date", out var rdt) && DateOnly.TryParse(rdt.GetString(), out var d) ? d : DateOnly.FromDateTime(DateTime.UtcNow),
            WarehouseKeeper = root.TryGetProperty("warehouse_keeper", out var wk) ? wk.GetString() : "Thủ kho"
        };

        if (root.TryGetProperty("items", out var itemsElem) && itemsElem.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in itemsElem.EnumerateArray())
            {
                grnObj.Items.Add(new GRItem
                {
                    ItemName = item.TryGetProperty("item_name", out var iname) ? iname.GetString() ?? "" : "",
                    QuantityReceived = item.TryGetProperty("quantity_received", out var iqty) ? iqty.GetDecimal() : (item.TryGetProperty("quantity", out var q) ? q.GetDecimal() : 0),
                    Unit = item.TryGetProperty("unit", out var iunit) ? iunit.GetString() ?? "cái" : "cái"
                });
            }
        }

        return grnObj;
    }

    /// <summary>
    /// So khớp 3 chiều giữa PO, GRN và Invoice bằng endpoint /api/ai/match-items
    /// Đọc đúng trường matched_invoice_item và invoice_item
    /// </summary>
    public async Task<List<ThreeWayRowDto>> MatchItemsAsync(
        ICollection<POItem> poItems,
        ICollection<InvoiceItem> invoiceItems,
        ICollection<GRItem> grItems)
    {
        try
        {
            var payload = new
            {
                po_items = poItems.Select(p => new { item_name = p.ItemName, quantity = p.Quantity, unit_price = p.UnitPrice }),
                invoice_items = invoiceItems.Select(i => new { item_name = i.ItemName, quantity = i.Quantity, unit_price = i.UnitPrice }),
                grn_items = grItems.Select(g => new { item_name = g.ItemName, quantity_received = g.QuantityReceived })
            };

            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_aiEngineUrl}/api/ai/match-items", content);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("matches", out var matchesElem))
                {
                    var results = new List<ThreeWayRowDto>();
                    var matchedGrnIds = new HashSet<Guid>();

                    foreach (var match in matchesElem.EnumerateArray())
                    {
                        var poName = match.TryGetProperty("po_item", out var p) && p.ValueKind != JsonValueKind.Null && p.TryGetProperty("item_name", out var pn)
                            ? pn.GetString() ?? "" 
                            : "";

                        // Đọc đúng trường matched_invoice_item (hoặc fallback invoice_item)
                        JsonElement iElem = default;
                        bool hasInv = (match.TryGetProperty("matched_invoice_item", out iElem) || match.TryGetProperty("invoice_item", out iElem))
                                      && iElem.ValueKind != JsonValueKind.Null;

                        var invName = hasInv && iElem.TryGetProperty("item_name", out var iname) 
                            ? iname.GetString() ?? "" 
                            : "";

                        var score = match.TryGetProperty("similarity_score", out var s) ? s.GetDecimal() : 0m;

                        var po = poItems.FirstOrDefault(x => x.ItemName == poName);
                        var inv = invoiceItems.FirstOrDefault(x => x.ItemName == invName);

                        decimal grQty = 0;
                        GRItem? chosenGrn = null;
                        decimal bestGrnScore = -1;

                        foreach (var g in grItems.Where(g => !matchedGrnIds.Contains(g.Id)))
                        {
                            var s1 = CalculateFuzzyScore(g.ItemName, poName);
                            var s2 = CalculateFuzzyScore(g.ItemName, invName);
                            var sc = Math.Max(s1, s2);
                            if (sc > bestGrnScore)
                            {
                                bestGrnScore = sc;
                                chosenGrn = g;
                            }
                        }

                        if (bestGrnScore >= 0.50m && chosenGrn != null)
                        {
                            grQty = chosenGrn.QuantityReceived;
                            matchedGrnIds.Add(chosenGrn.Id);
                        }
                        else
                        {
                            grQty = inv?.Quantity ?? (po?.Quantity ?? 0);
                        }

                        results.Add(new ThreeWayRowDto
                        {
                            PoItemName = poName,
                            PoQuantity = po?.Quantity ?? 0,
                            PoUnitPrice = po?.UnitPrice ?? 0,
                            InvoiceItemName = invName,
                            InvoiceQuantity = inv?.Quantity ?? 0,
                            InvoiceUnitPrice = inv?.UnitPrice ?? 0,
                            GrnQuantityReceived = grQty,
                            SimilarityScore = score,
                            MatchStatus = score >= 0.70m ? "MATCHED" : "UNMATCHED"
                        });
                    }

                    return results;
                }
            }
            else
            {
                var errDetail = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("AI Engine match-items trả về HTTP {Code}: {Detail}. Chuyển sang Fallback C# Matching.", 
                    (int)response.StatusCode, errDetail);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lỗi kết nối tới AI Matcher tại {Url}/api/ai/match-items. Kích hoạt C# Fuzzy Matching fallback.", _aiEngineUrl);
        }

        // Fallback C# Fuzzy Matching có ghi log minh bạch
        _logger.LogInformation("Thực thi Fallback C# Fuzzy Matching cho {PoCount} PO items và {InvCount} Invoice items.", 
            poItems.Count, invoiceItems.Count);
        return FallbackMatching(poItems, invoiceItems, grItems);
    }

    private List<ThreeWayRowDto> FallbackMatching(
        ICollection<POItem> poItems,
        ICollection<InvoiceItem> invoiceItems,
        ICollection<GRItem> grItems)
    {
        var rows = new List<ThreeWayRowDto>();
        var matchedInv = new HashSet<Guid>();
        var matchedGrn = new HashSet<Guid>();

        foreach (var po in poItems)
        {
            InvoiceItem? bestInv = null;
            decimal bestScore = -1;

            foreach (var inv in invoiceItems.Where(i => !matchedInv.Contains(i.Id)))
            {
                var score = CalculateFuzzyScore(po.ItemName, inv.ItemName);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestInv = inv;
                }
            }

            var isMatched = bestScore >= 0.50m && bestInv != null;
            if (isMatched && bestInv != null)
            {
                matchedInv.Add(bestInv.Id);
            }

            decimal grQty = 0;
            GRItem? chosenGrn = null;
            decimal bestGrScore = -1;

            foreach (var g in grItems.Where(g => !matchedGrn.Contains(g.Id)))
            {
                var s = CalculateFuzzyScore(g.ItemName, po.ItemName);
                if (s > bestGrScore)
                {
                    bestGrScore = s;
                    chosenGrn = g;
                }
            }

            if (bestGrScore >= 0.50m && chosenGrn != null)
            {
                grQty = chosenGrn.QuantityReceived;
                matchedGrn.Add(chosenGrn.Id);
            }
            else
            {
                grQty = isMatched && bestInv != null ? bestInv.Quantity : po.Quantity;
            }

            rows.Add(new ThreeWayRowDto
            {
                PoItemName = po.ItemName,
                PoQuantity = po.Quantity,
                PoUnitPrice = po.UnitPrice,
                InvoiceItemName = bestInv?.ItemName ?? "",
                InvoiceQuantity = bestInv?.Quantity ?? 0,
                InvoiceUnitPrice = bestInv?.UnitPrice ?? 0,
                GrnQuantityReceived = grQty,
                SimilarityScore = bestScore >= 0 ? bestScore : 0m,
                MatchStatus = isMatched ? "MATCHED" : "UNMATCHED"
            });
        }

        foreach (var inv in invoiceItems.Where(i => !matchedInv.Contains(i.Id)))
        {
            rows.Add(new ThreeWayRowDto
            {
                PoItemName = "",
                PoQuantity = 0,
                PoUnitPrice = 0,
                InvoiceItemName = inv.ItemName,
                InvoiceQuantity = inv.Quantity,
                InvoiceUnitPrice = inv.UnitPrice,
                GrnQuantityReceived = 0,
                SimilarityScore = 0m,
                MatchStatus = "UNMATCHED"
            });
        }

        return rows;
    }

    private decimal CalculateFuzzyScore(string str1, string str2)
    {
        if (string.IsNullOrWhiteSpace(str1) || string.IsNullOrWhiteSpace(str2)) return 0;
        var s1 = str1.Trim().ToLowerInvariant();
        var s2 = str2.Trim().ToLowerInvariant();

        if (s1 == s2) return 1.0m;
        if (s1.Contains(s2) || s2.Contains(s1)) return 0.85m;

        var set1 = s1.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var set2 = s2.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();

        int intersect = set1.Intersect(set2).Count();
        int union = set1.Union(set2).Count();

        return union == 0 ? 0 : (decimal)intersect / union;
    }
}
