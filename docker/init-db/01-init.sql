-- Kích hoạt extension pgvector và uuid
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

-- 1. Bảng Nhà cung cấp (Vendors)
CREATE TABLE IF NOT EXISTS vendors (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    tax_code VARCHAR(50) UNIQUE NOT NULL,
    name VARCHAR(255) NOT NULL,
    address TEXT,
    contact_email VARCHAR(100),
    risk_level VARCHAR(20) DEFAULT 'LOW', -- LOW, MEDIUM, HIGH (Blacklist)
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- 2. Bảng Đơn đặt hàng (Purchase Orders - PO)
CREATE TABLE IF NOT EXISTS purchase_orders (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    po_number VARCHAR(100) UNIQUE NOT NULL,
    vendor_id UUID REFERENCES vendors(id) ON DELETE SET NULL,
    order_date DATE NOT NULL,
    currency VARCHAR(10) DEFAULT 'VND',
    subtotal DECIMAL(18, 2) NOT NULL,
    tax_amount DECIMAL(18, 2) DEFAULT 0,
    total_amount DECIMAL(18, 2) NOT NULL,
    status VARCHAR(50) DEFAULT 'APPROVED', -- DRAFT, APPROVED, COMPLETED
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS po_items (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    po_id UUID REFERENCES purchase_orders(id) ON DELETE CASCADE,
    line_number INT NOT NULL,
    item_code VARCHAR(100),
    item_name VARCHAR(255) NOT NULL,
    quantity DECIMAL(12, 3) NOT NULL,
    unit VARCHAR(50) DEFAULT 'cái',
    unit_price DECIMAL(18, 2) NOT NULL,
    total_price DECIMAL(18, 2) NOT NULL,
    embedding vector(1024) -- Vector embedding của item_name (bge-m3)
);

-- 3. Bảng Phiếu nhập kho (Goods Receipts - GRN)
CREATE TABLE IF NOT EXISTS goods_receipts (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    grn_number VARCHAR(100) UNIQUE NOT NULL,
    po_id UUID REFERENCES purchase_orders(id) ON DELETE SET NULL,
    received_date DATE NOT NULL,
    warehouse_keeper VARCHAR(100),
    note TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS gr_items (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    gr_id UUID REFERENCES goods_receipts(id) ON DELETE CASCADE,
    item_name VARCHAR(255) NOT NULL,
    quantity_received DECIMAL(12, 3) NOT NULL,
    unit VARCHAR(50) DEFAULT 'cái',
    condition_status VARCHAR(50) DEFAULT 'GOOD' -- GOOD, DAMAGED, REJECTED
);

-- 4. Bảng Hóa đơn VAT (Invoices)
-- Lưu ý: Không dùng UNIQUE cứng trên (invoice_number, vendor_tax_code) để cho phép
-- hệ thống nạp và lưu vết các hóa đơn trùng lặp, phục vụ tạo hồ sơ kiểm toán (Audit Trail)
CREATE TABLE IF NOT EXISTS invoices (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    invoice_number VARCHAR(100) NOT NULL,
    invoice_series VARCHAR(50), -- Ký hiệu mẫu hóa đơn (1C26TBB...)
    vendor_tax_code VARCHAR(50) NOT NULL,
    vendor_name VARCHAR(255),
    invoice_date DATE NOT NULL,
    subtotal DECIMAL(18, 2) NOT NULL,
    tax_rate DECIMAL(5, 2) DEFAULT 8.00, -- 8%, 10%
    tax_amount DECIMAL(18, 2) NOT NULL,
    total_payable DECIMAL(18, 2) NOT NULL,
    file_path TEXT,
    raw_ocr_json JSONB,
    payment_status VARCHAR(50) DEFAULT 'UNPAID', -- UNPAID, PAID, CANCELLED
    is_duplicate BOOLEAN DEFAULT FALSE,
    original_invoice_id UUID REFERENCES invoices(id) ON DELETE SET NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS invoice_items (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    invoice_id UUID REFERENCES invoices(id) ON DELETE CASCADE,
    line_number INT NOT NULL,
    item_name VARCHAR(255) NOT NULL,
    quantity DECIMAL(12, 3) NOT NULL,
    unit VARCHAR(50) DEFAULT 'cái',
    unit_price DECIMAL(18, 2) NOT NULL,
    total_amount DECIMAL(18, 2) NOT NULL,
    embedding vector(1024) -- Vector embedding của item_name
);

-- 5. Bảng Phiên kiểm toán đối soát 3 chiều (Audit Sessions)
CREATE TABLE IF NOT EXISTS audit_sessions (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    session_code VARCHAR(100) UNIQUE NOT NULL,
    po_id UUID REFERENCES purchase_orders(id),
    grn_id UUID REFERENCES goods_receipts(id),
    invoice_id UUID REFERENCES invoices(id),
    status VARCHAR(50) DEFAULT 'PENDING', -- PENDING, PASSED, FLAGGED, REJECTED
    discrepancy_count INT DEFAULT 0,
    total_financial_diff DECIMAL(18, 2) DEFAULT 0,
    notes TEXT,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- 6. Bảng Chi tiết sai lệch (Audit Discrepancies)
CREATE TABLE IF NOT EXISTS audit_discrepancies (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    session_id UUID REFERENCES audit_sessions(id) ON DELETE CASCADE,
    discrepancy_type VARCHAR(50) NOT NULL, -- PRICE_MISMATCH, QTY_MISMATCH, DUPLICATE_INVOICE, TAX_ERROR, TIMING_ANOMALY
    severity VARCHAR(20) DEFAULT 'CRITICAL', -- INFO, WARNING, CRITICAL
    item_name VARCHAR(255),
    expected_value VARCHAR(100), -- Ví dụ: Đơn giá PO = 100,000
    actual_value VARCHAR(100),   -- Ví dụ: Đơn giá Hóa đơn = 115,000
    financial_impact DECIMAL(18, 2) DEFAULT 0, -- Số tiền chênh lệch
    description TEXT NOT NULL,
    is_resolved BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP
);

-- Chỉ mục hỗ trợ tra cứu trùng lặp và tìm kiếm nhanh
CREATE INDEX IF NOT EXISTS idx_invoices_lookup ON invoices (vendor_tax_code, invoice_number);
CREATE INDEX IF NOT EXISTS idx_invoices_payment_status ON invoices (payment_status);

-- Tạo Index tìm kiếm Vector HNSW cho tốc độ siêu nhanh (Cosine Similarity)
CREATE INDEX IF NOT EXISTS idx_po_items_embedding ON po_items USING hnsw (embedding vector_cosine_ops);
CREATE INDEX IF NOT EXISTS idx_invoice_items_embedding ON invoice_items USING hnsw (embedding vector_cosine_ops);
