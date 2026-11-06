# -*- coding: utf-8 -*-
"""
Structural Document Extractor Service (Local LLM via Ollama)
Hỗ trợ bóc tách chuyên biệt theo từng loại chứng từ: PO, GRN (Phiếu kho), và Hóa đơn VAT
"""
import json
import re
import httpx
from typing import Dict, Any, Optional, List
from pydantic import BaseModel, Field, ValidationError


# 1. Schemas cho Hóa đơn (Invoice)
class InvoiceItemSchema(BaseModel):
    line_number: int = Field(default=1, description="Số thứ tự dòng hàng")
    item_name: str = Field(description="Tên sản phẩm hoặc dịch vụ")
    quantity: float = Field(default=1.0, description="Số lượng")
    unit: str = Field(default="cái", description="Đơn vị tính")
    unit_price: float = Field(default=0.0, description="Đơn giá trước thuế")
    total_amount: float = Field(default=0.0, description="Thành tiền trước thuế")


class ExtractedInvoiceSchema(BaseModel):
    invoice_number: str = Field(default="", description="Số hóa đơn")
    invoice_series: Optional[str] = Field(default="", description="Ký hiệu hóa đơn")
    invoice_date: str = Field(default="", description="Ngày xuất hóa đơn (YYYY-MM-DD)")
    vendor_name: str = Field(default="", description="Tên đơn vị bán hàng")
    vendor_tax_code: str = Field(default="", description="Mã số thuế bên bán")
    items: List[InvoiceItemSchema] = Field(default_factory=list, description="Danh sách các mặt hàng")
    subtotal: float = Field(default=0.0, description="Tổng tiền trước thuế")
    tax_rate: float = Field(default=8.0, description="Thuế suất GTGT (%)")
    tax_amount: float = Field(default=0.0, description="Tiền thuế GTGT")
    total_payable: float = Field(default=0.0, description="Tổng tiền thanh toán")


# 2. Schemas cho Đơn đặt hàng (Purchase Order - PO)
class POItemSchema(BaseModel):
    line_number: int = Field(default=1, description="Số thứ tự dòng hàng")
    item_name: str = Field(description="Tên sản phẩm yêu cầu đặt hàng")
    quantity: float = Field(default=1.0, description="Số lượng đặt")
    unit: str = Field(default="cái", description="Đơn vị tính")
    unit_price: float = Field(default=0.0, description="Đơn giá đặt")
    total_amount: float = Field(default=0.0, description="Thành tiền")


class ExtractedPOSchema(BaseModel):
    po_number: str = Field(default="", description="Mã đơn đặt hàng (PO Number)")
    order_date: str = Field(default="", description="Ngày lập đơn hàng (YYYY-MM-DD)")
    vendor_name: str = Field(default="", description="Tên nhà cung cấp")
    total_amount: float = Field(default=0.0, description="Tổng giá trị đơn hàng")
    items: List[POItemSchema] = Field(default_factory=list, description="Danh sách các mặt hàng đặt")


# 3. Schemas cho Phiếu nhập kho (Goods Receipt Note - GRN)
class GRItemSchema(BaseModel):
    line_number: int = Field(default=1, description="Số thứ tự dòng hàng")
    item_name: str = Field(description="Tên vật tư hàng hóa thực nhận")
    quantity_received: float = Field(default=0.0, description="Số lượng thực nhập kho")
    unit: str = Field(default="cái", description="Đơn vị tính")


class ExtractedGRNSchema(BaseModel):
    grn_number: str = Field(default="", description="Số phiếu nhập kho")
    received_date: str = Field(default="", description="Ngày nhập kho (YYYY-MM-DD)")
    warehouse_keeper: str = Field(default="", description="Thủ kho nhận hàng")
    items: List[GRItemSchema] = Field(default_factory=list, description="Danh sách hàng hóa thực tế nhập kho")


