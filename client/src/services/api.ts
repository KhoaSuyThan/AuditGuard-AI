import { AuditSessionDto, DisputeEmailDto, ScenarioInfoDto } from '../types/audit';

const API_BASE = '/api/audit';

export async function fetchScenarios(): Promise<ScenarioInfoDto[]> {
  const res = await fetch(`${API_BASE}/scenarios`);
  if (!res.ok) throw new Error('Không thể tải danh sách kịch bản mẫu.');
  return res.json();
}

export async function runScenarioAsync(folderName: string, clientSessionId: string): Promise<{ sessionId: string; status: string; message: string }> {
  const res = await fetch(`${API_BASE}/scenarios/${folderName}/run-async?clientSessionId=${clientSessionId}`, {
    method: 'POST'
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    throw new Error(err.detail || 'Lỗi khi bắt đầu chạy kịch bản nền.');
  }
  return res.json();
}

export async function uploadAndAuditAsync(
  poFile: File,
  grnFile: File,
  invoiceFile: File,
  clientSessionId: string
): Promise<{ sessionId: string; status: string; message: string }> {
  const formData = new FormData();
  formData.append('poFile', poFile);
  formData.append('grnFile', grnFile);
  formData.append('invoiceFile', invoiceFile);

  const res = await fetch(`${API_BASE}/sessions/upload-async?clientSessionId=${clientSessionId}`, {
    method: 'POST',
    body: formData
  });

  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    throw new Error(err.detail || err.title || 'Lỗi khi tải lên chứng từ.');
  }
  return res.json();
}

export async function getSession(sessionId: string): Promise<AuditSessionDto> {
  const res = await fetch(`${API_BASE}/sessions/${sessionId}`);
  if (!res.ok) throw new Error('Không tìm thấy thông tin phiên đối soát.');
  const dbData: AuditSessionDto = await res.json();

  // Khôi phục comparisonRows từ localStorage nếu DB chỉ lưu discrepancies
  const localCached = localStorage.getItem(`audit_session_${sessionId}`);
  if (localCached) {
    try {
      const parsed = JSON.parse(localCached);
      if (parsed.comparisonRows && (!dbData.comparisonRows || dbData.comparisonRows.length === 0)) {
        dbData.comparisonRows = parsed.comparisonRows;
      }
    } catch {}
  }

  return dbData;
}

export async function generateDisputeEmail(sessionId: string): Promise<DisputeEmailDto> {
  const res = await fetch(`${API_BASE}/sessions/${sessionId}/email`, {
    method: 'POST'
  });
  if (!res.ok) throw new Error('Không thể sinh email khiếu nại tự động.');
  return res.json();
}
