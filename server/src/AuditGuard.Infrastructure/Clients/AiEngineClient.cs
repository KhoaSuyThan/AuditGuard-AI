using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AuditGuard.Application.DTOs;
using AuditGuard.Application.Interfaces;
using AuditGuard.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace AuditGuard.Infrastructure.Clients;

public class AiEngineClient : IAiEngineClient
{
    private readonly HttpClient _httpClient;
    private readonly string _aiEngineUrl;

    public AiEngineClient(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _aiEngineUrl = configuration["AiEngine:BaseUrl"] ?? "http://localhost:8000";
    }

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
                threshold = 0.50
            };

            var response = await _httpClient.PostAsJsonAsync($"{_aiEngineUrl}/api/ai/match-items", payload);
            if (response.IsSuccessStatusCode)
            {
                var jsonDoc = await response.Content.ReadFromJsonAsync<JsonElement>();
                if (jsonDoc.TryGetProperty("matches", out var matchesElem))
                {
                    var results = new List<ThreeWayRowDto>();
                    var matchedGrnIds = new HashSet<Guid>();

                    foreach (var match in matchesElem.EnumerateArray())
                    {
                        var poName = match.TryGetProperty("po_item", out var p) && p.ValueKind != JsonValueKind.Null ? p.GetProperty("item_name").GetString() ?? "" : "";
                        var poQty = p.ValueKind != JsonValueKind.Null && p.TryGetProperty("quantity", out var pq) ? pq.GetDecimal() : 0m;
                        var poPrice = p.ValueKind != JsonValueKind.Null && p.TryGetProperty("unit_price", out var pp) ? pp.GetDecimal() : 0m;

                        var invElem = match.GetProperty("matched_invoice_item");
                        var invName = invElem.ValueKind != JsonValueKind.Null && invElem.TryGetProperty("item_name", out var iname) ? iname.GetString() ?? "" : "";
                        var invQty = invElem.ValueKind != JsonValueKind.Null && invElem.TryGetProperty("quantity", out var iq) ? iq.GetDecimal() : 0m;
                        var invPrice = invElem.ValueKind != JsonValueKind.Null && invElem.TryGetProperty("unit_price", out var ip) ? ip.GetDecimal() : 0m;

                        var score = match.TryGetProperty("similarity_score", out var sc) ? sc.GetDecimal() : 0m;
                        var isMatched = match.TryGetProperty("is_confident_match", out var cm) && cm.GetBoolean();

                        // SỬA LỖI MAPPING GRN: Loại trừ các dòng kho đã ghép để không bị ghép trùng lặp
                        decimal grnQty = 0;
                        GRItem? bestGrn = null;
                        decimal bestGrnScore = -1;

                        foreach (var g in grItems.Where(g => !matchedGrnIds.Contains(g.Id)))
                        {
                            var s1 = CalculateFuzzyScore(g.ItemName, poName);
                            var s2 = CalculateFuzzyScore(g.ItemName, invName);
                            var s = Math.Max(s1, s2);
                            if (s > bestGrnScore)
                            {
                                bestGrnScore = s;
                                bestGrn = g;
                            }
                        }

                        if (bestGrnScore >= 0.50m && bestGrn != null)
                        {
                            grnQty = bestGrn.QuantityReceived;
                            matchedGrnIds.Add(bestGrn.Id);
                        }

                        results.Add(new ThreeWayRowDto
                        {
                            PoItemName = poName,
                            PoQuantity = poQty,
                            PoUnitPrice = poPrice,
                            GrnQuantityReceived = grnQty,
                            InvoiceItemName = invName,
                            InvoiceQuantity = invQty,
                            InvoiceUnitPrice = invPrice,
                            SimilarityScore = score,
                            MatchStatus = isMatched ? "MATCHED" : "UNMATCHED"
                        });
                    }
                    if (results.Any()) return results;
                }
            }
        }
        catch
        {
            // Fallback sang C# Local Matcher nếu AI Service chưa khởi động
        }

        return FallbackLocalMatch(poItems, invoiceItems, grItems);
    }

    private static List<ThreeWayRowDto> FallbackLocalMatch(
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

            var isMatched = bestScore >= 0.50m;
            if (isMatched && bestInv != null)
            {
                matchedInv.Add(bestInv.Id);
            }

            // Gán GRN độc quyền
            decimal grQty = 0;
            GRItem? chosenGrn = null;
            decimal bestGrScore = -1;

            foreach (var g in grItems.Where(g => !matchedGrn.Contains(g.Id)))
            {
                var s = CalculateFuzzyScore(g.ItemName, po.ItemName);
                if (s > bestGrScore)
                {
                    bestGrScore = s;
                    bestGrnScore:
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
                GrnQuantityReceived = grQty,
                InvoiceItemName = isMatched && bestInv != null ? bestInv.ItemName : "",
                InvoiceQuantity = isMatched && bestInv != null ? bestInv.Quantity : 0,
                InvoiceUnitPrice = isMatched && bestInv != null ? bestInv.UnitPrice : 0,
                SimilarityScore = bestScore > 0 ? bestScore : 0,
                MatchStatus = isMatched ? "MATCHED" : "UNMATCHED"
            });
        }

        // Bổ sung các mặt hàng hóa đơn không ghép được (Extra Item)
        foreach (var inv in invoiceItems.Where(i => !matchedInv.Contains(i.Id)))
        {
            rows.Add(new ThreeWayRowDto
            {
                PoItemName = "",
                PoQuantity = 0,
                PoUnitPrice = 0,
                GrnQuantityReceived = 0,
                InvoiceItemName = inv.ItemName,
                InvoiceQuantity = inv.Quantity,
                InvoiceUnitPrice = inv.UnitPrice,
                SimilarityScore = 0,
                MatchStatus = "EXTRA_ITEM"
            });
        }

        return rows;
    }

    private static decimal CalculateFuzzyScore(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return 0;
        var wordsA = Regex.Replace(a.ToLower(), @"[^\w\s]", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var wordsB = Regex.Replace(b.ToLower(), @"[^\w\s]", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        if (!wordsA.Any() || !wordsB.Any()) return 0;

        var intersect = wordsA.Intersect(wordsB).Count();
        var union = wordsA.Union(wordsB).Count();
        decimal jaccard = (decimal)intersect / union;

        var hasModelA = wordsA.Any(w => w.Any(char.IsDigit));
        var hasModelB = wordsB.Any(w => w.Any(char.IsDigit));
        if (hasModelA && hasModelB && wordsA.Intersect(wordsB).Any(w => w.Any(char.IsDigit)))
        {
            jaccard = Math.Min(1.0m, jaccard + 0.35m);
        }

        return Math.Round(jaccard, 4);
    }
}
