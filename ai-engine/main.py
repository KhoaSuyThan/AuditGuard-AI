# -*- coding: utf-8 -*-
"""
AuditGuard AI Engine - FastAPI Microservice
Đã chuẩn hóa Exception Handling không ghi đè lỗi 400/422/503 thành 500
"""
from fastapi import FastAPI, UploadFile, File, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
from typing import List, Dict, Any
import httpx

from services.ocr_service import OCRService
from services.extractor import DocumentExtractor
from services.matcher import SemanticMatcher

app = FastAPI(
    title="AuditGuard AI Engine",
    description="Microservice 100% Local: OCR (Digital + Scan + Image), Qwen 2.5 Extractor & Hungarian Semantic Matcher",
    version="1.1.0"
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

extractor = DocumentExtractor(ollama_url="http://localhost:11434", model_name="qwen2.5:3b")
matcher = SemanticMatcher()


class MatchItemsRequest(BaseModel):
    po_items: List[Dict[str, Any]]
    invoice_items: List[Dict[str, Any]]
    threshold: float = 0.50


@app.get("/api/ai/health")
async def health_check():
    ollama_status = "DISCONNECTED"
    available_models = []
    try:
        async with httpx.AsyncClient(timeout=3.0) as client:
            res = await client.get("http://localhost:11434/api/tags")
            if res.status_code == 200:
                ollama_status = "CONNECTED"
                data = res.json()
                available_models = [m["name"] for m in data.get("models", [])]
    except Exception:
        pass

    return {
        "status": "RUNNING",
        "service": "AuditGuard AI Engine (100% Local)",
        "ollama_connection": ollama_status,
        "active_model": extractor.model_name,
        "available_ollama_models": available_models
    }


@app.post("/api/ai/extract-document")
async def extract_document(file: UploadFile = File(...)):
    """
    Tiếp nhận file PDF (cả digital và scan) hoặc file ảnh (JPG, PNG):
    Bắt lỗi chuẩn xác:
    - 400: Định dạng không hỗ trợ hoặc file rỗng
    - 503: Không kết nối được Ollama
    - 504: Ollama timeout
    """
    try:
        content = await file.read()
        raw_text = OCRService.extract_from_file(file.filename, content)

        if not raw_text.strip():
            raise HTTPException(status_code=400, detail="Không thể trích xuất văn bản từ tệp tin đã cung cấp.")

        extracted_data = await extractor.extract_invoice_data(raw_text)
        return {
            "filename": file.filename,
            "raw_text_preview": raw_text[:500] + ("..." if len(raw_text) > 500 else ""),
            "data": extracted_data
        }
    except HTTPException:
        # Giữ nguyên HTTPException (400, 422...) không bị biến thành 500
        raise
    except httpx.ConnectError:
        raise HTTPException(
            status_code=503,
            detail="Không thể kết nối đến máy chủ Ollama tại http://localhost:11434. Vui lòng đảm bảo tiến trình 'ollama serve' đang chạy."
        )
    except httpx.TimeoutException:
        raise HTTPException(
            status_code=504,
            detail="Quá thời gian chờ phản hồi từ mô hình Ollama (Timeout > 60s)."
        )
    except ValueError as ve:
        raise HTTPException(status_code=400, detail=str(ve))
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Lỗi hệ thống: {str(e)}")


@app.post("/api/ai/match-items")
async def match_items(request: MatchItemsRequest):
    try:
        results = matcher.match_items_bipartite(
            po_items=request.po_items,
            invoice_items=request.invoice_items,
            similarity_threshold=request.threshold
        )
        return {
            "total_pairs": len(results),
            "matcher_engine": matcher.model_name,
            "matches": results
        }
    except HTTPException:
        raise
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Lỗi so khớp ngữ nghĩa: {str(e)}")


if __name__ == "__main__":
    import uvicorn
    uvicorn.run("main:app", host="0.0.0.0", port=8000, reload=True)