class DocumentExtractor:
    def __init__(self, ollama_url: str = "http://localhost:11434", model_name: str = "qwen2.5:3b"):
        self.ollama_url = ollama_url.rstrip("/")
        self.model_name = model_name

    def _build_prompt(self, raw_text: str, document_type: str) -> str:
        doc_type_upper = document_type.upper()
        if doc_type_upper == "PO":
            return f"""Bạn là trợ lý AI chuyên bóc tách thông tin Đơn đặt hàng (Purchase Order - PO) của doanh nghiệp Việt Nam.
Nhiệm vụ: Phân tích kỹ nội dung văn bản sau và trích xuất thành định dạng JSON chuẩn.

Yêu cầu định dạng JSON CHÍNH XÁC như sau (không thêm văn bản giải thích):
{{
  "po_number": "mã đơn hàng (vd: PO-2026-001)",
  "order_date": "ngày đặt hàng (định dạng YYYY-MM-DD)",
  "vendor_name": "tên nhà cung cấp",
  "total_amount": 0.0,
  "items": [
    {{
      "line_number": 1,
      "item_name": "tên mặt hàng đặt",
      "quantity": 10.0,
      "unit": "cái",
      "unit_price": 50000.0,
      "total_amount": 500000.0
    }}
  ]
}}

Nội dung Đơn đặt hàng:
--- NỘI DUNG VĂN BẢN ---
{raw_text}
--- HẾT NỘI DUNG ---
"""
        elif doc_type_upper == "GRN":
            return f"""Bạn là trợ lý AI chuyên bóc tách thông tin Phiếu nhập kho (Goods Receipt Note - GRN) tại kho bãi Việt Nam.
Nhiệm vụ: Phân tích kỹ nội dung văn bản sau và trích xuất thành định dạng JSON chuẩn.

Yêu cầu định dạng JSON CHÍNH XÁC như sau (không thêm văn bản giải thích):
{{
  "grn_number": "số phiếu nhập kho (vd: GRN-2026-001)",
  "received_date": "ngày nhập kho (định dạng YYYY-MM-DD)",
  "warehouse_keeper": "tên thủ kho hoặc người nhận",
  "items": [
    {{
      "line_number": 1,
      "item_name": "tên vật tư thực tế nhập kho",
      "quantity_received": 10.0,
      "unit": "cái"
    }}
  ]
}}

Nội dung Phiếu nhập kho:
--- NỘI DUNG VĂN BẢN ---
{raw_text}
--- HẾT NỘI DUNG ---
"""
        else:
            return f"""Bạn là trợ lý AI kế toán chuyên bóc tách thông tin Hóa đơn giá trị gia tăng (VAT Invoice) của Việt Nam.
Nhiệm vụ: Phân tích kỹ nội dung văn bản sau và trích xuất thành định dạng JSON chuẩn.

Yêu cầu định dạng JSON CHÍNH XÁC như sau (không thêm văn bản giải thích):
{{
  "invoice_number": "số hóa đơn",
  "invoice_series": "ký hiệu mẫu hóa đơn (vd: 1C26TBB)",
  "invoice_date": "ngày xuất hóa đơn (định dạng YYYY-MM-DD)",
  "vendor_name": "tên đơn vị bán hàng",
  "vendor_tax_code": "mã số thuế người bán (10 hoặc 13 chữ số)",
  "items": [
    {{
      "line_number": 1,
      "item_name": "tên sản phẩm/dịch vụ",
      "quantity": 10.0,
      "unit": "cái",
      "unit_price": 50000.0,
      "total_amount": 500000.0
    }}
  ],
  "subtotal": 0.0,
  "tax_rate": 8.0,
  "tax_amount": 0.0,
  "total_payable": 0.0
}}

Nội dung Hóa đơn VAT:
--- NỘI DUNG VĂN BẢN ---
{raw_text}
--- HẾT NỘI DUNG ---
"""

    async def extract_document_data(self, raw_text: str, document_type: str = "INVOICE") -> Dict[str, Any]:
        prompt = self._build_prompt(raw_text, document_type)
        payload = {
            "model": self.model_name,
            "prompt": prompt,
            "stream": False,
            "format": "json",
            "options": {
                "temperature": 0.0
            }
        }

        async with httpx.AsyncClient(timeout=60.0) as client:
            response = await client.post(f"{self.ollama_url}/api/generate", json=payload)
            response.raise_for_status()

            res_json = response.json()
            raw_response = res_json.get("response", "").strip()

            # Trích xuất khối json từ markdown nếu có
            json_match = re.search(r"```json\s*(.*?)\s*```", raw_response, re.DOTALL)
            if json_match:
                json_str = json_match.group(1).strip()
            else:
                json_str = raw_response

            parsed_data = json.loads(json_str)
            doc_type_upper = document_type.upper()

            if doc_type_upper == "PO":
                validated = ExtractedPOSchema(**parsed_data)
                return validated.model_dump()
            elif doc_type_upper == "GRN":
                validated = ExtractedGRNSchema(**parsed_data)
                return validated.model_dump()
            else:
                validated = ExtractedInvoiceSchema(**parsed_data)
                return validated.model_dump()

    # Giữ tương thích ngược với extract_invoice_data cũ
    async def extract_invoice_data(self, raw_text: str) -> Dict[str, Any]:
        return await self.extract_document_data(raw_text, "INVOICE")
