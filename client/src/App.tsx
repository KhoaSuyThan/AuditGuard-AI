import { useState, useEffect } from 'react';
import { Navbar } from './components/Navbar';
import { ScenarioSelector } from './components/ScenarioSelector';
import { FileUploadZone } from './components/FileUploadZone';
import { SignalRProgressBar } from './components/SignalRProgressBar';
import { AuditSummaryCards } from './components/AuditSummaryCards';
import { ThreeWayMatchingTable } from './components/ThreeWayMatchingTable';
import { DiscrepancyList } from './components/DiscrepancyList';
import { DisputeEmailModal } from './components/DisputeEmailModal';
import { AuditSessionDto, DisputeEmailDto, ProgressState, ScenarioInfoDto } from './types/audit';
import { fetchScenarios, runScenarioAsync, uploadAndAuditAsync, generateDisputeEmail } from './services/api';
import { signalRService } from './services/signalr';
import { ShieldCheck, History } from 'lucide-react';

export default function App() {
  const [activeTab, setActiveTab] = useState<'scenarios' | 'upload'>('scenarios');
  const [signalRConnected, setSignalRConnected] = useState<boolean>(false);
  const [scenarios, setScenarios] = useState<ScenarioInfoDto[]>([]);
  const [selectedScenario, setSelectedScenario] = useState<string | null>(null);
  const [currentSession, setCurrentSession] = useState<AuditSessionDto | null>(null);
  const [progress, setProgress] = useState<ProgressState>({
    stage: '',
    percent: 0,
    message: '',
    isProcessing: false
  });

  const [emailModalOpen, setEmailModalOpen] = useState<boolean>(false);
  const [emailData, setEmailData] = useState<DisputeEmailDto | null>(null);
  const [isEmailLoading, setIsEmailLoading] = useState<boolean>(false);

  // Khởi tạo SignalR kết nối thời gian thực
  useEffect(() => {
    let mounted = true;

    async function initSignalR() {
      try {
        await signalRService.connect();
        if (mounted) setSignalRConnected(true);

        signalRService.onProgress((p) => {
          if (mounted) setProgress(p);
        });

        signalRService.onAuditCompleted((session) => {
          if (mounted) {
            setCurrentSession(session);
            setProgress({
              stage: 'Completed',
              percent: 100,
              message: 'Đối soát hoàn tất thành công!',
              isProcessing: false
            });
          }
        });
      } catch {
        if (mounted) setSignalRConnected(false);
      }
    }

    initSignalR();

    // Tải danh sách 20 kịch bản mẫu
    fetchScenarios()
      .then((data) => {
        if (mounted) setScenarios(data);
      })
      .catch(() => {});

    return () => {
      mounted = false;
      signalRService.disconnect();
    };
  }, []);

  // Chạy kịch bản mẫu: Tham gia SignalR Group trước khi gửi request
  const handleSelectAndRun = async (folderName: string) => {
    try {
      setSelectedScenario(folderName);
      const clientSessionId = crypto.randomUUID();

      // Đảm bảo client tham gia group trước để không bỏ lỡ sự kiện đầu tiên
      await signalRService.joinSession(clientSessionId);

      setProgress({
        stage: 'Queueing',
        percent: 5,
        message: `Đang đưa kịch bản ${folderName} vào hàng đợi xử lý...`,
        isProcessing: true
      });

      await runScenarioAsync(folderName, clientSessionId);
    } catch (err: any) {
      setProgress({
        stage: 'Failed',
        percent: 0,
        message: err.message || 'Lỗi khi kích hoạt đối soát kịch bản.',
        isProcessing: false,
        error: err.message
      });
    }
  };

  // Tải lên 3 tệp chứng từ thực tế
  const handleStartUpload = async (po: File, grn: File, inv: File) => {
    try {
      const clientSessionId = crypto.randomUUID();

      // Đảm bảo tham gia group trước
      await signalRService.joinSession(clientSessionId);

      setProgress({
        stage: 'Uploading',
        percent: 5,
        message: 'Đang tải lên 3 tệp chứng từ và xếp hàng xử lý...',
        isProcessing: true
      });

      await uploadAndAuditAsync(po, grn, inv, clientSessionId);
    } catch (err: any) {
      setProgress({
        stage: 'Failed',
        percent: 0,
        message: err.message || 'Lỗi khi tải lên chứng từ đối soát.',
        isProcessing: false,
        error: err.message
      });
    }
  };

  // Mở popup email khiếu nại nhà cung cấp
  const handleOpenEmailModal = async () => {
    if (!currentSession) return;
    setEmailModalOpen(true);
    setIsEmailLoading(true);
    try {
      const data = await generateDisputeEmail(currentSession.id);
      setEmailData(data);
    } catch {
      setEmailData(null);
    } finally {
      setIsEmailLoading(false);
    }
  };

  return (
    <div className="min-h-screen flex flex-col">
      <Navbar
        activeTab={activeTab}
        setActiveTab={setActiveTab}
        signalRConnected={signalRConnected}
      />

      <main className="flex-1 max-w-7xl w-full mx-auto px-4 sm:px-6 lg:px-8 py-6">
        {/* Thanh chuyển đổi chế độ */}
        {activeTab === 'scenarios' ? (
          <ScenarioSelector
            scenarios={scenarios}
            selectedScenario={selectedScenario}
            onSelectAndRun={handleSelectAndRun}
            isLoading={progress.isProcessing}
          />
        ) : (
          <FileUploadZone
            onStartUpload={handleStartUpload}
            isLoading={progress.isProcessing}
          />
        )}

        {/* Thanh tiến trình SignalR thời gian thực */}
        <SignalRProgressBar progress={progress} />

        {/* Dashboard kết quả đối soát */}
        {currentSession ? (
          <div className="space-y-4 animate-in fade-in duration-300">
            <AuditSummaryCards session={currentSession} />

            {/* Bảng so khớp 3 chiều */}
            <ThreeWayMatchingTable rows={currentSession.comparisonRows || []} />

            {/* Chi tiết sai phạm & Cảnh báo */}
            <DiscrepancyList
              discrepancies={currentSession.discrepancies || []}
              onOpenEmailModal={handleOpenEmailModal}
            />
          </div>
        ) : !progress.isProcessing ? (
          <div className="my-12 p-12 text-center rounded-3xl border border-slate-800/80 bg-slate-900/20 backdrop-blur-sm">
            <div className="w-16 h-16 mx-auto mb-4 rounded-2xl bg-slate-900 border border-slate-800 flex items-center justify-center text-cyan-400">
              <ShieldCheck className="w-8 h-8" />
            </div>
            <h3 className="text-base font-bold text-white mb-1">Chưa có kết quả đối soát nào</h3>
            <p className="text-xs text-slate-400 max-w-md mx-auto">
              Vui lòng chọn 1 kịch bản mẫu từ danh sách phía trên hoặc kéo thả 3 tệp chứng từ (PO, Phiếu kho, Hóa đơn) để bắt đầu phân tích AI.
            </p>
          </div>
        ) : null}
      </main>

      {/* Footer */}
      <footer className="border-t border-slate-800/80 py-4 text-center text-xs text-slate-500">
        <p>AuditGuard-AI © 2026 | Giải pháp AI Agent Đối soát Tài chính 3 Chiều Doanh Nghiệp (100% Local On-Premise)</p>
      </footer>

      {/* Modal Email Khiếu nại */}
      <DisputeEmailModal
        isOpen={emailModalOpen}
        onClose={() => setEmailModalOpen(false)}
        emailData={emailData}
        isLoading={isEmailLoading}
      />
    </div>
  );
}
