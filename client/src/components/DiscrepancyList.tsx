import React from 'react';
import { AuditDiscrepancyDto } from '../types/audit';
import { AlertCircle, AlertTriangle, Info, Mail } from 'lucide-react';

interface DiscrepancyListProps {
  discrepancies: AuditDiscrepancyDto[];
  onOpenEmailModal: () => void;
}

export const DiscrepancyList: React.FC<DiscrepancyListProps> = ({ discrepancies, onOpenEmailModal }) => {
  const formatVnd = (num: number) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(num);
  };

  return (
    <div className="bg-slate-900/40 border border-slate-800/80 rounded-2xl p-5 backdrop-blur-sm my-4">
      <div className="flex items-center justify-between mb-4">
        <div>
          <h3 className="text-base font-bold text-white flex items-center gap-2">
            <AlertCircle className="w-4 h-4 text-rose-400" />
            Chi tiết {discrepancies.length} Sai lệch & Cảnh báo Rủi ro Kế toán
          </h3>
          <p className="text-xs text-slate-400 mt-0.5">
            Bằng chứng kiểm toán cụ thể để kế toán đối chất và xử lý với nhà cung cấp
          </p>
        </div>

        {discrepancies.length > 0 && (
          <button
            onClick={onOpenEmailModal}
            className="px-3.5 py-1.5 rounded-xl bg-rose-500/10 hover:bg-rose-500/20 text-rose-400 border border-rose-500/30 text-xs font-bold flex items-center gap-1.5 transition-all"
          >
            <Mail className="w-3.5 h-3.5" /> Tạo Thư Khiếu Nại (1-Click)
          </button>
        )}
      </div>

      {discrepancies.length === 0 ? (
        <div className="p-4 rounded-xl bg-emerald-950/10 border border-emerald-500/20 text-center text-xs text-emerald-400">
          Tuyệt vời! Không phát hiện bất kỳ sai lệch nào giữa Hóa đơn, Đơn đặt hàng và Phiếu nhập kho.
        </div>
      ) : (
        <div className="space-y-2.5">
          {discrepancies.map((d, i) => {
            const isCritical = d.severity === 'Critical';
            const isWarning = d.severity === 'Warning';

            return (
              <div
                key={i}
                className={`p-3.5 rounded-xl border flex items-start justify-between gap-4 ${
                  isCritical
                    ? 'bg-rose-950/20 border-rose-500/30'
                    : isWarning
                    ? 'bg-amber-950/20 border-amber-500/30'
                    : 'bg-slate-900 border-slate-800'
                }`}
              >
                <div className="flex items-start gap-3">
                  <div className="mt-0.5">
                    {isCritical ? (
                      <AlertCircle className="w-4 h-4 text-rose-400" />
                    ) : isWarning ? (
                      <AlertTriangle className="w-4 h-4 text-amber-400" />
                    ) : (
                      <Info className="w-4 h-4 text-cyan-400" />
                    )}
                  </div>
                  <div>
                    <div className="flex items-center gap-2">
                      <span className="text-xs font-bold text-slate-100">{d.discrepancyType}</span>
                      {d.itemName && (
                        <span className="text-[11px] font-mono px-2 py-0.5 rounded bg-slate-800 text-slate-300">
                          {d.itemName}
                        </span>
                      )}
                    </div>
                    <p className="text-xs text-slate-300 mt-1 leading-relaxed">{d.description}</p>
                    {(d.expectedValue || d.actualValue) && (
                      <div className="flex items-center gap-4 text-[11px] text-slate-400 mt-2 font-mono">
                        {d.expectedValue && <span>Kỳ vọng: <strong className="text-emerald-400">{d.expectedValue}</strong></span>}
                        {d.actualValue && <span>Thực tế: <strong className="text-rose-400">{d.actualValue}</strong></span>}
                      </div>
                    )}
                  </div>
                </div>

                <div className="text-right shrink-0">
                  <span className="text-xs font-extrabold text-rose-400 block font-mono">
                    +{formatVnd(d.financialImpact)}
                  </span>
                  <span className="text-[10px] text-slate-500 uppercase tracking-wider font-semibold">Chênh lệch</span>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
};
