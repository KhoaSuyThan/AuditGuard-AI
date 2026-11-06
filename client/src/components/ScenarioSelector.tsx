import React from 'react';
import { ScenarioInfoDto } from '../types/audit';
import { FileText, Play, AlertTriangle, CheckCircle, Tag } from 'lucide-react';

interface ScenarioSelectorProps {
  scenarios: ScenarioInfoDto[];
  selectedScenario: string | null;
  onSelectAndRun: (folderName: string) => void;
  isLoading: boolean;
}

export const ScenarioSelector: React.FC<ScenarioSelectorProps> = ({
  scenarios,
  selectedScenario,
  onSelectAndRun,
  isLoading
}) => {
  return (
    <div className="bg-slate-900/40 border border-slate-800/80 rounded-2xl p-5 backdrop-blur-sm">
      <div className="flex items-center justify-between mb-4">
        <div>
          <h2 className="text-base font-bold text-white flex items-center gap-2">
            <FileText className="w-4 h-4 text-cyan-400" />
            20 Kịch bản Kiểm toán & Đối soát Mẫu
          </h2>
          <p className="text-xs text-slate-400 mt-0.5">
            Chọn 1 kịch bản để AI Agent tự động nạp chứng từ và thực thi đối soát 3 chiều
          </p>
        </div>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-3 max-h-[380px] overflow-y-auto pr-1">
        {scenarios.map((s) => {
          const isSelected = selectedScenario === s.folderName;
          const isPerfect = s.folderName.includes('perfect');
          const isDuplicate = s.folderName.includes('duplicate');
          const isTax = s.folderName.includes('tax');

          return (
            <div
              key={s.folderName}
              className={`p-3.5 rounded-xl border transition-all duration-200 flex flex-col justify-between ${
                isSelected
                  ? 'border-cyan-500/80 bg-cyan-950/20 shadow-lg shadow-cyan-500/10 ring-1 ring-cyan-500/30'
                  : 'border-slate-800/80 bg-slate-950/40 hover:border-slate-700 hover:bg-slate-900/50'
              }`}
            >
              <div>
                <div className="flex items-start justify-between gap-1 mb-1.5">
                  <span className={`text-[10px] font-bold px-2 py-0.5 rounded-md border flex items-center gap-1 ${
                    isPerfect 
                      ? 'bg-emerald-500/10 border-emerald-500/20 text-emerald-400'
                      : isDuplicate
                      ? 'bg-rose-500/10 border-rose-500/20 text-rose-400'
                      : 'bg-amber-500/10 border-amber-500/20 text-amber-400'
                  }`}>
                    {isPerfect ? <CheckCircle className="w-2.5 h-2.5" /> : <AlertTriangle className="w-2.5 h-2.5" />}
                    {isPerfect ? 'HỢP LỆ' : isDuplicate ? 'GIAN LẬN' : 'SAI LỆCH'}
                  </span>
                  <span className="text-[10px] font-mono text-slate-500">{s.folderName}</span>
                </div>

                <h3 className="text-xs font-bold text-slate-100 line-clamp-1 mb-1">{s.name}</h3>
                <p className="text-[11px] text-slate-400 line-clamp-2 leading-relaxed">{s.description}</p>
              </div>

              <div className="mt-3 pt-2.5 border-t border-slate-800/60 flex items-center justify-between">
                <span className="text-[10px] text-slate-500 flex items-center gap-1">
                  <Tag className="w-3 h-3 text-cyan-500" /> 3-Way Match
                </span>
                <button
                  onClick={() => onSelectAndRun(s.folderName)}
                  disabled={isLoading}
                  className="px-2.5 py-1 rounded-lg bg-cyan-500/10 hover:bg-cyan-500/20 text-cyan-400 border border-cyan-500/30 text-xs font-semibold flex items-center gap-1 transition-all duration-150 disabled:opacity-50"
                >
                  <Play className="w-3 h-3 fill-current" /> Chạy
                </button>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};
