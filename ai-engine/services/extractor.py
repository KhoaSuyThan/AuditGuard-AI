# -*- coding: utf-8 -*-
"""
Structural Document Extractor Service (Local LLM via Ollama)
Bảo đảm 100% dữ liệu trả về đều được validate qua Pydantic schema
"""
import json
import re
import httpx
from typing import Dict, Any, Optional
from pydantic import BaseModel, Field, ValidationError


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
    items: list[InvoiceItemSchema] = Field(default_factory=list, description="Danh sách các mặt hàng")
    subtotal: float = Field(default=0.0, description="Tổng tiền trước thuế")
    tax_rate: float = Field(default=8.0, description="Thuế suất GTGT (%)")
    tax_amount: float = Field(default=0.0, description="Tiền thuế GTGT")
    total_payable: float = Field(default=0.0, description="Tổng tiền thanh toán")


class DocumentExtractor:
    def __init__(self, ollama_url: str = "http://localhost:11434", model_name: str = "qwen2.5:3b"):
        self.ollama_url = ollama_url.rstrip("/")
        self.model_name = model_name

    def _validate_and_dump(self, raw_dict: Dict[str, Any]) -> Dict[str, Any]:
        """Bắt buộc mọi dữ liệu JSON phải qua chuẩn hóa Pydantic"""
        try:
            validated = ExtractedInvoiceSchema(**raw_dict)
            return validated.model_dump()
        except ValidationError as ve:
            # Tự động gán fallback an toàn nếu có trường lỗi kiểu dữ liệu
            clean_dict = {
                "invoice_number": str(raw_dict.get("invoice_number", "")),
                "invoice_series": str(raw_dict.get("invoice_series", "")),
                "invoice_date": str(raw_dict.get("invoice_date", "")),
                "vendor_name": str(raw_dict.get("vendor_name", "")),
                "vendor_tax_code": str(raw_dict.get("vendor_tax_code", "")),
                "items": raw_dict.get("items", []),
                "subtotal": float(raw_dict.get("subtotal", 0.0) or 0.0),
                "tax_rate": float(raw_dict.get("tax_rate", 8.0) or 8.0),
                "tax_amount": float(raw_dict.get("tax_amount", 0.0) or 0.0),
                "total_payable": float(raw_dict.get("total_payable", 0.0) or 0.0),
            }
            return ExtractedInvoiceSchema(**clean_dict).model_dump()

    async def extract_invoice_data(self, raw_text: str) -> Dict[str, Any]:
        """Gửi prompt đến Ollama Qwen 2.5 và xác thực Pydantic"""
        system_prompt = (
            "Bạn là trợ lý AI chuyên gia bóc tách hóa đơn tài chính và chứng từ kế toán Việt Nam. "
            "Nhiệm vụ của bạn là đọc văn bản hóa đơn được cung cấp và trích xuất thành định dạng JSON chuẩn. "
            "QUY TẮC BẮT BUỘC:\n"
            "1. Chỉ trả về duy nhất 1 khối JSON hợp lệ, không giải thích thêm bất kỳ lời văn nào.\n"
            "2. Định dạng ngày tháng phải chuẩn 'YYYY-MM-DD'.\n"
            "3. Số tiền và số lượng phải là dạng số nguyên hoặc số thực.\n"
            "4. Cấu trúc JSON bắt buộc phải tuân theo mẫu:\n"
            "{\n"
            '  "invoice_number": "string",\n'
            '  "invoice_series": "string",\n'
            '  "invoice_date": "YYYY-MM-DD",\n'
            '  "vendor_name": "string",\n'
            '  "vendor_tax_code": "string",\n'
            '  "items": [\n'
            '    {\n'
            '      "line_number": 1,\n'
            '      "item_name": "string",\n'
            '      "quantity": 1.0,\n'
            '      "unit": "string",\n'
            '      "unit_price": 0.0,\n'
            '      "total_amount": 0.0\n'
            "    }\n"
            "  ],\n"
            '  "subtotal": 0.0,\n'
            '  "tax_rate": 8.0,\n'
            '  "tax_amount": 0.0,\n'
            '  "total_payable": 0.0\n'
            "}"
        )

        payload = {
            "model": self.model_name,
            "system": system_prompt,
            "prompt": f"Nội dung hóa đơn:\n{raw_text}\nTrích xuất JSON:",
            "stream": False,
            "format": "json",
            "options": {"temperature": 0.0, "top_p": 0.1}
        }

        async with httpx.AsyncClient(timeout=60.0) as client:
            response = await client.post(f"{self.ollama_url}/api/generate", json=payload)
            response.raise_for_status()
            data = response.json()
            response_text = data.get("response", "").strip()

            clean_json_str = re.sub(r"^```json\s*|\s*```$", "", response_text, flags=re.MULTILINE).strip()

            try:
                parsed_json = json.loads(clean_json_str)
                return self._validate_and_dump(parsed_json)
            except json.JSONDecodeError:
                # Fallback regex tìm khối json
                match = re.search(r"\{.*\}", response_text, re.DOTALL)
                if match:
                    parsed_json = json.loads(match.group(0))
                    # Vẫn bắt buộc validate qua Pydantic schema!
                    return self._validate_and_dump(parsed_json)
                raise ValueError("Không thể trích xuất cấu trúc JSON từ phản hồi của mô hình AI.")
