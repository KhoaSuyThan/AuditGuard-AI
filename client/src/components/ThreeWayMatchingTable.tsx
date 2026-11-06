import React from 'react';
import { ThreeWayRowDto } from '../types/audit';
import { Layers, AlertCircle, CheckCircle2 } from 'lucide-react';

interface ThreeWayMatchingTableProps {
  rows: ThreeWayRowDto[];
}

export const ThreeWayMatchingTable: React.FC<ThreeWayMatchingTableProps> = ({ rows }) => {
  const formatVnd = (num: number) => {
    return new Intl.NumberFormat('vi-VN').format(num) + ' đ';
  };

  if (!rows || rows.length === 0) {
    return (
      <div className="bg-slate-900/40 border border-slate-800 rounded-2xl p-6 text-center text-slate-400">
        Không có dữ liệu dòng hàng để hiển thị so khớp 3 chiều.
      </div>
    );
  }

  return (
    <div className="bg-slate-900/40 border border-slate-800/80 rounded-2xl p-5 backdrop-blur-sm my-4">
      <div className="flex items-center justify-between mb-4">
        <div>
          <h3 className="text-base font-bold text-white flex items-center gap-2">
            <Layers className="w-4 h-4 text-cyan-400" />
            Bảng So khớp 3 Chiều Dòng hàng (PO vs Kho GRN vs Hóa đơn VAT)
          </h3>
          <p className="text-xs text-slate-400 mt-0.5">
            AI Agent tự động mapping ngữ nghĩa sản phẩm và đối soát đa chiều số lượng & đơn giá
          </p>
        </div>
      </div>

      <div className="overflow-x-auto">
        <table className="w-full text-left text-xs border-collapse">
          <thead>
            <tr className="border-b border-slate-800 bg-slate-950/60 text-slate-400 font-semibold uppercase tracking-wider">
              <th className="py-3 px-3">Mặt hàng PO (Đơn đặt)</th>
              <th className="py-3 px-2">SL Đặt</th>
              <th className="py-3 px-3">SL Thực nhận (Kho)</th>
              <th className="py-3 px-3">Mặt hàng Hóa đơn VAT</th>
              <th className="py-3 px-2">SL HĐ</th>
              <th className="py-3 px-3">Đơn giá PO</th>
              <th className="py-3 px-3">Đơn giá HĐ</th>
              <th className="py-3 px-2 text-center">AI Match</th>
              <th className="py-3 px-3 text-center">Trạng thái</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-800/60 font-mono">
            {rows.map((r, idx) => {
              const isMatched = r.matchStatus === 'MATCHED';
              const isQtyMismatch = r.poQuantity !== r.grnQuantityReceived || r.grnQuantityReceived !== r.invoiceQuantity;
              const isPriceMismatch = r.poUnitPrice !== r.invoiceUnitPrice;
              const isUnmatched = r.matchStatus === 'UNMATCHED';

              return (
                <tr key={idx} className="hover:bg-slate-800/30 transition-colors">
                  <td className="py-3 px-3 font-sans font-semibold text-slate-200">
                    {r.poItemName || <span className="text-slate-500 italic">Không có trong PO</span>}
                  </td>
                  <td className="py-3 px-2 text-slate-300">{r.poQuantity}</td>
                  <td className={`py-3 px-3 font-semibold ${isQtyMismatch ? 'text-amber-400' : 'text-emerald-400'}`}>
                    {r.grnQuantityReceived}
                  </td>
                  <td className="py-3 px-3 font-sans font-semibold text-slate-200">
                    {r.invoiceItemName || <span className="text-slate-500 italic">Thiếu trên Hóa đơn</span>}
                  </td>
                  <td className={`py-3 px-2 font-semibold ${r.invoiceQuantity > r.grnQuantityReceived ? 'text-rose-400' : 'text-slate-300'}`}>
                    {r.invoiceQuantity}
                  </td>
                  <td className="py-3 px-3 text-slate-300">{formatVnd(r.poUnitPrice)}</td>
                  <td className={`py-3 px-3 font-semibold ${isPriceMismatch ? 'text-rose-400 font-bold' : 'text-slate-300'}`}>
                    {formatVnd(r.invoiceUnitPrice)}
                  </td>
                  <td className="py-3 px-2 text-center">
                    <span className="text-[11px] px-2 py-0.5 rounded-full bg-cyan-950/40 text-cyan-400 border border-cyan-800/40">
                      {(r.similarityScore * 100).toFixed(0)}%
                    </span>
                  </td>
                  <td className="py-3 px-3 text-center font-sans">
                    {isMatched ? (
                      <span className="inline-flex items-center gap-1 text-[11px] font-bold px-2 py-0.5 rounded-md bg-emerald-500/10 text-emerald-400 border border-emerald-500/20">
                        <CheckCircle2 className="w-3 h-3" /> KHỚP
                      </span>
                    ) : isUnmatched ? (
                      <span className="inline-flex items-center gap-1 text-[11px] font-bold px-2 py-0.5 rounded-md bg-rose-500/10 text-rose-400 border border-rose-500/20">
                        <AlertCircle className="w-3 h-3" /> NGOÀI PO
                      </span>
                    ) : (
                      <span className="inline-flex items-center gap-1 text-[11px] font-bold px-2 py-0.5 rounded-md bg-amber-500/10 text-amber-400 border border-amber-500/20">
                        <AlertCircle className="w-3 h-3" /> LỆCH
                      </span>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
};
