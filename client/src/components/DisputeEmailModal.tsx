import React, { useState } from 'react';
import { DisputeEmailDto } from '../types/audit';
import { X, Copy, Check, MailCheck } from 'lucide-react';

interface DisputeEmailModalProps {
  isOpen: boolean;
  onClose: () => void;
  emailData: DisputeEmailDto | null;
  isLoading: boolean;
}

export const DisputeEmailModal: React.FC<DisputeEmailModalProps> = ({
  isOpen,
  onClose,
  emailData,
  isLoading
}) => {
  const [copied, setCopied] = useState(false);

  if (!isOpen) return null;

  const handleCopy = () => {
    if (emailData) {
      navigator.clipboard.writeText(emailData.bodyPlainText);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/70 backdrop-blur-sm animate-in fade-in duration-200">
      <div className="bg-slate-900 border border-slate-800 rounded-2xl w-full max-w-2xl max-h-[85vh] flex flex-col shadow-2xl">
        <div className="p-4 border-b border-slate-800 flex items-center justify-between">
          <div className="flex items-center gap-2">
            <MailCheck className="w-5 h-5 text-cyan-400" />
            <h3 className="text-sm font-bold text-white">Thư Khiếu Nại Nhà Cung Cấp Tự Động</h3>
          </div>
          <button onClick={onClose} className="p-1 rounded-lg hover:bg-slate-800 text-slate-400 hover:text-white">
            <X className="w-5 h-5" />
          </button>
        </div>

        <div className="p-5 overflow-y-auto space-y-4">
          {isLoading ? (
            <div className="py-12 text-center text-slate-400 text-xs">Đang soạn thảo thư khiếu nại qua AI...</div>
          ) : emailData ? (
            <>
              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1">Tiêu đề Thư (Subject):</label>
                <div className="p-2.5 rounded-lg bg-slate-950 border border-slate-800 text-xs font-medium text-slate-200">
                  {emailData.subject}
                </div>
              </div>

              <div>
                <label className="text-xs font-semibold text-slate-400 block mb-1">Nội dung Thư (Body):</label>
                <div 
                  className="p-3.5 rounded-lg bg-slate-950 border border-slate-800 text-xs text-slate-300 font-sans leading-relaxed whitespace-pre-wrap max-h-[320px] overflow-y-auto"
                >
                  {emailData.bodyPlainText}
                </div>
              </div>
            </>
          ) : (
            <div className="py-12 text-center text-slate-400 text-xs">Không thể tạo nội dung email.</div>
          )}
        </div>

        <div className="p-4 border-t border-slate-800 flex items-center justify-end gap-2">
          <button
            onClick={onClose}
            className="px-4 py-2 rounded-xl bg-slate-800 hover:bg-slate-700 text-slate-300 text-xs font-semibold"
          >
            Đóng
          </button>
          <button
            onClick={handleCopy}
            disabled={!emailData}
            className="px-4 py-2 rounded-xl bg-cyan-500 hover:bg-cyan-400 text-slate-950 text-xs font-bold flex items-center gap-1.5 shadow-lg shadow-cyan-500/20"
          >
            {copied ? <Check className="w-4 h-4 text-emerald-950" /> : <Copy className="w-4 h-4" />}
            {copied ? 'Đã sao chép!' : 'Sao chép Nội dung'}
          </button>
        </div>
      </div>
    </div>
  );
};
