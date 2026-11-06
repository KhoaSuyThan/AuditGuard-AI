# 📋 KẾ HOẠCH TRIỂN KHAI MÃ NGUỒN CHI TIẾT (IMPLEMENTATION PLAN)
## Dự án: AuditGuard-AI (Autonomous 3-Way Matching & Financial Document Audit Agent)
**Mục tiêu:** Xây dựng hoàn chỉnh phiên bản MVP chạy 100% Local phục vụ thi đấu Chung kết Hackathon *AI FOR BUSINESS INNOVATION* (19/11).

---

## 📅 LỘ TRÌNH TỔNG QUAN (4 GIAI ĐOẠN)

```
[ Giai đoạn 0: Hạ tầng & Dữ liệu ] ──► [ Giai đoạn 1: AI Engine Service ]
                                                │
[ Giai đoạn 3: Frontend Web Dashboard ] ◄── [ Giai đoạn 2: Backend Core .NET 8 ]
                │
                ▼
[ Giai đoạn 4: Tích hợp, Test 4 Kịch bản & Chuẩn bị Pitching Demo ]
```

---

## 🛠️ CHI TIẾT CÁC GIAI ĐOẠN TRIỂN KHAI

### GIAI ĐOẠN 0: THIẾT LẬP HẠ TẦNG VÀ DỮ LIỆU MẪU (1 NGÀY)
* **Mục tiêu:** Đảm bảo toàn bộ môi trường chạy Local sẵn sàng và có bộ dữ liệu kiểm thử thực tế.
* **Các công việc cụ thể:**
  1. **Khởi tạo Docker Database:**
     - Tạo file `docker-compose.yml` chạy `pgvector/pgvector:pg16`.
     - Cấu hình cổng `5432`, user `audituser`, password `auditpass`, database `auditguard_db`.
     - Kích hoạt extension `CREATE EXTENSION IF NOT EXISTS vector;`.
  2. **Chuẩn bị Local LLM Runtime (Ollama):**
     - Cài đặt Ollama và tải model: `ollama pull qwen2.5:7b-instruct` (hoặc `qwen2.5:3b-instruct`).
     - Kiểm tra phản hồi qua curl/Postman tại `http://localhost:11434/api/generate`.
  3. **Tạo bộ dữ liệu mẫu (Synthetic Dataset) trong `data-samples/`:**
     - **Kịch bản 1 (Perfect Match):** 1 PO + 1 Phiếu kho + 1 Hóa đơn khớp 100% về mặt hàng, số lượng và đơn giá.
     - **Kịch bản 2 (Price Mismatch):** Hóa đơn có 1 mặt hàng bị tăng giá 15% so với PO đã ký.
     - **Kịch bản 3 (Quantity Mismatch):** Hóa đơn ghi 100 sản phẩm nhưng phiếu nhập kho thực tế chỉ có 85 sản phẩm (giao thiếu hàng).
     - **Kịch bản 4 (Duplicate Payment):** Hóa đơn có mã số đã từng được thanh toán trong phiên đối soát trước đó.

---

### GIAI ĐOẠN 1: PHÁT TRIỂN AI ENGINE SERVICE (PYTHON FASTAPI) (2 NGÀY)
* **Thư mục:** `ai-engine/`
* **Mục tiêu:** Cung cấp API nội bộ cho Backend gọi để OCR văn bản, bóc tách JSON và ghép cặp ngữ nghĩa.
* **Các module cần xây dựng:**
  1. `services/ocr_service.py`:
     - Phân loại định dạng: Nếu file là digital PDF -> dùng `pdfplumber`/`PyMuPDF` bóc text trong 0.1s.
     - Nếu file là ảnh chụp/scan -> gọi `PaddleOCR` (model tiếng Việt `ch_PP-OCRv4`) nhận diện text và tọa độ khối.
  2. `services/extractor.py`:
     - Tích hợp Ollama client kết nối `qwen2.5:7b-instruct`.
     - Thiết lập System Prompt ép kiểu dữ liệu trả về theo Pydantic Schema:
       `{ invoice_number, invoice_date, vendor: {name, tax_code}, items: [{item_name, quantity, unit_price, total_amount}], tax_amount, total_payable }`.
  3. `services/matcher.py`:
     - Sử dụng thư viện `sentence-transformers` với model `BAAI/bge-m3`.
     - Hàm tính toán độ tương đồng Cosine giữa 2 chuỗi tên mặt hàng:
       `calculate_similarity(name_in_po, name_in_invoice) -> float [0.0 - 1.0]`.
     - Ngưỡng matching tự động: $\ge 0.82$.
  4. `main.py` (FastAPI Server):
     - Endpoint `POST /api/ai/extract-document`: Nhận file upload -> trả về JSON cấu trúc.
     - Endpoint `POST /api/ai/match-items`: Nhận danh sách 2 nhóm mặt hàng -> trả về danh sách cặp ghép nối kèm điểm tương đồng.

