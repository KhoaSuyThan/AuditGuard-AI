import React from 'react';
import { AuditSessionDto } from '../types/audit';
import { AlertTriangle, CheckCircle, XCircle, DollarSign, ListFilter, Hash } from 'lucide-react';

interface AuditSummaryCardsProps {
  session: AuditSessionDto;
}

export const AuditSummaryCards: React.FC<AuditSummaryCardsProps> = ({ session }) => {
  const isApproved = session.status === 'APPROVED' || session.status === 'PASSED';
  const isRejected = session.status === 'REJECTED';
  const isFlagged = session.status === 'FLAGGED' || session.status === 'FLAGGED_FOR_REVIEW';

  const formatVnd = (num: number) => {
    return new Intl.NumberFormat('vi-VN', { style: 'currency', currency: 'VND' }).format(num);
  };

  return (
    <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4 my-4">
      {/* 1. Trạng thái kết luận */}
      <div className={`p-4 rounded-2xl border backdrop-blur-sm ${
        isApproved
          ? 'bg-emerald-950/20 border-emerald-500/30'
          : isRejected
          ? 'bg-rose-950/20 border-rose-500/30'
          : 'bg-amber-950/20 border-amber-500/30'
      }`}>
        <div className="flex items-center justify-between">
          <span className="text-xs font-semibold text-slate-400">Kết luận Đối soát</span>
          {isApproved ? (
            <CheckCircle className="w-5 h-5 text-emerald-400" />
          ) : isRejected ? (
            <XCircle className="w-5 h-5 text-rose-400" />
          ) : (
            <AlertTriangle className="w-5 h-5 text-amber-400" />
          )}
        </div>
        <div className="mt-2">
          <span className={`text-xl font-extrabold ${
            isApproved ? 'text-emerald-400' : isRejected ? 'text-rose-400' : 'text-amber-400'
          }`}>
            {isApproved ? 'HỢP LỆ (APPROVED)' : isRejected ? 'TỪ CHỐI (REJECTED)' : 'CẦN RÀ SOÁT (FLAGGED)'}
          </span>
          <p className="text-[11px] text-slate-400 mt-1 font-mono">{session.sessionCode}</p>
        </div>
      </div>

      {/* 2. Tổng thiệt hại / Rủi ro tài chính */}
      <div className="p-4 rounded-2xl border border-slate-800/80 bg-slate-900/40 backdrop-blur-sm">
        <div className="flex items-center justify-between">
          <span className="text-xs font-semibold text-slate-400">Rủi ro Tài chính Phát hiện</span>
          <DollarSign className="w-5 h-5 text-rose-400" />
        </div>
        <div className="mt-2">
          <span className="text-xl font-extrabold text-white">
            {formatVnd(session.totalFinancialDiff)}
          </span>
          <p className="text-[11px] text-slate-400 mt-1">Tổng tiền chênh lệch cần khiếu nại</p>
        </div>
      </div>

      {/* 3. Số lượng sai lệch */}
      <div className="p-4 rounded-2xl border border-slate-800/80 bg-slate-900/40 backdrop-blur-sm">
        <div className="flex items-center justify-between">
          <span className="text-xs font-semibold text-slate-400">Số Sai lệch Phát hiện</span>
          <ListFilter className="w-5 h-5 text-cyan-400" />
        </div>
        <div className="mt-2">
          <span className="text-xl font-extrabold text-cyan-300">
            {session.discrepancyCount} cảnh báo
          </span>
          <p className="text-[11px] text-slate-400 mt-1">Giao thiếu / Kê giá / Thuế / Trùng lặp</p>
        </div>
      </div>

      {/* 4. Thông tin chứng từ đối soát */}
      <div className="p-4 rounded-2xl border border-slate-800/80 bg-slate-900/40 backdrop-blur-sm">
        <div className="flex items-center justify-between">
          <span className="text-xs font-semibold text-slate-400">Hồ sơ Nhà Cung Cấp</span>
          <Hash className="w-5 h-5 text-slate-400" />
        </div>
        <div className="mt-2">
          <span className="text-sm font-bold text-slate-200 line-clamp-1">
            {session.vendorName || 'Nhà Cung Cấp Doanh Nghiệp'}
          </span>
          <p className="text-[11px] text-slate-400 mt-1 font-mono">HĐ: {session.invoiceNumber || 'Chưa định danh'}</p>
        </div>
      </div>
    </div>
  );
};
