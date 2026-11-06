# -*- coding: utf-8 -*-
"""
OCR & Document Text Extractor Service
Hỗ trợ:
- PDF điện tử (Digital PDF với text layer)
- PDF scan (Scanned PDF không có text layer -> tự động render trang thành ảnh để OCR)
- File ảnh trực tiếp: JPG, JPEG, PNG, WEBP, BMP
"""
import io
from typing import Optional
from PIL import Image


class OCRService:
    @staticmethod
    def _ocr_image_bytes(image_bytes: bytes) -> str:
        """
        Nhận diện văn bản từ ảnh bằng PaddleOCR hoặc Tesseract
        Có cơ chế Fallback tuần tự an toàn
        """
        # 1. Ưu tiên PaddleOCR nếu có
        try:
            from paddleocr import PaddleOCR
            ocr = PaddleOCR(use_angle_cls=True, lang="vi", show_log=False)
            image = Image.open(io.BytesIO(image_bytes)).convert("RGB")
            import numpy as np
            img_np = np.array(image)
            result = ocr.ocr(img_np, cls=True)
            lines = []
            if result and result[0]:
                for line in result[0]:
                    text = line[1][0]
                    lines.append(text)
            text_res = "\n".join(lines).strip()
            if text_res:
                return text_res
        except Exception:
            pass

        # 2. Thử Tesseract nếu có
        try:
            import pytesseract
            image = Image.open(io.BytesIO(image_bytes))
            text = pytesseract.image_to_string(image, lang="vie+eng")
            if text.strip():
                return text.strip()
        except Exception:
            pass

        raise RuntimeError(
            "Tệp ảnh hoặc PDF Scan yêu cầu bộ cài OCR (PaddleOCR hoặc Tesseract). "
            "Vui lòng cài đặt 'paddleocr' hoặc tải Tesseract OCR cho hệ thống."
        )

    @classmethod
    def extract_text_from_pdf(cls, file_bytes: bytes) -> str:
        """
        Trích xuất văn bản từ PDF.
        Nếu phát hiện PDF Scan (rỗng hoặc < 30 ký tự) -> Tự động chuyển trang thành ảnh để OCR.
        """
        import pdfplumber

        extracted_text = []
        is_scanned_pdf = True

        try:
            with pdfplumber.open(io.BytesIO(file_bytes)) as pdf:
                for page_idx, page in enumerate(pdf.pages):
                    page_text = page.extract_text() or ""
                    tables = page.extract_tables()
                    table_str = ""
                    if tables:
                        table_rows = []
                        for table in tables:
                            for row in table:
                                clean_row = [str(cell).strip() if cell else "" for cell in row]
                                table_rows.append(" | ".join(clean_row))
                        table_str = "\n--- BẢNG BIỂU ---\n" + "\n".join(table_rows)

                    if len(page_text.strip()) > 30:
                        is_scanned_pdf = False

                    full_page = f"--- TRANG {page_idx + 1} ---\n{page_text}\n{table_str}".strip()
                    extracted_text.append(full_page)

            final_text = "\n".join(extracted_text).strip()
            if not is_scanned_pdf and len(final_text) > 30:
                return final_text
        except Exception:
            is_scanned_pdf = True

        # Xử lý PDF Scan: Render trang PDF thành ảnh rồi OCR
        try:
            import pypdfium2 as pdfium
            pdf = pdfium.PdfDocument(file_bytes)
            ocr_pages = []
            for i in range(len(pdf)):
                page = pdf[i]
                image = page.render(scale=2.0).to_pil()
                img_byte_arr = io.BytesIO()
                image.save(img_byte_arr, format="PNG")
                page_ocr_text = cls._ocr_image_bytes(img_byte_arr.getvalue())
                ocr_pages.append(f"--- TRANG SCAN {i + 1} ---\n{page_ocr_text}")
            return "\n".join(ocr_pages).strip()
        except Exception as e:
            if not is_scanned_pdf and final_text:
                return final_text
            raise ValueError(f"Không thể OCR PDF scan: {str(e)}")

    @classmethod
    def extract_from_file(cls, filename: str, file_bytes: bytes) -> str:
        """Nhận diện định dạng tệp (PDF, JPG, PNG, WEBP...) và trích xuất nội dung"""
        ext = filename.lower().split(".")[-1]
        
        if ext == "pdf":
            return cls.extract_text_from_pdf(file_bytes)
        elif ext in ["jpg", "jpeg", "png", "webp", "bmp"]:
            return cls._ocr_image_bytes(file_bytes)
        elif ext in ["txt", "json", "csv"]:
            return file_bytes.decode("utf-8", errors="ignore")
        else:
            raise ValueError(f"Định dạng .{ext} không được hỗ trợ. Vui lòng tải lên PDF hoặc Ảnh (JPG/PNG).")