---

### GIAI ĐOẠN 2: PHÁT TRIỂN BACKEND CORE API (.NET 8 WEB API) (3 NGÀY)
* **Thư mục:** `server/`
* **Kiến trúc:** Clean Architecture (Domain, Application, Infrastructure, Api).
* **Các module cần xây dựng:**
  1. **Domain Layer (`AuditGuard.Domain`):**
     - Entities: `Vendor`, `PurchaseOrder`, `POItem`, `GoodsReceipt`, `GRItem`, `Invoice`, `InvoiceItem`, `AuditSession`, `AuditDiscrepancy`.
     - Enums: `DocumentType`, `AuditStatus (PENDING, PASSED, FLAGGED, REJECTED)`, `DiscrepancyType (PRICE_MISMATCH, QTY_MISMATCH, DUPLICATE_INVOICE, TAX_ERROR)`.
  2. **Infrastructure Layer (`AuditGuard.Infrastructure`):**
     - EF Core `ApplicationDbContext` kết nối PostgreSQL.
     - Cấu hình lưu trữ cột Vector (`Npgsql.EntityFrameworkCore.PostgreSQL` + `pgvector`).
     - `LocalDocumentStorageService`: Lưu file PDF/ảnh vào thư mục mã hóa local.
  3. **Application Layer & Business Logic (`AuditGuard.Application`):**
     - **Verification Engine (Động cơ đối soát 3 chiều):**
       + `ValidateQuantities()`: So sánh số lượng PO vs Kho vs Hóa đơn.
       + `ValidatePricing()`: So sánh đơn giá PO vs Hóa đơn.
       + `ValidateTaxAndTotal()`: Kiểm tra tính đúng đắn của thuế GTGT và tổng cộng.
       + `DetectDuplicates()`: Tìm kiếm hóa đơn trùng số hiệu hoặc mã số thuế trong lịch sử.
     - **Async Job Worker:** Sử dụng `System.Threading.Channels` chạy ngầm quy trình OCR -> LLM -> So khớp mà không chặn luồng HTTP.
  4. **API Layer (`AuditGuard.Api`):**
     - `POST /api/audit/sessions`: Tạo phiên đối soát mới, nhận 3 file tải lên.
     - `GET /api/audit/sessions/{id}`: Lấy kết quả chi tiết phiên đối soát và danh sách cảnh báo đỏ (Red Flags).
     - `POST /api/audit/sessions/{id}/generate-dispute-email`: Tự động soạn văn bản phản hồi gửi nhà cung cấp dựa trên các lỗi bắt được.
     - `SignalR Hub (/hubs/audit-progress)`: Bắn tiến trình xử lý thời gian thực về giao diện (20% -> 50% -> 100%).

---

