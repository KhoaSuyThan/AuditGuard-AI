# -*- coding: utf-8 -*-
"""
AuditGuard-AI - Trình Khởi Động Trung Tâm AI Engine
Tự động cài đặt thư viện cần thiết, kích hoạt dịch vụ Ollama nền và khởi chạy FastAPI
"""
import os
import sys
import time
import json
import subprocess
import urllib.request
import urllib.error

OLLAMA_URL = "http://localhost:11434"
REQUIRED_MODEL = "qwen2.5:3b"

# Danh sách đầy đủ các module cần thiết tương ứng ai-engine/requirements.txt
REQUIRED_MODULES = [
    ("fastapi", "fastapi"),
    ("uvicorn", "uvicorn"),
    ("pydantic", "pydantic"),
    ("multipart", "python-multipart"),
    ("httpx", "httpx"),
    ("pdfplumber", "pdfplumber"),
    ("pypdf", "pypdf"),
    ("pypdfium2", "pypdfium2"),
    ("PIL", "pillow"),
    ("scipy", "scipy"),
    ("sentence_transformers", "sentence-transformers"),
    ("numpy", "numpy")
]

def ensure_dependencies():
    """Tự động kiểm tra toàn diện tất cả các thư viện cần thiết"""
    missing = []
    for mod_name, pkg_name in REQUIRED_MODULES:
        try:
            __import__(mod_name)
        except ImportError:
            missing.append(pkg_name)

    if missing:
        print(f"[0/2] Phát hiện thiếu các thư viện: {', '.join(missing)}")
        print(" -> Đang tự động cài đặt từ ai-engine/requirements.txt...")
        base_dir = os.path.dirname(os.path.abspath(__file__))
        req_file = os.path.join(base_dir, "ai-engine", "requirements.txt")
        try:
            subprocess.run([sys.executable, "-m", "pip", "install", "-r", req_file], check=True)
            print(" -> Cài đặt thư viện AI Engine thành công!")
        except Exception as e:
            print(f" [!] Không thể tự cài đặt thư viện: {e}")
            print(f" [*] Vui lòng chạy thủ công: pip install -r ai-engine/requirements.txt")
            sys.exit(1)
    else:
        print("[0/2] Toàn bộ thư viện phụ thuộc AI Engine đã sẵn sàng.")

def ensure_ollama_running():
    """Tự động kiểm tra và khởi chạy Ollama nếu chưa chạy (Dùng thư viện chuẩn urllib)"""
    print("[1/2] Đang kiểm tra trạng thái dịch vụ Ollama...")
    try:
        req = urllib.request.Request(f"{OLLAMA_URL}/api/tags")
        with urllib.request.urlopen(req, timeout=3.0) as resp:
            if resp.status == 200:
                print(" -> Dịch vụ Ollama đã sẵn sàng.")
                tags_data = json.loads(resp.read().decode("utf-8"))
                check_and_pull_model(tags_data)
                return
    except Exception:
        print(" -> Chưa phát hiện Ollama. Đang tự động khởi chạy 'ollama serve'...")

    # Khởi chạy tiến trình ngầm
    try:
        if sys.platform == "win32":
            subprocess.Popen(
                ["ollama", "serve"],
                creationflags=subprocess.CREATE_NEW_PROCESS_GROUP | subprocess.DETACHED_PROCESS,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL
            )
        else:
            subprocess.Popen(
                ["ollama", "serve"],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                start_new_session=True
            )

        # Chờ tối đa 10 giây để Ollama sẵn sàng
        for i in range(10):
            time.sleep(1.0)
            try:
                req = urllib.request.Request(f"{OLLAMA_URL}/api/tags")
                with urllib.request.urlopen(req, timeout=2.0) as resp:
                    if resp.status == 200:
                        print(" -> Đã khởi động Ollama thành công!")
                        tags_data = json.loads(resp.read().decode("utf-8"))
                        check_and_pull_model(tags_data)
                        return
            except Exception:
                pass
        print(" [!] Cảnh báo: Ollama đang khởi động chậm. Bạn có thể kiểm tra lại tiến trình nền.")
    except Exception as e:
        print(f" [X] Không thể tự khởi chạy Ollama: {e}. Vui lòng chạy thủ công 'ollama serve'.")

def check_and_pull_model(tags_data):
    """Kiểm tra chính xác tag REQUIRED_MODEL (vd: 'qwen2.5:3b'), tránh nhầm với các bản khác"""
    installed_models = set()
    for m in tags_data.get("models", []):
        name = m.get("name", "")
        if name:
            installed_models.add(name.lower())
        model_tag = m.get("model", "")
        if model_tag:
            installed_models.add(model_tag.lower())

    target = REQUIRED_MODEL.lower()
    # Kiểm tra xem tag chính xác đã được cài đặt chưa (ví dụ: 'qwen2.5:3b' hoặc 'qwen2.5:3b-instruct')
    has_exact_model = any(target == m or m.startswith(target) for m in installed_models)

    if not has_exact_model:
        print(f" -> Chưa tìm thấy tag mô hình chính xác '{REQUIRED_MODEL}'. Đang tự động tải (Lần đầu tiên)...")
        try:
            subprocess.run(["ollama", "pull", REQUIRED_MODEL], check=True)
            print(f" -> Tải thành công mô hình '{REQUIRED_MODEL}'!")
        except Exception as e:
            print(f" [!] Không thể tự tải mô hình: {e}")
            print(f" [*] Vui lòng chạy thủ công: ollama pull {REQUIRED_MODEL}")
    else:
        print(f" -> Mô hình AI chính xác '{REQUIRED_MODEL}' đã sẵn sàng.")

if __name__ == "__main__":
    ensure_dependencies()
    ensure_ollama_running()

    import uvicorn
    base_dir = os.path.dirname(os.path.abspath(__file__))
    ai_dir = os.path.join(base_dir, "ai-engine")
    if ai_dir not in sys.path:
        sys.path.insert(0, ai_dir)

    os.chdir(ai_dir)
    print("[2/2] Đang khởi chạy AI Engine FastAPI Microservice tại http://localhost:8000 ...")
    uvicorn.run("main:app", host="0.0.0.0", port=8000, reload=False)
