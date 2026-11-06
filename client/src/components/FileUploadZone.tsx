import React, { useState } from 'react';
import { UploadCloud, File, AlertCircle, Sparkles } from 'lucide-react';

interface FileUploadZoneProps {
  onStartUpload: (po: File, grn: File, inv: File) => void;
  isLoading: boolean;
}

export const FileUploadZone: React.FC<FileUploadZoneProps> = ({ onStartUpload, isLoading }) => {
  const [poFile, setPoFile] = useState<File | null>(null);
  const [grnFile, setGrnFile] = useState<File | null>(null);
  const [invFile, setInvFile] = useState<File | null>(null);

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>, type: 'po' | 'grn' | 'inv') => {
    if (e.target.files && e.target.files[0]) {
      const f = e.target.files[0];
      if (type === 'po') setPoFile(f);
      if (type === 'grn') setGrnFile(f);
      if (type === 'inv') setInvFile(f);
    }
  };

  const isReady = poFile && grnFile && invFile;

  return (
    <div className="bg-slate-900/40 border border-slate-800/80 rounded-2xl p-5 backdrop-blur-sm">
      <div className="mb-4">
        <h2 className="text-base font-bold text-white flex items-center gap-2">
          <UploadCloud className="w-5 h-5 text-cyan-400" />
          Tải lên 3 Tệp Chứng từ để Đối soát Tự động
        </h2>
        <p className="text-xs text-slate-400 mt-0.5">
          Hệ thống tiếp nhận tệp định dạng <span className="text-cyan-400 font-semibold">.PDF</span> hoặc <span className="text-cyan-400 font-semibold">.JSON</span> (tối đa 20MB/tệp).
        </p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
        {/* 1. Đơn đặt hàng PO */}
        <div className={`p-4 rounded-xl border border-dashed text-center transition-all ${
          poFile ? 'border-cyan-500/80 bg-cyan-950/20' : 'border-slate-800 hover:border-slate-700 bg-slate-950/40'
        }`}>
          <label className="cursor-pointer block">
            <input 
              type="file" 
              accept=".pdf,.json" 
              className="hidden" 
              onChange={(e) => handleFileChange(e, 'po')} 
              disabled={isLoading}
            />
            <div className="flex flex-col items-center">
              <File className={`w-8 h-8 mb-2 ${poFile ? 'text-cyan-400' : 'text-slate-500'}`} />
              <span className="text-xs font-bold text-slate-200">1. Đơn Đặt Hàng (PO)</span>
              {poFile ? (
                <span className="text-[11px] text-cyan-300 font-mono mt-1 line-clamp-1">{poFile.name}</span>
              ) : (
                <span className="text-[11px] text-slate-500 mt-1">Chọn tệp PO (.pdf / .json)</span>
              )}
            </div>
          </label>
        </div>

        {/* 2. Phiếu nhập kho GRN */}
        <div className={`p-4 rounded-xl border border-dashed text-center transition-all ${
          grnFile ? 'border-cyan-500/80 bg-cyan-950/20' : 'border-slate-800 hover:border-slate-700 bg-slate-950/40'
        }`}>
          <label className="cursor-pointer block">
            <input 
              type="file" 
              accept=".pdf,.json" 
              className="hidden" 
              onChange={(e) => handleFileChange(e, 'grn')} 
              disabled={isLoading}
            />
            <div className="flex flex-col items-center">
              <File className={`w-8 h-8 mb-2 ${grnFile ? 'text-cyan-400' : 'text-slate-500'}`} />
              <span className="text-xs font-bold text-slate-200">2. Phiếu Nhập Kho (GRN)</span>
              {grnFile ? (
                <span className="text-[11px] text-cyan-300 font-mono mt-1 line-clamp-1">{grnFile.name}</span>
              ) : (
                <span className="text-[11px] text-slate-500 mt-1">Chọn tệp Phiếu kho (.pdf / .json)</span>
              )}
            </div>
          </label>
        </div>

        {/* 3. Hóa đơn VAT */}
        <div className={`p-4 rounded-xl border border-dashed text-center transition-all ${
          invFile ? 'border-cyan-500/80 bg-cyan-950/20' : 'border-slate-800 hover:border-slate-700 bg-slate-950/40'
        }`}>
          <label className="cursor-pointer block">
            <input 
              type="file" 
              accept=".pdf,.json" 
              className="hidden" 
              onChange={(e) => handleFileChange(e, 'inv')} 
              disabled={isLoading}
            />
            <div className="flex flex-col items-center">
              <File className={`w-8 h-8 mb-2 ${invFile ? 'text-cyan-400' : 'text-slate-500'}`} />
              <span className="text-xs font-bold text-slate-200">3. Hóa Đơn VAT (Invoice)</span>
              {invFile ? (
                <span className="text-[11px] text-cyan-300 font-mono mt-1 line-clamp-1">{invFile.name}</span>
              ) : (
                <span className="text-[11px] text-slate-500 mt-1">Chọn tệp Hóa đơn (.pdf / .json)</span>
              )}
            </div>
          </label>
        </div>
      </div>

      <div className="mt-4 pt-3 border-t border-slate-800/80 flex items-center justify-between">
        <div className="flex items-center gap-1.5 text-xs text-amber-400/90">
          <AlertCircle className="w-4 h-4 shrink-0" />
          <span>Lưu ý: Tính năng OCR ảnh scan (JPG/PNG) đang được hoàn thiện chuyên sâu ở giai đoạn kế tiếp.</span>
        </div>

        <button
          onClick={() => {
            if (poFile && grnFile && invFile) onStartUpload(poFile, grnFile, invFile);
          }}
          disabled={!isReady || isLoading}
          className="px-5 py-2 rounded-xl bg-gradient-to-r from-cyan-500 to-blue-600 hover:from-cyan-400 hover:to-blue-500 text-white font-bold text-xs shadow-lg shadow-cyan-500/20 flex items-center gap-2 transition-all duration-200 disabled:opacity-40 disabled:cursor-not-allowed"
        >
          <Sparkles className="w-4 h-4" /> Bắt đầu Đối soát AI
        </button>
      </div>
    </div>
  );
};