### GIAI ĐOẠN 3: PHÁT TRIỂN FRONTEND B2B WEB DASHBOARD (REACT 18 + VITE) (3 NGÀY)
* **Thư mục:** `client/`
* **Công nghệ:** React 18, TypeScript, Tailwind CSS, Lucide Icons, PDF.js.
* **Các màn hình & Component chính:**
  1. **Màn hình Tiếp nhận tài liệu (Ingestion Screen):**
     - 3 ô kéo thả (Dropzone) chuyên dụng cho: `Đơn đặt hàng (PO)`, `Phiếu giao kho (GRN)`, `Hóa đơn (Invoice)`.
     - Nút *"Bắt đầu Đối soát Tự động"* kèm thanh tiến trình SignalR chạy mượt mà.
  2. **Màn hình Không gian Kiểm toán (Audit Workspace - Split Screen):**
     - **Cột trái (Document Viewer):** Sử dụng `pdfjs-dist` hiển thị file hóa đơn/phiếu kho gốc, có khả năng zoom và highlight.
     - **Cột phải (3-Way Comparison Table):** Bảng so sánh 3 cột:
       + Cột 1: Mặt hàng & Số lượng đặt (PO).
       + Cột 2: Số lượng thực nhập kho (GRN).
       + Cột 3: Số lượng & Đơn giá ghi trên Hóa đơn.
       + Trạng thái: Badge Xanh lá (`Matched 100%`), Badge Đỏ (`Lệch giá +15%` hoặc `Thiếu 15 sản phẩm`).
  3. **Component Cảnh báo gian lận (Red Flag Summary Banner):**
     - Hộp cảnh báo nổi bật ở đầu trang: Tóm tắt tổng số tiền chênh lệch (Ví dụ: *"Phát hiện chênh lệch thất thoát: 4.850.000 VNĐ"*).
  4. **Modal Soạn thảo Khiếu nại (One-Click Dispute Resolver):**
     - Mở popup mẫu email đã điền sẵn số liệu sai lệch, cho phép chỉnh sửa nhanh và bấm nút "Sao chép nội dung" hoặc "Gửi đối tác".

---

### GIAI ĐOẠN 4: CHUẨN BỊ KỊCH BẢN DEMO SÂN KHẤU & THUYẾT TRÌNH (2 NGÀY)
* **Mục tiêu:** Tối ưu hóa trải nghiệm thuyết trình 5 phút trước Ban giám khảo.
* **Các hạng mục cần hoàn thành:**
  1. **Kịch bản Demo thực chiến (Live Pitch Demo):**
     - Kéo thả trực tiếp file Kịch bản 2 (Bị tăng đơn giá ngầm) và Kịch bản 3 (Giao thiếu hàng).
     - Cho giám khảo thấy AI xử lý trong **dưới 4 giây** và bật cờ đỏ cảnh báo ngay tức thì.
     - Bấm nút sinh email khiếu nại để minh chứng tính năng tự động hóa khép kín.
  2. **Video Demo dự phòng (Backup Screen Recording):**
     - Quay sẵn 1 video 60 giây chất lượng cao các bước demo mượt mà, phòng ngừa sự cố máy tính tại hội trường.
  3. **Hoàn thiện Slide Thuyết trình (10 Slide chuẩn LikeLion):**
     - Đầy đủ 10 phần từ Problem, Target Business, Solution, AI Mechanism, Demo, Feasibility đến Competitive Advantage (Nhấn mạnh thế mạnh: 100% On-Premise & Tiết kiệm 95% thời gian).

---

## 📌 BẢNG THEO DÕI TIẾN ĐỘ (CHECKLIST)

| Hạng mục | Công việc cụ thể | Trạng thái |
| :--- | :--- | :---: |
| **Hạ tầng** | Cấu hình Docker Postgres 16 + pgvector | ⏳ Sẵn sàng |
| **Hạ tầng** | Cài đặt Ollama & tải model Qwen 2.5:7B | ⏳ Sẵn sàng |
| **AI Service** | Viết module OCR tiếng Việt (PaddleOCR) | ⏳ Sẵn sàng |
| **AI Service** | Viết module bóc tách JSON bằng Qwen 2.5 | ⏳ Sẵn sàng |
| **AI Service** | Viết module Semantic Matching (bge-m3) | ⏳ Sẵn sàng |
| **Backend** | Thiết lập Clean Architecture .NET 8 Web API | ⏳ Sẵn sàng |
| **Backend** | Xây dựng Verification Logic 3 chiều | ⏳ Sẵn sàng |
| **Backend** | Tích hợp SignalR cập nhật tiến trình | ⏳ Sẵn sàng |
| **Frontend** | Xây dựng giao diện Upload 3 file | ⏳ Sẵn sàng |
| **Frontend** | Xây dựng bảng Split-Screen & Audit Table | ⏳ Sẵn sàng |
| **Dữ liệu** | Chuẩn bị 4 kịch bản chứng từ mẫu | ⏳ Sẵn sàng |
| **Pitching** | Chuẩn bị Slide 10 trang & Video demo | ⏳ Sẵn sàng |
