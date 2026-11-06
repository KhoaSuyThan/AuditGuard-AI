# Nhật ký thay đổi (Changelog) - AuditGuard-AI

Tất cả các thay đổi quan trọng của dự án sẽ được ghi nhận tại đây theo thứ tự thời gian mới nhất lên đầu.

Định dạng dựa trên [Keep a Changelog](https://keepachangelog.com/).

---
## [Giai đoạn 2: Sửa Blocker Tích Hợp End-to-End & Tạo Solution Chuẩn .sln] - 2026-11-06
### Đã sửa đổi & Gia cố (Fixed & Hardened)
- **Sửa trường Matching**: Đọc chuẩn xác thuộc tính `matched_invoice_item` (kèm fallback `invoice_item`) từ kết quả API `match-items` của AI Engine.
- **Phân tách Schema chuyên biệt PO / GRN / Invoice**:
  - Mở rộng `ai-engine/services/extractor.py` và `main.py` hỗ trợ tham số `document_type`: `ExtractedPOSchema`, `ExtractedGRNSchema`, `ExtractedInvoiceSchema`.
  - Phía backend C#: `ExtractPurchaseOrderAsync`, `ExtractGoodsReceiptAsync`, `ExtractInvoiceAsync` gửi đúng `document_type` và bóc tách thực tế từng loại chứng từ.
- **Minh bạch hóa Fallback AI & Không trả Entity giả ngầm**:
  - Khi AI Engine gặp lỗi hoặc timeout: Ném ngoại lệ `InvalidOperationException` rõ ràng, ghi log cảnh báo chi tiết qua `ILogger`.
  - Tuyệt đối không sinh dữ liệu giả định (`INV-FALLBACK-...`); nếu bóc tách lỗi thì cập nhật trạng thái phiên là `REJECTED/FAILED` và báo lỗi cho người dùng.
- **Đồng nhất tên tệp kịch bản mẫu**: Ưu tiên đọc `goods_receipt.json` trong cả nhánh đồng bộ (`RunScenario`) và bất đồng bộ (`AuditProcessingWorker`), tương thích 100% với 20 bộ kịch bản trong `data-samples/`.
- **Khởi tạo tệp Solution chuẩn `AuditGuard.sln`**: Bổ sung tệp Solution định dạng chuẩn VS 12.00 (bên cạnh `AuditGuard.slnx`) chứa đủ 5 projects; xác nhận `dotnet build` và `dotnet test` chạy thành công (0 Warning, 0 Error, 100% Tests Passed).

## [Giai đoạn 2: Hoàn thiện luồng End-to-End, Bóc tách 3 file & Khớp EF Schema] - 2026-11-06
### Đã sửa đổi & Gia cố (Fixed & Hardened)
- **Chuẩn hóa Endpoint AI Engine**: Sửa chính xác endpoint AI Service sang `/api/ai/extract-document` và `/api/ai/match-items`, loại bỏ hoàn toàn việc rơi vào fallback ngầm.
- **Bóc tách độc lập 3 tệp chứng từ**: Cài đặt `ExtractPurchaseOrderAsync`, `ExtractGoodsReceiptAsync`, và `ExtractInvoiceAsync`. Worker bóc tách độc lập và thực sự cả 3 tệp (PO, Phiếu kho, Hóa đơn) do người dùng tải lên, không tự sinh mock data.
- **Xử lý lỗi CSDL minh bạch**: Loại bỏ hoàn toàn các khối `catch { }` rỗng. Bắt lỗi có cấu trúc, log chi tiết qua `ILogger`, cập nhật `Status = AuditStatus.Rejected` kèm nguyên nhân cụ thể trong `Notes`, và phát sự kiện `Failed` qua SignalR.
- **Đồng bộ 100% EF Core Schema với 01-init.sql**: Cấu hình quy chuẩn `snake_case` tự động cho toàn bộ bảng và cột (`purchase_orders`, `po_items`, `goods_receipts`, `gr_items`, `invoices`, `invoice_items`, `audit_sessions`, `audit_discrepancies`), chuyển đổi Enum sang chuỗi (`HasConversion<string>()`), và cấu hình khóa ngoại rõ ràng.
- **Gia cố an toàn tệp Upload**: Kiểm tra định dạng hợp lệ (`.pdf`, `.json`, `.png`, `.jpg`, `.jpeg`, `.webp`), giới hạn dung lượng tối đa 20MB, và sử dụng thư mục GUID cách ly kèm tên ngẫu nhiên chống tấn công Path Traversal.
- **Lưu trữ trạng thái Job bền vững**: Khởi tạo bản ghi `AuditSession` với trạng thái `PENDING` ngay khi tiếp nhận job vào Channel; hỗ trợ tra cứu tiến trình qua `GET /api/audit/sessions/{id}` mà không lo mất dấu.

## [Giai đoạn 2: Chuẩn hóa .NET 8 (LTS) & Background Worker Channel] - 2026-11-06
### Đã thay đổi & Cải tiến (Changed & Improved)
- **Chuẩn hóa TargetFramework**: Chuyển toàn bộ 5 project từ `net9.0` về `.NET 8` (`net8.0` LTS) kèm `global.json` cố định SDK, giải quyết triệt để xung đột tooling/workload giữa các bản preview.
- **Đồng bộ Package NuGet .NET 8**: Sử dụng `Npgsql.EntityFrameworkCore.PostgreSQL 8.0.11`, `Pgvector.EntityFrameworkCore 0.2.1`, `Microsoft.EntityFrameworkCore.Design 8.0.11`, và `Swashbuckle.AspNetCore 6.6.2`.
- **Hàng đợi & Worker bất đồng bộ**: Triển khai `IAuditTaskQueue` với `System.Threading.Channels.Channel<AuditBackgroundJob>` và `AuditProcessingWorker` (`BackgroundService`) điều phối toàn bộ chu trình OCR -> LLM -> 3-Way Matching -> SignalR Progress -> EF Core Database.
- **Bổ sung API Upload & Run Async**: Thêm endpoint `POST /api/audit/sessions/upload-async` và `POST /api/audit/scenarios/{folderName}/run-async` trả về `202 Accepted` kèm `sessionId`.
- **Hoàn thiện Persistence EF Core**: Tích hợp `EnsureCreated()` trong `Program.cs` và đọc/ghi đầy đủ `AuditSession`, `AuditDiscrepancy` thông qua PostgreSQL.

## [Giai đoạn 2: Backend Core API .NET 9] - 2026-11-06
### Đã thêm (Added)
- Tích hợp EF Core PostgreSQL (`AuditDbContext`) lưu bền vững phiên đối soát (`AuditSession`), chứng từ (`PO`, `GRN`, `Invoice`) và các sai lệch (`Discrepancies`).
- Xây dựng endpoint `POST /api/audit/sessions/upload` tiếp nhận 3 tệp tin (PO, GRN, Hóa đơn) và điều phối trích xuất AI song song.
- Bổ sung SignalR Hub (`AuditHub`) với cơ chế cô lập tiến trình theo từng phiên (`JoinSession` / `Clients.Group(sessionId)`).
- Hoàn thiện bộ quy tắc kiểm tra số lượng 3 chiều toàn diện (`INVOICE_EXCEEDS_GRN`, `SHORT_SHIPMENT`, `OVER_DELIVERY`, `ExtraItem`).
- Bổ sung kiểm tra tổng tiền và cộng dồn dòng hàng: So sánh tổng chi tiết dòng hàng vs Subtotal hóa đơn, kiểm tra tính đúng thuế suất VAT, kiểm tra Total Payable vs Subtotal + Tax.
- Áp dụng thuật toán khử trùng lặp 1-to-1 độc quyền (`matchedGrnIds` HashSet) khi ghép dòng kho GRN.
- Bổ sung phản hồi lỗi có cấu trúc (`ProblemDetails`) chống crash 500 khi dữ liệu thiếu hoặc sai định dạng.

## [Giai đoạn 1: AI Engine Service FastAPI] - 2026-11-06
### Đã thêm (Added)
- Xây dựng dịch vụ FastAPI đa luồng cho OCR, LLM Information Extraction và Vector Similarity Matcher.
- Hỗ trợ OCR đa định dạng (PDF điện tử, PDF scan tự render ảnh, hình ảnh JPG/PNG/WEBP).
- Tích hợp Ollama Qwen 2.5 với xác thực chặt chẽ qua Pydantic schema `ExtractedInvoiceSchema`.
- Cài đặt thuật toán Hungarian và Global Best-Edge Bipartite Matching cho độ chính xác đối soát toàn cục.

## [Giai đoạn 0: Khởi tạo hạ tầng & Dữ liệu mẫu] - 2026-11-05
### Đã thêm (Added)
- Cấu hình Docker Compose với PostgreSQL 16 + pgvector.
- Tạo 20 bộ kịch bản dữ liệu mẫu (80 tệp JSON) đa dạng các ngành nghề và loại sai phạm kế toán.
