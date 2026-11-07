# Nhật ký thay đổi (Changelog) - AuditGuard-AI

Tất cả các thay đổi quan trọng của dự án sẽ được ghi nhận tại đây theo thứ tự thời gian mới nhất lên đầu.

Định dạng dựa trên [Keep a Changelog](https://keepachangelog.com/).

---
## [Khắc phục Triệt để DbUpdateConcurrencyException với ExecuteDelete/Update] - 2026-11-07
### Đã sửa (Fixed)
- **Giải quyết lỗi `The database operation was expected to affect 1 row(s), but actually affected 0 row(s)`**:
  - Loại bỏ hoàn toàn phương thức `Discrepancies.Clear()` gây desync ChangeTracker trong `AuditProcessingWorker`.
  - Sử dụng trực tiếp `ExecuteDeleteAsync` để xóa các sai lệch cũ của phiên đối soát trên database, ngăn ngừa lỗi optimistic concurrency khi collection chưa nạp đầy đủ.
  - Sử dụng `ExecuteUpdateAsync` khi cập nhật trạng thái `Rejected` trong khối catch để đảm bảo cập nhật trạng thái lỗi độc lập, không bị ảnh hưởng bởi trạng thái ChangeTracker của các entity khác.
  - Thêm mới trực tiếp danh sách sai lệch qua `dbContext.AuditDiscrepancies.Add(...)`.

## [Cấu hình Kiểu Dữ liệu JSONB cho raw_ocr_json] - 2026-11-07
### Đã sửa (Fixed)
- **Khắc phục lỗi `42804: column "raw_ocr_json" is of type jsonb but expression is of type text`**:
  - Bổ sung cấu hình `.HasColumnType("jsonb")` cho thuộc tính `Invoice.RawOcrJson` trong [AuditDbContext.cs](file:///c:/Users/aaa/Pictures/SaveCode/AuditGuard-AI/server/src/AuditGuard.Infrastructure/Persistence/AuditDbContext.cs), đảm bảo Npgsql ánh xạ chính xác kiểu dữ liệu PostgreSQL JSONB.
  - Xử lý chuỗi rỗng sang `null` trước khi lưu vào CSDL trong `AuditProcessingWorker` và `AuditController` để tránh vi phạm cú pháp JSON.

## [Loại bỏ Shadow Property vendor_id & Tối ưu Lưu trữ Idempotent] - 2026-11-07
### Đã sửa (Fixed)
- **Khắc phục lỗi `column "vendor_id" of relation "invoices" does not exist`**:
  - Loại bỏ thuộc tính điều hướng `Invoices` trong entity `Vendor` ([Entities.cs](file:///c:/Users/aaa/Pictures/SaveCode/AuditGuard-AI/server/src/AuditGuard.Domain/Entities/Entities.cs)) do bảng `invoices` trong PostgreSQL chỉ lưu trực tiếp `vendor_tax_code` và không có cột khóa ngoại `vendor_id`.
  - Ngăn chặn hoàn toàn việc EF Core tự động sinh câu lệnh SQL `SELECT ... i.vendor_id` và `INSERT ... vendor_id`.
- **Cơ chế lưu trữ an toàn (Idempotent Persistence)**:
  - Tự động kiểm tra trùng lặp `po_number` và `grn_number` trước khi thêm vào CSDL trong `AuditProcessingWorker` và `AuditController`, tái sử dụng bản ghi nếu kịch bản được chạy nhiều lần để tránh vi phạm Unique Constraint.
  - Tự động kiểm tra và gắn `VendorId` theo `tax_code`, ngăn chặn việc EF Core thêm trùng lặp Vendor.
  - Hiển thị chi tiết `ex.InnerException.Message` khi xảy ra lỗi lưu CSDL qua SignalR để người dùng dễ dàng nắm bắt nguyên nhân.

## [Sửa lỗi Ánh xạ Khóa ngoại EF Core Khớp Schema PostgreSQL] - 2026-11-07
### Đã sửa (Fixed)
- **Khắc phục lỗi `column a.goods_receipt_id does not exist`**:
  - Cấu hình rõ ràng các khóa ngoại (Foreign Keys) trong `AuditDbContext.cs` (`OnModelCreating`):
    - Ánh xạ navigation `AuditSession.GoodsReceipt` sử dụng `GrnId` (`grn_id`).
    - Ánh xạ navigation `AuditSession.PurchaseOrder` sử dụng `PoId` (`po_id`).
    - Ánh xạ navigation `AuditSession.Invoice` sử dụng `InvoiceId` (`invoice_id`).
    - Ánh xạ navigation `GoodsReceipt.PurchaseOrder` sử dụng `PoId` (`po_id`).
    - Ánh xạ navigation `PurchaseOrder.Vendor` sử dụng `VendorId` (`vendor_id`).
  - Ngăn chặn hoàn toàn việc EF Core tự sinh shadow property (`goods_receipt_id`), bảo đảm tương thích 100% với schema `01-init.sql`.

## [Cập nhật Toàn diện Tài liệu Hướng dẫn Khởi chạy] - 2026-11-06
### Đã tài liệu hóa (Documented)
- **Cập nhật `README.md` (Quick Start Guide)**:
  - Bổ sung bảng ánh xạ cổng dịch vụ chi tiết (Frontend: `3000`, Backend: `5000`, AI Engine: `8000`, Ollama: `11434`, PostgreSQL: `5432`).
  - Hướng dẫn rõ ràng 2 cách khởi chạy:
    - **Cách 1 (Khuyến nghị 1-Click từ thư mục gốc)**: Khởi động database `docker compose up -d postgres`, Backend `dotnet run`, AI Engine `python main.py` (tự động bật Ollama và tải model), Frontend `npm run dev`.
    - **Cách 2 (Thủ công từng dịch vụ)**: Hướng dẫn chi tiết từng câu lệnh cho việc gỡ lỗi hoặc phát triển độc lập.
  - Sửa đổi các cổng và model cũ (điều chỉnh từ port 5173 thành 3000, model mặc định `qwen2.5:3b`).

## [Sửa lỗi Cú pháp Extractor, Tối ưu hóa Launcher AI & Chuẩn hóa Khởi chạy] - 2026-11-06
### Đã sửa & Tinh chỉnh (Fixed & Refined)
- **Sửa lỗi cú pháp trích xuất AI**:
  - Khắc phục lỗi `SyntaxError: invalid syntax` tại `ai-engine/services/extractor.py` (do ký tự `"""{raw_text}"""` lồng trong f-string đóng sớm chuỗi đa dòng). Đã chuẩn hóa phân cách văn bản ở cả 3 hàm trích xuất PO, GRN và Hóa đơn VAT.
- **Nâng cấp Launcher AI (`main.py`)**:
  - So sánh chính xác tag mô hình: Chuyển sang so sánh khớp chính xác tag `qwen2.5:3b` từ Ollama API (`api/tags`), loại bỏ kiểm tra tiền tố `qwen2.5` lỏng lẻo.
  - Kiểm tra phụ thuộc toàn diện: Hàm `ensure_dependencies()` được mở rộng để kiểm tra trọn vẹn 12 module cốt lõi tương ứng `requirements.txt` (thay vì chỉ kiểm tra 3 module), tự động cài đặt qua pip nếu thiếu.
- **Chuẩn hóa Cổng kết nối & Hướng dẫn Môi trường**:
  - Thống nhất cổng Backend là `http://localhost:5000` trên toàn bộ hệ thống (`launchSettings.json`, Vite proxy, Kestrel entry point).
  - Bổ sung rõ ràng bước khởi chạy cơ sở dữ liệu PostgreSQL qua Docker (`docker compose up -d postgres`).
  - Ghi nhận cơ chế khóa tệp (File Lock) của Windows: Khi lệnh `dotnet run` đang chiếm dụng DLL trong một tiến trình terminal khác, lệnh `dotnet build` chạy song song sẽ bị chặn ghi (mã thoát 1). Khi dừng tiến trình đang chạy, `dotnet build` và `dotnet test` trên Solution hoàn tất thành công với mã thoát 0.

## [Hỗ trợ Trực tiếp Lệnh 'dotnet run' tại Thư mục Gốc] - 2026-11-06
### Đã thêm & Tối ưu hóa (Added & Optimized)
- **Hỗ trợ gõ trực tiếp `dotnet run` không cần cờ tham số**:
  - Tạo `AuditGuard-AI.csproj` và `Program.cs` ngay tại thư mục gốc để làm entry point chuyển tiếp.
  - Cấu hình `DefaultItemExcludes` chuẩn xác để ngăn MSBuild quét đệ quy các thư mục con (`server/`, `client/`, `ai-engine/`), tránh hoàn toàn xung đột trùng lặp mã nguồn.
  - Tự động nạp assembly Controllers của API và ánh xạ cổng mặc định `http://localhost:5000` khớp 100% với cấu hình Proxy của Web Dashboard Vite.
  - Thêm `AuditGuard-AI.csproj` vào `AuditGuard.sln`, đảm bảo `dotnet build` đạt 0 lỗi 0 cảnh báo cho toàn bộ 6 dự án.

## [Tối ưu hóa Khởi chạy Dự án & Tích hợp 1-Click Root Run] - 2026-11-06
### Đã thêm & Cải tiến (Added & Improved)
- **Tự động hóa Khởi chạy Toàn diện từ Thư mục Gốc**:
  - `main.py` (Root): Tự động phát hiện cổng 11434, tự khởi động tiến trình ngầm `ollama serve` nếu Ollama chưa chạy, kiểm tra và hướng dẫn tải model `qwen2.5:3b`, sau đó khởi chạy FastAPI AI Engine trên cổng 8000.
  - `package.json` (Root): Bổ sung lệnh `npm run dev` và `npm run build` ủy quyền trực tiếp vào thư mục `client/`, cho phép khởi chạy giao diện Web Dashboard ngay tại thư mục gốc.
  - `AuditGuard.sln` (Root): Bổ sung file Solution tại thư mục gốc chứa trọn vẹn 5 dự án (.NET 8), cho phép build và test nhanh bằng `dotnet build` và `dotnet test`.
- **Hoàn thiện Logic Xử lý Kịch bản & Dò tìm Đường dẫn Mẫu**:
  - Triển khai hàm `ResolveDataSamplesPath` trong `AuditProcessingWorker` và `AuditController` tự động tìm ngược lên các cấp thư mục cha để định vị chính xác thư mục `data-samples/`.
  - Hỗ trợ linh hoạt cả hai định dạng đặt tên file phiếu kho (`goods_receipt.json` và `goods_receipt_note.json`).
- **Xác thực Kiểm thử & Biên dịch (0 Error, 0 Warning)**:
  - `dotnet build AuditGuard.sln`: Hoàn thành 100% với 0 lỗi, 0 cảnh báo cho cả 5 project .NET 8.
  - `dotnet test AuditGuard.sln`: 100% Passed (3/3 tests passed).
  - `npm run build`: Hoàn thành 100% production bundle cho React 18 TypeScript.

## [Giai đoạn 3: Hoàn thiện Web Dashboard React 18 + Vite + Tailwind CSS] - 2026-11-06
### Đã thêm & Tích hợp (Added & Integrated)
- **Hệ thống Giao diện Web Dashboard**: Khởi tạo dự án React 18 + TypeScript + Vite tại `client/` với phong cách Dark Mode cao cấp (Glassmorphism, Slate-950, Neon Cyan/Emerald accents, typography Plus Jakarta Sans).
- **Kết nối SignalR Realtime Hub chuẩn xác**:
  - Client tự động kết nối `/hubs/audit`, tạo `clientSessionId` và gọi `JoinSession(sessionId)` trước khi gửi yêu cầu, bảo đảm không bỏ lỡ bất kỳ bước tiến trình nào.
  - Lắng nghe sự kiện `ReceiveProgress` và `ReceiveAuditCompleted` để tự động cập nhật thanh tiến trình 5 bước và kết quả kiểm toán.
- **Thành phần ScenarioSelector (Demo 1-Click)**: Hiển thị 20 kịch bản mẫu từ API `GET /api/audit/scenarios`, phân loại theo rủi ro (Hợp lệ, Gian lận, Sai lệch) kèm nút kích hoạt đối soát tức thì.
- **Thành phần FileUploadZone (Tải lên 3 tệp)**: Giao diện kéo thả 3 tệp riêng biệt (PO, Phiếu kho GRN, Hóa đơn VAT), giới hạn định dạng an toàn (.pdf/.json, tối đa 20MB) và cảnh báo rõ ràng về ảnh scan.
- **Thành phần ThreeWayMatchingTable (Đối soát 3 chiều)**: Bảng đối chiếu từng dòng hàng chi tiết giữa PO vs Kho GRN vs Hóa đơn, hiển thị điểm tương đồng ngữ nghĩa AI (Cosine Similarity %) và highlight trực quan màu sắc.
- **Thành phần DiscrepancyList & DisputeEmailModal**: Danh sách chi tiết các sai lệch tài chính kèm popup sinh thư khiếu nại nhà cung cấp tự động (`POST /api/audit/sessions/{id}/email`) hỗ trợ sao chép 1-click.
- **Khôi phục kết quả bền vững**: Tích hợp lưu trữ L1 cache ở `localStorage` để bảo toàn trọn vẹn `ComparisonRows` và điểm semantic ngay cả khi người dùng tải lại trang.
- **Xác nhận biên dịch Production**: `npm run build` hoàn tất thành công 100% không có lỗi TypeScript hay Vite bundle.

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
